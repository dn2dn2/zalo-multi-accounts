using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ZaloMultiWorkspace.Services
{
    public enum AppThemeMode
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>
    /// Quản lý giao diện Sáng/Tối: thay từ điển màu Themes/*.xaml tại Application.Resources (mọi
    /// {DynamicResource Brush.*} cập nhật tức thì), đổi màu thanh tiêu đề Windows và theo dõi
    /// thay đổi chế độ màu của Windows khi đang ở chế độ "Theo Windows".
    /// </summary>
    public static class ThemeService
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // Windows 10 trước bản 20H1

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static bool _initialized;

        public static AppThemeMode Mode { get; private set; } = AppThemeMode.System;

        /// <summary>Giao diện thực tế đang hiển thị (đã quy đổi từ chế độ "Theo Windows")</summary>
        public static bool IsDark { get; private set; } = true;

        /// <summary>Phát ra khi giao diện thực tế đổi (tham số: isDark)</summary>
        public static event Action<bool>? EffectiveThemeChanged;

        public static void Initialize(AppThemeMode mode)
        {
            if (!_initialized)
            {
                _initialized = true;
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

                // Áp màu thanh tiêu đề cho mọi cửa sổ (kể cả hộp thoại) ngay khi chúng được tạo
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((s, e) =>
                    {
                        if (s is Window w) ApplyTitleBar(w);
                    }));
            }
            Mode = mode;
            ApplyEffective(force: true);
        }

        public static void SetMode(AppThemeMode mode)
        {
            Mode = mode;
            ApplyEffective(force: false);
        }

        public static bool IsWindowsInDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key?.GetValue("AppsUseLightTheme") is int v)
                    return v == 0;
            }
            catch
            {
                // Không đọc được registry -> mặc định tối
            }
            return true;
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Mode != AppThemeMode.System)
                return;
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)
                return;

            Application.Current?.Dispatcher.BeginInvoke(() => ApplyEffective(force: false));
        }

        private static void ApplyEffective(bool force)
        {
            bool dark = Mode switch
            {
                AppThemeMode.Light => false,
                AppThemeMode.Dark => true,
                _ => IsWindowsInDarkMode()
            };

            if (!force && dark == IsDark)
                return;

            IsDark = dark;
            SwapDictionary(dark);

            if (Application.Current != null)
            {
                foreach (Window w in Application.Current.Windows)
                    ApplyTitleBar(w);
            }

            EffectiveThemeChanged?.Invoke(dark);
        }

        private static void SwapDictionary(bool dark)
        {
            var app = Application.Current;
            if (app == null)
                return;

            var merged = app.Resources.MergedDictionaries;
            var old = merged.FirstOrDefault(d => d.Source != null &&
                (d.Source.OriginalString.Contains("Themes/Dark", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.Contains("Themes/Light", StringComparison.OrdinalIgnoreCase)));

            var next = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
            };

            if (old != null)
            {
                int index = merged.IndexOf(old);
                merged[index] = next;
            }
            else
            {
                merged.Add(next);
            }
        }

        private static void ApplyTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                int value = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));
            }
            catch
            {
                // Windows cũ không hỗ trợ - bỏ qua
            }
        }
    }
}
