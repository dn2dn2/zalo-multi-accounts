using System;
using Microsoft.Win32;

namespace ZaloMultiWorkspace.Services
{
    /// <summary>
    /// Bật/tắt chạy cùng Windows cho người dùng hiện tại (HKCU\...\Run, không cần quyền quản trị).
    /// Ứng dụng được mở với tham số --minimized để chạy ngầm ở khay.
    /// </summary>
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ChatWorkspacePro";
        public const string MinimizedArg = "--minimized";

        public static void Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled)
                {
                    string? exe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exe))
                        return;
                    string command = $"\"{exe}\" {MinimizedArg}";
                    // Cập nhật nếu file exe đã được chuyển sang chỗ khác
                    if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(ValueName, command);
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // Không ghi được registry (chính sách công ty...) - bỏ qua
            }
        }
    }
}
