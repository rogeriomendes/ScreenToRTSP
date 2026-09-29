using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using ScreenToRTSP.I18n;

namespace ScreenToRTSP.UI
{
    public static class UIManager
    {
        public static System.Collections.Generic.List<Action> UpdateTexts = new System.Collections.Generic.List<Action>();
        private static bool IsEventRegistered = false;
        private static ToolTip uiToolTip;

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        private static void ApplyDarkThemeToControl(Control c) {
            try {
                c.HandleCreated += (s, e) => { SetWindowTheme(c.Handle, "DarkMode_CFD", null); };
                if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_CFD", null);
            } catch { }
        }

        private static void MakeGreenCheckBox(CheckBox chk)
        {
            chk.Appearance = Appearance.Normal;
            chk.BackColor = ThemeColors.BgMain;
            chk.ForeColor = ThemeColors.TextMain;
            chk.Paint += (s, e) => {
                e.Graphics.Clear(chk.BackColor);
                int boxSize = 16;
                var rect = new Rectangle(0, (chk.Height - boxSize) / 2, boxSize, boxSize);
                if (chk.Checked) {
                    using (var brush = new SolidBrush(Color.FromArgb(22, 163, 74)))
                        e.Graphics.FillRectangle(brush, rect);
                    using (var pen = new Pen(ThemeColors.TextMain, 2)) {
                        e.Graphics.DrawLines(pen, new[] {
                            new Point(rect.X + 3, rect.Y + 8),
                            new Point(rect.X + 6, rect.Y + 11),
                            new Point(rect.X + 12, rect.Y + 4)
                        });
                    }
                } else {
                    using (var pen = new Pen(UIManager.ThemeColors.Border, 1))
                        e.Graphics.DrawRectangle(pen, rect);
                    using (var brush = new SolidBrush(UIManager.ThemeColors.BgPanel))
                        e.Graphics.FillRectangle(brush, rect.X + 1, rect.Y + 1, rect.Width - 1, rect.Height - 1);
                }
                using (var brush = new SolidBrush(chk.ForeColor)) {
                    e.Graphics.DrawString(chk.Text, chk.Font, brush, boxSize + 6, (chk.Height - e.Graphics.MeasureString(chk.Text, chk.Font).Height) / 2);
                }
            };
        }

        public static class ThemeColors
        {
            public static bool IsDark { get; set; } = true;
            public static Color BgMain => IsDark ? Color.FromArgb(15, 23, 42) : Color.FromArgb(241, 245, 249);
            public static Color BgPanel => IsDark ? Color.FromArgb(30, 41, 59) : Color.White;
            public static Color TextMain => IsDark ? Color.White : Color.FromArgb(15, 23, 42);
            public static Color TextMuted => IsDark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139);
            public static Color Border => IsDark ? Color.FromArgb(51, 65, 85) : Color.FromArgb(203, 213, 225);
            public static Color Highlight => Color.FromArgb(34, 197, 94);
        }

        public static int ActiveTabIndex = 0;
        public static void Build(Main form)
        {
            var cfg = form.configManager.Load();
            string themeStr = cfg.ContainsKey("Theme") ? cfg["Theme"] : "System";
            if (themeStr == "System") {
                try {
                    object registryValue = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", "1");
                    ThemeColors.IsDark = registryValue == null || registryValue.ToString() == "0";
                } catch { ThemeColors.IsDark = true; }
            } else {
                ThemeColors.IsDark = themeStr == "Dark";
            }

            if (uiToolTip == null) {
                uiToolTip = new ToolTip();
                uiToolTip.AutoPopDelay = 10000;
                uiToolTip.InitialDelay = 100;
                uiToolTip.ReshowDelay = 100;
                uiToolTip.ShowAlways = true;
                uiToolTip.Active = true;
            }
            UpdateTexts.Clear();
            if (!IsEventRegistered) {
                LanguageManager.OnLanguageChanged += () => {
                    if (form.IsDisposed) return;
                    form.InvokeSafe(() => {
                        foreach (var a in UpdateTexts) a();
                        // Refresh button and status
                        if (form.isTransmitting) {
                            form.btnStart.Text = "■ " + LanguageManager.Get("BtnStop");
                            form.btnStart.BackColor = Color.FromArgb(225, 29, 72);
                            form.lblStatus.Text = "● " + LanguageManager.Get("StatusPrefix") + LanguageManager.Get("StatusRunning");
                            form.lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                        } else {
                            form.btnStart.Text = "▶ " + LanguageManager.Get("BtnStart");
                            form.btnStart.BackColor = Color.FromArgb(22, 163, 74);
                            form.lblStatus.Text = "● " + LanguageManager.Get("StatusPrefix") + LanguageManager.Get("StatusStopped");
                            form.lblStatus.ForeColor = Color.FromArgb(225, 29, 72);
                        }
                    });
                };
                IsEventRegistered = true;
            }

            form.BackColor = ThemeColors.BgMain;
            form.ClientSize = new Size(700, 660);
            form.MinimumSize = new Size(700, 660);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;

            // Header Moderno
            Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = UIManager.ThemeColors.BgPanel };
            form.Controls.Add(headerPanel);
            
            Label lblTitle = new Label { Left = 25, Top = 25, AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.FromArgb(34, 197, 94) };
            headerPanel.Controls.Add(lblTitle);
            UpdateTexts.Add(() => lblTitle.Text = LanguageManager.Get("AppTitle"));
            
            Label lblSubTitle = new Label { Left = 28, Top = 65, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Regular), ForeColor = UIManager.ThemeColors.TextMuted };
            headerPanel.Controls.Add(lblSubTitle);
            UpdateTexts.Add(() => lblSubTitle.Text = LanguageManager.Get("AppSubtitle"));

            // Container principal com sombra fake ou borda
            Panel mainContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
            form.Controls.Add(mainContainer);

            form.lblSaveStatus = new Label { Dock = DockStyle.Bottom, Height = 25, ForeColor = Color.FromArgb(22, 163, 74), Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent, Visible = false, Padding = new Padding(0,5,5,0) };
            mainContainer.Controls.Add(form.lblSaveStatus);

            // Custom Tabs to avoid Windows Forms white borders
            Panel tabHeaderPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = ThemeColors.BgMain, Margin = new Padding(0,0,0,10) };
            mainContainer.Controls.Add(tabHeaderPanel);
            
            Panel tabContentPanel = new Panel { Dock = DockStyle.Fill, BackColor = ThemeColors.BgMain };
            mainContainer.Controls.Add(tabContentPanel);
            tabContentPanel.BringToFront();

            var tabButtons = new System.Collections.Generic.List<Button>();
            var tabPages = new System.Collections.Generic.List<Panel>();

            Panel AddCustomTab(string titleLangKey, string fixedTitle = null)
            {
                Panel pnl = new Panel { Dock = DockStyle.Fill, BackColor = ThemeColors.BgMain, Padding = new Padding(0, 15, 0, 0), Visible = false };
                tabContentPanel.Controls.Add(pnl);
                tabPages.Add(pnl);

                Button btn = new Button { FlatStyle = FlatStyle.Flat, Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(10, 0, 10, 0), BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMuted, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
                btn.FlatAppearance.BorderSize = 0;
                
                UpdateTexts.Add(() => {
                    btn.Text = fixedTitle ?? LanguageManager.Get(titleLangKey);
                });
                
                tabHeaderPanel.Controls.Add(btn);
                btn.BringToFront();
                tabButtons.Add(btn);

                btn.Click += (s, e) => {
                    ActiveTabIndex = tabButtons.IndexOf(btn);
                    foreach (var p in tabPages) p.Visible = false;
                    foreach (var b in tabButtons) { 
                                                b.BackColor = UIManager.ThemeColors.BgPanel; 
                        b.ForeColor = UIManager.ThemeColors.TextMuted; 
                        b.Paint -= PaintActiveTabIndicator;
                        b.Invalidate();
                    }
                    
                    pnl.Visible = true;
                    btn.BackColor = UIManager.ThemeColors.BgMain;
                    btn.ForeColor = Color.FromArgb(34, 197, 94); // Green accent for active text
                    btn.Paint += PaintActiveTabIndicator;
                    btn.Invalidate();
                };
                return pnl;
            }
            
            void PaintActiveTabIndicator(object sender, PaintEventArgs e) {
                var b = sender as Button;
                using (var pen = new Pen(Color.FromArgb(34, 197, 94), 3)) {
                    e.Graphics.DrawLine(pen, 0, b.Height - 2, b.Width, b.Height - 2);
                }
            }

            Panel tabVideo = AddCustomTab("TabVideo");
            Panel tabRede = AddCustomTab("TabNetwork");
            Panel tabAuto = AddCustomTab("TabAutomation");
            Panel tabConfig = AddCustomTab("TabConfig");
            Panel tabLogs = AddCustomTab("", "Logs");
            Panel tabSobre = AddCustomTab("TabAbout");

            if (form.IsHandleCreated) {
                int target = ActiveTabIndex;
                if (tabButtons.Count > target) {
                    form.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate {
                        tabButtons[target].PerformClick();
                    });
                }
            } else {
                form.Load += (s, e) => {
                    if (tabButtons.Count > ActiveTabIndex) tabButtons[ActiveTabIndex].PerformClick();
                };
            }

            TableLayoutPanel CreateTablePanel()
            {
                var tlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0), AutoScroll = true };
                tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
                tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
                return tlp;
            }

            void AddRow(TableLayoutPanel tlp, int row, string langKey, Control input, int height = 38)
            {
                Label lbl = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI", 10, FontStyle.Regular), ForeColor = UIManager.ThemeColors.TextMain };
                UpdateTexts.Add(() => {
                    lbl.Text = LanguageManager.Get(langKey);
                    
                    // Se houver tooltip para essa chave, aplicar também ao label
                    string tipKey = "Tip" + langKey.Replace("Label", "");
                    string tip = LanguageManager.Get(tipKey);
                    if (!string.IsNullOrEmpty(tip) && tip != tipKey) {
                        uiToolTip.SetToolTip(lbl, tip);
                        uiToolTip.SetToolTip(input, tip);
                    }
                });
                input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                input.Font = new Font("Segoe UI", 10);
                input.Margin = new Padding(5, 5, 5, 5);
                ApplyDarkThemeToControl(input);
                
                tlp.Controls.Add(lbl, 0, row);
                tlp.Controls.Add(input, 1, row);
                tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            }

            void AddDummyRow(TableLayoutPanel tlp, int row)
            {
                tlp.Controls.Add(new Label { AutoSize = false, Height = 0 }, 0, row);
                tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            }

            // --- TAB VÍDEO ---
            var tlpVideo = CreateTablePanel();
            tabVideo.Controls.Add(tlpVideo);

            form.cmbMonitor = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            UpdateTexts.Add(() => {
                int c = form.cmbMonitor.SelectedIndex;
                form.cmbMonitor.Items.Clear();
                form.cmbMonitor.Items.Add(LanguageManager.Get("AllMonitors"));
                for (int i = 0; i < Screen.AllScreens.Length; i++) form.cmbMonitor.Items.Add($"{LanguageManager.Get("MonitorPrefix")} {i + 1}");
                if (c >= 0 && c < form.cmbMonitor.Items.Count) form.cmbMonitor.SelectedIndex = c;
                else form.cmbMonitor.SelectedIndex = 0;
            });
            AddRow(tlpVideo, 0, "MonitorLabel", form.cmbMonitor);

            form.cmbEncoder = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            UpdateTexts.Add(() => {
                int c = form.cmbEncoder.SelectedIndex;
                form.cmbEncoder.Items.Clear();
                form.cmbEncoder.Items.AddRange(new[] { LanguageManager.Get("EncoderCPU"), LanguageManager.Get("EncoderNVENC"), LanguageManager.Get("EncoderQSV") });
                if (c >= 0 && c < form.cmbEncoder.Items.Count) form.cmbEncoder.SelectedIndex = c;
                else form.cmbEncoder.SelectedIndex = 0;
            });
            form.cmbEncoder.SelectedIndexChanged += (s, e) => {
                string tip = "";
                if (form.cmbEncoder.SelectedIndex == 0) tip = LanguageManager.Get("TipCPU");
                else if (form.cmbEncoder.SelectedIndex == 1) tip = LanguageManager.Get("TipNVENC");
                else if (form.cmbEncoder.SelectedIndex == 2) tip = LanguageManager.Get("TipQSV");
                uiToolTip.SetToolTip(form.cmbEncoder, tip);
                form.TriggerAutoSave();
            };
            UpdateTexts.Add(() => {
                string tip = "";
                if (form.cmbEncoder.SelectedIndex == 0) tip = LanguageManager.Get("TipCPU");
                else if (form.cmbEncoder.SelectedIndex == 1) tip = LanguageManager.Get("TipNVENC");
                else if (form.cmbEncoder.SelectedIndex == 2) tip = LanguageManager.Get("TipQSV");
                uiToolTip.SetToolTip(form.cmbEncoder, tip);
            });
            AddRow(tlpVideo, 1, "EncoderLabel", form.cmbEncoder);

            form.cmbQuality = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            UpdateTexts.Add(() => {
                int c = form.cmbQuality.SelectedIndex;
                form.cmbQuality.Items.Clear();
                form.cmbQuality.Items.AddRange(new[] { LanguageManager.Get("QualityLow"), LanguageManager.Get("QualityMedium"), LanguageManager.Get("QualityHigh") });
                if (c >= 0 && c < form.cmbQuality.Items.Count) form.cmbQuality.SelectedIndex = c;
                else form.cmbQuality.SelectedIndex = 1;
            });
            AddRow(tlpVideo, 2, "QualityLabel", form.cmbQuality);

            form.cmbFps = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            form.cmbFps.Items.AddRange(new[] { "10", "12", "15", "20", "24", "30" });
            form.cmbFps.SelectedIndex = 3;
            AddRow(tlpVideo, 3, "FpsLabel", form.cmbFps);

            form.chkDualStream = new CheckBox { AutoSize = false, Size = new Size(550, 45), Checked = true, Margin = new Padding(5, 10, 5, 5), Font = new Font("Segoe UI", 10) };
            MakeGreenCheckBox(form.chkDualStream);
            UpdateTexts.Add(() => {
                form.chkDualStream.Text = LanguageManager.Get("DualStreamLabel");
                uiToolTip.SetToolTip(form.chkDualStream, LanguageManager.Get("TipDualStream"));
            });
            tlpVideo.Controls.Add(form.chkDualStream, 0, 4);
            tlpVideo.SetColumnSpan(form.chkDualStream, 2);
            tlpVideo.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            form.chkMouseCursor = new CheckBox { AutoSize = false, Size = new Size(550, 45), Checked = true, Margin = new Padding(5), Font = new Font("Segoe UI", 10) };
            MakeGreenCheckBox(form.chkMouseCursor);
            UpdateTexts.Add(() => form.chkMouseCursor.Text = LanguageManager.Get("MouseCursorLabel"));
            tlpVideo.Controls.Add(form.chkMouseCursor, 0, 5);
            tlpVideo.SetColumnSpan(form.chkMouseCursor, 2);
            tlpVideo.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            AddDummyRow(tlpVideo, 6);

            // --- TAB REDE ---
            var tlpRede = CreateTablePanel();
            tabRede.Controls.Add(tlpRede);

            form.txtStreamName = new TextBox { Text = "camera1", BorderStyle = BorderStyle.FixedSingle, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            AddRow(tlpRede, 0, "StreamNameLabel", form.txtStreamName);

            form.txtPort = new TextBox { Text = "8554", BorderStyle = BorderStyle.FixedSingle, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            AddRow(tlpRede, 1, "PortLabel", form.txtPort);

            form.chkAuth = new CheckBox { AutoSize = false, Size = new Size(550, 45), Checked = true, Margin = new Padding(5), Font = new Font("Segoe UI", 10) };
            MakeGreenCheckBox(form.chkAuth);
            UpdateTexts.Add(() => form.chkAuth.Text = LanguageManager.Get("AuthLabel"));
            form.chkAuth.CheckedChanged += (s, e) => { form.txtUsername.Enabled = form.chkAuth.Checked; form.txtPassword.Enabled = form.chkAuth.Checked; };
            tlpRede.Controls.Add(form.chkAuth, 0, 2);
            tlpRede.SetColumnSpan(form.chkAuth, 2);
            tlpRede.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            form.txtUsername = new TextBox { Text = "admin", BorderStyle = BorderStyle.FixedSingle, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            AddRow(tlpRede, 3, "UserLabel", form.txtUsername);

            form.txtPassword = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            UpdateTexts.Add(() => {
                if (!string.IsNullOrEmpty(form.currentPassword)) {
                    form.txtPassword.PlaceholderText = LanguageManager.Get("PassPlaceholder");
                } else {
                    form.txtPassword.PlaceholderText = "";
                }
            });
            AddRow(tlpRede, 4, "PasswordLabel", form.txtPassword);

            AddDummyRow(tlpRede, 5);

            // --- TAB AUTOMAÇÃO ---
            var tlpAuto = CreateTablePanel();
            tabAuto.Controls.Add(tlpAuto);

            form.chkStartWithWindows = new CheckBox { AutoSize = false, Size = new Size(550, 45), Margin = new Padding(5), Font = new Font("Segoe UI", 10) };
            MakeGreenCheckBox(form.chkStartWithWindows);
            UpdateTexts.Add(() => form.chkStartWithWindows.Text = LanguageManager.Get("StartWindowsLabel"));
            form.chkStartWithWindows.CheckedChanged += (s, e) => { form.ToggleStartWithWindows(); };
            tlpAuto.Controls.Add(form.chkStartWithWindows, 0, 0);
            tlpAuto.SetColumnSpan(form.chkStartWithWindows, 2);
            tlpAuto.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            form.chkSchedule = new CheckBox { AutoSize = false, Size = new Size(550, 45), Margin = new Padding(5, 5, 5, 10), Font = new Font("Segoe UI", 10) };
            MakeGreenCheckBox(form.chkSchedule);
            UpdateTexts.Add(() => {
                form.chkSchedule.Text = LanguageManager.Get("ScheduleLabel");
                uiToolTip.SetToolTip(form.chkSchedule, LanguageManager.Get("TipSchedule"));
            });
            tlpAuto.Controls.Add(form.chkSchedule, 0, 1);
            tlpAuto.SetColumnSpan(form.chkSchedule, 2);
            tlpAuto.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            var flowSchedule = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            
            var lblSchedStart = new Label { AutoSize = true, Margin = new Padding(0, 4, 5, 0), Font = new Font("Segoe UI", 10), ForeColor = UIManager.ThemeColors.TextMain };
            UpdateTexts.Add(() => lblSchedStart.Text = LanguageManager.Get("ScheduleStartLabel"));
            flowSchedule.Controls.Add(lblSchedStart);
            
            form.txtScheduleStart = new MaskedTextBox { Mask = "00:00", Text = "0545", Width = 60, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, Font = new Font("Segoe UI", 10), BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain, Margin = new Padding(0, 2, 20, 0) };
            ApplyDarkThemeToControl(form.txtScheduleStart);
            flowSchedule.Controls.Add(form.txtScheduleStart);
            
            var lblSchedEnd = new Label { AutoSize = true, Margin = new Padding(0, 4, 5, 0), Font = new Font("Segoe UI", 10), ForeColor = UIManager.ThemeColors.TextMain };
            UpdateTexts.Add(() => lblSchedEnd.Text = LanguageManager.Get("ScheduleEndLabel"));
            flowSchedule.Controls.Add(lblSchedEnd);
            
            form.txtScheduleEnd = new MaskedTextBox { Mask = "00:00", Text = "2215", Width = 60, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, Font = new Font("Segoe UI", 10), BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain, Margin = new Padding(0, 2, 0, 0) };
            ApplyDarkThemeToControl(form.txtScheduleEnd);
            flowSchedule.Controls.Add(form.txtScheduleEnd);

            tlpAuto.Controls.Add(flowSchedule, 0, 2);
            tlpAuto.SetColumnSpan(flowSchedule, 2);
            tlpAuto.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

            AddDummyRow(tlpAuto, 3);

            // --- TAB CONFIGURAÇÕES ---
            var tlpConfig = CreateTablePanel();
            tabConfig.Controls.Add(tlpConfig);

            form.cmbLanguage = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            
            UpdateTexts.Add(() => {
                var items = LanguageManager.GetLanguageKeys();
                int currentSel = form.cmbLanguage.SelectedIndex;
                form.cmbLanguage.Items.Clear();
                foreach(var k in items) {
                    if (k == "System") form.cmbLanguage.Items.Add(LanguageManager.Get("LanguageSystem"));
                    else if (k == "pt-BR") form.cmbLanguage.Items.Add("Português (Brasil)");
                    else if (k == "en-US") form.cmbLanguage.Items.Add("English (US)");
                    else if (k == "es-ES") form.cmbLanguage.Items.Add("Español (España)");
                    else form.cmbLanguage.Items.Add(k);
                }
                if (currentSel >= 0 && currentSel < form.cmbLanguage.Items.Count) {
                    form.cmbLanguage.SelectedIndex = currentSel;
                } else {
                    int idx = items.IndexOf(LanguageManager.CurrentLangCode);
                    form.cmbLanguage.SelectedIndex = idx != -1 ? idx : 0;
                }
            });
            form.cmbLanguage.SelectedIndexChanged += (s, e) => {
                var keys = LanguageManager.GetLanguageKeys();
                if (form.cmbLanguage.SelectedIndex >= 0 && form.cmbLanguage.SelectedIndex < keys.Count) {
                    var selectedKey = keys[form.cmbLanguage.SelectedIndex];
                    if (selectedKey != LanguageManager.CurrentLangCode) {
                        LanguageManager.ApplyLanguage(selectedKey);
                    }
                }
            };
            AddRow(tlpConfig, 0, "LanguageLabel", form.cmbLanguage);

            ComboBox cmbTheme = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            UpdateTexts.Add(() => {
                int c = cmbTheme.SelectedIndex;
                cmbTheme.Items.Clear();
                cmbTheme.Items.AddRange(new[] { LanguageManager.Get("ThemeSystem"), LanguageManager.Get("ThemeDark"), LanguageManager.Get("ThemeLight") });
                if (c == -1) {
                    var cfg = form.configManager.Load();
                    string t = cfg.ContainsKey("Theme") ? cfg["Theme"] : "System";
                    if (t == "Dark") cmbTheme.SelectedIndex = 1;
                    else if (t == "Light") cmbTheme.SelectedIndex = 2;
                    else cmbTheme.SelectedIndex = 0;
                } else {
                    cmbTheme.SelectedIndex = c;
                }
            });
            cmbTheme.SelectedIndexChanged += (s, e) => {
                string newTheme = cmbTheme.SelectedIndex == 1 ? "Dark" : (cmbTheme.SelectedIndex == 2 ? "Light" : "System");
                var c = form.configManager.Load();
                string current = c.ContainsKey("Theme") ? c["Theme"] : "System";
                if (newTheme != current) {
                    c["Theme"] = newTheme;
                    form.configManager.Save(System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(c, kv => kv.Key + "=" + kv.Value)));
                    form.SaveSettings();
                    form.Controls.Clear();
                    UIManager.Build(form);
                    form.LoadSettings();
                    form.AttachAutoSave(form);
                    LanguageManager.ApplyLanguage(LanguageManager.CurrentLangCode);
                }
            };
            AddRow(tlpConfig, 1, "ThemeLabel", cmbTheme);

            form.chkWebServer = new CheckBox { AutoSize = true, Checked = false, Margin = new Padding(5), Font = new Font("Segoe UI", 10) };
            MakeGreenCheckBox(form.chkWebServer);
            UpdateTexts.Add(() => form.chkWebServer.Text = LanguageManager.Get("WebServerLabel"));
            form.chkWebServer.CheckedChanged += (s, e) => { 
                if (form.chkWebServer.Checked) form.webPanel.Start(); else form.webPanel.Stop();
                form.UpdateUrlStatus();
            };
            tlpConfig.Controls.Add(form.chkWebServer, 0, 2);
            tlpConfig.SetColumnSpan(form.chkWebServer, 2);
            tlpConfig.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));

            form.txtWebPort = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10), Margin = new Padding(5), BackColor = UIManager.ThemeColors.BgPanel, ForeColor = UIManager.ThemeColors.TextMain };
            AddRow(tlpConfig, 3, "WebPortLabel", form.txtWebPort);
            
            System.Windows.Forms.Timer portTypingTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            portTypingTimer.Tick += (s, e) => {
                portTypingTimer.Stop();
                if (form.chkWebServer.Checked) {
                    form.webPanel.Stop();
                    form.webPanel.Start();
                    form.UpdateUrlStatus();
                }
            };
            form.txtWebPort.TextChanged += (s, e) => {
                portTypingTimer.Stop();
                portTypingTimer.Start();
            };

            form.lblWebUrl = new Label { Dock = DockStyle.Fill, ForeColor = UIManager.ThemeColors.Border, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            tlpConfig.Controls.Add(form.lblWebUrl, 0, 4);
            
            form.btnCopyWeb = new Button { Dock = DockStyle.Fill, Margin = new Padding(3,5,3,5), Visible = false, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(51, 122, 183), ForeColor = UIManager.ThemeColors.TextMain, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
            UpdateTexts.Add(() => form.btnCopyWeb.Text = LanguageManager.Get("BtnOpenWeb"));
            form.btnCopyWeb.FlatAppearance.BorderSize = 0;
            form.btnCopyWeb.Click += (s, e) => { if (!string.IsNullOrEmpty(form.currentWebUrl)) Process.Start(new ProcessStartInfo(form.currentWebUrl) { UseShellExecute = true }); };
            tlpConfig.Controls.Add(form.btnCopyWeb, 1, 4);
            tlpConfig.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            
            AddDummyRow(tlpConfig, 5);

            // --- BOTTON PANEL ---
            Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 195, Padding = new Padding(15, 0, 15, 15) };
            form.Controls.Add(bottomPanel);
            
            TableLayoutPanel btnLayout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 55, ColumnCount = 1, Margin = new Padding(0,0,0,15) };
            btnLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottomPanel.Controls.Add(btnLayout);

            form.btnStart = new Button { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), BackColor = Color.FromArgb(22, 163, 74), ForeColor = UIManager.ThemeColors.TextMain, Font = new Font("Segoe UI", 11, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            UpdateTexts.Add(() => form.btnStart.Text = form.isTransmitting ? "■ " + LanguageManager.Get("BtnStop") : "▶ " + LanguageManager.Get("BtnStart"));
            form.btnStart.FlatAppearance.BorderSize = 0;
            form.btnStart.Click += (s, e) => {
                if (form.isTransmitting) form.StopClick();
                else form.StartClick();
            };
            form.btnStart.MouseEnter += (s, e) => {
                if (!form.isTransmitting) form.btnStart.BackColor = Color.FromArgb(21, 128, 61); // green-700
                else form.btnStart.BackColor = Color.FromArgb(190, 18, 60); // rose-700
            };
            form.btnStart.MouseLeave += (s, e) => {
                if (!form.isTransmitting) form.btnStart.BackColor = Color.FromArgb(22, 163, 74);
                else form.btnStart.BackColor = Color.FromArgb(225, 29, 72);
            };
            btnLayout.Controls.Add(form.btnStart, 0, 0);

            Panel pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = UIManager.ThemeColors.BgMain };
            pnlStatus.Paint += (s, ev) => {
                ev.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, pnlStatus.Width - 1, pnlStatus.Height - 1);
                int radius = 12;
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseFigure();
                
                using (var brush = new SolidBrush(UIManager.ThemeColors.BgPanel)) {
                    ev.Graphics.FillPath(brush, path);
                }
                using (var pen = new Pen(UIManager.ThemeColors.Border, 1)) {
                    ev.Graphics.DrawPath(pen, path);
                }
            };
            bottomPanel.Controls.Add(pnlStatus);

            form.lblStatus = new Label { Dock = DockStyle.Top, Height = 32, ForeColor = Color.FromArgb(225, 29, 72), Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(15, 8, 0, 0), BackColor = Color.Transparent };
            pnlStatus.Controls.Add(form.lblStatus);

            form.lblUrl = new Label { Dock = DockStyle.Top, Height = 42, ForeColor = UIManager.ThemeColors.TextMuted, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(15, 0, 0, 0), BackColor = Color.Transparent };
            UpdateTexts.Add(() => form.UpdateUrlStatus());
            pnlStatus.Controls.Add(form.lblUrl);
            form.lblUrl.BringToFront();

            TableLayoutPanel tlpCopy = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(12, 5, 12, 5), ColumnCount = 2, BackColor = Color.Transparent };
            tlpCopy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tlpCopy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pnlStatus.Controls.Add(tlpCopy);

            form.btnCopyLan = new Button { Dock = DockStyle.Fill, Margin = new Padding(3,0,3,0), Visible = false, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgMain, ForeColor = UIManager.ThemeColors.TextMain, Font = new Font("Segoe UI", 8, FontStyle.Regular), Cursor = Cursors.Hand };
            UpdateTexts.Add(() => form.btnCopyLan.Text = LanguageManager.Get("BtnCopyLan"));
            form.btnCopyLan.FlatAppearance.BorderColor = UIManager.ThemeColors.Border;
            form.btnCopyLan.Click += (s, e) => { if (!string.IsNullOrEmpty(form.currentLanUrl)) Clipboard.SetText(form.currentLanUrl); };
            form.btnCopyLan.MouseEnter += (s, e) => form.btnCopyLan.BackColor = Color.FromArgb(51, 65, 85);
            form.btnCopyLan.MouseLeave += (s, e) => form.btnCopyLan.BackColor = UIManager.ThemeColors.BgMain;
            tlpCopy.Controls.Add(form.btnCopyLan, 0, 0);

            form.btnCopyLocal = new Button { Dock = DockStyle.Fill, Margin = new Padding(3,0,3,0), Visible = false, FlatStyle = FlatStyle.Flat, BackColor = UIManager.ThemeColors.BgMain, ForeColor = UIManager.ThemeColors.TextMain, Font = new Font("Segoe UI", 8, FontStyle.Regular), Cursor = Cursors.Hand };
            UpdateTexts.Add(() => form.btnCopyLocal.Text = LanguageManager.Get("BtnCopyLocal"));
            form.btnCopyLocal.FlatAppearance.BorderColor = UIManager.ThemeColors.Border;
            form.btnCopyLocal.Click += (s, e) => { if (!string.IsNullOrEmpty(form.currentLanLowUrl)) Clipboard.SetText(form.currentLanLowUrl); };
            form.btnCopyLocal.MouseEnter += (s, e) => form.btnCopyLocal.BackColor = Color.FromArgb(51, 65, 85);
            form.btnCopyLocal.MouseLeave += (s, e) => form.btnCopyLocal.BackColor = UIManager.ThemeColors.BgMain;
            tlpCopy.Controls.Add(form.btnCopyLocal, 1, 0);

            // Tray Menu
            
            // TAB LOGS
            form.rtbLogs = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = UIManager.ThemeColors.BgMain, ForeColor = UIManager.ThemeColors.TextMain, Font = new Font("Consolas", 9), BorderStyle = BorderStyle.None };
            tabLogs.Controls.Add(form.rtbLogs);
            var _dummy = form.rtbLogs.Handle; // Force handle creation
            Console.SetOut(new TextBoxWriter(form.rtbLogs));
            Console.WriteLine("--- " + LanguageManager.Get("AppTitle") + " Logs ---");

            // TAB SOBRE
            Panel pnlSobre = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            tabSobre.Controls.Add(pnlSobre);

                        PictureBox picIcon = new PictureBox { Size = new Size(64, 64), Location = new Point(20, 20), SizeMode = PictureBoxSizeMode.Zoom };
            try { 
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenToRTSP.assets.icon.png"))
                {
                    if (stream != null) picIcon.Image = Image.FromStream(stream);
                }
            } catch { }
            pnlSobre.Controls.Add(picIcon);

            Label lblAboutTitle = new Label { Location = new Point(100, 25), AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.FromArgb(22, 163, 74) };
            UpdateTexts.Add(() => lblAboutTitle.Text = LanguageManager.Get("AppTitle"));
            pnlSobre.Controls.Add(lblAboutTitle);

            Label lblAboutDesc = new Label { Location = new Point(100, 55), AutoSize = true, MaximumSize = new Size(450, 0), Font = new Font("Segoe UI", 10), ForeColor = UIManager.ThemeColors.TextMuted };
            UpdateTexts.Add(() => lblAboutDesc.Text = LanguageManager.Get("AboutDesc"));
            pnlSobre.Controls.Add(lblAboutDesc);

                        FlowLayoutPanel flpApp = new FlowLayoutPanel { Location = new Point(15, 110), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight };
            pnlSobre.Controls.Add(flpApp);
            Label lblAboutVersion = new Label { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = UIManager.ThemeColors.TextMain, Margin = new Padding(0) };
            Label lblAboutUpdate = new Label { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38), Margin = new Padding(0, 2, 0, 0) };
            flpApp.Controls.Add(lblAboutVersion);
            flpApp.Controls.Add(lblAboutUpdate);
            UpdateTexts.Add(() => {
                lblAboutVersion.Text = LanguageManager.Get("AboutVersion") + Application.ProductVersion.Split('+')[0];
                if (Core.Updater.HasAppUpdate) lblAboutUpdate.Text = " (" + LanguageManager.Get("NewVersion") + Core.Updater.LatestVersionStr + ")";
                else lblAboutUpdate.Text = "";
            });

            FlowLayoutPanel flpFfmpeg = new FlowLayoutPanel { Location = new Point(15, 137), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight };
            pnlSobre.Controls.Add(flpFfmpeg);
            Label lblFFmpegVersion = new Label { AutoSize = true, Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(100, 116, 139), Margin = new Padding(0) };
            Label lblFFmpegUpdate = new Label { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38), Margin = new Padding(0) };
            flpFfmpeg.Controls.Add(lblFFmpegVersion);
            flpFfmpeg.Controls.Add(lblFFmpegUpdate);
            UpdateTexts.Add(() => {
                lblFFmpegVersion.Text = "FFmpeg " + LanguageManager.Get("AboutVersion") + Core.DependencyManager.GetFFmpegVersion();
                if (Core.DependencyManager.HasFfmpegUpdate) lblFFmpegUpdate.Text = " (" + LanguageManager.Get("NewVersion") + Core.DependencyManager.FfmpegLatestVersionStr + ")";
                else lblFFmpegUpdate.Text = "";
            });

            FlowLayoutPanel flpMtx = new FlowLayoutPanel { Location = new Point(15, 162), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight };
            pnlSobre.Controls.Add(flpMtx);
            Label lblMediaMTXVersion = new Label { AutoSize = true, Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(100, 116, 139), Margin = new Padding(0) };
            Label lblMediaMTXUpdate = new Label { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38), Margin = new Padding(0) };
            flpMtx.Controls.Add(lblMediaMTXVersion);
            flpMtx.Controls.Add(lblMediaMTXUpdate);
            UpdateTexts.Add(() => {
                lblMediaMTXVersion.Text = "MediaMTX " + LanguageManager.Get("AboutVersion") + Core.DependencyManager.GetMediaMTXVersion();
                if (Core.DependencyManager.HasMtxUpdate) lblMediaMTXUpdate.Text = " (" + LanguageManager.Get("NewVersion") + Core.DependencyManager.MtxLatestVersionStr + ")";
                else lblMediaMTXUpdate.Text = "";
            });

            Label lblUpdateAvailable = new Label { Location = new Point(20, 190), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38), Visible = false };
            UpdateTexts.Add(() => {
                lblUpdateAvailable.Text = LanguageManager.Get("UpdateAvailable");
            });
            pnlSobre.Controls.Add(lblUpdateAvailable);

            Button btnUpdateBinaries = new Button { Location = new Point(220, 215), Size = new Size(180, 35), Visible = false, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 163, 74), ForeColor = UIManager.ThemeColors.TextMain, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            UpdateTexts.Add(() => btnUpdateBinaries.Text = LanguageManager.Get("BtnUpdateBinaries"));
            btnUpdateBinaries.FlatAppearance.BorderSize = 0;
            btnUpdateBinaries.MouseEnter += (s, e) => btnUpdateBinaries.BackColor = Color.FromArgb(21, 128, 61);
            btnUpdateBinaries.MouseLeave += (s, e) => btnUpdateBinaries.BackColor = Color.FromArgb(22, 163, 74);
            pnlSobre.Controls.Add(btnUpdateBinaries);

                        btnUpdateBinaries.Click += async (s, e) => {
                bool wasTransmitting = form.isTransmitting;
                if (wasTransmitting) {
                    var result = MessageBox.Show(LanguageManager.Get("MsgStopBeforeUpdate"), "Atenção", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (result != DialogResult.OK) return;
                    form.StopClick();
                }
                
                bool updatedDeps = false;
                if (Core.DependencyManager.HasFfmpegUpdate || Core.DependencyManager.HasMtxUpdate) {
                    if (Core.DependencyManager.CheckAndDownloadDependencies(true)) {
                        await Core.DependencyManager.CheckForNewVersionsAsync();
                        updatedDeps = true;
                    }
                }

                if (Core.Updater.HasAppUpdate) {
                    await Core.Updater.PerformUpdate(Core.Updater.LatestDownloadUrl, wasTransmitting);
                    return; // The app will exit
                }

                foreach (var update in UpdateTexts) update();
                lblUpdateAvailable.Visible = false;
                btnUpdateBinaries.Visible = false;
                
                if (updatedDeps) {
                    MessageBox.Show(LanguageManager.Get("MsgUpdateSuccess"), "Screen to RTSP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                
                if (wasTransmitting) {
                    form.StartClick();
                }
            };

            Button btnCheckUpdates = new Button { Location = new Point(20, 215), Size = new Size(180, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(51, 122, 183), ForeColor = UIManager.ThemeColors.TextMain, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            UpdateTexts.Add(() => btnCheckUpdates.Text = LanguageManager.Get("BtnCheckUpdates"));
            btnCheckUpdates.FlatAppearance.BorderSize = 0;
            btnCheckUpdates.MouseEnter += (s, e) => btnCheckUpdates.BackColor = Color.FromArgb(40, 96, 144);
            btnCheckUpdates.MouseLeave += (s, e) => btnCheckUpdates.BackColor = Color.FromArgb(51, 122, 183);
            pnlSobre.Controls.Add(btnCheckUpdates);

                        btnCheckUpdates.Click += async (s, e) => {
                btnCheckUpdates.Enabled = false;
                try {
                    await Core.Updater.CheckForUpdatesAsync();
                    bool hasUpdate = await Core.DependencyManager.CheckForNewVersionsAsync(true);
                    foreach (var update in UpdateTexts) update();
                    if (hasUpdate || Core.Updater.HasAppUpdate) {
                        string msg = LanguageManager.Get("UpdateAvailable");
                        if (Core.Updater.HasAppUpdate) msg += "\n- App: " + Core.Updater.LatestVersionStr;
                        if (Core.DependencyManager.HasFfmpegUpdate) msg += "\n- FFmpeg: " + Core.DependencyManager.FfmpegLatestVersionStr;
                        if (Core.DependencyManager.HasMtxUpdate) msg += "\n- MediaMTX: " + Core.DependencyManager.MtxLatestVersionStr;
                        
                        lblUpdateAvailable.Visible = true;
                        btnUpdateBinaries.Visible = true;
                        MessageBox.Show(msg, "Screen to RTSP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    } else {
                        MessageBox.Show(LanguageManager.Get("MsgNoUpdate"), "Screen to RTSP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                } catch (Exception ex) {
                    MessageBox.Show(LanguageManager.Get("MsgCheckUpdateError") + "\n\n" + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                btnCheckUpdates.Enabled = true;
            };

                        Task.Run(async () => {
                await Core.Updater.CheckForUpdatesAsync();
                bool hasUpdate = await Core.DependencyManager.CheckForNewVersionsAsync();
                if (hasUpdate || Core.Updater.HasAppUpdate)
                {
                    form.Invoke((MethodInvoker)(() => {
                        foreach (var update in UpdateTexts) update();
                        lblUpdateAvailable.Visible = true;
                        btnUpdateBinaries.Visible = true;
                    }));
                }
            });

            form.trayIcon = new NotifyIcon();
            UpdateTexts.Add(() => form.trayIcon.Text = LanguageManager.Get("AppTitle"));
            try { form.trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { form.trayIcon.Icon = SystemIcons.Application; }
            form.trayIcon.DoubleClick += (s,e) => form.ShowFormSafe();

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            var itemStart = trayMenu.Items.Add("▶ " + LanguageManager.Get("BtnStart"), null, (s, e) => { form.StartClick(); });
            var itemStop = trayMenu.Items.Add("■ " + LanguageManager.Get("BtnStop"), null, (s, e) => { form.StopClick(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            var itemWeb = trayMenu.Items.Add(LanguageManager.Get("BtnOpenWeb"), null, (s, e) => { if (form.chkWebServer.Checked) { string port = string.IsNullOrEmpty(form.txtWebPort.Text) ? "8889" : form.txtWebPort.Text; Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true }); } });
            var itemConfig = trayMenu.Items.Add(LanguageManager.Get("TrayConfig"), null, (s, e) => { form.ShowFormSafe(); });
            var itemExit = trayMenu.Items.Add(LanguageManager.Get("TrayExit"), null, (s, e) => { form.isForceClosing = true; form.trayIcon.Visible = false; Application.Exit(); });
            
            UpdateTexts.Add(() => {
                itemStart.Text = "▶ " + LanguageManager.Get("BtnStart");
                itemStop.Text = "■ " + LanguageManager.Get("BtnStop");
                itemWeb.Text = LanguageManager.Get("BtnOpenWeb");
                itemConfig.Text = LanguageManager.Get("TrayConfig");
                itemExit.Text = LanguageManager.Get("TrayExit");
            });
            
            trayMenu.Opening += (s, e) => {
                itemStart.Visible = !form.isTransmitting;
                itemStop.Visible = form.isTransmitting;
                itemWeb.Visible = form.chkWebServer.Checked;
            };
            form.trayIcon.ContextMenuStrip = trayMenu;

            mainContainer.BringToFront();

            foreach (var a in UpdateTexts) a();
        }
    }

    public class TextBoxWriter : System.IO.TextWriter
    {
        private RichTextBox _rtb;
        public TextBoxWriter(RichTextBox rtb) { _rtb = rtb; }
        public override void Write(char value) { Write(value.ToString()); }
        public override void Write(string value)
        {
            if (_rtb.IsDisposed) return;
            try
            {
                if (_rtb.InvokeRequired) {
                    _rtb.BeginInvoke(new Action(() => Write(value)));
                } else {
                    _rtb.AppendText(value);
                    _rtb.SelectionStart = _rtb.Text.Length;
                    _rtb.ScrollToCaret();
                }
            }
            catch { }
        }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }

    public class DarkComboBox : ComboBox
    {
        public DarkComboBox()
        {
            this.DrawMode = DrawMode.OwnerDrawFixed;
            this.FlatStyle = FlatStyle.Flat;
            this.BackColor = UIManager.ThemeColors.BgPanel;
            this.ForeColor = UIManager.ThemeColors.TextMain;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush bg = new SolidBrush(isSelected ? Color.FromArgb(51, 122, 183) : this.BackColor))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }
            using (SolidBrush fg = new SolidBrush(this.ForeColor))
            {
                e.Graphics.DrawString(Items[e.Index].ToString(), e.Font, fg, e.Bounds);
            }
            e.DrawFocusRectangle();
        }

        private const int WM_PAINT = 0xF;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
            {
                using (Graphics g = Graphics.FromHwnd(this.Handle))
                {
                    using (Pen p = new Pen(UIManager.ThemeColors.Border, 1))
                    {
                        g.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
                    }
                    Rectangle rect = new Rectangle(this.Width - 18, 1, 17, this.Height - 2);
                    using (SolidBrush b = new SolidBrush(UIManager.ThemeColors.BgPanel))
                    {
                        g.FillRectangle(b, rect);
                    }
                    using (SolidBrush b = new SolidBrush(UIManager.ThemeColors.TextMuted))
                    {
                        Point[] pts = new Point[] {
                            new Point(this.Width - 13, this.Height / 2 - 2),
                            new Point(this.Width - 5, this.Height / 2 - 2),
                            new Point(this.Width - 9, this.Height / 2 + 3)
                        };
                        g.FillPolygon(b, pts);
                    }
                }
            }
        }
    }
}












