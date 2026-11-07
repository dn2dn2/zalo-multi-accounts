using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;

namespace ZaloMultiWorkspace.Models
{
    public partial class QuickReplyTemplate : ObservableObject
    {
        [BsonId]
        public Guid Id { get; set; } = Guid.NewGuid();

        [ObservableProperty]
        private string _shortcut = "/chao";

        [ObservableProperty]
        private string _title = "Chào khách mới";

        [ObservableProperty]
        private string _content = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasImage))]
        private string _imagePath = string.Empty;

        [BsonIgnore]
        public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);
    }
}

