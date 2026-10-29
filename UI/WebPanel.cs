using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using ScreenToRTSP.Core;
using ScreenToRTSP.I18n;

namespace ScreenToRTSP.UI
{
    public class WebPanel
    {
        private Main form;
        private static byte[] _cachedIconBytes;

        public WebPanel(Main form)
        {
            this.form = form;
        }

        public void Start()
        {
            if (form.isWebServerRunning) return;
            string portStr = form.txtWebPort.Text;
            if (string.IsNullOrEmpty(portStr)) portStr = "8889";

            try
            {
                form.httpListener = new HttpListener();
                form.httpListener.Prefixes.Add($"http://*:{portStr}/");
                form.httpListener.Start();
                form.isWebServerRunning = true;
                form.webServerThread = new Thread(Loop) { IsBackground = true };
                form.webServerThread.Start();
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 5) // Access Denied
                {
                    try
                    {
                        form.httpListener = new HttpListener();
                        form.httpListener.Prefixes.Add($"http://localhost:{portStr}/");
                        form.httpListener.Prefixes.Add($"http://127.0.0.1:{portStr}/");
                        form.httpListener.Start();
                        form.isWebServerRunning = true;
                        form.webServerThread = new Thread(Loop) { IsBackground = true };
                        form.webServerThread.Start();
                        MonitorHelper.ShowMessage(
                            "Painel Web iniciado, porém com acesso bloqueado para outros computadores.\n\nPara acessar usando o celular ou outros computadores da rede, feche o aplicativo e abra como 'Executar como Administrador'.",
                            "Aviso de Permissão",
                            MonitorHelper.MB_ICONINFORMATION);
                    }
                    catch (Exception ex2)
                    {
                        MonitorHelper.ShowMessage(
                            $"Não foi possível iniciar o painel Web na porta {portStr}. Ela pode estar em uso ou bloqueada.\nMotivo: " + ex2.Message,
                            "Erro no Painel Web",
                            MonitorHelper.MB_ICONWARNING);
                        form.chkWebServer.IsOn = false;
                        form.isWebServerRunning = false;
                    }
                }
                else
                {
                    MonitorHelper.ShowMessage(
                        $"A porta {portStr} já está em uso ou indisponível.\nPor favor, escolha outra porta e tente novamente.\n\nMotivo: " + ex.Message,
                        "Porta em Uso",
                        MonitorHelper.MB_ICONWARNING);
                    form.chkWebServer.IsOn = false;
                    form.isWebServerRunning = false;
                }
            }
            catch (Exception ex)
            {
                MonitorHelper.ShowMessage(
                    $"Não foi possível iniciar o painel Web na porta {portStr}.\nMotivo: " + ex.Message,
                    "Erro no Painel Web",
                    MonitorHelper.MB_ICONWARNING);
                form.chkWebServer.IsOn = false;
                form.isWebServerRunning = false;
            }
        }

        public void Stop()
        {
            form.isWebServerRunning = false;
            if (form.httpListener != null)
            {
                try
                {
                    form.httpListener.Stop();
                    form.httpListener.Close();
                }
                catch { }
                form.httpListener = null;
            }
        }

        private void Loop()
        {
            while (form.isWebServerRunning && form.httpListener != null && form.httpListener.IsListening)
            {
                try
                {
                    var context = form.httpListener.GetContext();
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            var request = context.Request;
                            var response = context.Response;

                            bool authRequired = false;
                            string reqUser = "", reqPass = "";
                            form.InvokeSafe(() =>
                            {
                                authRequired = form.chkAuth.IsOn;
                                reqUser = form.txtUsername.Text;
                                reqPass = string.IsNullOrEmpty(form.txtPassword.Password) ? form.currentPassword : form.txtPassword.Password;
                            });

                            if (authRequired)
                            {
                                string authHeader = request.Headers["Authorization"];
                                bool authorized = false;
                                if (authHeader != null && authHeader.StartsWith("Basic "))
                                {
                                    try
                                    {
                                        string encoded = authHeader.Substring(6);
                                        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                                        int colonIdx = decoded.IndexOf(':');
                                        if (colonIdx >= 0 && decoded.Substring(0, colonIdx) == reqUser && decoded.Substring(colonIdx + 1) == reqPass) authorized = true;
                                    }
                                    catch { }
                                }
                                if (!authorized)
                                {
                                    response.StatusCode = 401;
                                    response.AddHeader("WWW-Authenticate", $"Basic realm=\"{LanguageManager.Get("AppTitle")}\"");
                                    byte[] err = Encoding.UTF8.GetBytes(LanguageManager.Get("AccessDenied"));
                                    response.OutputStream.Write(err, 0, err.Length);
                                    response.Close();
                                    return;
                                }
                            }

                            if (request.Url.AbsolutePath == "/favicon.ico" || request.Url.AbsolutePath == "/icon.png")
                            {
                                try
                                {
                                    if (_cachedIconBytes == null)
                                    {
                                        using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenToRTSP.assets.icon.png"))
                                        {
                                            if (stream != null)
                                            {
                                                using var ms = new MemoryStream((int)stream.Length);
                                                stream.CopyTo(ms);
                                                _cachedIconBytes = ms.ToArray();
                                            }
                                        }
                                    }
                                    if (_cachedIconBytes != null)
                                    {
                                        response.ContentType = "image/png";
                                        response.ContentLength64 = _cachedIconBytes.Length;
                                        response.OutputStream.Write(_cachedIconBytes, 0, _cachedIconBytes.Length);
                                    }
                                }
                                catch { }
                                response.Close();
                                return;
                            }

                            if (request.Url.AbsolutePath == "/status")
                            {
                                byte[] sBuf = Encoding.UTF8.GetBytes(form.isTransmitting ? "true" : "false");
                                response.ContentType = "text/plain";
                                response.ContentLength64 = sBuf.Length;
                                response.OutputStream.Write(sBuf, 0, sBuf.Length);
                                response.Close();
                                return;
                            }

                            if (request.Url.AbsolutePath == "/logs")
                            {
                                string logs = form.LogWriter?.GetSnapshot() ?? "";
                                byte[] sBuf = Encoding.UTF8.GetBytes(logs);
                                response.ContentType = "text/plain";
                                response.ContentLength64 = sBuf.Length;
                                response.OutputStream.Write(sBuf, 0, sBuf.Length);
                                response.Close();
                                return;
                            }

                            if (request.Url.AbsolutePath == "/clearlogs")
                            {
                                form.InvokeSafe(() => form.LogWriter?.Clear());
                                byte[] cBuf = Encoding.UTF8.GetBytes("ok");
                                response.ContentType = "text/plain";
                                response.ContentLength64 = cBuf.Length;
                                response.OutputStream.Write(cBuf, 0, cBuf.Length);
                                response.Close();
                                return;
                            }

                            if (request.Url.AbsolutePath == "/api/state")
                            {
                                bool isTx = false;
                                string lUrl = "", lLowUrl = "";
                                bool dOn = false;
                                form.InvokeSafe(() =>
                                {
                                    isTx = form.isTransmitting;
                                    lUrl = form.currentLanUrl ?? "";
                                    lLowUrl = form.currentLanLowUrl ?? "";
                                    dOn = form.chkDualStream.IsOn;
                                });

                                string json = $"{{\"isTransmitting\":{(isTx ? "true" : "false")},\"lanUrl\":\"{lUrl.Replace("\\", "\\\\")}\",\"lanLowUrl\":\"{lLowUrl.Replace("\\", "\\\\")}\",\"dualOn\":{(dOn ? "true" : "false")}}}";
                                byte[] jBuf = Encoding.UTF8.GetBytes(json);
                                response.ContentType = "application/json; charset=utf-8";
                                response.ContentLength64 = jBuf.Length;
                                response.OutputStream.Write(jBuf, 0, jBuf.Length);
                                response.Close();
                                return;
                            }

                            if (request.HttpMethod == "POST")
                            {
                                string action = "";
                                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                                {
                                    string body = reader.ReadToEnd();
                                    var p = new System.Collections.Generic.Dictionary<string, string>();
                                    foreach (string pair in body.Split('&'))
                                    {
                                        var parts = pair.Split('=');
                                        if (parts.Length == 2) p[parts[0]] = System.Uri.UnescapeDataString(parts[1]).Replace("+", " ");
                                    }
                                    if (p.ContainsKey("action")) action = p["action"];

                                    if (action == "check")
                                    {
                                        try
                                        {
                                            var appTask = Updater.CheckForUpdatesAsync();
                                            var depTask = DependencyManager.CheckForNewVersionsAsync(true);
                                            Task.WhenAll(appTask, depTask).GetAwaiter().GetResult();
                                            bool hasUpdate = Updater.HasAppUpdate || DependencyManager.HasUpdateAvailable;
                                            action = hasUpdate ? "hasupdate" : "noupdate";
                                        }
                                        catch
                                        {
                                            action = "checkerror";
                                        }
                                    }

                                    if (action == "update")
                                    {
                                        bool wasTransmitting = false;
                                        form.InvokeSafe(() =>
                                        {
                                            wasTransmitting = form.isTransmitting;
                                            if (wasTransmitting) form.StopClick();
                                        });

                                        DependencyManager.CheckAndDownloadDependencies(true);
                                        DependencyManager.CheckForNewVersionsAsync().GetAwaiter().GetResult();

                                        if (wasTransmitting)
                                        {
                                            form.InvokeSafe(() => form.StartClick());
                                        }
                                        action = "updated";
                                    }
                                    else
                                    {
                                        form.InvokeSafe(() =>
                                        {
                                            if (action == "start") form.StartClick();
                                            else if (action == "stop") form.StopClick();
                                            else if (action == "save")
                                            {
                                                bool wasTransmitting = form.isTransmitting;
                                                if (wasTransmitting) form.StopClick();

                                                form.isLoading = true;
                                                try
                                                {
                                                    if (p.ContainsKey("port")) form.txtPort.Text = p["port"];
                                                    if (p.ContainsKey("stream")) form.txtStreamName.Text = p["stream"];
                                                    if (p.ContainsKey("user")) form.txtUsername.Text = p["user"];
                                                    if (p.ContainsKey("pass") && !string.IsNullOrEmpty(p["pass"]))
                                                    {
                                                        form.txtPassword.Password = p["pass"];
                                                        form.currentPassword = p["pass"];
                                                    }
                                                    if (p.ContainsKey("monitor")) form.cmbMonitor.SelectedIndex = int.Parse(p["monitor"]);
                                                    if (p.ContainsKey("encoder")) form.cmbEncoder.SelectedIndex = int.Parse(p["encoder"]);
                                                    if (p.ContainsKey("quality")) form.cmbQuality.SelectedIndex = int.Parse(p["quality"]);
                                                    if (p.ContainsKey("fps"))
                                                    {
                                                        int fIdx = form.cmbFps.Items.IndexOf(p["fps"]);
                                                        if (fIdx >= 0) form.cmbFps.SelectedIndex = fIdx;
                                                    }
                                                    form.chkDualStream.IsOn = p.ContainsKey("dual");
                                                    form.chkMouseCursor.IsOn = p.ContainsKey("mouse");
                                                    form.chkSchedule.IsOn = p.ContainsKey("sched");
                                                    if (p.ContainsKey("sched_s")) form.ScheduleStartText = p["sched_s"];
                                                    if (p.ContainsKey("sched_e")) form.ScheduleEndText = p["sched_e"];
                                                    form.chkStartWithWindows.IsOn = p.ContainsKey("startwin");
                                                    form.ToggleStartWithWindows();

                                                    if (p.ContainsKey("lang"))
                                                    {
                                                        int langIdx = int.Parse(p["lang"]);
                                                        form.cmbLanguage.SelectedIndex = langIdx;
                                                        var keys = LanguageManager.GetLanguageKeys();
                                                        if (langIdx >= 0 && langIdx < keys.Count)
                                                            LanguageManager.ApplyLanguage(keys[langIdx]);
                                                    }

                                                    if (p.ContainsKey("theme"))
                                                    {
                                                        int themeIdx = int.Parse(p["theme"]);
                                                        form.cmbTheme.SelectedIndex = themeIdx;
                                                        string newTheme = themeIdx == 1 ? "Dark" : (themeIdx == 2 ? "Light" : "System");
                                                        form.ApplyTheme(newTheme);
                                                    }
                                                }
                                                finally
                                                {
                                                    form.isLoading = false;
                                                }

                                                form.TriggerSave();
                                                form.UpdateUrlStatus();
                                                if (wasTransmitting) form.StartClick();
                                            }
                                        });
                                    }
                                }

                                if (action == "save") response.Redirect("/?saved=1");
                                else if (action == "updated") response.Redirect("/?updated=1#tab-sobre");
                                else if (action == "error") response.Redirect("/?error=1#tab-sobre");
                                else if (action == "hasupdate") response.Redirect("/?hasupdate=1#tab-sobre");
                                else if (action == "noupdate") response.Redirect("/?noupdate=1#tab-sobre");
                                else if (action == "checkerror") response.Redirect("/?checkerror=1#tab-sobre");
                                else response.Redirect("/");

                                response.Close();
                                return;
                            }

                            bool isSaved = request.Url.Query.Contains("saved=1");
                            bool isUpdated = request.Url.Query.Contains("updated=1");
                            bool isError = request.Url.Query.Contains("error=1");
                            bool hasUpdateQ = request.Url.Query.Contains("hasupdate=1");
                            bool noUpdateQ = request.Url.Query.Contains("noupdate=1");
                            bool checkErrorQ = request.Url.Query.Contains("checkerror=1");

                            string toastHtml = "";
                            if (isSaved)
                            {
                                toastHtml = $"<div id='appToast' class='toast-pill toast-success'><span>✔</span> <span>{LanguageManager.Get("MsgSaveSuccess")}</span></div>";
                            }
                            else if (isUpdated)
                            {
                                toastHtml = $"<div id='appToast' class='toast-pill toast-success'><span>✔</span> <span>{LanguageManager.Get("MsgUpdateSuccess")}</span></div>";
                            }
                            else if (noUpdateQ)
                            {
                                toastHtml = $"<div id='appToast' class='toast-pill toast-info'><span>ℹ️</span> <span>{LanguageManager.Get("MsgNoUpdate")}</span></div>";
                            }
                            else if (hasUpdateQ)
                            {
                                toastHtml = $"<div id='appToast' class='toast-pill toast-warning'><span>⚠️</span> <span>{LanguageManager.Get("UpdateAvailable")}</span></div>";
                            }
                            else if (isError)
                            {
                                toastHtml = $"<div id='appToast' class='toast-pill toast-error'><span>🛑</span> <span>{LanguageManager.Get("MsgStopBeforeUpdate")}</span></div>";
                            }
                            else if (checkErrorQ)
                            {
                                toastHtml = $"<div id='appToast' class='toast-pill toast-error'><span>⚠️</span> <span>{LanguageManager.Get("MsgCheckUpdateError")}</span></div>";
                            }

                            bool isTransmitting = false;
                            int monitorIdx = 0, monitorCount = 1, encoderIdx = 0, qualityIdx = 1, langIdx = 0;
                            string selectedFps = "30", lanUrl = "", lanLowUrl = "", streamName = "", portText = "", userText = "", schedStart = "", schedEnd = "";
                            bool dualOn = false, mouseOn = true, authOn = false, startWinOn = false, schedOn = false;
                            var fpsItems = new System.Collections.Generic.List<string>();
                            var langItems = new System.Collections.Generic.List<string>();

                            form.InvokeSafe(() =>
                            {
                                isTransmitting = form.isTransmitting;
                                monitorIdx = form.cmbMonitor.SelectedIndex;
                                monitorCount = form.cmbMonitor.Items.Count;
                                selectedFps = form.cmbFps.SelectedItem?.ToString() ?? "30";
                                foreach (var fOpt in form.cmbFps.Items) fpsItems.Add(fOpt.ToString() ?? "");
                                langIdx = form.cmbLanguage.SelectedIndex;
                                foreach (var lOpt in form.cmbLanguage.Items) langItems.Add(lOpt.ToString() ?? "");
                                encoderIdx = form.cmbEncoder.SelectedIndex;
                                qualityIdx = form.cmbQuality.SelectedIndex;
                                dualOn = form.chkDualStream.IsOn;
                                mouseOn = form.chkMouseCursor.IsOn;
                                authOn = form.chkAuth.IsOn;
                                startWinOn = form.chkStartWithWindows.IsOn;
                                schedOn = form.chkSchedule.IsOn;
                                lanUrl = form.currentLanUrl;
                                lanLowUrl = form.currentLanLowUrl;
                                streamName = form.txtStreamName.Text;
                                portText = form.txtPort.Text;
                                userText = form.txtUsername.Text;
                                schedStart = form.ScheduleStartText;
                                schedEnd = form.ScheduleEndText;
                            });

                            if (!string.IsNullOrEmpty(schedStart) && TimeSpan.TryParse(schedStart, out var tsStart))
                                schedStart = $"{tsStart.Hours:D2}:{tsStart.Minutes:D2}";
                            if (!string.IsNullOrEmpty(schedEnd) && TimeSpan.TryParse(schedEnd, out var tsEnd))
                                schedEnd = $"{tsEnd.Hours:D2}:{tsEnd.Minutes:D2}";

                            string statusDotColor = isTransmitting ? "#22c55e" : "#e11d48";
                            string statusText = isTransmitting ? LanguageManager.Get("StatusRunning") : LanguageManager.Get("StatusStopped");
                            string statusBadge = isTransmitting 
                                ? $"<div class='badge-streaming'>● {statusText.ToUpperInvariant()}</div>" 
                                : "";

                            string chk(bool b) => b ? "checked" : "";
                            string sel(int idx, int match) => idx == match ? "selected" : "";

                            string monitorOptions = $"<option value='0' {sel(monitorIdx, 0)}>{LanguageManager.Get("AllMonitors")}</option>";
                            for (int i = 1; i < monitorCount; i++)
                                monitorOptions += $"<option value='{i}' {sel(monitorIdx, i)}>{LanguageManager.Get("MonitorPrefix")} {i}</option>";

                            string fpsOptions = "";
                            foreach (var fOpt in fpsItems)
                                fpsOptions += $"<option value='{fOpt}' {(selectedFps == fOpt ? "selected" : "")}>{fOpt}</option>";

                            string langOptions = "";
                            for (int i = 0; i < langItems.Count; i++)
                                langOptions += $"<option value='{i}' {sel(langIdx, i)}>{langItems[i]}</option>";

                            var cCfg = form.configManager.Load();
                            string curThemeStr = cCfg.ContainsKey("Theme") ? cCfg["Theme"] : "System";
                            int curThemeIdx = curThemeStr == "Dark" ? 1 : (curThemeStr == "Light" ? 2 : 0);
                            string themeOptions = $"<option value='0' {sel(curThemeIdx, 0)}>{LanguageManager.Get("ThemeSystem")}</option>";
                            themeOptions += $"<option value='1' {sel(curThemeIdx, 1)}>{LanguageManager.Get("ThemeDark")}</option>";
                            themeOptions += $"<option value='2' {sel(curThemeIdx, 2)}>{LanguageManager.Get("ThemeLight")}</option>";

                            string btnAction = isTransmitting ? "stop" : "start";
                            string btnText = isTransmitting ? "■ " + LanguageManager.Get("BtnStop") : "▶ " + LanguageManager.Get("BtnStart");
                            string btnClass = isTransmitting ? "btn-stop" : "btn-start";

                            string linksHtml = "";
                            string copyBtnsHtml = "";
                            if (isTransmitting)
                            {
                                linksHtml += $@"<div class='links-card'>
                                    <div><span class='link-tag'>{LanguageManager.Get("BtnCopyLan")}:</span> <a href='{lanUrl}' target='_blank' class='rtsp-link'>{lanUrl}</a></div>";
                                if (dualOn)
                                {
                                    linksHtml += $"<div><span class='link-tag'>{LanguageManager.Get("BtnCopyLocal")}:</span> <a href='{lanLowUrl}' target='_blank' class='rtsp-link'>{lanLowUrl}</a></div>";
                                }
                                linksHtml += "</div>";

                                copyBtnsHtml += $@"<button type='button' class='btn-copy' title='{LanguageManager.Get("TipBtnCopyLan")}' onclick=""copyText('{lanUrl}')"">📋 {LanguageManager.Get("BtnCopyLan")}</button>";
                                if (dualOn)
                                {
                                    copyBtnsHtml += $@"<button type='button' class='btn-copy' title='{LanguageManager.Get("TipBtnCopyLocal")}' onclick=""copyText('{lanLowUrl}')"">📋 {LanguageManager.Get("BtnCopyLocal")}</button>";
                                }
                            }
                            else
                            {
                                linksHtml += $@"<div class='links-card links-empty'>
                                    <div>{LanguageManager.Get("StatusWaitingLinks")}</div>
                                </div>";
                            }

                            string updateAlertHtml = "";
                            if (DependencyManager.HasUpdateAvailable || Updater.HasAppUpdate)
                            {
                                string lblMsg = LanguageManager.Get("UpdateAvailable");
                                updateAlertHtml = $@"
                                    <div class='update-alert'>
                                        <p style='margin:0;'>⚠️ {lblMsg}</p>
                                    </div>";
                            }

                            string updateBtn = (DependencyManager.HasUpdateAvailable || Updater.HasAppUpdate)
                                ? $@"<button type='button' class='btn-update' title='{LanguageManager.Get("TipBtnUpdateBinaries")}' onclick=""if(Main_isTransmitting) {{ if(confirm('{LanguageManager.Get("MsgStopBeforeUpdate")}')) submitAction('update'); }} else {{ submitAction('update'); }}"">⬇ {LanguageManager.Get("BtnUpdateBinaries")}</button>"
                                : "";

                            string appVer = Updater.GetAppVersion();
                            string appUpdateBadge = Updater.HasAppUpdate ? $" <span class='badge-update'>({LanguageManager.Get("NewVersion")}{Updater.LatestVersionStr})</span>" : "";

                            string ffmpegVer = DependencyManager.GetFFmpegVersion();
                            string ffmpegUpdateBadge = DependencyManager.HasFfmpegUpdate ? $" <span class='badge-update'>({LanguageManager.Get("NewVersion")}{DependencyManager.FfmpegLatestVersionStr})</span>" : "";

                            string mtxVer = DependencyManager.GetMediaMTXVersion();
                            string mtxUpdateBadge = DependencyManager.HasMtxUpdate ? $" <span class='badge-update'>({LanguageManager.Get("NewVersion")}{DependencyManager.MtxLatestVersionStr})</span>" : "";

                            string html = $@"<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>
                                <link rel='icon' type='image/png' href=""/icon.png"">
                                <title>{(isTransmitting ? $"● [{statusText}] " : "")}{LanguageManager.Get("AppTitle")}</title>
                                <style>
                                    :root {{
                                        color-scheme: dark;
                                        --bg-canvas: #070707;
                                        --bg-sheet: #121212;
                                        --bg-card: #181818;
                                        --bg-card-hover: #222222;
                                        --bg-input: #242424;
                                        --border-sheet: #222222;
                                        --border-card: #282828;
                                        --border-input: #333333;
                                        --accent-green: #16a34a;
                                        --accent-green-bright: #22c55e;
                                        --accent-green-glow: rgba(34, 197, 94, 0.25);
                                        --danger-red: #e11d48;
                                        --text-primary: #ffffff;
                                        --text-secondary: #a3a3a3;
                                        --text-muted: #737373;
                                    }}

                                    body.theme-light {{
                                        color-scheme: light;
                                        --bg-canvas: #f4f4f5;
                                        --bg-sheet: #ffffff;
                                        --bg-card: #f9fafb;
                                        --bg-card-hover: #f3f4f6;
                                        --bg-input: #ffffff;
                                        --border-sheet: #e4e4e7;
                                        --border-card: #e5e7eb;
                                        --border-input: #d1d5db;
                                        --accent-green: #16a34a;
                                        --accent-green-bright: #15803d;
                                        --accent-green-glow: rgba(22, 163, 74, 0.2);
                                        --danger-red: #dc2626;
                                        --text-primary: #111827;
                                        --text-secondary: #4b5563;
                                        --text-muted: #9ca3af;
                                    }}

                                    body.theme-dark {{
                                        color-scheme: dark;
                                    }}

                                    @media (prefers-color-scheme: light) {{
                                        body.theme-system {{
                                            color-scheme: light;
                                            --bg-canvas: #f4f4f5;
                                            --bg-sheet: #ffffff;
                                            --bg-card: #f9fafb;
                                            --bg-card-hover: #f3f4f6;
                                            --bg-input: #ffffff;
                                            --border-sheet: #e4e4e7;
                                            --border-card: #e5e7eb;
                                            --border-input: #d1d5db;
                                            --accent-green: #16a34a;
                                            --accent-green-bright: #15803d;
                                            --accent-green-glow: rgba(22, 163, 74, 0.2);
                                            --danger-red: #dc2626;
                                            --text-primary: #111827;
                                            --text-secondary: #4b5563;
                                            --text-muted: #9ca3af;
                                        }}
                                    }}

                                    @media (prefers-color-scheme: dark) {{
                                        body.theme-system {{
                                            color-scheme: dark;
                                        }}
                                    }}

                                    * {{ box-sizing: border-box; }}
                                    html, body {{
                                        width: 100%;
                                        height: 100%;
                                        margin: 0;
                                        padding: 0;
                                        overflow: hidden;
                                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                                        background: var(--bg-canvas);
                                        color: var(--text-primary);
                                        transition: background 0.25s, color 0.25s;
                                    }} 

                                    .app-layout {{
                                        display: flex;
                                        flex-direction: column;
                                        width: 100vw;
                                        height: 100vh;
                                        overflow: hidden;
                                        background: var(--bg-canvas);
                                    }}

                                    /* Top Custom Titlebar */
                                    .app-titlebar {{
                                        display: flex;
                                        align-items: center;
                                        justify-content: space-between;
                                        padding: 0 24px;
                                        height: 52px;
                                        background: #000000;
                                        border-bottom: 1px solid var(--border-sheet);
                                        flex-shrink: 0;
                                        z-index: 10;
                                    }}

                                    .brand-area {{
                                        display: flex;
                                        align-items: center;
                                        gap: 12px;
                                    }}

                                    .app-icon {{
                                        width: 24px;
                                        height: 24px;
                                        object-fit: contain;
                                    }}

                                    .app-name {{
                                        font-size: 15px;
                                        font-weight: 700;
                                        color: var(--accent-green-bright);
                                        letter-spacing: -0.2px;
                                    }}

                                    .app-desc {{
                                        font-size: 13px;
                                        color: var(--text-secondary);
                                    }}

                                    .badge-streaming {{
                                        display: inline-flex;
                                        align-items: center;
                                        gap: 6px;
                                        background: #162E1E;
                                        border: 1px solid #22C55E;
                                        color: #22C55E;
                                        font-size: 11px;
                                        font-weight: 700;
                                        padding: 4px 10px;
                                        border-radius: 12px;
                                        letter-spacing: 0.5px;
                                    }}

                                    .header-actions {{
                                        display: flex;
                                        align-items: center;
                                        gap: 12px;
                                    }}

                                    .toast-pill {{
                                        display: inline-flex;
                                        align-items: center;
                                        gap: 7px;
                                        padding: 5px 14px;
                                        border-radius: 14px;
                                        font-size: 12px;
                                        font-weight: 600;
                                        letter-spacing: -0.1px;
                                        animation: toastFadeIn 0.25s ease-out;
                                        transition: opacity 0.35s ease, transform 0.35s ease;
                                    }}

                                    @keyframes toastFadeIn {{
                                        from {{ opacity: 0; transform: translateY(-4px); }}
                                        to {{ opacity: 1; transform: translateY(0); }}
                                    }}

                                    .toast-pill.toast-success {{
                                        background: #14291a;
                                        border: 1px solid #22c55e;
                                        color: #4ade80;
                                    }}

                                    .toast-pill.toast-info {{
                                        background: #102136;
                                        border: 1px solid #3b82f6;
                                        color: #60a5fa;
                                    }}

                                    .toast-pill.toast-warning {{
                                        background: #2b220c;
                                        border: 1px solid #eab308;
                                        color: #fde047;
                                    }}

                                    .toast-pill.toast-error {{
                                        background: #2a1515;
                                        border: 1px solid #ef4444;
                                        color: #fca5a5;
                                    }}

                                    body.theme-light .toast-pill.toast-success {{
                                        background: #ecfdf5;
                                        border-color: #86efac;
                                        color: #15803d;
                                    }}
                                    body.theme-light .toast-pill.toast-info {{
                                        background: #eff6ff;
                                        border-color: #93c5fd;
                                        color: #1d4ed8;
                                    }}
                                    body.theme-light .toast-pill.toast-warning {{
                                        background: #fefce8;
                                        border-color: #fde047;
                                        color: #a16207;
                                    }}
                                    body.theme-light .toast-pill.toast-error {{
                                        background: #fef2f2;
                                        border-color: #fca5a5;
                                        color: #b91c1c;
                                    }}

                                    /* Main Layout: Sidebar + Content Area */
                                    .app-main {{
                                        display: flex;
                                        flex: 1;
                                        overflow: hidden;
                                    }}

                                    .app-sidebar {{
                                        width: 220px;
                                        background: #000000;
                                        border-right: 1px solid var(--border-sheet);
                                        padding: 20px 12px;
                                        display: flex;
                                        flex-direction: column;
                                        gap: 4px;
                                        flex-shrink: 0;
                                        overflow-y: auto;
                                    }}

                                    .nav-item {{
                                        display: flex;
                                        align-items: center;
                                        gap: 12px;
                                        padding: 10px 14px;
                                        border-radius: 8px;
                                        font-size: 13px;
                                        font-weight: 600;
                                        color: var(--text-secondary);
                                        background: transparent;
                                        border: none;
                                        cursor: pointer;
                                        width: 100%;
                                        text-align: left;
                                        transition: background 0.15s, color 0.15s;
                                    }}
                                    .nav-item:hover {{
                                        background: var(--bg-card);
                                        color: var(--text-primary);
                                    }}
                                    .nav-item.active {{
                                        background: var(--bg-card);
                                        color: var(--accent-green-bright);
                                        border-left: 3px solid var(--accent-green);
                                        padding-left: 11px;
                                    }}

                                    .nav-icon {{
                                        font-size: 16px;
                                        line-height: 1;
                                        width: 20px;
                                        text-align: center;
                                    }}

                                    .nav-label {{
                                        flex: 1;
                                    }}

                                    /* Right Content Area */
                                    .app-content {{
                                        flex: 1;
                                        padding: 32px 40px;
                                        overflow-y: auto;
                                        background: var(--bg-sheet);
                                    }}

                                    .content-container {{
                                        max-width: 880px;
                                        margin: 0 auto;
                                    }}

                                    .page-title {{
                                        margin: 0 0 16px 0;
                                        font-size: 20px;
                                        font-weight: 700;
                                        color: var(--text-primary);
                                        letter-spacing: -0.3px;
                                    }}

                                    .tabcontent {{
                                        display: none;
                                        animation: fadeEffect 0.2s ease;
                                    }}
                                    @keyframes fadeEffect {{
                                        from {{ opacity: 0; transform: translateY(4px); }}
                                        to {{ opacity: 1; transform: translateY(0); }}
                                    }}

                                    /* Settings Cards */
                                    .settings-card {{
                                        background: var(--bg-card);
                                        border: 1px solid var(--border-card);
                                        border-radius: 10px;
                                        padding: 14px 18px;
                                        margin-bottom: 8px;
                                        display: flex;
                                        align-items: center;
                                        justify-content: space-between;
                                        gap: 16px;
                                        text-decoration: none;
                                        color: inherit;
                                        transition: background 0.15s, border-color 0.15s;
                                    }}
                                    .settings-card:hover {{
                                        background: var(--bg-card-hover);
                                        border-color: #383838;
                                    }}
                                    .settings-card-left {{
                                        display: flex;
                                        align-items: center;
                                        gap: 14px;
                                        flex: 1;
                                    }}
                                    .settings-card-icon {{
                                        font-size: 18px;
                                        line-height: 1;
                                        width: 24px;
                                        text-align: center;
                                        color: var(--accent-green-bright);
                                        flex-shrink: 0;
                                    }}
                                    .settings-card-title {{
                                        font-weight: 600;
                                        font-size: 13.5px;
                                        color: var(--text-primary);
                                    }}
                                    .settings-card-desc {{
                                        font-size: 12px;
                                        color: var(--text-secondary);
                                        margin-top: 2px;
                                    }}
                                    .settings-card-right {{
                                        font-size: 13px;
                                        color: var(--text-secondary);
                                        display: flex;
                                        align-items: center;
                                        gap: 8px;
                                        flex-shrink: 0;
                                    }}
                                    .settings-card-value {{
                                        font-weight: 600;
                                        color: var(--text-primary);
                                    }}
                                    .badge-update {{
                                        color: #ef4444;
                                        font-weight: 700;
                                    }}

                                    /* Controls inside cards */
                                    .card-select, .card-input {{
                                        min-width: 200px;
                                        max-width: 260px;
                                        margin: 0 !important;
                                        padding: 8px 12px;
                                        font-size: 13px;
                                        background: var(--bg-input);
                                        border: 1px solid var(--border-input);
                                        border-radius: 8px;
                                        color: var(--text-primary);
                                        outline: none;
                                        box-sizing: border-box;
                                        transition: border-color 0.15s, box-shadow 0.15s;
                                    }}
                                    .card-select:focus, .card-input:focus {{
                                        border-color: var(--accent-green);
                                        box-shadow: 0 0 0 2px var(--accent-green-glow);
                                    }}

                                    .card-input[type='time'] {{
                                        min-width: 130px;
                                        max-width: 150px;
                                        text-align: center;
                                        font-family: inherit;
                                        font-size: 13px;
                                        font-weight: 500;
                                        cursor: pointer;
                                        padding: 7px 10px;
                                    }}
                                    .card-input[type='time']::-webkit-calendar-picker-indicator {{
                                        cursor: pointer;
                                        opacity: 0.8;
                                        transition: opacity 0.15s, transform 0.15s;
                                    }}
                                    .card-input[type='time']::-webkit-calendar-picker-indicator:hover {{
                                        opacity: 1;
                                        transform: scale(1.1);
                                    }}

                                    /* Modern WinUI ToggleSwitch */
                                    .switch {{
                                        position: relative;
                                        display: inline-block;
                                        width: 44px;
                                        height: 22px;
                                        margin: 0;
                                        cursor: pointer;
                                    }}
                                    .switch input {{
                                        opacity: 0;
                                        width: 0;
                                        height: 0;
                                    }}
                                    .slider {{
                                        position: absolute;
                                        cursor: pointer;
                                        top: 0; left: 0; right: 0; bottom: 0;
                                        background-color: var(--bg-input);
                                        border: 1px solid var(--border-sheet);
                                        transition: .2s;
                                        border-radius: 22px;
                                    }}
                                    .slider:before {{
                                        position: absolute;
                                        content: "";
                                        height: 16px;
                                        width: 16px;
                                        left: 2px;
                                        bottom: 2px;
                                        background-color: var(--text-muted);
                                        transition: .2s;
                                        border-radius: 50%;
                                    }}
                                    .switch input:checked + .slider {{
                                        background-color: var(--accent-green);
                                        border-color: var(--accent-green);
                                    }}
                                    .switch input:checked + .slider:before {{
                                        transform: translateX(22px);
                                        background-color: #ffffff;
                                    }}

                                    /* SettingsExpander-style subgroups */
                                    .settings-subgroup {{
                                        margin-left: 20px;
                                        padding-left: 12px;
                                        border-left: 2px solid var(--border-card);
                                        margin-bottom: 8px;
                                    }}
                                    .settings-card.sub-card {{
                                        background: rgba(255, 255, 255, 0.02);
                                        padding: 10px 14px;
                                    }}

                                    .btn-clear-logs {{
                                        background: var(--bg-card);
                                        border: 1px solid var(--border-card);
                                        color: var(--text-primary);
                                        padding: 6px 14px;
                                        border-radius: 16px;
                                        font-size: 12px;
                                        font-weight: 500;
                                        cursor: pointer;
                                        display: inline-flex;
                                        align-items: center;
                                        gap: 6px;
                                        transition: background 0.15s;
                                    }}
                                    .btn-clear-logs:hover {{
                                        background: var(--bg-card-hover);
                                        border-color: #383838;
                                    }}

                                    /* Hero Card About */
                                    .hero-about-card {{
                                        background: var(--bg-card);
                                        border: 1px solid var(--border-card);
                                        border-radius: 12px;
                                        padding: 22px;
                                        display: flex;
                                        align-items: center;
                                        gap: 20px;
                                        margin-bottom: 16px;
                                    }}
                                    .hero-icon {{
                                        width: 64px;
                                        height: 64px;
                                        object-fit: contain;
                                        flex-shrink: 0;
                                    }}
                                    .hero-title {{
                                        margin: 0 0 4px 0;
                                        font-size: 18px;
                                        font-weight: 700;
                                        color: var(--accent-green-bright);
                                    }}
                                    .hero-desc {{
                                        margin: 0 0 10px 0;
                                        font-size: 13px;
                                        color: var(--text-secondary);
                                        line-height: 1.5;
                                    }}
                                    .hero-author-link {{
                                        display: inline-flex;
                                        align-items: center;
                                        gap: 6px;
                                        font-size: 12px;
                                        font-weight: 600;
                                        color: var(--accent-green-bright);
                                        text-decoration: none;
                                    }}
                                    .hero-author-link:hover {{
                                        text-decoration: underline;
                                    }}

                                    /* Fixed Bottom Bar */
                                    .app-bottombar {{
                                        background: #0a0a0a;
                                        border-top: 1px solid var(--border-sheet);
                                        padding: 0 28px;
                                        display: flex;
                                        align-items: center;
                                        justify-content: space-between;
                                        gap: 20px;
                                        height: 84px;
                                        flex-shrink: 0;
                                        z-index: 10;
                                    }}

                                    .status-indicator {{
                                        display: flex;
                                        align-items: center;
                                        gap: 10px;
                                        white-space: nowrap;
                                        font-size: 13px;
                                    }}
                                    .status-dot {{
                                        width: 10px;
                                        height: 10px;
                                        border-radius: 50%;
                                        display: inline-block;
                                        flex-shrink: 0;
                                    }}

                                    .links-area {{
                                        flex: 1;
                                        display: flex;
                                        justify-content: center;
                                    }}

                                    .links-card {{
                                        height: 42px;
                                        display: flex;
                                        flex-direction: column;
                                        justify-content: center;
                                        font-size: 13px;
                                        line-height: 20px;
                                        text-align: left;
                                    }}

                                    .links-empty {{
                                        text-align: center;
                                        color: var(--text-muted);
                                    }}

                                    .link-tag {{
                                        color: var(--text-secondary);
                                        font-weight: 600;
                                    }}

                                    .rtsp-link {{
                                        color: var(--accent-green-bright);
                                        text-decoration: none;
                                        font-weight: 500;
                                    }}
                                    .rtsp-link:hover {{ text-decoration: underline; }}

                                    .actions-area {{
                                        display: flex;
                                        align-items: center;
                                        gap: 8px;
                                        flex-shrink: 0;
                                    }}

                                    button {{
                                        font-family: inherit;
                                        border: none;
                                        cursor: pointer;
                                        font-weight: 600;
                                        font-size: 14px;
                                        transition: filter 0.15s, transform 0.1s, background-color 0.15s;
                                    }}
                                    button:hover {{ filter: brightness(1.1); }}
                                    button:active {{ transform: scale(0.98); }}

                                    .btn-start {{
                                        background: var(--accent-green);
                                        color: #ffffff;
                                        padding: 10px 20px;
                                        border-radius: 20px;
                                        white-space: nowrap;
                                    }}

                                    .btn-stop {{
                                        background: var(--danger-red);
                                        color: #ffffff;
                                        padding: 10px 20px;
                                        border-radius: 20px;
                                        white-space: nowrap;
                                    }}

                                    .btn-copy {{
                                        background: var(--bg-card);
                                        border: 1px solid var(--border-card);
                                        color: var(--accent-green-bright);
                                        padding: 10px 14px;
                                        border-radius: 20px;
                                        white-space: nowrap;
                                        font-size: 13px;
                                    }}
                                    .btn-copy:hover {{ background: var(--bg-card-hover); }}

                                    .btn-preview {{
                                        background: var(--bg-card);
                                        border: 1px solid var(--border-card);
                                        color: var(--text-primary);
                                        padding: 10px 14px;
                                        border-radius: 20px;
                                        white-space: nowrap;
                                        font-size: 13px;
                                    }}
                                    .btn-preview:hover {{ background: var(--bg-card-hover); }}

                                    /* Preview Tab Styles */
                                    .preview-player-card {{
                                        background: #000000;
                                        border: 1px solid var(--border-card);
                                        border-radius: 12px;
                                        overflow: hidden;
                                        box-shadow: 0 4px 20px rgba(0,0,0,0.4);
                                    }}
                                    .preview-player-wrapper {{
                                        width: 100%;
                                        height: 520px;
                                        background: #000000;
                                        position: relative;
                                        display: flex;
                                        align-items: center;
                                        justify-content: center;
                                    }}
                                    .preview-player-wrapper iframe {{
                                        width: 100%;
                                        height: 100%;
                                        border: none;
                                    }}
                                    .preview-toolbar {{
                                        padding: 12px 18px;
                                        background: var(--bg-card);
                                        border-top: 1px solid var(--border-card);
                                        display: flex;
                                        align-items: center;
                                        justify-content: space-between;
                                    }}
                                    .preview-meta {{
                                        display: flex;
                                        align-items: center;
                                        gap: 10px;
                                    }}
                                    .stream-indicator-live {{
                                        width: 8px;
                                        height: 8px;
                                        border-radius: 50%;
                                        background: var(--accent-green);
                                        box-shadow: 0 0 8px var(--accent-green);
                                        display: inline-block;
                                    }}
                                    .btn-reload-preview {{
                                        background: var(--bg-sheet);
                                        border: 1px solid var(--border-card);
                                        color: var(--text-primary);
                                        padding: 6px 12px;
                                        border-radius: 6px;
                                        font-size: 12px;
                                        font-weight: 500;
                                        cursor: pointer;
                                        transition: background 0.15s;
                                    }}
                                    .btn-reload-preview:hover {{
                                        background: var(--bg-card-hover);
                                    }}
                                    .preview-empty-state {{
                                        background: var(--bg-card);
                                        border: 1px dashed var(--border-card);
                                        border-radius: 12px;
                                        padding: 60px 20px;
                                        text-align: center;
                                        display: flex;
                                        flex-direction: column;
                                        align-items: center;
                                        justify-content: center;
                                    }}
                                    .preview-empty-icon {{
                                        font-size: 48px;
                                        margin-bottom: 12px;
                                        opacity: 0.7;
                                    }}
                                    .preview-empty-title {{
                                        font-size: 17px;
                                        font-weight: 700;
                                        margin: 0 0 8px 0;
                                        color: var(--text-primary);
                                    }}
                                    .preview-empty-desc {{
                                        font-size: 13px;
                                        color: var(--text-secondary);
                                        max-width: 440px;
                                        margin: 0 0 20px 0;
                                        line-height: 1.5;
                                    }}
                                    .btn-start-stream {{
                                        background: var(--accent-green);
                                        color: #ffffff;
                                        font-weight: 700;
                                        font-size: 13px;
                                        border: none;
                                        border-radius: 20px;
                                        padding: 10px 24px;
                                        cursor: pointer;
                                        transition: transform 0.15s, background 0.15s;
                                    }}
                                    .btn-start-stream:hover {{
                                        background: var(--accent-green-bright);
                                        transform: scale(1.02);
                                    }}

                                    .btn-check {{
                                        background: var(--bg-card);
                                        border: 1px solid var(--border-card);
                                        color: var(--text-primary);
                                        padding: 10px 18px;
                                        border-radius: 18px;
                                        font-size: 13px;
                                        font-weight: 500;
                                        cursor: pointer;
                                        display: inline-flex;
                                        align-items: center;
                                        gap: 8px;
                                        transition: background 0.15s, border-color 0.15s, transform 0.1s;
                                    }}
                                    .btn-check:hover {{
                                        background: var(--bg-card-hover);
                                        border-color: #383838;
                                    }}
                                    .btn-check:active {{
                                        transform: scale(0.98);
                                    }}

                                    .btn-update {{
                                        background: var(--accent-green);
                                        color: #ffffff;
                                        padding: 10px 18px;
                                        border-radius: 18px;
                                        font-size: 13px;
                                        font-weight: 600;
                                        border: none;
                                        cursor: pointer;
                                        display: inline-flex;
                                        align-items: center;
                                        gap: 8px;
                                        transition: background 0.15s, transform 0.1s;
                                    }}
                                    .btn-update:hover {{
                                        background: var(--accent-green-bright);
                                    }}
                                    .btn-update:active {{
                                        transform: scale(0.98);
                                    }}

                                    label {{
                                        font-size: 13px;
                                        font-weight: 500;
                                        color: var(--text-secondary);
                                        display: block;
                                        margin-bottom: 6px;
                                    }}

                                    input[type=text], input[type=password], select {{
                                        width: 100%;
                                        padding: 9px 12px;
                                        background: var(--bg-input);
                                        border: 1px solid var(--border-input);
                                        border-radius: 8px;
                                        color: var(--text-primary);
                                        font-family: inherit;
                                        font-size: 13px;
                                        margin-bottom: 14px;
                                        outline: none;
                                        transition: border-color 0.15s, box-shadow 0.15s;
                                    }}
                                    input[type=text]:focus, input[type=password]:focus, select:focus {{
                                        border-color: var(--accent-green);
                                        box-shadow: 0 0 0 2px var(--accent-green-glow);
                                    }}

                                    .checkbox-item {{
                                        display: flex;
                                        align-items: center;
                                        gap: 10px;
                                        margin: 10px 0;
                                        cursor: pointer;
                                        font-size: 13px;
                                        color: var(--text-primary);
                                    }}

                                    input[type=checkbox] {{
                                        accent-color: var(--accent-green);
                                        width: 18px;
                                        height: 18px;
                                        cursor: pointer;
                                    }}

                                    .flex {{
                                        display: flex;
                                        gap: 14px;
                                    }}
                                    .flex > div {{ flex: 1; }}

                                    .save-indicator {{
                                        position: fixed;
                                        top: 20px;
                                        right: 20px;
                                        background: #162E1E;
                                        border: 1px solid #22C55E;
                                        color: #22C55E;
                                        padding: 6px 14px;
                                        border-radius: 16px;
                                        font-size: 12px;
                                        font-weight: 600;
                                        box-shadow: 0 4px 12px rgba(0,0,0,0.4);
                                        z-index: 1000;
                                    }}

                                    .update-alert {{
                                        background: #2b220c;
                                        border: 1px solid #ca8a04;
                                        color: #fde047;
                                        border-radius: 10px;
                                        padding: 12px;
                                        font-size: 13px;
                                        font-weight: 600;
                                        margin-bottom: 14px;
                                        text-align: center;
                                    }}
                                    body.theme-light .update-alert {{
                                        background: #fefce8;
                                        border-color: #fde047;
                                        color: #a16207;
                                    }}

                                    #log-viewer {{
                                        box-sizing: border-box;
                                        width: 100%;
                                        height: 380px;
                                        background-color: #0b0f17;
                                        color: #4ade80;
                                        font-family: 'Consolas', 'Courier New', monospace;
                                        font-size: 12px;
                                        line-height: 18px;
                                        border: 1px solid var(--border-card);
                                        border-radius: 8px;
                                        padding: 12px;
                                        resize: none;
                                        outline: none;
                                    }}

                                    /* Responsive Adjustments for Mobile */
                                    @media (max-width: 768px) {{
                                        html, body {{
                                            overflow-y: auto;
                                            height: auto;
                                        }}
                                        .app-layout {{
                                            height: auto;
                                            min-height: 100vh;
                                            overflow: visible;
                                        }}
                                        .app-main {{
                                            flex-direction: column;
                                            overflow: visible;
                                        }}
                                        .app-sidebar {{
                                            width: 100%;
                                            flex-direction: row;
                                            overflow-x: auto;
                                            padding: 8px 10px;
                                            border-right: none;
                                            border-bottom: 1px solid var(--border-sheet);
                                        }}
                                        .nav-item {{
                                            white-space: nowrap;
                                            border-left: none !important;
                                            border-bottom: 3px solid transparent;
                                            padding: 8px 12px;
                                        }}
                                        .nav-item.active {{
                                            border-bottom: 3px solid var(--accent-green);
                                        }}
                                        .app-content {{
                                            padding: 18px 16px;
                                            overflow: visible;
                                        }}
                                        .content-container {{
                                            max-width: 100%;
                                        }}
                                        .app-bottombar {{
                                            flex-direction: column;
                                            align-items: stretch;
                                            height: auto;
                                            gap: 12px;
                                            padding: 16px;
                                        }}
                                        .actions-area {{
                                            justify-content: center;
                                            flex-wrap: wrap;
                                        }}
                                        .hero-about-card {{
                                            flex-direction: column;
                                            text-align: center;
                                        }}
                                        .preview-player-wrapper {{
                                            height: 260px;
                                        }}
                                        .settings-card {{
                                            flex-direction: column;
                                            align-items: flex-start;
                                            gap: 12px;
                                        }}
                                        .settings-card-right {{
                                            width: 100%;
                                            justify-content: flex-end;
                                        }}
                                        .card-select, .card-input {{
                                            width: 100%;
                                            max-width: 100%;
                                        }}
                                        .settings-subgroup {{
                                            margin-left: 8px;
                                            padding-left: 8px;
                                        }}
                                    }}
                                </style>
                                <script>
                                    var Main_isTransmitting = {(isTransmitting ? "true" : "false")};
                                    let currentState = '{isTransmitting.ToString().ToLower()}';
                                    
                                    document.addEventListener('DOMContentLoaded', function() {{
                                        var themeSelect = document.querySelector('select[name=""theme""]');
                                        if(themeSelect) {{
                                            themeSelect.addEventListener('change', function(e) {{
                                                var val = e.target.value;
                                                var themeStr = val === '1' ? 'dark' : (val === '2' ? 'light' : 'system');
                                                document.body.className = 'theme-' + themeStr;
                                            }});
                                        }}

                                        var appToast = document.getElementById('appToast');
                                        if(appToast) {{
                                            setTimeout(function() {{
                                                appToast.style.opacity = '0';
                                                appToast.style.transform = 'translateY(-4px)';
                                                setTimeout(function() {{ appToast.style.display = 'none'; }}, 350);
                                            }}, 4000);
                                        }}
                                    }});

                                    // Polling suave de status
                                    setInterval(() => {{
                                        fetch('/status').then(r => r.text()).then(t => {{
                                            if(t.trim() !== '' && t.trim() !== currentState) {{
                                                window.location.reload();
                                            }}
                                        }}).catch(()=>{{}});
                                    }}, 2000);
                                    
                                    // Polling de logs
                                    setInterval(function() {{
                                        var logsTab = document.getElementById('tab-logs');
                                        if(logsTab && logsTab.style.display === 'block') {{
                                            fetch('/logs').then(r=>r.text()).then(t=>{{
                                                var ta = document.getElementById('log-viewer');
                                                if(ta && ta.value !== t) {{
                                                    ta.value = t;
                                                    ta.scrollTop = ta.scrollHeight;
                                                }}
                                            }}).catch(()=>{{}});
                                        }}
                                    }}, 1000);
                                </script>
                                </head><body class='theme-{curThemeStr.ToLower()}'>
                                
                                <div class='app-layout'>
                                    
                                    <!-- Title Bar Header -->
                                    <header class='app-titlebar'>
                                        <div class='brand-area'>
                                            <img src='/icon.png' class='app-icon' alt='Logo'>
                                            <span class='app-name'>{LanguageManager.Get("AppTitle")}</span>
                                            <span class='app-desc'>- {LanguageManager.Get("AppSubtitle")}</span>
                                        </div>
                                        <div class='header-actions'>
                                            {toastHtml}
                                            {statusBadge}
                                        </div>
                                    </header>

                                    <!-- Main Shell: Sidebar + Content -->
                                    <div class='app-main'>
                                        <!-- Left Sidebar -->
                                        <nav class='app-sidebar'>
                                            <button type='button' class='nav-item active' onclick=""openTab(event, 'tab-video')"">
                                                <span class='nav-icon'>🎥</span>
                                                <span class='nav-label'>{LanguageManager.Get("TabVideo")}</span>
                                            </button>
                                            <button type='button' class='nav-item' onclick=""openTab(event, 'tab-preview')"">
                                                <span class='nav-icon'>📺</span>
                                                <span class='nav-label'>{LanguageManager.Get("TabPreview")}</span>
                                            </button>
                                            <button type='button' class='nav-item' onclick=""openTab(event, 'tab-rede')"">
                                                <span class='nav-icon'>🌐</span>
                                                <span class='nav-label'>{LanguageManager.Get("TabNetwork")}</span>
                                            </button>
                                            <button type='button' class='nav-item' onclick=""openTab(event, 'tab-auto')"">
                                                <span class='nav-icon'>⏱️</span>
                                                <span class='nav-label'>{LanguageManager.Get("TabAutomation")}</span>
                                            </button>
                                            <button type='button' class='nav-item' onclick=""openTab(event, 'tab-config')"">
                                                <span class='nav-icon'>⚙️</span>
                                                <span class='nav-label'>{LanguageManager.Get("TabConfig")}</span>
                                            </button>
                                            <button type='button' class='nav-item' onclick=""openTab(event, 'tab-logs')"">
                                                <span class='nav-icon'>📄</span>
                                                <span class='nav-label'>{LanguageManager.Get("TabLogs")}</span>
                                            </button>
                                            <button type='button' class='nav-item' onclick=""openTab(event, 'tab-sobre')"">
                                                <span class='nav-icon'>ℹ️</span>
                                                <span class='nav-label'>{LanguageManager.Get("TabAbout")}</span>
                                            </button>
                                        </nav>

                                        <!-- Right Content Area -->
                                        <main class='app-content'>
                                            <div class='content-container'>
                                                <form id='configForm' method='POST' style='margin:0;'>
                                                <input type='hidden' name='action' value='save'/>
                                                
                                                <div id='save-status-saving' class='save-indicator' style='display:none;'>{LanguageManager.Get("MsgSaving")}</div>
                                                <div id='save-status' class='save-indicator' style='display:none;'>✔ {LanguageManager.Get("MsgSaved")}</div>

                                                <!-- Tab Video -->
                                                <div id='tab-video' class='tabcontent'>
                                                    <h2 class='page-title'>{LanguageManager.Get("TabVideo")}</h2>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🖥️</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("MonitorLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipMonitor")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <select name='monitor' class='card-select' title='{LanguageManager.Get("TipMonitor")}'>{monitorOptions}</select>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>⚙️</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("EncoderLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipEncoder")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <select name='encoder' class='card-select' title='{LanguageManager.Get("TipEncoder")}'>
                                                                <option value='0' {sel(encoderIdx, 0)}>{LanguageManager.Get("EncoderCPU")}</option>
                                                                <option value='1' {sel(encoderIdx, 1)}>{LanguageManager.Get("EncoderNVENC")}</option>
                                                                <option value='2' {sel(encoderIdx, 2)}>{LanguageManager.Get("EncoderQSV")}</option>
                                                            </select>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>💎</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("QualityLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipQuality")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <select name='quality' class='card-select' title='{LanguageManager.Get("TipQuality")}'>
                                                                <option value='0' {sel(qualityIdx, 0)}>{LanguageManager.Get("QualityLow")}</option>
                                                                <option value='1' {sel(qualityIdx, 1)}>{LanguageManager.Get("QualityMedium")}</option>
                                                                <option value='2' {sel(qualityIdx, 2)}>{LanguageManager.Get("QualityHigh")}</option>
                                                            </select>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>⚡</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("FpsLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipFps")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <select name='fps' class='card-select' title='{LanguageManager.Get("TipFps")}'>{fpsOptions}</select>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🔀</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("DualStreamLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipDualStream")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <label class='switch' title='{LanguageManager.Get("TipDualStream")}'>
                                                                <input type='checkbox' name='dual' value='1' {chk(dualOn)}>
                                                                <span class='slider'></span>
                                                            </label>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🖱️</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("MouseCursorLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipMouseCursor")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <label class='switch' title='{LanguageManager.Get("TipMouseCursor")}'>
                                                                <input type='checkbox' name='mouse' value='1' {chk(mouseOn)}>
                                                                <span class='slider'></span>
                                                            </label>
                                                        </div>
                                                    </div>
                                                </div>

                                                <!-- Tab Preview -->
                                                <div id='tab-preview' class='tabcontent'>
                                                    <div style='display:flex; align-items:center; justify-content:space-between; margin-bottom:16px;'>
                                                        <h2 class='page-title' style='margin:0;'>{LanguageManager.Get("TabPreview")}</h2>
                                                        {(isTransmitting ? $"<span class='badge-streaming'>● {LanguageManager.Get("LiveBadge")} WEBRTC</span>" : $"<span style='font-size:12px; color:var(--text-muted); font-weight:600;'>{LanguageManager.Get("OfflineBadge")}</span>")}
                                                    </div>
                                                    {(isTransmitting ? $@"
                                                    <div class='preview-player-card'>
                                                        <div id='preview-tab-player' class='preview-player-wrapper'></div>
                                                        <div class='preview-toolbar'>
                                                            <div class='preview-meta'>
                                                                <span class='stream-indicator-live'></span>
                                                                <span style='font-weight:600; font-size:13px;'>{streamName}</span>
                                                                <span style='color:var(--text-muted); font-size:12px;'>• WebRTC (8880)</span>
                                                            </div>
                                                            <button type='button' class='btn-reload-preview' onclick='reloadPreview()'>🔄 {LanguageManager.Get("BtnReloadPreview")}</button>
                                                        </div>
                                                    </div>" : $@"
                                                    <div class='preview-empty-state'>
                                                        <div class='preview-empty-icon'>📺</div>
                                                        <h3 class='preview-empty-title'>{LanguageManager.Get("PreviewEmptyTitle")}</h3>
                                                        <p class='preview-empty-desc'>{LanguageManager.Get("PreviewEmptyDesc")}</p>
                                                        <button type='button' class='btn-start-stream' onclick=""document.querySelector('.actions-area form button').click()"">▶ {LanguageManager.Get("BtnStart")}</button>
                                                    </div>")}
                                                </div>

                                                <!-- Tab Rede -->
                                                <div id='tab-rede' class='tabcontent'>
                                                    <h2 class='page-title'>{LanguageManager.Get("TabNetwork")}</h2>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>📡</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("StreamNameLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipStreamName")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <input type='text' name='stream' class='card-input' title='{LanguageManager.Get("TipStreamName")}' value='{streamName}'>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🔌</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("PortLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipPort")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <input type='text' name='port' class='card-input' title='{LanguageManager.Get("TipPort")}' value='{portText}'>
                                                        </div>
                                                    </div>

                                                    {(authOn ? $@"
                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🔒</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("AuthLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("AuthEnabledWebDesc")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <span class='badge-streaming' style='font-size:11px; padding:3px 10px;'>🔒 {LanguageManager.Get("AuthProtectedBadge")}</span>
                                                        </div>
                                                    </div>

                                                    <div class='settings-subgroup'>
                                                        <div class='settings-card sub-card'>
                                                            <div class='settings-card-left'>
                                                                <span class='settings-card-icon'>👤</span>
                                                                <div>
                                                                    <div class='settings-card-title'>{LanguageManager.Get("UserLabel")}</div>
                                                                    <div class='settings-card-desc'>{LanguageManager.Get("TipUsername")}</div>
                                                                </div>
                                                            </div>
                                                            <div class='settings-card-right'>
                                                                <input type='text' name='user' class='card-input' title='{LanguageManager.Get("TipUsername")}' value='{userText}'>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card sub-card'>
                                                            <div class='settings-card-left'>
                                                                <span class='settings-card-icon'>🔑</span>
                                                                <div>
                                                                    <div class='settings-card-title'>{LanguageManager.Get("PasswordLabel")}</div>
                                                                    <div class='settings-card-desc'>{LanguageManager.Get("TipPassword")}</div>
                                                                </div>
                                                            </div>
                                                            <div class='settings-card-right'>
                                                                <input type='password' name='pass' class='card-input' title='{LanguageManager.Get("TipPassword")}' placeholder='{LanguageManager.Get("PassPlaceholder")}'>
                                                            </div>
                                                        </div>
                                                    </div>" : $@"
                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🔓</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("AuthLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("AuthDisabledWebDesc")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <span style='font-size:12px; color:var(--text-muted); font-weight:600;'>{LanguageManager.Get("AuthDisabledBadge")}</span>
                                                        </div>
                                                    </div>")}
                                                </div>

                                                <!-- Tab Automacao -->
                                                <div id='tab-auto' class='tabcontent'>
                                                    <h2 class='page-title'>{LanguageManager.Get("TabAutomation")}</h2>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🚀</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("StartWindowsLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipStartWindows")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <label class='switch' title='{LanguageManager.Get("TipStartWindows")}'>
                                                                <input type='checkbox' name='startwin' value='1' {chk(startWinOn)}>
                                                                <span class='slider'></span>
                                                            </label>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>⏱️</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("ScheduleLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipSchedule")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <label class='switch' title='{LanguageManager.Get("TipSchedule")}'>
                                                                <input type='checkbox' name='sched' value='1' {chk(schedOn)} onchange=""document.getElementById('sched-subgroup').style.display = this.checked ? 'block' : 'none';"">
                                                                <span class='slider'></span>
                                                            </label>
                                                        </div>
                                                    </div>

                                                    <div id='sched-subgroup' class='settings-subgroup' style='display:{(schedOn ? "block" : "none")};'>
                                                        <div class='settings-card sub-card'>
                                                            <div class='settings-card-left'>
                                                                <span class='settings-card-icon'>🌅</span>
                                                                <div>
                                                                    <div class='settings-card-title'>{LanguageManager.Get("ScheduleStartLabel")}</div>
                                                                    <div class='settings-card-desc'>{LanguageManager.Get("TipScheduleStart")}</div>
                                                                </div>
                                                            </div>
                                                            <div class='settings-card-right'>
                                                                <input type='time' step='60' name='sched_s' class='card-input' title='{LanguageManager.Get("TipScheduleStart")}' value='{schedStart}'>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card sub-card'>
                                                            <div class='settings-card-left'>
                                                                <span class='settings-card-icon'>🌙</span>
                                                                <div>
                                                                    <div class='settings-card-title'>{LanguageManager.Get("ScheduleEndLabel")}</div>
                                                                    <div class='settings-card-desc'>{LanguageManager.Get("TipScheduleEnd")}</div>
                                                                </div>
                                                            </div>
                                                            <div class='settings-card-right'>
                                                                <input type='time' step='60' name='sched_e' class='card-input' title='{LanguageManager.Get("TipScheduleEnd")}' value='{schedEnd}'>
                                                            </div>
                                                        </div>
                                                    </div>
                                                </div>

                                                <!-- Tab Config -->
                                                <div id='tab-config' class='tabcontent'>
                                                    <h2 class='page-title'>{LanguageManager.Get("TabConfig")}</h2>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🌐</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("LanguageLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipLanguage")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <select name='lang' class='card-select' title='{LanguageManager.Get("TipLanguage")}'>{langOptions}</select>
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🎨</span>
                                                            <div>
                                                                <div class='settings-card-title'>{LanguageManager.Get("ThemeLabel")}</div>
                                                                <div class='settings-card-desc'>{LanguageManager.Get("TipTheme")}</div>
                                                            </div>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <select name='theme' class='card-select' title='{LanguageManager.Get("TipTheme")}'>{themeOptions}</select>
                                                        </div>
                                                    </div>
                                                </div>
                                                
                                                <!-- Tab Logs -->
                                                <div id='tab-logs' class='tabcontent'>
                                                    <div style='display:flex; align-items:center; justify-content:space-between; margin-bottom:14px;'>
                                                        <h2 class='page-title' style='margin:0;'>{LanguageManager.Get("TabLogs")}</h2>
                                                        <button type='button' class='btn-clear-logs' onclick='clearWebLogs()' title='{LanguageManager.Get("TipBtnClearLogs")}'>
                                                            <span style='color:var(--accent-green-bright);'>🗑️</span>
                                                            <span>{LanguageManager.Get("BtnClearLogs")}</span>
                                                        </button>
                                                    </div>
                                                    <textarea id='log-viewer' readonly></textarea>
                                                </div>

                                                <!-- Tab Sobre (Espelhado com o WinUI 3 Nativo) -->
                                                <div id='tab-sobre' class='tabcontent'>
                                                    <h2 class='page-title'>{LanguageManager.Get("TabAbout")}</h2>
                                                    
                                                    <!-- Hero App Card -->
                                                    <div class='hero-about-card'>
                                                        <img src='/icon.png' class='hero-icon' alt='Logo'>
                                                        <div class='hero-details'>
                                                            <h3 class='hero-title'>{LanguageManager.Get("AppTitle")}</h3>
                                                            <p class='hero-desc'>{LanguageManager.Get("AboutDesc")}</p>
                                                            <a href='https://github.com/rogeriomendes/ScreenToRTSP' target='_blank' class='hero-author-link'>
                                                                <span>👨‍💻 {LanguageManager.Get("DevelopedBy")}</span>
                                                                <span style='font-size:11px; opacity:0.8;'>↗</span>
                                                            </a>
                                                        </div>
                                                    </div>

                                                    {updateAlertHtml}

                                                    <!-- Versions Cards -->
                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🏷️</span>
                                                            <span class='settings-card-title'>{LanguageManager.Get("AppTitle")}</span>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <span class='settings-card-value'>{LanguageManager.Get("AboutVersion")}{appVer}</span>{appUpdateBadge}
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>🎞️</span>
                                                            <span class='settings-card-title'>FFmpeg</span>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <span class='settings-card-value'>{LanguageManager.Get("AboutVersion")}{ffmpegVer}</span>{ffmpegUpdateBadge}
                                                        </div>
                                                    </div>

                                                    <div class='settings-card'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>📡</span>
                                                            <span class='settings-card-title'>MediaMTX</span>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <span class='settings-card-value'>{LanguageManager.Get("AboutVersion")}{mtxVer}</span>{mtxUpdateBadge}
                                                        </div>
                                                    </div>

                                                    <a href='https://github.com/rogeriomendes/ScreenToRTSP' target='_blank' class='settings-card' style='cursor:pointer;'>
                                                        <div class='settings-card-left'>
                                                            <span class='settings-card-icon'>📦</span>
                                                            <span class='settings-card-title'>{LanguageManager.Get("ProjectRepository")}</span>
                                                        </div>
                                                        <div class='settings-card-right'>
                                                            <span style='color:var(--accent-green-bright); font-weight:500;'>github.com/rogeriomendes/ScreenToRTSP</span>
                                                            <span style='font-size:12px; color:var(--text-muted);'>↗</span>
                                                        </div>
                                                    </a>

                                                    <!-- Action Buttons -->
                                                    <div style='display:flex; gap:12px; margin-top:16px;'>
                                                        <button type='button' class='btn-check' title='{LanguageManager.Get("TipBtnCheckUpdates")}' onclick=""submitAction('check')"">
                                                            <span style='color:var(--accent-green-bright); font-size:14px;'>🔄</span>
                                                            <span>{LanguageManager.Get("BtnCheckUpdates")}</span>
                                                        </button>
                                                        {updateBtn}
                                                    </div>
                                                </div>

                                            </form>
                                            </div>
                                        </main>
                                    </div>

                                    <!-- Fixed Bottom Bar -->
                                    <footer class='app-bottombar'>
                                        <div class='status-indicator'>
                                            <span class='status-dot' style='background:{statusDotColor};'></span>
                                            <span>{LanguageManager.Get("StatusPrefix")}<b>{statusText}</b></span>
                                        </div>
                                        
                                        <div class='links-area'>
                                            {linksHtml}
                                        </div>

                                        <div class='actions-area'>
                                            {copyBtnsHtml}
                                            <form method='POST' style='display:inline-block; margin:0;'>
                                                <input type='hidden' name='action' value='{btnAction}'/>
                                                <button class='{btnClass}' title='{(isTransmitting ? LanguageManager.Get("TipBtnStop") : LanguageManager.Get("TipBtnStart"))}'>{btnText}</button>
                                            </form>
                                        </div>
                                    </footer>
                                </div>
                                <script>
                                function submitAction(act) {{
                                    let f = document.createElement('form');
                                    f.method = 'POST'; f.action = window.location.pathname + window.location.search + window.location.hash;
                                    let i = document.createElement('input');
                                    i.type = 'hidden'; i.name = 'action'; i.value = act;
                                    f.appendChild(i);
                                    document.body.appendChild(f);
                                    f.submit();
                                }}
                                let saveTimeout;
                                function autoSave() {{
                                    document.getElementById('save-status').style.display = 'block';
                                    document.getElementById('save-status-saving').style.display = 'none';
                                    
                                    let formData = new FormData(document.getElementById('configForm'));
                                    let usp = new URLSearchParams(formData);
                                    fetch('/', {{ method: 'POST', body: usp, headers: {{'Content-Type': 'application/x-www-form-urlencoded'}} }}).then(r => {{}}).catch(e => {{}});
                                    setTimeout(() => document.getElementById('save-status').style.display = 'none', 2000);
                                }}
                                
                                document.addEventListener('DOMContentLoaded', function() {{
                                    let hash = window.location.hash.substring(1);
                                    if (!hash) hash = 'tab-video';
                                    let tabBtn = document.querySelector('.nav-item[onclick*=""' + hash + '""]');
                                    if (tabBtn) tabBtn.click();
                                    else document.querySelector('.nav-item').click();

                                    const form = document.getElementById('configForm');
                                    const inputs = form.querySelectorAll('input, select');
                                    inputs.forEach(input => {{
                                        input.addEventListener('input', function() {{
                                            document.getElementById('save-status-saving').style.display = 'block';
                                            document.getElementById('save-status').style.display = 'none';
                                            clearTimeout(saveTimeout);
                                            saveTimeout = setTimeout(autoSave, 500);
                                        }});
                                        input.addEventListener('change', function() {{
                                            if(input.type === 'checkbox' || input.tagName === 'SELECT' || input.type === 'time') {{
                                                document.getElementById('save-status-saving').style.display = 'block';
                                                document.getElementById('save-status').style.display = 'none';
                                                clearTimeout(saveTimeout);
                                                saveTimeout = setTimeout(autoSave, 500);
                                            }}
                                        }});
                                    }});
                                }});
                                </script>
                                <script>
                                function openTab(evt, tabName) {{
                                    var i, tabcontent, navitems;
                                    tabcontent = document.getElementsByClassName('tabcontent');
                                    for (i = 0; i < tabcontent.length; i++) tabcontent[i].style.display = 'none';
                                    navitems = document.getElementsByClassName('nav-item');
                                    for (i = 0; i < navitems.length; i++) navitems[i].className = navitems[i].className.replace(' active', '');
                                    var targetTab = document.getElementById(tabName);
                                    if (targetTab) targetTab.style.display = 'block';

                                    if (evt && evt.currentTarget) {{
                                        evt.currentTarget.className += ' active';
                                    }} else {{
                                        var btn = document.querySelector('.nav-item[onclick*=""' + tabName + '""]');
                                        if (btn && !btn.className.includes('active')) btn.className += ' active';
                                    }}
                                    history.replaceState(null, null, '#' + tabName);

                                    var player = document.getElementById('preview-tab-player');
                                    if (player) {{
                                        if (tabName === 'tab-preview' && Main_isTransmitting) {{
                                            if (!player.querySelector('iframe')) {{
                                                player.innerHTML = ""<iframe src='http://"" + window.location.hostname + "":8880/{streamName}/' style='width:100%; height:100%; border:none;' allowfullscreen></iframe>"";
                                            }}
                                        }} else {{
                                            player.innerHTML = '';
                                        }}
                                    }}
                                }}
                                function openTabByName(tabName) {{
                                    var btn = document.querySelector('.nav-item[onclick*=""' + tabName + '""]');
                                    if (btn) btn.click();
                                }}
                                function showPreview() {{
                                    openTabByName('tab-preview');
                                }}
                                function reloadPreview() {{
                                    var player = document.getElementById('preview-tab-player');
                                    if (player && Main_isTransmitting) {{
                                        player.innerHTML = ""<iframe src='http://"" + window.location.hostname + "":8880/{streamName}/?t="" + new Date().getTime() + ""' style='width:100%; height:100%; border:none;' allowfullscreen></iframe>"";
                                    }}
                                }}
                                function clearWebLogs() {{
                                    var ta = document.getElementById('log-viewer');
                                    if (ta) ta.value = '';
                                    fetch('/clearlogs').catch(function(){{}});
                                }}
                                function copyText(text) {{
                                    navigator.clipboard.writeText(text).then(function() {{
                                        alert('{LanguageManager.Get("MsgCopied")}\n' + text);
                                    }});
                                }}
                                </script>
                                </body></html>";

                            byte[] buffer = Encoding.UTF8.GetBytes(html);
                            response.ContentLength64 = buffer.Length;
                            using (var os = response.OutputStream)
                            {
                                os.Write(buffer, 0, buffer.Length);
                            }
                        }
                        catch (Exception ___ex)
                        {
                            Console.WriteLine("WebPanel error: " + ___ex.ToString());
                        }
                    });
                }
                catch { }
            }
        }
    }
}
