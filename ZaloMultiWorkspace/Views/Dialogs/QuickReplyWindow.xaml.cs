using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Views.Dialogs
{
    public partial class QuickReplyWindow : Window
    {
        private readonly IReadOnlyCollection<QuickReplyTemplate> _existingTemplates;

        public QuickReplyTemplate QuickReplyItem { get; }
        public bool IsEditMode { get; }

        /// <param name="existing">Tin mẫu cần sửa (null = thêm mới)</param>
        /// <param name="allTemplates">Danh sách tin mẫu hiện có để kiểm tra trùng phím tắt</param>
        public QuickReplyWindow(QuickReplyTemplate? existing = null, IEnumerable<QuickReplyTemplate>? allTemplates = null)
        {
            InitializeComponent();
            _existingTemplates = allTemplates?.ToList() ?? new List<QuickReplyTemplate>();
            IsEditMode = existing != null;
            QuickReplyItem = existing ?? new QuickReplyTemplate { Shortcut = string.Empty, Title = string.Empty };

            if (IsEditMode)
            {
                Title = "Sửa tin nhắn mẫu";
                HeaderText.Text = "SỬA TIN NHẮN MẪU";
                SaveButton.Content = "Lưu thay đổi";
            }
            else
            {
                Title = "Thêm tin nhắn mẫu";
            }

            ShortcutTextBox.Text = QuickReplyItem.Shortcut;
            TitleTextBox.Text = QuickReplyItem.Title;
            ContentTextBox.Text = QuickReplyItem.Content;
            ImagePathTextBox.Text = QuickReplyItem.ImagePath;
            UpdateClearImageButton();

            ShortcutTextBox.TextChanged += (s, e) => HideError();
            ContentTextBox.TextChanged += (s, e) => HideError();
            Loaded += (s, e) =>
            {
                ShortcutTextBox.Focus();
                ShortcutTextBox.CaretIndex = ShortcutTextBox.Text.Length;
            };
        }

        private void UpdateClearImageButton()
        {
            ClearImageButton.Visibility = string.IsNullOrWhiteSpace(ImagePathTextBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorText.Visibility = Visibility.Collapsed;
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
                UpdateClearImageButton();
                HideError();
            }
        }

        private void ClearImage_Click(object sender, RoutedEventArgs e)
        {
            ImagePathTextBox.Text = string.Empty;
            UpdateClearImageButton();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string shortcut = ShortcutTextBox.Text.Trim();
            if (shortcut.Length > 0 && !shortcut.StartsWith("/"))
                shortcut = "/" + shortcut;

            if (shortcut.Length < 2)
            {
                ShowError("Nhập phím tắt, ví dụ /baogia.");
                ShortcutTextBox.Focus();
                return;
            }
            if (shortcut.Any(char.IsWhiteSpace))
            {
                ShowError("Phím tắt không được chứa khoảng trắng (khoảng trắng dùng để kích hoạt phím tắt khi gõ).");
                ShortcutTextBox.Focus();
                return;
            }

            var duplicate = _existingTemplates.FirstOrDefault(t =>
                t.Id != QuickReplyItem.Id &&
                string.Equals(t.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                ShowError($"Phím tắt {shortcut} đã được dùng cho tin mẫu \"{duplicate.Title}\". Hãy chọn phím tắt khác.");
                ShortcutTextBox.Focus();
                ShortcutTextBox.SelectAll();
                return;
            }

            string content = ContentTextBox.Text.Trim();
            string imagePath = ImagePathTextBox.Text.Trim();
            if (content.Length == 0 && imagePath.Length == 0)
            {
                ShowError("Nhập nội dung tin nhắn hoặc chọn ảnh đính kèm.");
                ContentTextBox.Focus();
                return;
            }

            QuickReplyItem.Shortcut = shortcut;
            QuickReplyItem.Title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? shortcut : TitleTextBox.Text.Trim();
            QuickReplyItem.Content = content;
            QuickReplyItem.ImagePath = imagePath;

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
