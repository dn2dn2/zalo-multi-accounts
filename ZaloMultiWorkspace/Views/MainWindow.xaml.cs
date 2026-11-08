using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using ZaloMultiWorkspace.Models;
using ZaloMultiWorkspace.ViewModels;
using ZaloMultiWorkspace.Views.Dialogs;

namespace ZaloMultiWorkspace.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        /// <summary>Mở cùng Windows: chạy ngầm ở khay, không hiện cửa sổ</summary>
        public bool StartHiddenInTray { get; set; }
        private readonly Services.TrayBadgeService _trayBadge = new();
        public Services.GlobalHotkeyService GlobalHotkeys { get; } = new();
        private bool _capturing;
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            // Áp giao diện đã lưu (mặc định Theo Windows) trước khi cửa sổ hiển thị
            Services.ThemeService.Initialize(ViewModel.ThemeMode);

            // Gắn Grid chứa các thực thể WebView2
            ViewModel.SessionManager.AttachHostGrid(WebViewHostGrid);

            Loaded += async (s, e) =>
            {
                if (StartHiddenInTray)
                {
                    Hide();
                    Opacity = 1;
                }
                // Đồng bộ đường dẫn chạy cùng Windows (nếu đang bật) rồi tự khởi chạy các tài khoản
                Services.StartupService.Apply(ViewModel.DatabaseService.GetSettings().StartWithWindows);
                await ViewModel.StartAutoLaunchAccountsAsync();
            };
            SourceInitialized += (s, e) =>
            {
                if (StartHiddenInTray)
                    Opacity = 0; // tránh nháy cửa sổ trước khi ẩn xuống khay
                GlobalHotkeys.Attach(this);
                ApplyHotkeys(notifyConflicts: true);
            };
            GlobalHotkeys.HotkeyPressed += OnGlobalHotkey;

            // Icon khay: chấm đỏ khi có tin chưa đọc + tooltip theo từng tài khoản
            UpdateTrayIcon();
            UpdateTaskbarBadge();
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.TrayHasUnread) ||
                    e.PropertyName == nameof(MainViewModel.TrayTooltipText))
                {
                    UpdateTrayIcon();
                }
                else if (e.PropertyName == nameof(MainViewModel.TotalUnreadCount))
                {
                    UpdateTaskbarBadge();
                }
            };

            // Nhận sự kiện thông báo tin nhắn mới để hiển thị Balloon góc màn hình
            ViewModel.OnTrayBalloonNotificationRequested += (title, message) =>
            {
                TrayIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
            };
        }

        private bool? _trayShowingBadge;
        private int _taskbarBadgeCount = -1;

        /// <summary>
        /// Badge số tin chưa đọc trên nút ứng dụng ở thanh taskbar (overlay icon chuẩn của Windows).
        /// Chỉ hiện khi cửa sổ có nút trên taskbar (đang mở hoặc thu nhỏ, không phải ẩn xuống khay).
        /// </summary>
        private void UpdateTaskbarBadge()
        {
            int count = ViewModel.TotalUnreadCount;
            if (count == _taskbarBadgeCount)
                return;
            _taskbarBadgeCount = count;

            TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
            TaskbarItemInfo.Overlay = count > 0 ? Services.TrayBadgeService.CreateCountBadge(count) : null;
            TaskbarItemInfo.Description = count > 0
                ? $"Chat Workspace Pro — {count} tin chưa đọc"
                : "Chat Workspace Pro";
        }


        private void UpdateTrayIcon()
        {
            try
            {
                bool hasUnread = ViewModel.TrayHasUnread;
                if (_trayShowingBadge != hasUnread)
                {
                    TrayIcon.Icon = hasUnread ? _trayBadge.BadgeIcon : _trayBadge.NormalIcon;
                    _trayShowingBadge = hasUnread;
                }
                if (TrayIcon.ToolTipText != ViewModel.TrayTooltipText)
                    TrayIcon.ToolTipText = ViewModel.TrayTooltipText;
            }
            catch
            {
                // Không để lỗi vẽ icon làm ảnh hưởng ứng dụng
            }
        }

        /// <summary>Lăn chuột trên dải tab để cuộn ngang khi có nhiều tài khoản</summary>
        private void AccountTabScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv && sv.ScrollableWidth > 0)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 2.0);
                e.Handled = true;
            }
        }

        private async void AccountItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ZaloAccountProfile profile)
            {
                await ViewModel.SelectAccountAsync(profile);
            }
        }

        /// <summary>
        /// Hỗ trợ chuyển nhanh tài khoản bằng phím tắt Ctrl + 1..9 với độ trễ 0ms
        /// </summary>
        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var settings = ViewModel.DatabaseService.GetSettings();
            // Tải lại (mặc định F5) trang chat của tài khoản đang chọn (khi WebView2 đang focus, nó tự xử lý F5)
            if (settings.GetHotkey(HotkeyAction.Reload).Matches(e) && !ViewModel.IsLocked)
            {
                if (ViewModel.ReloadCurrentAccountCommand.CanExecute(null))
                    ViewModel.ReloadCurrentAccountCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (settings.GetHotkey(HotkeyAction.Translate).Matches(e) && !ViewModel.IsLocked)
            {
                e.Handled = true;
                await ViewModel.GrabSelectionAndTranslateAsync();
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    int index = e.Key - Key.D1;
                    await ViewModel.SelectAccountByIndexAsync(index);
                    e.Handled = true;
                }
                else if (e.Key == Key.L)
                {
                    ViewModel.LockWorkspace();
                    e.Handled = true;
                }
            }
        }

        private void PinLockInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel.UnlockWorkspaceCommand.CanExecute(null))
            {
                ViewModel.UnlockWorkspaceCommand.Execute(null);
            }
        }

        /// <summary>
        /// Mở Dialog an toàn: tạm ẩn WebViewHostGrid để xử lý triệt để lỗi WPF Airspace
        /// </summary>
        private bool? ShowAirspaceSafeDialog(Window dialog)
        {
            dialog.Owner = this;
            ViewModel.IsModalDialogOpen = true;
            try
            {
                return dialog.ShowDialog();
            }
            finally
            {
                ViewModel.IsModalDialogOpen = false;
            }
        }

        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            var vm = new AddAccountViewModel(ViewModel.ProxyHealthService);
            var dlg = new AddAccountWindow(vm);
            if (ShowAirspaceSafeDialog(dlg) == true)
            {
                vm.Profile.SortOrder = ViewModel.Accounts.Count + 1;
                vm.Profile.ConnectionStatus = AccountConnectionStatus.Hibernated;
                vm.Profile.IsHibernated = true;
                ViewModel.DatabaseService.UpsertAccount(vm.Profile);
                ViewModel.Accounts.Add(vm.Profile);
                ViewModel.SelectedAccount = vm.Profile;
                ViewModel.RefreshStatusMetrics();
            }
        }

        private void EditSelectedAccountProxy_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedAccount == null)
                return;
            OpenEditAccountDialog(ViewModel.SelectedAccount);
        }

        private async void OpenEditAccountDialog(ZaloAccountProfile profile)
        {
            string proxyBefore = profile.ProxySignature;
            var vm = new AddAccountViewModel(ViewModel.ProxyHealthService, profile);
            var dlg = new AddAccountWindow(vm);
            if (ShowAirspaceSafeDialog(dlg) == true)
            {
                ViewModel.DatabaseService.UpsertAccount(profile);
                // Proxy chỉ áp dụng khi tạo WebView2 -> khởi động lại phiên đang chạy nếu cấu hình mạng thay đổi
                if (profile.ProxySignature != proxyBefore)
                {
                    await ViewModel.RestartSessionIfRunningAsync(profile);
                }
                ViewModel.RefreshStatusMetrics();
            }
        }

        private void AddQuickReply_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new QuickReplyWindow(null, ViewModel.AllQuickReplies);
            if (ShowAirspaceSafeDialog(dlg) == true)
            {
                ViewModel.DatabaseService.UpsertQuickReply(dlg.QuickReplyItem);
                ViewModel.AllQuickReplies.Add(dlg.QuickReplyItem);
                ViewModel.ApplyQuickReplyFilter();
                FlashQuickReply(dlg.QuickReplyItem);
            }
        }

        private void OpenEditQuickReplyDialog(QuickReplyTemplate template)
        {
            var dlg = new QuickReplyWindow(template, ViewModel.AllQuickReplies);
            if (ShowAirspaceSafeDialog(dlg) == true)
            {
                ViewModel.DatabaseService.UpsertQuickReply(template);
                ViewModel.ApplyQuickReplyFilter();
                FlashQuickReply(template);
            }
        }

        /// <summary>Viền xanh lá ~1,2 giây trên tin mẫu vừa thêm/sửa</summary>
        private void FlashQuickReply(QuickReplyTemplate template)
        {
            template.IsJustUpdated = true;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                template.IsJustUpdated = false;
            };
            timer.Start();
        }

        private void EditQuickReply_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is QuickReplyTemplate template)
                OpenEditQuickReplyDialog(template);
        }

        private void QuickReplyCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.Tag is QuickReplyTemplate template)
            {
                e.Handled = true;
                OpenEditQuickReplyDialog(template);
            }
        }

        private void DeleteQuickReply_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not QuickReplyTemplate template)
                return;

            ViewModel.IsModalDialogOpen = true;
            try
            {
                var confirm = MessageBox.Show(
                    this,
                    $"Xóa tin mẫu {template.Shortcut} \"{template.Title}\"?",
                    "Xác nhận xóa tin mẫu",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (confirm == MessageBoxResult.Yes)
                    ViewModel.DeleteQuickReplyCommand.Execute(template);
            }
            finally
            {
                ViewModel.IsModalDialogOpen = false;
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsBackupWindow(ViewModel);
            ShowAirspaceSafeDialog(dlg);
        }

        // --- Context Menu trên Left Rail ---
        private async void ContextWakeAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ZaloAccountProfile profile)
            {
                ViewModel.SelectedAccount = profile;
                await ViewModel.StartOrWakeSelectedAccountAsync();
            }
        }

        private void ContextHibernateAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ZaloAccountProfile profile)
            {
                ViewModel.SessionManager.HibernateSession(profile);
                ViewModel.RefreshStatusMetrics();
            }
        }

        private void ContextEditAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ZaloAccountProfile profile)
            {
                OpenEditAccountDialog(profile);
            }
        }

        private void ContextToggleMute_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ZaloAccountProfile profile)
            {
                ViewModel.ToggleAccountNotificationMuteCommand.Execute(profile);
            }
        }

        private void ContextOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ZaloAccountProfile profile)
            {
                ViewModel.SelectedAccount = profile;
                ViewModel.OpenProfileFolderCommand.Execute(null);
            }
        }

        private void ContextDeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ZaloAccountProfile profile)
            {
                ViewModel.IsModalDialogOpen = true;
                try
                {
                    var confirm = MessageBox.Show(
                        this,
                        $"Xóa tài khoản '{profile.DisplayName}'? Thông tin đăng nhập {profile.PlatformName} của tài khoản này trên máy cũng sẽ bị xóa.",
                        "Xác nhận xóa tài khoản",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        ViewModel.SessionManager.HibernateSession(profile);
                        ViewModel.DatabaseService.DeleteAccount(profile.Id);
                        ViewModel.Accounts.Remove(profile);

                        string folder = Services.WebViewSessionManager.GetProfileFolderPath(profile.Id);
                        if (Directory.Exists(folder))
                        {
                            try { Directory.Delete(folder, recursive: true); } catch { }
                        }

                        if (ViewModel.SelectedAccount == profile)
                        {
                            ViewModel.SelectedAccount = ViewModel.Accounts.Count > 0 ? ViewModel.Accounts[0] : null;
                        }
                        ViewModel.RefreshStatusMetrics();
                    }
                }
                finally
                {
                    ViewModel.IsModalDialogOpen = false;
                }
            }
        }

        // --- System Tray & Closing (Giai đoạn 5) ---
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            var settings = ViewModel.DatabaseService.GetSettings();
            if (!_isExplicitExit && settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                // Có thể tắt trong Cài đặt > Chung > "Báo khi ẩn xuống khay"
                if (settings.ShowTrayHintOnClose)
                {
                    TrayIcon.ShowBalloonTip(
                        "Chat Workspace Pro",
                        "Ứng dụng vẫn chạy ở khay hệ thống. Nhấp đúp vào biểu tượng để mở lại.",
                        BalloonIcon.Info);
                }
            }
            else
            {
                ViewModel.SessionManager.HibernateAllSessions(ViewModel.Accounts);
                GlobalHotkeys.Dispose();
                TrayIcon.Dispose();
                _trayBadge.Dispose();
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayOpen_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayLock_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.LockWorkspace();
            RestoreFromTray();
        }

        private void TrayHibernateAll_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SessionManager.HibernateAllSessions(ViewModel.Accounts);
            ViewModel.RefreshStatusMetrics();
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e)
        {
            _isExplicitExit = true;
            Close();
        }

        // ================= Phím tắt =================
        /// <summary>
        /// Áp phím tắt theo cài đặt: đăng ký phím toàn hệ thống, cập nhật phím Dịch trong trang chat và tooltip.
        /// Trả về các chức năng không đăng ký được (bị chương trình khác giữ phím).
        /// </summary>
        public System.Collections.Generic.List<HotkeyAction> ApplyHotkeys(bool notifyConflicts = false)
        {
            var settings = ViewModel.DatabaseService.GetSettings();
            var failed = GlobalHotkeys.Apply(settings);

            var translate = settings.GetHotkey(HotkeyAction.Translate);
            ViewModel.JsBridgeService.TranslateHotkey = translate;
            foreach (var wv in ViewModel.SessionManager.ActiveWebViews.ToList())
                _ = ViewModel.JsBridgeService.ApplyTranslateHotkeyAsync(wv);
            ViewModel.TranslateHotkeyText = translate.ToString();
            ViewModel.ReloadHotkeyText = settings.GetHotkey(HotkeyAction.Reload).ToString();

            string cap = settings.GetHotkey(HotkeyAction.Capture).ToString();
            string capHidden = settings.GetHotkey(HotkeyAction.CaptureHidden).ToString();
            CaptureMenuNormal.InputGestureText = failed.Contains(HotkeyAction.Capture) ? "" : cap;
            CaptureMenuHidden.InputGestureText = failed.Contains(HotkeyAction.CaptureHidden) ? "" : capHidden;
            TrayCaptureItem.InputGestureText = CaptureMenuNormal.InputGestureText;
            TrayCaptureHiddenItem.InputGestureText = CaptureMenuHidden.InputGestureText;


            var tip = new StackPanel();
            tip.Children.Add(new TextBlock { Text = "Chụp màn hình", FontWeight = FontWeights.SemiBold });
            string line1 = string.IsNullOrEmpty(cap) ? "Chưa đặt phím tắt"
                : failed.Contains(HotkeyAction.Capture) ? $"{cap} (đang bị chương trình khác dùng)" : cap;
            var t1 = new TextBlock { Text = line1 };
            var t2 = new TextBlock { Text = "Bấm mũi tên bên cạnh để chụp ẩn ứng dụng" };
            t1.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Muted");
            t2.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Muted");
            tip.Children.Add(t1);
            tip.Children.Add(t2);
            CaptureButton.ToolTip = tip;

            if (notifyConflicts)
            {
                // Chỉ báo khi tình trạng trùng phím thay đổi, tránh lần nào mở máy cũng báo
                string signature = string.Join(",", failed.Select(a => $"{a}={settings.GetHotkey(a)}"));
                if (signature != settings.HotkeyConflictNotified)
                {
                    settings.HotkeyConflictNotified = signature;
                    ViewModel.DatabaseService.SaveSettings(settings);
                    if (failed.Count > 0)
                    {
                        string keys = string.Join(", ", failed.Select(a => settings.GetHotkey(a).ToString()));
                        TrayIcon.ShowBalloonTip("Phím tắt đang bị trùng",
                            $"{keys} đang được chương trình khác dùng (vd Zalo PC). Đổi phím trong Cài đặt > Chung > Phím tắt.",
                            BalloonIcon.Warning);
                    }
                }
            }
            return failed;
        }

        private void OnGlobalHotkey(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.Capture:
                    _ = StartCaptureAsync(hideApp: false, delayMs: 0);
                    break;
                case HotkeyAction.CaptureHidden:
                    _ = StartCaptureAsync(hideApp: true, delayMs: 0);
                    break;
                case HotkeyAction.ToggleApp:
                    if (IsVisible && WindowState != WindowState.Minimized && IsActive)
                        Hide();
                    else
                        RestoreFromTray();
                    break;
            }
        }

        // ================= Chụp màn hình =================
        private void CaptureButton_Click(object sender, RoutedEventArgs e) => _ = StartCaptureAsync(false, 120);

        private void CaptureMenuButton_Click(object sender, RoutedEventArgs e)
        {
            CaptureMenu.PlacementTarget = CaptureButton;
            CaptureMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            CaptureMenu.IsOpen = true;
        }

        private void CaptureNormal_Click(object sender, RoutedEventArgs e) => _ = StartCaptureAsync(false, 250);
        private void CaptureHidden_Click(object sender, RoutedEventArgs e) => _ = StartCaptureAsync(true, 0);
        private void TrayCapture_Click(object sender, RoutedEventArgs e) => _ = StartCaptureAsync(false, 250);
        private void TrayCaptureHidden_Click(object sender, RoutedEventArgs e) => _ = StartCaptureAsync(true, 250);

        /// <summary>
        /// Chụp màn hình. hideApp = ẩn cửa sổ ứng dụng trước khi chụp rồi hiện lại đúng chỗ cũ.
        /// delayMs: chờ menu / tooltip đóng hẳn để không dính vào ảnh.
        /// </summary>
        public async Task StartCaptureAsync(bool hideApp, int delayMs)
        {
            if (_capturing)
                return;
            _capturing = true;
            bool wasShown = IsVisible && WindowState != WindowState.Minimized;
            bool hidden = false;
            try
            {
                if (hideApp && wasShown)
                {
                    Hide();
                    hidden = true;
                    delayMs = Math.Max(delayMs, 300); // chờ hiệu ứng ẩn cửa sổ của Windows
                }
                if (delayMs > 0)
                    await Task.Delay(delayMs);

                var settings = ViewModel.DatabaseService.GetSettings();
                var acc = ViewModel.SelectedAccount;
                string sendHint = ViewModel.IsLocked
                    ? "Ứng dụng đang khóa: ảnh sẽ được chép vào bộ nhớ tạm"
                    : acc != null ? $"Dán ảnh vào ô chat của {acc.DisplayName} ({acc.PlatformName})"
                    : "Chưa chọn tài khoản: ảnh sẽ được chép vào bộ nhớ tạm";
                var win = ScreenCaptureWindow.Create(settings.GetScreenshotFolder(), sendHint);
                if (win == null)
                {
                    TrayIcon.ShowBalloonTip("Chụp màn hình", "Không chụp được màn hình.", BalloonIcon.Error);
                    return;
                }
                win.ShowDialog();

                switch (win.Result)
                {
                    case CaptureResult.Saved:
                        TrayIcon.ShowBalloonTip("Đã lưu ảnh chụp", win.SavedPath ?? string.Empty, BalloonIcon.Info);
                        break;
                    case CaptureResult.SendToChat when win.ResultImage != null:
                        if (ViewModel.IsLocked || acc == null)
                        {
                            ScreenCaptureWindow.SetClipboardImage(win.ResultImage);
                            TrayIcon.ShowBalloonTip("Chụp màn hình",
                                ViewModel.IsLocked ? "Ứng dụng đang khóa. Ảnh đã chép vào bộ nhớ tạm." : "Chưa chọn tài khoản. Ảnh đã chép vào bộ nhớ tạm.",
                                BalloonIcon.Info);
                            break;
                        }
                        RestoreFromTray();
                        hidden = false;
                        await Task.Delay(150);
                        if (!await ViewModel.PasteImageToCurrentChatAsync(win.ResultImage))
                        {
                            TrayIcon.ShowBalloonTip("Chụp màn hình",
                                "Chưa tìm thấy ô chat (hãy mở một cuộc trò chuyện). Ảnh đã chép vào bộ nhớ tạm, bấm Ctrl+V để dán.",
                                BalloonIcon.Info);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                TrayIcon.ShowBalloonTip("Chụp màn hình", "Lỗi: " + ex.Message, BalloonIcon.Error);
            }
            finally
            {
                if (hidden)
                {
                    Show();
                    Activate();
                }
                _capturing = false;
            }
        }

        public void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
    }
}
