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
    }

    public class AppSettings
    {
        [BsonId]
        public int Id { get; set; } = 1;

        public string MasterPin { get; set; } = "1234";
        public bool IsAutoLockEnabled { get; set; } = true;
        public int AutoLockIdleMinutes { get; set; } = 15;
        public bool MinimizeToTrayOnClose { get; set; } = true;
    }
}

