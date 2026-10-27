using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace ScreenToRTSP.Core
{
    public struct DependencyProgress
    {
        public string StatusText { get; set; }
        public double Percent { get; set; }
        public bool IsIndeterminate { get; set; }
    }

    public static class DependencyManager
    {
        public static bool HasUpdateAvailable { get; private set; } = false;
        public static bool HasMtxUpdate { get; private set; } = false;
        public static bool HasFfmpegUpdate { get; private set; } = false;
        public static string MtxLatestVersionStr { get; private set; } = "";
        public static string FfmpegLatestVersionStr { get; private set; } = "";

        public static bool AreDependenciesInstalled()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpeg = Path.Combine(appDir, "ffmpeg.exe");
            string mediamtx = Path.Combine(appDir, "mediamtx.exe");
            return File.Exists(ffmpeg) && File.Exists(mediamtx);
        }

        public static async Task<bool> DownloadDependenciesAsync(bool force = false, IProgress<DependencyProgress> progress = null)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpeg = Path.Combine(appDir, "ffmpeg.exe");
            string mediamtx = Path.Combine(appDir, "mediamtx.exe");

            bool needFfmpeg = !File.Exists(ffmpeg) || (force && HasFfmpegUpdate);
            bool needMtx = !File.Exists(mediamtx) || (force && HasMtxUpdate);

            if (force && !needFfmpeg && !needMtx)
            {
                needFfmpeg = true;
                needMtx = true;
            }

            if (!needFfmpeg && !needMtx)
            {
                return true;
            }

            try
            {
                string mtxZip = Path.Combine(appDir, "mediamtx_temp.zip");
                string ffmpeg7z = Path.Combine(appDir, "ffmpeg_temp.7z");

                if (needFfmpeg)
                {
                    progress?.Report(new DependencyProgress { StatusText = "Buscando link do FFmpeg...", Percent = 0, IsIndeterminate = true });
                    string ffmpegUrl = await GetLatestReleaseAssetUrl("GyanD/codexffmpeg", "essentials_build.7z");
                    if (string.IsNullOrEmpty(ffmpegUrl)) throw new Exception("Não foi possível encontrar a última versão do FFmpeg.");

                    await DownloadFileWithProgress(ffmpegUrl, ffmpeg7z, progress, "FFmpeg");
                }

                if (needMtx)
                {
                    progress?.Report(new DependencyProgress { StatusText = "Buscando link do MediaMTX...", Percent = 0, IsIndeterminate = true });
                    string mtxUrl = await GetLatestReleaseAssetUrl("bluenviron/mediamtx", "windows_amd64.zip");
                    if (string.IsNullOrEmpty(mtxUrl)) throw new Exception("Não foi possível encontrar a última versão do MediaMTX.");

                    await DownloadFileWithProgress(mtxUrl, mtxZip, progress, "MediaMTX");
                }

                if (needFfmpeg)
                {
                    progress?.Report(new DependencyProgress { StatusText = "Extraindo FFmpeg...", Percent = 100, IsIndeterminate = true });
                    await Task.Run(() =>
                    {
                        using (var archive = ArchiveFactory.OpenArchive(ffmpeg7z))
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
                    progress?.Report(new DependencyProgress { StatusText = "Extraindo MediaMTX...", Percent = 100, IsIndeterminate = true });
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

                progress?.Report(new DependencyProgress { StatusText = "Concluído!", Percent = 100, IsIndeterminate = false });
                HasUpdateAvailable = false;
                return true;
            }
            catch (Exception ex)
            {
                MonitorHelper.ShowMessage(
                    $"Erro ao baixar dependências: {ex.Message}\n\nVerifique sua conexão com a internet ou tente novamente mais tarde.",
                    "Erro de Download",
                    MonitorHelper.MB_ICONERROR);
                return false;
            }
        }

        public static bool CheckAndDownloadDependencies(bool force = false)
        {
            if (!force && AreDependenciesInstalled())
                return true;

            return Task.Run(() => DownloadDependenciesAsync(force, null)).GetAwaiter().GetResult();
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
            }
            catch { }
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

        private static async Task DownloadFileWithProgress(string url, string destination, IProgress<DependencyProgress> progress, string componentName)
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
                                double percentComplete = (double)(totalRead * 100) / totalBytes;
                                progress?.Report(new DependencyProgress
                                {
                                    StatusText = $"{componentName}: {(int)percentComplete}% ({(totalRead / 1024 / 1024)} MB / {(totalBytes / 1024 / 1024)} MB)",
                                    Percent = percentComplete,
                                    IsIndeterminate = false
                                });
                            }
                        }
                    }
                }
            }
        }

        public static string GetFFmpegVersion()
        {
            try
            {
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
            }
            catch { }
            return "N/A";
        }

        public static string GetMediaMTXVersion()
        {
            try
            {
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
            }
            catch { }
            return "N/A";
        }
    }
}
