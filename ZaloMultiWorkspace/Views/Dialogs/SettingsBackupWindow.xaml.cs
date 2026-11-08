using System;
using System.Linq;
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
            StartWithWindowsSwitch.IsChecked = _settings.StartWithWindows;
            TrayHintSwitch.IsChecked = _settings.ShowTrayHintOnClose;
            InitCaptureAndHotkeys();

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

            // Tab Dịch
            PromptBox.Text = string.IsNullOrWhiteSpace(_settings.TranslatePrompt)
                ? Services.TranslationService.DefaultPrompt
                : _settings.TranslatePrompt;
            foreach (var kv in _settings.AiKeysProtected)
                _aiKeys[kv.Key] = Services.SecurityHelper.Unprotect(kv.Value);
            _loadingAi = true;
            AiPresetCombo.ItemsSource = Services.TranslationService.AiPresets;
            AiPresetCombo.SelectedItem = Services.TranslationService.AiPresets.FirstOrDefault(p => p.Id == _settings.AiPresetId)
                                         ?? Services.TranslationService.AiPresets[0];
            _currentPresetId = ((Services.AiPreset)AiPresetCombo.SelectedItem).Id;
            AiUrlBox.Text = string.IsNullOrWhiteSpace(_settings.AiApiUrl) ? ((Services.AiPreset)AiPresetCombo.SelectedItem).Url : _settings.AiApiUrl;
            AiModelBox.Text = string.IsNullOrWhiteSpace(_settings.AiApiUrl) ? ((Services.AiPreset)AiPresetCombo.SelectedItem).Model : _settings.AiModel;
            AiKeyBox.Password = _aiKeys.TryGetValue(_currentPresetId, out var savedKey) ? savedKey : string.Empty;
            _loadingAi = false;
            UpdateAiUrlState();
            (_settings.TranslateProvider == Services.TranslationProvider.GoogleFree ? ProviderGoogle : ProviderAi).IsChecked = true;
            var langs = Services.TranslationService.Languages;
            var pins = _settings.PinnedLanguages;
            foreach (var (combo, index) in new[] { (Pin1Combo, 0), (Pin2Combo, 1), (Pin3Combo, 2) })
            {
                combo.ItemsSource = langs;
                string code = index < pins.Count ? pins[index] : (index == 0 ? "en" : index == 1 ? "zh-CN" : "vi");
                combo.SelectedItem = langs.FirstOrDefault(l => l.Code == code) ?? langs[0];
            }

            // Tab Thông tin: phiên bản và ngày build lấy từ chính file đang chạy
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            string version = asm.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
            string? buildDate = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
            AboutLogo.Source = LoadLargestIconFrame();
            VersionText.Text = string.IsNullOrEmpty(buildDate) ? $"Phiên bản {version}" : $"Phiên bản {version} · build {buildDate}";

            // Tab Bảo mật
            DefaultPinWarningText.Visibility = _settings.IsUsingDefaultPin ? Visibility.Visible : Visibility.Collapsed;
            IdleMinutesTextBox.Text = _settings.AutoLockIdleMinutes.ToString();
            AutoLockCheckBox.IsChecked = _settings.IsAutoLockEnabled;
        }

        // --- Cấu hình AI ---
        private readonly System.Collections.Generic.Dictionary<string, string> _aiKeys = new();
        private string _currentPresetId = "DeepSeek";
        private bool _loadingAi;

        private void Provider_Checked(object sender, RoutedEventArgs e)
        {
            if (ProviderHintText == null || AiPanel == null || PromptPanel == null)
                return;
            bool ai = (sender as FrameworkElement)?.Tag?.ToString() == "Ai";
            AiPanel.Visibility = ai ? Visibility.Visible : Visibility.Collapsed;
            PromptPanel.Visibility = ai ? Visibility.Visible : Visibility.Collapsed;
            TestStatusText.Text = string.Empty;
            ProviderHintText.Text = ai
                ? "Dịch theo ngữ cảnh bằng AI. Nhập API URL và API key của nhà cung cấp (Gemini, DeepSeek, OpenAI… hoặc dịch vụ tương thích OpenAI)."
                : "Không cần khóa, dùng được ngay. Đây là cổng không chính thức nên đôi khi có thể bị giới hạn.";
        }

        /// <summary>Đổi nhà cung cấp: nhớ khóa của nhà cung cấp cũ, điền URL/mô hình mẫu và khóa đã lưu của nhà cung cấp mới</summary>
        private void AiPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingAi || AiPresetCombo.SelectedItem is not Services.AiPreset preset)
                return;
            _aiKeys[_currentPresetId] = AiKeyBox.Password;
            _currentPresetId = preset.Id;
            if (preset.Id != "Custom")
            {
                AiUrlBox.Text = preset.Url;
                AiModelBox.Text = preset.Model;
            }
            AiKeyBox.Password = _aiKeys.TryGetValue(preset.Id, out var key) ? key : string.Empty;
            TestStatusText.Text = string.Empty;
            UpdateAiUrlState();
        }

        private void AiUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loadingAi)
                UpdateAiUrlState();
        }

        /// <summary>URL Gemini đã có tên mô hình -> khóa ô Mô hình và hiển thị mô hình lấy từ URL</summary>
        private void UpdateAiUrlState()
        {
            if (AiUrlBox == null || AiModelBox == null || AiUrlHint == null)
                return;
            string url = AiUrlBox.Text.Trim();
            string? modelInUrl = Services.TranslationService.IsGeminiNativeUrl(url)
                ? Services.TranslationService.ModelFromGeminiUrl(url)
                : null;
            if (modelInUrl != null)
            {
                AiModelBox.IsEnabled = false;
                AiModelBox.Text = modelInUrl;
                AiUrlHint.Text = $"Cổng riêng của Gemini. Mô hình lấy từ URL: {modelInUrl}.";
            }
            else
            {
                AiModelBox.IsEnabled = true;
                AiUrlHint.Text = url.Length == 0
                    ? "Nhập địa chỉ API của nhà cung cấp."
                    : Services.TranslationService.IsGeminiNativeUrl(url)
                        ? "Cổng riêng của Gemini. Nhập tên mô hình ở ô bên dưới."
                        : "Chuẩn tương thích OpenAI (tự thêm /chat/completions nếu thiếu). Nhập tên mô hình ở ô bên dưới.";
            }
            if (AiPresetHint != null && AiPresetCombo.SelectedItem is Services.AiPreset p)
            {
                AiPresetHint.Text = p.Id switch
                {
                    "Gemini" => "Lấy khóa tại aistudio.google.com",
                    "DeepSeek" => "Lấy khóa tại platform.deepseek.com",
                    "OpenAI" => "Lấy khóa tại platform.openai.com",
                    "OpenRouter" => "Lấy khóa tại openrouter.ai",
                    _ => "Dịch vụ tương thích OpenAI hoặc Gemini"
                };
            }
        }

        private Services.TranslationProvider SelectedProvider =>
            ProviderAi.IsChecked == true ? Services.TranslationProvider.Ai : Services.TranslationProvider.GoogleFree;

        /// <summary>Gửi thử một câu ngắn với URL, khóa, mô hình và prompt đang nhập</summary>
        private async void TestProvider_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string name = Services.TranslationService.ServiceNameFromUrl(AiUrlBox.Text);
            btn.IsEnabled = false;
            TestStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Text.Secondary");
            TestStatusText.Text = $"⏳ Đang kiểm tra {name}…";
            try
            {
                var r = await _mainViewModel.TranslationService.TranslateAsync(
                    "Thank you, we will deliver tomorrow.", "auto", "vi", Services.TranslationProvider.Ai,
                    AiUrlBox.Text, AiKeyBox.Password, AiModelBox.Text, PromptBox.Text);
                if (r.Success)
                {
                    TestStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Success.TextSoft");
                    TestStatusText.Text = $"✅ {name} hoạt động. Dịch thử: \"{r.Text}\"";
                }
                else
                {
                    TestStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger.Text");
                    TestStatusText.Text = "❌ " + r.Error;
                }
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        private void ResetPrompt_Click(object sender, RoutedEventArgs e)
        {
            PromptBox.Text = Services.TranslationService.DefaultPrompt;
            TestStatusText.Text = "Đã khôi phục prompt mặc định (bấm Lưu để áp dụng).";
        }

        private void AddElectricalPreset_Click(object sender, RoutedEventArgs e)
        {
            if (PromptBox.Text.Contains("Main Switchboard (MSB)"))
            {
                TestStatusText.Text = "Prompt đã có mẫu Thiết kế điện.";
                return;
            }
            PromptBox.Text = PromptBox.Text.TrimEnd() + "\n\n" + Services.TranslationService.ElectricalDesignPreset;
            PromptBox.ScrollToEnd();
            TestStatusText.Text = "Đã thêm mẫu Thiết kế điện vào cuối prompt. Sửa thuật ngữ theo cách công ty bạn dùng rồi bấm Lưu.";
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null)
                return; // đang khởi tạo
            PanelGeneral.Visibility = TabGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelNotify.Visibility = TabNotify.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelSecurity.Visibility = TabSecurity.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelBackup.Visibility = TabBackup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelTranslate.Visibility = TabTranslate.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = TabAbout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            TabContentScroller.ScrollToTop(); // đổi tab thì về đầu nội dung
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
                Title = "Chọn nơi lưu gói Sao lưu Chat Workspace Pro (.zip)",
                Filter = "Zip Archive (*.zip)|*.zip",
                FileName = $"ChatWorkspacePro_Backup_{DateTime.Now:yyyyMMdd_HHmm}.zip"
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
                Title = "Chọn file Sao lưu Chat Workspace Pro (.zip)",
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

            if (!TryApplyHotkeysToSettings())
                return;

            // --- Chung ---
            _settings.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;
            _settings.StartWithWindows = StartWithWindowsSwitch.IsChecked == true;
            Services.StartupService.Apply(_settings.StartWithWindows);
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

            // --- Dịch ---
            _settings.TranslateProvider = SelectedProvider;
            _aiKeys[_currentPresetId] = AiKeyBox.Password;
            _settings.AiPresetId = _currentPresetId;
            _settings.AiApiUrl = AiUrlBox.Text.Trim();
            _settings.AiModel = AiModelBox.Text.Trim();
            _settings.AiKeysProtected = _aiKeys
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => Services.SecurityHelper.Protect(kv.Value.Trim()));
            // Lưu rỗng nếu trùng mặc định -> sau này mặc định được cải tiến sẽ tự áp dụng
            _settings.TranslatePrompt = PromptBox.Text.Trim() == Services.TranslationService.DefaultPrompt.Trim()
                ? string.Empty
                : PromptBox.Text.Trim();
            _settings.PinnedLanguages = new[] { Pin1Combo, Pin2Combo, Pin3Combo }
                .Select(c => (c.SelectedItem as Services.LanguageInfo)?.Code)
                .Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).Distinct().ToList();

            // --- Bảo mật ---
            if (newPin.Length > 0)
                _mainViewModel.DatabaseService.SetMasterPin(_settings, newPin);
            _settings.AutoLockIdleMinutes = int.TryParse(IdleMinutesTextBox.Text.Trim(), out int m) && m > 0 ? m : 15;
            _settings.IsAutoLockEnabled = AutoLockCheckBox.IsChecked == true;

            _mainViewModel.DatabaseService.SaveSettings(_settings);
            _mainViewModel.LoadTranslationSettings();
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

        /// <summary>Đổi bố cục tài khoản áp dụng và lưu ngay, không cần bấm Lưu</summary>
        private void LayoutSegment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && Enum.TryParse(fe.Tag?.ToString(), out AccountLayoutMode mode))
            {
                _mainViewModel.SetAccountLayout(mode);
            }
        }

        /// <summary>
        /// Lấy khung ảnh lớn nhất trong app.ico (256px) để logo nét khi hiển thị 56px.
        /// Mặc định WPF có thể chọn khung nhỏ rồi phóng to nên bị mờ.
        /// </summary>
        private static System.Windows.Media.ImageSource? LoadLargestIconFrame()
        {
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
                using var stream = info.Stream;
                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var best = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                best.Freeze();
                return best;
            }
            catch
            {
                return null;
            }
        }

        private void CopyAbout_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string text)
            {
                try
                {
                    Clipboard.SetText(text);
                    AboutStatusText.Text = $"✓ Đã sao chép {text}";
                }
                catch
                {
                    AboutStatusText.Text = "Không sao chép được, thử lại.";
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                AboutStatusText.Text = "Máy chưa có ứng dụng email mặc định. Bấm nút sao chép để lấy địa chỉ email.";
            }
            e.Handled = true;
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Services.DatabaseService.BaseAppDir) { UseShellExecute = true });
            }
            catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
