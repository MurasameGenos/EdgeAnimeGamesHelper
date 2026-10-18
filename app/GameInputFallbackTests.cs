using System;
using System.Collections.Generic;

namespace EdgeAnimeGamesHelper
{
    // Deterministic tests of the game's polling backend. All key and foreground
    // state is supplied in memory; the tests never read or send real input.
    internal static class GameInputFallbackTests
    {
        internal static List<object> Run()
        {
            List<object> results = new List<object>();

            Check(GameInputFallback.IsSupportedProcessName("Endfield")
                && GameInputFallback.IsSupportedProcessName("ENDFILD")
                && !GameInputFallback.IsSupportedProcessName("EndfieldLauncher")
                && !GameInputFallback.IsSupportedProcessName("other-endfield")
                && !GameInputFallback.IsSupportedProcessName("Endfield.exe")
                && !GameInputFallback.IsSupportedProcessName(null),
                "game fallback matches only the two exact game process names", results);

            int[] controls = { 0xC0, 0x35, 0x36, 0x37, 0x38, 0x65, 0x66, 0x67, 0x68 };
            string[] commands = { "togglePause", "seekBackward", "seekForward", "toggleVisibility", "toggleImmersion", "seekBackward", "seekForward", "toggleVisibility", "toggleImmersion" };
            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                for (int i = 0; i < controls.Length; i++)
                {
                    f.Down.Add(controls[i]);
                    f.Input.Poll();
                    f.Input.Poll();
                    f.Input.Poll();
                    Require(f.Calls.Count == i + 1 && f.Calls[i] == commands[i], "configured game key must dispatch exactly once while held");
                    f.Down.Remove(controls[i]);
                    f.Input.Poll();
                }
                Check(GameInputFallback.CommandForKey(0x39) == null,
                    "game fallback maps every configured top-row and numpad key and leaves 9 unused", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                f.Tap(0x35);
                f.Tap(0x35);
                Check(f.Calls.Count == 2 && f.Calls[0] == "seekBackward" && f.Calls[1] == "seekBackward",
                    "game fallback fires again after release and repress", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Foreground = 2;
                Require(!f.Input.IsGameForeground && !f.Input.ShouldUseFallbackForForeground(), "ordinary foreground must use normal hotkeys");
                f.Tap(0xC0);
                f.Tap(0x37);
                f.Foreground = 0;
                f.Tap(0x38);
                Check(f.Calls.Count == 0 && !f.Input.IsGameForeground,
                    "game fallback never dispatches for another application or no foreground window", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Down.Add(0x35);
                f.Input.Poll();
                f.Input.Poll();
                Require(f.Calls.Count == 0, "entering a game with 5 held must not seek");
                f.Down.Clear();
                f.Input.Poll();
                f.Tap(0x35);
                Check(f.Calls.Count == 1 && f.Calls[0] == "seekBackward",
                    "game activation snapshots already held keys before accepting a new press", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Foreground = 2;
                f.Input.Poll();
                f.Down.Add(0x38);
                f.Foreground = 1;
                f.Input.Poll();
                f.Input.Poll();
                Require(f.Calls.Count == 0, "switching into the game with 8 held must not toggle immersion");
                f.Down.Clear();
                f.Input.Poll();
                f.Tap(0x38);
                Check(f.Calls.Count == 1 && f.Calls[0] == "toggleImmersion",
                    "switching from an ordinary application does not create a phantom game key press", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                f.Down.Add(0x36);
                f.Input.Poll();
                f.Calls.Clear();
                f.Foreground = 2;
                f.Input.Poll();
                f.Foreground = 1;
                f.Input.Poll();
                f.Input.Poll();
                Require(f.Calls.Count == 0, "a held key must not fire when returning to the game");
                f.Down.Clear();
                f.Input.Poll();
                f.Tap(0x36);
                Check(f.Calls.Count == 1 && f.Calls[0] == "seekForward",
                    "switching away and back preserves release-before-repress behavior", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                f.Input.UpdateState(true, true);
                int[] blockedWhileHidden = { 0xC0, 0x35, 0x36, 0x38, 0x65, 0x66, 0x68 };
                foreach (int key in blockedWhileHidden) f.Tap(key);
                Require(f.Calls.Count == 0, "hidden game window must ignore every video and immersion key");
                f.Tap(0x37);
                f.Tap(0x67);
                Check(f.Calls.Count == 2 && f.Calls[0] == "toggleVisibility" && f.Calls[1] == "toggleVisibility",
                    "hidden game window accepts only top-row or numpad 7 for recovery", results);
            }

            using (Fixture f = new Fixture())
            {
                bool hidden = false;
                f.AfterDispatch = delegate(string command)
                {
                    if (command != "toggleVisibility") return;
                    hidden = !hidden;
                    f.Input.UpdateState(true, hidden);
                };
                f.Input.Poll();
                f.Down.Add(0x37);
                f.Down.Add(0x38);
                f.Input.Poll();
                f.Input.Poll();
                f.Input.Poll();
                Require(hidden && f.Calls.Count == 1 && f.Calls[0] == "toggleVisibility", "hiding inside dispatch must snapshot held 7 and stop the current poll before 8");
                f.Down.Remove(0x37);
                f.Input.Poll();
                f.Down.Add(0x37);
                f.Input.Poll();
                Require(!hidden && f.Calls.Count == 2, "a fresh 7 press must restore the hidden game window");
                f.Input.Poll();
                Check(f.Calls.Count == 2,
                    "visibility changes during polling neither repeat a held 7 nor leak simultaneous 8", results);
            }

            using (Fixture f = new Fixture())
            {
                bool hidden = true;
                f.AfterDispatch = delegate(string command)
                {
                    if (command != "toggleVisibility") return;
                    hidden = !hidden;
                    f.Input.UpdateState(true, hidden);
                };
                f.Input.UpdateState(true, true);
                f.Input.Poll();
                f.Down.Add(0x35);
                f.Input.Poll();
                f.Down.Add(0x37);
                f.Input.Poll();
                f.Input.Poll();
                Require(!hidden && f.Calls.Count == 1 && f.Calls[0] == "toggleVisibility", "restoring while 5 is held must not seek");
                f.Down.Clear();
                f.Input.Poll();
                f.Tap(0x35);
                Check(f.Calls.Count == 2 && f.Calls[1] == "seekBackward",
                    "restoring a hidden game window requires a fresh video key press", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                f.Input.UpdateState(false, false);
                Require(!f.Input.ShouldUseFallbackForForeground(), "disabled hotkeys must disable game fallback routing");
                f.Tap(0xC0);
                f.Tap(0x37);
                f.Down.Add(0x36);
                f.Input.Poll();
                f.Input.UpdateState(true, false);
                f.Input.Poll();
                f.Input.Poll();
                Require(f.Calls.Count == 0, "enabling while 6 is held must not issue a command");
                f.Down.Clear();
                f.Input.Poll();
                f.Tap(0x36);
                Check(f.Calls.Count == 1 && f.Calls[0] == "seekForward",
                    "disabled game controls stay silent and reenable without phantom presses", results);
            }

            int[] modifiers = { 0x11, 0x12, 0x5B, 0x5C };
            foreach (int modifier in modifiers)
            {
                using (Fixture f = new Fixture())
                {
                    f.Input.Poll();
                    f.Down.Add(modifier);
                    f.Down.Add(0x35);
                    f.Down.Add(0xC0);
                    f.Down.Add(0x37);
                    f.Input.Poll();
                    f.Down.Remove(modifier);
                    f.Input.Poll();
                    Require(f.Calls.Count == 0, "modified game controls must remain blocked when the modifier is released");
                    f.Down.Clear();
                    f.Input.Poll();
                    f.Tap(0x35);
                    Check(f.Calls.Count == 1 && f.Calls[0] == "seekBackward",
                        "game fallback suppresses modified input and modifier-release phantoms for VK " + modifier, results);
                }
            }

            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                f.Down.Add(0x10);
                f.Down.Add(0x35);
                f.Down.Add(0x38);
                f.Down.Add(0xC0);
                f.Input.Poll();
                Require(f.Calls.Count == 1 && f.Calls[0] == "togglePause", "Shift must allow tilde but suppress shifted numeric controls");
                f.Down.Remove(0x10);
                f.Input.Poll();
                Require(f.Calls.Count == 1, "releasing Shift must not issue held numeric commands");
                f.Down.Clear();
                f.Input.Poll();
                f.Tap(0x38);
                Check(f.Calls.Count == 2 && f.Calls[1] == "toggleImmersion",
                    "Shift supports tilde while shifted number presses cannot trigger game controls", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                f.AfterDispatch = delegate(string command) { f.Input.UpdateState(false, false); };
                f.Down.Add(0x35);
                f.Down.Add(0x36);
                f.Input.Poll();
                Check(f.Calls.Count == 1 && f.Calls[0] == "seekBackward",
                    "disabling hotkeys inside dispatch stops later commands from the same poll", results);
            }

            using (Fixture f = new Fixture())
            {
                f.Input.Poll();
                f.Input.Dispose();
                f.Down.Add(0x35);
                f.Input.Poll();
                Check(f.Calls.Count == 0 && !f.Input.IsGameForeground && !f.Input.ShouldUseFallbackForForeground(),
                    "disposed game fallback ignores input and stops backend selection", results);
            }

            return results;
        }

        private static void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
        }

        private static void Check(bool condition, string name, List<object> results)
        {
            Require(condition, name);
            results.Add(new { name = name, ok = true });
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly HashSet<int> Down = new HashSet<int>();
            internal readonly List<string> Calls = new List<string>();
            internal readonly GameInputFallback Input;
            internal uint Foreground = 1;
            internal Action<string> AfterDispatch;

            internal Fixture()
            {
                Input = new GameInputFallback(delegate(string command)
                {
                    Calls.Add(command);
                    if (AfterDispatch != null) AfterDispatch(command);
                }, delegate(uint pid) { return pid == 1; },
                delegate(int key) { return Down.Contains(key); },
                delegate { return Foreground; }, false);
            }

            internal void Tap(int key)
            {
                Down.Add(key);
                Input.Poll();
                Down.Remove(key);
                Input.Poll();
            }

            public void Dispose() { Input.Dispose(); }
        }
    }
}
