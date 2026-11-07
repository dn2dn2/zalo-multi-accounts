using System.Windows;
using ZaloMultiWorkspace.Services;

namespace ZaloMultiWorkspace
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Phần 5: Dọn dẹp các tiến trình msedgewebview2.exe mồ côi khi khởi động
            WebViewSessionManager.CleanupOrphanProcesses();
        }
    }
}

