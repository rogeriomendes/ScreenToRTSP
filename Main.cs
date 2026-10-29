using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using ScreenToRTSP.Core;
using ScreenToRTSP.I18n;
using ScreenToRTSP.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;

namespace ScreenToRTSP
{
    public sealed partial class Main : Window
    {
        internal TrayIconService trayIcon;
        internal DispatcherQueueTimer watchdogTimer;
        internal DispatcherQueueTimer autoSaveTimer;
        internal DispatcherQueueTimer saveStatusTimer;
        internal DispatcherQueueTimer portTypingTimer;

        internal volatile bool isTransmitting = false;
        internal bool isForceClosing = false;
        internal string currentPassword = "";
        internal bool langChanged = false;
        internal bool isLoading = false;
        private bool wasInSchedule = false;

        internal string currentLanUrl = "";
        internal string currentLocalUrl = "";
        internal string currentLanLowUrl = "";
        internal string currentWebUrl = "";

        internal HttpListener httpListener;
        internal Thread webServerThread;
        internal bool isWebServerRunning = false;

        internal string AppDataPath => MonitorHelper.AppDir;

        internal WebPanel webPanel;
        internal MediaServer mediaServer;
        internal ConfigManager configManager;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private string pendingUpdateUrl = null;
        private bool hasPendingBinUpdate = false;
        private WinUITextBoxWriter _logWriter;
        internal WinUITextBoxWriter LogWriter => _logWriter;
        private SolidColorBrush _brushRunning;
        private SolidColorBrush _brushStopped;
        private Style _defaultBtnStartStyle;

        internal string ScheduleStartText
        {
            get => $"{tpScheduleStart.Time.Hours:D2}:{tpScheduleStart.Time.Minutes:D2}";
            set
            {
                if (TimeSpan.TryParse(value, out var ts))
                    tpScheduleStart.Time = ts;
                else if (value != null && value.Length == 4 &&
                         int.TryParse(value.Substring(0, 2), out int h) &&
                         int.TryParse(value.Substring(2, 2), out int m))
                    tpScheduleStart.Time = new TimeSpan(h, m, 0);
            }
        }

        internal string ScheduleEndText
        {
            get => $"{tpScheduleEnd.Time.Hours:D2}:{tpScheduleEnd.Time.Minutes:D2}";
            set
            {
                if (TimeSpan.TryParse(value, out var ts))
                    tpScheduleEnd.Time = ts;
                else if (value != null && value.Length == 4 &&
                         int.TryParse(value.Substring(0, 2), out int h) &&
                         int.TryParse(value.Substring(2, 2), out int m))
                    tpScheduleEnd.Time = new TimeSpan(h, m, 0);
            }
        }

        public Main()
        {
            this.InitializeComponent();
            _defaultBtnStartStyle = btnStart.Style;
            InitializeAppWindow();

            configManager = new ConfigManager();
            var cfg = configManager.Load();
            string lang = cfg.ContainsKey("Language") ? cfg["Language"] : "System";
            LanguageManager.ApplyLanguage(lang);

            webPanel = new WebPanel(this);
            mediaServer = new MediaServer();

            SetupControls();
            SetupTrayIcon();
            SetupTimers();

            LoadSettings();
            CheckStartupRegistry();
            AttachAutoSaveHandlers();

            _logWriter = new WinUITextBoxWriter(rtbLogs, DispatcherQueue, maxLines: 300, isVisibleProvider: () => PageLogs != null && PageLogs.Visibility == Visibility.Visible);
            Console.SetOut(_logWriter);
            Console.WriteLine("--- " + LanguageManager.Get("AppTitle") + " Logs ---");

            if (chkWebServer.IsOn) webPanel.Start();
            UpdateUrlStatus();
        }

        public async Task InitializeStartupAsync(string[] startupArgs)
        {
            if (!DependencyManager.AreDependenciesInstalled())
            {
                bool ok = await RunDependencyDownloadWithOverlayAsync(force: false);
                if (!ok)
                {
                    isForceClosing = true;
                    trayIcon?.Dispose();
                    Application.Current.Exit();
                    return;
                }
            }

            _ = Task.Run(async () =>
            {
                await DependencyManager.WarmupVersionCacheAsync();
                DispatcherQueue.TryEnqueue(RefreshAboutVersionLabels);
            });

            bool startMinimized = startupArgs != null && Array.IndexOf(startupArgs, "--minimized") >= 0;
            bool startResume = startupArgs != null && Array.IndexOf(startupArgs, "--resume") >= 0;

            if (startMinimized)
            {
                StartAuto();
            }
            else if (startResume)
            {
                StartClick();
            }

            _ = RunBackgroundUpdateCheck();
        }

        public async Task<bool> RunDependencyDownloadWithOverlayAsync(bool force = false)
        {
            DownloadOverlayGrid.Visibility = Visibility.Visible;
            LblDownloadProgress.Text = "Iniciando...";
            PbDownload.IsIndeterminate = true;
            PbDownload.Value = 0;

            var progress = new Progress<DependencyProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LblDownloadProgress.Text = p.StatusText;
                    PbDownload.IsIndeterminate = p.IsIndeterminate;
                    if (!p.IsIndeterminate)
                    {
                        PbDownload.Value = p.Percent;
                    }
                });
            });

            bool result = await DependencyManager.DownloadDependenciesAsync(force, progress);
            DownloadOverlayGrid.Visibility = Visibility.Collapsed;
            RefreshAboutVersionLabels();
            _ = Task.Run(() => MonitorHelper.TrimProcessMemory());
            return result;
        }

        private void InitializeAppWindow()
        {
            try
            {
                _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                Microsoft.UI.WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                _appWindow = AppWindow.GetFromWindowId(windowId);

                const int winWidth = 1060;
                const int winHeight = 800;
                _appWindow.Resize(new SizeInt32(winWidth, winHeight));

                if (_appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsMaximizable = false;
                    presenter.IsResizable = false;
                }

                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico");
                if (File.Exists(icoPath))
                {
                    _appWindow.SetIcon(icoPath);
                }

                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    int x = (displayArea.WorkArea.Width - winWidth) / 2;
                    int y = (displayArea.WorkArea.Height - winHeight) / 2;
                    _appWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));
                }

                this.ExtendsContentIntoTitleBar = true;
                this.SetTitleBar(AppTitleBar);

                if (AppWindowTitleBar.IsCustomizationSupported())
                {
                    var titleBar = _appWindow.TitleBar;
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                }

                _appWindow.Closing += AppWindow_Closing;
                _appWindow.Changed += AppWindow_Changed;
            }
            catch { }
        }

        private void SetupControls()
        {
            cmbFps.Items.Clear();
            foreach (var fps in new[] { "10", "12", "15", "20", "24", "30" })
                cmbFps.Items.Add(fps);
            cmbFps.SelectedIndex = 3;

            UpdateUITexts();

            LanguageManager.OnLanguageChanged += () =>
            {
                InvokeSafe(() =>
                {
                    UpdateUITexts();
                    UpdateStartStopVisuals();
                });
            };

            chkAuth.Toggled += (s, e) =>
            {
                txtUsername.IsEnabled = chkAuth.IsOn;
                txtPassword.IsEnabled = chkAuth.IsOn;
                TriggerAutoSave();
            };

            chkStartWithWindows.Toggled += (s, e) =>
            {
                if (!isLoading) ToggleStartWithWindows();
                TriggerAutoSave();
            };

            chkSchedule.Toggled += (s, e) =>
            {
                if (chkSchedule.IsOn) watchdogTimer.Start();
                else if (!isTransmitting) watchdogTimer.Stop();
                TriggerAutoSave();
            };

            cmbLanguage.SelectionChanged += (s, e) =>
            {
                if (isLoading) return;
                var keys = LanguageManager.GetLanguageKeys();
                if (cmbLanguage.SelectedIndex >= 0 && cmbLanguage.SelectedIndex < keys.Count)
                {
                    var selectedKey = keys[cmbLanguage.SelectedIndex];
                    if (selectedKey != LanguageManager.CurrentLangCode)
                    {
                        LanguageManager.ApplyLanguage(selectedKey);
                    }
                    TriggerAutoSave();
                }
            };

            cmbTheme.SelectionChanged += (s, e) =>
            {
                if (isLoading || cmbTheme.SelectedIndex < 0) return;
                string newTheme = cmbTheme.SelectedIndex == 1 ? "Dark" : (cmbTheme.SelectedIndex == 2 ? "Light" : "System");
                ApplyTheme(newTheme);
                SaveSettings();
            };

            chkWebServer.Toggled += (s, e) =>
            {
                if (isLoading) return;
                if (chkWebServer.IsOn) webPanel.Start();
                else webPanel.Stop();
                UpdateUrlStatus();
                TriggerAutoSave();
            };

            btnCopyWeb.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(currentWebUrl))
                {
                    Process.Start(new ProcessStartInfo(currentWebUrl) { UseShellExecute = true });
                }
            };

            btnCopyLan.Click += (s, e) => CopyToClipboard(currentLanUrl);
            btnCopyLocal.Click += (s, e) => CopyToClipboard(currentLanLowUrl);

            btnStart.Click += (s, e) =>
            {
                if (isTransmitting) StopClick();
                else StartClick();
            };

            btnCheckUpdates.Click += BtnCheckUpdates_Click;
            btnUpdateBinaries.Click += BtnUpdateBinaries_Click;
        }

        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var pkg = new DataPackage();
                pkg.SetText(text);
                Clipboard.SetContent(pkg);
            }
            catch { }
        }

        internal void ApplyTheme(string themeStr)
        {
            ElementTheme targetTheme = ElementTheme.Default;
            if (themeStr == "Dark") targetTheme = ElementTheme.Dark;
            else if (themeStr == "Light") targetTheme = ElementTheme.Light;

            RootGrid.RequestedTheme = targetTheme;

            try
            {
                if (_appWindow != null && AppWindowTitleBar.IsCustomizationSupported())
                {
                    bool isDark = targetTheme == ElementTheme.Dark ||
                                  (targetTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
                    _appWindow.TitleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
                }
            }
            catch { }
        }

        internal void UpdateUITexts()
        {
            bool prevLoading = isLoading;
            isLoading = true;
            try
            {
                LblAppTitle.Text = LanguageManager.Get("AppTitle");
                LblAppSubtitle.Text = LanguageManager.Get("AppSubtitle");

                NavVideo.Content = LanguageManager.Get("TabVideo");
                NavNetwork.Content = LanguageManager.Get("TabNetwork");
                NavAutomation.Content = LanguageManager.Get("TabAutomation");
                NavConfig.Content = LanguageManager.Get("TabConfig");
                NavLogs.Content = LanguageManager.Get("TabLogs");
                NavAbout.Content = LanguageManager.Get("TabAbout");

                ToolTipService.SetToolTip(NavVideo, LanguageManager.Get("TipTabVideo"));
                ToolTipService.SetToolTip(NavNetwork, LanguageManager.Get("TipTabNetwork"));
                ToolTipService.SetToolTip(NavAutomation, LanguageManager.Get("TipTabAutomation"));
                ToolTipService.SetToolTip(NavConfig, LanguageManager.Get("TipTabConfig"));
                ToolTipService.SetToolTip(NavLogs, LanguageManager.Get("TipTabLogs"));
                ToolTipService.SetToolTip(NavAbout, LanguageManager.Get("TipTabAbout"));

                HeaderVideo.Text = LanguageManager.Get("TabVideo");
                HeaderNetwork.Text = LanguageManager.Get("TabNetwork");
                HeaderAutomation.Text = LanguageManager.Get("TabAutomation");
                HeaderConfig.Text = LanguageManager.Get("TabConfig");
                HeaderLogs.Text = LanguageManager.Get("TabLogs");
                HeaderAbout.Text = LanguageManager.Get("TabAbout");

                LblClearLogs.Text = LanguageManager.Get("BtnClearLogs");
                ToolTipService.SetToolTip(BtnClearLogs, LanguageManager.Get("TipBtnClearLogs"));

                // Video Cards
                CardMonitor.Header = LanguageManager.Get("MonitorLabel").TrimEnd(':');
                CardMonitor.Description = LanguageManager.Get("TipMonitor");
                ToolTipService.SetToolTip(cmbMonitor, LanguageManager.Get("TipMonitor"));

                int monSel = cmbMonitor.SelectedIndex;
                cmbMonitor.Items.Clear();
                cmbMonitor.Items.Add(LanguageManager.Get("AllMonitors"));
                var monitors = MonitorHelper.GetMonitors();
                for (int i = 0; i < monitors.Count; i++)
                {
                    cmbMonitor.Items.Add($"{LanguageManager.Get("MonitorPrefix")} {i + 1} ({monitors[i].Width}x{monitors[i].Height})");
                }
                cmbMonitor.SelectedIndex = (monSel >= 0 && monSel < cmbMonitor.Items.Count) ? monSel : 0;

                CardEncoder.Header = LanguageManager.Get("EncoderLabel").TrimEnd(':');
                CardEncoder.Description = LanguageManager.Get("TipEncoder");
                ToolTipService.SetToolTip(cmbEncoder, LanguageManager.Get("TipEncoder"));
                int encSel = cmbEncoder.SelectedIndex;
                cmbEncoder.Items.Clear();
                cmbEncoder.Items.Add(LanguageManager.Get("EncoderCPU"));
                cmbEncoder.Items.Add(LanguageManager.Get("EncoderNVENC"));
                cmbEncoder.Items.Add(LanguageManager.Get("EncoderQSV"));
                cmbEncoder.SelectedIndex = (encSel >= 0 && encSel < cmbEncoder.Items.Count) ? encSel : 0;

                CardQuality.Header = LanguageManager.Get("QualityLabel").TrimEnd(':');
                CardQuality.Description = LanguageManager.Get("TipQuality");
                ToolTipService.SetToolTip(cmbQuality, LanguageManager.Get("TipQuality"));
                int qSel = cmbQuality.SelectedIndex;
                cmbQuality.Items.Clear();
                cmbQuality.Items.Add(LanguageManager.Get("QualityLow"));
                cmbQuality.Items.Add(LanguageManager.Get("QualityMedium"));
                cmbQuality.Items.Add(LanguageManager.Get("QualityHigh"));
                cmbQuality.SelectedIndex = (qSel >= 0 && qSel < cmbQuality.Items.Count) ? qSel : 1;

                CardFps.Header = LanguageManager.Get("FpsLabel").TrimEnd(':');
                CardFps.Description = LanguageManager.Get("TipFps");
                ToolTipService.SetToolTip(cmbFps, LanguageManager.Get("TipFps"));

                CardDualStream.Header = LanguageManager.Get("DualStreamLabel");
                CardDualStream.Description = LanguageManager.Get("TipDualStream");
                ToolTipService.SetToolTip(chkDualStream, LanguageManager.Get("TipDualStream"));

                CardMouseCursor.Header = LanguageManager.Get("MouseCursorLabel");
                CardMouseCursor.Description = LanguageManager.Get("TipMouseCursor");
                ToolTipService.SetToolTip(chkMouseCursor, LanguageManager.Get("TipMouseCursor"));

                // Network Cards
                CardStreamName.Header = LanguageManager.Get("StreamNameLabel").TrimEnd(':');
                CardStreamName.Description = LanguageManager.Get("TipStreamName");
                ToolTipService.SetToolTip(txtStreamName, LanguageManager.Get("TipStreamName"));

                CardPort.Header = LanguageManager.Get("PortLabel").TrimEnd(':');
                CardPort.Description = LanguageManager.Get("TipPort");
                ToolTipService.SetToolTip(txtPort, LanguageManager.Get("TipPort"));

                ExpanderAuth.Header = LanguageManager.Get("AuthLabel");
                ExpanderAuth.Description = LanguageManager.Get("TipAuth");
                ToolTipService.SetToolTip(chkAuth, LanguageManager.Get("TipAuth"));

                CardUsername.Header = LanguageManager.Get("UserLabel").TrimEnd(':');
                ToolTipService.SetToolTip(txtUsername, LanguageManager.Get("TipUsername"));

                CardPassword.Header = LanguageManager.Get("PasswordLabel").TrimEnd(':');
                ToolTipService.SetToolTip(txtPassword, LanguageManager.Get("TipPassword"));
                txtPassword.PlaceholderText = !string.IsNullOrEmpty(currentPassword) ? LanguageManager.Get("PassPlaceholder") : "";

                // Automation Cards
                CardStartWindows.Header = LanguageManager.Get("StartWindowsLabel");
                CardStartWindows.Description = LanguageManager.Get("TipStartWindows");
                ToolTipService.SetToolTip(chkStartWithWindows, LanguageManager.Get("TipStartWindows"));

                ExpanderSchedule.Header = LanguageManager.Get("ScheduleLabel").TrimEnd(':');
                ExpanderSchedule.Description = LanguageManager.Get("TipSchedule");
                ToolTipService.SetToolTip(chkSchedule, LanguageManager.Get("TipSchedule"));

                CardScheduleStart.Header = LanguageManager.Get("ScheduleStartLabel").TrimEnd(':');
                ToolTipService.SetToolTip(tpScheduleStart, LanguageManager.Get("TipScheduleStart"));

                CardScheduleEnd.Header = LanguageManager.Get("ScheduleEndLabel").TrimEnd(':');
                ToolTipService.SetToolTip(tpScheduleEnd, LanguageManager.Get("TipScheduleEnd"));

                // Config Cards
                CardLanguage.Header = LanguageManager.Get("LanguageLabel").TrimEnd(':');
                CardLanguage.Description = LanguageManager.Get("TipLanguage");
                ToolTipService.SetToolTip(cmbLanguage, LanguageManager.Get("TipLanguage"));
                var langItems = LanguageManager.GetLanguageKeys();
                int langSel = cmbLanguage.SelectedIndex;
                cmbLanguage.Items.Clear();
                foreach (var k in langItems)
                {
                    if (k == "System") cmbLanguage.Items.Add(LanguageManager.Get("LanguageSystem"));
                    else if (k == "pt-BR") cmbLanguage.Items.Add("Português (Brasil)");
                    else if (k == "en-US") cmbLanguage.Items.Add("English (US)");
                    else if (k == "es-ES") cmbLanguage.Items.Add("Español (España)");
                    else cmbLanguage.Items.Add(k);
                }
                if (langSel >= 0 && langSel < cmbLanguage.Items.Count)
                    cmbLanguage.SelectedIndex = langSel;
                else
                {
                    int idx = langItems.IndexOf(LanguageManager.CurrentLangCode);
                    cmbLanguage.SelectedIndex = idx != -1 ? idx : 0;
                }

                CardTheme.Header = LanguageManager.Get("ThemeLabel").TrimEnd(':');
                CardTheme.Description = LanguageManager.Get("TipTheme");
                ToolTipService.SetToolTip(cmbTheme, LanguageManager.Get("TipTheme"));
                int themeSel = cmbTheme.SelectedIndex;
                cmbTheme.Items.Clear();
                cmbTheme.Items.Add(LanguageManager.Get("ThemeSystem"));
                cmbTheme.Items.Add(LanguageManager.Get("ThemeDark"));
                cmbTheme.Items.Add(LanguageManager.Get("ThemeLight"));
                if (themeSel >= 0 && themeSel < cmbTheme.Items.Count)
                    cmbTheme.SelectedIndex = themeSel;
                else
                    cmbTheme.SelectedIndex = 0;

                ExpanderWebServer.Header = LanguageManager.Get("WebServerLabel");
                ExpanderWebServer.Description = LanguageManager.Get("TipWebServer");
                ToolTipService.SetToolTip(chkWebServer, LanguageManager.Get("TipWebServer"));

                CardWebPort.Header = LanguageManager.Get("WebPortLabel").TrimEnd(':');
                CardWebPort.Description = LanguageManager.Get("TipWebPort");
                ToolTipService.SetToolTip(txtWebPort, LanguageManager.Get("TipWebPort"));

                LblBtnOpenWeb.Text = LanguageManager.Get("BtnOpenWeb");
                ToolTipService.SetToolTip(btnCopyWeb, LanguageManager.Get("TipBtnOpenWeb"));

                // Logs Button
                ToolTipService.SetToolTip(BtnClearLogs, LanguageManager.Get("TipBtnClearLogs"));

                // About Cards
                lblAboutTitle.Text = LanguageManager.Get("AppTitle");
                lblAboutDesc.Text = LanguageManager.Get("AboutDesc");
                lblAuthor.Text = LanguageManager.Get("DevelopedBy");
                CardAppVersion.Header = LanguageManager.Get("AppTitle");
                CardRepository.Header = LanguageManager.Get("ProjectRepository");
                ToolTipService.SetToolTip(CardRepository, LanguageManager.Get("TipRepository"));
                ToolTipService.SetToolTip(lnkAuthor, LanguageManager.Get("TipRepository"));
                RefreshAboutVersionLabels();

                LblBtnCheckUpdates.Text = LanguageManager.Get("BtnCheckUpdates");
                ToolTipService.SetToolTip(btnCheckUpdates, LanguageManager.Get("TipBtnCheckUpdates"));

                LblBtnUpdateBinaries.Text = LanguageManager.Get("BtnUpdateBinaries");
                ToolTipService.SetToolTip(btnUpdateBinaries, LanguageManager.Get("TipBtnUpdateBinaries"));
                InfoBarUpdate.Title = LanguageManager.Get("UpdateAvailable");

                // Bottom Bar
                LblBtnCopyLan.Text = LanguageManager.Get("BtnCopyLan");
                ToolTipService.SetToolTip(btnCopyLan, LanguageManager.Get("TipBtnCopyLan"));

                LblBtnCopyLocal.Text = LanguageManager.Get("BtnCopyLocal");
                ToolTipService.SetToolTip(btnCopyLocal, LanguageManager.Get("TipBtnCopyLocal"));

                ToolTipService.SetToolTip(btnStart, isTransmitting ? LanguageManager.Get("TipBtnStop") : LanguageManager.Get("TipBtnStart"));
                UpdateUrlStatus();
                UpdateStartStopVisuals();
            }
            finally
            {
                isLoading = prevLoading;
            }
        }

        private void RefreshAboutVersionLabels()
        {
            lblAboutVersion.Text = LanguageManager.Get("AboutVersion") + Updater.GetAppVersion();
            lblAboutUpdate.Text = Updater.HasAppUpdate ? $"({LanguageManager.Get("NewVersion")}{Updater.LatestVersionStr})" : "";

            lblFFmpegVersion.Text = LanguageManager.Get("AboutVersion") + DependencyManager.GetFFmpegVersion();
            lblFFmpegUpdate.Text = DependencyManager.HasFfmpegUpdate ? $"({LanguageManager.Get("NewVersion")}{DependencyManager.FfmpegLatestVersionStr})" : "";

            lblMediaMTXVersion.Text = LanguageManager.Get("AboutVersion") + DependencyManager.GetMediaMTXVersion();
            lblMediaMTXUpdate.Text = DependencyManager.HasMtxUpdate ? $"({LanguageManager.Get("NewVersion")}{DependencyManager.MtxLatestVersionStr})" : "";
        }

        private void SetupTrayIcon()
        {
            trayIcon = new TrayIconService();
            trayIcon.SetTooltip($"{LanguageManager.Get("AppTitle")} - {(isTransmitting ? LanguageManager.Get("StatusRunning") : LanguageManager.Get("StatusStopped"))}");
            trayIcon.IsTransmittingProvider = () => isTransmitting;
            trayIcon.IsWebServerEnabledProvider = () => isWebServerRunning;
            trayIcon.MenuLabelsProvider = () => (
                "▶ " + LanguageManager.Get("BtnStart"),
                "■ " + LanguageManager.Get("BtnStop"),
                LanguageManager.Get("BtnOpenWeb"),
                LanguageManager.Get("TrayConfig"),
                LanguageManager.Get("TrayExit")
            );

            trayIcon.DoubleClicked += () => DispatcherQueue.TryEnqueue(ShowFormSafe);
            trayIcon.OpenConfigRequested += () => DispatcherQueue.TryEnqueue(ShowFormSafe);
            trayIcon.StartRequested += () => DispatcherQueue.TryEnqueue(StartClick);
            trayIcon.StopRequested += () => DispatcherQueue.TryEnqueue(StopClick);
            trayIcon.OpenWebRequested += () => DispatcherQueue.TryEnqueue(() =>
            {
                if (chkWebServer.IsOn)
                {
                    string port = string.IsNullOrEmpty(txtWebPort.Text) ? "8889" : txtWebPort.Text;
                    Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true });
                }
            });
            trayIcon.ExitRequested += () => DispatcherQueue.TryEnqueue(() =>
            {
                isForceClosing = true;
                trayIcon.Dispose();
                mediaServer?.StopProcesses();
                webPanel?.Stop();
                Application.Current.Exit();
            });
            trayIcon.BalloonTipClicked += () => DispatcherQueue.TryEnqueue(async () =>
            {
                if (!string.IsNullOrEmpty(pendingUpdateUrl))
                {
                    await Updater.PerformUpdate(pendingUpdateUrl, isTransmitting);
                }
                else if (hasPendingBinUpdate)
                {
                    bool wasTransmitting = isTransmitting;
                    if (wasTransmitting) StopClick();
                    await RunDependencyDownloadWithOverlayAsync(true);
                    if (wasTransmitting) StartClick();
                    hasPendingBinUpdate = false;
                }
            });
        }

        private void SetupTimers()
        {
            watchdogTimer = DispatcherQueue.CreateTimer();
            watchdogTimer.Interval = TimeSpan.FromSeconds(5);
            watchdogTimer.Tick += WatchdogTimer_Tick;

            var updateTimer = DispatcherQueue.CreateTimer();
            updateTimer.Interval = TimeSpan.FromHours(24);
            updateTimer.Tick += async (s, e) => await RunBackgroundUpdateCheck();
            updateTimer.Start();

            autoSaveTimer = DispatcherQueue.CreateTimer();
            autoSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
            autoSaveTimer.Tick += async (s, e) =>
            {
                autoSaveTimer.Stop();
                if (!isLoading)
                {
                    SaveSettings();
                    lblSaveStatus.Text = "✔ " + LanguageManager.Get("MsgSaved");
                    SaveStatusBadge.Visibility = Visibility.Visible;
                    saveStatusTimer.Stop();
                    saveStatusTimer.Start();

                    if (isTransmitting)
                    {
                        StopClick();
                        await Task.Delay(500);
                        StartClick();
                    }
                }
            };

            saveStatusTimer = DispatcherQueue.CreateTimer();
            saveStatusTimer.Interval = TimeSpan.FromSeconds(2);
            saveStatusTimer.Tick += (s, e) =>
            {
                saveStatusTimer.Stop();
                SaveStatusBadge.Visibility = Visibility.Collapsed;
            };

            portTypingTimer = DispatcherQueue.CreateTimer();
            portTypingTimer.Interval = TimeSpan.FromSeconds(1);
            portTypingTimer.Tick += (s, e) =>
            {
                portTypingTimer.Stop();
                if (chkWebServer.IsOn)
                {
                    webPanel.Stop();
                    webPanel.Start();
                    UpdateUrlStatus();
                }
            };
        }

        private void AttachAutoSaveHandlers()
        {
            cmbMonitor.SelectionChanged += (s, e) => TriggerAutoSave();
            cmbEncoder.SelectionChanged += (s, e) => TriggerAutoSave();
            cmbQuality.SelectionChanged += (s, e) => TriggerAutoSave();
            cmbFps.SelectionChanged += (s, e) => TriggerAutoSave();
            chkDualStream.Toggled += (s, e) => { TriggerAutoSave(); UpdateUrlStatus(); };
            chkMouseCursor.Toggled += (s, e) => TriggerAutoSave();

            txtStreamName.TextChanged += (s, e) => { TriggerAutoSave(); UpdateUrlStatus(); };
            txtPort.TextChanged += (s, e) => { TriggerAutoSave(); UpdateUrlStatus(); };
            txtUsername.TextChanged += (s, e) => TriggerAutoSave();
            txtPassword.PasswordChanged += (s, e) => TriggerAutoSave();

            tpScheduleStart.TimeChanged += (s, e) => TriggerAutoSave();
            tpScheduleEnd.TimeChanged += (s, e) => TriggerAutoSave();

            txtWebPort.TextChanged += (s, e) =>
            {
                if (isLoading) return;
                TriggerAutoSave();
                portTypingTimer.Stop();
                portTypingTimer.Start();
            };
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (PageVideo == null || PageNetwork == null || PageAutomation == null ||
                PageConfig == null || PageLogs == null || PageAbout == null)
            {
                return;
            }

            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                PageVideo.Visibility = tag == "video" ? Visibility.Visible : Visibility.Collapsed;
                PageNetwork.Visibility = tag == "network" ? Visibility.Visible : Visibility.Collapsed;
                PageAutomation.Visibility = tag == "automation" ? Visibility.Visible : Visibility.Collapsed;
                PageConfig.Visibility = tag == "config" ? Visibility.Visible : Visibility.Collapsed;
                PageLogs.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
                PageAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;

                if (tag == "logs")
                {
                    _logWriter?.FlushToUI();
                }
            }
        }

        private void LnkRepo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/rogeriomendes/ScreenToRTSP") { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            _logWriter?.Clear();
            rtbLogs.Text = "";
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            btnCheckUpdates.IsEnabled = false;
            PrgCheckUpdates.Visibility = Visibility.Visible;
            PrgCheckUpdates.IsActive = true;

            try
            {
                var appUpdateTask = Updater.CheckForUpdatesAsync();
                var depUpdateTask = DependencyManager.CheckForNewVersionsAsync(true);
                await Task.WhenAll(appUpdateTask, depUpdateTask);
                bool hasUpdate = await depUpdateTask;
                RefreshAboutVersionLabels();

                if (hasUpdate || Updater.HasAppUpdate)
                {
                    string msg = "";
                    if (Updater.HasAppUpdate) msg += $"App ({Updater.LatestVersionStr}) ";
                    if (DependencyManager.HasFfmpegUpdate) msg += $"FFmpeg ({DependencyManager.FfmpegLatestVersionStr}) ";
                    if (DependencyManager.HasMtxUpdate) msg += $"MediaMTX ({DependencyManager.MtxLatestVersionStr})";

                    InfoBarUpdate.Title = LanguageManager.Get("UpdateAvailable");
                    InfoBarUpdate.Message = msg.Trim();
                    InfoBarUpdate.Severity = InfoBarSeverity.Warning;
                    InfoBarUpdate.IsOpen = true;
                    btnUpdateBinaries.Visibility = Visibility.Visible;
                }
                else
                {
                    InfoBarUpdate.Title = LanguageManager.Get("MsgNoUpdate");
                    InfoBarUpdate.Message = "";
                    InfoBarUpdate.Severity = InfoBarSeverity.Success;
                    InfoBarUpdate.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                InfoBarUpdate.Title = LanguageManager.Get("MsgCheckUpdateError");
                InfoBarUpdate.Message = ex.Message;
                InfoBarUpdate.Severity = InfoBarSeverity.Error;
                InfoBarUpdate.IsOpen = true;
            }
            finally
            {
                PrgCheckUpdates.IsActive = false;
                PrgCheckUpdates.Visibility = Visibility.Collapsed;
                btnCheckUpdates.IsEnabled = true;
            }
        }

        private async void BtnUpdateBinaries_Click(object sender, RoutedEventArgs e)
        {
            bool wasTransmitting = isTransmitting;
            if (wasTransmitting)
            {
                if (!MonitorHelper.ShowConfirm(LanguageManager.Get("MsgStopBeforeUpdate"), "Atenção", _hwnd))
                    return;
                StopClick();
            }

            bool updatedDeps = false;
            if (DependencyManager.HasFfmpegUpdate || DependencyManager.HasMtxUpdate)
            {
                if (await RunDependencyDownloadWithOverlayAsync(true))
                {
                    await DependencyManager.CheckForNewVersionsAsync();
                    updatedDeps = true;
                }
            }

            if (Updater.HasAppUpdate)
            {
                await Updater.PerformUpdate(Updater.LatestDownloadUrl, wasTransmitting);
                return;
            }

            RefreshAboutVersionLabels();
            InfoBarUpdate.IsOpen = false;
            btnUpdateBinaries.Visibility = Visibility.Collapsed;

            if (updatedDeps)
            {
                InfoBarUpdate.Title = LanguageManager.Get("MsgUpdateSuccess");
                InfoBarUpdate.Message = "";
                InfoBarUpdate.Severity = InfoBarSeverity.Success;
                InfoBarUpdate.IsOpen = true;
            }

            if (wasTransmitting)
            {
                StartClick();
            }
        }

        public StreamConfig GetStreamConfig()
        {
            var config = new StreamConfig
            {
                AppDataPath = AppDataPath,
                Port = txtPort.Text,
                UseAuth = chkAuth.IsOn,
                Username = txtUsername.Text,
                Password = string.IsNullOrEmpty(txtPassword.Password) ? currentPassword : txtPassword.Password,
                StreamName = txtStreamName.Text,
                DualStream = chkDualStream.IsOn,
                QualityIndex = cmbQuality.SelectedIndex,
                Fps = cmbFps.SelectedItem?.ToString() ?? "20",
                EncoderIndex = cmbEncoder.SelectedIndex,
                ShowCursor = chkMouseCursor.IsOn,
                MonitorIndex = cmbMonitor.SelectedIndex
            };

            var monitors = MonitorHelper.GetMonitors();
            if (config.MonitorIndex > 0 && config.MonitorIndex - 1 < monitors.Count)
            {
                config.MonitorBounds = monitors[config.MonitorIndex - 1];
            }
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
                chkAuth.IsOn = Convert.ToBoolean(Get("UseAuth", "False"));
                chkDualStream.IsOn = Convert.ToBoolean(Get("UseDual", "True"));
                chkMouseCursor.IsOn = Convert.ToBoolean(Get("MouseCursor", "True"));
                chkWebServer.IsOn = Convert.ToBoolean(Get("WebServer", "False"));
                txtWebPort.Text = Get("WebPort", "8889");

                chkSchedule.IsOn = Convert.ToBoolean(Get("UseSchedule", "False"));
                if (chkSchedule.IsOn) watchdogTimer.Start();

                ScheduleStartText = Get("ScheduleStart", "05:45");
                ScheduleEndText = Get("ScheduleEnd", "22:15");

                txtUsername.Text = Get("Username", "admin");
                currentPassword = configManager.DecryptPassword(Get("Password", ""));
                txtPassword.Password = "";
                txtPassword.PlaceholderText = !string.IsNullOrEmpty(currentPassword) ? LanguageManager.Get("PassPlaceholder") : "";

                int encIdx = Convert.ToInt32(Get("EncoderIdx", "0"));
                if (encIdx >= 0 && encIdx < cmbEncoder.Items.Count) cmbEncoder.SelectedIndex = encIdx;

                int qIdx = Convert.ToInt32(Get("QualityIdx", "1"));
                if (qIdx >= 0 && qIdx < cmbQuality.Items.Count) cmbQuality.SelectedIndex = qIdx;

                int mIdx = Convert.ToInt32(Get("MonitorIdx", "0"));
                if (mIdx >= 0 && mIdx < cmbMonitor.Items.Count) cmbMonitor.SelectedIndex = mIdx;

                string fpsVal = Get("FPS", "12");
                int fpsIdx = cmbFps.Items.IndexOf(fpsVal);
                cmbFps.SelectedIndex = fpsIdx >= 0 ? fpsIdx : 1;

                string langVal = Get("Language", "System");
                int langIdx = 0;
                if (langVal == "pt-BR") langIdx = 1;
                else if (langVal == "es-ES") langIdx = 2;
                else if (langVal == "en-US") langIdx = 3;
                if (langIdx >= 0 && langIdx < cmbLanguage.Items.Count) cmbLanguage.SelectedIndex = langIdx;

                string themeVal = Get("Theme", "System");
                int themeIdx = themeVal == "Dark" ? 1 : (themeVal == "Light" ? 2 : 0);
                if (themeIdx >= 0 && themeIdx < cmbTheme.Items.Count) cmbTheme.SelectedIndex = themeIdx;
                ApplyTheme(themeVal);
            }
            catch { }

            txtUsername.IsEnabled = chkAuth.IsOn;
            txtPassword.IsEnabled = chkAuth.IsOn;
            isLoading = false;
        }

        internal void TriggerAutoSave()
        {
            if (isLoading || autoSaveTimer == null) return;
            lblSaveStatus.Text = LanguageManager.Get("MsgSaving");
            SaveStatusBadge.Visibility = Visibility.Visible;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        public void SaveSettings()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("StreamName=" + txtStreamName.Text);
                lines.Add("Port=" + txtPort.Text);
                lines.Add("UseAuth=" + chkAuth.IsOn.ToString());
                lines.Add("UseDual=" + chkDualStream.IsOn.ToString());
                lines.Add("MouseCursor=" + chkMouseCursor.IsOn.ToString());
                lines.Add("WebServer=" + chkWebServer.IsOn.ToString());
                lines.Add("WebPort=" + txtWebPort.Text);
                lines.Add("UseSchedule=" + chkSchedule.IsOn.ToString());
                lines.Add("ScheduleStart=" + ScheduleStartText);
                lines.Add("ScheduleEnd=" + ScheduleEndText);
                lines.Add("Username=" + txtUsername.Text);

                string passToSave = string.IsNullOrEmpty(txtPassword.Password) ? currentPassword : txtPassword.Password;
                currentPassword = passToSave;
                lines.Add("Password=" + configManager.EncryptPassword(passToSave));
                lines.Add("EncoderIdx=" + cmbEncoder.SelectedIndex);
                lines.Add("QualityIdx=" + cmbQuality.SelectedIndex);
                lines.Add("MonitorIdx=" + cmbMonitor.SelectedIndex);
                lines.Add("FPS=" + (cmbFps.SelectedItem?.ToString() ?? "20"));

                string[] langKeys = { "System", "pt-BR", "es-ES", "en-US" };
                int lIdx = Math.Clamp(cmbLanguage.SelectedIndex, 0, langKeys.Length - 1);
                string newLang = langKeys[lIdx];
                if (newLang != LanguageManager.CurrentLangCode) langChanged = true;
                lines.Add("Language=" + newLang);

                string themeStr = cmbTheme.SelectedIndex == 1 ? "Dark" : (cmbTheme.SelectedIndex == 2 ? "Light" : "System");
                lines.Add("Theme=" + themeStr);

                configManager.Save(lines);
            }
            catch { }
        }

        private void CheckStartupRegistry()
        {
            isLoading = true;
            try
            {
                bool isFirstRun = configManager.IsFirstRun();
                using RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (isFirstRun)
                {
                    chkStartWithWindows.IsOn = true;
                    ToggleStartWithWindows();
                }
                else if (rk != null)
                {
                    chkStartWithWindows.IsOn = rk.GetValue("ScreenToRTSP") != null;
                }
            }
            catch { }
            isLoading = false;
        }

        internal void ToggleStartWithWindows()
        {
            try
            {
                using RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (rk == null) return;
                string exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenToRTSP.exe");
                if (chkStartWithWindows.IsOn)
                    rk.SetValue("ScreenToRTSP", $"\"{exePath}\" --minimized");
                else
                    rk.DeleteValue("ScreenToRTSP", false);
            }
            catch { }
        }

        private void UpdateStartStopVisuals()
        {
            string baseTitle = LanguageManager.Get("AppTitle");
            string runningStatus = LanguageManager.Get("StatusRunning");
            string stoppedStatus = LanguageManager.Get("StatusStopped");

            if (isTransmitting)
            {
                if (Application.Current.Resources.TryGetValue("DangerButtonStyle", out var dangerStyle) && dangerStyle is Style ds)
                {
                    btnStart.Style = ds;
                }
                IconBtnStart.Glyph = "\uE71A"; // Stop symbol
                LblBtnStart.Text = LanguageManager.Get("BtnStop");
                ToolTipService.SetToolTip(btnStart, LanguageManager.Get("TipBtnStop"));
                lblStatus.Text = $"{LanguageManager.Get("StatusPrefix")}{runningStatus}";
                _brushRunning ??= new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                StatusDot.Fill = _brushRunning;

                // Título da Janela e Barra de Tarefas (Taskbar tooltip)
                string windowTitle = $"● [{runningStatus}] {baseTitle}";
                this.Title = windowTitle;
                if (_appWindow != null) _appWindow.Title = windowTitle;

                // Badge no cabeçalho personalizado da janela (TitleBar)
                if (BadgeTitleStreaming != null)
                {
                    BadgeTitleStreaming.Visibility = Visibility.Visible;
                    if (lblTitleStreaming != null) lblTitleStreaming.Text = runningStatus.ToUpperInvariant();
                    ToolTipService.SetToolTip(BadgeTitleStreaming, $"{baseTitle} - {runningStatus}");
                }

                // Tooltip do ícone na bandeja do sistema
                trayIcon?.SetTooltip($"{baseTitle} - {runningStatus}");
            }
            else
            {
                if (_defaultBtnStartStyle != null)
                {
                    btnStart.Style = _defaultBtnStartStyle;
                }
                else if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accentStyle) && accentStyle is Style asStyle)
                {
                    btnStart.Style = asStyle;
                }
                IconBtnStart.Glyph = "\uE768"; // Play symbol
                LblBtnStart.Text = LanguageManager.Get("BtnStart");
                ToolTipService.SetToolTip(btnStart, LanguageManager.Get("TipBtnStart"));
                lblStatus.Text = $"{LanguageManager.Get("StatusPrefix")}{stoppedStatus}";
                _brushStopped ??= new SolidColorBrush(Color.FromArgb(255, 225, 29, 72));
                StatusDot.Fill = _brushStopped;

                // Título da Janela e Barra de Tarefas (Taskbar tooltip)
                this.Title = baseTitle;
                if (_appWindow != null) _appWindow.Title = baseTitle;

                // Badge no cabeçalho personalizado da janela (TitleBar)
                if (BadgeTitleStreaming != null)
                {
                    BadgeTitleStreaming.Visibility = Visibility.Collapsed;
                }

                // Tooltip do ícone na bandeja do sistema
                trayIcon?.SetTooltip($"{baseTitle} - {stoppedStatus}");
            }
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            try
            {
                if (isTransmitting) return;

                string actualPassForCheck = string.IsNullOrEmpty(txtPassword.Password) ? currentPassword : txtPassword.Password;
                if (chkAuth.IsOn && (string.IsNullOrEmpty(txtUsername.Text) || string.IsNullOrEmpty(actualPassForCheck)))
                {
                    isLoading = true;
                    chkAuth.IsOn = false;
                    isLoading = false;
                }

                SaveSettings();
                var config = GetStreamConfig();
                mediaServer.GenerateMediaMtxConfig(config);
                mediaServer.LaunchMediaMtx(config);
                mediaServer.LaunchFFmpeg(config);

                isTransmitting = true;
                watchdogTimer.Start();

                UpdateStartStopVisuals();
                UpdateUrlStatus();
                if (trayIcon != null) trayIcon.Visible = true;
            }
            catch (Exception ex)
            {
                MonitorHelper.ShowMessage("Erro ao iniciar: " + ex.Message, "Erro", MonitorHelper.MB_ICONERROR, _hwnd);
                mediaServer.StopProcesses();
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            isTransmitting = false;

            bool calledBySchedule = ReferenceEquals(sender, watchdogTimer);
            if (!calledBySchedule)
            {
                if (!chkSchedule.IsOn) watchdogTimer.Stop();
                SaveSettings();
            }

            mediaServer.StopProcesses();
            UpdateStartStopVisuals();
            UpdateUrlStatus();
        }

        private void WatchdogTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (chkSchedule.IsOn)
            {
                TimeSpan start = tpScheduleStart.Time;
                TimeSpan end = tpScheduleEnd.Time;
                TimeSpan now = DateTime.Now.TimeOfDay;
                bool inSchedule = (start <= end) ? (now >= start && now <= end) : (now >= start || now <= end);

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
            else
            {
                wasInSchedule = false;
            }

            if (!isTransmitting) return;
            bool ffmpegDied = !mediaServer.IsFfmpegRunning;
            bool mtxDied = !mediaServer.IsMediaMtxRunning;
            if (ffmpegDied || mtxDied)
            {
                try
                {
                    var cfg = GetStreamConfig();
                    if (mtxDied) mediaServer.LaunchMediaMtx(cfg);
                    if (ffmpegDied) mediaServer.LaunchFFmpeg(cfg);
                }
                catch { }
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (isTransmitting && !isForceClosing)
            {
                args.Cancel = true;
                HideForm();
                return;
            }
            SaveSettings();
            mediaServer?.StopProcesses();
            webPanel?.Stop();
            trayIcon?.Dispose();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPresenterChange && _appWindow.Presenter is OverlappedPresenter presenter)
            {
                if (isTransmitting && presenter.State == OverlappedPresenterState.Minimized)
                {
                    HideForm();
                }
            }
        }

        private void HideForm()
        {
            try
            {
                if (trayIcon != null) trayIcon.Visible = true;
                _appWindow?.Hide();
                _ = Task.Run(() => MonitorHelper.TrimProcessMemory());
            }
            catch { }
        }

        internal void ShowFormSafe()
        {
            try
            {
                _appWindow?.Show();
                if (_appWindow?.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
                {
                    presenter.Restore();
                }
                this.Activate();
            }
            catch { }
        }

        public async Task RunBackgroundUpdateCheck()
        {
            var appUpdateTask = Updater.CheckForUpdatesAsync();
            var depUpdateTask = DependencyManager.CheckForNewVersionsAsync();
            await Task.WhenAll(appUpdateTask, depUpdateTask);
            var updateInfo = await appUpdateTask;
            bool hasBinUpdate = DependencyManager.HasFfmpegUpdate || DependencyManager.HasMtxUpdate;

            if (updateInfo.HasUpdate || hasBinUpdate)
            {
                string msg = "";
                if (updateInfo.HasUpdate) msg += $"Nova versão do app ({updateInfo.Version}) disponível!\n";
                if (hasBinUpdate) msg += "Novas versões do FFmpeg/MediaMTX disponíveis!\n";
                msg += "Clique aqui para atualizar.";

                InvokeSafe(() =>
                {
                    RefreshAboutVersionLabels();
                    InfoBarUpdate.Title = LanguageManager.Get("UpdateAvailable");
                    InfoBarUpdate.Message = msg.Replace("\n", " ");
                    InfoBarUpdate.Severity = InfoBarSeverity.Warning;
                    InfoBarUpdate.IsOpen = true;
                    btnUpdateBinaries.Visibility = Visibility.Visible;

                    trayIcon?.ShowBalloonTip("Atualização Disponível", msg);
                    this.pendingUpdateUrl = updateInfo.DownloadUrl;
                    this.hasPendingBinUpdate = hasBinUpdate;
                });
            }
        }

        public void StartAuto()
        {
            HideForm();
            watchdogTimer.Start();
            if (!chkSchedule.IsOn) BtnStart_Click(null, null);
        }

        public void InvokeSafe(Action action)
        {
            if (DispatcherQueue == null || DispatcherQueue.HasThreadAccess)
            {
                action();
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                if (!DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        action();
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }))
                {
                    tcs.TrySetResult(false);
                }
                tcs.Task.GetAwaiter().GetResult();
            }
        }

        internal void StartClick() => BtnStart_Click(null, null);
        internal void StopClick() => BtnStop_Click(null, null);
        internal void TriggerSave() => SaveSettings();

        internal void UpdateUrlStatus()
        {
            InvokeSafe(() =>
            {
                string labelText = "";
                string localIp = (isTransmitting || chkWebServer.IsOn) ? mediaServer.GetLocalIPAddress() : "127.0.0.1";
                if (isTransmitting)
                {
                    string actualPass = string.IsNullOrEmpty(txtPassword.Password) ? currentPassword : txtPassword.Password;
                    string auth = (chkAuth.IsOn && !string.IsNullOrEmpty(txtUsername.Text) && !string.IsNullOrEmpty(actualPass))
                        ? $"{txtUsername.Text}:{actualPass}@"
                        : "";
                    string streamName = string.IsNullOrWhiteSpace(txtStreamName.Text) ? "camera1" : txtStreamName.Text.Trim().Trim('/');

                    currentLanUrl = $"rtsp://{auth}{localIp}:{txtPort.Text}/{streamName}";
                    currentLanLowUrl = $"rtsp://{auth}{localIp}:{txtPort.Text}/{streamName}_low";

                    labelText += $"Principal: {currentLanUrl}";
                    if (chkDualStream.IsOn) labelText += $"\nLeve:      {currentLanLowUrl}";

                    btnCopyLan.Visibility = Visibility.Visible;
                    btnCopyLocal.Visibility = chkDualStream.IsOn ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    labelText = LanguageManager.Get("StatusWaitingLinks");
                    btnCopyLan.Visibility = Visibility.Collapsed;
                    btnCopyLocal.Visibility = Visibility.Collapsed;
                }

                if (chkWebServer.IsOn)
                {
                    currentWebUrl = $"http://{localIp}:{txtWebPort.Text}";
                    if (lblWebUrl != null) lblWebUrl.Text = currentWebUrl;
                    btnCopyWeb.Visibility = Visibility.Visible;
                }
                else
                {
                    if (lblWebUrl != null) lblWebUrl.Text = "";
                    btnCopyWeb.Visibility = Visibility.Collapsed;
                }

                lblUrl.Text = labelText.TrimEnd('\n');
            });
        }
    }

    public sealed class WinUITextBoxWriter : TextWriter
    {
        private readonly TextBox _textBox;
        private readonly DispatcherQueue _dispatcher;
        private readonly int _maxLines;
        private readonly Func<bool> _isVisibleProvider;
        private readonly Queue<string> _lines = new Queue<string>();
        private readonly StringBuilder _currentLine = new StringBuilder();
        private readonly object _sync = new object();
        private bool _uiUpdatePending = false;
        private bool _isDirty = false;
        private long _lastUiFlushTick = 0;

        public WinUITextBoxWriter(TextBox textBox, DispatcherQueue dispatcher, int maxLines = 300, Func<bool> isVisibleProvider = null)
        {
            _textBox = textBox;
            _dispatcher = dispatcher;
            _maxLines = Math.Max(10, maxLines);
            _isVisibleProvider = isVisibleProvider;
        }

        public void Clear()
        {
            lock (_sync)
            {
                _lines.Clear();
                _currentLine.Clear();
                _isDirty = false;
            }
        }

        public string GetSnapshot()
        {
            lock (_sync)
            {
                var sb = new StringBuilder(_lines.Count * 64);
                foreach (var line in _lines)
                {
                    sb.AppendLine(line);
                }
                if (_currentLine.Length > 0)
                {
                    sb.Append(_currentLine);
                }
                return sb.ToString();
            }
        }

        public void FlushToUI()
        {
            if (_dispatcher == null) return;
            _dispatcher.TryEnqueue(() =>
            {
                string snapshot;
                lock (_sync)
                {
                    _uiUpdatePending = false;
                    _isDirty = false;
                    _lastUiFlushTick = Environment.TickCount64;
                }
                snapshot = GetSnapshot();
                try
                {
                    _textBox.Text = snapshot;
                    _textBox.SelectionStart = snapshot.Length;
                }
                catch { }
            });
        }

        public override void Write(char value) => Write(value.ToString());

        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value) || _dispatcher == null) return;

            bool shouldSchedule = false;
            lock (_sync)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '\r') continue;
                    if (c == '\n')
                    {
                        _lines.Enqueue(_currentLine.ToString());
                        _currentLine.Clear();
                        while (_lines.Count > _maxLines)
                        {
                            _lines.Dequeue();
                        }
                    }
                    else
                    {
                        _currentLine.Append(c);
                    }
                }

                _isDirty = true;
                long now = Environment.TickCount64;
                if (!_uiUpdatePending && (now - _lastUiFlushTick) >= 200)
                {
                    _uiUpdatePending = true;
                    shouldSchedule = true;
                }
            }

            if (shouldSchedule)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    if (_isVisibleProvider != null && !_isVisibleProvider())
                    {
                        lock (_sync)
                        {
                            _uiUpdatePending = false;
                        }
                        return;
                    }

                    string snapshot;
                    lock (_sync)
                    {
                        if (!_isDirty)
                        {
                            _uiUpdatePending = false;
                            return;
                        }
                        _uiUpdatePending = false;
                        _isDirty = false;
                        _lastUiFlushTick = Environment.TickCount64;
                    }

                    snapshot = GetSnapshot();
                    try
                    {
                        _textBox.Text = snapshot;
                        _textBox.SelectionStart = snapshot.Length;
                    }
                    catch { }
                });
            }
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
