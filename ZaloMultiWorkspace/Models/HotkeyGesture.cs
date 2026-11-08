using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace ZaloMultiWorkspace.Models
{
    /// <summary>
    /// Một tổ hợp phím tắt (vd "Ctrl+Alt+A"), lưu trong cài đặt dưới dạng chuỗi.
    /// Dùng cho cả phím toàn hệ thống (RegisterHotKey) và phím trong ứng dụng / trong trang chat.
    /// </summary>
    public readonly struct HotkeyGesture : IEquatable<HotkeyGesture>
    {
        public ModifierKeys Modifiers { get; }
        public Key Key { get; }

        public HotkeyGesture(ModifierKeys modifiers, Key key)
        {
            Modifiers = modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
            Key = key;
        }

        public bool IsEmpty => Key == Key.None;

        private static readonly Dictionary<Key, string> SpecialNames = new()
        {
            [Key.Snapshot] = "PrtSc",
            [Key.Insert] = "Insert",
            [Key.Delete] = "Delete",
            [Key.Home] = "Home",
            [Key.End] = "End",
            [Key.PageUp] = "PageUp",
            [Key.PageDown] = "PageDown",
        };

        private static readonly Dictionary<Key, string> JsCodes = new()
        {
            [Key.Snapshot] = "PrintScreen",
            [Key.Insert] = "Insert",
            [Key.Delete] = "Delete",
            [Key.Home] = "Home",
            [Key.End] = "End",
            [Key.PageUp] = "PageUp",
            [Key.PageDown] = "PageDown",
        };

        public static bool IsFunctionKey(Key k) => k >= Key.F1 && k <= Key.F24;
        private static bool IsLetter(Key k) => k >= Key.A && k <= Key.Z;
        private static bool IsDigit(Key k) => k >= Key.D0 && k <= Key.D9;

        /// <summary>Phím chính có được phép dùng làm phím tắt không (chữ, số, F1–F24, PrtSc, Insert…)</summary>
        public static bool IsSupportedKey(Key k) => IsLetter(k) || IsDigit(k) || IsFunctionKey(k) || SpecialNames.ContainsKey(k);

        /// <summary>
        /// Lý do tổ hợp không hợp lệ, null nếu hợp lệ. Phím thường phải kèm Ctrl hoặc Alt
        /// (Shift + chữ sẽ chặn mất việc gõ chữ hoa); F1–F24 và PrtSc được dùng một mình.
        /// </summary>
        public string? Validate()
        {
            if (IsEmpty)
                return null;
            if (!IsSupportedKey(Key))
                return "Phím này không dùng làm phím tắt được. Hãy dùng chữ, số hoặc F1–F12.";
            bool hasCtrlAlt = (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;
            if (!hasCtrlAlt && !IsFunctionKey(Key) && Key != Key.Snapshot)
                return "Cần kèm Ctrl hoặc Alt (riêng F1–F12 được dùng một mình).";
            return null;
        }

        public override string ToString()
        {
            if (IsEmpty)
                return string.Empty;
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }

        private static string KeyName(Key k)
        {
            if (IsDigit(k)) return ((int)(k - Key.D0)).ToString();
            if (SpecialNames.TryGetValue(k, out var name)) return name;
            return k.ToString();
        }

        public static HotkeyGesture Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return default;
            var mods = ModifierKeys.None;
            Key key = Key.None;
            foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModifierKeys.Control; continue;
                    case "alt": mods |= ModifierKeys.Alt; continue;
                    case "shift": mods |= ModifierKeys.Shift; continue;
                }
                if (raw.Length == 1 && char.IsDigit(raw[0]))
                    key = Key.D0 + (raw[0] - '0');
                else if (SpecialNames.FirstOrDefault(kv => string.Equals(kv.Value, raw, StringComparison.OrdinalIgnoreCase)) is { Value: not null } kv)
                    key = kv.Key;
                else if (Enum.TryParse(raw, true, out Key parsed))
                    key = parsed;
            }
            return key == Key.None ? default : new HotkeyGesture(mods, key);
        }

        /// <summary>Phím thật của sự kiện bàn phím WPF (Alt+phím đến dưới dạng Key.System)</summary>
        public static Key RealKey(KeyEventArgs e) => e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };

        public bool Matches(KeyEventArgs e)
        {
            if (IsEmpty)
                return false;
            var mods = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
            return RealKey(e) == Key && mods == Modifiers;
        }

        /// <summary>Đối tượng JS { ctrl, alt, shift, code } để so với KeyboardEvent.code trong trang chat</summary>
        public string ToJsObject()
        {
            if (IsEmpty)
                return "null";
            string code = IsLetter(Key) ? "Key" + Key
                : IsDigit(Key) ? "Digit" + (int)(Key - Key.D0)
                : JsCodes.TryGetValue(Key, out var c) ? c
                : Key.ToString();
            var mods = Modifiers;
            string b(ModifierKeys m) => mods.HasFlag(m) ? "true" : "false";
            return $"{{ ctrl: {b(ModifierKeys.Control)}, alt: {b(ModifierKeys.Alt)}, shift: {b(ModifierKeys.Shift)}, code: '{code}' }}";
        }

        public bool Equals(HotkeyGesture other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object? obj) => obj is HotkeyGesture g && Equals(g);
        public override int GetHashCode() => HashCode.Combine(Modifiers, Key);
        public static bool operator ==(HotkeyGesture a, HotkeyGesture b) => a.Equals(b);
        public static bool operator !=(HotkeyGesture a, HotkeyGesture b) => !a.Equals(b);
    }

    /// <summary>Các chức năng có phím tắt đổi được trong Cài đặt</summary>
    public enum HotkeyAction
    {
        Capture = 1,
        CaptureHidden = 2,
        ToggleApp = 3,
        Translate = 4,
        Reload = 5,
    }

    public static class HotkeyDefaults
    {
        public sealed record Info(HotkeyAction Action, string Name, string Description, bool IsGlobal, string Default);

        public static readonly IReadOnlyList<Info> All = new[]
        {
            new Info(HotkeyAction.Capture, "Chụp màn hình", "Chụp cả cửa sổ ứng dụng", true, "Ctrl+Alt+A"),
            new Info(HotkeyAction.CaptureHidden, "Chụp ẩn ứng dụng", "Ẩn ứng dụng rồi mới chụp", true, "Ctrl+Alt+S"),
            new Info(HotkeyAction.ToggleApp, "Hiện / ẩn ứng dụng", "Gọi cửa sổ lên từ khay", true, ""),
            new Info(HotkeyAction.Translate, "Dịch đoạn đã chọn", "Bôi đen trong khung chat rồi bấm", false, "Ctrl+Shift+D"),
            new Info(HotkeyAction.Reload, "Tải lại tài khoản", "Tải lại trang chat đang mở", false, "F5"),
        };

        public static Info Get(HotkeyAction a) => All.First(i => i.Action == a);
    }
}
