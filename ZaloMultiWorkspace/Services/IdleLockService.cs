using System;
using System.Runtime.InteropServices;

namespace ZaloMultiWorkspace.Services
{
    public static class IdleLockService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>
        /// Giai đoạn 5: Trả về thời gian hệ thống không có thao tác chuột/bàn phím tính bằng giây
        /// </summary>
        public static double GetSystemIdleSeconds()
        {
            var lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
            if (!GetLastInputInfo(ref lastInputInfo))
                return 0;

            uint elapsedTicks = (uint)Environment.TickCount - lastInputInfo.dwTime;
            return elapsedTicks / 1000.0;
        }
    }
}

