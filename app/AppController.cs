using System;
using System.Drawing;

namespace EdgeAnimeGamesHelper
{
    // The live application and regression tests share the same key-to-browser wiring.
    internal sealed class AppController : IDisposable
    {
        private readonly BrowserForm form;
        private bool disposed, snapQueued;
        internal OverlayController Overlay { get; private set; }

        internal AppController(BrowserForm browser)
        {
            form = browser;
            Overlay = new OverlayController(form, DispatchCommand);
            Overlay.StateChanged += OnStateChanged;
            form.LayoutModeChanged += OnLayoutChanged;
            form.ImmersionChanged += OnImmersionChanged;
            form.ToggleVisibilityRequested += OnVisibility;
            form.ToggleHotkeysRequested += OnHotkeys;
            form.ResetPositionRequested += OnReset;
            form.ExitRequested += OnExit;
            form.DragRequested += OnDrag;
            OnStateChanged();
        }

        private async void DispatchCommand(string command)
        {
            if (disposed || form.IsDisposed || (Overlay.Hidden && command != "exitImmersion")) return;
            try
            {
                if (command == "toggleImmersion") await form.ToggleImmersionAsync();
                else if (command == "exitImmersion") await form.ExitImmersionAsync();
                else await form.ExecuteVideoCommandAsync(command);
            }
            catch (Exception) { /* BrowserForm displays navigation/playback failures. */ }
        }
        internal void RefreshLayout()
        {
            if (disposed || form.IsDisposed) return;
            Overlay.SetDragRegion(Rectangle.Empty);
            Overlay.SetResizeInsets(form.ResizeInsets);
            Overlay.SetContentInsets(form.ChromeInsets.Width, form.ChromeInsets.Height);
            Overlay.SetAspectRatio(form.VideoAspectRatio);
            Overlay.SetImmersive(form.Immersive);
        }
        private void OnStateChanged()
        {
            if (disposed || form.IsDisposed) return;
            form.SetOverlayUi(Overlay.Hidden, Overlay.HotkeysEnabled, Overlay.Error);
            RefreshLayout();
        }
        private void OnLayoutChanged(object sender, EventArgs e) { RefreshLayout(); }
        private void OnImmersionChanged(object sender, EventArgs e)
        {
            RefreshLayout();
            if (disposed || !form.Immersive || snapQueued || !form.IsHandleCreated) return;
            snapQueued = true;
            form.BeginInvoke(new Action(delegate
            {
                snapQueued = false;
                if (!disposed && !form.IsDisposed && form.Immersive) Overlay.RefreshImmersivePosition();
            }));
        }
        private void OnVisibility(object sender, EventArgs e) { Overlay.ToggleVisibility(); }
        private void OnHotkeys(object sender, EventArgs e) { Overlay.SetHotkeys(!Overlay.HotkeysEnabled); }
        private void OnReset(object sender, EventArgs e) { Overlay.ResetPosition(); }
        private void OnExit(object sender, EventArgs e) { form.Close(); }
        private void OnDrag(object sender, EventArgs e) { Overlay.BeginDrag(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Overlay.StateChanged -= OnStateChanged;
            form.LayoutModeChanged -= OnLayoutChanged;
            form.ImmersionChanged -= OnImmersionChanged;
            form.ToggleVisibilityRequested -= OnVisibility;
            form.ToggleHotkeysRequested -= OnHotkeys;
            form.ResetPositionRequested -= OnReset;
            form.ExitRequested -= OnExit;
            form.DragRequested -= OnDrag;
            Overlay.Dispose();
        }
    }
}
