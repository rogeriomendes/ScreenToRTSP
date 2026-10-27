using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ScreenToRTSP.Core;

namespace ScreenToRTSP
{
    public partial class App : Application
    {
        public static string[] StartupArgs { get; set; } = Array.Empty<string>();
        public static Main MainWindowInstance { get; private set; }

        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += (sender, e) =>
            {
                try
                {
                    string p = Path.Combine(MonitorHelper.AppDir, "crash.log");
                    File.WriteAllText(p, e.Exception?.ToString() + "\r\nMessage: " + e.Message);
                }
                catch { }
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq != null)
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dq));
            }

            MainWindowInstance = new Main();
            MainWindowInstance.Activate();
            _ = MainWindowInstance.InitializeStartupAsync(StartupArgs);
        }
    }
}
