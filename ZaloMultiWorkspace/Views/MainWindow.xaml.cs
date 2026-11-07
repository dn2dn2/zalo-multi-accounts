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
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            // Gắn Grid chứa các thực thể WebView2
            ViewModel.SessionManager.AttachHostGrid(WebViewHostGrid);

            // Nhận sự kiện thông báo tin nhắn mới để hiển thị Balloon góc màn hình
            ViewModel.OnTrayBalloonNotificationRequested += (title, message) =>
            {
                TrayIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
            };
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

        private void OpenEditAccountDialog(ZaloAccountProfile profile)
        {
            var vm = new AddAccountViewModel(ViewModel.ProxyHealthService, profile);
            var dlg = new AddAccountWindow(vm);
            if (ShowAirspaceSafeDialog(dlg) == true)
            {
                ViewModel.DatabaseService.UpsertAccount(profile);
                ViewModel.RefreshStatusMetrics();
            }
        }

        private void AddQuickReply_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new QuickReplyWindow();
            if (ShowAirspaceSafeDialog(dlg) == true)
            {
                ViewModel.DatabaseService.UpsertQuickReply(dlg.QuickReplyItem);
                ViewModel.AllQuickReplies.Add(dlg.QuickReplyItem);
                ViewModel.ApplyQuickReplyFilter();
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

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
    }
}
