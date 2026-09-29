using System;
using System.Linq;
using System.Windows.Forms;

namespace ScreenToRTSP
{
    internal static class Program
    {
                static System.Threading.Mutex mutex = new System.Threading.Mutex(true, "{0FF32F1A-B829-450F-A9D4-ScreenToRTSP}");

        [STAThread]
        static void Main(string[] args)
        {
            if (!mutex.WaitOne(TimeSpan.Zero, true))
            {
                MessageBox.Show("O ScreenToRTSP já está em execução.\nPor favor, verifique a bandeja do sistema (perto do relógio).", "Screen to RTSP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.ThreadException += (sender, e) => {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                System.IO.File.WriteAllText(p, e.Exception.ToString());
                MessageBox.Show(e.Exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                System.IO.File.WriteAllText(p, e.ExceptionObject.ToString());
                MessageBox.Show(e.ExceptionObject.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try {
                ApplicationConfiguration.Initialize();
                
                if (!ScreenToRTSP.Core.DependencyManager.CheckAndDownloadDependencies())
                {
                    return;
                }

                var form = new Main();
            
                if (args.Contains("--minimized"))
                {
                    form.WindowState = FormWindowState.Minimized;
                    form.ShowInTaskbar = false;
                    form.Load += (s, e) => {
                        form.Hide();
                        form.StartAuto();
                    };
                }
                else if (args.Contains("--resume"))
                {
                    form.Load += (s, e) => {
                        form.StartClick();
                    };
                }

                Application.Run(form);
            } catch (Exception ex) {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                System.IO.File.WriteAllText(p, ex.ToString());
                MessageBox.Show(ex.Message, "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

