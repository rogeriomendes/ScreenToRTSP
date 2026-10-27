using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScreenToRTSP.Core
{
    public static class Updater
    {
        private const string RepoOwner = "rogeriomendes";
        private const string RepoName = "ScreenToRTSP";

        public static bool HasAppUpdate { get; private set; } = false;
        public static string LatestVersionStr { get; private set; } = "";
        public static string LatestDownloadUrl { get; private set; } = "";
        private static string _cachedAppVersion;

        public static string GetAppVersion()
        {
            if (_cachedAppVersion != null) return _cachedAppVersion;
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (ver != null)
                {
                    _cachedAppVersion = $"{ver.Major}.{ver.Minor}.{ver.Build}";
                    return _cachedAppVersion;
                }
            }
            catch { }
            _cachedAppVersion = "2.0.0";
            return _cachedAppVersion;
        }

        public static async Task<(bool HasUpdate, string DownloadUrl, string Version)> CheckForUpdatesAsync()
        {
            try
            {
                using (var response = await DependencyManager.SharedHttpClient.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest"))
                {
                    if (!response.IsSuccessStatusCode) return (false, null, null);

                    var json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        string tagName = root.GetProperty("tag_name").GetString();

                        string latestVersionStr = tagName.TrimStart('v', 'V');
                        if (Version.TryParse(latestVersionStr, out Version latestVersion))
                        {
                            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 0, 0);

                            if (latestVersion > currentVersion)
                            {
                                string downloadUrl = null;
                                var assets = root.GetProperty("assets");
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    string name = asset.GetProperty("name").GetString();
                                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                        break;
                                    }
                                }

                                if (downloadUrl != null)
                                {
                                    HasAppUpdate = true;
                                    LatestVersionStr = tagName;
                                    LatestDownloadUrl = downloadUrl;
                                    return (true, downloadUrl, tagName);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Update error: " + ex.Message);
            }
            return (false, null, null);
        }

        public static async Task PerformUpdate(string downloadUrl, bool isTransmitting)
        {
            try
            {
                bool isZip = downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                string ext = isZip ? ".zip" : ".exe";
                string tempFile = Path.Combine(Path.GetTempPath(), "ScreenToRTSP_Update" + ext);

                using (var response = await DependencyManager.SharedHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var streamToReadFrom = await response.Content.ReadAsStreamAsync())
                    using (var streamToWriteTo = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        await streamToReadFrom.CopyToAsync(streamToWriteTo, 65536);
                    }
                }

                string currentExe = Environment.ProcessPath ?? Path.Combine(MonitorHelper.AppDir, "ScreenToRTSP.exe");
                string appDir = MonitorHelper.AppDir.TrimEnd('\\');
                string scriptPath = Path.Combine(Path.GetTempPath(), "update_ScreenToRTSP.ps1");
                string resumeArg = isTransmitting ? " --resume" : "";

                string script;
                if (isZip)
                {
                    script = $@"
Start-Sleep -Seconds 2
Expand-Archive -Path '{tempFile}' -DestinationPath '{appDir}' -Force
Remove-Item -Path '{tempFile}' -Force -ErrorAction SilentlyContinue
Start-Process '{currentExe}' -ArgumentList '{resumeArg}'
Remove-Item -Path '{scriptPath}' -Force -ErrorAction SilentlyContinue
";
                }
                else
                {
                    script = $@"
Start-Sleep -Seconds 2
Remove-Item -Path '{currentExe}' -Force
Move-Item -Path '{tempFile}' -Destination '{currentExe}' -Force
Start-Process '{currentExe}' -ArgumentList '{resumeArg}'
Remove-Item -Path '{scriptPath}' -Force
";
                }

                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MonitorHelper.ShowMessage("Erro ao baixar a atualização: " + ex.Message, "Erro", MonitorHelper.MB_ICONERROR);
            }
        }
    }
}
