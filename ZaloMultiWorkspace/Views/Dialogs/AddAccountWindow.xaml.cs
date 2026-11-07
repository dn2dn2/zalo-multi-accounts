using System.Windows;
using ZaloMultiWorkspace.ViewModels;

namespace ZaloMultiWorkspace.Views.Dialogs
{
    public partial class AddAccountWindow : Window
    {
        public AddAccountWindow(AddAccountViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseAction = result =>
            {
                DialogResult = result;
                Close();
            };
        }
    }
}

