using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenToRTSP.Core;
using ScreenToRTSP.I18n;
using ScreenToRTSP.UI;

namespace ScreenToRTSP
{
    public partial class Main : Form
    {
        internal TextBox txtStreamName;
        internal CheckBox chkAuth;
        internal CheckBox chkDualStream;
        internal CheckBox chkMouseCursor;
        internal CheckBox chkSchedule;
        internal MaskedTextBox txtScheduleStart;
        internal MaskedTextBox txtScheduleEnd;
        internal TextBox txtPort;
        internal TextBox txtUsername;
        internal TextBox txtPassword;
        internal ComboBox cmbLanguage;
        internal CheckBox chkStartWithWindows;
        internal Button btnStart;
        internal Label lblStatus;
        internal Label lblUrl;
        internal Label lblWebUrl;
        internal NotifyIcon trayIcon;
        
        
        internal System.Windows.Forms.Timer watchdogTimer;
        internal bool isTransmitting = false;
        internal bool isForceClosing = false;
        internal string currentPassword = "";
        internal bool langChanged = false;
        internal bool isLoading = false;
        internal System.Windows.Forms.Timer autoSaveTimer;
        internal System.Windows.Forms.Timer saveStatusTimer;
        internal Label lblSaveStatus;

        internal string currentLanUrl = "";
        internal string currentLocalUrl = "";
        internal string currentLanLowUrl = "";
        internal string currentWebUrl = "";

        internal Button btnCopyLan;
        internal Button btnCopyLocal;
        internal Button btnCopyWeb;

        internal ComboBox cmbEncoder;
        internal ComboBox cmbQuality;
        internal ComboBox cmbMonitor;
        internal ComboBox cmbFps;
        internal RichTextBox rtbLogs;
        
        // Web Server
        internal CheckBox chkWebServer;
        internal TextBox txtWebPort;
        internal HttpListener httpListener;
        internal Thread webServerThread;
        internal bool isWebServerRunning = false;

        internal string AppDataPath => AppDomain.CurrentDomain.BaseDirectory;

        internal WebPanel webPanel;
        internal MediaServer mediaServer;
        internal ConfigManager configManager;


        public Main()
        {
            InitializeComponent();
            configManager = new ConfigManager();
            var cfg = configManager.Load();
            string lang = cfg.ContainsKey("Language") ? cfg["Language"] : "System";
            ScreenToRTSP.I18n.LanguageManager.ApplyLanguage(lang);
            
            webPanel = new WebPanel(this);
            mediaServer = new MediaServer();
            ScreenToRTSP.UI.UIManager.Build(this);
            LoadSettings();
                        CheckStartupRegistry();

            System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = (int)TimeSpan.FromHours(24).TotalMilliseconds; // Checa a cada 24 horas
            updateTimer.Tick += async (s, e) => {
                await RunBackgroundUpdateCheck();
            };
            updateTimer.Start();
            
            trayIcon.BalloonTipClicked += async (s, e) => {
                if (!string.IsNullOrEmpty(pendingUpdateUrl)) {
                    await Core.Updater.PerformUpdate(pendingUpdateUrl, isTransmitting);
                }
                else if (hasPendingBinUpdate) {
                    bool wasTransmitting = isTransmitting;
                    if (wasTransmitting) StopClick();
                    Core.DependencyManager.CheckAndDownloadDependencies(true);
                    if (wasTransmitting) StartClick();
                    hasPendingBinUpdate = false;
                }
            };
            
            _ = RunBackgroundUpdateCheck();

            autoSaveTimer = new System.Windows.Forms.Timer();
            autoSaveTimer.Interval = 500;
            autoSaveTimer.Tick += async (s, e) => {
                autoSaveTimer.Stop();
                if (!isLoading) {
                    SaveSettings();
                    if (lblSaveStatus != null) {
                        lblSaveStatus.Text = "✔ " + ScreenToRTSP.I18n.LanguageManager.Get("MsgSaved");
                        saveStatusTimer.Stop();
                        saveStatusTimer.Start();
                    }
                    if (isTransmitting) {
                        StopClick();
                        await System.Threading.Tasks.Task.Delay(500);
                        StartClick();
                    }
                }
            };
            AttachAutoSave(this);

            saveStatusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            saveStatusTimer.Tick += (s, e) => {
                saveStatusTimer.Stop();
                if (lblSaveStatus != null) lblSaveStatus.Visible = false;
            };

            if (chkWebServer.Checked) webPanel.Start();
            UpdateUrlStatus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                webPanel?.Stop();
                trayIcon?.Dispose();
                
                
                watchdogTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int useImmersiveDarkMode = 1;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(800, 800);
            this.Name = "Main";
            this.Text = "Screen to RTSP";
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.FormClosing += Form1_FormClosing;
            this.Resize += Form1_Resize;
            this.BackColor = Color.FromArgb(15, 23, 42); // Will be overridden by UIManager.Build
            this.ResumeLayout(false);

            watchdogTimer = new System.Windows.Forms.Timer();
            watchdogTimer.Interval = 5000; // 5s for schedule check
            watchdogTimer.Tick += WatchdogTimer_Tick;
        }

        private bool wasInSchedule = false;

                public Core.StreamConfig GetStreamConfig()
        {
            var config = new Core.StreamConfig();
            config.AppDataPath = AppDataPath;
            config.Port = txtPort.Text;
            config.UseAuth = chkAuth.Checked;
            config.Username = txtUsername.Text;
            config.Password = txtPassword.Text;
            config.StreamName = txtStreamName.Text;
            config.DualStream = chkDualStream.Checked;
            config.QualityIndex = cmbQuality.SelectedIndex;
            config.Fps = cmbFps.Text;
            config.EncoderIndex = cmbEncoder.SelectedIndex;
            config.ShowCursor = chkMouseCursor.Checked;
            config.MonitorIndex = cmbMonitor.SelectedIndex;
            if (config.MonitorIndex > 0 && config.MonitorIndex - 1 < Screen.AllScreens.Length)
                config.MonitorBounds = Screen.AllScreens[config.MonitorIndex - 1].Bounds;
            return config;
        }

        public void LoadSettings()
        {
            isLoading = true;
            try
            {
                var cfg = configManager.Load();
                
                string Get(string key, string def) => cfg.ContainsKey(key) ? cfg[key] : def;
                
                txtStreamName.Text = Get("StreamName", "camera1");
                txtPort.Text = Get("Port", "8554");
                chkAuth.Checked = Convert.ToBoolean(Get("UseAuth", "False"));
                chkDualStream.Checked = Convert.ToBoolean(Get("UseDual", "True"));
                chkMouseCursor.Checked = Convert.ToBoolean(Get("MouseCursor", "True"));
                chkWebServer.Checked = Convert.ToBoolean(Get("WebServer", "False"));
                txtWebPort.Text = Get("WebPort", "8889");
                
                chkSchedule.Checked = Convert.ToBoolean(Get("UseSchedule", "False"));
                chkSchedule.CheckedChanged += (s, e) => {
                    if (chkSchedule.Checked) watchdogTimer.Start();
                    else if (!isTransmitting) watchdogTimer.Stop();
                };
                if (chkSchedule.Checked) watchdogTimer.Start();
                
                txtScheduleStart.Text = Get("ScheduleStart", "05:45");
                txtScheduleEnd.Text = Get("ScheduleEnd", "22:15");

                txtUsername.Text = Get("Username", "admin");
                currentPassword = configManager.DecryptPassword(Get("Password", ""));
                txtPassword.Text = "";
                if (!string.IsNullOrEmpty(currentPassword)) {
                    txtPassword.PlaceholderText = LanguageManager.Get("PassPlaceholder");
                } else {
                    txtPassword.PlaceholderText = "";
                }
                
                int encIdx = Convert.ToInt32(Get("EncoderIdx", "0"));
                if (encIdx >= 0 && encIdx < cmbEncoder.Items.Count) cmbEncoder.SelectedIndex = encIdx;

                int qIdx = Convert.ToInt32(Get("QualityIdx", "1"));
                if (qIdx >= 0 && qIdx < cmbQuality.Items.Count) cmbQuality.SelectedIndex = qIdx;

                int mIdx = Convert.ToInt32(Get("MonitorIdx", "0"));
                if (mIdx >= 0 && mIdx < cmbMonitor.Items.Count) cmbMonitor.SelectedIndex = mIdx;

                string fpsVal = Get("FPS", "12");
                int fpsIdx = cmbFps.Items.IndexOf(fpsVal);
                if (fpsIdx >= 0) cmbFps.SelectedIndex = fpsIdx;
                else cmbFps.SelectedIndex = 1;

                string langVal = Get("Language", "System");
                int langIdx = 0;
                if (langVal == "pt-BR") langIdx = 1;
                else if (langVal == "es-ES") langIdx = 2;
                else if (langVal == "en-US") langIdx = 3;
                if (langIdx >= 0 && langIdx < cmbLanguage.Items.Count) cmbLanguage.SelectedIndex = langIdx;
            }
            catch { }
            txtUsername.Enabled = chkAuth.Checked;
            txtPassword.Enabled = chkAuth.Checked;
            isLoading = false;
        }

        internal void TriggerAutoSave()
        {
            if (isLoading || autoSaveTimer == null) return;
            if (lblSaveStatus != null) {
                lblSaveStatus.Text = ScreenToRTSP.I18n.LanguageManager.Get("MsgSaving");
                lblSaveStatus.Visible = true;
            }
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        public void AttachAutoSave(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TextBox txt) txt.TextChanged += (s, e) => TriggerAutoSave();
                else if (c is MaskedTextBox mtxt) mtxt.TextChanged += (s, e) => TriggerAutoSave();
                else if (c is CheckBox chk) chk.CheckedChanged += (s, e) => TriggerAutoSave();
                else if (c is ComboBox cmb) cmb.SelectedIndexChanged += (s, e) => TriggerAutoSave();
                
                if (c.HasChildren) AttachAutoSave(c);
            }
        }

        public void SaveSettings()
        {
            try
            {
                var lines = new System.Collections.Generic.List<string>();
                lines.Add("StreamName=" + txtStreamName.Text);
                lines.Add("Port=" + txtPort.Text);
                lines.Add("UseAuth=" + chkAuth.Checked.ToString());
                lines.Add("UseDual=" + chkDualStream.Checked.ToString());
                lines.Add("MouseCursor=" + chkMouseCursor.Checked.ToString());
                lines.Add("WebServer=" + chkWebServer.Checked.ToString());
                lines.Add("WebPort=" + txtWebPort.Text);
                lines.Add("UseSchedule=" + chkSchedule.Checked.ToString());
                lines.Add("ScheduleStart=" + txtScheduleStart.Text);
                lines.Add("ScheduleEnd=" + txtScheduleEnd.Text);
                lines.Add("Username=" + txtUsername.Text);
                string passToSave = string.IsNullOrEmpty(txtPassword.Text) ? currentPassword : txtPassword.Text;
                currentPassword = passToSave;
                lines.Add("Password=" + configManager.EncryptPassword(passToSave));
                lines.Add("EncoderIdx=" + cmbEncoder.SelectedIndex);
                lines.Add("QualityIdx=" + cmbQuality.SelectedIndex);
                lines.Add("MonitorIdx=" + cmbMonitor.SelectedIndex);
                lines.Add("FPS=" + cmbFps.SelectedItem.ToString());
                
                string[] langKeys = { "System", "pt-BR", "es-ES", "en-US" };
                string newLang = langKeys[cmbLanguage.SelectedIndex];
                var oldCfg = configManager.Load();
                string oldLang = oldCfg.ContainsKey("Language") ? oldCfg["Language"] : "System";
                if (newLang != oldLang) langChanged = true;
                lines.Add("Language=" + newLang);
                if (oldCfg.ContainsKey("Theme")) lines.Add("Theme=" + oldCfg["Theme"]);
                
                configManager.Save(lines);
            }
            catch { }
        }

        private void CheckStartupRegistry()
        {
            bool isFirstRun = configManager.IsFirstRun();

            RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            
            if (isFirstRun) 
            {
                chkStartWithWindows.Checked = true;
            }
            else
            {
                chkStartWithWindows.Checked = rk.GetValue("ScreenToRTSP") != null;
            }
        }

        internal void ToggleStartWithWindows()
        {
            RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (chkStartWithWindows.Checked) rk.SetValue("ScreenToRTSP", Application.ExecutablePath + " --minimized");
            else rk.DeleteValue("ScreenToRTSP", false);
        }



        // Os métodos MediaMtx e FFmpeg foram movidos para Core.MediaServer

        private void BtnStart_Click(object sender, EventArgs e)
        {
            try
            {
                if (isTransmitting) return;
                
                bool calledBySchedule = (sender == watchdogTimer);
                
                string actualPassForCheck = string.IsNullOrEmpty(txtPassword.Text) ? currentPassword : txtPassword.Text;
                if (chkAuth.Checked && (string.IsNullOrEmpty(txtUsername.Text) || string.IsNullOrEmpty(actualPassForCheck))) {
                    chkAuth.Checked = false;
                }
                
                SaveSettings();
                var config = GetStreamConfig();
                mediaServer.GenerateMediaMtxConfig(config);

                mediaServer.LaunchMediaMtx(config);
                mediaServer.LaunchFFmpeg(config);

                isTransmitting = true;
                watchdogTimer.Start();

                btnStart.Text = "■ " + LanguageManager.Get("BtnStop");
                btnStart.BackColor = Color.FromArgb(226, 232, 240);
                btnStart.ForeColor = Color.FromArgb(51, 65, 85);
                lblStatus.Text = $"● {LanguageManager.Get("StatusPrefix")}{LanguageManager.Get("StatusRunning")}";
                lblStatus.ForeColor = Color.MediumSeaGreen;

                UpdateUrlStatus();
                trayIcon.Visible = true;
                trayIcon.Visible = true;
            }
            catch (Exception ex) { MessageBox.Show("Erro ao iniciar: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error); mediaServer.StopProcesses(); }
        }

        private void WatchdogTimer_Tick(object sender, EventArgs e)
        {
            // Check Schedule
            if (chkSchedule.Checked)
            {
                if (TimeSpan.TryParse(txtScheduleStart.Text, out TimeSpan start) && TimeSpan.TryParse(txtScheduleEnd.Text, out TimeSpan end))
                {
                    TimeSpan now = DateTime.Now.TimeOfDay;
                    bool inSchedule = false;
                    if (start <= end) inSchedule = (now >= start && now <= end);
                    else inSchedule = (now >= start || now <= end);

                    if (inSchedule && !wasInSchedule) 
                    {
                        if (!isTransmitting) BtnStart_Click(watchdogTimer, null);
                    }
                    else if (!inSchedule && wasInSchedule) 
                    {
                        if (isTransmitting) BtnStop_Click(watchdogTimer, null);
                    }
                    
                    wasInSchedule = inSchedule;
                }
            }
            else
            {
                wasInSchedule = false;
            }

            // Watchdog
            if (!isTransmitting) return;
            bool ffmpegDied = !mediaServer.IsFfmpegRunning;
            bool mtxDied = !mediaServer.IsMediaMtxRunning;
            if (ffmpegDied || mtxDied)
            {
                try { var cfg = GetStreamConfig(); if (mtxDied) mediaServer.LaunchMediaMtx(cfg); if (ffmpegDied) mediaServer.LaunchFFmpeg(cfg); } catch { }
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            isTransmitting = false;
            
            bool calledBySchedule = (sender == watchdogTimer);
            if (!calledBySchedule)
            {
                if (!chkSchedule.Checked) watchdogTimer.Stop();
                SaveSettings();
            }
            
            mediaServer.StopProcesses();
            btnStart.Text = "▶ " + LanguageManager.Get("BtnStart");
            btnStart.BackColor = Color.FromArgb(22, 163, 74);
            btnStart.ForeColor = Color.White;
            lblStatus.Text = $"● {LanguageManager.Get("StatusPrefix")}{LanguageManager.Get("StatusStopped")}";
            lblStatus.ForeColor = Color.Crimson;
            UpdateUrlStatus();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e) 
        { 
            if (isTransmitting && !isForceClosing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideForm();
                return;
            }
            SaveSettings();
            mediaServer.StopProcesses(); 
            webPanel.Stop(); 
        }
        private void Form1_Resize(object sender, EventArgs e) { if (isTransmitting && this.WindowState == FormWindowState.Minimized) HideForm(); }
        private void TrayIcon_DoubleClick(object sender, EventArgs e) { ShowFormSafe(); }
        private void HideForm() { this.Hide(); trayIcon.Visible = true; }
        internal void ShowFormSafe() { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); }
                private string pendingUpdateUrl = null;
        private bool hasPendingBinUpdate = false;

        public async System.Threading.Tasks.Task RunBackgroundUpdateCheck()
        {
            var updateInfo = await Core.Updater.CheckForUpdatesAsync();
            
            await Core.DependencyManager.CheckForNewVersionsAsync();
            bool hasBinUpdate = Core.DependencyManager.HasFfmpegUpdate || Core.DependencyManager.HasMtxUpdate;

            if (updateInfo.HasUpdate || hasBinUpdate)
            {
                string msg = "";
                if (updateInfo.HasUpdate) msg += $"Nova versão do app ({updateInfo.Version}) disponível!\n";
                if (hasBinUpdate) msg += "Novas versões do FFmpeg/MediaMTX disponíveis!\n";
                msg += "Clique aqui para atualizar.";

                InvokeSafe(() => {
                    trayIcon.BalloonTipTitle = "Atualização Disponível";
                    trayIcon.BalloonTipText = msg;
                    trayIcon.ShowBalloonTip(10000);
                    this.pendingUpdateUrl = updateInfo.DownloadUrl;
                    this.hasPendingBinUpdate = hasBinUpdate;
                });
            }
        }

        public void StartAuto() { HideForm(); watchdogTimer.Start(); if (!chkSchedule.Checked) BtnStart_Click(null, null); }

        public void InvokeSafe(Action action)
        {
            if (this.InvokeRequired) this.Invoke((System.Windows.Forms.MethodInvoker)delegate { action(); });
            else action();
        }

        internal void StartClick() { BtnStart_Click(null, null); }
        internal void StopClick() { BtnStop_Click(null, null); }
        internal void TriggerSave() { SaveSettings(); }

        internal void UpdateUrlStatus()
        {
            InvokeSafe(() => {
                string labelText = "";
                if (isTransmitting)
                {
                    string actualPass = string.IsNullOrEmpty(txtPassword.Text) ? currentPassword : txtPassword.Text;
                    string auth = (chkAuth.Checked && !string.IsNullOrEmpty(txtUsername.Text) && !string.IsNullOrEmpty(actualPass)) ? $"{txtUsername.Text}:{actualPass}@" : "";
                    string streamName = string.IsNullOrWhiteSpace(txtStreamName.Text) ? "camera1" : txtStreamName.Text.Trim().Trim('/');

                    currentLanUrl = $"rtsp://{auth}{mediaServer.GetLocalIPAddress()}:{txtPort.Text}/{streamName}";
                    currentLanLowUrl = $"rtsp://{auth}{mediaServer.GetLocalIPAddress()}:{txtPort.Text}/{streamName}_low";
                    
                    labelText += $"Principal: {currentLanUrl}\n";
                    if (chkDualStream.Checked) labelText += $"Leve: {currentLanLowUrl}";
                    
                    btnCopyLan.Visible = true;
                    btnCopyLocal.Visible = chkDualStream.Checked;
                }
                else
                {
                    labelText = LanguageManager.Get("StatusWaitingLinks");
                    btnCopyLan.Visible = false;
                    btnCopyLocal.Visible = false;
                }

                if (chkWebServer.Checked)
                {
                    currentWebUrl = $"http://{mediaServer.GetLocalIPAddress()}:{txtWebPort.Text}";
                    if (lblWebUrl != null) lblWebUrl.Text = currentWebUrl;
                    btnCopyWeb.Visible = true;
                }
                else
                {
                    if (lblWebUrl != null) lblWebUrl.Text = "";
                    btnCopyWeb.Visible = false;
                }
                
                lblUrl.Text = labelText.TrimEnd('\n');
            });
        }
    }
}









