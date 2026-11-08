using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Views.Dialogs
{
    /// <summary>Tab Chung: mục Chụp màn hình và Phím tắt (đổi phím bằng cách bấm vào ô rồi nhấn tổ hợp mới)</summary>
    public partial class SettingsBackupWindow
    {
        private sealed class HotkeyRow
        {
            public required HotkeyDefaults.Info Info { get; init; }
            public HotkeyGesture Value { get; set; }
            public required Button KeyButton { get; init; }
            public required TextBlock ErrorText { get; init; }
            public string? Error { get; set; }
        }

        private readonly List<HotkeyRow> _hotkeyRows = new();
        private HotkeyRow? _recording;
        private string _screenshotFolder = string.Empty;

        private MainWindow? HostWindow => Owner as MainWindow;

        private void InitCaptureAndHotkeys()
        {
            _screenshotFolder = _settings.ScreenshotFolder;
            UpdateScreenshotFolderText();

            foreach (var info in HotkeyDefaults.All)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                grid.RowDefinitions.Add(new RowDefinition());
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
                titleLine.Children.Add(new TextBlock { Text = info.Name, Style = (Style)FindResource("RowTitle") });
                var tag = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 0, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = info.IsGlobal ? "Toàn hệ thống" : "Trong ứng dụng", FontSize = 10 }
                };
                tag.SetResourceReference(Border.BackgroundProperty, info.IsGlobal ? "Brush.Accent.SoftBg" : "Brush.Bg.Control");
                ((TextBlock)tag.Child).SetResourceReference(TextBlock.ForegroundProperty, info.IsGlobal ? "Brush.Accent.Text" : "Brush.Text.Secondary");
                titleLine.Children.Add(tag);
                label.Children.Add(titleLine);
                label.Children.Add(new TextBlock { Text = info.Description, Style = (Style)FindResource("RowHint") });
                grid.Children.Add(label);

                var keyBtn = new Button
                {
                    Height = 32,
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Focusable = true
                };
                AutomationPropertiesHelper(keyBtn, "Phím tắt " + info.Name);
                Grid.SetColumn(keyBtn, 1);
                grid.Children.Add(keyBtn);

                var resetBtn = new Button
                {
                    Content = "",
                    FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"),
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = string.IsNullOrEmpty(info.Default) ? "Khôi phục mặc định (không đặt phím)" : $"Khôi phục mặc định ({info.Default})"
                };
                AutomationPropertiesHelper(resetBtn, "Khôi phục phím mặc định " + info.Name);
                Grid.SetColumn(resetBtn, 2);
                grid.Children.Add(resetBtn);

                var err = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
                err.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger.Text");
                Grid.SetRow(err, 1);
                Grid.SetColumnSpan(err, 3);
                grid.Children.Add(err);

                var row = new HotkeyRow { Info = info, Value = _settings.GetHotkey(info.Action), KeyButton = keyBtn, ErrorText = err };
                keyBtn.Click += (s, e) => StartRecording(row);
                keyBtn.LostKeyboardFocus += (s, e) => { if (_recording == row) StopRecording(); };
                resetBtn.Click += (s, e) =>
                {
                    StopRecording();
                    row.Value = HotkeyGesture.Parse(info.Default);
                    ValidateHotkeys();
                };
                _hotkeyRows.Add(row);

                HotkeyRows.Children.Add(new Border { Style = (Style)FindResource("SettingRow"), Child = grid });
            }

            PreviewKeyDown += HotkeyRecorder_PreviewKeyDown;
            Loaded += (s, e) => ValidateHotkeys();
            // Đóng hộp thoại (Lưu hoặc Đóng): đăng ký lại phím theo cài đặt đã lưu
            Closed += (s, e) => HostWindow?.ApplyHotkeys();
        }

        private static void AutomationPropertiesHelper(UIElement el, string name)
            => System.Windows.Automation.AutomationProperties.SetName(el, name);

        private void StartRecording(HotkeyRow row)
        {
            if (_recording == row)
                return;
            StopRecording();
            _recording = row;
            // Tạm bỏ phím toàn hệ thống để nhấn lại được chính tổ hợp đang dùng
            HostWindow?.GlobalHotkeys.Suspend();
            row.KeyButton.Focus();
            RefreshHotkeyButtons();
        }

        private void StopRecording()
        {
            if (_recording == null)
                return;
            _recording = null;
            RefreshHotkeyButtons();
        }

        private void HotkeyRecorder_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_recording is not HotkeyRow row)
                return;
            e.Handled = true;
            var key = HotkeyGesture.RealKey(e);
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return; // chờ phím chính
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                StopRecording();
                return;
            }
            if ((key == Key.Back || key == Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
            {
                row.Value = default;
                StopRecording();
                ValidateHotkeys();
                return;
            }
            var g = new HotkeyGesture(Keyboard.Modifiers, key);
            string? invalid = g.Validate();
            if (invalid != null)
            {
                row.Error = invalid;
                ShowRowError(row);
                return; // vẫn đang chờ, cho nhấn lại
            }
            row.Value = g;
            StopRecording();
            ValidateHotkeys();
        }

        /// <summary>Kiểm tra trùng giữa các chức năng và phím đã bị chương trình khác giữ</summary>
        private void ValidateHotkeys()
        {
            var host = HostWindow;
            foreach (var row in _hotkeyRows)
            {
                row.Error = null;
                if (row.Value.IsEmpty)
                    continue;
                row.Error = row.Value.Validate();
                if (row.Error != null)
                    continue;
                var dup = _hotkeyRows.FirstOrDefault(r => r != row && r.Value == row.Value);
                if (dup != null)
                {
                    row.Error = $"Trùng với \"{dup.Info.Name}\". Hãy chọn tổ hợp khác.";
                    continue;
                }
                if (row.Info.IsGlobal && host != null && !host.GlobalHotkeys.IsAvailable(row.Value, row.Info.Action))
                {
                    string suggest = row.Info.Action == HotkeyAction.CaptureHidden ? "Ctrl+Shift+S" : "Ctrl+Shift+A";
                    row.Error = $"{row.Value} đang được chương trình khác dùng (vd Zalo PC). Hãy chọn tổ hợp khác, ví dụ {suggest}.";
                }
            }
            RefreshHotkeyButtons();
        }

        private void RefreshHotkeyButtons()
        {
            foreach (var row in _hotkeyRows)
            {
                bool rec = _recording == row;
                row.KeyButton.Content = rec ? "Nhấn phím…" : row.Value.IsEmpty ? "Chưa đặt" : row.Value.ToString();
                if (rec)
                {
                    row.KeyButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x68, 0xFF));
                    row.KeyButton.SetResourceReference(Control.ForegroundProperty, "Brush.Accent.Text");
                }
                else if (row.Error != null)
                {
                    row.KeyButton.SetResourceReference(Control.BorderBrushProperty, "Brush.Danger.Text");
                    row.KeyButton.ClearValue(Control.ForegroundProperty);
                }
                else
                {
                    row.KeyButton.ClearValue(Control.BorderBrushProperty);
                    row.KeyButton.ClearValue(Control.ForegroundProperty);
                    if (row.Value.IsEmpty)
                        row.KeyButton.SetResourceReference(Control.ForegroundProperty, "Brush.Text.Muted");
                }
                ShowRowError(row);
            }
        }

        private static void ShowRowError(HotkeyRow row)
        {
            row.ErrorText.Text = row.Error ?? string.Empty;
            row.ErrorText.Visibility = string.IsNullOrEmpty(row.Error) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Gọi trong Lưu: false nếu còn phím lỗi (giữ hộp thoại mở)</summary>
        private bool TryApplyHotkeysToSettings()
        {
            StopRecording();
            ValidateHotkeys();
            var bad = _hotkeyRows.FirstOrDefault(r => r.Error != null);
            if (bad != null)
            {
                TabGeneral.IsChecked = true;
                bad.KeyButton.BringIntoView();
                MessageBox.Show(this, $"Phím tắt \"{bad.Info.Name}\": {bad.Error}", "Phím tắt chưa hợp lệ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            var dict = new Dictionary<string, string>();
            foreach (var row in _hotkeyRows)
            {
                // Chỉ lưu phím đã đổi khác mặc định -> sau này mặc định thay đổi vẫn tự áp dụng
                if (row.Value != HotkeyGesture.Parse(row.Info.Default))
                    dict[row.Info.Action.ToString()] = row.Value.ToString();
            }
            _settings.Hotkeys = dict;
            _settings.ScreenshotFolder = _screenshotFolder;
            return true;
        }

        // --- Thư mục lưu ảnh ---
        private void UpdateScreenshotFolderText()
        {
            var tmp = new AppSettings { ScreenshotFolder = _screenshotFolder };
            ScreenshotFolderText.Text = tmp.GetScreenshotFolder();
            ScreenshotFolderText.ToolTip = ScreenshotFolderText.Text;
        }

        private void ChooseScreenshotFolder_Click(object sender, RoutedEventArgs e)
        {
            var current = new AppSettings { ScreenshotFolder = _screenshotFolder }.GetScreenshotFolder();
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Chọn thư mục lưu ảnh chụp màn hình",
                InitialDirectory = Directory.Exists(current) ? current : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dlg.ShowDialog(this) == true)
            {
                _screenshotFolder = dlg.FolderName;
                UpdateScreenshotFolderText();
            }
        }

        private void OpenScreenshotFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = new AppSettings { ScreenshotFolder = _screenshotFolder }.GetScreenshotFolder();
            try
            {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
