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

            PinTextBox.Text = _settings.MasterPin;
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
                    BackupStatusText.Text = "⏳ Đang đưa các tài khoản về chế độ Ngủ đông và nén dữ liệu...";
                    // Giai đoạn 5: Đưa toàn bộ tài khoản về trạng thái Ngủ đông (Dispose) để mở khóa file Cookie/IndexedDB
                    _mainViewModel.SessionManager.HibernateAllSessions(_mainViewModel.Accounts);
                    await _mainViewModel.BackupRestoreService.BackupWorkspaceAsync(dlg.FileName);
                    BackupStatusText.Text = $"✅ Đã sao lưu toàn bộ Profile thành công tại: {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"❌ Lỗi khi sao lưu: {ex.Message}";
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
                    BackupStatusText.Text = "⏳ Đang đóng các phiên hiện tại và giải nén khôi phục...";
                    _mainViewModel.SessionManager.HibernateAllSessions(_mainViewModel.Accounts);
                    await _mainViewModel.BackupRestoreService.RestoreWorkspaceAsync(dlg.FileName);
                    _mainViewModel.LoadInitialData();
                    BackupStatusText.Text = "✅ Khôi phục dữ liệu hoàn tất! Danh sách tài khoản đã được cập nhật.";
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"❌ Lỗi khi khôi phục: {ex.Message}";
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.MasterPin = string.IsNullOrWhiteSpace(PinTextBox.Text) ? "1234" : PinTextBox.Text.Trim();
            _settings.AutoLockIdleMinutes = int.TryParse(IdleMinutesTextBox.Text.Trim(), out int m) && m > 0 ? m : 15;
            _settings.IsAutoLockEnabled = AutoLockCheckBox.IsChecked == true;
            _settings.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;

            _mainViewModel.DatabaseService.SaveSettings(_settings);
            _mainViewModel.RefreshStatusMetrics();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

