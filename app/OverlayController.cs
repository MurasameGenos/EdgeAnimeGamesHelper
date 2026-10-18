using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EdgeAnimeGamesHelper
{
    public sealed class OverlayController : IDisposable
    {
        private const int PauseId = 0x5A01, ShiftPauseId = 0x5A02, BackwardId = 0x5A03,
            ForwardId = 0x5A04, VisibilityId = 0x5A05, ImmersionId = 0x5A06,
            NumpadBackwardId = 0x5A08, NumpadForwardId = 0x5A09, NumpadVisibilityId = 0x5A0A, NumpadImmersionId = 0x5A0B;
        private readonly Form form;
        private readonly Action<string> command;
        private readonly OverlayNativeWindow native;
        private readonly GameInputFallback gameInput;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem visibilityMenu, hotkeysMenu, immersionMenu, inputModeMenu;
        private readonly Timer topmostTimer;
        private readonly List<int> registered = new List<int>();
        private bool disposed, hidden, immersive;
        private bool hotkeysEnabled = true;
        private Rectangle browserBounds;
        private string immersiveDisplay;
        private string error = String.Empty;

        public bool Hidden { get { return hidden; } }
        public bool Immersive { get { return immersive; } }
        public bool HotkeysEnabled { get { return hotkeysEnabled; } }
        public string Error { get { return error; } }
        public event Action StateChanged;

        public OverlayController(Form target, Action<string> handler)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (handler == null) throw new ArgumentNullException("handler");
            form = target;
            command = handler;
            form.TopMost = true;
            form.ShowInTaskbar = false;
            native = new OverlayNativeWindow(form);
            native.Hotkey += OnHotkey;
            native.MoveEnded += SnapToEdge;
            gameInput = new GameInputFallback(OnGameInput);
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(new ToolStripMenuItem("攻略小窗 " + Application.ProductVersion) { Enabled = false });
            inputModeMenu = new ToolStripMenuItem("输入：系统热键") { Enabled = false };
            trayMenu.Items.Add(inputModeMenu);
            trayMenu.Items.Add("显示浏览器", null, delegate { RestoreBrowser(); });
            visibilityMenu = new ToolStripMenuItem("隐藏窗口（7）", null, delegate { ToggleVisibility(); });
            immersionMenu = new ToolStripMenuItem("沉浸模式（8）", null, delegate { if (!hidden) command("toggleImmersion"); });
            hotkeysMenu = new ToolStripMenuItem("启用全局快捷键", null, delegate { SetHotkeys(!hotkeysEnabled); });
            trayMenu.Items.Add(visibilityMenu);
            trayMenu.Items.Add(immersionMenu);
            trayMenu.Items.Add(hotkeysMenu);
            trayMenu.Items.Add("重置窗口位置", null, delegate { ResetPosition(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, delegate { form.Close(); });
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "攻略小窗 · 独立浏览器", ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { RestoreBrowser(); };
            topmostTimer = new Timer { Interval = 1000 };
            topmostTimer.Tick += delegate
            {
                UpdateTray();
                if (hidden) return;
                if (immersive) RefreshImmersivePosition();
                else KeepTopmost();
            };
            topmostTimer.Start();
            form.FormClosed += OnFormClosed;
            ConfigureHotkeys();
            UpdateTray();
        }

        public void SetDragRegion(Rectangle region) { native.DragRegion = region; }
        public void SetResizeInsets(Padding insets) { native.ResizeInsets = insets; }
        public void SetContentInsets(int horizontal, int vertical)
        {
            native.HorizontalInsets = Math.Max(0, horizontal);
            native.VerticalInsets = Math.Max(0, vertical);
        }
        public void SetAspectRatio(double ratio)
        {
            if (Double.IsNaN(ratio) || Double.IsInfinity(ratio) || ratio <= 0.1 || ratio >= 10 || Math.Abs(native.AspectRatio - ratio) <= 0.001) return;
            native.AspectRatio = ratio;
            if (immersive) FitImmersiveWindow();
        }

        public void SetImmersive(bool enabled)
        {
            if (disposed || immersive == enabled) return;
            immersive = enabled;
            if (enabled)
            {
                browserBounds = form.Bounds;
                immersiveDisplay = Screen.FromRectangle(browserBounds).DeviceName;
                FitImmersiveWindow();
            }
            else if (!browserBounds.IsEmpty)
            {
                Rectangle area = Screen.FromRectangle(browserBounds).WorkingArea;
                Rectangle restored = browserBounds;
                restored.Width = Math.Min(restored.Width, area.Width);
                restored.Height = Math.Min(restored.Height, area.Height);
                restored.X = Math.Max(area.Left, Math.Min(restored.X, area.Right - restored.Width));
                restored.Y = Math.Max(area.Top, Math.Min(restored.Y, area.Bottom - restored.Height));
                form.Bounds = restored;
                KeepTopmost();
            }
            PublishState();
        }

        private void FitImmersiveWindow()
        {
            Rectangle screen = ImmersiveMonitor().Bounds;
            double width = Math.Max(form.MinimumSize.Width, form.Width);
            double height = width / native.AspectRatio;
            if (height < form.MinimumSize.Height) { height = form.MinimumSize.Height; width = height * native.AspectRatio; }
            double scale = Math.Min(1.0, Math.Min(screen.Width / width, screen.Height / height));
            form.Size = new Size(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
            RefreshImmersivePosition();
        }

        // Use the actual HWND rectangle after WinForms/WebView layout has committed.
        public bool RefreshImmersivePosition()
        {
            if (disposed || !immersive || !form.IsHandleCreated) return false;
            NativeMethods.RECT actual;
            if (!NativeMethods.GetWindowRect(form.Handle, out actual)) return ReportSnapError();
            Rectangle bounds = actual.ToRectangle();
            Rectangle screen = ImmersiveMonitor().Bounds;
            Rectangle snapped = OverlayGeometry.SnapBottom(bounds, screen);
            if (!NativeMethods.SetWindowPos(form.Handle, NativeMethods.HWND_TOPMOST, snapped.X, snapped.Y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE)) return ReportSnapError();
            if (!NativeMethods.GetWindowRect(form.Handle, out actual)) return ReportSnapError();
            Rectangle verified = actual.ToRectangle();
            bool attached = verified.Bottom == screen.Bottom;
            if (!attached)
            {
                error = "窗口尚未贴到屏幕底部，请从托盘重置窗口位置。";
                PublishState();
            }
            return attached;
        }

        private Screen ImmersiveMonitor()
        {
            foreach (Screen display in Screen.AllScreens)
                if (display.DeviceName == immersiveDisplay) return display;
            Screen fallback = Screen.FromRectangle(form.Bounds);
            immersiveDisplay = fallback.DeviceName;
            return fallback;
        }

        private bool ReportSnapError()
        {
            error = "窗口吸边失败：" + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            PublishState();
            return false;
        }

        public void BeginDrag()
        {
            if (disposed || hidden) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(form.Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
        }

        public void ToggleVisibility()
        {
            if (disposed) return;
            hidden = !hidden;
            // Release every playback/mode shortcut before hiding. Only 7 can wake us.
            ConfigureHotkeys();
            NativeMethods.ShowWindow(form.Handle, hidden ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOWNOACTIVATE);
            if (!hidden)
            {
                if (immersive) RefreshImmersivePosition();
                else KeepTopmost();
            }
            PublishState();
        }

        public void SetHotkeys(bool enabled)
        {
            if (disposed) return;
            hotkeysEnabled = enabled;
            ConfigureHotkeys();
            PublishState();
        }

        public void ResetPosition()
        {
            if (disposed) return;
            Rectangle area = Screen.FromRectangle(form.Bounds).WorkingArea;
            int margin = (int)Math.Round(24 * NativeMethods.DpiScale(form.Handle));
            int width = Math.Min(Math.Max(form.MinimumSize.Width, 800), area.Width);
            int height = Math.Max(form.MinimumSize.Height, (int)Math.Round((width - native.HorizontalInsets) / native.AspectRatio) + native.VerticalInsets);
            if (height > area.Height) { height = area.Height; width = (int)Math.Round((height - native.VerticalInsets) * native.AspectRatio) + native.HorizontalInsets; }
            form.Bounds = new Rectangle(Math.Max(area.Left, area.Right - width - margin), Math.Max(area.Top, area.Top + margin), width, height);
            if (immersive) RefreshImmersivePosition();
            else KeepTopmost();
            PublishState();
        }

        private void RestoreBrowser()
        {
            if (disposed) return;
            hidden = false;
            command("exitImmersion");
            ConfigureHotkeys();
            NativeMethods.ShowWindow(form.Handle, NativeMethods.SW_SHOWNOACTIVATE);
            KeepTopmost();
            form.Activate();
            PublishState();
        }

        private void ConfigureHotkeys()
        {
            error = String.Empty;
            gameInput.UpdateState(hotkeysEnabled, hidden);
            if (!hotkeysEnabled) { UnregisterHotkeys(); return; }
            if (hidden)
            {
                foreach (int id in registered.ToArray())
                    if (id != VisibilityId && id != NumpadVisibilityId) Unregister(id);
            }
            List<string> failures = new List<string>();
            if (!Register(VisibilityId, 0, (uint)Keys.D7)) failures.Add("7");
            Register(NumpadVisibilityId, 0, (uint)Keys.NumPad7);
            if (!hidden)
            {
                // Playback shortcuts are active in the normal browser as well as immersion.
                if (!Register(PauseId, 0, (uint)Keys.Oemtilde)) failures.Add("` / ~");
                if (!Register(ShiftPauseId, NativeMethods.MOD_SHIFT, (uint)Keys.Oemtilde)) failures.Add("Shift+~");
                if (!Register(BackwardId, 0, (uint)Keys.D5)) failures.Add("5");
                if (!Register(ForwardId, 0, (uint)Keys.D6)) failures.Add("6");
                if (!Register(ImmersionId, 0, (uint)Keys.D8)) failures.Add("8");
                Register(NumpadBackwardId, 0, (uint)Keys.NumPad5);
                Register(NumpadForwardId, 0, (uint)Keys.NumPad6);
                Register(NumpadImmersionId, 0, (uint)Keys.NumPad8);
            }
            if (failures.Count > 0)
            {
                error = "快捷键 " + String.Join("、", failures.ToArray()) + " 已被其他程序占用；可暂停热键后重试。";
                if (tray != null)
                {
                    tray.BalloonTipTitle = "攻略小窗：快捷键冲突";
                    tray.BalloonTipText = error;
                    tray.ShowBalloonTip(5000);
                }
            }
        }

        private bool Register(int id, uint modifiers, uint key)
        {
            if (registered.Contains(id)) return true;
            bool ok = NativeMethods.RegisterHotKey(form.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, key);
            if (ok) registered.Add(id);
            return ok;
        }
        private void Unregister(int id)
        {
            if (registered.Remove(id) && form.IsHandleCreated) NativeMethods.UnregisterHotKey(form.Handle, id);
        }
        private void UnregisterHotkeys()
        {
            foreach (int id in registered.ToArray()) Unregister(id);
        }
        private void OnHotkey(int id)
        {
            if (disposed || !hotkeysEnabled || !registered.Contains(id)) return;
            // Games can disable application-defined hotkey messages. A foreground
            // game uses one polling backend, so the same press cannot fire twice.
            if (gameInput.ShouldUseFallbackForForeground()) return;
            if (id == VisibilityId || id == NumpadVisibilityId) { ToggleVisibility(); return; }
            if (hidden) return;
            if (id == ImmersionId || id == NumpadImmersionId) command("toggleImmersion");
            else if (id == PauseId || id == ShiftPauseId) command("togglePause");
            else if (id == BackwardId || id == NumpadBackwardId) command("seekBackward");
            else if (id == ForwardId || id == NumpadForwardId) command("seekForward");
        }

        private void OnGameInput(string action)
        {
            if (disposed || !hotkeysEnabled) return;
            if (action == "toggleVisibility") { ToggleVisibility(); return; }
            if (!hidden) command(action);
        }

        private void SnapToEdge()
        {
            if (disposed) return;
            if (immersive)
            {
                immersiveDisplay = Screen.FromRectangle(form.Bounds).DeviceName;
                RefreshImmersivePosition();
                return;
            }
            Rectangle area = Screen.FromRectangle(form.Bounds).Bounds;
            Rectangle snapped = OverlayGeometry.Snap(form.Bounds, area, (int)Math.Round(24 * NativeMethods.DpiScale(form.Handle)));
            if (form.Bounds != snapped) form.Bounds = snapped;
            KeepTopmost();
        }
        private void KeepTopmost()
        {
            if (!disposed && !hidden && form.IsHandleCreated)
                NativeMethods.SetWindowPos(form.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        private void UpdateTray()
        {
            visibilityMenu.Text = hidden ? "显示窗口（7）" : "隐藏窗口（7）";
            immersionMenu.Checked = immersive;
            immersionMenu.Enabled = !hidden;
            immersionMenu.Text = immersive ? "退出沉浸模式（8）" : "沉浸模式（8）";
            hotkeysMenu.Checked = hotkeysEnabled;
            inputModeMenu.Text = gameInput.ShouldUseFallbackForForeground() ? "输入：终末地兼容检测" : "输入：系统热键";
            tray.Text = "攻略小窗 · " + (hidden ? "已隐藏，仅 7 可恢复" : immersive ? "沉浸模式" : "浏览器");
        }
        private void PublishState()
        {
            UpdateTray();
            Action handler = StateChanged;
            if (handler != null) handler();
        }
        private void OnFormClosed(object sender, FormClosedEventArgs e) { Dispose(); }
        public void Close() { if (!disposed) form.Close(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            topmostTimer.Stop();
            topmostTimer.Dispose();
            gameInput.Dispose();
            UnregisterHotkeys();
            form.FormClosed -= OnFormClosed;
            native.Hotkey -= OnHotkey;
            native.MoveEnded -= SnapToEdge;
            native.Dispose();
            tray.Visible = false;
            tray.Dispose();
            trayMenu.Dispose();
        }
    }
}
