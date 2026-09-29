using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenToRTSP.Core
{
        public static class Updater
    {
        private const string RepoOwner = "rogeriomendes";
        private const string RepoName = "ScreenToRTSP";

        public static bool HasAppUpdate { get; private set; } = false;
        public static string LatestVersionStr { get; private set; } = "";
        public static string LatestDownloadUrl { get; private set; } = "";

        public static async Task<(bool HasUpdate, string DownloadUrl, string Version)> CheckForUpdatesAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ScreenToRTSP", "1.0"));
                    var response = await client.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
                    if (!response.IsSuccessStatusCode) return (false, null, null);

                    var json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        string tagName = root.GetProperty("tag_name").GetString();
                        
                        string latestVersionStr = tagName.TrimStart('v', 'V');
                        if (Version.TryParse(latestVersionStr, out Version latestVersion))
                        {
                            Version currentVersion = new Version(Application.ProductVersion);
                            
                            if (latestVersion > currentVersion)
                            {
                                string downloadUrl = null;
                                var assets = root.GetProperty("assets");
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    string name = asset.GetProperty("name").GetString();
                                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
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
                string tempFile = Path.Combine(Path.GetTempPath(), "ScreenToRTSP_Update.exe");
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ScreenToRTSP", "1.0"));
                    var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    using (var streamToReadFrom = await response.Content.ReadAsStreamAsync())
                    using (var streamToWriteTo = File.Open(tempFile, FileMode.Create))
                    {
                        await streamToReadFrom.CopyToAsync(streamToWriteTo);
                    }
                }

                string currentExe = Application.ExecutablePath;
                string scriptPath = Path.Combine(Path.GetTempPath(), "update_ScreenToRTSP.ps1");
                
                string resumeArg = isTransmitting ? " --resume" : "";

                string script = $@"
Start-Sleep -Seconds 2
Remove-Item -Path '{currentExe}' -Force
Move-Item -Path '{tempFile}' -Destination '{currentExe}' -Force
Start-Process '{currentExe}' -ArgumentList '{resumeArg}'
Remove-Item -Path '{scriptPath}' -Force
";
                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao baixar a atualização: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}



