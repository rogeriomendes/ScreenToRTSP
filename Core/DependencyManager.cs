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
        internal static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

        private static string _cachedFfmpegVersion;
        private static DateTime _cachedFfmpegWriteTime;
        private static string _cachedMtxVersion;
        private static DateTime _cachedMtxWriteTime;

        private static HttpClient CreateSharedHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenToRTSP/2.0");
            return client;
        }

        public static bool HasUpdateAvailable { get; private set; } = false;
        public static bool HasMtxUpdate { get; private set; } = false;
        public static bool HasFfmpegUpdate { get; private set; } = false;
        public static string MtxLatestVersionStr { get; private set; } = "";
        public static string FfmpegLatestVersionStr { get; private set; } = "";

        public static bool AreDependenciesInstalled()
        {
            string appDir = MonitorHelper.AppDir;
            string ffmpeg = Path.Combine(appDir, "ffmpeg.exe");
            string mediamtx = Path.Combine(appDir, "mediamtx.exe");
            return File.Exists(ffmpeg) && File.Exists(mediamtx);
        }

        public static async Task<bool> DownloadDependenciesAsync(bool force = false, IProgress<DependencyProgress> progress = null)
        {
            string appDir = MonitorHelper.AppDir;
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
            var response = await SharedHttpClient.GetStringAsync($"https://api.github.com/repos/{repoUrl}/releases/latest");
            using (var doc = System.Text.Json.JsonDocument.Parse(response))
            {
                return doc.RootElement.Clone();
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
                var mtxTask = GetLatestRelease("bluenviron/mediamtx");
                var ffmpegTask = GetLatestRelease("GyanD/codexffmpeg");
                await Task.WhenAll(mtxTask, ffmpegTask);

                var mtxRelease = await mtxTask;
                var ffmpegRelease = await ffmpegTask;

                string mtxLatest = mtxRelease.GetProperty("tag_name").GetString();
                string ffmpegLatest = ffmpegRelease.GetProperty("tag_name").GetString();

                MtxLatestVersionStr = mtxLatest ?? "";
                FfmpegLatestVersionStr = ffmpegLatest ?? "";

                await WarmupVersionCacheAsync();
                string mtxCurrent = GetMediaMTXVersion(allowBlocking: true);
                string ffmpegCurrent = GetFFmpegVersion(allowBlocking: true);

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
            using (var response = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var buffer = new byte[65536];
                    long totalRead = 0;
                    int bytesRead;
                    int lastReportedPercent = -1;
                    long lastReportTick = 0;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (canReportProgress && progress != null)
                        {
                            double percentComplete = (double)(totalRead * 100) / totalBytes;
                            int intPercent = (int)percentComplete;
                            long nowTick = Environment.TickCount64;
                            if (intPercent != lastReportedPercent && (nowTick - lastReportTick >= 50 || intPercent == 100))
                            {
                                lastReportedPercent = intPercent;
                                lastReportTick = nowTick;
                                progress.Report(new DependencyProgress
                                {
                                    StatusText = $"{componentName}: {intPercent}% ({(totalRead / 1024 / 1024)} MB / {(totalBytes / 1024 / 1024)} MB)",
                                    Percent = percentComplete,
                                    IsIndeterminate = false
                                });
                            }
                        }
                    }
                }
            }
        }

        public static Task WarmupVersionCacheAsync()
        {
            return Task.WhenAll(
                Task.Run(() => GetFFmpegVersion(allowBlocking: true)),
                Task.Run(() => GetMediaMTXVersion(allowBlocking: true)));
        }

        public static string GetFFmpegVersion(bool allowBlocking = false)
        {
            try
            {
                string appDir = MonitorHelper.AppDir;
                string ffmpeg = Path.Combine(appDir, "ffmpeg.exe");
                if (!File.Exists(ffmpeg)) return "N/A";

                DateTime writeTime = File.GetLastWriteTimeUtc(ffmpeg);
                if (_cachedFfmpegVersion != null && _cachedFfmpegWriteTime == writeTime)
                {
                    return _cachedFfmpegVersion;
                }

                if (!allowBlocking)
                {
                    return _cachedFfmpegVersion ?? "...";
                }

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
                    proc.WaitForExit(1500);
                    if (!string.IsNullOrEmpty(line))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length >= 3)
                        {
                            _cachedFfmpegWriteTime = writeTime;
                            _cachedFfmpegVersion = parts[2];
                            return _cachedFfmpegVersion;
                        }
                    }
                }
            }
            catch { }
            return "N/A";
        }

        public static string GetMediaMTXVersion(bool allowBlocking = false)
        {
            try
            {
                string appDir = MonitorHelper.AppDir;
                string mediamtx = Path.Combine(appDir, "mediamtx.exe");
                if (!File.Exists(mediamtx)) return "N/A";

                DateTime writeTime = File.GetLastWriteTimeUtc(mediamtx);
                if (_cachedMtxVersion != null && _cachedMtxWriteTime == writeTime)
                {
                    return _cachedMtxVersion;
                }

                if (!allowBlocking)
                {
                    return _cachedMtxVersion ?? "...";
                }

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
                    proc.WaitForExit(1500);
                    if (!string.IsNullOrEmpty(output))
                    {
                        _cachedMtxWriteTime = writeTime;
                        _cachedMtxVersion = output;
                        return _cachedMtxVersion;
                    }
                }
            }
            catch { }
            return "N/A";
        }
    }
}
