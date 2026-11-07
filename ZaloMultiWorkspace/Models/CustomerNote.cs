using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;

namespace ZaloMultiWorkspace.Models
{
    public partial class CustomerNote : ObservableObject
    {
        [BsonId]
        public Guid Id { get; set; } = Guid.NewGuid();

        [ObservableProperty]
        private Guid _accountId;

        [ObservableProperty]
        private string _customerName = string.Empty;

        [ObservableProperty]
        private string _customerPhoneOrTag = string.Empty;

        [ObservableProperty]
        private string _noteContent = string.Empty;

        [ObservableProperty]
        private DateTime _updatedAt = DateTime.Now;

        /// <summary>Ghi chú thuộc về cuộc hội thoại đang mở trên Zalo Web</summary>
        [ObservableProperty]
        [property: BsonIgnore]
        private bool _isActiveChatMatch;
    }

    public enum AccountLayoutMode
    {
        Horizontal = 0,
        Vertical = 1
    }

    public class AppSettings
    {
        public const string DefaultPin = "1234";

        [BsonId]
        public int Id { get; set; } = 1;

        /// <summary>Trường cũ (PIN văn bản thô) - chỉ dùng để chuyển đổi dữ liệu, luôn để trống sau migration.</summary>
        public string MasterPin { get; set; } = string.Empty;
        public string MasterPinHash { get; set; } = string.Empty;
        public string MasterPinSalt { get; set; } = string.Empty;
        public bool IsUsingDefaultPin { get; set; } = true;
        public bool IsAutoLockEnabled { get; set; } = true;
        public int AutoLockIdleMinutes { get; set; } = 15;
        public bool MinimizeToTrayOnClose { get; set; } = true;

        /// <summary>Tự chạy ngầm ở khay khi đăng nhập Windows</summary>
        public bool StartWithWindows { get; set; } = false;

        /// <summary>Đã chuyển tài khoản đang dùng mặc định cũ "tự tạm dừng sau 30 phút" sang "không bao giờ"</summary>
        public bool AutoHibernateDefaultMigrated { get; set; } = false;

        /// <summary>Hiện thông báo "Ứng dụng vẫn chạy ở khay hệ thống" mỗi lần ẩn xuống khay (mặc định tắt)</summary>
        public bool ShowTrayHintOnClose { get; set; } = false;

        // --- Thông báo (mặc định giữ nguyên hành vi trước đây) ---
        /// <summary>Popup góc màn hình (balloon/toast của Windows)</summary>
        public bool ShowToastPopup { get; set; } = true;
        /// <summary>Ẩn tên người gửi và nội dung tin trong popup (khi chia sẻ màn hình)</summary>
        public bool HideMessageContent { get; set; } = false;
        /// <summary>Thanh thông báo xanh "Xem ngay" trong ứng dụng</summary>
        public bool ShowInAppBanner { get; set; } = true;
        public bool PlaySound { get; set; } = true;
        /// <summary>Chấm đỏ ở icon khay và badge số ở nút taskbar</summary>
        public bool ShowUnreadBadge { get; set; } = true;

        /// <summary>Không làm phiền theo khung giờ (tắt popup và âm thanh)</summary>
        public bool IsDndScheduleEnabled { get; set; } = false;
        public string DndStart { get; set; } = "22:00";
        public string DndEnd { get; set; } = "07:00";
        /// <summary>Tạm tắt popup và âm thanh tới thời điểm này</summary>
        public DateTime? DndUntil { get; set; }

        public bool IsInDoNotDisturb(DateTime now)
        {
            if (DndUntil.HasValue && DndUntil.Value > now)
                return true;
            if (!IsDndScheduleEnabled)
                return false;
            if (!TimeSpan.TryParse(DndStart, out var start) || !TimeSpan.TryParse(DndEnd, out var end))
                return false;
            var t = now.TimeOfDay;
            // Khung giờ qua đêm (vd 22:00 - 07:00) hoặc trong ngày (vd 12:00 - 13:30)
            return start <= end ? (t >= start && t < end) : (t >= start || t < end);
        }

        /// <summary>Bố cục danh sách tài khoản: Horizontal (tab ngang, mặc định) / Vertical (thanh dọc bên trái)</summary>
        public AccountLayoutMode AccountLayout { get; set; } = AccountLayoutMode.Horizontal;

        // --- Dịch tin nhắn ---
        public Services.TranslationProvider TranslateProvider { get; set; } = Services.TranslationProvider.GoogleFree;
        /// <summary>Khóa API đã mã hóa DPAPI (dùng SecurityHelper.Protect/Unprotect)</summary>
        public string GeminiApiKeyProtected { get; set; } = string.Empty;
        public string DeepSeekApiKeyProtected { get; set; } = string.Empty;
        public string GeminiModel { get; set; } = Services.TranslationService.DefaultGeminiModel;
        public string DeepSeekModel { get; set; } = Services.TranslationService.DefaultDeepSeekModel;
        // --- Dịch bằng AI qua API URL ---
        /// <summary>Nhà cung cấp đang chọn (DeepSeek, Gemini, OpenAI, OpenRouter, Custom)</summary>
        public string AiPresetId { get; set; } = "DeepSeek";
        public string AiApiUrl { get; set; } = string.Empty;
        public string AiModel { get; set; } = string.Empty;
        /// <summary>Khóa API theo từng nhà cung cấp (đã mã hóa DPAPI)</summary>
        public Dictionary<string, string> AiKeysProtected { get; set; } = new();
        /// <summary>Đã chuyển cấu hình Gemini/DeepSeek của bản trước sang AI theo URL</summary>
        public bool AiConfigMigrated { get; set; } = false;

        /// <summary>Prompt dịch cho AI (rỗng = dùng prompt mặc định)</summary>
        public string TranslatePrompt { get; set; } = string.Empty;
        /// <summary>Ngôn ngữ ghim để chọn nhanh khi soạn tin gửi khách</summary>
        public List<string> PinnedLanguages { get; set; } = new() { "en", "zh-CN", "vi" };
        /// <summary>Ngôn ngữ dùng lần trước cho từng cuộc chat: khóa "accountId|tên cuộc chat"</summary>
        public Dictionary<string, string> ChatTargetLanguages { get; set; } = new();

        /// <summary>Chế độ giao diện: System (theo Windows, mặc định) / Light / Dark</summary>
        public Services.AppThemeMode ThemeMode { get; set; } = Services.AppThemeMode.System;
    }
}

