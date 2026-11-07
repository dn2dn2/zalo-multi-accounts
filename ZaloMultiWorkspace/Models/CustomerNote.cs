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

        /// <summary>Chế độ giao diện: System (theo Windows, mặc định) / Light / Dark</summary>
        public Services.AppThemeMode ThemeMode { get; set; } = Services.AppThemeMode.System;
    }
}

