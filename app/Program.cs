using System;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace EdgeAnimeGamesHelper
{
    internal static class Program
    {
        private const string HomeUrl = "https://www.bilibili.com/";

        [STAThread]
        private static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--test-game-input") >= 0)
            {
                try
                {
                    object result = new { ok = true, tests = GameInputFallbackTests.Run() };
                    using (System.IO.StreamWriter output = new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)))
                        output.WriteLine(new JavaScriptSerializer().Serialize(result));
                    return 0;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(error.Message);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Array.IndexOf(args, "--self-test") >= 0) return AppSelfTests.Run();
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdgeAnimeGamesHelper");
            string profile = Path.Combine(appData, "WebView2");
            string identity = WindowsIdentity.GetCurrent().User.Value;
            bool firstInstance;
            using (Mutex instance = new Mutex(true, "Local\\EdgeAnimeGamesHelper.Desktop." + identity, out firstInstance))
            using (EventWaitHandle activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\EdgeAnimeGamesHelper.Activate." + identity))
            {
                if (!firstInstance) { activate.Set(); return 0; }
                try
                {
                    Directory.CreateDirectory(appData);
                    BrowserForm form = new BrowserForm(profile, HomeUrl, false);
                    WindowPlacement.Load(form, Path.Combine(appData, "window.json"));
                    AppController app = new AppController(form);
                    OverlayController overlay = app.Overlay;
                    form.Shown += async delegate { await form.InitializeBrowserAsync(); app.RefreshLayout(); };
                    form.FormClosing += delegate { WindowPlacement.Save(form, Path.Combine(appData, "window.json")); };
                    RegisteredWaitHandle wake = ThreadPool.RegisterWaitForSingleObject(activate, delegate
                    {
                        try
                        {
                            if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke(new Action(async delegate
                            {
                                if (form.IsDisposed) return;
                                if (overlay.Hidden) overlay.ToggleVisibility();
                                await form.ExitImmersionAsync();
                                form.Activate();
                            }));
                        }
                        catch (InvalidOperationException) { }
                    }, null, Timeout.Infinite, false);
                    try { Application.Run(form); }
                    finally
                    {
                        wake.Unregister(null);
                        app.Dispose();
                        if (!form.IsDisposed) form.Dispose();
                    }
                    return 0;
                }
                catch (Exception error)
                {
                    MessageBox.Show("攻略小窗启动失败：\n" + error.Message, "攻略小窗", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    internal static class WindowPlacement
    {
        internal static void Load(Form form, string file)
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Rectangle desired = new Rectangle(Math.Max(work.Left, work.Right - 840), work.Top + 24, 816, 566);
            try
            {
                if (File.Exists(file))
                {
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    Placement value = json.Deserialize<Placement>(File.ReadAllText(file));
                    if (value != null && value.Width >= 360 && value.Height >= 240 && value.Width <= 10000 && value.Height <= 10000)
                        desired = new Rectangle(value.X, value.Y, value.Width, value.Height);
                }
            }
            catch (Exception) { /* A malformed placement must not prevent opening the browser. */ }
            work = Screen.FromRectangle(desired).WorkingArea;
            desired.Width = Math.Min(desired.Width, work.Width);
            desired.Height = Math.Min(desired.Height, work.Height);
            desired.X = Math.Max(work.Left, Math.Min(work.Right - desired.Width, desired.X));
            desired.Y = Math.Max(work.Top, Math.Min(work.Bottom - desired.Height, desired.Y));
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = desired;
        }

        internal static void Save(Form form, string file)
        {
            try
            {
                Rectangle bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
                JavaScriptSerializer json = new JavaScriptSerializer();
                File.WriteAllText(file, json.Serialize(new Placement { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height }));
            }
            catch (Exception) { /* Saving geometry must not block exit or hold global hotkeys. */ }
        }

        public sealed class Placement
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }
    }
}
