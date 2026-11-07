using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;

namespace ZaloMultiWorkspace.Services
{
    /// <summary>
    /// Tạo icon khay hệ thống: icon gốc (app.ico) hoặc icon gốc + chấm đỏ báo tin chưa đọc (giống Zalo PC).
    /// Icon được vẽ đúng kích thước icon nhỏ của hệ thống (16px ở 100%, 20px ở 125%, 24px ở 150%...).
    /// </summary>
    public sealed class TrayBadgeService : IDisposable
    {
        private const int SM_CXSMICON = 49;
        private static readonly Color DotColor = Color.FromArgb(255, 229, 41, 62); // #E5293E

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private Icon? _normalIcon;
        private Icon? _badgeIcon;
        private readonly List<IntPtr> _handles = new();

        public Icon NormalIcon => _normalIcon ??= CreateIcon(withDot: false);

        public Icon BadgeIcon => _badgeIcon ??= CreateIcon(withDot: true);

        private static int IconSize
        {
            get
            {
                int size = GetSystemMetrics(SM_CXSMICON);
                return size > 0 ? size : 16;
            }
        }

        /// <summary>
        /// Đọc trực tiếp khung ảnh trong app.ico (các khung lưu dạng PNG) và chọn khung nhỏ nhất
        /// có kích thước >= minSize. Không dùng Icon.ToBitmap vì không giải mã ổn định khung PNG.
        /// </summary>
        private static Bitmap LoadIconFrame(int minSize)
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            byte[] data;
            using (var ms = new MemoryStream())
            {
                info.Stream.CopyTo(ms);
                info.Stream.Dispose();
                data = ms.ToArray();
            }

            int count = BitConverter.ToUInt16(data, 4);
            int bestIndex = -1, bestSize = int.MaxValue, largestIndex = 0, largestSize = 0;
            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                int w = data[entry] == 0 ? 256 : data[entry];
                if (w >= minSize && w < bestSize) { bestSize = w; bestIndex = i; }
                if (w > largestSize) { largestSize = w; largestIndex = i; }
            }
            if (bestIndex < 0) bestIndex = largestIndex;

            int e = 6 + bestIndex * 16;
            int length = BitConverter.ToInt32(data, e + 8);
            int offset = BitConverter.ToInt32(data, e + 12);

            bool isPng = data[offset] == 0x89 && data[offset + 1] == 0x50;
            if (isPng)
            {
                using var frame = new MemoryStream(data, offset, length);
                using var decoded = new Bitmap(frame);
                return new Bitmap(decoded); // tách khỏi stream
            }

            // Khung BMP cũ: để Windows giải mã qua Icon
            using var icoStream = new MemoryStream(data);
            using var icon = new Icon(icoStream, minSize, minSize);
            return icon.ToBitmap();
        }

        private Icon CreateIcon(bool withDot)
        {
            int size = IconSize;
            // Lấy khung lớn hơn (gấp đôi) rồi thu nhỏ để icon và chấm đỏ mượt hơn
            using var source = LoadIconFrame(size * 2);
            using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(source, new Rectangle(0, 0, size, size));

                if (withDot)
                {
                    // Chấm đỏ ở góc trên bên phải, đường kính ~38% icon (gần giống Zalo PC)
                    float d = (float)Math.Round(size * 0.38f);
                    float x = size - d;
                    float y = 0;

                    // Khoảng trong suốt quanh chấm để tách khỏi logo
                    float ring = Math.Max(1f, size / 16f);
                    using (var clear = new GraphicsPath())
                    {
                        clear.AddEllipse(x - ring, y - ring, d + ring * 2, d + ring * 2);
                        g.SetClip(clear);
                        g.Clear(Color.Transparent);
                        g.ResetClip();
                    }

                    using var brush = new SolidBrush(DotColor);
                    g.FillEllipse(brush, x, y, d, d);
                }
            }

            IntPtr handle = bmp.GetHicon();
            _handles.Add(handle);
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }

        public void Dispose()
        {
            _normalIcon?.Dispose();
            _badgeIcon?.Dispose();
            foreach (var h in _handles)
                DestroyIcon(h);
            _handles.Clear();
        }
    }
}
