using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace ZaloMultiWorkspace.Views
{
    public enum CaptureResult
    {
        Cancelled,
        Copied,
        Saved,
        SendToChat
    }

    /// <summary>
    /// Công cụ chụp màn hình kiểu Zalo PC: chụp toàn bộ màn hình (mọi màn hình) trước,
    /// rồi phủ một cửa sổ để người dùng kéo chọn vùng, vẽ chú thích, làm mờ và chọn Lưu / Sao chép / Gửi vào chat.
    /// Mọi toạ độ vẽ trong Surface là pixel của ảnh chụp, nên không lệch khi màn hình có tỉ lệ phóng to (DPI) khác nhau.
    /// </summary>
    public partial class ScreenCaptureWindow : Window
    {
        private enum Tool { None, Rect, Arrow, Pen, Text, Mosaic }
        private enum DragMode { None, NewSelection, Move, Resize, Draw }

        private static readonly Color[] Palette =
        {
            Color.FromRgb(0xE5, 0x29, 0x3E), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0x22, 0xC5, 0x5E),
            Color.FromRgb(0x00, 0x68, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x11, 0x11, 0x11)
        };
        private static readonly double[] StrokeWidths = { 2, 4, 7 };

        // Nhớ màu và độ dày đã chọn cho lần chụp sau
        private static Color _lastColor = Palette[0];
        private static int _lastWidthIndex = 1;

        private readonly BitmapSource _screen;
        private readonly Int32Rect _virtualRect; // vùng toàn bộ màn hình (toạ độ Windows)
        private readonly string _saveFolder;

        private Rect? _sel;            // vùng chọn, pixel ảnh
        private Tool _tool = Tool.None;
        private DragMode _drag = DragMode.None;
        private int _resizeHandle = -1;
        private Point _dragStart;
        private Rect _selAtDragStart;
        private bool _movedSinceDown;
        private UIElement? _current;
        private Point _drawStart;
        private TextBox? _editingText;
        private readonly List<UIElement> _annotations = new();
        private readonly Rectangle[] _handles = new Rectangle[8];
        private double _pxPerDip = 1; // số pixel ảnh trên 1 đơn vị WPF của cửa sổ
        private Color _color = _lastColor;
        private int _widthIndex = _lastWidthIndex;

        public CaptureResult Result { get; private set; } = CaptureResult.Cancelled;
        public BitmapSource? ResultImage { get; private set; }
        public string? SavedPath { get; private set; }

        private ScreenCaptureWindow(BitmapSource screen, Int32Rect virtualRect, string saveFolder, string sendHint)
        {
            InitializeComponent();
            _screen = screen;
            _virtualRect = virtualRect;
            _saveFolder = saveFolder;

            ScreenImage.Source = screen;
            Surface.Width = screen.PixelWidth;
            Surface.Height = screen.PixelHeight;
            AnnotCanvas.Width = screen.PixelWidth;
            AnnotCanvas.Height = screen.PixelHeight;
            SendHintText.Text = sendHint;

            for (int i = 0; i < 8; i++)
            {
                _handles[i] = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x68, 0xFF)), Stroke = Brushes.White, Visibility = Visibility.Collapsed };
                SelCanvas.Children.Add(_handles[i]);
            }
            BuildColorPopup();
            UpdateDim();

            // Đặt vị trí/kích thước theo đơn vị WPF ngay từ đầu (trước khi hiện) để phần vẽ khớp toàn màn hình
            var dpi = Application.Current?.MainWindow is Window mw ? VisualTreeHelper.GetDpi(mw) : new DpiScale(1, 1);
            Left = virtualRect.X / dpi.DpiScaleX;
            Top = virtualRect.Y / dpi.DpiScaleY;
            Width = virtualRect.Width / dpi.DpiScaleX;
            Height = virtualRect.Height / dpi.DpiScaleY;
            Loaded += (s, e) =>
            {
                Activate();
                Focus();
                Keyboard.Focus(this);
                PlaceHint();
            };
            SizeChanged += (s, e) =>
            {
                _pxPerDip = Root.ActualWidth > 0 ? _screen.PixelWidth / Root.ActualWidth : 1;
                PlaceHint();
                UpdateSelectionVisuals();
            };
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        /// <summary>Chụp toàn bộ màn hình rồi mở công cụ chọn vùng. Trả về null nếu không chụp được.</summary>
        public static ScreenCaptureWindow? Create(string saveFolder, string sendHint)
        {
            var rect = GetVirtualScreenRect();
            var bmp = CaptureScreen(rect);
            if (bmp == null)
                return null;
            return new ScreenCaptureWindow(bmp, rect, saveFolder, sendHint);
        }

        // ================= Chụp màn hình =================
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

        private static Int32Rect GetVirtualScreenRect()
            => new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

        private static BitmapSource? CaptureScreen(Int32Rect r)
        {
            if (r.Width <= 0 || r.Height <= 0)
                return null;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = CreateCompatibleBitmap(screenDc, r.Width, r.Height);
            IntPtr old = SelectObject(memDc, hBmp);
            try
            {
                if (!BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y, SRCCOPY | CAPTUREBLT))
                    return null;
                SelectObject(memDc, old);
                var src = Imaging.CreateBitmapSourceFromHBitmap(hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                // Đưa về 96 DPI để 1 pixel ảnh = 1 đơn vị khi hiển thị trong Surface
                var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
                var stride = r.Width * 4;
                var pixels = new byte[stride * r.Height];
                bmp.CopyPixels(pixels, stride, 0);
                var result = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
                result.Freeze();
                return result;
            }
            catch
            {
                return null;
            }
            finally
            {
                DeleteObject(hBmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>Màn hình đang có con trỏ chuột, theo pixel ảnh</summary>
        private Rect CurrentMonitorRect()
        {
            if (GetCursorPos(out var p))
            {
                var mon = MonitorFromPoint(p, 2 /*NEAREST*/);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
                {
                    var r = new Rect(mi.rcMonitor.Left - _virtualRect.X, mi.rcMonitor.Top - _virtualRect.Y,
                                     mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                    r.Intersect(new Rect(0, 0, _screen.PixelWidth, _screen.PixelHeight));
                    if (!r.IsEmpty && r.Width > 0)
                        return r;
                }
            }
            return new Rect(0, 0, _screen.PixelWidth, _screen.PixelHeight);
        }

        // ================= Vẽ vùng chọn =================
        private void UpdateDim()
        {
            var full = new RectangleGeometry(new Rect(0, 0, _screen.PixelWidth, _screen.PixelHeight));
            DimPath.Data = _sel is Rect s && s.Width > 0 && s.Height > 0
                ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(s))
                : full;
        }

        private Point[] HandlePoints(Rect s) => new[]
        {
            s.TopLeft, new Point(s.Left + s.Width / 2, s.Top), s.TopRight,
            new Point(s.Right, s.Top + s.Height / 2), s.BottomRight,
            new Point(s.Left + s.Width / 2, s.Bottom), s.BottomLeft,
            new Point(s.Left, s.Top + s.Height / 2)
        };

        private void UpdateSelectionVisuals()
        {
            UpdateDim();
            if (_sel is not Rect s)
            {
                SelBorder.Visibility = Visibility.Collapsed;
                foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
                SizeLabel.Visibility = Visibility.Collapsed;
                Toolbar.Visibility = Visibility.Collapsed;
                ColorPopup.Visibility = Visibility.Collapsed;
                AnnotCanvas.Clip = new RectangleGeometry(Rect.Empty);
                return;
            }

            double t = 1.5 * _pxPerDip;
            SelBorder.StrokeThickness = t;
            SelBorder.StrokeDashArray = new DoubleCollection { 4, 3 };
            Canvas.SetLeft(SelBorder, s.X - t / 2);
            Canvas.SetTop(SelBorder, s.Y - t / 2);
            SelBorder.Width = s.Width + t;
            SelBorder.Height = s.Height + t;
            SelBorder.Visibility = Visibility.Visible;
            AnnotCanvas.Clip = new RectangleGeometry(s);

            double hs = 7 * _pxPerDip;
            var pts = HandlePoints(s);
            for (int i = 0; i < 8; i++)
            {
                var h = _handles[i];
                h.Width = h.Height = hs;
                h.StrokeThickness = 1 * _pxPerDip;
                h.RadiusX = h.RadiusY = hs / 2;
                Canvas.SetLeft(h, pts[i].X - hs / 2);
                Canvas.SetTop(h, pts[i].Y - hs / 2);
                h.Visibility = Visibility.Visible;
            }

            // Nhãn kích thước + thanh công cụ (toạ độ cửa sổ)
            var tl = ToUi(s.TopLeft);
            var br = ToUi(s.BottomRight);
            SizeText.Text = $"{(int)Math.Round(s.Width)} × {(int)Math.Round(s.Height)}";
            SizeLabel.Visibility = Visibility.Visible;
            SizeLabel.UpdateLayout();
            Canvas.SetLeft(SizeLabel, Math.Max(2, tl.X));
            Canvas.SetTop(SizeLabel, tl.Y - SizeLabel.ActualHeight - 6 >= 0 ? tl.Y - SizeLabel.ActualHeight - 6 : tl.Y + 6);

            bool showToolbar = _drag != DragMode.NewSelection && _drag != DragMode.Resize && _drag != DragMode.Move;
            Toolbar.Visibility = showToolbar ? Visibility.Visible : Visibility.Collapsed;
            if (showToolbar)
            {
                Toolbar.UpdateLayout();
                double w = Toolbar.ActualWidth, h = Toolbar.ActualHeight;
                double top = br.Y + 8;
                if (top + h > Root.ActualHeight - 4)
                    top = tl.Y - h - 8 >= 4 ? tl.Y - h - 8 : br.Y - h - 8; // không đủ chỗ dưới -> đặt trên hoặc bên trong
                double left = Math.Max(4, Math.Min(Root.ActualWidth - w - 4, br.X - w));
                Canvas.SetLeft(Toolbar, left);
                Canvas.SetTop(Toolbar, top);
                PlaceColorPopup();
            }
        }

        private Point ToUi(Point surfacePt) => Surface.TranslatePoint(surfacePt, UiCanvas);

        private void PlaceHint()
        {
            HintBox.UpdateLayout();
            Canvas.SetLeft(HintBox, (Root.ActualWidth - HintBox.ActualWidth) / 2);
            Canvas.SetTop(HintBox, 18);
        }

        private int HitHandle(Point p)
        {
            if (_sel is not Rect s)
                return -1;
            double tol = 7 * _pxPerDip;
            var pts = HandlePoints(s);
            for (int i = 0; i < 8; i++)
                if (Math.Abs(p.X - pts[i].X) <= tol && Math.Abs(p.Y - pts[i].Y) <= tol)
                    return i;
            return -1;
        }

        private static readonly Cursor[] HandleCursors =
        {
            Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
            Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE
        };

        private Point Clamp(Point p) => new(
            Math.Max(0, Math.Min(_screen.PixelWidth, p.X)),
            Math.Max(0, Math.Min(_screen.PixelHeight, p.Y)));

        private static Rect RectFrom(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        // ================= Chuột =================
        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOverUi(e))
                return;
            CommitText();
            ColorPopup.Visibility = Visibility.Collapsed;
            var p = Clamp(e.GetPosition(Surface));
            _dragStart = p;
            _movedSinceDown = false;

            // Nhấp đúp trong vùng chọn = sao chép (giống Zalo PC)
            if (e.ClickCount == 2 && _sel is Rect sd && sd.Contains(p))
            {
                Copy_Click(this, new RoutedEventArgs());
                return;
            }

            int handle = HitHandle(p);
            if (handle >= 0)
            {
                _drag = DragMode.Resize;
                _resizeHandle = handle;
                _selAtDragStart = _sel!.Value;
            }
            else if (_sel is Rect s && s.Contains(p))
            {
                if (_tool == Tool.None)
                {
                    _drag = DragMode.Move;
                    _selAtDragStart = s;
                }
                else if (_tool == Tool.Text)
                {
                    StartText(p);
                    e.Handled = true;
                    return;
                }
                else
                {
                    StartDraw(p);
                }
            }
            else if (_annotations.Count == 0)
            {
                // Chọn vùng mới (khi chưa vẽ gì)
                _drag = DragMode.NewSelection;
                HintBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                return;
            }
            Root.CaptureMouse();
            e.Handled = true;
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            var p = Clamp(e.GetPosition(Surface));
            if (_drag == DragMode.None)
            {
                UpdateCursor(p, e);
                return;
            }
            if (!_movedSinceDown && (Math.Abs(p.X - _dragStart.X) > 2 * _pxPerDip || Math.Abs(p.Y - _dragStart.Y) > 2 * _pxPerDip))
                _movedSinceDown = true;

            switch (_drag)
            {
                case DragMode.NewSelection:
                    if (_movedSinceDown)
                    {
                        _sel = RectFrom(_dragStart, p);
                        UpdateSelectionVisuals();
                    }
                    break;
                case DragMode.Move:
                    {
                        var s = _selAtDragStart;
                        double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
                        double x = Math.Max(0, Math.Min(_screen.PixelWidth - s.Width, s.X + dx));
                        double y = Math.Max(0, Math.Min(_screen.PixelHeight - s.Height, s.Y + dy));
                        _sel = new Rect(x, y, s.Width, s.Height);
                        UpdateSelectionVisuals();
                        break;
                    }
                case DragMode.Resize:
                    {
                        var s = _selAtDragStart;
                        double l = s.Left, t = s.Top, r = s.Right, b = s.Bottom;
                        int h = _resizeHandle;
                        if (h is 0 or 6 or 7) l = p.X;
                        if (h is 2 or 3 or 4) r = p.X;
                        if (h is 0 or 1 or 2) t = p.Y;
                        if (h is 4 or 5 or 6) b = p.Y;
                        _sel = RectFrom(new Point(l, t), new Point(r, b));
                        UpdateSelectionVisuals();
                        break;
                    }
                case DragMode.Draw:
                    UpdateDraw(p);
                    break;
            }
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag == DragMode.None)
                return;
            var mode = _drag;
            _drag = DragMode.None;
            Root.ReleaseMouseCapture();

            if (mode == DragMode.NewSelection)
            {
                // Nhấp không kéo = chụp cả màn hình đang có con trỏ
                if (!_movedSinceDown || _sel is not Rect ns || ns.Width < 4 || ns.Height < 4)
                    _sel = CurrentMonitorRect();
                HintBox.Visibility = Visibility.Collapsed;
            }
            else if (mode == DragMode.Draw)
            {
                EndDraw();
            }
            if (_sel is Rect s && (s.Width < 1 || s.Height < 1))
                _sel = null;
            UpdateSelectionVisuals();
            UpdateCursor(Clamp(e.GetPosition(Surface)), e);
        }

        private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsOverUi(e))
                return;
            // Chuột phải: bỏ vùng chọn (nếu chưa vẽ gì), lần nữa thì thoát
            if (_sel != null && _annotations.Count == 0)
            {
                _sel = null;
                SetTool(Tool.None);
                HintBox.Visibility = Visibility.Visible;
                UpdateSelectionVisuals();
            }
            else
            {
                Finish(CaptureResult.Cancelled);
            }
        }

        private bool IsOverUi(MouseEventArgs e)
        {
            foreach (var el in new FrameworkElement[] { Toolbar, ColorPopup })
            {
                if (el.Visibility != Visibility.Visible)
                    continue;
                var pos = e.GetPosition(el);
                if (pos.X >= 0 && pos.Y >= 0 && pos.X <= el.ActualWidth && pos.Y <= el.ActualHeight)
                    return true;
            }
            return false;
        }

        private void UpdateCursor(Point p, MouseEventArgs e)
        {
            if (IsOverUi(e))
            {
                Root.Cursor = Cursors.Arrow;
                return;
            }
            int h = HitHandle(p);
            if (h >= 0)
                Root.Cursor = HandleCursors[h];
            else if (_sel is Rect s && s.Contains(p))
                Root.Cursor = _tool == Tool.None ? Cursors.SizeAll : _tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            else
                Root.Cursor = Cursors.Cross;
        }

        // ================= Công cụ vẽ =================
        private double StrokePx => StrokeWidths[_widthIndex] * _pxPerDip;
        private Brush ColorBrush => new SolidColorBrush(_color);

        private void StartDraw(Point p)
        {
            _drag = DragMode.Draw;
            _drawStart = p;
            switch (_tool)
            {
                case Tool.Rect:
                    _current = new Rectangle { Stroke = ColorBrush, StrokeThickness = StrokePx, StrokeLineJoin = PenLineJoin.Round };
                    break;
                case Tool.Arrow:
                    _current = new Path { Stroke = ColorBrush, Fill = ColorBrush, StrokeThickness = StrokePx, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round };
                    break;
                case Tool.Pen:
                    var line = new Polyline { Stroke = ColorBrush, StrokeThickness = StrokePx, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    line.Points.Add(p);
                    _current = line;
                    break;
                case Tool.Mosaic:
                    _current = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1 * _pxPerDip, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)) };
                    break;
            }
            if (_current != null)
                AnnotCanvas.Children.Add(_current);
        }

        private void UpdateDraw(Point p)
        {
            switch (_current)
            {
                case Polyline pl:
                    pl.Points.Add(p);
                    break;
                case Rectangle rc:
                    var r = RectFrom(_drawStart, p);
                    Canvas.SetLeft(rc, r.X);
                    Canvas.SetTop(rc, r.Y);
                    rc.Width = r.Width;
                    rc.Height = r.Height;
                    break;
                case Path path:
                    path.Data = ArrowGeometry(_drawStart, p, StrokePx);
                    break;
            }
        }

        private void EndDraw()
        {
            var el = _current;
            _current = null;
            if (el == null)
                return;

            if (!_movedSinceDown)
            {
                AnnotCanvas.Children.Remove(el);
                return;
            }

            if (_tool == Tool.Mosaic && el is Rectangle preview)
            {
                AnnotCanvas.Children.Remove(preview);
                var r = new Rect(Canvas.GetLeft(preview), Canvas.GetTop(preview), preview.Width, preview.Height);
                var mosaic = CreateMosaic(r);
                if (mosaic == null)
                    return;
                el = mosaic;
                AnnotCanvas.Children.Add(el);
            }
            _annotations.Add(el);
        }

        private static Geometry ArrowGeometry(Point a, Point b, double thickness)
        {
            var v = b - a;
            double len = v.Length;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (len < 1)
                    return g;
                v.Normalize();
                double headLen = Math.Max(12, thickness * 4.5);
                double headW = headLen * 0.6;
                var normal = new Vector(-v.Y, v.X);
                var basePt = b - v * Math.Min(headLen, len);
                ctx.BeginFigure(a, false, false);
                ctx.LineTo(basePt, true, true);
                ctx.BeginFigure(b, true, true);
                ctx.LineTo(basePt + normal * headW / 2, true, true);
                ctx.LineTo(basePt - normal * headW / 2, true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Làm mờ: thu nhỏ vùng ảnh rồi phóng to lại theo khối vuông (pixel hóa)</summary>
        private UIElement? CreateMosaic(Rect r)
        {
            r.Intersect(new Rect(0, 0, _screen.PixelWidth, _screen.PixelHeight));
            int x = (int)Math.Floor(r.X), y = (int)Math.Floor(r.Y);
            int w = (int)Math.Ceiling(r.Width), h = (int)Math.Ceiling(r.Height);
            w = Math.Min(w, _screen.PixelWidth - x);
            h = Math.Min(h, _screen.PixelHeight - y);
            if (w < 3 || h < 3)
                return null;
            double block = Math.Max(6, 9 * _pxPerDip);
            var crop = new CroppedBitmap(_screen, new Int32Rect(x, y, w, h));
            double sx = Math.Max(1.0 / w, 1.0 / block), sy = Math.Max(1.0 / h, 1.0 / block);
            var small = new TransformedBitmap(crop, new ScaleTransform(sx, sy));
            small.Freeze();
            var img = new Image { Source = small, Width = w, Height = h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, x);
            Canvas.SetTop(img, y);
            return img;
        }

        private void StartText(Point p)
        {
            CommitText();
            double fontSize = (StrokeWidths[_widthIndex] * 2 + 12) * _pxPerDip; // 16 / 20 / 26
            var tb = new TextBox
            {
                Style = null,
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, _color.R, _color.G, _color.B)),
                BorderThickness = new Thickness(1 * _pxPerDip),
                Foreground = ColorBrush,
                CaretBrush = ColorBrush,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
                MinWidth = 40 * _pxPerDip,
                Padding = new Thickness(2 * _pxPerDip),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap
            };
            Canvas.SetLeft(tb, p.X);
            Canvas.SetTop(tb, p.Y - fontSize * 0.7);
            AnnotCanvas.Children.Add(tb);
            _editingText = tb;
            tb.Loaded += (s, e) => { tb.Focus(); Keyboard.Focus(tb); };
        }

        /// <summary>Kết thúc ô nhập chữ: chuyển thành chữ cố định (bỏ nếu rỗng)</summary>
        private void CommitText()
        {
            var tb = _editingText;
            if (tb == null)
                return;
            _editingText = null;
            AnnotCanvas.Children.Remove(tb);
            string text = tb.Text.TrimEnd();
            if (text.Length == 0)
                return;
            var block = new TextBlock
            {
                Text = text,
                Foreground = tb.Foreground,
                FontSize = tb.FontSize,
                FontWeight = tb.FontWeight,
                FontFamily = tb.FontFamily,
                Padding = new Thickness(tb.Padding.Left + tb.BorderThickness.Left, tb.Padding.Top + tb.BorderThickness.Top, 0, 0)
            };
            Canvas.SetLeft(block, Canvas.GetLeft(tb));
            Canvas.SetTop(block, Canvas.GetTop(tb));
            AnnotCanvas.Children.Add(block);
            _annotations.Add(block);
            Keyboard.Focus(this);
        }

        private void SetTool(Tool tool)
        {
            CommitText();
            _tool = tool;
            ToolRect.Tag = tool == Tool.Rect ? "on" : null;
            ToolArrow.Tag = tool == Tool.Arrow ? "on" : null;
            ToolPen.Tag = tool == Tool.Pen ? "on" : null;
            ToolText.Tag = tool == Tool.Text ? "on" : null;
            ToolMosaic.Tag = tool == Tool.Mosaic ? "on" : null;
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            var tool = sender == ToolRect ? Tool.Rect
                : sender == ToolArrow ? Tool.Arrow
                : sender == ToolPen ? Tool.Pen
                : sender == ToolText ? Tool.Text
                : Tool.Mosaic;
            SetTool(_tool == tool ? Tool.None : tool); // bấm lại công cụ đang chọn = bỏ chọn (kéo để di chuyển vùng)
            ColorPopup.Visibility = Visibility.Collapsed;
        }

        private void BuildColorPopup()
        {
            for (int i = 0; i < StrokeWidths.Length; i++)
            {
                int idx = i;
                double d = 4 + i * 4;
                var btn = new Button
                {
                    Style = (Style)FindResource("CapToolButton"),
                    Width = 28,
                    Content = new Ellipse { Width = d, Height = d, Fill = Brushes.White },
                    ToolTip = idx == 0 ? "Nét mảnh" : idx == 1 ? "Nét vừa" : "Nét dày"
                };
                btn.Click += (s, e) => { _widthIndex = idx; _lastWidthIndex = idx; RefreshColorPopup(); };
                WidthPanel.Children.Add(btn);
            }
            foreach (var c in Palette)
            {
                var color = c;
                var btn = new Button { Style = (Style)FindResource("SwatchButton"), Background = new SolidColorBrush(color) };
                btn.Click += (s, e) =>
                {
                    _color = color;
                    _lastColor = color;
                    RefreshColorPopup();
                    ColorPopup.Visibility = Visibility.Collapsed;
                    if (_editingText != null)
                    {
                        _editingText.Foreground = ColorBrush;
                        _editingText.CaretBrush = ColorBrush;
                    }
                };
                SwatchPanel.Children.Add(btn);
            }
            RefreshColorPopup();
        }

        private void RefreshColorPopup()
        {
            ColorDot.Fill = ColorBrush;
            for (int i = 0; i < WidthPanel.Children.Count; i++)
                ((Button)WidthPanel.Children[i]).Tag = i == _widthIndex ? "on" : null;
            for (int i = 0; i < SwatchPanel.Children.Count; i++)
                ((Button)SwatchPanel.Children[i]).Tag = Palette[i] == _color ? "on" : null;
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.Visibility = ColorPopup.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            PlaceColorPopup();
        }

        private void PlaceColorPopup()
        {
            if (ColorPopup.Visibility != Visibility.Visible)
                return;
            ColorPopup.UpdateLayout();
            double tbTop = Canvas.GetTop(Toolbar), tbLeft = Canvas.GetLeft(Toolbar);
            var pos = ColorButton.TranslatePoint(new Point(0, 0), UiCanvas);
            double top = tbTop + Toolbar.ActualHeight + 4;
            if (top + ColorPopup.ActualHeight > Root.ActualHeight - 4)
                top = tbTop - ColorPopup.ActualHeight - 4;
            double left = Math.Max(4, Math.Min(Root.ActualWidth - ColorPopup.ActualWidth - 4, pos.X - 8));
            if (double.IsNaN(tbLeft)) left = pos.X;
            Canvas.SetLeft(ColorPopup, left);
            Canvas.SetTop(ColorPopup, top);
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

        private void Undo()
        {
            if (_editingText != null)
            {
                AnnotCanvas.Children.Remove(_editingText);
                _editingText = null;
                Keyboard.Focus(this);
                return;
            }
            if (_annotations.Count == 0)
                return;
            var last = _annotations[^1];
            _annotations.RemoveAt(_annotations.Count - 1);
            AnnotCanvas.Children.Remove(last);
        }

        // ================= Phím =================
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_editingText != null)
            {
                if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None))
                {
                    // Esc / Enter: xong ô chữ (Shift+Enter xuống dòng)
                    CommitText();
                    e.Handled = true;
                }
                return;
            }

            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Escape)
            {
                Finish(CaptureResult.Cancelled);
                e.Handled = true;
            }
            else if (_sel == null)
            {
                return;
            }
            else if (e.Key == Key.Enter)
            {
                Send_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.Z)
            {
                Undo();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.C)
            {
                Copy_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.S)
            {
                Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ================= Kết quả =================
        /// <summary>Ghép ảnh vùng chọn với các nét vẽ thành ảnh cuối</summary>
        private BitmapSource? RenderSelection()
        {
            CommitText();
            if (_sel is not Rect s)
                return null;
            int x = (int)Math.Round(s.X), y = (int)Math.Round(s.Y);
            int w = Math.Min((int)Math.Round(s.Width), _screen.PixelWidth - x);
            int h = Math.Min((int)Math.Round(s.Height), _screen.PixelHeight - y);
            if (w < 1 || h < 1)
                return null;

            var target = new Rect(0, 0, w, h);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(new CroppedBitmap(_screen, new Int32Rect(x, y, w, h)), target);
                if (_annotations.Count > 0)
                {
                    // Vẽ lại các nét trong một Canvas tạm, dịch theo vùng chọn
                    var layer = new Canvas { Width = w, Height = h, ClipToBounds = true };
                    foreach (var el in _annotations.ToList())
                    {
                        AnnotCanvas.Children.Remove(el);
                        layer.Children.Add(el);
                        if (el is Polyline pl)
                        {
                            var moved = new PointCollection(pl.Points.Select(pt => new Point(pt.X - x, pt.Y - y)));
                            pl.Points = moved;
                        }
                        else if (el is Path path && path.Data != null)
                        {
                            path.RenderTransform = new TranslateTransform(-x, -y);
                        }
                        else
                        {
                            Canvas.SetLeft(el, Canvas.GetLeft(el) - x);
                            Canvas.SetTop(el, Canvas.GetTop(el) - y);
                        }
                    }
                    layer.Measure(new Size(w, h));
                    layer.Arrange(target);
                    layer.UpdateLayout();
                    var rtbLayer = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtbLayer.Render(layer);
                    dc.DrawImage(rtbLayer, target);
                }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private void Finish(CaptureResult result, BitmapSource? image = null)
        {
            Result = result;
            ResultImage = image;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(CaptureResult.Cancelled);

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var img = RenderSelection();
            if (img == null)
                return;
            SetClipboardImage(img);
            Finish(CaptureResult.Copied, img);
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            var img = RenderSelection();
            if (img == null)
                return;
            Finish(CaptureResult.SendToChat, img);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var img = RenderSelection();
            if (img == null)
                return;
            try { Directory.CreateDirectory(_saveFolder); } catch { }
            var dlg = new SaveFileDialog
            {
                Title = "Lưu ảnh chụp màn hình",
                FileName = $"ChupManHinh_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                InitialDirectory = Directory.Exists(_saveFolder) ? _saveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Filter = "Ảnh PNG (*.png)|*.png|Ảnh JPG (*.jpg)|*.jpg",
                AddExtension = true
            };
            Topmost = false;
            bool ok = dlg.ShowDialog(this) == true;
            Topmost = true;
            if (!ok)
            {
                // Đã vẽ rồi thì giữ lại để người dùng chọn tiếp: đặt lại các nét vào màn hình
                RestoreAnnotationsAfterRender();
                return;
            }
            try
            {
                BitmapEncoder enc = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? new JpegBitmapEncoder { QualityLevel = 92 }
                    : new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(img));
                using var fs = File.Create(dlg.FileName);
                enc.Save(fs);
                SavedPath = dlg.FileName;
                Finish(CaptureResult.Saved, img);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không lưu được ảnh: " + ex.Message, "Lưu ảnh", MessageBoxButton.OK, MessageBoxImage.Warning);
                RestoreAnnotationsAfterRender();
            }
        }

        /// <summary>RenderSelection chuyển các nét sang lớp tạm; đưa chúng về lại màn hình khi người dùng chưa xong</summary>
        private void RestoreAnnotationsAfterRender()
        {
            if (_sel is not Rect s)
                return;
            int x = (int)Math.Round(s.X), y = (int)Math.Round(s.Y);
            foreach (var el in _annotations)
            {
                if (VisualTreeHelper.GetParent(el) is Canvas parent && parent != AnnotCanvas)
                    parent.Children.Remove(el);
                if (el is Polyline pl)
                    pl.Points = new PointCollection(pl.Points.Select(pt => new Point(pt.X + x, pt.Y + y)));
                else if (el is Path path)
                    path.RenderTransform = Transform.Identity;
                else
                {
                    Canvas.SetLeft(el, Canvas.GetLeft(el) + x);
                    Canvas.SetTop(el, Canvas.GetTop(el) + y);
                }
                if (!AnnotCanvas.Children.Contains(el))
                    AnnotCanvas.Children.Add(el);
            }
        }

        /// <summary>Clipboard đôi khi bị chương trình khác giữ -> thử lại vài lần</summary>
        public static bool SetClipboardImage(BitmapSource img)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetImage(img);
                    return true;
                }
                catch (COMException)
                {
                    System.Threading.Thread.Sleep(60);
                }
            }
            return false;
        }
    }
}
