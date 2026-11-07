using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZaloMultiWorkspace.Models;
using ZaloMultiWorkspace.Services;

namespace ZaloMultiWorkspace.ViewModels
{
    public partial class AddAccountViewModel : ObservableObject
    {
        private readonly ProxyHealthService _proxyHealthService;
        public ZaloAccountProfile Profile { get; }
        public bool IsEditMode { get; }

        [ObservableProperty]
        private string _displayName;

        [ObservableProperty]
        private string _tagCategory;

        [ObservableProperty]
        private string _tagColor;

        [ObservableProperty]
        private string _proxyType;

        [ObservableProperty]
        private string _proxyHost;

        [ObservableProperty]
        private string _proxyPortText;

        [ObservableProperty]
        private string _proxyUser;

        [ObservableProperty]
        private string _proxyPass;

        [ObservableProperty]
        private int _autoHibernateMinutes;

        [ObservableProperty]
        private string _proxyTestStatus = "Chưa kiểm tra kết nối";

        [ObservableProperty]
        private bool _isTestingProxy = false;

        [ObservableProperty]
        private bool _isProxyTestSuccess = false;

        public Action<bool>? CloseAction { get; set; }

        /// <summary>Nền tảng chỉ chọn được khi thêm mới; dữ liệu đăng nhập không chuyển được giữa các nền tảng</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsZaloSelected))]
        [NotifyPropertyChangedFor(nameof(IsTelegramSelected))]
        private ChatPlatform _platform = ChatPlatform.Zalo;

        public bool IsZaloSelected => Platform == ChatPlatform.Zalo;
        public bool IsTelegramSelected => Platform == ChatPlatform.Telegram;
        public bool CanChangePlatform => !IsEditMode;

        [RelayCommand]
        private void SelectPlatform(string? platform)
        {
            if (IsEditMode || !Enum.TryParse(platform, out ChatPlatform p))
                return;
            Platform = p;
        }

        public AddAccountViewModel(ProxyHealthService proxyHealthService, ZaloAccountProfile? existingProfile = null)
        {
            _proxyHealthService = proxyHealthService;
            if (existingProfile != null)
            {
                IsEditMode = true;
                Profile = existingProfile;
            }
            else
            {
                IsEditMode = false;
                Profile = new ZaloAccountProfile();
            }

            _platform = Profile.Platform;
            _displayName = Profile.DisplayName;
            _tagCategory = Profile.TagCategory;
            _tagColor = string.IsNullOrWhiteSpace(Profile.TagColor) ? "#1E3A8A" : Profile.TagColor;
            _proxyType = string.IsNullOrWhiteSpace(Profile.ProxyType) ? "http" : Profile.ProxyType;
            _proxyHost = Profile.ProxyHost;
            _proxyPortText = Profile.ProxyPort > 0 ? Profile.ProxyPort.ToString() : string.Empty;
            _proxyUser = Profile.ProxyUser;
            _proxyPass = Profile.ProxyPass;
            _autoHibernateMinutes = Profile.AutoHibernateMinutes > 0 ? Profile.AutoHibernateMinutes : 30;
        }

        private void ApplyToProfile(ZaloAccountProfile target)
        {
            if (!IsEditMode)
                target.Platform = Platform;
            target.DisplayName = string.IsNullOrWhiteSpace(DisplayName)
                ? (Platform == ChatPlatform.Telegram ? "Tài khoản Telegram" : "Tài khoản Zalo")
                : DisplayName.Trim();
            target.TagCategory = string.IsNullOrWhiteSpace(TagCategory) ? "CSKH" : TagCategory.Trim();
            target.TagColor = string.IsNullOrWhiteSpace(TagColor) ? "#1E3A8A" : TagColor.Trim();
            target.ProxyType = string.IsNullOrWhiteSpace(ProxyType) ? "http" : ProxyType.Trim().ToLowerInvariant();
            target.ProxyHost = ProxyHost?.Trim() ?? string.Empty;
            target.ProxyPort = int.TryParse(ProxyPortText?.Trim(), out int p) ? p : 0;
            target.ProxyAddress = (!string.IsNullOrWhiteSpace(target.ProxyHost) && target.ProxyPort > 0)
                ? $"{target.ProxyHost}:{target.ProxyPort}"
                : string.Empty;
            target.ProxyUser = ProxyUser?.Trim() ?? string.Empty;
            target.ProxyPass = ProxyPass ?? string.Empty;
            target.AutoHibernateMinutes = AutoHibernateMinutes > 0 ? AutoHibernateMinutes : 30;
        }

        [RelayCommand]
        private async Task TestProxyAsync()
        {
            IsTestingProxy = true;
            ProxyTestStatus = "⏳ Đang kiểm tra kết nối…";

            var tempProfile = new ZaloAccountProfile();
            ApplyToProfile(tempProfile);
            if (tempProfile.IsUnsupportedSocks5Auth)
            {
                IsTestingProxy = false;
                IsProxyTestSuccess = false;
                ProxyTestStatus = "❌ Proxy SOCKS5 có tên đăng nhập/mật khẩu chưa được hỗ trợ. Hãy chọn http, hoặc xóa tên đăng nhập/mật khẩu nếu proxy xác thực theo IP.";
                return;
            }

            var result = await _proxyHealthService.CheckProxyAsync(tempProfile);
            IsTestingProxy = false;
            IsProxyTestSuccess = result.IsHealthy;

            if (result.IsHealthy)
            {
                Profile.PingMs = result.PingMs;
                ProxyTestStatus = tempProfile.HasProxy
                    ? $"✅ Proxy hoạt động tốt! Độ trễ: {result.PingMs} ms"
                    : $"✅ Mạng trực tiếp ổn định! Độ trễ: {result.PingMs} ms";
            }
            else
            {
                ProxyTestStatus = $"❌ {result.ErrorMessage}";
            }
        }

        [RelayCommand]
        private void Save()
        {
            var check = new ZaloAccountProfile();
            ApplyToProfile(check);
            if (check.IsUnsupportedSocks5Auth)
            {
                IsProxyTestSuccess = false;
                ProxyTestStatus = "❌ Proxy SOCKS5 có tên đăng nhập/mật khẩu chưa được hỗ trợ. Hãy chọn http, hoặc xóa tên đăng nhập/mật khẩu nếu proxy xác thực theo IP.";
                return;
            }

            ApplyToProfile(Profile);
            CloseAction?.Invoke(true);
        }

        [RelayCommand]
        private void Cancel()
        {
            CloseAction?.Invoke(false);
        }
    }
}

