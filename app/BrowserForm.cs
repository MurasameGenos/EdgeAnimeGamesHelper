using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EdgeAnimeGamesHelper
{
    public sealed class BrowserForm : Form
    {
        private const string HomeUrl = "https://www.bilibili.com/";
        private readonly string userDataPath;
        private readonly string startUrl;
        private readonly bool testMode;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly ToolTip tooltips = new ToolTip();
        private readonly Panel titleBar;
        private readonly TableLayoutPanel toolbar;
        private readonly Panel statusBar;
        private readonly TextBox address;
        private readonly Label title;
        private readonly Label status;
        private readonly Button back;
        private readonly Button forward;
        private readonly Button focusButton;
        private readonly Button hotkeysButton;
        private readonly Panel browserHost;
        private readonly WebView2 browser;
        private readonly Timer stateTimer;
        private bool hotkeysEnabled = true;
        private bool hidden;
        private bool videoFocus;
        private bool immersive;
        private bool focusBeforeImmersion;
        private bool immersionTransition;
        private Padding paddingBeforeImmersion;
        private int navigationGeneration;
        private int immersionGeneration;
        private bool pageFullscreen;
        private bool polling;
        private bool ready;
        private bool chromeVisible = true;
        private string overlayError = "";
        private string pageError = "";
        private string bridgeSource;
        private double videoAspectRatio = 16.0 / 9.0;

        public event EventHandler ToggleVisibilityRequested;
        public event EventHandler ToggleHotkeysRequested;
        public event EventHandler ResetPositionRequested;
        public event EventHandler ExitRequested;
        public event EventHandler DragRequested;
        public event EventHandler LayoutModeChanged;
        public event EventHandler ImmersionChanged;

        public CoreWebView2 Core { get { return browser.CoreWebView2; } }
        public WebView2 Browser { get { return browser; } }
        public bool Ready { get { return ready; } }
        public bool VideoFocus { get { return videoFocus; } }
        public bool Immersive { get { return immersive; } }
        public double VideoAspectRatio { get { return videoAspectRatio; } }
        public Rectangle DragRegion { get { return chromeVisible ? titleBar.Bounds : Rectangle.Empty; } }
        public Padding ResizeInsets { get { return Padding; } }
        public Size ChromeInsets { get { return new Size(Padding.Horizontal, Padding.Vertical + (chromeVisible ? titleBar.Height + toolbar.Height + statusBar.Height : 0)); } }

        public BrowserForm(string userDataPath, string startUrl, bool testMode)
        {
            this.userDataPath = userDataPath;
            this.startUrl = IsWebUrl(startUrl) ? startUrl : HomeUrl;
            this.testMode = testMode;
            Text = "攻略小窗";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(48, 54, 64);
            ForeColor = Color.FromArgb(225, 229, 235);
            Font = new Font("Microsoft YaHei UI", 9F);
            Padding = new Padding(8);
            MinimumSize = new Size(360, 240);
            Size = new Size(800, 566);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            titleBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(25, 29, 36) };
            title = new Label { Text = "攻略小窗", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), AutoEllipsis = true };
            title.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Raise(DragRequested); };
            title.MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Raise(ResetPositionRequested); };
            titleBar.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Raise(DragRequested); };
            Button hideButton = MakeButton("—", "隐藏窗口（7）");
            hideButton.Dock = DockStyle.Right;
            hideButton.Width = 32;
            hideButton.Click += delegate { Raise(ToggleVisibilityRequested); };
            Button closeButton = MakeButton("×", "退出攻略小窗");
            closeButton.Dock = DockStyle.Right;
            closeButton.Width = 32;
            closeButton.Click += delegate { if (ExitRequested != null) Raise(ExitRequested); else Close(); };
            titleBar.Controls.Add(title);
            titleBar.Controls.Add(hideButton);
            titleBar.Controls.Add(closeButton);

            toolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(25, 29, 36), ColumnCount = 6, RowCount = 1, Padding = new Padding(3, 5, 3, 5), Margin = Padding.Empty };
            for (int i = 0; i < 4; i++) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 29));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            back = MakeButton("‹", "后退（Alt+左方向键）");
            forward = MakeButton("›", "前进（Alt+右方向键）");
            Button reload = MakeButton("↻", "刷新（F5）");
            Button home = MakeButton("⌂", "Bilibili 主页");
            back.Click += delegate { if (ready && Core.CanGoBack) Core.GoBack(); };
            forward.Click += delegate { if (ready && Core.CanGoForward) Core.GoForward(); };
            reload.Click += delegate { if (ready) Core.Reload(); };
            home.Click += delegate { Navigate(HomeUrl); };
            address = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(39, 45, 55), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9F), Margin = new Padding(4, 5, 4, 0), Text = this.startUrl };
            address.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { NavigateAddress(); e.SuppressKeyPress = true; } };
            address.Enter += delegate { address.SelectAll(); };
            focusButton = MakeButton("沉浸", "8 切换沉浸：只显示视频，隐藏工具栏和边框，并贴住屏幕底部");
            focusButton.Click += async delegate { await ToggleImmersionAsync(); };
            toolbar.Controls.Add(back, 0, 0);
            toolbar.Controls.Add(forward, 1, 0);
            toolbar.Controls.Add(reload, 2, 0);
            toolbar.Controls.Add(home, 3, 0);
            toolbar.Controls.Add(address, 4, 0);
            toolbar.Controls.Add(focusButton, 5, 0);

            statusBar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Color.FromArgb(25, 29, 36) };
            hotkeysButton = MakeButton("暂停热键", "输入包含 ~ / 5 / 6 / 7 / 8 的网址或文字时，可暂停所有全局热键；托盘也可恢复");
            hotkeysButton.Dock = DockStyle.Right;
            hotkeysButton.Width = 82;
            hotkeysButton.Click += delegate { Raise(ToggleHotkeysRequested); };
            status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(7, 0, 3, 0), AutoEllipsis = true, Font = new Font("Microsoft YaHei UI", 8F), ForeColor = Color.FromArgb(163, 174, 191) };
            statusBar.Controls.Add(status);
            statusBar.Controls.Add(hotkeysButton);

            browserHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
            browserHost.Controls.Add(browser);
            Controls.Add(browserHost);
            Controls.Add(statusBar);
            Controls.Add(toolbar);
            Controls.Add(titleBar);
            stateTimer = new Timer { Interval = 800 };
            stateTimer.Tick += async delegate { await PollVideoStateAsync(); };
            UpdateStatus();
        }

        public async Task InitializeBrowserAsync()
        {
            if (ready) return;
            try
            {
                Directory.CreateDirectory(userDataPath);
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EdgeAnimeGamesHelper.VideoBridge.js"))
                {
                    if (stream == null) throw new FileNotFoundException("找不到视频控制脚本。");
                    using (StreamReader reader = new StreamReader(stream)) bridgeSource = reader.ReadToEnd();
                }
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataPath);
                await browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed) return;
                Core.Settings.AreHostObjectsAllowed = false;
                Core.Settings.IsWebMessageEnabled = false;
                Core.Settings.IsStatusBarEnabled = false;
                Core.Settings.AreDevToolsEnabled = testMode;
                Core.Settings.AreDefaultContextMenusEnabled = true;
                Core.Settings.IsZoomControlEnabled = true;
                browser.KeyDown += OnBrowserKeyDown;
                Core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                Core.NavigationStarting += OnNavigationStarting;
                Core.NavigationCompleted += OnNavigationCompleted;
                Core.SourceChanged += delegate { if (!address.Focused) address.Text = Core.Source; };
                Core.HistoryChanged += delegate { back.Enabled = Core.CanGoBack; forward.Enabled = Core.CanGoForward; };
                Core.DocumentTitleChanged += delegate { title.Text = String.IsNullOrWhiteSpace(Core.DocumentTitle) ? "攻略小窗" : Core.DocumentTitle; Text = title.Text + " · 攻略小窗"; };
                Core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    if (IsWebUrl(e.Uri)) Navigate(e.Uri);
                    else SetPageError("已阻止打开外部应用链接，请在网页内继续操作。");
                };
                Core.ContainsFullScreenElementChanged += delegate { pageFullscreen = Core.ContainsFullScreenElement; UpdateChrome(); };
                Core.ProcessFailed += delegate { SetPageError("网页进程意外退出，请点击刷新重新载入。"); };
                await Core.AddScriptToExecuteOnDocumentCreatedAsync(bridgeSource);
                if (IsDisposed) return;
                ready = true;
                back.Enabled = false;
                forward.Enabled = false;
                // Test callers own their first document; an extra blank navigation can race NavigateToString.
                if (!testMode) Core.Navigate(startUrl);
                stateTimer.Start();
                UpdateStatus();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                if (!IsDisposed) ShowStartupError("缺少 Microsoft Edge WebView2 Runtime", "请从微软官网安装 Evergreen WebView2 Runtime，完成后重新打开攻略小窗。", true);
            }
            catch (Exception ex)
            {
                if (!IsDisposed) ShowStartupError("浏览器启动失败", ex.Message, false);
            }
        }

        public async Task<string> ExecuteVideoCommandAsync(string command)
        {
            if (!ready || IsDisposed) return json.Serialize(new { ok = false, error = "not_ready", message = "浏览器尚未启动完成。" });
            if (hidden) return json.Serialize(new { ok = false, error = "window_hidden", message = "窗口已隐藏，按 7 恢复后再操作。" });
            int generation = navigationGeneration;
            int request = immersionGeneration;
            try
            {
                string result = await Core.ExecuteScriptAsync("window.__eaghBridge ? window.__eaghBridge.execute(" + json.Serialize(command) + ") : ({ok:false,error:'bridge_unavailable',message:'网页正在载入，请稍后再试。'})");
                if (IsDisposed || generation != navigationGeneration || request != immersionGeneration) return result;
                ProcessBridgeResult(result, true);
                return result;
            }
            catch (Exception)
            {
                if (!IsDisposed && generation == navigationGeneration && request == immersionGeneration) SetPageError("网页正在切换或已关闭，请稍后再试。");
                return json.Serialize(new { ok = false, error = "navigation_in_progress", message = pageError });
            }
        }

        public async Task ToggleVideoFocusAsync()
        {
            if (!ready || IsDisposed || hidden) return;
            int generation = navigationGeneration;
            try
            {
                string result = await Core.ExecuteScriptAsync("window.__eaghBridge ? window.__eaghBridge.setFocus(" + (videoFocus ? "false" : "true") + ") : ({ok:false,message:'网页正在载入，请稍后再试。'})");
                if (IsDisposed || generation != navigationGeneration) return;
                ProcessBridgeResult(result, true);
                UpdateChrome();
            }
            catch (Exception) { if (!IsDisposed && generation == navigationGeneration) SetPageError("专注视图切换失败，请等网页载入后重试。"); }
        }

        public async Task ToggleImmersionAsync()
        {
            if (!ready || IsDisposed || hidden || immersionTransition) return;
            if (immersive) { await ExitImmersionAsync(); return; }
            if (!hotkeysEnabled) { SetPageError("请先恢复热键，再按 8 进入沉浸模式。"); return; }
            immersionTransition = true;
            int generation = navigationGeneration;
            int request = ++immersionGeneration;
            bool previousFocus = videoFocus;
            try
            {
                string result = await Core.ExecuteScriptAsync("window.__eaghBridge ? window.__eaghBridge.setFocus(true) : ({ok:false,message:'网页正在载入，请稍后再试。'})");
                // A result belongs to the document where the toggle started, never its replacement.
                if (IsDisposed || generation != navigationGeneration || request != immersionGeneration) return;
                Dictionary<string, object> state = json.DeserializeObject(result) as Dictionary<string, object>;
                object value;
                bool success = state != null && state.TryGetValue("ok", out value) && value is bool && (bool)value;
                bool hasVideo = state != null && state.TryGetValue("hasVideo", out value) && value is bool && (bool)value;
                if (!success || !hasVideo)
                {
                    ProcessBridgeResult(result, true);
                    if (success) SetPageError("当前页面没有可用的视频，请先打开并播放一个视频。");
                    return;
                }
                focusBeforeImmersion = previousFocus;
                pageError = "";
                SetImmersive(true);
                // Layout/state notifications may synchronously recover from a hotkey conflict.
                if (IsDisposed || generation != navigationGeneration || request != immersionGeneration || !immersive) return;
                ProcessBridgeResult(result, true);
                UpdateStatus();
            }
            catch (Exception)
            {
                if (!IsDisposed && generation == navigationGeneration && request == immersionGeneration) SetPageError("沉浸模式切换失败，请等网页载入后重试。");
            }
            finally { immersionTransition = false; }
        }

        public async Task ExitImmersionAsync()
        {
            if (IsDisposed || (!immersive && !immersionTransition)) return;
            bool restoreFocus = immersive && focusBeforeImmersion;
            int generation = navigationGeneration;
            int request = ++immersionGeneration;
            videoFocus = restoreFocus;
            // Recovery controls must remain available even if the WebView rejects the script.
            SetImmersive(false);
            UpdateStatus();
            if (!ready) return;
            try
            {
                string result = await Core.ExecuteScriptAsync("window.__eaghBridge ? window.__eaghBridge.setFocus(" + (restoreFocus ? "true" : "false") + ") : null");
                if (IsDisposed || generation != navigationGeneration || request != immersionGeneration) return;
                ProcessBridgeResult(result, true);
            }
            catch (Exception)
            {
                if (!IsDisposed && generation == navigationGeneration && request == immersionGeneration) SetPageError("已退出沉浸模式；网页正在切换，请稍后重试。");
            }
        }

        private void SetImmersive(bool enabled)
        {
            if (immersive == enabled) return;
            SuspendLayout();
            if (enabled)
            {
                paddingBeforeImmersion = Padding;
                immersive = true;
                Padding = Padding.Empty;
            }
            else
            {
                immersive = false;
                Padding = paddingBeforeImmersion;
            }
            // Geometry notifications must observe the final client layout. In particular,
            // snapping before ResumeLayout can be undone by the deferred dock layout.
            UpdateChrome(false);
            ResumeLayout(true);
            Raise(LayoutModeChanged);
            Raise(ImmersionChanged);
        }

        private void LeaveImmersionForRecovery()
        {
            if (!immersive && !immersionTransition) return;
            ++immersionGeneration;
            // Recover a usable browser immediately even when the document has lost its player.
            videoFocus = false;
            pageFullscreen = false;
            SetImmersive(false);
            RestorePageAfterImmersionAsync(navigationGeneration);
        }

        private async void RestorePageAfterImmersionAsync(int generation)
        {
            try
            {
                if (!ready || IsDisposed || generation != navigationGeneration) return;
                await Core.ExecuteScriptAsync("if (window.__eaghBridge) window.__eaghBridge.setFocus(false); if (document.fullscreenElement) document.exitFullscreen().catch(function () {});");
            }
            catch (Exception) { /* Recovery may race a navigation or a browser process failure. */ }
        }

        public void SetOverlayUi(bool hidden, bool hotkeysEnabled, string error)
        {
            if (IsDisposed) return;
            this.hidden = hidden;
            this.hotkeysEnabled = hotkeysEnabled;
            overlayError = error ?? "";
            if (!String.IsNullOrEmpty(overlayError) || !hotkeysEnabled) LeaveImmersionForRecovery();
            hotkeysButton.Text = hotkeysEnabled ? "暂停热键" : "恢复热键";
            UpdateChrome();
            UpdateStatus();
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            bool testFixture = testMode && e.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);
            if (!IsWebUrl(e.Uri) && e.Uri != "about:blank" && !testFixture)
            {
                e.Cancel = true;
                SetPageError("只支持打开 HTTP / HTTPS 网页。");
                return;
            }
            ++navigationGeneration;
            ++immersionGeneration;
            pageError = "";
            videoFocus = false;
            pageFullscreen = false;
            SetImmersive(false);
            UpdateChrome();
            status.Text = "正在载入网页…";
        }

        private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                SetPageError("网页加载失败（" + e.WebErrorStatus.ToString() + "），请检查网络后刷新。");
                return;
            }
            try { await Core.ExecuteScriptAsync(bridgeSource); }
            catch (Exception) { SetPageError("视频控制尚未就绪，请刷新重试。"); }
            UpdateStatus();
        }

        private async Task PollVideoStateAsync()
        {
            if (!ready || polling || IsDisposed || hidden) return;
            polling = true;
            int generation = navigationGeneration;
            int request = immersionGeneration;
            try
            {
                string result = await Core.ExecuteScriptAsync("window.__eaghBridge ? window.__eaghBridge.state() : null");
                if (IsDisposed || generation != navigationGeneration || request != immersionGeneration) return;
                ProcessBridgeResult(result, false);
            }
            catch (Exception) { /* Navigating documents may reject an in-flight state read. */ }
            finally { polling = false; }
        }

        private void ProcessBridgeResult(string result, bool clearPreviousError)
        {
            Dictionary<string, object> state = json.DeserializeObject(result) as Dictionary<string, object>;
            if (state == null) return;
            object value;
            if (state.TryGetValue("ok", out value) && value is bool && !(bool)value)
            {
                SetPageError(state.ContainsKey("message") ? Convert.ToString(state["message"]) : "视频操作失败。");
                return;
            }
            if (immersive && state.TryGetValue("hasVideo", out value) && value is bool && !(bool)value)
            {
                SetPageError("当前页面已没有可用的视频，已退出沉浸模式。");
                return;
            }
            if (clearPreviousError) pageError = "";
            if (state.TryGetValue("aspectRatio", out value))
            {
                double aspect = Convert.ToDouble(value);
                if (!Double.IsNaN(aspect) && !Double.IsInfinity(aspect) && aspect > 0.1 && aspect < 10 && Math.Abs(videoAspectRatio - aspect) > 0.001)
                {
                    videoAspectRatio = aspect;
                    Raise(LayoutModeChanged);
                }
            }
            if (state.TryGetValue("focused", out value) && value is bool && videoFocus != (bool)value)
            {
                videoFocus = (bool)value;
                if (immersive && !videoFocus) SetImmersive(false);
                UpdateChrome();
            }
            if (state.TryGetValue("lastError", out value) && value is Dictionary<string, object>)
            {
                Dictionary<string, object> problem = (Dictionary<string, object>)value;
                if (problem.ContainsKey("message")) SetPageError(Convert.ToString(problem["message"]));
            }
            UpdateStatus();
        }

        private void UpdateChrome(bool notifyLayout = true)
        {
            bool show = !String.IsNullOrEmpty(overlayError) || !String.IsNullOrEmpty(pageError) || (!immersive && !pageFullscreen);
            bool changed = chromeVisible != show;
            chromeVisible = show;
            titleBar.Visible = show;
            toolbar.Visible = show;
            statusBar.Visible = show;
            focusButton.Text = immersive ? "退出沉浸" : "沉浸";
            if (changed && notifyLayout) Raise(LayoutModeChanged);
        }

        private void UpdateStatus()
        {
            UpdateChrome();
            string error = !String.IsNullOrEmpty(overlayError) ? overlayError : pageError;
            status.ForeColor = String.IsNullOrEmpty(error) ? Color.FromArgb(163, 174, 191) : Color.FromArgb(255, 190, 132);
            status.Text = !String.IsNullOrEmpty(error) ? error :
                !hotkeysEnabled ? "热键已暂停，可正常输入所有按键" :
                "~ 暂停 · 5/6 进退 · 7 隐藏 · 8 沉浸 · 输入前暂停热键";
        }

        private void SetPageError(string message)
        {
            pageError = message ?? "";
            if (!String.IsNullOrEmpty(pageError)) LeaveImmersionForRecovery();
            UpdateStatus();
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            // Padding is zero while immersed, so WinForms cannot scale the saved browser frame for us.
            if (immersive)
            {
                paddingBeforeImmersion = new Padding(
                    (int)Math.Round(paddingBeforeImmersion.Left * factor.Width),
                    (int)Math.Round(paddingBeforeImmersion.Top * factor.Height),
                    (int)Math.Round(paddingBeforeImmersion.Right * factor.Width),
                    (int)Math.Round(paddingBeforeImmersion.Bottom * factor.Height));
            }
            base.ScaleControl(factor, specified);
        }

        private void NavigateAddress()
        {
            string value = address.Text.Trim();
            if (String.IsNullOrEmpty(value)) return;
            if (value.IndexOf("://", StringComparison.Ordinal) < 0) value = "https://" + value;
            if (!IsWebUrl(value)) { SetPageError("请输入有效的 HTTP / HTTPS 网址。"); return; }
            Navigate(value);
        }

        private void Navigate(string uri)
        {
            if (!ready || !IsWebUrl(uri)) return;
            try { Core.Navigate(uri); browser.Focus(); }
            catch (Exception) { SetPageError("无法打开这个网址，请检查后重试。"); }
        }

        private static bool IsWebUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private Button MakeButton(string text, string tooltip)
        {
            Button button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(25, 29, 36), ForeColor = ForeColor, Margin = new Padding(1), Padding = Padding.Empty, TabStop = false };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 62, 76);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(63, 78, 93);
            tooltips.SetToolTip(button, tooltip);
            return button;
        }

        private void ShowStartupError(string heading, string detail, bool runtimeMissing)
        {
            ready = false;
            browser.Visible = false;
            Label description = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = ForeColor, BackColor = Color.FromArgb(20, 24, 30), Padding = new Padding(24), Text = heading + Environment.NewLine + Environment.NewLine + detail, TextAlign = ContentAlignment.MiddleCenter };
            browserHost.Controls.Add(description);
            description.BringToFront();
            if (runtimeMissing)
            {
                LinkLabel link = new LinkLabel { Text = "打开微软 WebView2 Runtime 下载页面", Dock = DockStyle.Bottom, Height = 48, TextAlign = ContentAlignment.MiddleCenter, LinkColor = Color.FromArgb(111, 204, 191) };
                link.LinkClicked += delegate
                {
                    try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }); }
                    catch (Exception) { SetPageError("请访问 developer.microsoft.com/microsoft-edge/webview2/ 安装运行时。"); }
                };
                browserHost.Controls.Add(link);
                link.BringToFront();
            }
            SetPageError(heading);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleNavigationKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnBrowserKeyDown(object sender, KeyEventArgs e)
        {
            // WinForms WebView2 maps its private controller's AcceleratorKeyPressed to KeyDown.
            Keys keyData = e.KeyData;
            if (keyData != (Keys.Control | Keys.L) && keyData != Keys.F5 && keyData != (Keys.Alt | Keys.Left) && keyData != (Keys.Alt | Keys.Right)) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            // This event is synchronous: defer WebView calls until its callback has returned.
            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke(new Action(delegate { if (!IsDisposed) HandleNavigationKey(keyData); })); }
                catch (InvalidOperationException) { /* The form closed between the handle check and scheduling. */ }
            }
        }

        private bool HandleNavigationKey(Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.L))
            {
                LeaveImmersionForRecovery();
                if (pageFullscreen && ready)
                {
                    // Leave HTML fullscreen inside this window so the address bar is available.
                    Core.ExecuteScriptAsync("if (document.fullscreenElement) document.exitFullscreen().catch(function () {});");
                    pageFullscreen = false;
                    UpdateChrome();
                }
                address.Focus();
                address.SelectAll();
                return true;
            }
            if (!ready) return false;
            if (keyData == Keys.F5) { Core.Reload(); return true; }
            if (keyData == (Keys.Alt | Keys.Left)) { if (Core.CanGoBack) Core.GoBack(); return true; }
            if (keyData == (Keys.Alt | Keys.Right)) { if (Core.CanGoForward) Core.GoForward(); return true; }
            return false;
        }

        private void Raise(EventHandler handler) { if (handler != null) handler(this, EventArgs.Empty); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { stateTimer.Stop(); stateTimer.Dispose(); tooltips.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
