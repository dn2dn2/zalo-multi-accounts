using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ZaloMultiWorkspace.Models;
using ZaloMultiWorkspace.ViewModels;

namespace ZaloMultiWorkspace.Views.Dialogs
{
    public partial class SettingsBackupWindow : Window
    {
        private const int DndOff = 0;
        private const int DndSchedule = 1;
        private const int DndSnooze = 2;

        private readonly MainViewModel _mainViewModel;
        private readonly AppSettings _settings;

        public SettingsBackupWindow(MainViewModel mainViewModel)
        {
            InitializeComponent();
            _mainViewModel = mainViewModel;
            _settings = _mainViewModel.DatabaseService.GetSettings();
            DataContext = _mainViewModel; // cho nhóm nút chọn giao diện

            // Tab Chung
            MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTrayOnClose;
            TrayHintSwitch.IsChecked = _settings.ShowTrayHintOnClose;

            // Tab Thông báo
            ToastPopupSwitch.IsChecked = _settings.ShowToastPopup;
            HideContentSwitch.IsChecked = _settings.HideMessageContent;
            InAppBannerSwitch.IsChecked = _settings.ShowInAppBanner;
            SoundSwitch.IsChecked = _settings.PlaySound;
            BadgeSwitch.IsChecked = _settings.ShowUnreadBadge;
            DndStartTextBox.Text = _settings.DndStart;
            DndEndTextBox.Text = _settings.DndEnd;
            bool snoozing = _settings.DndUntil.HasValue && _settings.DndUntil.Value > DateTime.Now;
            DndModeCombo.SelectedIndex = snoozing ? DndSnooze : _settings.IsDndScheduleEnabled ? DndSchedule : DndOff;
            UpdateDndUi();

            // Tab Bảo mật
            DefaultPinWarningText.Visibility = _settings.IsUsingDefaultPin ? Visibility.Visible : Visibility.Collapsed;
            IdleMinutesTextBox.Text = _settings.AutoLockIdleMinutes.ToString();
            AutoLockCheckBox.IsChecked = _settings.IsAutoLockEnabled;
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null)
                return; // đang khởi tạo
            PanelGeneral.Visibility = TabGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelNotify.Visibility = TabNotify.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelSecurity.Visibility = TabSecurity.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelBackup.Visibility = TabBackup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DndModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DndSchedulePanel == null)
                return;
            UpdateDndUi();
        }

        private void UpdateDndUi()
        {
            int mode = DndModeCombo.SelectedIndex;
            DndSchedulePanel.Visibility = mode == DndSchedule ? Visibility.Visible : Visibility.Collapsed;

            if (mode == DndSnooze && _settings.DndUntil.HasValue && _settings.DndUntil.Value > DateTime.Now)
                DndHintText.Text = $"Đang tạm tắt đến {_settings.DndUntil.Value:HH:mm}";
            else if (mode == DndSnooze)
                DndHintText.Text = "Tạm tắt 1 giờ kể từ khi bấm Lưu cài đặt";
            else
                DndHintText.Text = "Tắt popup và âm thanh; chấm đỏ vẫn hiện";
        }

        private static bool TryNormalizeTime(string? input, out string normalized)
        {
            normalized = string.Empty;
            if (TimeSpan.TryParseExact(input?.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t) &&
                t < TimeSpan.FromDays(1))
            {
                normalized = t.ToString(@"hh\:mm");
                return true;
            }
            return false;
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
                    BackupStatusText.Text = "⏳ Đang tạm dừng các tài khoản và sao lưu...";
                    // Giai đoạn 5: Ngủ đông toàn bộ và chờ tiến trình WebView2 thoát hẳn để mở khóa file Cookie/IndexedDB
                    await _mainViewModel.SessionManager.HibernateAllSessionsAndWaitAsync(_mainViewModel.Accounts);
                    var result = await _mainViewModel.BackupRestoreService.BackupWorkspaceAsync(dlg.FileName);
                    _mainViewModel.RefreshStatusMetrics();
                    BackupStatusText.Text = result.SkippedFiles.Count == 0
                        ? $"✅ Đã sao lưu vào: {dlg.FileName}"
                        : $"⚠️ Đã sao lưu vào: {dlg.FileName}. Có {result.SkippedFiles.Count} file đang được dùng nên bị bỏ qua.";
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"❌ Không sao lưu được: {ex.Message}";
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
                    BackupStatusText.Text = "⏳ Đang tạm dừng các tài khoản và khôi phục...";
                    await _mainViewModel.SessionManager.HibernateAllSessionsAndWaitAsync(_mainViewModel.Accounts);
                    await _mainViewModel.BackupRestoreService.RestoreWorkspaceAsync(dlg.FileName);
                    _mainViewModel.DatabaseService.RunMigrations();
                    _mainViewModel.LoadInitialData();
                    BackupStatusText.Text = "✅ Đã khôi phục. Danh sách tài khoản đã được cập nhật.";
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"❌ Không khôi phục được (dữ liệu cũ vẫn giữ nguyên): {ex.Message}";
                }
                finally
                {
                    IsEnabled = true;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // --- Kiểm tra dữ liệu trước khi lưu ---
            string newPin = PinTextBox.Password?.Trim() ?? string.Empty;
            if (newPin.Length > 0 && newPin.Length < 4)
            {
                TabSecurity.IsChecked = true;
                MessageBox.Show(this, "Mã PIN phải có ít nhất 4 ký tự.", "Mã PIN không hợp lệ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int dndMode = DndModeCombo.SelectedIndex;
            string dndStart = _settings.DndStart, dndEnd = _settings.DndEnd;
            if (dndMode == DndSchedule &&
                (!TryNormalizeTime(DndStartTextBox.Text, out dndStart) || !TryNormalizeTime(DndEndTextBox.Text, out dndEnd)))
            {
                TabNotify.IsChecked = true;
                MessageBox.Show(this, "Giờ Không làm phiền phải theo dạng HH:mm, ví dụ 22:00 và 07:00.",
                    "Giờ không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // --- Chung ---
            _settings.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;
            _settings.ShowTrayHintOnClose = TrayHintSwitch.IsChecked == true;

            // --- Thông báo ---
            _settings.ShowToastPopup = ToastPopupSwitch.IsChecked == true;
            _settings.HideMessageContent = HideContentSwitch.IsChecked == true;
            _settings.ShowInAppBanner = InAppBannerSwitch.IsChecked == true;
            _settings.PlaySound = SoundSwitch.IsChecked == true;
            _settings.ShowUnreadBadge = BadgeSwitch.IsChecked == true;
            _settings.IsDndScheduleEnabled = dndMode == DndSchedule;
            if (dndMode == DndSchedule)
            {
                _settings.DndStart = dndStart;
                _settings.DndEnd = dndEnd;
            }
            if (dndMode == DndSnooze)
            {
                // Giữ nguyên mốc cũ nếu đang tạm tắt, ngược lại tạm tắt 1 giờ kể từ bây giờ
                if (!(_settings.DndUntil.HasValue && _settings.DndUntil.Value > DateTime.Now))
                    _settings.DndUntil = DateTime.Now.AddHours(1);
            }
            else
            {
                _settings.DndUntil = null;
            }

            // --- Bảo mật ---
            if (newPin.Length > 0)
                _mainViewModel.DatabaseService.SetMasterPin(_settings, newPin);
            _settings.AutoLockIdleMinutes = int.TryParse(IdleMinutesTextBox.Text.Trim(), out int m) && m > 0 ? m : 15;
            _settings.IsAutoLockEnabled = AutoLockCheckBox.IsChecked == true;

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
