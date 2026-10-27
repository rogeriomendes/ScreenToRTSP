using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenToRTSP.Core;
using Windows.Graphics;

namespace ScreenToRTSP.UI
{
    public sealed partial class DependencyDownloadWindow : Window
    {
        public DependencyDownloadWindow()
        {
            this.InitializeComponent();
            ConfigureWindow();
        }

        private void ConfigureWindow()
        {
            try
            {
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

                appWindow.Resize(new SizeInt32(480, 230));
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico");
                if (File.Exists(icoPath))
                {
                    appWindow.SetIcon(icoPath);
                }

                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                    presenter.IsResizable = false;
                    presenter.IsAlwaysOnTop = true;
                }

                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    int x = (displayArea.WorkArea.Width - 480) / 2;
                    int y = (displayArea.WorkArea.Height - 230) / 2;
                    appWindow.Move(new PointInt32(x, y));
                }
            }
            catch { }
        }

        public async Task<bool> RunDownloadAsync(bool force = false)
        {
            this.Activate();

            var progress = new Progress<DependencyProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LblProgress.Text = p.StatusText;
                    PbDownload.IsIndeterminate = p.IsIndeterminate;
                    if (!p.IsIndeterminate)
                    {
                        PbDownload.Value = p.Percent;
                    }
                });
            });

            bool result = await DependencyManager.DownloadDependenciesAsync(force, progress);
            this.Close();
            return result;
        }
    }
}
