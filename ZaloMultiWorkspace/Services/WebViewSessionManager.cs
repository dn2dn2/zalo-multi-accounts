using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Services
{
    public class WebViewSessionManager
    {
        private readonly Dictionary<Guid, WebView2> _activeWebViews = new();
        private readonly ProxyHealthService _proxyHealthService;
        private readonly JsBridgeService _jsBridgeService;
        private Grid? _hostGrid;
        private readonly Dictionary<Guid, DateTime> _lastNotifyAt = new();
        private const int GenericNotifyDelayMs = 700;
        private const double NotifyDedupSeconds = 3.0;

        public event Action<ZaloAccountProfile, int>? OnUnreadCountChanged;
        public event Action<ZaloAccountProfile, string, string>? OnNewMessageNotification;
        public event Action<ZaloAccountProfile, string>? OnShortcutTriggered;
        public event Action<ZaloAccountProfile, string>? OnActiveChatChanged;
        public event Action<ZaloAccountProfile>? OnAvatarUpdated;
        public event Action? OnSessionStateChanged;

        public WebViewSessionManager(ProxyHealthService proxyHealthService, JsBridgeService jsBridgeService)
        {
            _proxyHealthService = proxyHealthService;
            _jsBridgeService = jsBridgeService;
        }

        public void AttachHostGrid(Grid hostGrid)
        {
            _hostGrid = hostGrid;
        }

        public static string GetProfileFolderPath(Guid accountId)
        {
            return Path.Combine(DatabaseService.BaseProfileDir, $"acc_{accountId}");
        }

        /// <summary>
        /// Phần 5: Dọn dẹp các tiến trình msedgewebview2.exe mồ côi từ phiên trước đang giữ khóa thư mục Profiles.
        /// Chỉ đóng tiến trình trình duyệt có --user-data-dir nằm trong thư mục Profiles của ứng dụng
        /// và có tiến trình cha đã thoát (ứng dụng chủ bị crash / bị kill).
        /// </summary>
        public static int CleanupOrphanProcesses()
        {
            int killed = 0;
            try
            {
                string profileBase = Path.GetFullPath(DatabaseService.BaseProfileDir).TrimEnd('\\');
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");
                using var results = searcher.Get();
                foreach (System.Management.ManagementObject mo in results)
                {
                    using (mo)
                    {
                        string? cmd = mo["CommandLine"] as string;
                        if (string.IsNullOrEmpty(cmd) ||
                            cmd.IndexOf(profileBase, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        // Chỉ đóng tiến trình trình duyệt gốc (không có --type=); tiến trình con tự thoát theo
                        if (cmd.Contains("--type=", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Chỉ đóng khi tiến trình cha (ứng dụng chủ) không còn chạy -> thực sự mồ côi.
                        // Tránh đóng nhầm phiên của một instance khác (vd: bản cũ chưa có khóa 1 instance).
                        int parentPid = Convert.ToInt32(mo["ParentProcessId"]);
                        if (IsProcessAlive(parentPid))
                            continue;

                        try
                        {
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            using var proc = Process.GetProcessById(pid);
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(3000);
                            killed++;
                        }
                        catch
                        {
                            // Tiến trình đã thoát hoặc không đủ quyền
                        }
                    }
                }
            }
            catch
            {
                // WMI không khả dụng - bỏ qua
            }
            return killed;
        }

        private static bool IsProcessAlive(int pid)
        {
            if (pid <= 0)
                return false;
            try
            {
                using var proc = Process.GetProcessById(pid);
                return !proc.HasExited;
            }
            catch
            {
                return false;
            }
        }

        public bool IsSessionRunning(Guid accountId)
        {
            return _activeWebViews.ContainsKey(accountId);
        }

        public WebView2? GetWebView(Guid accountId)
        {
            return _activeWebViews.TryGetValue(accountId, out var wv) ? wv : null;
        }

        public int RunningCount => _activeWebViews.Count;

        public async Task<bool> StartOrWakeSessionAsync(ZaloAccountProfile profile, bool makeVisible = true)
        {
            if (_hostGrid == null)
                return false;

            profile.LastInteractionTime = DateTime.Now;

            if (_activeWebViews.TryGetValue(profile.Id, out var existingWebView))
            {
                profile.IsHibernated = false;
                profile.ConnectionStatus = AccountConnectionStatus.Online;
                if (makeVisible)
                    SwitchVisibleTab(profile.Id);
                OnSessionStateChanged?.Invoke();
                return true;
            }

            if (profile.IsUnsupportedSocks5Auth)
            {
                profile.ConnectionStatus = AccountConnectionStatus.ProxyError;
                profile.IsHibernated = false;
                profile.StatusMessage = "WebView2 không hỗ trợ Proxy SOCKS5 có User/Pass. Hãy dùng Proxy HTTP, " +
                                        "hoặc SOCKS5 xác thực bằng IP (whitelist) và xóa User/Pass.";
                if (makeVisible)
                    HideAllWebViews();
                OnSessionStateChanged?.Invoke();
                return false;
            }

            profile.ConnectionStatus = AccountConnectionStatus.Connecting;
            profile.StatusMessage = "Đang kiểm tra kết nối mạng / Proxy...";
            OnSessionStateChanged?.Invoke();

            // Giai đoạn 2: Kiểm tra Proxy trước khi mở WebView2
            var health = await _proxyHealthService.CheckProxyAsync(profile);
            profile.PingMs = health.PingMs;
            profile.IsPingFailed = !health.IsHealthy;

            if (!health.IsHealthy && profile.HasProxy)
            {
                profile.ConnectionStatus = AccountConnectionStatus.ProxyError;
                profile.IsHibernated = false;
                profile.StatusMessage = health.ErrorMessage;
                if (makeVisible)
                    HideAllWebViews();
                OnSessionStateChanged?.Invoke();
                return false;
            }

            try
            {
                string userDataFolder = GetProfileFolderPath(profile.Id);
                Directory.CreateDirectory(userDataFolder);

                // Giai đoạn 2 & 3: Cấu hình tham số dòng lệnh Chromium, chống rò rỉ WebRTC IP & chống ngủ đông tab chạy ngầm
                string browserArgs = "--disable-features=WebRtcHideLocalIpsWithMdns " +
                                     "--force-webrtc-ip-handling-policy=default_public_interface_only " +
                                     "--disable-background-timer-throttling " +
                                     "--disable-backgrounding-occluded-windows " +
                                     "--disable-renderer-backgrounding";

                if (profile.HasProxy)
                {
                    string host = profile.ProxyHost;
                    int port = profile.ProxyPort;
                    if (string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(profile.ProxyAddress))
                    {
                        var parts = profile.ProxyAddress.Split(':', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int p))
                        {
                            host = parts[0];
                            port = p;
                        }
                    }
                    string proxyType = string.IsNullOrWhiteSpace(profile.ProxyType) ? "http" : profile.ProxyType.ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(host) && port > 0)
                    {
                        browserArgs = $"--proxy-server={proxyType}://{host}:{port} " + browserArgs;
                    }
                }

                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = browserArgs
                };

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);

                var webView = new WebView2
                {
                    Tag = profile.Id,
                    Visibility = makeVisible ? Visibility.Visible : Visibility.Collapsed,
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 24, 24, 27)
                };

                _hostGrid.Children.Add(webView);
                _activeWebViews[profile.Id] = webView;

                await webView.EnsureCoreWebView2Async(env);

                // Giai đoạn 1: Tắt các tính năng trình duyệt dư thừa
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Giai đoạn 2: Tự động xác thực Proxy có mật khẩu (User:Pass)
                if (!string.IsNullOrWhiteSpace(profile.ProxyUser))
                {
                    webView.CoreWebView2.BasicAuthenticationRequested += (sender, args) =>
                    {
                        args.Response.UserName = profile.ProxyUser;
                        args.Response.Password = profile.ProxyPass ?? string.Empty;
                    };
                }

                // Tự động cấp quyền Notification cho Zalo Web để nhận sự kiện tin nhắn mới khi ở tab khác
                webView.CoreWebView2.PermissionRequested += (sender, args) =>
                {
                    if (args.PermissionKind == CoreWebView2PermissionKind.Notifications)
                    {
                        args.State = CoreWebView2PermissionState.Allow;
                    }
                };

                // Bắt sự kiện thay đổi tiêu đề trực tiếp từ trình duyệt (ổn định ngay cả khi tab bị ẩn)
                webView.CoreWebView2.DocumentTitleChanged += (sender, args) =>
                {
                    try
                    {
                        string title = webView.CoreWebView2.DocumentTitle ?? string.Empty;
                        var match = Regex.Match(title, @"\((\d+)\)");
                        int titleCount = (match.Success && int.TryParse(match.Groups[1].Value, out int parsed)) ? parsed : 0;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            int oldCount = profile.UnreadCount;
                            // Không tự xóa badge chưa đọc khi tài khoản đang ở nền (chưa được người dùng mở xem)
                            if (!profile.IsSelected && titleCount < oldCount)
                                return;

                            if (titleCount != oldCount)
                            {
                                profile.UnreadCount = titleCount;
                                OnUnreadCountChanged?.Invoke(profile, titleCount);
                                if (titleCount > oldCount)
                                {
                                    RaiseGenericNewMessage(profile, profile.DisplayName,
                                        $"Có {titleCount} tin nhắn chưa đọc trên Zalo");
                                }
                            }
                        });
                    }
                    catch { }
                };

                // Giai đoạn 3: Tiêm kịch bản theo dõi tự động (MutationObserver đọc <title>, Avatar, Shortcut)
                string observerScript = _jsBridgeService.BuildStartupObserverScript();
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(observerScript);

                webView.CoreWebView2.WebMessageReceived += (sender, args) =>
                {
                    HandleWebMessage(profile, args.WebMessageAsJson);
                };

                webView.CoreWebView2.Navigate("https://chat.zalo.me");

                profile.IsHibernated = false;
                profile.ConnectionStatus = AccountConnectionStatus.Online;
                profile.StatusMessage = string.Empty;

                if (makeVisible)
                    SwitchVisibleTab(profile.Id);

                OnSessionStateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                profile.ConnectionStatus = AccountConnectionStatus.ProxyError;
                profile.StatusMessage = $"Lỗi khởi tạo WebView2: {ex.Message}";
                OnSessionStateChanged?.Invoke();
                return false;
            }
        }

        private void HandleWebMessage(ZaloAccountProfile profile, string jsonMessage)
        {
            if (string.IsNullOrWhiteSpace(jsonMessage))
                return;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var msg = JsonSerializer.Deserialize<JsBridgeMessage>(jsonMessage, options);
                if (msg == null || string.IsNullOrEmpty(msg.Type))
                    return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    switch (msg.Type)
                    {
                        case "UNREAD_UPDATE":
                            int oldCount = profile.UnreadCount;
                            // Khi tài khoản đang ở nền (người dùng đang ở tab khác), giữ nguyên badge thông báo trên icon,
                            // không cho phép giảm về 0 cho tới khi người dùng chuyển sang tài khoản đó
                            if (!profile.IsSelected && msg.Count < oldCount)
                            {
                                break;
                            }

                            profile.UnreadCount = msg.Count;
                            OnUnreadCountChanged?.Invoke(profile, msg.Count);
                            if (msg.Count > oldCount)
                            {
                                string senderName = !string.IsNullOrWhiteSpace(msg.Sender) ? msg.Sender : profile.DisplayName;
                                string bodyText = !string.IsNullOrWhiteSpace(msg.Body)
                                    ? msg.Body
                                    : $"Có {msg.Count} tin nhắn chưa đọc";
                                RaiseGenericNewMessage(profile, senderName, bodyText);
                            }
                            else
                            {
                                OnSessionStateChanged?.Invoke();
                            }
                            break;

                        case "NEW_MESSAGE_NOTIFY":
                            if (!profile.IsSelected)
                            {
                                profile.UnreadCount = profile.UnreadCount + 1;
                                OnUnreadCountChanged?.Invoke(profile, profile.UnreadCount);
                            }
                            else if (profile.UnreadCount == 0)
                            {
                                profile.UnreadCount = 1;
                                OnUnreadCountChanged?.Invoke(profile, 1);
                            }
                            _lastNotifyAt[profile.Id] = DateTime.Now;
                            OnNewMessageNotification?.Invoke(
                                profile,
                                !string.IsNullOrWhiteSpace(msg.Sender) ? msg.Sender : profile.DisplayName,
                                !string.IsNullOrWhiteSpace(msg.Body) ? msg.Body : "Bạn có tin nhắn Zalo mới");
                            break;

                        case "AVATAR_UPDATE":
                            if (!string.IsNullOrWhiteSpace(msg.AvatarUrl))
                            {
                                _ = UpdateProfileAvatarAsync(profile, msg.AvatarUrl);
                            }
                            break;

                        case "USER_ACTIVITY":
                            profile.LastInteractionTime = DateTime.Now;
                            break;

                        case "SHORTCUT_TRIGGER":
                            if (!string.IsNullOrWhiteSpace(msg.Shortcut))
                            {
                                OnShortcutTriggered?.Invoke(profile, msg.Shortcut);
                            }
                            break;

                        case "ACTIVE_CHAT":
                            if (!string.IsNullOrWhiteSpace(msg.ActiveChatName))
                            {
                                OnActiveChatChanged?.Invoke(profile, msg.ActiveChatName);
                            }
                            break;
                    }
                });
            }
            catch
            {
                // Bỏ qua các bản tin không đúng định dạng
            }
        }

        /// <summary>
        /// Thông báo "chung" (từ tiêu đề / bộ đếm) được trì hoãn ngắn và bỏ qua nếu quanh thời điểm đó
        /// đã có thông báo chi tiết từ window.Notification của Zalo (tránh 2-3 thông báo cho cùng 1 tin).
        /// </summary>
        private async void RaiseGenericNewMessage(ZaloAccountProfile profile, string sender, string body)
        {
            DateTime requestedAt = DateTime.Now;
            await Task.Delay(GenericNotifyDelayMs);
            if (_lastNotifyAt.TryGetValue(profile.Id, out var last) &&
                last >= requestedAt.AddSeconds(-NotifyDedupSeconds))
                return;

            _lastNotifyAt[profile.Id] = DateTime.Now;
            OnNewMessageNotification?.Invoke(profile, sender, body);
        }

        private async Task UpdateProfileAvatarAsync(ZaloAccountProfile profile, string rawAvatarUrl)
        {
            try
            {
                if (rawAvatarUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    if (profile.AvatarUrl != rawAvatarUrl)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            profile.AvatarUrl = rawAvatarUrl;
                            OnAvatarUpdated?.Invoke(profile);
                        });
                    }
                    return;
                }

                if (rawAvatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (profile.AvatarRemoteUrl == rawAvatarUrl && profile.HasAvatar)
                        return;

                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/png,image/jpeg,*/*;q=0.8");
                    byte[] bytes = await client.GetByteArrayAsync(rawAvatarUrl);
                    if (bytes.Length > 0)
                    {
                        string dataUri = "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            profile.AvatarRemoteUrl = rawAvatarUrl;
                            profile.AvatarUrl = dataUri;
                            OnAvatarUpdated?.Invoke(profile);
                        });
                    }
                }
            }
            catch
            {
                // Giữ nguyên nếu tải ảnh thất bại
            }
        }

        /// <summary>
        /// Giai đoạn 1: Chuyển đổi Tab với độ trễ 0ms bằng Visibility.Visible / Visibility.Collapsed
        /// </summary>
        public void SwitchVisibleTab(Guid activeAccountId)
        {
            if (_hostGrid == null)
                return;

            foreach (UIElement child in _hostGrid.Children)
            {
                if (child is WebView2 wv && wv.Tag is Guid id)
                {
                    bool isTarget = (id == activeAccountId);
                    wv.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
                    if (isTarget && wv.CoreWebView2 != null)
                    {
                        _ = SyncUnreadAfterTabVisibleAsync(wv);
                    }
                }
            }
        }

        private async Task SyncUnreadAfterTabVisibleAsync(WebView2 webView)
        {
            try
            {
                await Task.Delay(800);
                if (webView.Visibility == Visibility.Visible && webView.CoreWebView2 != null)
                {
                    await webView.CoreWebView2.ExecuteScriptAsync("if (window.checkZaloUnreadNow) window.checkZaloUnreadNow();");
                }
            }
            catch { }
        }

        public void HideAllWebViews()
        {
            if (_hostGrid == null)
                return;

            foreach (UIElement child in _hostGrid.Children)
            {
                if (child is WebView2 wv)
                {
                    wv.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Giai đoạn 3: Cơ chế Ngủ đông (Tab Hibernation) giải phóng hoàn toàn tiến trình WebView2
        /// </summary>
        public void HibernateSession(ZaloAccountProfile profile)
        {
            if (_activeWebViews.TryGetValue(profile.Id, out var webView))
            {
                _activeWebViews.Remove(profile.Id);
                _hostGrid?.Children.Remove(webView);
                try
                {
                    webView.Dispose();
                }
                catch
                {
                    // Bỏ qua lỗi khi giải phóng
                }
            }

            profile.IsHibernated = true;
            profile.ConnectionStatus = AccountConnectionStatus.Hibernated;
            profile.RamUsageMb = 0;
            profile.IsPingFailed = false;
            profile.StatusMessage = "Tài khoản đang ở chế độ Ngủ đông để tiết kiệm RAM.";
            OnSessionStateChanged?.Invoke();
        }

        /// <summary>
        /// Ngủ đông toàn bộ và chờ tiến trình trình duyệt WebView2 thoát hẳn (giải phóng khóa file Profile).
        /// </summary>
        public async Task HibernateAllSessionsAndWaitAsync(IEnumerable<ZaloAccountProfile> profiles)
        {
            var targets = profiles.ToList();
            var pids = new List<int>();
            foreach (var p in targets)
            {
                if (!_activeWebViews.TryGetValue(p.Id, out var wv))
                    continue;
                try
                {
                    if (wv.CoreWebView2 != null)
                        pids.Add((int)wv.CoreWebView2.BrowserProcessId);
                }
                catch { }
            }

            HibernateAllSessions(targets);

            await Task.Run(() =>
            {
                foreach (int pid in pids)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        proc.WaitForExit(5000);
                    }
                    catch
                    {
                        // Tiến trình đã thoát
                    }
                }
            });
        }

        public void HibernateAllSessions(IEnumerable<ZaloAccountProfile> profiles)
        {
            foreach (var p in profiles)
            {
                HibernateSession(p);
            }
        }

        public void ReloadSession(ZaloAccountProfile profile)
        {
            if (_activeWebViews.TryGetValue(profile.Id, out var webView) && webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Reload();
            }
        }

        /// <summary>
        /// Giai đoạn 3: Đo RAM thực tế của từng tài khoản bằng cách cộng mọi tiến trình
        /// (browser, renderer, GPU, utility) thuộc môi trường WebView2 riêng của tài khoản đó.
        /// Không cộng tiến trình msedgewebview2 của ứng dụng khác (Teams, Widgets...).
        /// </summary>
        public double MeasureTotalRamUsageMb(IEnumerable<ZaloAccountProfile> profiles)
        {
            double totalMb = 0;
            var profileMap = profiles.ToDictionary(p => p.Id);

            foreach (var kvp in _activeWebViews.ToList())
            {
                double accountMb = 0;
                try
                {
                    var core = kvp.Value.CoreWebView2;
                    if (core != null)
                    {
                        var seen = new HashSet<int>();
                        foreach (var info in core.Environment.GetProcessInfos())
                        {
                            if (!seen.Add(info.ProcessId))
                                continue;
                            try
                            {
                                using var proc = Process.GetProcessById(info.ProcessId);
                                accountMb += proc.WorkingSet64 / (1024.0 * 1024.0);
                            }
                            catch { }
                        }
                    }
                }
                catch
                {
                    accountMb = 0;
                }

                accountMb = Math.Round(accountMb, 1);
                if (profileMap.TryGetValue(kvp.Key, out var profile))
                {
                    profile.RamUsageMb = accountMb;
                }
                totalMb += accountMb;
            }

            return Math.Round(totalMb, 0);
        }
    }
}
