using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EdgeAnimeGamesHelper
{
    // RegisterHotKey is retained for ordinary applications. Some games consume those
    // notifications, so the named game's foreground window uses key-state edges.
    // This never injects code, installs a keyboard hook, or suppresses game input.
    internal sealed class GameInputFallback : IDisposable
    {
        private static readonly int[] Keys = { 0xC0, 0x35, 0x36, 0x37, 0x38, 0x65, 0x66, 0x67, 0x68 };
        private readonly Action<string> dispatch;
        private readonly Func<uint, bool> gameMatcher;
        private readonly Func<int, bool> keyReader;
        private readonly Func<uint> foregroundPid;
        private readonly PressEdgeTracker edges = new PressEdgeTracker();
        private readonly Timer timer;
        private bool hotkeysEnabled = true, hidden, active, disposed;
        private int stateRevision;
        private uint cachedPid;
        private bool cachedMatch;
        private long cacheValidUntil;

        internal GameInputFallback(Action<string> handler, Func<uint, bool> gameMatcher = null,
            Func<int, bool> keyReader = null, Func<uint> foregroundPid = null, bool startTimer = true)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            dispatch = handler;
            this.gameMatcher = gameMatcher ?? MatchesGameProcess;
            this.keyReader = keyReader ?? IsKeyDown;
            this.foregroundPid = foregroundPid ?? GetForegroundPid;
            timer = new Timer { Interval = 15 };
            timer.Tick += OnTick;
            if (startTimer) timer.Start();
        }

        // A fresh foreground lookup also lets the WM_HOTKEY path choose exactly one
        // backend before the next timer tick, preventing a key from firing twice.
        internal bool IsGameForeground
        {
            get
            {
                if (disposed) return false;
                try
                {
                    uint pid = foregroundPid();
                    return pid != 0 && gameMatcher(pid);
                }
                catch (ArgumentException) { return false; }
                catch (InvalidOperationException) { return false; }
                catch (System.ComponentModel.Win32Exception) { return false; }
            }
        }

        internal bool ShouldUseFallbackForForeground()
        {
            return !disposed && hotkeysEnabled && IsGameForeground;
        }

        internal void UpdateState(bool enabled, bool isHidden)
        {
            if (disposed || (hotkeysEnabled == enabled && hidden == isHidden)) return;
            hotkeysEnabled = enabled;
            hidden = isHidden;
            stateRevision++;
            // In particular, holding 7 while it hides the window must not restore
            // it. Holding another key while restoring must not issue a command.
            SnapshotKeys();
        }

        private void OnTick(object sender, EventArgs args) { Poll(); }

        internal void Poll()
        {
            if (disposed) return;
            if (!ShouldUseFallbackForForeground())
            {
                active = false;
                edges.Clear();
                return;
            }
            if (!active)
            {
                active = true;
                SnapshotKeys();
                return;
            }

            // Read only the configured controls and modifier states. Updating edges
            // even for blocked combinations prevents commands on modifier release.
            bool blocked = keyReader(0x11) || keyReader(0x12) || keyReader(0x5B) || keyReader(0x5C);
            bool shift = keyReader(0x10);
            int revision = stateRevision;
            foreach (int key in Keys)
            {
                bool pressed = edges.Update(key, keyReader(key));
                if (!pressed || blocked || (shift && key != 0xC0)) continue;
                string command = CommandForKey(key);
                if (hidden && command != "toggleVisibility") continue;
                dispatch(command);
                // The callback can hide, restore, pause hotkeys, or dispose us.
                // Its snapshot owns the next edge state; do not overwrite it.
                if (disposed || stateRevision != revision) return;
            }
        }

        private void SnapshotKeys()
        {
            foreach (int key in Keys) edges.Update(key, keyReader(key));
        }

        internal static string CommandForKey(int key)
        {
            switch (key)
            {
                case 0xC0: return "togglePause";
                case 0x35: case 0x65: return "seekBackward";
                case 0x36: case 0x66: return "seekForward";
                case 0x37: case 0x67: return "toggleVisibility";
                case 0x38: case 0x68: return "toggleImmersion";
                default: return null;
            }
        }

        private bool MatchesGameProcess(uint pid)
        {
            long now = DateTime.UtcNow.Ticks;
            if (pid == cachedPid && now < cacheValidUntil) return cachedMatch;
            bool matches = false;
            try
            {
                using (Process process = Process.GetProcessById(unchecked((int)pid)))
                    matches = IsSupportedProcessName(process.ProcessName);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            cachedPid = pid;
            cachedMatch = matches;
            // Bound cache lifetime for process exit/PID reuse without opening the
            // process on every 15 ms sample. No executable path is inspected.
            cacheValidUntil = now + TimeSpan.FromSeconds(2).Ticks;
            return matches;
        }

        internal static bool IsSupportedProcessName(string name)
        {
            return String.Equals(name, "Endfield", StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, "endfild", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static uint GetForegroundPid()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return 0;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return pid;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();
            timer.Tick -= OnTick;
            timer.Dispose();
            edges.Clear();
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }

    internal sealed class PressEdgeTracker
    {
        private readonly Dictionary<int, bool> down = new Dictionary<int, bool>();

        internal bool Update(int key, bool isDown)
        {
            bool wasDown;
            down.TryGetValue(key, out wasDown);
            down[key] = isDown;
            return isDown && !wasDown;
        }

        internal void Clear() { down.Clear(); }
    }
}
