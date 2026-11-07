using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZaloMultiWorkspace.Models;
using ZaloMultiWorkspace.Services;

namespace ZaloMultiWorkspace.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public DatabaseService DatabaseService { get; }
        public ProxyHealthService ProxyHealthService { get; }
        public JsBridgeService JsBridgeService { get; }
        public WebViewSessionManager SessionManager { get; }
        public BackupRestoreService BackupRestoreService { get; }

        private readonly DispatcherTimer _monitorTimer;
        private readonly DispatcherTimer _toastDismissTimer;
        private DateTime _lastToastTime = DateTime.MinValue;
        private Guid _lastToastAccountId = Guid.Empty;

        public ObservableCollection<ZaloAccountProfile> Accounts { get; } = new();
        public ObservableCollection<QuickReplyTemplate> AllQuickReplies { get; } = new();
        public ObservableCollection<QuickReplyTemplate> FilteredQuickReplies { get; } = new();
        public ObservableCollection<CustomerNote> CurrentAccountNotes { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSelectedSessionRunning))]
        [NotifyPropertyChangedFor(nameof(ShowPlaceholderOverlay))]
        private ZaloAccountProfile? _selectedAccount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasQuickReplySearch))]
        private string _quickReplySearchQuery = string.Empty;

        public bool HasQuickReplySearch => !string.IsNullOrWhiteSpace(QuickReplySearchQuery);

        /// <summary>"2 / 3 tin mẫu" khi đang lọc</summary>
        [ObservableProperty]
        private string _quickReplyFilterSummary = string.Empty;

        [RelayCommand]
        private void ClearQuickReplySearch()
        {
            QuickReplySearchQuery = string.Empty;
        }

        [ObservableProperty]
        private bool _isRightDrawerOpen = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomerNotesTabActive))]
        private bool _isQuickReplyTabActive = true;

        public bool IsCustomerNotesTabActive => !IsQuickReplyTabActive;

        // --- Mini-CRM Note fields ---
        [ObservableProperty]
        private string _newNoteCustomerName = string.Empty;

        [ObservableProperty]
        private string _newNotePhoneOrTag = string.Empty;

        [ObservableProperty]
        private string _newNoteContent = string.Empty;

        /// <summary>Tên cuộc hội thoại đang mở trên Zalo Web của tài khoản đang chọn</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasActiveChat))]
        private string _currentActiveChatName = string.Empty;

        [ObservableProperty]
        private string _activeChatNotesSummary = string.Empty;

        public bool HasActiveChat => !string.IsNullOrWhiteSpace(CurrentActiveChatName);

        // Tên đã tự điền gần nhất; chỉ ghi đè ô tên khách khi người dùng chưa tự sửa
        private string _lastAutoFilledCustomerName = string.Empty;
        private double _lastTotalRamMb = 0;

        // --- Status Bar (bản gọn hiển thị trên thanh, bản đầy đủ *Text dùng làm tooltip) ---
        [ObservableProperty]
        private string _unreadShortText = "0";

        [ObservableProperty]
        private string _runningShortText = "0/0";

        [ObservableProperty]
        private string _hibernatedShortText = "0";

        [ObservableProperty]
        private string _ramShortText = "0 MB";

        [ObservableProperty]
        private string _autoLockShortText = "Tắt";

        [ObservableProperty]
        private bool _isAutoLockOn = false;

        // --- Icon khay hệ thống: chấm đỏ + tooltip tin chưa đọc theo tài khoản ---
        [ObservableProperty]
        private bool _trayHasUnread = false;

        [ObservableProperty]
        private int _totalUnreadCount = 0;

        [ObservableProperty]
        private string _trayTooltipText = "Chat Workspace Pro";

        // --- Bố cục danh sách tài khoản (tab ngang / thanh dọc) ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVerticalLayout))]
        private bool _isHorizontalLayout = true;

        public bool IsVerticalLayout => !IsHorizontalLayout;

        /// <summary>Áp dụng ngay và lưu bố cục tài khoản vào cài đặt</summary>
        public void SetAccountLayout(AccountLayoutMode mode)
        {
            IsHorizontalLayout = mode == AccountLayoutMode.Horizontal;
            var settings = DatabaseService.GetSettings();
            if (settings.AccountLayout != mode)
            {
                settings.AccountLayout = mode;
                DatabaseService.SaveSettings(settings);
            }
        }

        // --- Giao diện Sáng/Tối ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ThemeGlyph))]
        [NotifyPropertyChangedFor(nameof(ThemeTooltipTitle))]
        [NotifyPropertyChangedFor(nameof(ThemeTooltipHint))]
        [NotifyPropertyChangedFor(nameof(IsThemeSystem))]
        [NotifyPropertyChangedFor(nameof(IsThemeLight))]
        [NotifyPropertyChangedFor(nameof(IsThemeDark))]
        private AppThemeMode _themeMode = AppThemeMode.System;

        public bool IsThemeSystem => ThemeMode == AppThemeMode.System;
        public bool IsThemeLight => ThemeMode == AppThemeMode.Light;
        public bool IsThemeDark => ThemeMode == AppThemeMode.Dark;

        private static string ThemeName(AppThemeMode m) => m switch
        {
            AppThemeMode.Light => "Sáng",
            AppThemeMode.Dark => "Tối",
            _ => "Theo Windows"
        };

        private static AppThemeMode NextTheme(AppThemeMode m) => m switch
        {
            AppThemeMode.System => AppThemeMode.Light,
            AppThemeMode.Light => AppThemeMode.Dark,
            _ => AppThemeMode.System
        };

        /// <summary>Glyph Segoe: màn hình (Theo Windows), mặt trời (Sáng), mặt trăng (Tối)</summary>
        public string ThemeGlyph => ThemeMode switch
        {
            AppThemeMode.Light => "\uE706",
            AppThemeMode.Dark => "\uE708",
            _ => "\uE7F4"
        };

        public string ThemeTooltipTitle => $"Giao diện: {ThemeName(ThemeMode)}";

        public string ThemeTooltipHint =>
            (ThemeMode == AppThemeMode.System ? (ThemeService.IsDark ? "Windows đang tối · " : "Windows đang sáng · ") : string.Empty)
            + $"nhấn để chuyển sang {ThemeName(NextTheme(ThemeMode))}";

        [RelayCommand]
        private void CycleTheme()
        {
            SetThemeMode(NextTheme(ThemeMode));
        }

        /// <summary>Áp dụng ngay và lưu chế độ giao diện vào cài đặt</summary>
        public void SetThemeMode(AppThemeMode mode)
        {
            ThemeMode = mode;
            ThemeService.SetMode(mode);
            var settings = DatabaseService.GetSettings();
            if (settings.ThemeMode != mode)
            {
                settings.ThemeMode = mode;
                DatabaseService.SaveSettings(settings);
            }
            OnPropertyChanged(nameof(ThemeTooltipHint));
        }

        // Đo lại Ping định kỳ cho tài khoản đang chọn
        private static readonly TimeSpan PingRefreshInterval = TimeSpan.FromSeconds(60);
        private DateTime _lastPingRefresh = DateTime.MinValue;
        private bool _isPingRefreshRunning = false;

        [ObservableProperty]
        private string _totalUnreadText = "🔔 Tổng chưa đọc: 0 tin";

        [ObservableProperty]
        private string _runningStatsText = "🟢 Đang chạy: 0/0 tài khoản";

        [ObservableProperty]
        private string _hibernatedStatsText = "Đang tạm dừng: 0 tài khoản";

        [ObservableProperty]
        private string _totalRamUsageText = "Bộ nhớ đang dùng: ~0 MB";

        [ObservableProperty]
        private string _autoLockStatusText = "🔒 Tự động khóa: Bật";

        // --- Master Lock (PIN) & Airspace ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWebViewAirspaceClear))]
        private bool _isLocked = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWebViewAirspaceClear))]
        private bool _isModalDialogOpen = false;

        [ObservableProperty]
        private string _pinInput = string.Empty;

        [ObservableProperty]
        private string _lockErrorMessage = string.Empty;

        // --- In-App Notification Banner (Thông báo nổi khi có tin nhắn từ tài khoản khác) ---
        [ObservableProperty]
        private bool _isInAppNotificationVisible = false;

        [ObservableProperty]
        private ZaloAccountProfile? _notificationAccount;

        [ObservableProperty]
        private string _notificationHeader = string.Empty;

        [ObservableProperty]
        private string _notificationBody = string.Empty;

        public bool IsWebViewAirspaceClear => !IsLocked && !IsModalDialogOpen;

        public bool IsSelectedSessionRunning =>
            SelectedAccount != null &&
            SessionManager.IsSessionRunning(SelectedAccount.Id) &&
            SelectedAccount.ConnectionStatus == AccountConnectionStatus.Online;

        public bool ShowPlaceholderOverlay => !IsSelectedSessionRunning;

        public event Action<string, string>? OnTrayBalloonNotificationRequested;

        public MainViewModel()
        {
            DatabaseService = new DatabaseService();
            ProxyHealthService = new ProxyHealthService();
            JsBridgeService = new JsBridgeService();
            SessionManager = new WebViewSessionManager(ProxyHealthService, JsBridgeService);
            BackupRestoreService = new BackupRestoreService();

            SessionManager.OnUnreadCountChanged += HandleUnreadCountChanged;
            SessionManager.OnNewMessageNotification += HandleNewMessageNotification;
            SessionManager.OnShortcutTriggered += HandleShortcutTriggered;
            SessionManager.OnActiveChatChanged += HandleActiveChatChanged;
            SessionManager.OnAvatarUpdated += profile => DatabaseService.UpsertAccount(profile);
            SessionManager.OnSessionStateChanged += RefreshStatusMetrics;

            _toastDismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(7)
            };
            _toastDismissTimer.Tick += (s, e) =>
            {
                IsInAppNotificationVisible = false;
                _toastDismissTimer.Stop();
            };

            LoadInitialData();

            ThemeMode = DatabaseService.GetSettings().ThemeMode;
            IsHorizontalLayout = DatabaseService.GetSettings().AccountLayout == AccountLayoutMode.Horizontal;
            ThemeService.EffectiveThemeChanged += _ => OnPropertyChanged(nameof(ThemeTooltipHint));

            // Giai đoạn 3 & 5: Bộ đếm định kỳ mỗi 5 giây đo RAM, kiểm tra Ngủ đông tự động & Khóa tự động
            _monitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _monitorTimer.Tick += OnMonitorTimerTick;
            _monitorTimer.Start();
        }

        public void LoadInitialData()
        {
            Accounts.Clear();
            foreach (var acc in DatabaseService.GetAccounts())
            {
                // Trạng thái lúc chạy luôn bắt đầu sạch (DB cũ có thể còn lưu nhầm các giá trị này)
                acc.IsSelected = false;
                acc.UnreadCount = 0;
                acc.IsHibernated = true;
                acc.ConnectionStatus = AccountConnectionStatus.Hibernated;
                acc.StatusMessage = string.Empty;
                Accounts.Add(acc);
            }

            AllQuickReplies.Clear();
            foreach (var qr in DatabaseService.GetQuickReplies())
            {
                AllQuickReplies.Add(qr);
            }
            ApplyQuickReplyFilter();

            SelectedAccount = Accounts.FirstOrDefault();
            if (SelectedAccount != null)
            {
                SelectedAccount.IsSelected = true;
                LoadCustomerNotesForAccount(SelectedAccount.Id);
            }

            RefreshStatusMetrics();
        }

        partial void OnSelectedAccountChanged(ZaloAccountProfile? oldValue, ZaloAccountProfile? newValue)
        {
            if (oldValue != null)
                oldValue.IsSelected = false;

            if (newValue != null)
            {
                newValue.IsSelected = true;
                newValue.LastInteractionTime = DateTime.Now;
                CurrentActiveChatName = newValue.ActiveChatName;
                _lastPingRefresh = DateTime.MinValue;
                AutoFillCustomerName(newValue.ActiveChatName);
                LoadCustomerNotesForAccount(newValue.Id);

                // Nếu người dùng chuyển sang đúng tài khoản đang có Banner thông báo nổi thì tự đóng Banner
                if (NotificationAccount != null && NotificationAccount.Id == newValue.Id)
                {
                    IsInAppNotificationVisible = false;
                    _toastDismissTimer.Stop();
                }

                if (SessionManager.IsSessionRunning(newValue.Id))
                {
                    SessionManager.SwitchVisibleTab(newValue.Id);
                }
                else
                {
                    SessionManager.HideAllWebViews();
                }
            }
            RefreshStatusMetrics();
        }

        partial void OnQuickReplySearchQueryChanged(string value)
        {
            ApplyQuickReplyFilter();
        }

        public void ApplyQuickReplyFilter()
        {
            FilteredQuickReplies.Clear();
            string q = QuickReplySearchQuery?.Trim().ToLowerInvariant() ?? string.Empty;

            foreach (var item in AllQuickReplies)
            {
                if (string.IsNullOrEmpty(q) ||
                    item.Shortcut.ToLowerInvariant().Contains(q) ||
                    item.Title.ToLowerInvariant().Contains(q) ||
                    item.Content.ToLowerInvariant().Contains(q))
                {
                    FilteredQuickReplies.Add(item);
                }
            }

            QuickReplyFilterSummary = string.IsNullOrEmpty(q)
                ? string.Empty
                : $"{FilteredQuickReplies.Count} / {AllQuickReplies.Count} tin mẫu";
        }

        private void LoadCustomerNotesForAccount(Guid accountId)
        {
            var notes = DatabaseService.GetCustomerNotes(accountId);
            CurrentAccountNotes.Clear();
            foreach (var note in notes)
            {
                CurrentAccountNotes.Add(note);
            }
            ApplyActiveChatNoteMatching();
        }

        private static string NormalizeName(string? name) =>
            (name ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// Đánh dấu và đưa lên đầu các ghi chú thuộc khách hàng đang chat trên Zalo Web.
        /// </summary>
        private void ApplyActiveChatNoteMatching()
        {
            string active = NormalizeName(CurrentActiveChatName);
            foreach (var note in CurrentAccountNotes)
            {
                note.IsActiveChatMatch = active.Length > 0 && NormalizeName(note.CustomerName) == active;
            }

            var ordered = CurrentAccountNotes
                .OrderByDescending(n => n.IsActiveChatMatch)
                .ThenByDescending(n => n.UpdatedAt)
                .ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                int oldIndex = CurrentAccountNotes.IndexOf(ordered[i]);
                if (oldIndex != i)
                    CurrentAccountNotes.Move(oldIndex, i);
            }

            int matchCount = CurrentAccountNotes.Count(n => n.IsActiveChatMatch);
            ActiveChatNotesSummary = active.Length == 0
                ? string.Empty
                : matchCount > 0
                    ? $"💬 Đang chat: {CurrentActiveChatName} — {matchCount} ghi chú"
                    : $"💬 Đang chat: {CurrentActiveChatName} — chưa có ghi chú";
        }

        private void AutoFillCustomerName(string activeChatName)
        {
            if (string.IsNullOrWhiteSpace(activeChatName))
                return;
            // Không ghi đè khi người dùng đã tự gõ tên khác
            if (string.IsNullOrWhiteSpace(NewNoteCustomerName) || NewNoteCustomerName == _lastAutoFilledCustomerName)
            {
                NewNoteCustomerName = activeChatName;
                _lastAutoFilledCustomerName = activeChatName;
            }
        }

        [RelayCommand]
        public async Task SelectAccountAsync(ZaloAccountProfile? profile)
        {
            if (profile == null)
                return;

            SelectedAccount = profile;
            OnPropertyChanged(nameof(IsSelectedSessionRunning));
            OnPropertyChanged(nameof(ShowPlaceholderOverlay));
            await Task.CompletedTask;
        }

        public async Task SelectAccountByIndexAsync(int index)
        {
            if (index >= 0 && index < Accounts.Count)
            {
                var acc = Accounts[index];
                SelectedAccount = acc;
                if (!SessionManager.IsSessionRunning(acc.Id))
                {
                    await StartOrWakeSelectedAccountAsync();
                }
            }
        }

        [RelayCommand]
        public async Task StartOrWakeSelectedAccountAsync()
        {
            if (SelectedAccount == null)
                return;

            await SessionManager.StartOrWakeSessionAsync(SelectedAccount, makeVisible: true);
            OnPropertyChanged(nameof(IsSelectedSessionRunning));
            OnPropertyChanged(nameof(ShowPlaceholderOverlay));
            RefreshStatusMetrics();
        }

        [RelayCommand]
        private void ReloadCurrentAccount()
        {
            if (SelectedAccount == null)
                return;

            if (SessionManager.IsSessionRunning(SelectedAccount.Id))
            {
                SessionManager.ReloadSession(SelectedAccount);
            }
            else
            {
                _ = StartOrWakeSelectedAccountAsync();
            }
        }

        [RelayCommand]
        private void HibernateCurrentAccount()
        {
            if (SelectedAccount == null)
                return;

            SessionManager.HibernateSession(SelectedAccount);
            OnPropertyChanged(nameof(IsSelectedSessionRunning));
            OnPropertyChanged(nameof(ShowPlaceholderOverlay));
            RefreshStatusMetrics();
        }

        [RelayCommand]
        private void OpenProfileFolder()
        {
            if (SelectedAccount == null)
                return;

            string folder = WebViewSessionManager.GetProfileFolderPath(SelectedAccount.Id);
            Directory.CreateDirectory(folder);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        private void ToggleRightDrawer()
        {
            IsRightDrawerOpen = !IsRightDrawerOpen;
        }

        [RelayCommand]
        private void SwitchToQuickReplyTab()
        {
            IsQuickReplyTabActive = true;
        }

        [RelayCommand]
        private void SwitchToCustomerNotesTab()
        {
            IsQuickReplyTabActive = false;
        }

        // --- Quick Reply Actions ---
        [RelayCommand]
        private async Task InsertQuickReplyAsync(QuickReplyTemplate? template)
        {
            if (template == null || SelectedAccount == null)
                return;

            var webView = SessionManager.GetWebView(SelectedAccount.Id);
            if (webView == null)
            {
                await StartOrWakeSelectedAccountAsync();
                webView = SessionManager.GetWebView(SelectedAccount.Id);
            }

            if (webView != null)
            {
                await JsBridgeService.InsertQuickReplyAsync(webView, SelectedAccount.Platform, template.Content, template.ImagePath, sendImmediately: false);
            }
        }

        [RelayCommand]
        private async Task SendQuickReplyNowAsync(QuickReplyTemplate? template)
        {
            if (template == null || SelectedAccount == null)
                return;

            var webView = SessionManager.GetWebView(SelectedAccount.Id);
            if (webView == null)
            {
                await StartOrWakeSelectedAccountAsync();
                webView = SessionManager.GetWebView(SelectedAccount.Id);
            }

            if (webView != null)
            {
                await JsBridgeService.InsertQuickReplyAsync(webView, SelectedAccount.Platform, template.Content, template.ImagePath, sendImmediately: true);
            }
        }

        [RelayCommand]
        private void DeleteQuickReply(QuickReplyTemplate? template)
        {
            if (template == null)
                return;

            DatabaseService.DeleteQuickReply(template.Id);
            AllQuickReplies.Remove(template);
            ApplyQuickReplyFilter();
        }

        // --- Customer Notes (Mini-CRM) ---
        [RelayCommand]
        private void SaveCustomerNote()
        {
            if (SelectedAccount == null || string.IsNullOrWhiteSpace(NewNoteContent))
                return;

            var note = new CustomerNote
            {
                Id = Guid.NewGuid(),
                AccountId = SelectedAccount.Id,
                CustomerName = string.IsNullOrWhiteSpace(NewNoteCustomerName) ? "Khách hàng" : NewNoteCustomerName.Trim(),
                CustomerPhoneOrTag = NewNotePhoneOrTag?.Trim() ?? string.Empty,
                NoteContent = NewNoteContent.Trim(),
                UpdatedAt = DateTime.Now
            };

            DatabaseService.UpsertCustomerNote(note);
            CurrentAccountNotes.Insert(0, note);
            ApplyActiveChatNoteMatching();

            NewNoteContent = string.Empty;
            NewNotePhoneOrTag = string.Empty;
        }

        [RelayCommand]
        private void DeleteCustomerNote(CustomerNote? note)
        {
            if (note == null)
                return;

            DatabaseService.DeleteCustomerNote(note.Id);
            CurrentAccountNotes.Remove(note);
        }

        // --- Master Lock (PIN) ---
        [RelayCommand]
        public void LockWorkspace()
        {
            PinInput = string.Empty;
            LockErrorMessage = string.Empty;
            IsLocked = true;
        }

        [RelayCommand]
        private void UnlockWorkspace()
        {
            if (DatabaseService.VerifyMasterPin(PinInput))
            {
                PinInput = string.Empty;
                LockErrorMessage = string.Empty;
                IsLocked = false;
            }
            else
            {
                LockErrorMessage = "Mã PIN không đúng.";
            }
        }

        // --- Event Handlers từ WebViewSessionManager ---
        private void HandleUnreadCountChanged(ZaloAccountProfile profile, int newCount)
        {
            if (newCount == 0 && NotificationAccount != null && NotificationAccount.Id == profile.Id)
            {
                IsInAppNotificationVisible = false;
                _toastDismissTimer.Stop();
            }
            RefreshStatusMetrics();
        }

        private void HandleNewMessageNotification(ZaloAccountProfile profile, string senderName, string bodyText)
        {
            RefreshStatusMetrics();

            // Chỉ hiện thông báo khi tin nhắn đến từ tài khoản khác (không phải tab đang mở) hoặc khi cửa sổ thu nhỏ
            bool isBackgroundAccount = !profile.IsSelected;
            bool isWindowInactive = Application.Current?.MainWindow != null &&
                                    (!Application.Current.MainWindow.IsActive ||
                                     Application.Current.MainWindow.WindowState == WindowState.Minimized);

            if (!isBackgroundAccount && !isWindowInactive)
                return;

            // Chống lặp thông báo quá dày trong 2 giây trên cùng 1 tài khoản
            var now = DateTime.Now;
            if (_lastToastAccountId == profile.Id && (now - _lastToastTime).TotalSeconds < 2.0)
                return;

            _lastToastAccountId = profile.Id;
            _lastToastTime = now;

            // Tài khoản đã tắt thông báo: chỉ đếm tin chưa đọc (chấm đỏ/badge), không báo gì thêm
            if (profile.IsNotificationMuted)
                return;

            var settings = DatabaseService.GetSettings();
            bool doNotDisturb = settings.IsInDoNotDisturb(now);

            if (settings.ShowInAppBanner)
            {
                NotificationAccount = profile;
                NotificationHeader = $"🔔 [{profile.DisplayName}] {senderName}";
                NotificationBody = bodyText;
                IsInAppNotificationVisible = true;

                _toastDismissTimer.Stop();
                _toastDismissTimer.Start();
            }

            if (doNotDisturb)
                return;

            if (settings.PlaySound)
            {
                try
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
                catch { }
            }

            if (settings.ShowToastPopup)
            {
                if (settings.HideMessageContent)
                {
                    OnTrayBalloonNotificationRequested?.Invoke(
                        $"{profile.PlatformName} [{profile.DisplayName}]",
                        "Bạn có tin nhắn mới");
                }
                else
                {
                    OnTrayBalloonNotificationRequested?.Invoke(
                        $"{profile.PlatformName} [{profile.DisplayName}] - {senderName}",
                        bodyText);
                }
            }
        }

        [RelayCommand]
        private async Task SwitchToNotifiedAccountAsync()
        {
            IsInAppNotificationVisible = false;
            _toastDismissTimer.Stop();
            if (NotificationAccount != null)
            {
                await SelectAccountAsync(NotificationAccount);
                if (!SessionManager.IsSessionRunning(NotificationAccount.Id))
                {
                    await StartOrWakeSelectedAccountAsync();
                }
            }
        }

        [RelayCommand]
        private void DismissNotification()
        {
            IsInAppNotificationVisible = false;
            _toastDismissTimer.Stop();
        }

        private async void HandleShortcutTriggered(ZaloAccountProfile profile, string shortcut)
        {
            var matched = AllQuickReplies.FirstOrDefault(
                q => string.Equals(q.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                var wv = SessionManager.GetWebView(profile.Id);
                if (wv != null)
                {
                    await JsBridgeService.InsertQuickReplyAsync(
                        wv,
                        profile.Platform,
                        matched.Content,
                        matched.ImagePath,
                        sendImmediately: false,
                        replaceAll: true);
                }
            }
        }

        private void HandleActiveChatChanged(ZaloAccountProfile profile, string activeChatName)
        {
            if (string.IsNullOrWhiteSpace(activeChatName) || profile.ActiveChatName == activeChatName)
                return;

            profile.ActiveChatName = activeChatName;
            if (profile.IsSelected)
            {
                CurrentActiveChatName = activeChatName;
                AutoFillCustomerName(activeChatName);
                ApplyActiveChatNoteMatching();
            }
        }

        /// <summary>
        /// Khởi động lại phiên đang chạy để áp dụng cấu hình Proxy mới (tham số Proxy chỉ đặt được khi tạo WebView2).
        /// </summary>
        public async Task RestartSessionIfRunningAsync(ZaloAccountProfile profile)
        {
            if (!SessionManager.IsSessionRunning(profile.Id))
                return;

            await SessionManager.HibernateAllSessionsAndWaitAsync(new[] { profile });
            await SessionManager.StartOrWakeSessionAsync(profile, makeVisible: profile.IsSelected);
            RefreshStatusMetrics();
        }

        private void OnMonitorTimerTick(object? sender, EventArgs e)
        {
            // 1. Kiểm tra tự động ngủ đông (Tab Hibernation) đối với các tab không tương tác quá X phút & không có tin chưa đọc
            var now = DateTime.Now;
            foreach (var acc in Accounts)
            {
                if (!acc.IsSelected &&
                    SessionManager.IsSessionRunning(acc.Id) &&
                    acc.UnreadCount == 0 &&
                    acc.AutoHibernateMinutes > 0)
                {
                    double idleMinutes = (now - acc.LastInteractionTime).TotalMinutes;
                    if (idleMinutes >= acc.AutoHibernateMinutes)
                    {
                        SessionManager.HibernateSession(acc);
                    }
                }
            }

            // 2. Kiểm tra tự động khóa màn hình (Master Lock) khi máy không hoạt động
            var settings = DatabaseService.GetSettings();
            if (settings.IsAutoLockEnabled && !IsLocked && settings.AutoLockIdleMinutes > 0)
            {
                double systemIdleMinutes = IdleLockService.GetSystemIdleSeconds() / 60.0;
                if (systemIdleMinutes >= settings.AutoLockIdleMinutes)
                {
                    LockWorkspace();
                }
            }

            // 3. Đo lại Ping của tài khoản đang chọn mỗi 60 giây để chip mạng phản ánh tình trạng thực
            _ = RefreshSelectedPingIfDueAsync();

            // 4. Đo RAM (tốn tài nguyên -> chỉ chạy theo chu kỳ 5 giây) và cập nhật Status Bar
            _lastTotalRamMb = SessionManager.MeasureTotalRamUsageMb(Accounts);
            RefreshStatusMetrics();
        }

        private async Task RefreshSelectedPingIfDueAsync()
        {
            var acc = SelectedAccount;
            if (acc == null || _isPingRefreshRunning ||
                acc.ConnectionStatus != AccountConnectionStatus.Online ||
                !SessionManager.IsSessionRunning(acc.Id) ||
                DateTime.Now - _lastPingRefresh < PingRefreshInterval)
                return;

            _isPingRefreshRunning = true;
            _lastPingRefresh = DateTime.Now;
            try
            {
                var result = await ProxyHealthService.CheckProxyAsync(acc);
                // Không chuyển trạng thái phiên sang lỗi (tránh che WebView), chỉ báo trên chip
                acc.IsPingFailed = !result.IsHealthy;
                acc.PingMs = result.PingMs;
            }
            catch
            {
                acc.IsPingFailed = true;
            }
            finally
            {
                _isPingRefreshRunning = false;
            }
        }

        /// <summary>
        /// Tooltip icon khay: tổng tin chưa đọc và chia theo tài khoản.
        /// Windows giới hạn tooltip khay ~127 ký tự nên rút gọn nếu quá dài.
        /// </summary>
        private string BuildTrayTooltip(int totalUnread)
        {
            const string appName = "Chat Workspace Pro";
            const int maxLength = 127;
            if (totalUnread <= 0)
                return $"{appName}\nKhông có tin chưa đọc";

            string header = $"{appName}\n{totalUnread} tin chưa đọc";
            var parts = Accounts
                .Where(a => a.UnreadCount > 0)
                .OrderByDescending(a => a.UnreadCount)
                .Select(a => $"{a.DisplayName}: {a.UnreadCount}")
                .ToList();

            string detail = string.Join(" · ", parts);
            string full = $"{header}\n{detail}";
            if (full.Length <= maxLength)
                return full;

            // Bớt dần tài khoản cuối danh sách, thêm "…" cho tới khi vừa giới hạn
            for (int keep = parts.Count - 1; keep >= 1; keep--)
            {
                string shortDetail = string.Join(" · ", parts.Take(keep)) + " …";
                string candidate = $"{header}\n{shortDetail}";
                if (candidate.Length <= maxLength)
                    return candidate;
            }
            return header.Length <= maxLength ? header : header.Substring(0, maxLength);
        }

        [RelayCommand]
        private void ToggleAccountNotificationMute(ZaloAccountProfile? profile)
        {
            if (profile == null)
                return;
            profile.IsNotificationMuted = !profile.IsNotificationMuted;
            DatabaseService.UpsertAccount(profile);
            if (profile.IsNotificationMuted && NotificationAccount?.Id == profile.Id)
            {
                IsInAppNotificationVisible = false;
                _toastDismissTimer.Stop();
            }
        }

        public void RefreshStatusMetrics()
        {
            int totalUnread = Accounts.Sum(a => a.UnreadCount);
            int totalAccounts = Accounts.Count;
            int runningCount = SessionManager.RunningCount;
            int hibernatedCount = Accounts.Count(a => !SessionManager.IsSessionRunning(a.Id));
            if (runningCount == 0)
                _lastTotalRamMb = 0;
            double totalRamMb = _lastTotalRamMb;
            var settings = DatabaseService.GetSettings(); // lấy từ bộ nhớ đệm, không mở DB

            TotalUnreadText = $"Tổng tin chưa đọc: {totalUnread}";
            RunningStatsText = $"Đang chạy: {runningCount}/{totalAccounts} tài khoản";
            HibernatedStatsText = $"Đang tạm dừng: {hibernatedCount} tài khoản";
            TotalRamUsageText = $"Bộ nhớ đang dùng: ~{totalRamMb:0} MB";
            AutoLockStatusText = settings.IsAutoLockEnabled
                ? $"Tự động khóa sau {settings.AutoLockIdleMinutes} phút không thao tác — nhấn để cấu hình"
                : "Tự động khóa: Tắt — nhấn để cấu hình";

            UnreadShortText = totalUnread.ToString();
            // Chấm đỏ khay + badge taskbar có thể tắt trong Cài đặt > Thông báo
            TrayHasUnread = settings.ShowUnreadBadge && totalUnread > 0;
            TotalUnreadCount = settings.ShowUnreadBadge ? totalUnread : 0;
            TrayTooltipText = BuildTrayTooltip(totalUnread);
            RunningShortText = $"{runningCount}/{totalAccounts}";
            HibernatedShortText = hibernatedCount.ToString();
            RamShortText = $"{totalRamMb:0} MB";
            IsAutoLockOn = settings.IsAutoLockEnabled;
            AutoLockShortText = settings.IsAutoLockEnabled ? $"{settings.AutoLockIdleMinutes}p" : "Tắt";

            OnPropertyChanged(nameof(IsSelectedSessionRunning));
            OnPropertyChanged(nameof(ShowPlaceholderOverlay));
        }
    }
}

