using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ScreenToRTSP.Core;

namespace ScreenToRTSP
{
    internal static class Program
    {
        private static Mutex mutex;

        [ModuleInitializer]
        internal static void InitSingleFileEnvironment()
        {
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        }

        [STAThread]
        static void Main(string[] args)
        {
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            mutex = new Mutex(true, "{0FF32F1A-B829-450F-A9D4-ScreenToRTSP}");
            if (!mutex.WaitOne(TimeSpan.Zero, true))
            {
                MonitorHelper.ShowMessage(
                    "O ScreenToRTSP já está em execução.\nPor favor, verifique a bandeja do sistema (perto do relógio).",
                    "Screen to RTSP",
                    MonitorHelper.MB_ICONINFORMATION);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try
                {
                    string p = Path.Combine(MonitorHelper.AppDir, "crash.log");
                    File.WriteAllText(p, e.ExceptionObject.ToString());
                    MonitorHelper.ShowMessage(e.ExceptionObject.ToString(), "Error", MonitorHelper.MB_ICONERROR);
                }
                catch { }
            };

            try
            {
                App.StartupArgs = args ?? Array.Empty<string>();
                WinRT.ComWrappersSupport.InitializeComWrappers();

                Application.Start((p) =>
                {
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    _ = new App();
                });
            }
            catch (Exception ex)
            {
                string p = Path.Combine(MonitorHelper.AppDir, "crash.log");
                File.WriteAllText(p, ex.ToString());
                MonitorHelper.ShowMessage(ex.Message, "Fatal Error", MonitorHelper.MB_ICONERROR);
            }
        }
    }
}
