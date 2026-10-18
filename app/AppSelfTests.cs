using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace EdgeAnimeGamesHelper
{
    internal static class AppSelfTests
    {
        private static readonly List<object> results = new List<object>();
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            results.Add(new { name = name, ok = true });
        }

        internal static int Run()
        {
            int code = 1;
            string profile = Path.Combine(Path.GetTempPath(), "EdgeAnimeGamesHelper.Tests", Guid.NewGuid().ToString("N"));
            BrowserForm form = new BrowserForm(profile, "https://www.bilibili.com/", true);
            AppController controller = null;
            OverlayController overlay = null;
            Form underlying = null;
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(-20000, -20000, 816, 566);
            form.Shown += async delegate
            {
                try
                {
                    results.AddRange(GameInputFallbackTests.Run());
                    Check(OverlayGeometry.Snap(new Rectangle(-1270, 12, 800, 500), new Rectangle(-1280, 0, 1280, 1040), 24).Location == new Point(-1280, 0), "negative-monitor edge snapping");
                    Rectangle kept = OverlayGeometry.KeepAspect(new Rectangle(20, 30, 976, 600), new Rectangle(20, 30, 816, 566), 2, 16.0 / 9, 16, 116, new Size(360, 240));
                    Check(kept.Width == 976 && kept.Height == 656, "resize keeps video ratio after browser chrome insets");
                    Rectangle display = new Rectangle(0, 0, 1920, 1080);
                    Check(OverlayGeometry.SnapBottom(new Rectangle(60, 60, 800, 450), display) == new Rectangle(60, 630, 800, 450), "immersion attaches to the physical bottom even when the top or left edge is nearer");
                    Check(OverlayGeometry.SnapBottom(new Rectangle(1700, 100, 800, 450), display) == new Rectangle(1120, 630, 800, 450), "bottom attachment clamps horizontal overflow and preserves window size");
                    Check(OverlayGeometry.SnapBottom(new Rectangle(-1700, -900, 640, 360), new Rectangle(-1920, -1080, 1920, 1080)) == new Rectangle(-1700, -360, 640, 360), "bottom attachment supports a monitor with negative horizontal and vertical coordinates");
                    Check(OverlayGeometry.Snap(new Rectangle(200, 615, 800, 450), display, 24).Bottom == display.Bottom, "manual snapping includes the physical bottom within its threshold");
                    Check(OverlayGeometry.Snap(new Rectangle(-1700, -375, 640, 360), new Rectangle(-1920, -1080, 1920, 1080), 24).Bottom == 0, "manual bottom snapping supports negative-coordinate monitors");
                    Check(OverlayGeometry.Snap(new Rectangle(200, 600, 800, 450), display, 24).Bottom == 1050, "manual bottom snapping leaves windows outside the threshold in place");
                    await form.InitializeBrowserAsync();
                    Check(form.Ready, "WebView2 Runtime initialization with isolated test profile");
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("location.href") == "\"about:blank\""; });
                    Check(await form.Core.ExecuteScriptAsync("location.href") == "\"about:blank\"", "test mode never opens live Bilibili");
                    Check(!form.Core.Settings.AreHostObjectsAllowed && !form.Core.Settings.IsWebMessageEnabled, "web page cannot call native host objects or messages");
                    List<string> navigationTrace = new List<string>();
                    form.Core.NavigationStarting += delegate(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e) { navigationTrace.Add("start:" + e.Uri.Substring(0, Math.Min(80, e.Uri.Length)) + ",cancel:" + e.Cancel); };
                    form.Core.NavigationCompleted += delegate(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) { navigationTrace.Add("complete:" + e.IsSuccess + ",error:" + e.WebErrorStatus); };
                    Rectangle initialWork = Screen.PrimaryScreen.WorkingArea;
                    form.Location = new Point(initialWork.Left + 60, initialWork.Top + 60);
                    string video;
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EdgeAnimeGamesHelper.TestVideo.webm"))
                    using (MemoryStream bytes = new MemoryStream())
                    { stream.CopyTo(bytes); video = Convert.ToBase64String(bytes.ToArray()); }
                    string fixture = "<!doctype html><html><head><meta charset='utf-8'><title>攻略小窗 · 自测</title><style>body{margin:0;background:#131a22;color:#e8edf3;font:16px system-ui}header{padding:12px}video{width:640px;height:360px}</style></head><body><header id='page-chrome'>WebView2 isolated test video</header><div class='bpx-player-video-wrap'><video id='guide' muted preload='auto' src='data:video/webm;base64," + video + "'></video></div><input id='ordinary-input' placeholder='normal browser input'></body></html>";
                    form.Core.NavigateToString(fixture);
                    bool videoTimedOut = false;
                    try { await Until(async delegate { return await form.Core.ExecuteScriptAsync("!!(window.__eaghBridge && document.querySelector('video') && document.querySelector('video').readyState >= 2)") == "true"; }); }
                    catch (TimeoutException) { videoTimedOut = true; }
                    if (videoTimedOut)
                    {
                        string diagnostic = await form.Core.ExecuteScriptAsync("({url:location.href,documentReady:document.readyState,bridge:!!window.__eaghBridge,video:!!document.querySelector('video'),ready:document.querySelector('video')?.readyState,error:document.querySelector('video')?.error?.message,body:document.body?.innerText?.slice(0,180)})");
                        throw new InvalidOperationException("Video fixture did not load: " + diagnostic + "; navigation=" + String.Join(" | ", navigationTrace.ToArray()) + "; control=" + form.Browser.Visible + ",size=" + form.Browser.Size);
                    }
                    Check(true, "video bridge loads on an actual WebView2 document");
                    await form.Core.ExecuteScriptAsync("document.querySelector('video').currentTime=12");
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("!document.querySelector('video').seeking") == "true"; });
                    Dictionary<string, object> result = json.DeserializeObject(await form.ExecuteVideoCommandAsync("seekBackward")) as Dictionary<string, object>;
                    Check(result != null && Convert.ToBoolean(result["ok"]) && Math.Abs(Convert.ToDouble(result["currentTime"]) - 7) < 0.2, "real video seeks backward five seconds");
                    result = json.DeserializeObject(await form.ExecuteVideoCommandAsync("seekForward")) as Dictionary<string, object>;
                    Check(result != null && Convert.ToBoolean(result["ok"]) && Math.Abs(Convert.ToDouble(result["currentTime"]) - 12) < 0.2, "real video seeks forward five seconds");
                    await form.ExecuteVideoCommandAsync("togglePause");
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("!document.querySelector('video').paused") == "true"; });
                    await form.ExecuteVideoCommandAsync("togglePause");
                    Check(await form.Core.ExecuteScriptAsync("document.querySelector('video').paused") == "true", "real video pause and resume");
                    await form.ToggleVideoFocusAsync();
                    Check(form.VideoFocus && await form.Core.ExecuteScriptAsync("getComputedStyle(document.querySelector('video')).objectFit") == "\"contain\"", "focus view uses original video with preserved aspect ratio");
                    await form.ToggleVideoFocusAsync();
                    Check(!form.VideoFocus && await form.Core.ExecuteScriptAsync("document.querySelector('[data-eagh-style]') === null") == "true", "leaving focus restores the full browser page");
                    string artifactDirectory = Environment.GetEnvironmentVariable("EAGH_TEST_ARTIFACTS");
                    if (!String.IsNullOrEmpty(artifactDirectory))
                    {
                        Directory.CreateDirectory(artifactDirectory);
                        using (MemoryStream preview = new MemoryStream())
                        {
                            await form.Core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, preview);
                            preview.Position = 0;
                            using (Image page = Image.FromStream(preview))
                            using (Bitmap screenshot = new Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                                Point content = form.PointToClient(form.Browser.PointToScreen(Point.Empty));
                                using (Graphics graphics = Graphics.FromImage(screenshot)) graphics.DrawImage(page, new Rectangle(content, form.Browser.Size));
                                screenshot.Save(Path.Combine(artifactDirectory, "app-ui.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                    }

                    // Use exactly the production binding, including the asynchronous WebView
                    // command callback. A fabricated WM_HOTKEY cannot cover registration bugs.
                    controller = new AppController(form);
                    overlay = controller.Overlay;
                    controller.RefreshLayout();
                    Check(overlay.HotkeysEnabled && overlay.Error.Length == 0, "normal browser startup registers every video and recovery hotkey");
                    Rectangle work = Screen.PrimaryScreen.WorkingArea;
                    form.Bounds = new Rectangle(work.Left + 60, work.Top + 60, 816, 566);
                    underlying = new Form { Text = "Guide browser isolated keyboard test target", StartPosition = FormStartPosition.Manual, Bounds = form.Bounds };
                    TextBox keyboardTarget = new TextBox { Dock = DockStyle.Fill, Multiline = true, Text = "Only this self-test window receives injected keyboard input.\r\n" };
                    underlying.Controls.Add(keyboardTarget);
                    underlying.Show();
                    NativeTest.SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                    await Task.Delay(100);
                    Point probe = new Point(form.Left + 220, form.Top + 190);
                    Check(NativeTest.GetAncestor(NativeTest.WindowFromPoint(probe), 2) == form.Handle, "normal WebView2 surface remains mouse interactive");
                    long style = NativeTest.GetWindowLongPtr(form.Handle, -20).ToInt64();
                    Check((style & 0x08000020L) == 0, "normal window has neither transparent nor noactivate mouse styles");
                    AssertKeyAvailable(underlying, 0x39, "9 remains unregistered while the application is active");

                    await form.Core.ExecuteScriptAsync("document.querySelector('video').pause();document.querySelector('video').currentTime=12");
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("!document.querySelector('video').seeking && document.querySelector('video').paused") == "true"; });
                    await SendKeyAsync(underlying, 0xC0);
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("!document.querySelector('video').paused") == "true"; }, "physical ~ hotkey did not resume the video in normal browser mode");
                    Check(!form.Immersive, "physical ~ resumes video from another foreground window without changing browser mode");
                    await SendKeyAsync(underlying, 0xC0);
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("document.querySelector('video').paused") == "true"; }, "physical ~ hotkey did not pause the video");
                    Check(true, "physical ~ pauses the real WebView2 video from another foreground window");
                    await form.Core.ExecuteScriptAsync("document.querySelector('video').currentTime=12");
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("!document.querySelector('video').seeking") == "true"; });
                    await SendKeyAsync(underlying, 0x35);
                    await Until(async delegate { return Math.Abs(await VideoTime(form) - 7) < 0.2; }, "physical 5 hotkey did not seek backward in normal browser mode");
                    Check(true, "physical 5 seeks the real video backward from another foreground window");
                    await SendKeyAsync(underlying, 0x36);
                    await Until(async delegate { return Math.Abs(await VideoTime(form) - 12) < 0.2; }, "physical 6 hotkey did not seek forward in normal browser mode");
                    Check(true, "physical 6 seeks the real video forward from another foreground window");

                    await SendKeyAsync(underlying, 0x37);
                    await Until(delegate { return Task.FromResult(overlay.Hidden && !NativeTest.IsWindowVisible(form.Handle)); }, "physical 7 hotkey did not hide the window");
                    Check(true, "physical 7 hides the entire real WebView2 HWND");
                    Check(NativeTest.GetAncestor(NativeTest.WindowFromPoint(probe), 2) == underlying.Handle, "hidden browser leaves no input obstruction");
                    AssertKeyAvailable(underlying, 0xC0, "hidden window releases ~ to the foreground application");
                    AssertKeyAvailable(underlying, 0x35, "hidden window releases 5 to the foreground application");
                    AssertKeyAvailable(underlying, 0x36, "hidden window releases 6 to the foreground application");
                    AssertKeyAvailable(underlying, 0x38, "hidden window releases 8 to the foreground application");
                    await SendKeyAsync(underlying, 0x35);
                    await SendKeyAsync(underlying, 0x36);
                    await SendKeyAsync(underlying, 0xC0);
                    await SendKeyAsync(underlying, 0x38);
                    await Task.Delay(150);
                    Check(overlay.Hidden && !form.Immersive && await form.Core.ExecuteScriptAsync("document.querySelector('video').paused") == "true" && Math.Abs(await VideoTime(form) - 12) < 0.2, "hidden window ignores real ~ / 5 / 6 / 8 input and preserves video state");
                    Check(keyboardTarget.Text.Contains("56"), "released hidden-mode number keys reach the isolated foreground text box");
                    await SendKeyAsync(underlying, 0x37);
                    await Until(delegate { return Task.FromResult(!overlay.Hidden && NativeTest.IsWindowVisible(form.Handle)); }, "physical 7 hotkey did not restore the window");
                    Check(true, "physical 7 restores the window and its video controls");
                    await SendKeyAsync(underlying, 0x35);
                    await Until(async delegate { return Math.Abs(await VideoTime(form) - 7) < 0.2; }, "video hotkeys were not restored after unhiding");
                    Check(true, "restoring the window re-registers video hotkeys");
                    await SendKeyAsync(underlying, 0x36);
                    await Until(async delegate { return Math.Abs(await VideoTime(form) - 12) < 0.2; });

                    Rectangle beforeImmersion = NativeTest.WindowBounds(form.Handle);
                    await SendKeyAsync(underlying, 0x38);
                    await Until(delegate { return Task.FromResult(form.Immersive && overlay.Immersive && form.VideoFocus); }, "physical 8 hotkey did not enter immersion");
                    Check(true, "physical 8 enters immersion through the production binding from another foreground window");
                    await Until(delegate { return Task.FromResult(form.Padding == Padding.Empty && form.ChromeInsets == Size.Empty && form.Browser.Bounds == form.ClientRectangle && NativeWindowTouchesScreenBottom(form)); }, "immersive native layout did not finish and attach to the physical screen bottom");
                    Check(form.Padding == Padding.Empty && form.ChromeInsets == Size.Empty && form.Browser.Bounds == form.ClientRectangle, "immersive video fills the entire window without native chrome or padding");
                    Check(NativeWindowTouchesScreenBottom(form), "GetWindowRect confirms immersion touches the physical screen bottom");
                    Check(Math.Abs((double)form.Width / form.Height - form.VideoAspectRatio) < 0.01, "borderless immersion resizes the window to the video ratio");
                    await Task.Delay(1000);
                    Check(form.Immersive && NativeWindowTouchesScreenBottom(form), "native bottom attachment survives WebView layout and the 800 ms state poll");
                    overlay.SetAspectRatio(9.0 / 16);
                    Check(NativeWindowTouchesScreenBottom(form) && Math.Abs((double)form.Width / form.Height - 9.0 / 16) < 0.01, "portrait video aspect refit keeps the real HWND attached to the physical bottom");
                    overlay.SetAspectRatio(form.VideoAspectRatio);
                    Check(NativeWindowTouchesScreenBottom(form) && Math.Abs((double)form.Width / form.Height - form.VideoAspectRatio) < 0.01, "restoring the video aspect keeps the real HWND attached to the physical bottom");
                    Check((NativeTest.GetWindowLongPtr(form.Handle, -20).ToInt64() & 0x08000020L) == 0, "immersive mode keeps the video mouse interactive");
                    Point immersiveProbe = new Point(form.Left + form.Width / 2, form.Top + form.Height / 2);
                    Check(NativeTest.GetAncestor(NativeTest.WindowFromPoint(immersiveProbe), 2) == form.Handle, "immersive video HWND still receives mouse hit tests");
                    if (!String.IsNullOrEmpty(artifactDirectory))
                    {
                        using (FileStream immersivePreview = File.Create(Path.Combine(artifactDirectory, "app-immersive.png")))
                            await form.Core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, immersivePreview);
                    }
                    await SendKeyAsync(underlying, 0x37);
                    await Until(delegate { return Task.FromResult(overlay.Hidden); });
                    AssertKeyAvailable(underlying, 0x38, "hidden immersive window also releases 8");
                    await SendKeyAsync(underlying, 0x37);
                    await Until(delegate { return Task.FromResult(!overlay.Hidden); });
                    Check(form.Immersive && form.Padding == Padding.Empty && NativeWindowTouchesScreenBottom(form), "7 hide and restore preserves borderless immersion and physical bottom attachment");
                    await SendKeyAsync(underlying, 0x38);
                    await Until(delegate { return Task.FromResult(!form.Immersive && !form.VideoFocus && NativeTest.WindowBounds(form.Handle) == beforeImmersion); }, "physical 8 did not restore the previous normal browser geometry");
                    Check(form.Padding != Padding.Empty && form.ChromeInsets != Size.Empty, "physical 8 exits immersion and restores the complete normal browser interface and geometry");
                    await form.ToggleVideoFocusAsync();
                    await form.ToggleImmersionAsync();
                    await form.ExitImmersionAsync();
                    Check(form.VideoFocus && !form.Immersive, "exiting immersion restores a pre-existing focus view");
                    await form.ToggleVideoFocusAsync();
                    await form.ToggleImmersionAsync();
                    overlay.SetHotkeys(false);
                    Check(!form.Immersive && form.Padding != Padding.Empty && !overlay.HotkeysEnabled, "pausing all hotkeys immediately exits immersion and restores recovery controls");
                    AssertKeyAvailable(underlying, 0xC0, "pausing hotkeys releases ~ for ordinary browser input");
                    AssertKeyAvailable(underlying, 0x35, "pausing hotkeys releases 5 for ordinary browser input");
                    AssertKeyAvailable(underlying, 0x36, "pausing hotkeys releases 6 for ordinary browser input");
                    AssertKeyAvailable(underlying, 0x38, "pausing hotkeys releases 8 for ordinary browser input");
                    using (Form conflictingKeys = new Form())
                    {
                        Check(NativeTest.RegisterHotKey(conflictingKeys.Handle, 0x6113, 0x4000, 0x37), "isolated conflict can reserve 7 while app hotkeys are paused");
                        overlay.SetHotkeys(true);
                        await form.ToggleImmersionAsync();
                        Check(!form.Immersive && !form.VideoFocus && !String.IsNullOrEmpty(overlay.Error), "synchronous hotkey conflict recovery cannot restore a stale immersive focus result");
                        NativeTest.UnregisterHotKey(conflictingKeys.Handle, 0x6113);
                    }
                    overlay.SetHotkeys(true);
                    await form.ToggleImmersionAsync();
                    form.Core.NavigateToString("<!doctype html><title>Immersion navigation test</title><body id='no-video'>No video on this page</body>");
                    await Until(async delegate { return await form.Core.ExecuteScriptAsync("!!document.querySelector('#no-video')") == "true"; });
                    Check(!form.Immersive && form.Padding != Padding.Empty, "navigation leaves immersion and restores normal browser controls");
                    Rectangle noVideoBounds = form.Bounds;
                    await form.ToggleImmersionAsync();
                    Check(!form.Immersive && form.Bounds == noVideoBounds && form.Padding != Padding.Empty, "8 on a page without video does not hide or reposition the browser");
                    controller.Dispose();
                    controller = null;
                    Check(NativeTest.RegisterHotKey(form.Handle, 0x6111, 0x4000, 0x38), "closing controller releases global 8");
                    NativeTest.UnregisterHotKey(form.Handle, 0x6111);
                    Check(NativeTest.RegisterHotKey(form.Handle, 0x6112, 0x4000, 0x39), "9 remains available after controller disposal");
                    NativeTest.UnregisterHotKey(form.Handle, 0x6112);
                    code = 0;
                }
                catch (Exception error) { results.Add(new { name = "self-test failure", ok = false, error = error.ToString() }); }
                finally
                {
                    if (controller != null) controller.Dispose();
                    if (underlying != null) { underlying.Close(); underlying.Dispose(); }
                    form.Close();
                }
            };
            Application.Run(form);
            if (!form.IsDisposed) form.Dispose();
            using (StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                output.WriteLine(json.Serialize(new { ok = code == 0, tests = results.ToArray(), isolatedProfile = profile }));
            return code;
        }

        private static async Task<double> VideoTime(BrowserForm form)
        {
            return Convert.ToDouble(json.DeserializeObject(await form.Core.ExecuteScriptAsync("document.querySelector('video').currentTime")));
        }

        private static bool NativeWindowTouchesScreenBottom(BrowserForm form)
        {
            Rectangle bounds = NativeTest.WindowBounds(form.Handle);
            Rectangle display = Screen.FromRectangle(bounds).Bounds;
            return bounds.Bottom == display.Bottom;
        }

        private static void AssertKeyAvailable(Form target, uint key, string name)
        {
            const int reservation = 0x6120;
            bool registered = NativeTest.RegisterHotKey(target.Handle, reservation, 0x4000, key);
            try { Check(registered, name); }
            finally { if (registered) NativeTest.UnregisterHotKey(target.Handle, reservation); }
        }

        private static async Task SendKeyAsync(Form target, ushort key)
        {
            if (target == null || target.IsDisposed || !target.IsHandleCreated)
                throw new InvalidOperationException("The isolated self-test foreground window is unavailable; no keyboard input was injected.");
            uint ownProcess = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            uint targetProcess;
            NativeTest.GetWindowThreadProcessId(target.Handle, out targetProcess);
            if (targetProcess != ownProcess)
                throw new InvalidOperationException("The isolated keyboard target is not owned by this test process; no keyboard input was injected.");
            // Foreground activation is restricted to a window created by this test. Never
            // inject a key if Windows left a user application in the foreground.
            // A WebView process or the launching shell can briefly own foreground during
            // startup. Retry a bounded number of ordinary activation requests, without
            // changing Windows foreground-lock rules or attaching to another input queue.
            bool wasTopMost = target.TopMost;
            List<string> activationTrace = new List<string>();
            try
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    target.Show();
                    if (attempt >= 1) target.TopMost = true;
                    target.BringToFront();
                    target.Activate();
                    if (target.ActiveControl != null) target.ActiveControl.Focus();
                    bool activationAccepted = NativeTest.SetForegroundWindow(target.Handle);
                    await Task.Delay(120);
                    IntPtr observedForeground = NativeTest.GetForegroundWindow();
                    uint observedProcess = 0;
                    if (observedForeground != IntPtr.Zero) NativeTest.GetWindowThreadProcessId(observedForeground, out observedProcess);
                    activationTrace.Add("attempt=" + (attempt + 1) + ",accepted=" + activationAccepted + ",foreground=0x" + observedForeground.ToInt64().ToString("X") + ",foregroundPid=" + observedProcess);
                    if (observedForeground == target.Handle) break;
                }
                // Recheck immediately before SendInput; no asynchronous work intervenes.
                IntPtr foreground = NativeTest.GetForegroundWindow();
                int focusWait;
                if (foreground != target.Handle && Int32.TryParse(Environment.GetEnvironmentVariable("EAGH_TEST_WAIT_FOR_FOCUS_MS"), out focusWait) && focusWait > 0)
                {
                    // Interactive verification may use a normal click/activation of this
                    // exact test window. Never weaken the OS foreground-lock policy.
                    Console.Error.WriteLine("Waiting for isolated keyboard test window focus. HWND=0x" + target.Handle.ToInt64().ToString("X") + ", PID=" + ownProcess);
                    DateTime focusDeadline = DateTime.UtcNow.AddMilliseconds(Math.Min(30000, focusWait));
                    while (foreground != target.Handle && DateTime.UtcNow < focusDeadline)
                    {
                        await Task.Delay(100);
                        foreground = NativeTest.GetForegroundWindow();
                    }
                }
                if (foreground != target.Handle)
                {
                    uint foregroundProcess = 0;
                    if (foreground != IntPtr.Zero) NativeTest.GetWindowThreadProcessId(foreground, out foregroundProcess);
                    throw new InvalidOperationException("Could not foreground the isolated self-test window for virtual key " + key.ToString("X2") +
                        "; ownPid=" + ownProcess + ",target=0x" + target.Handle.ToInt64().ToString("X") + ",targetPid=" + targetProcess +
                        ",foreground=0x" + foreground.ToInt64().ToString("X") + ",foregroundPid=" + foregroundProcess +
                        ",targetVisible=" + target.Visible + ",targetState=" + target.WindowState +
                        "; activationTrace=" + String.Join(" | ", activationTrace.ToArray()) +
                        "; no keyboard input was injected into another application.");
                }
                NativeTest.INPUT[] events = new NativeTest.INPUT[]
                {
                    NativeTest.KeyboardInput(key, false),
                    NativeTest.KeyboardInput(key, true)
                };
                uint sent = NativeTest.SendInput((uint)events.Length, events, Marshal.SizeOf(typeof(NativeTest.INPUT)));
                if (sent != events.Length)
                    throw new InvalidOperationException("SendInput delivered " + sent + " of " + events.Length + " isolated keyboard events; Win32 error " + Marshal.GetLastWin32Error() + ".");
            }
            finally
            {
                // Do not leave this temporary test target covering the video under test.
                if (!target.IsDisposed) target.TopMost = wasTopMost;
            }
            await Task.Delay(80);
        }

        private static async Task Until(Func<Task<bool>> condition, string failure = null)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition()) return;
                await Task.Delay(80);
            }
            throw new TimeoutException(failure ?? "WebView2 self-test condition timed out.");
        }
    }

    internal static class NativeTest
    {
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, [In] INPUT[] input, int size);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT bounds);

        internal static Rectangle WindowBounds(IntPtr window)
        {
            RECT bounds;
            if (!GetWindowRect(window, out bounds)) throw new InvalidOperationException("GetWindowRect failed: " + Marshal.GetLastWin32Error());
            return Rectangle.FromLTRB(bounds.left, bounds.top, bounds.right, bounds.bottom);
        }

        internal static INPUT KeyboardInput(ushort key, bool released)
        {
            INPUT input = new INPUT();
            input.type = 1;
            input.data.keyboard.virtualKey = key;
            input.data.keyboard.flags = released ? 0x0002U : 0U;
            return input;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT { public uint type; public INPUTUNION data; }

        [StructLayout(LayoutKind.Explicit)]
        internal struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort virtualKey;
            public ushort scanCode;
            public uint flags;
            public uint time;
            public UIntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int x;
            public int y;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT { public uint message; public ushort parameterLow; public ushort parameterHigh; }
    }
}
