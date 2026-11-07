using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;

namespace ZaloMultiWorkspace.Models
{
    public enum AccountConnectionStatus
    {
        Online,
        Hibernated,
        ProxyError,
        Connecting
    }

    public partial class ZaloAccountProfile : ObservableObject
    {
        [BsonId]
        public Guid Id { get; set; } = Guid.NewGuid();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Initials))]
        private string _displayName = "Tài khoản mới";

        [ObservableProperty]
        private string _tagCategory = "CSKH";

        [ObservableProperty]
        private string _tagColor = "#1E3A8A";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasProxy))]
        [NotifyPropertyChangedFor(nameof(ProxyDisplayText))]
        private string _proxyType = "http"; // http, socks5

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasProxy))]
        [NotifyPropertyChangedFor(nameof(ProxyDisplayText))]
        private string _proxyAddress = string.Empty; // host:port hoặc toàn bộ chuỗi

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasProxy))]
        [NotifyPropertyChangedFor(nameof(ProxyDisplayText))]
        private string _proxyHost = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasProxy))]
        [NotifyPropertyChangedFor(nameof(ProxyDisplayText))]
        private int _proxyPort = 0;

        [ObservableProperty]
        private string _proxyUser = string.Empty;

        // Mật khẩu Proxy chỉ giữ trong bộ nhớ; khi lưu DB dùng ProxyPassProtected (DPAPI)
        [ObservableProperty]
        [property: BsonIgnore]
        private string _proxyPass = string.Empty;

        [BsonField("ProxyPassProtected")]
        public string ProxyPassProtected
        {
            get => Services.SecurityHelper.Protect(ProxyPass);
            set => ProxyPass = Services.SecurityHelper.Unprotect(value);
        }

        /// <summary>
        /// Chromium/WebView2 không hỗ trợ xác thực User:Pass cho SOCKS5.
        /// </summary>
        [BsonIgnore]
        public bool IsUnsupportedSocks5Auth =>
            string.Equals(ProxyType, "socks5", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ProxyUser);

        /// <summary>
        /// Chuỗi đại diện cấu hình mạng, dùng để phát hiện thay đổi Proxy cần khởi động lại phiên.
        /// </summary>
        [BsonIgnore]
        public string ProxySignature => $"{ProxyType}|{ProxyHost}|{ProxyPort}|{ProxyAddress}|{ProxyUser}|{ProxyPass}";

        [BsonIgnore]
        [ObservableProperty]
        private string _activeChatName = string.Empty;

        [ObservableProperty]
        private int _autoHibernateMinutes = 30;

        public int SortOrder { get; set; } = 0;

        // --- Thuộc tính Runtime (Không lưu cứng trạng thái phiên tạm thời vào DB) ---

        [BsonIgnore]
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasUnread))]
        private int _unreadCount = 0;

        [BsonIgnore]
        [ObservableProperty]
        private bool _isHibernated = false;

        [BsonIgnore]
        [ObservableProperty]
        private AccountConnectionStatus _connectionStatus = AccountConnectionStatus.Hibernated;

        [BsonIgnore]
        [ObservableProperty]
        private double _ramUsageMb = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasAvatar))]
        [NotifyPropertyChangedFor(nameof(AvatarImageSource))]
        private string _avatarUrl = string.Empty;

        [ObservableProperty]
        private string _avatarRemoteUrl = string.Empty;

        [BsonIgnore]
        private ImageSource? _cachedAvatarImage;

        partial void OnAvatarUrlChanged(string value)
        {
            _cachedAvatarImage = CreateImageSource(value);
            OnPropertyChanged(nameof(AvatarImageSource));
            OnPropertyChanged(nameof(HasAvatar));
        }

        [BsonIgnore]
        public ImageSource? AvatarImageSource
        {
            get
            {
                if (_cachedAvatarImage == null && !string.IsNullOrWhiteSpace(AvatarUrl))
                {
                    _cachedAvatarImage = CreateImageSource(AvatarUrl);
                }
                return _cachedAvatarImage;
            }
        }

        private static ImageSource? CreateImageSource(string? urlOrDataUri)
        {
            if (string.IsNullOrWhiteSpace(urlOrDataUri))
                return null;

            try
            {
                if (urlOrDataUri.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    int commaIndex = urlOrDataUri.IndexOf(',');
                    if (commaIndex > 0)
                    {
                        string base64 = urlOrDataUri.Substring(commaIndex + 1);
                        byte[] bytes = Convert.FromBase64String(base64);
                        using var ms = new MemoryStream(bytes);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }
                else if (urlOrDataUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(urlOrDataUri, UriKind.Absolute);
                    bmp.EndInit();
                    return bmp;
                }
            }
            catch
            {
                // Fallback về chữ viết tắt nếu ảnh lỗi
            }
            return null;
        }

        [BsonIgnore]
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PingDisplayText))]
        private long _pingMs = -1;

        [BsonIgnore]
        [ObservableProperty]
        private bool _isSelected = false;

        [BsonIgnore]
        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [BsonIgnore]
        public DateTime LastInteractionTime { get; set; } = DateTime.Now;

        [BsonIgnore]
        public bool HasUnread => UnreadCount > 0;

        [BsonIgnore]
        public bool HasAvatar => AvatarImageSource != null;

        [BsonIgnore]
        public bool HasProxy => !string.IsNullOrWhiteSpace(ProxyHost) && ProxyPort > 0
                                || !string.IsNullOrWhiteSpace(ProxyAddress);

        [BsonIgnore]
        public string ProxyDisplayText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ProxyHost) && ProxyPort > 0)
                    return $"Proxy: {ProxyHost}:{ProxyPort}";
                if (!string.IsNullOrWhiteSpace(ProxyAddress))
                    return $"Proxy: {ProxyAddress}";
                return "Mạng trực tiếp (Direct IP)";
            }
        }

        [BsonIgnore]
        public string PingDisplayText => PingMs >= 0 ? $" ({PingMs}ms)" : string.Empty;

        [BsonIgnore]
        public string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DisplayName))
                    return "ZL";
                var parts = DisplayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    return parts[0].Length >= 3
                        ? parts[0].Substring(0, 3).ToUpperInvariant()
                        : parts[0].ToUpperInvariant();
                }
                var first = parts[0][0];
                var last = parts[^1];
                var suffix = new string(last.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(suffix))
                    return $"{char.ToUpperInvariant(first)}{suffix}";
                return $"{char.ToUpperInvariant(first)}{char.ToUpperInvariant(last[0])}";
            }
        }
    }
}

