using System;
using System.ComponentModel;
using System.IO;
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
        private readonly Services.TrayBadgeService _trayBadge = new();
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
            TaskbarItemInfo.Overlay = count > 0 ? CreateBadgeImage(count) : null;
            TaskbarItemInfo.Description = count > 0
                ? $"Zalo Multi-Workspace Pro — {count} tin chưa đọc"
                : "Zalo Multi-Workspace Pro";
        }

        private static System.Windows.Media.ImageSource CreateBadgeImage(int count)
        {
            const double size = 32; // vẽ 32px, Windows tự thu về kích thước overlay (16px ở 100%)
            string text = count > 99 ? "99+" : count.ToString();
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var red = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 41, 62));
                dc.DrawEllipse(red, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2),
                    new Point(size / 2, size / 2), size / 2 - 1, size / 2 - 1);

                double fontSize = text.Length switch { 1 => 22, 2 => 17, _ => 12 };
                var ft = new System.Windows.Media.FormattedText(text,
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"),
                        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    fontSize, System.Windows.Media.Brushes.White, 1.0);
                dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size, (int)size, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
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
            // F5: tải lại trang Zalo của tài khoản đang chọn (khi WebView2 đang focus, nó tự xử lý F5)
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None && !ViewModel.IsLocked)
            {
                if (ViewModel.ReloadCurrentAccountCommand.CanExecute(null))
                    ViewModel.ReloadCurrentAccountCommand.Execute(null);
                e.Handled = true;
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
                        $"Bạn có chắc chắn muốn xóa tài khoản '{profile.DisplayName}' và toàn bộ dữ liệu phiên đăng nhập của tài khoản này?",
                        "Xác nhận xóa Profile",
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
                TrayIcon.ShowBalloonTip(
                    "Zalo Multi-Workspace Pro",
                    "Ứng dụng đang chạy ngầm dưới khay hệ thống. Nhấp đúp vào biểu tượng để mở lại.",
                    BalloonIcon.Info);
            }
            else
            {
                ViewModel.SessionManager.HibernateAllSessions(ViewModel.Accounts);
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

        public void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
    }
}
