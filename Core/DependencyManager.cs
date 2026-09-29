using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;

namespace ScreenToRTSP.Core
{
    public static class DependencyManager
    {
        public static bool HasUpdateAvailable { get; private set; } = false;
        public static bool HasMtxUpdate { get; private set; } = false;
        public static bool HasFfmpegUpdate { get; private set; } = false;
        public static string MtxLatestVersionStr { get; private set; } = "";
        public static string FfmpegLatestVersionStr { get; private set; } = "";

        public static bool CheckAndDownloadDependencies(bool force = false)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpeg = Path.Combine(appDir, "ffmpeg.exe");
            string mediamtx = Path.Combine(appDir, "mediamtx.exe");

            bool needFfmpeg = !File.Exists(ffmpeg) || (force && HasFfmpegUpdate);
            bool needMtx = !File.Exists(mediamtx) || (force && HasMtxUpdate);

            // Se forçam a atualização, mas nenhum dos dois precisa (e.g., clicou sem ter update?), fallback:
            if (force && !needFfmpeg && !needMtx) {
                needFfmpeg = true;
                needMtx = true;
            }

            if (!needFfmpeg && !needMtx)
            {
                return true;
            }

            Form dlForm = new Form
            {
                Text = "Screen to RTSP - Download",
                Size = new Size(450, 180),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowIcon = false,
                ShowInTaskbar = true,
                TopMost = true
            };

            Label lblStatus = new Label
            {
                Text = "Baixando componentes essenciais (FFmpeg e MediaMTX)...\nIsso ocorrerá apenas uma vez. Aguarde...",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 60,
                Font = new Font("Segoe UI", 10)
            };

            ProgressBar pb = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Dock = DockStyle.Bottom,
                Height = 30
            };

            Label lblProgress = new Label
            {
                Text = "Iniciando...",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 163, 74)
            };

            dlForm.Controls.Add(lblProgress);
            dlForm.Controls.Add(lblStatus);
            dlForm.Controls.Add(pb);

            bool success = false;

            dlForm.Shown += async (s, e) =>
            {
                try
                {
                    string mtxZip = Path.Combine(appDir, "mediamtx_temp.zip");
                    string ffmpeg7z = Path.Combine(appDir, "ffmpeg_temp.7z");

                    // Step 1: Downloads
                    if (needFfmpeg)
                    {
                        dlForm.Invoke((MethodInvoker)(() => lblProgress.Text = "Buscando link do FFmpeg..."));
                        string ffmpegUrl = await GetLatestReleaseAssetUrl("GyanD/codexffmpeg", "essentials_build.7z");
                        if (string.IsNullOrEmpty(ffmpegUrl)) throw new Exception("Não foi possível encontrar a última versão do FFmpeg.");

                        await DownloadFileWithProgress(ffmpegUrl, ffmpeg7z, dlForm, pb, lblProgress, "FFmpeg");
                    }

                    if (needMtx)
                    {
                        dlForm.Invoke((MethodInvoker)(() => lblProgress.Text = "Buscando link do MediaMTX..."));
                        string mtxUrl = await GetLatestReleaseAssetUrl("bluenviron/mediamtx", "windows_amd64.zip");
                        if (string.IsNullOrEmpty(mtxUrl)) throw new Exception("Não foi possível encontrar a última versão do MediaMTX.");

                        await DownloadFileWithProgress(mtxUrl, mtxZip, dlForm, pb, lblProgress, "MediaMTX");
                    }

                    // Step 2: Extractions
                    if (needMtx || needFfmpeg)
                    {
                        dlForm.Invoke((MethodInvoker)(() => { pb.Style = ProgressBarStyle.Marquee; }));
                    }

                    if (needFfmpeg)
                    {
                        dlForm.Invoke((MethodInvoker)(() => { lblProgress.Text = "Extraindo FFmpeg..."; }));
                        await Task.Run(() =>
                        {
                            using (var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(ffmpeg7z))
                            {
                                foreach (var entry in archive.Entries)
                                {
                                    if (!entry.IsDirectory && entry.Key.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        entry.WriteToFile(ffmpeg, new SharpCompress.Common.ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                                        break;
                                    }
                                }
                            }
                            File.Delete(ffmpeg7z);
                        });
                    }

                    if (needMtx)
                    {
                        dlForm.Invoke((MethodInvoker)(() => { lblProgress.Text = "Extraindo MediaMTX..."; }));
                        await Task.Run(() =>
                        {
                            using (var archive = ZipFile.OpenRead(mtxZip))
                            {
                                foreach (var entry in archive.Entries)
                                {
                                    if (entry.Name.Equals("mediamtx.exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        entry.ExtractToFile(mediamtx, true);
                                        break;
                                    }
                                }
                            }
                            File.Delete(mtxZip);
                        });
                    }

                    dlForm.Invoke((MethodInvoker)(() => pb.Style = ProgressBarStyle.Continuous));

                    success = true;
                    HasUpdateAvailable = false;
                    dlForm.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao baixar dependências: {ex.Message}\n\nVerifique sua conexão com a internet ou tente novamente mais tarde.", "Erro de Download", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    dlForm.Close();
                }
            };

            dlForm.ShowDialog();
            return success;
        }

        private static async Task<System.Text.Json.JsonElement> GetLatestRelease(string repoUrl)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "ScreenToRTSP");
                var response = await client.GetStringAsync($"https://api.github.com/repos/{repoUrl}/releases/latest");
                using (var doc = System.Text.Json.JsonDocument.Parse(response))
                {
                    return doc.RootElement.Clone();
                }
            }
        }

        private static async Task<string> GetLatestReleaseAssetUrl(string repoUrl, string assetNameFilter)
        {
            try
            {
                var root = await GetLatestRelease(repoUrl);
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString();
                        if (name != null && name.IndexOf(assetNameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return asset.GetProperty("browser_download_url").GetString();
                        }
                    }
                }
            } catch { }
            return null;
        }

        public static async Task<bool> CheckForNewVersionsAsync(bool throwErrors = false)
        {
            try
            {
                var mtxRelease = await GetLatestRelease("bluenviron/mediamtx");
                var ffmpegRelease = await GetLatestRelease("GyanD/codexffmpeg");

                string mtxLatest = mtxRelease.GetProperty("tag_name").GetString();
                string ffmpegLatest = ffmpegRelease.GetProperty("tag_name").GetString();

                MtxLatestVersionStr = mtxLatest ?? "";
                FfmpegLatestVersionStr = ffmpegLatest ?? "";

                string mtxCurrent = GetMediaMTXVersion();
                string ffmpegCurrent = GetFFmpegVersion();

                HasMtxUpdate = false;
                HasFfmpegUpdate = false;

                // Simple check if the latest tag is not present in the current version string
                if (!string.IsNullOrEmpty(mtxLatest) && mtxCurrent != "N/A" && !mtxCurrent.Contains(mtxLatest)) HasMtxUpdate = true;
                if (!string.IsNullOrEmpty(ffmpegLatest) && ffmpegCurrent != "N/A" && !ffmpegCurrent.Contains(ffmpegLatest)) HasFfmpegUpdate = true;
                
                HasUpdateAvailable = HasMtxUpdate || HasFfmpegUpdate;
                
                return HasUpdateAvailable;
            }
            catch
            {
                if (throwErrors) throw;
                return false;
            }
        }

        private static async Task DownloadFileWithProgress(string url, string destination, Form dlForm, ProgressBar pb, Label lblProgress, string componentName)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "ScreenToRTSP");
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = totalBytes != -1;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (canReportProgress)
                            {
                                int percentComplete = (int)((totalRead * 100) / totalBytes);
                                dlForm.Invoke((MethodInvoker)(() =>
                                {
                                    pb.Value = percentComplete;
                                    lblProgress.Text = $"{componentName}: {percentComplete}% ({(totalRead / 1024 / 1024)} MB / {(totalBytes / 1024 / 1024)} MB)";
                                }));
                            }
                        }
                    }
                }
            }
        }

        public static string GetFFmpegVersion()
        {
            try {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string ffmpeg = Path.Combine(appDir, "ffmpeg.exe");
                if (!File.Exists(ffmpeg)) return "N/A";
                
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    string line = proc.StandardOutput.ReadLine();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(line))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length >= 3)
                            return parts[2];
                    }
                }
            } catch { }
            return "N/A";
        }

        public static string GetMediaMTXVersion()
        {
            try {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string mediamtx = Path.Combine(appDir, "mediamtx.exe");
                if (!File.Exists(mediamtx)) return "N/A";
                
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = mediamtx,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    return output;
                }
            } catch { }
            return "N/A";
        }
    }
}
