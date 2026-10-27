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
                                        string[] parts = decoded.Split(':');
                                        if (parts.Length == 2 && parts[0] == reqUser && parts[1] == reqPass) authorized = true;
                                    }
                                    catch { }
                                }
                                if (!authorized)
                                {
                                    response.StatusCode = 401;
                                    response.AddHeader("WWW-Authenticate", "Basic realm=\"Painel RTSP\"");
                                    byte[] err = Encoding.UTF8.GetBytes("Acesso Negado. Senha incorreta.");
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
                                            bool hasUpdate = DependencyManager.CheckForNewVersionsAsync(true).GetAwaiter().GetResult();
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
                                                    form.chkAuth.IsOn = p.ContainsKey("auth");
                                                    if (p.ContainsKey("user")) form.txtUsername.Text = p["user"];
                                                    if (p.ContainsKey("pass") && !string.IsNullOrEmpty(p["pass"])) form.txtPassword.Password = p["pass"];
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
                                else if (action == "updated") response.Redirect("/?updated=1");
                                else if (action == "error") response.Redirect("/?error=1");
                                else if (action == "hasupdate") response.Redirect("/?hasupdate=1");
                                else if (action == "noupdate") response.Redirect("/?noupdate=1");
                                else if (action == "checkerror") response.Redirect("/?checkerror=1");
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

                            string successMsg = "";
                            if (isSaved)
                            {
                                successMsg = $"<div id='successMsg' style='background-color:#dcfce7; color:#166534; padding:10px; margin-bottom:15px; border-radius:5px; text-align:center; border:1px solid #bbf7d0;'><b>{LanguageManager.Get("MsgSaveSuccess")}</b></div><script>setTimeout(function(){{ var el = document.getElementById('successMsg'); if(el) el.style.display='none'; }}, 4000);</script>";
                            }
                            else if (isUpdated)
                            {
                                successMsg = $"<div id='successMsg' style='background-color:#dcfce7; color:#166534; padding:10px; margin-bottom:15px; border-radius:5px; text-align:center; border:1px solid #bbf7d0;'><b>{LanguageManager.Get("MsgUpdateSuccess")}</b></div><script>setTimeout(function(){{ var el = document.getElementById('successMsg'); if(el) el.style.display='none'; }}, 4000);</script>";
                            }
                            else if (noUpdateQ)
                            {
                                successMsg = $"<div id='successMsg' style='background-color:#dbeafe; color:#1e40af; padding:10px; margin-bottom:15px; border-radius:5px; text-align:center; border:1px solid #bfdbfe;'><b>{LanguageManager.Get("MsgNoUpdate")}</b></div><script>setTimeout(function(){{ var el = document.getElementById('successMsg'); if(el) el.style.display='none'; }}, 4000);</script>";
                            }
                            else if (hasUpdateQ)
                            {
                                successMsg = $"<div id='successMsg' style='background-color:#fef08a; color:#854d0e; padding:10px; margin-bottom:15px; border-radius:5px; text-align:center; border:1px solid #fde047;'><b>{LanguageManager.Get("UpdateAvailable")}</b></div><script>setTimeout(function(){{ var el = document.getElementById('successMsg'); if(el) el.style.display='none'; }}, 4000);</script>";
                            }
                            else if (isError)
                            {
                                successMsg = $"<div id='successMsg' style='background-color:#fee2e2; color:#991b1b; padding:10px; margin-bottom:15px; border-radius:5px; text-align:center; border:1px solid #fecaca;'><b>{LanguageManager.Get("MsgStopBeforeUpdate")}</b></div><script>setTimeout(function(){{ var el = document.getElementById('successMsg'); if(el) el.style.display='none'; }}, 4000);</script>";
                            }
                            else if (checkErrorQ)
                            {
                                successMsg = $"<div id='successMsg' style='background-color:#fee2e2; color:#991b1b; padding:10px; margin-bottom:15px; border-radius:5px; text-align:center; border:1px solid #fecaca;'><b>{LanguageManager.Get("MsgCheckUpdateError")}</b></div><script>setTimeout(function(){{ var el = document.getElementById('successMsg'); if(el) el.style.display='none'; }}, 4000);</script>";
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

                            string status = isTransmitting
                                ? $"<b style='color:green'>● {LanguageManager.Get("StatusPrefix")}{LanguageManager.Get("StatusRunning")}</b>"
                                : $"<b style='color:red'>● {LanguageManager.Get("StatusPrefix")}{LanguageManager.Get("StatusStopped")}</b>";
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
                                linksHtml += $@"<div class='links-card' style='text-align:left; font-size:14px; line-height:20px; margin-top:15px; padding:10px; border-radius:5px; height:40px; display:flex; flex-direction:column; justify-content:center;'>
                                    <div><b>Principal:</b> <a href='{lanUrl}' style='color:#3b82f6; text-decoration:none;'>{lanUrl}</a></div>";
                                if (dualOn)
                                {
                                    linksHtml += $"<div><b>Leve:</b> <a href='{lanLowUrl}' style='color:#3b82f6; text-decoration:none;'>{lanLowUrl}</a></div>";
                                }
                                linksHtml += "</div>";

                                copyBtnsHtml += $@"<button type='button' class='btn-outline' style='width:auto; flex:0 0 auto; font-size:14px; margin:0; padding:12px 14px;' onclick=""copyText('{lanUrl}')"">📋 {LanguageManager.Get("BtnCopyLan")}</button>";
                                if (dualOn)
                                {
                                    copyBtnsHtml += $@"<button type='button' class='btn-outline' style='width:auto; flex:0 0 auto; font-size:14px; margin:0; padding:12px 14px;' onclick=""copyText('{lanLowUrl}')"">📋 {LanguageManager.Get("BtnCopyLocal")}</button>";
                                }
                            }
                            else
                            {
                                linksHtml += $@"<div class='links-card' style='text-align:center; font-size:14px; line-height:20px; margin-top:15px; padding:10px; border-radius:5px; height:40px; display:flex; flex-direction:column; justify-content:center; opacity:0.6;'>
                                    <div>{LanguageManager.Get("StatusWaitingLinks")}</div>
                                </div>";
                            }

                            string updateHtml = "";
                            if (DependencyManager.HasUpdateAvailable)
                            {
                                string lblMsg = LanguageManager.Get("UpdateAvailable");
                                updateHtml += $@"
                                    <div style='margin-top:15px; padding:10px; border:1px solid #fecaca; background:#fee2e2; border-radius:8px;'>
                                        <p style='color:#dc2626; font-weight:bold; font-size:14px; margin:0;'>{lblMsg}</p>
                                    </div>";
                            }

                            string updateBtn = DependencyManager.HasUpdateAvailable
                                ? $@"<button type='button' style='background:#16a34a; margin:0; padding:8px; font-size:14px;' onclick=""if(Main_isTransmitting) {{ if(confirm('{LanguageManager.Get("MsgStopBeforeUpdate")}')) submitAction('update'); }} else {{ submitAction('update'); }}"">{LanguageManager.Get("BtnUpdateBinaries")}</button>"
                                : "";

                            updateHtml += $@"
                                    <div class='flex' style='margin-top:15px;'>
                                        <div><button type='button' style='background:#3b82f6; margin:0; padding:8px; font-size:14px;' onclick=""submitAction('check')"">{LanguageManager.Get("BtnCheckUpdates")}</button></div>
                                        <div>{updateBtn}</div>
                                    </div>";

                            string appVer = Updater.GetAppVersion();
                            string ffText = "FFmpeg " + LanguageManager.Get("AboutVersion") + DependencyManager.GetFFmpegVersion();
                            if (DependencyManager.HasFfmpegUpdate) ffText += " <span style='color:#dc2626; font-weight:bold;'>(" + LanguageManager.Get("NewVersion") + DependencyManager.FfmpegLatestVersionStr + ")</span>";
                            string mtxText = "MediaMTX " + LanguageManager.Get("AboutVersion") + DependencyManager.GetMediaMTXVersion();
                            if (DependencyManager.HasMtxUpdate) mtxText += " <span style='color:#dc2626; font-weight:bold;'>(" + LanguageManager.Get("NewVersion") + DependencyManager.MtxLatestVersionStr + ")</span>";

                            string versionsHtml = $@"
                                <div class='info-card' style='margin-top:15px; padding:10px; border-radius:8px; font-size:13px;'>
                                    <strong>{LanguageManager.Get("AboutVersion")}{appVer}</strong><br/>
                                    {ffText}<br/>
                                    {mtxText}
                                </div>
                            ";

                            string html = $@"<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>
                                <link rel='icon' type='image/png' href=""/icon.png"">
                                <title>{LanguageManager.Get("AppTitle")}</title>
                                <style>
                                    body{{font-family:Segoe UI, sans-serif; background:#f0f2f5; padding:20px; color:#333; transition: background 0.3s, color 0.3s;}} 
                                    .card{{background:#fff; padding:25px; border-radius:10px; max-width:680px; margin:auto; box-shadow:0 4px 12px rgba(0,0,0,0.1); transition: background 0.3s;}} 
                                    button{{padding:12px; border:none; border-radius:8px; cursor:pointer; color:#fff; font-weight:bold; width:100%; font-size:16px; margin-bottom:10px; transition: transform 0.1s, filter 0.2s;}} 
                                    button:hover{{filter:brightness(1.1);}} button:active{{transform:scale(0.98);}}
                                    .btn-start{{background:#16a34a;}} .btn-stop{{background:#e2e8f0; color:#334155;}} .btn-save{{background:#0f172a; margin-top:15px; color:#ffffff;}}
                                    .status-card{{background:#f9f9f9; border:1px solid transparent;}} .links-card{{background:#fff; border:1px solid #eee;}} .info-card{{background:#f8fafc; border:1px solid #e2e8f0; color:#475569;}}
                                    .btn-outline{{background:#ffffff; color:#0f172a; border:1px solid #cbd5e1;}}
                                    input[type=text], input[type=password], select {{width:100%; padding:10px; margin:5px 0 15px; border:1px solid #cbd5e1; border-radius:8px; box-sizing:border-box; background:#fff; color:#333; transition: background 0.3s, color 0.3s;}}
                                    input[type=checkbox] {{accent-color:#16a34a; width:18px; height:18px; cursor:pointer;}}
                                    .flex {{display:flex; justify-content:space-between; gap:10px;}}
                                    .flex div {{width:50%;}}
                                    hr {{border:0; border-top:1px solid #eee; margin:20px 0; transition: border-color 0.3s;}}
                                    .tabs {{display:flex; border-bottom:1px solid #ccc; margin-bottom:15px; transition: border-color 0.3s;}}
                                    .tablinks {{background:none; color:#333; padding:10px 15px; cursor:pointer; font-weight:bold; opacity:0.6; transition: opacity 0.2s, border-bottom 0.2s;}}
                                    .tablinks:hover {{opacity:0.8;}}
                                    .tablinks.active {{opacity:1; border-bottom:3px solid #3b82f6;}}
                                    .tabcontent {{display:none; animation:fadeEffect 0.3s;}}
                                    @keyframes fadeEffect {{from {{opacity:0; transform:translateY(5px);}} to {{opacity:1; transform:translateY(0);}}}}
                                    
                                    .theme-dark {{ background:#0f172a; color:#f8fafc; }}
                                    .theme-dark .card {{ background:#1e293b; box-shadow:0 4px 12px rgba(0,0,0,0.5); }}
                                    .theme-dark input[type=text], .theme-dark input[type=password], .theme-dark select {{ background:#334155; color:#f8fafc; border-color:#475569; }}
                                    .theme-dark hr {{ border-top-color:#334155; }}
                                    .theme-dark .tabs {{ border-bottom-color:#475569; }}
                                    .theme-dark .tablinks {{ color:#cbd5e1; }}
                                    .theme-dark .status-card {{ background:#0f172a; border-color:#334155; }}
                                    .theme-dark .links-card {{ background:#1e293b; border-color:#334155; }}
                                    .theme-dark .info-card {{ background:#0f172a; border-color:#334155; color:#cbd5e1; }}
                                    .theme-dark .btn-outline {{ background:#1e293b; color:#cbd5e1; border-color:#475569; }}
                                    .theme-dark .btn-stop {{ background:#334155; color:#f8fafc; }}
                                    
                                    @media (prefers-color-scheme: dark) {{
                                        .theme-system {{ background:#0f172a; color:#f8fafc; }}
                                        .theme-system .card {{ background:#1e293b; box-shadow:0 4px 12px rgba(0,0,0,0.5); }}
                                        .theme-system input[type=text], .theme-system input[type=password], .theme-system select {{ background:#334155; color:#f8fafc; border-color:#475569; }}
                                        .theme-system hr {{ border-top-color:#334155; }}
                                        .theme-system .tabs {{ border-bottom-color:#475569; }}
                                        .theme-system .tablinks {{ color:#cbd5e1; }}
                                        .theme-system .status-card {{ background:#0f172a; border-color:#334155; }}
                                        .theme-system .links-card {{ background:#1e293b; border-color:#334155; }}
                                        .theme-system .info-card {{ background:#0f172a; border-color:#334155; color:#cbd5e1; }}
                                        .theme-system .btn-outline {{ background:#1e293b; color:#cbd5e1; border-color:#475569; }}
                                        .theme-system .btn-stop {{ background:#334155; color:#f8fafc; }}
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
                                    }});

                                    setInterval(() => {{
                                        fetch('/status').then(r => r.text()).then(t => {{
                                            if(t.trim() !== '' && t.trim() !== currentState) window.location.reload();
                                        }}).catch(()=>{{}});
                                    }}, 2000);
                                    
                                    setInterval(function() {{
                                        if(document.getElementById('tab-logs').style.display === 'block') {{
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
                                </head><body class='theme-{curThemeStr.ToLower()}'><div class='card'>
                                {successMsg}
                                <div style='display:flex; align-items:center; justify-content:center; gap:15px; margin-bottom:20px; padding-bottom:15px; border-bottom:1px solid #ccc;'>
                                    <img src='/icon.png' style='width:48px; height:48px; object-fit:contain;' alt='Logo'>
                                    <h2 style='margin:0; color:#16a34a;'>{LanguageManager.Get("AppTitle")}</h2>
                                </div>
                                <div class='status-card' style='text-align:center; padding:15px; border-radius:5px; margin-bottom:20px;'>
                                    <p style='font-size:18px; margin:0;'>{LanguageManager.Get("StatusStopped").Replace("Aguardando", "")} {status}</p>
                                    {linksHtml}
                                </div>
                                
                                <div style='display:flex; gap:10px;'>
                                    <form method='POST' style='flex:1; margin:0;'>
                                        <input type='hidden' name='action' value='{btnAction}'/>
                                        <button class='{btnClass}' style='margin:0;'>{btnText}</button>
                                    </form>
                                    {copyBtnsHtml}
                                    {(isTransmitting ? $"<button type='button' class='btn-outline' onclick=\"showPreview()\" style='width:auto; flex:0 0 auto; padding:12px 14px; margin:0;'>📺 {LanguageManager.Get("TabPreview")}</button>" : "")} 
                                </div>
                                
                                <hr>
                                <form id='configForm' method='POST'><input type='hidden' name='action' value='save'/>
                                <div style='position:relative;'>
                                    <div id='save-status-saving' style='display:none; position:absolute; top:-25px; right:0; color:#16a34a; font-size:14px; font-weight:bold;'>{LanguageManager.Get("MsgSaving")}</div>
                                    <div id='save-status' style='display:none; position:absolute; top:-25px; right:0; color:#16a34a; font-size:14px; font-weight:bold;'>✔ {LanguageManager.Get("MsgSaved")}</div>
                                </div>
                                <div class='tabs'>
                                    <div class='tablinks' onclick=""openTab(event, 'tab-video')"">{LanguageManager.Get("TabVideo")}</div>
                                    <div class='tablinks' onclick=""openTab(event, 'tab-rede')"">{LanguageManager.Get("TabNetwork")}</div>
                                    <div class='tablinks' onclick=""openTab(event, 'tab-auto')"">{LanguageManager.Get("TabAutomation")}</div>
                                    <div class='tablinks' onclick=""openTab(event, 'tab-config')"">{LanguageManager.Get("TabConfig")}</div>
                                    <div class='tablinks' onclick=""openTab(event, 'tab-logs')"">Logs</div>
                                    <div class='tablinks' onclick=""openTab(event, 'tab-sobre')"">{LanguageManager.Get("TabAbout")}</div>
                                </div>

                                <div id='tab-video' class='tabcontent'>
                                    <div class='flex'>
                                        <div><label title='{LanguageManager.Get("TipMonitor")}'>{LanguageManager.Get("MonitorLabel")}</label><br><select name='monitor' title='{LanguageManager.Get("TipMonitor")}'>{monitorOptions}</select></div>
                                        <div><label title='{LanguageManager.Get("TipFps")}'>{LanguageManager.Get("FpsLabel")}</label><br><select name='fps' title='{LanguageManager.Get("TipFps")}'>{fpsOptions}</select></div>
                                    </div>
                                    <div class='flex'>
                                        <div><label title='{LanguageManager.Get("TipEncoder")}'>{LanguageManager.Get("EncoderLabel")}</label><br><select name='encoder' title='{LanguageManager.Get("TipEncoder")}'>
                                            <option value='0' {sel(encoderIdx, 0)}>{LanguageManager.Get("EncoderCPU")}</option>
                                            <option value='1' {sel(encoderIdx, 1)}>{LanguageManager.Get("EncoderNVENC")}</option>
                                            <option value='2' {sel(encoderIdx, 2)}>{LanguageManager.Get("EncoderQSV")}</option>
                                        </select></div>
                                        <div><label title='{LanguageManager.Get("TipQuality")}'>{LanguageManager.Get("QualityLabel")}</label><br><select name='quality' title='{LanguageManager.Get("TipQuality")}'>
                                            <option value='0' {sel(qualityIdx, 0)}>{LanguageManager.Get("QualityLow")}</option>
                                            <option value='1' {sel(qualityIdx, 1)}>{LanguageManager.Get("QualityMedium")}</option>
                                            <option value='2' {sel(qualityIdx, 2)}>{LanguageManager.Get("QualityHigh")}</option>
                                        </select></div>
                                    </div>
                                    <label title='{LanguageManager.Get("TipDualStream")}'><input type='checkbox' name='dual' value='1' {chk(dualOn)}> {LanguageManager.Get("DualStreamLabel")}</label><br><br>
                                    <label title='{LanguageManager.Get("TipMouseCursor")}'><input type='checkbox' name='mouse' value='1' {chk(mouseOn)}> {LanguageManager.Get("MouseCursorLabel")}</label><br>
                                </div>

                                <div id='tab-rede' class='tabcontent'>
                                    <div class='flex'>
                                        <div><label>{LanguageManager.Get("StreamNameLabel")}</label><br><input type='text' name='stream' value='{streamName}'></div>
                                        <div><label>{LanguageManager.Get("PortLabel")}</label><br><input type='text' name='port' value='{portText}'></div>
                                    </div>

                                    <label><input type='checkbox' name='auth' value='1' {chk(authOn)}> <b>{LanguageManager.Get("AuthLabel")}</b></label><br><br>
                                    <div class='flex'>
                                        <div><label>{LanguageManager.Get("UserLabel")}</label><br><input type='text' name='user' value='{userText}'></div>
                                        <div><label>{LanguageManager.Get("PasswordLabel")}</label><br><input type='password' name='pass' placeholder='{LanguageManager.Get("PassPlaceholder")}'></div>
                                    </div>
                                </div>

                                <div id='tab-auto' class='tabcontent'>
                                    <label title='{LanguageManager.Get("TipStartWindows")}'><input type='checkbox' name='startwin' value='1' {chk(startWinOn)}> {LanguageManager.Get("StartWindowsLabel")}</label><br><br>
                                    <label title='{LanguageManager.Get("TipSchedule")}'><input type='checkbox' name='sched' value='1' {chk(schedOn)}> {LanguageManager.Get("ScheduleLabel")}</label>
                                    <div class='flex' style='margin-top:10px'>
                                        <div><label>{LanguageManager.Get("ScheduleStartLabel")}</label><br><input type='text' name='sched_s' value='{schedStart}' placeholder='05:45'></div>
                                        <div><label>{LanguageManager.Get("ScheduleEndLabel")}</label><br><input type='text' name='sched_e' value='{schedEnd}' placeholder='22:15'></div>
                                    </div>
                                </div>

                                <div id='tab-config' class='tabcontent'>
                                    <div><label title='{LanguageManager.Get("TipTheme")}'>{LanguageManager.Get("ThemeLabel")}</label><br><select name='theme'>{themeOptions}</select></div><br>
                                    <div><label title='{LanguageManager.Get("TipLanguage")}'>{LanguageManager.Get("LanguageLabel")}</label><br><select name='lang'>{langOptions}</select></div><br>
                                </div>
                                
                                <div id='tab-logs' class='tabcontent' style='padding:0'>
                                    <textarea id='log-viewer' readonly style='box-sizing:border-box; width:100%; height:300px; background-color:#0f172a; color:#f8fafc; font-family:Consolas, monospace; border:none; padding:15px; resize:none;'></textarea>
                                </div>

                                <div id='tab-sobre' class='tabcontent'>
                                    <div style='text-align:center; padding: 20px 0;'>
                                        <h3 style='color:#16a34a; margin:0;'>{LanguageManager.Get("AppTitle")}</h3>
                                        <p style='color:#475569; font-size:14px;'>{LanguageManager.Get("AboutDesc")}</p>
                                        {versionsHtml}
                                        {updateHtml}
                                    </div>
                                </div>

                                </form>
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
                                    let tabBtn = document.querySelector('.tablinks[onclick*=""' + hash + '""]');
                                    if (tabBtn) tabBtn.click();
                                    else document.querySelector('.tablinks').click();

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
                                            if(input.type === 'checkbox' || input.tagName === 'SELECT') {{
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
                                    var i, tabcontent, tablinks;
                                    tabcontent = document.getElementsByClassName('tabcontent');
                                    for (i = 0; i < tabcontent.length; i++) tabcontent[i].style.display = 'none';
                                    tablinks = document.getElementsByClassName('tablinks');
                                    for (i = 0; i < tablinks.length; i++) tablinks[i].className = tablinks[i].className.replace(' active', '');
                                    document.getElementById(tabName).style.display = 'block';
                                    evt.currentTarget.className += ' active';
                                    history.replaceState(null, null, '#' + tabName);
                                }}
                                function copyText(text) {{
                                    navigator.clipboard.writeText(text).then(function() {{
                                        alert('Link copiado: ' + text);
                                    }});
                                }}
                                </script>
                                <div id='previewModal' style='display:none; position:fixed; z-index:9999; left:0; top:0; width:100%; height:100%; background-color:rgba(0,0,0,0.8); align-items:center; justify-content:center;'>
                                    <div style='position:relative; width:95%; max-width:800px; background:#0f172a; border-radius:10px; padding:15px; box-shadow:0 4px 15px rgba(0,0,0,0.5);'>
                                        <span onclick='hidePreview()' style='color:#cbd5e1; position:absolute; right:15px; top:10px; font-size:28px; font-weight:bold; cursor:pointer;'>&times;</span>
                                        <h3 style='color:#f8fafc; margin:0 0 10px 0;'>{LanguageManager.Get("TabPreview")}</h3>
                                        <div id='previewContainer' style='width:100%; height:450px; background:#000; border-radius:5px; overflow:hidden;'></div>
                                    </div>
                                </div>
                                <script>
                                    function showPreview() {{
                                        document.getElementById('previewContainer').innerHTML = ""<iframe src='http://"" + window.location.hostname + "":8880/{streamName}/' style='width:100%; height:100%; border:none;' allowfullscreen></iframe>"";
                                        document.getElementById('previewModal').style.display='flex';
                                    }}
                                    function hidePreview() {{
                                        document.getElementById('previewModal').style.display='none';
                                        document.getElementById('previewContainer').innerHTML = '';
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
