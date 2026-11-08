using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Services
{
    /// <summary>
    /// Phím tắt toàn hệ thống (dùng được cả khi ứng dụng ẩn dưới khay) qua RegisterHotKey của Windows.
    /// Nếu chương trình khác (vd Zalo PC) đã giữ tổ hợp phím thì Windows từ chối -> báo cho người dùng.
    /// </summary>
    public sealed class GlobalHotkeyService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
        private const int ProbeId = 0xBF00;

        private HwndSource? _source;
        private readonly Dictionary<HotkeyAction, HotkeyGesture> _registered = new();

        public event Action<HotkeyAction>? HotkeyPressed;

        public void Attach(Window window)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (Enum.IsDefined(typeof(HotkeyAction), id))
                {
                    handled = true;
                    HotkeyPressed?.Invoke((HotkeyAction)id);
                }
            }
            return IntPtr.Zero;
        }

        private static uint ToNative(ModifierKeys m)
        {
            uint r = MOD_NOREPEAT;
            if (m.HasFlag(ModifierKeys.Alt)) r |= MOD_ALT;
            if (m.HasFlag(ModifierKeys.Control)) r |= MOD_CONTROL;
            if (m.HasFlag(ModifierKeys.Shift)) r |= MOD_SHIFT;
            return r;
        }

        private bool TryRegister(int id, HotkeyGesture g)
        {
            if (_source == null || g.IsEmpty)
                return false;
            return RegisterHotKey(_source.Handle, id, ToNative(g.Modifiers), (uint)KeyInterop.VirtualKeyFromKey(g.Key));
        }

        /// <summary>Đăng ký lại các phím toàn hệ thống theo cài đặt. Trả về các chức năng không đăng ký được.</summary>
        public List<HotkeyAction> Apply(AppSettings settings)
        {
            var failed = new List<HotkeyAction>();
            if (_source == null)
                return failed;
            foreach (var action in _registered.Keys)
                UnregisterHotKey(_source.Handle, (int)action);
            _registered.Clear();

            foreach (var info in HotkeyDefaults.All)
            {
                if (!info.IsGlobal)
                    continue;
                var g = settings.GetHotkey(info.Action);
                if (g.IsEmpty)
                    continue;
                if (TryRegister((int)info.Action, g))
                    _registered[info.Action] = g;
                else
                    failed.Add(info.Action);
            }
            return failed;
        }

        /// <summary>
        /// Tổ hợp phím có đăng ký được không (chưa bị chương trình khác giữ).
        /// Phím ứng dụng đang giữ cho đúng chức năng này coi như dùng được.
        /// </summary>
        public bool IsAvailable(HotkeyGesture g, HotkeyAction forAction)
        {
            if (g.IsEmpty || _source == null)
                return true;
            if (_registered.TryGetValue(forAction, out var current) && current == g)
                return true;
            foreach (var kv in _registered)
                if (kv.Value == g)
                    return true; // ứng dụng đang giữ cho chức năng khác: phần kiểm tra trùng sẽ báo
            if (!TryRegister(ProbeId, g))
                return false;
            UnregisterHotKey(_source.Handle, ProbeId);
            return true;
        }

        /// <summary>Tạm bỏ đăng ký mọi phím (khi đang ghi phím mới trong Cài đặt). Gọi Apply để đăng ký lại.</summary>
        public void Suspend()
        {
            if (_source == null)
                return;
            foreach (var action in _registered.Keys)
                UnregisterHotKey(_source.Handle, (int)action);
            _registered.Clear();
        }

        public void Dispose()
        {
            if (_source == null)
                return;
            foreach (var action in _registered.Keys)
                UnregisterHotKey(_source.Handle, (int)action);
            _registered.Clear();
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
