using System;
using System.Threading;
using System.Windows;
using ZaloMultiWorkspace.Services;
using ZaloMultiWorkspace.Views;

namespace ZaloMultiWorkspace
{
    public partial class App : Application
    {
        private const string MutexName = @"Local\ZaloMultiWorkspace_SingleInstance";
        private const string ActivateEventName = @"Local\ZaloMultiWorkspace_Activate";

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _activateEvent;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Chỉ cho chạy 1 instance: 2 instance cùng mở một thư mục Profile sẽ gây khóa dữ liệu WebView2
            _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(ActivateEventName);
                    existing.Set(); // Yêu cầu instance đang chạy hiện cửa sổ lên
                }
                catch
                {
                    // Instance cũ chưa sẵn sàng
                }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            // Phần 5: Dọn dẹp các tiến trình msedgewebview2.exe mồ côi khi khởi động
            WebViewSessionManager.CleanupOrphanProcesses();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var listener = new Thread(() =>
            {
                while (_activateEvent != null)
                {
                    try
                    {
                        _activateEvent.WaitOne();
                    }
                    catch
                    {
                        return;
                    }
                    Dispatcher.BeginInvoke(() => mainWindow.RestoreFromTray());
                }
            })
            {
                IsBackground = true,
                Name = "SingleInstanceActivateListener"
            };
            listener.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch { }
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
