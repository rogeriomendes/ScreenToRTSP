using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using ScreenToRTSP.Core;
using ScreenToRTSP.UI;

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
                    string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                    File.WriteAllText(p, e.Exception.ToString());
                }
                catch { }
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            if (!DependencyManager.AreDependenciesInstalled())
            {
                var dlWindow = new DependencyDownloadWindow();
                bool ok = await dlWindow.RunDownloadAsync();
                if (!ok)
                {
                    Exit();
                    return;
                }
            }

            LaunchMainWindow();
        }

        private void LaunchMainWindow()
        {
            MainWindowInstance = new Main();

            bool startMinimized = StartupArgs.Contains("--minimized");
            bool startResume = StartupArgs.Contains("--resume");

            if (startMinimized)
            {
                MainWindowInstance.Activate();
                MainWindowInstance.StartAuto();
            }
            else
            {
                MainWindowInstance.Activate();
                if (startResume)
                {
                    MainWindowInstance.StartClick();
                }
            }
        }
    }
}
