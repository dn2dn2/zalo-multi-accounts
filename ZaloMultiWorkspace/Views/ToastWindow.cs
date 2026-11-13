using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Views
{
    /// <summary>Yêu cầu hiện một thông báo tin nhắn</summary>
    public sealed record ToastRequest(ZaloAccountProfile Profile, string Sender, string Body, string? AvatarUrl, bool HideContent, bool IsTest = false);

    /// <summary>
    /// Thông báo tin nhắn kiểu Zalo PC ở góc phải dưới màn hình: avatar người gửi, tên in đậm, nội dung, tài khoản nhận.
    /// Một cửa sổ duy nhất chứa tối đa 3 thẻ xếp chồng; không lấy focus của cửa sổ đang dùng.
    /// Tự ẩn sau 6 giây (rê chuột vào thì dừng), bấm vào để mở đúng cuộc chat.
    /// </summary>
    public sealed class ToastWindow : Window
    {
        private const int MaxCards = 3;
        private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan AfterHover = TimeSpan.FromSeconds(2.5);

        private static readonly string[] AvatarColors =
            { "#2563EB", "#7C3AED", "#DB2777", "#EA580C", "#0F766E", "#4D7C0F", "#B45309", "#0369A1" };

        private sealed class Card
        {
            public required ToastRequest Request { get; init; }
            public required Border Root { get; init; }
            public required Ellipse AvatarImage { get; init; }
            public required TextBlock Initials { get; init; }
            public required ScaleTransform Progress { get; init; }
            public required DispatcherTimer Timer { get; init; }
            public bool HasImage { get; set; }
            public bool Closing { get; set; }
        }

        private readonly StackPanel _stack = new() { VerticalAlignment = VerticalAlignment.Bottom };
        private readonly List<Card> _cards = new();
        // Avatar trang chat gửi sang trước khi thẻ kịp hiện (thông báo "chung" hiện trễ ~0,7 giây)
        private readonly Dictionary<string, (ImageSource Image, DateTime At)> _recentAvatars = new();

        /// <summary>Người dùng bấm vào thẻ thông báo</summary>
        public event Action<ToastRequest>? Clicked;

        public ToastWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            Width = 372;
            SizeToContent = SizeToContent.Height;
            FontFamily = new FontFamily("Segoe UI");
            Title = "Thông báo Chat Workspace Pro";
            Content = _stack;
            SourceInitialized += (s, e) => MakeNoActivate();
            SizeChanged += (s, e) => PlaceBottomRight();
        }

        // ================= Không lấy focus, không hiện trong Alt+Tab =================
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
        private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

        private void MakeNoActivate()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        private void PlaceBottomRight()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 4;
            Top = wa.Bottom - ActualHeight - 4;
        }

        private static string Key(Guid profileId, string sender) => profileId + "|" + (sender ?? string.Empty).Trim().ToLowerInvariant();

        // ================= Hiện thông báo =================
        public void ShowToast(ToastRequest req)
        {
            while (_cards.Count(c => !c.Closing) >= MaxCards)
                Dismiss(_cards.First(c => !c.Closing), animate: false);

            var card = BuildCard(req);
            _cards.Add(card);
            _stack.Children.Add(card.Root);

            if (!IsVisible)
                Show();
            PlaceBottomRight();

            // Trượt vào từ bên phải
            var slide = (TranslateTransform)card.Root.RenderTransform;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            card.Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            StartCountdown(card, Lifetime);

            if (!req.HideContent)
            {
                // Avatar: lấy từ trang chat (đã gửi trước) hoặc tải qua proxy của tài khoản
                if (_recentAvatars.TryGetValue(Key(req.Profile.Id, req.Sender), out var recent) && DateTime.Now - recent.At < TimeSpan.FromSeconds(30))
                    SetAvatar(card, recent.Image);
                else if (!string.IsNullOrWhiteSpace(req.AvatarUrl) && req.AvatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    _ = FetchAvatarLaterAsync(card);
            }
        }

        private async System.Threading.Tasks.Task FetchAvatarLaterAsync(Card card)
        {
            // Chờ trang chat tự gửi ảnh (nhanh hơn, đọc được cả ảnh riêng tư); không có thì tự tải qua proxy
            await System.Threading.Tasks.Task.Delay(1500);
            if (card.HasImage || card.Closing)
                return;
            var img = await Services.AvatarFetcher.FetchAsync(card.Request.Profile, card.Request.AvatarUrl);
            if (img != null && !card.HasImage)
                SetAvatar(card, img);
        }

        /// <summary>Trang chat gửi avatar người gửi: cập nhật thẻ đang hiện và nhớ cho thẻ sắp hiện</summary>
        public void ApplyAvatar(ZaloAccountProfile profile, string sender, ImageSource image)
        {
            string key = Key(profile.Id, sender);
            _recentAvatars[key] = (image, DateTime.Now);
            foreach (var old in _recentAvatars.Where(kv => DateTime.Now - kv.Value.At > TimeSpan.FromMinutes(2)).Select(kv => kv.Key).ToList())
                _recentAvatars.Remove(old);
            foreach (var card in _cards.Where(c => !c.Request.HideContent && Key(c.Request.Profile.Id, c.Request.Sender) == key))
                SetAvatar(card, image);
        }

        private static void SetAvatar(Card card, ImageSource image)
        {
            card.AvatarImage.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            card.Initials.Visibility = Visibility.Collapsed;
            card.HasImage = true;
        }

        // ================= Thẻ thông báo =================
        private Card BuildCard(ToastRequest req)
        {
            var p = req.Profile;
            string name = req.HideContent ? $"Tin nhắn {p.PlatformName} mới" : req.Sender;
            string body = req.HideContent ? "Bạn có tin nhắn mới" : req.Body;

            // --- Avatar: ảnh hoặc chữ cái đầu, kèm huy hiệu nền tảng ---
            var avatarGrid = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
            var circle = new Ellipse { Fill = (Brush)new BrushConverter().ConvertFromString(PickColor(name))! };
            var initials = new TextBlock
            {
                Text = InitialsOf(name),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -3, -3),
                Background = (Brush)new BrushConverter().ConvertFromString(p.PlatformColor)!,
                Child = new TextBlock
                {
                    Text = p.PlatformGlyph,
                    Foreground = Brushes.White,
                    FontSize = 8,
                    FontWeight = p.IsGlyphIcon ? FontWeights.Normal : FontWeights.Bold,
                    FontFamily = p.IsGlyphIcon ? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") : new FontFamily("Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            badge.SetResourceReference(Border.BorderBrushProperty, "Brush.Bg.Panel");
            avatarGrid.Children.Add(circle);
            avatarGrid.Children.Add(initials);
            avatarGrid.Children.Add(badge);

            // --- Chữ ---
            var nameText = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Primary");
            var bodyText = new TextBlock
            {
                Text = body,
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36 // tối đa 2 dòng
            };
            bodyText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Secondary");
            var accountText = new TextBlock { Text = $"{p.PlatformName} · {p.DisplayName}", FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            accountText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Muted");
            var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(nameText);
            texts.Children.Add(bodyText);
            texts.Children.Add(accountText);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(avatarGrid);
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            // --- Nút đóng ---
            var close = new Button
            {
                Content = "",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10,
                Width = 22,
                Height = 22,
                Cursor = Cursors.Hand,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -24, 0),
                ToolTip = "Đóng",
                Template = CloseButtonTemplate()
            };
            close.SetResourceReference(Control.ForegroundProperty, "Brush.Text.Muted");

            // --- Vạch thời gian ---
            var progress = new ScaleTransform(1, 1);
            var bar = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -18, -8),
                Background = (Brush)new BrushConverter().ConvertFromString(p.PlatformColor)!,
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = progress,
                Opacity = 0.8
            };

            var content = new Grid();
            content.Children.Add(row);
            content.Children.Add(close);
            content.Children.Add(bar);

            var root = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 12, 30, 12),
                Margin = new Thickness(12, 5, 12, 7),
                Cursor = Cursors.Hand,
                Child = content,
                RenderTransform = new TranslateTransform(),
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.28, Direction = 270 }
            };
            root.SetResourceReference(Border.BackgroundProperty, "Brush.Bg.Panel");
            root.SetResourceReference(Border.BorderBrushProperty, "Brush.Border.Subtle");
            System.Windows.Automation.AutomationProperties.SetName(root, $"Thông báo: {name}");

            var card = new Card
            {
                Request = req,
                Root = root,
                AvatarImage = circle,
                Initials = initials,
                Progress = progress,
                Timer = new DispatcherTimer()
            };
            card.Timer.Tick += (s, e) => Dismiss(card, animate: true);

            close.Click += (s, e) => { e.Handled = true; Dismiss(card, animate: true); };
            root.MouseLeftButtonUp += (s, e) =>
            {
                if (card.Closing)
                    return;
                Dismiss(card, animate: true);
                Clicked?.Invoke(req);
            };
            // Rê chuột vào: dừng tự ẩn; rời chuột: ẩn sau 2,5 giây
            root.MouseEnter += (s, e) =>
            {
                card.Timer.Stop();
                card.Progress.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                card.Progress.ScaleX = 1;
            };
            root.MouseLeave += (s, e) => { if (!card.Closing) StartCountdown(card, AfterHover); };
            return card;
        }

        private static ControlTemplate CloseButtonTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), "bd"));
            t.Triggers.Add(hover);
            return t;
        }

        private static void StartCountdown(Card card, TimeSpan duration)
        {
            card.Timer.Stop();
            card.Timer.Interval = duration;
            card.Timer.Start();
            card.Progress.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, duration));
        }

        private void Dismiss(Card card, bool animate)
        {
            if (card.Closing)
                return;
            card.Closing = true;
            card.Timer.Stop();

            void Remove()
            {
                _stack.Children.Remove(card.Root);
                _cards.Remove(card);
                if (_cards.Count == 0)
                    Hide();
                else
                    PlaceBottomRight();
            }

            if (!animate)
            {
                Remove();
                return;
            }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (s, e) => Remove();
            ((TranslateTransform)card.Root.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(200)));
            card.Root.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>Đóng hết thông báo (vd khi khóa ứng dụng)</summary>
        public void DismissAll()
        {
            foreach (var c in _cards.ToList())
                Dismiss(c, animate: false);
        }

        private static string InitialsOf(string name)
        {
            var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).ToArray();
            if (words.Length == 0)
                return "?";
            string s = words.Length == 1 ? words[0][..1] : $"{words[^2][0]}{words[^1][0]}";
            return s.ToUpperInvariant();
        }

        private static string PickColor(string name)
        {
            int h = 0;
            foreach (char c in name ?? string.Empty)
                h = unchecked(h * 31 + c);
            return AvatarColors[Math.Abs(h % AvatarColors.Length)];
        }
    }
}
