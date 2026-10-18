using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EdgeAnimeGamesHelper
{
    // Pure geometry is shared by the real window and the --self-test checks.
    public static class OverlayGeometry
    {
        public static Rectangle SnapBottom(Rectangle bounds, Rectangle screen)
        {
            int x = Math.Max(screen.Left, Math.Min(bounds.X, screen.Right - bounds.Width));
            return new Rectangle(x, screen.Bottom - bounds.Height, bounds.Width, bounds.Height);
        }

        public static Rectangle SnapNearestEdge(Rectangle bounds, Rectangle screen)
        {
            int x = Math.Max(screen.Left, Math.Min(bounds.X, screen.Right - bounds.Width));
            int y = Math.Max(screen.Top, Math.Min(bounds.Y, screen.Bottom - bounds.Height));
            long left = Math.Abs((long)x - screen.Left);
            long right = Math.Abs((long)x + bounds.Width - screen.Right);
            long top = Math.Abs((long)y - screen.Top);
            long bottom = Math.Abs((long)y + bounds.Height - screen.Bottom);
            long nearest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            if (left == nearest) x = screen.Left;
            else if (right == nearest) x = screen.Right - bounds.Width;
            else if (top == nearest) y = screen.Top;
            else y = screen.Bottom - bounds.Height;
            return new Rectangle(x, y, bounds.Width, bounds.Height);
        }

        public static Rectangle Snap(Rectangle bounds, Rectangle workingArea, int distance)
        {
            int x = bounds.X;
            int y = bounds.Y;
            if (Math.Abs(bounds.Left - workingArea.Left) <= distance) x = workingArea.Left;
            else if (Math.Abs(bounds.Right - workingArea.Right) <= distance) x = workingArea.Right - bounds.Width;
            if (Math.Abs(bounds.Top - workingArea.Top) <= distance) y = workingArea.Top;
            else if (Math.Abs(bounds.Bottom - workingArea.Bottom) <= distance) y = workingArea.Bottom - bounds.Height;
            return new Rectangle(x, y, bounds.Width, bounds.Height);
        }

        public static Rectangle KeepAspect(Rectangle proposed, Rectangle original, int edge,
            double ratio, int horizontalInsets, int verticalInsets, Size minimum)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0) return proposed;
            int width = Math.Max(1, proposed.Width - horizontalInsets);
            int height = Math.Max(1, proposed.Height - verticalInsets);
            bool widthDrives = edge == 1 || edge == 2;
            if (edge >= 4 && edge != 6)
            {
                double dx = Math.Abs(proposed.Width - original.Width);
                double dy = Math.Abs(proposed.Height - original.Height) * ratio;
                widthDrives = dx >= dy;
            }
            if (widthDrives) height = Math.Max(1, (int)Math.Round(width / ratio));
            else width = Math.Max(1, (int)Math.Round(height * ratio));
            int minimumContentWidth = Math.Max(1, minimum.Width - horizontalInsets);
            int minimumContentHeight = Math.Max(1, minimum.Height - verticalInsets);
            minimumContentWidth = Math.Max(minimumContentWidth, (int)Math.Ceiling(minimumContentHeight * ratio));
            if (width < minimumContentWidth)
            {
                width = minimumContentWidth;
                height = Math.Max(1, (int)Math.Round(width / ratio));
            }
            int outerWidth = width + horizontalInsets;
            int outerHeight = height + verticalInsets;
            bool left = edge == 1 || edge == 4 || edge == 7;
            bool top = edge == 3 || edge == 4 || edge == 5;
            return new Rectangle(left ? proposed.Right - outerWidth : proposed.Left,
                top ? proposed.Bottom - outerHeight : proposed.Top, outerWidth, outerHeight);
        }
    }

    internal sealed class OverlayNativeWindow : System.Windows.Forms.NativeWindow, IDisposable
    {
        private readonly Form form;
        private Rectangle resizeOrigin;
        internal double AspectRatio = 16.0 / 9.0;
        internal int HorizontalInsets = 16;
        internal int VerticalInsets = 116;
        internal Padding ResizeInsets = new Padding(8);
        internal Rectangle DragRegion = Rectangle.Empty;
        internal event Action<int> Hotkey;
        internal event Action MoveEnded;

        internal OverlayNativeWindow(Form target)
        {
            form = target;
            AssignHandle(target.Handle);
            resizeOrigin = target.Bounds;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_HOTKEY)
            {
                Action<int> handler = Hotkey;
                if (handler != null) handler(message.WParam.ToInt32());
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == NativeMethods.WM_NCHITTEST && form.WindowState == FormWindowState.Normal)
            {
                long packed = message.LParam.ToInt64();
                Point point = new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
                Rectangle bounds = form.Bounds;
                bool left = point.X < bounds.Left + Math.Max(4, ResizeInsets.Left);
                bool right = point.X >= bounds.Right - Math.Max(4, ResizeInsets.Right);
                bool top = point.Y < bounds.Top + Math.Max(4, ResizeInsets.Top);
                bool bottom = point.Y >= bounds.Bottom - Math.Max(4, ResizeInsets.Bottom);
                int hit = left && top ? 13 : right && top ? 14 : left && bottom ? 16 : right && bottom ? 17 :
                    left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
                if (hit != 0)
                {
                    message.Result = new IntPtr(hit);
                    return;
                }
                if (!DragRegion.IsEmpty && DragRegion.Contains(form.PointToClient(point)))
                {
                    message.Result = new IntPtr(NativeMethods.HTCAPTION);
                    return;
                }
            }
            if (message.Msg == NativeMethods.WM_ENTERSIZEMOVE) resizeOrigin = form.Bounds;
            if (message.Msg == NativeMethods.WM_SIZING)
            {
                NativeMethods.RECT rect = (NativeMethods.RECT)Marshal.PtrToStructure(message.LParam, typeof(NativeMethods.RECT));
                Rectangle adjusted = OverlayGeometry.KeepAspect(rect.ToRectangle(), resizeOrigin,
                    message.WParam.ToInt32(), AspectRatio, HorizontalInsets, VerticalInsets, form.MinimumSize);
                Marshal.StructureToPtr(new NativeMethods.RECT(adjusted), message.LParam, false);
                message.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == NativeMethods.WM_EXITSIZEMOVE)
            {
                Action handler = MoveEnded;
                if (handler != null) handler();
            }
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) ReleaseHandle();
        }
    }

    internal static class NativeMethods
    {
        internal const int WM_HOTKEY = 0x0312;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_NCLBUTTONDOWN = 0x00A1;
        internal const int WM_ENTERSIZEMOVE = 0x0231;
        internal const int WM_EXITSIZEMOVE = 0x0232;
        internal const int WM_SIZING = 0x0214;
        internal const int HTCAPTION = 2;
        internal const uint MOD_SHIFT = 0x4;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const uint SWP_NOSIZE = 0x1;
        internal const uint SWP_NOMOVE = 0x2;
        internal const uint SWP_NOACTIVATE = 0x10;
        internal const uint SWP_FRAMECHANGED = 0x20;
        internal const int SW_HIDE = 0;
        internal const int SW_SHOWNOACTIVATE = 4;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left, Top, Right, Bottom;
            internal RECT(Rectangle rectangle)
            {
                Left = rectangle.Left; Top = rectangle.Top; Right = rectangle.Right; Bottom = rectangle.Bottom;
            }
            internal Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);
        internal static double DpiScale(IntPtr window)
        {
            try { uint dpi = GetDpiForWindow(window); return dpi == 0 ? 1.0 : dpi / 96.0; }
            catch (EntryPointNotFoundException) { return 1.0; }
        }
    }
}
