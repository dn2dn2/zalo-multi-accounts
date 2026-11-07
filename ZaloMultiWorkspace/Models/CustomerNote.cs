using System;
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

        /// <summary>Chế độ giao diện: System (theo Windows, mặc định) / Light / Dark</summary>
        public Services.AppThemeMode ThemeMode { get; set; } = Services.AppThemeMode.System;
    }
}

