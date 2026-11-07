using System.Windows;
using Microsoft.Win32;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Views.Dialogs
{
    public partial class QuickReplyWindow : Window
    {
        public QuickReplyTemplate QuickReplyItem { get; }

        public QuickReplyWindow(QuickReplyTemplate? existing = null)
        {
            InitializeComponent();
            QuickReplyItem = existing ?? new QuickReplyTemplate();
            ShortcutTextBox.Text = QuickReplyItem.Shortcut;
            TitleTextBox.Text = QuickReplyItem.Title;
            ContentTextBox.Text = QuickReplyItem.Content;
            ImagePathTextBox.Text = QuickReplyItem.ImagePath;
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn hình ảnh đính kèm tin nhắn mẫu",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog() == true)
            {
                ImagePathTextBox.Text = dlg.FileName;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string shortcut = ShortcutTextBox.Text.Trim();
            if (string.IsNullOrEmpty(shortcut))
                shortcut = "/mau";
            if (!shortcut.StartsWith("/"))
                shortcut = "/" + shortcut;

            QuickReplyItem.Shortcut = shortcut;
            QuickReplyItem.Title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "Tin nhắn mẫu" : TitleTextBox.Text.Trim();
            QuickReplyItem.Content = ContentTextBox.Text.Trim();
            QuickReplyItem.ImagePath = ImagePathTextBox.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
