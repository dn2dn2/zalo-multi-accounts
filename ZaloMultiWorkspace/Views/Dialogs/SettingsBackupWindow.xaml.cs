using System;
using System.Windows;
using Microsoft.Win32;
using ZaloMultiWorkspace.Models;
using ZaloMultiWorkspace.ViewModels;

namespace ZaloMultiWorkspace.Views.Dialogs
{
    public partial class SettingsBackupWindow : Window
    {
        private readonly MainViewModel _mainViewModel;
        private readonly AppSettings _settings;

        public SettingsBackupWindow(MainViewModel mainViewModel)
        {
            InitializeComponent();
            _mainViewModel = mainViewModel;
            _settings = _mainViewModel.DatabaseService.GetSettings();
            DataContext = _mainViewModel; // cho nhóm nút chọn giao diện

            DefaultPinWarningText.Visibility = _settings.IsUsingDefaultPin ? Visibility.Visible : Visibility.Collapsed;
            IdleMinutesTextBox.Text = _settings.AutoLockIdleMinutes.ToString();
            AutoLockCheckBox.IsChecked = _settings.IsAutoLockEnabled;
            MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTrayOnClose;
        }

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Chọn nơi lưu gói Sao lưu Zalo Multi-Workspace (.zip)",
                Filter = "Zip Archive (*.zip)|*.zip",
                FileName = $"ZaloMultiWorkspace_Backup_{DateTime.Now:yyyyMMdd_HHmm}.zip"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    IsEnabled = false;
                    BackupStatusText.Text = "⏳ Đang đưa các tài khoản về chế độ Ngủ đông và nén dữ liệu...";
                    // Giai đoạn 5: Ngủ đông toàn bộ và chờ tiến trình WebView2 thoát hẳn để mở khóa file Cookie/IndexedDB
                    await _mainViewModel.SessionManager.HibernateAllSessionsAndWaitAsync(_mainViewModel.Accounts);
                    var result = await _mainViewModel.BackupRestoreService.BackupWorkspaceAsync(dlg.FileName);
                    _mainViewModel.RefreshStatusMetrics();
                    BackupStatusText.Text = result.SkippedFiles.Count == 0
                        ? $"✅ Đã sao lưu {result.FileCount} file thành công tại: {dlg.FileName}"
                        : $"⚠️ Đã sao lưu {result.FileCount} file tại: {dlg.FileName}. Bỏ qua {result.SkippedFiles.Count} file đang bị khóa (vd: {result.SkippedFiles[0]}).";
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"❌ Lỗi khi sao lưu: {ex.Message}";
                }
                finally
                {
                    IsEnabled = true;
                }
            }
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn file Sao lưu Zalo Multi-Workspace (.zip)",
                Filter = "Zip Archive (*.zip)|*.zip"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    IsEnabled = false;
                    BackupStatusText.Text = "⏳ Đang đóng các phiên hiện tại và giải nén khôi phục...";
                    await _mainViewModel.SessionManager.HibernateAllSessionsAndWaitAsync(_mainViewModel.Accounts);
                    await _mainViewModel.BackupRestoreService.RestoreWorkspaceAsync(dlg.FileName);
                    _mainViewModel.DatabaseService.RunMigrations();
                    _mainViewModel.LoadInitialData();
                    BackupStatusText.Text = "✅ Khôi phục dữ liệu hoàn tất! Danh sách tài khoản đã được cập nhật.";
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"❌ Lỗi khi khôi phục (dữ liệu cũ đã được giữ nguyên): {ex.Message}";
                }
                finally
                {
                    IsEnabled = true;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string newPin = PinTextBox.Password?.Trim() ?? string.Empty;
            if (newPin.Length > 0)
            {
                if (newPin.Length < 4)
                {
                    MessageBox.Show(this, "Mã PIN phải có ít nhất 4 ký tự.", "Mã PIN không hợp lệ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _mainViewModel.DatabaseService.SetMasterPin(_settings, newPin);
            }
            _settings.AutoLockIdleMinutes = int.TryParse(IdleMinutesTextBox.Text.Trim(), out int m) && m > 0 ? m : 15;
            _settings.IsAutoLockEnabled = AutoLockCheckBox.IsChecked == true;
            _settings.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;

            _mainViewModel.DatabaseService.SaveSettings(_settings);
            _mainViewModel.RefreshStatusMetrics();
            DialogResult = true;
            Close();
        }

        /// <summary>Đổi giao diện áp dụng và lưu ngay, không cần bấm Lưu cài đặt</summary>
        private void ThemeSegment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && Enum.TryParse(fe.Tag?.ToString(), out Services.AppThemeMode mode))
            {
                _mainViewModel.SetThemeMode(mode);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

