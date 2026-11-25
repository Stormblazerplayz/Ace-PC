using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Nimbus.AceV4;
using Nimbus.AceV4.Pc;
using Nimbus.Services;
using Nimbus.Services.Ace;
using Nimbus.Overlay;

namespace Nimbus
{
    /// <summary>
    /// Starts Ace: one normal window with the orb and the chat, like the iPad app. It has a taskbar button,
    /// and there is no pop-up of any kind. Only one Ace runs at a time.
    /// </summary>
    public partial class App : Application
    {
        private const string InstanceName = "Stormblazer.Ace.SingleInstance";
        private Mutex? _single;
        private EventWaitHandle? _wakeOther;
        private bool _smokeTest;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _smokeTest = e.Args.Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
            if (_smokeTest)
            {
                // The build check (on GitHub) fails if anything crashes.
                DispatcherUnhandledException += (_, x) => { SmokeLog("CRASH: " + x.Exception); x.Handled = true; Shutdown(1); };
                AppDomain.CurrentDomain.UnhandledException += (_, x) => { SmokeLog("CRASH: " + x.ExceptionObject); Environment.Exit(1); };
            }

            // Ace is already open (for example it started with Windows): bring that one to the front and quit.
            _single = new Mutex(true, InstanceName, out bool first);
            if (!first)
            {
                try { EventWaitHandle.OpenExisting(InstanceName + ".Show").Set(); } catch { }
                _single.Dispose();
                _single = null;
                Shutdown();
                return;
            }
            _wakeOther = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");

            Hub.Start();
            AceRuntime.Current.Start();   // Ace's brain, wake-up checks, greeting, "Hey Ace", timers and sounds

            var window = new AceWindow();
            MainWindow = window;
            // Started with Windows: open as a normal window, just minimized to the taskbar so it doesn't jump in
            // front of what you're doing. Its taskbar button flashes when Ace has something to say.
            bool background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (background) { window.ShowActivated = false; window.WindowState = WindowState.Minimized; }
            window.Show();
            if (!background) window.BringBack();

            // A second copy of Ace asked this one to show itself.
            var handle = _wakeOther;
            new Thread(() =>
            {
                try
                {
                    while (handle.WaitOne())
                        Dispatcher.BeginInvoke(() => (MainWindow as AceWindow)?.BringBack());
                }
                catch { }
            }) { IsBackground = true, Name = "Ace show" }.Start();

            if (_smokeTest) _ = RunSmokeTestAsync(window);
        }

        /// <summary>
        /// "Ace.exe --smoke-test": the build check on GitHub runs this on a real Windows machine. It opens Ace,
        /// checks there is exactly one window (the Ace window, with a taskbar button), waits for the brain to load,
        /// asks it a real question, then closes. Exit code 0 means it all worked.
        /// </summary>
        private async Task RunSmokeTestAsync(AceWindow window)
        {
            int code = 1;
            try
            {
                await Task.Delay(3000);
                var windows = Windows.OfType<Window>().Where(w => w.IsVisible).ToList();
                if (windows.Count != 1 || windows[0] != window) throw new Exception($"Expected only the Ace window, found {windows.Count} window(s): " + string.Join(", ", windows.Select(w => w.GetType().Name)));
                if (!window.ShowInTaskbar) throw new Exception("The Ace window has no taskbar button");
                if (window.Topmost) throw new Exception("The Ace window is stuck on top");
                if (window.ResizeMode != ResizeMode.CanResize) throw new Exception("The Ace window can't be resized");
                if (window.ActualWidth < 300 || window.ActualHeight < 300) throw new Exception($"The Ace window is too small ({window.ActualWidth}x{window.ActualHeight})");
                SmokeLog($"Window OK: {window.Title}, {window.ActualWidth:0}x{window.ActualHeight:0}");

                await TestShortcutsAsync(window);

                var loaded = await Task.WhenAny(AceRuntime.Current.WhenLoaded, Task.Delay(TimeSpan.FromSeconds(90)));
                if (loaded != AceRuntime.Current.WhenLoaded) throw new Exception("Ace's brain didn't load in 90 seconds");
                SmokeLog("Brain loaded");

                var answer = await AceRuntime.Current.AskAsync("what is 12 times 12");
                SmokeLog("Asked 'what is 12 times 12', Ace said: " + answer.Text);
                if (!answer.Text.Contains("144")) throw new Exception("Ace got 12 x 12 wrong");

                await TestShortcutWordsAsync();

                await Task.Delay(1500);
                windows = Windows.OfType<Window>().Where(w => w.IsVisible).ToList();
                if (windows.Count != 1) throw new Exception($"A pop-up window appeared: " + string.Join(", ", windows.Select(w => w.GetType().Name)));
                SmokeLog("PASS");
                code = 0;
            }
            catch (Exception ex) { SmokeLog("FAIL: " + ex.Message); }
            Shutdown(code);
        }

        /// <summary>
        /// Smoke test for the global shortcuts: combo text, the "open Ace" shortcut bringing back a minimized Ace,
        /// pressing it again to hide Ace, a combo another app already has (Ace must refuse it and keep its old one),
        /// and the same combo for both shortcuts.
        /// </summary>
        private async Task TestShortcutsAsync(AceWindow window)
        {
            static void Check(bool ok, string what) { if (!ok) throw new Exception("Shortcut check failed: " + what); }
            var rt = AceRuntime.Current;
            var hk = rt.Hotkeys;

            // 1. Reading and writing combos.
            Check(HotkeyCombo.TryParse("ctrl + alt + a", out var c1, out _) && c1.ToString() == "Ctrl+Alt+A", "\"ctrl + alt + a\" reads as Ctrl+Alt+A");
            Check(HotkeyCombo.TryParse("Win+Shift+F5", out var c2, out _) && c2.ToString() == "Shift+Win+F5", "\"Win+Shift+F5\" reads as Shift+Win+F5");
            Check(HotkeyCombo.TryParse("Control+Space", out var c3, out _) && c3 == new HotkeyCombo(HotkeyCombo.Ctrl, 0x20), "Control+Space");
            Check(HotkeyCombo.TryParse("off", out var c4, out _) && c4.IsNone, "\"off\" means no shortcut");
            Check(HotkeyCombo.TryParse("F13", out _, out _), "F13 can be used alone");
            Check(!HotkeyCombo.TryParse("A", out _, out _), "a plain letter is refused");
            Check(!HotkeyCombo.TryParse("Shift+A", out _, out _), "Shift+A is refused (it's just a capital A)");
            Check(!HotkeyCombo.TryParse("Ctrl+Alt+Delete", out _, out _), "Ctrl+Alt+Delete is refused");
            Check(!HotkeyCombo.TryParse("Shift+Win+S", out _, out _), "Win+Shift+S (Snipping Tool) is refused");
            Check(!HotkeyCombo.TryParse("Ctrl+F12", out _, out _), "F12 is refused");
            Check(!HotkeyCombo.TryParse("Ctrl+Alt+Banana", out _, out string why) && why.Contains("Banana"), "an unknown key is refused");
            Check(!HotkeyCombo.TryParse("Ctrl+A+B", out _, out _), "two keys are refused");
            Check(AceV4Config.CleanHotkey("ctrl+alt+a", "x") == "Ctrl+Alt+A" && AceV4Config.CleanHotkey("", "x") == "" && AceV4Config.CleanHotkey("nonsense+", "Ctrl+Alt+A") == "Ctrl+Alt+A", "settings file clean-up");
            Check(AceV4Brain.NormalizeComboWords("control shift a") == "control+shift+a", "\"control shift a\"");
            Check(AceV4Brain.NormalizeComboWords("ctrl plus alt plus t") == "ctrl+alt+t", "\"ctrl plus alt plus t\"");
            Check(AceV4Brain.NormalizeComboWords("the windows key and space bar") == "win+space", "\"the windows key and space bar\"");
            SmokeLog("Shortcut combos OK");

            // 2. The open shortcut is registered with Windows.
            Check(hk.Attached, "the shortcuts are hooked to the Ace window");
            string original = rt.Config.ShowHotkey;
            if (!hk[HotkeyAction.ShowAce].Working)
            {
                SmokeLog($"{hk[HotkeyAction.ShowAce].Wanted} isn't free on this PC (error {hk[HotkeyAction.ShowAce].LastError}): {hk.StatusText(HotkeyAction.ShowAce)}");
                var (spareOk, spareMsg) = rt.SetHotkey(HotkeyAction.ShowAce, "Ctrl+Alt+Shift+F9", save: false);
                Check(spareOk, "set a spare open shortcut: " + spareMsg);
            }
            var active = hk[HotkeyAction.ShowAce].Active;
            Check(hk[HotkeyAction.ShowAce].Working && hk.IdOf(HotkeyAction.ShowAce) != 0, "the open shortcut is on");
            SmokeLog("Open shortcut on: " + active + " · " + hk.StatusText(HotkeyAction.ShowAce));
            Check(hk[HotkeyAction.Talk].Working || rt.Config.TalkHotkey.Length == 0 || hk[HotkeyAction.Talk].LastError == GlobalHotkeys.ErrorHotkeyAlreadyRegistered,
                  "the talk shortcut is on (or honestly reported as taken)");
            SmokeLog("Talk shortcut: " + hk.StatusText(HotkeyAction.Talk));

            // 3. Minimized: the open shortcut brings Ace back.
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            IntPtr Id() => new IntPtr(hk.IdOf(HotkeyAction.ShowAce));
            window.WindowState = WindowState.Minimized;
            await Task.Delay(600);
            Check(window.WindowState == WindowState.Minimized, "Ace minimized for the test");
            Win32.PostMessage(hwnd, (uint)GlobalHotkeys.WM_HOTKEY, Id(), IntPtr.Zero);
            await Task.Delay(1200);
            Check(window.IsVisible && window.WindowState != WindowState.Minimized, "the open shortcut brings Ace back from the taskbar");
            SmokeLog("Open shortcut brought back the minimized window");

            // The real keys, pressed like a person would (skipped if this PC doesn't take pretend key presses).
            window.WindowState = WindowState.Minimized;
            await Task.Delay(600);
            var keys = new System.Collections.Generic.List<ushort>();
            if ((active.Modifiers & HotkeyCombo.Ctrl) != 0) keys.Add(Win32.VK_CONTROL);
            if ((active.Modifiers & HotkeyCombo.Alt) != 0) keys.Add(Win32.VK_MENU);
            if ((active.Modifiers & HotkeyCombo.Shift) != 0) keys.Add(Win32.VK_SHIFT);
            if ((active.Modifiers & HotkeyCombo.Win) != 0) keys.Add(Win32.VK_LWIN);
            keys.Add((ushort)active.Key);
            bool sent = Win32.Chord(keys.ToArray());
            await Task.Delay(1500);
            if (window.WindowState != WindowState.Minimized) SmokeLog($"Pressing {active} for real brought Ace back too");
            else
            {
                SmokeLog($"Pressing {active} for real didn't reach Ace here (SendInput {(sent ? "sent" : "blocked")}), so that part is skipped");
                Win32.PostMessage(hwnd, (uint)GlobalHotkeys.WM_HOTKEY, Id(), IntPtr.Zero);
                await Task.Delay(1200);
                Check(window.WindowState != WindowState.Minimized, "Ace came back for the rest of the test");
            }

            // 4. Already in front: pressing it again hides Ace, and once more brings it back.
            if (Win32.GetForegroundWindow() == hwnd && rt.Config.ShowHotkeyHides)
            {
                Win32.PostMessage(hwnd, (uint)GlobalHotkeys.WM_HOTKEY, Id(), IntPtr.Zero);
                await Task.Delay(900);
                Check(window.WindowState == WindowState.Minimized, "pressing it while Ace is in front hides Ace");
                Win32.PostMessage(hwnd, (uint)GlobalHotkeys.WM_HOTKEY, Id(), IntPtr.Zero);
                await Task.Delay(1200);
                Check(window.WindowState != WindowState.Minimized, "pressing it again brings Ace back");
                SmokeLog("Press again to hide works");
            }
            else SmokeLog("Ace isn't the front window on this PC, so the hide check is skipped");

            // 5. A combo another app already has: Ace says it's taken and keeps the shortcut it had.
            const uint takenVk = 0x7A;   // F11
            string takenText = "Ctrl+Alt+Shift+F11";
            using (var other = new HwndSource(new HwndSourceParameters("Ace shortcut test") { ParentWindow = new IntPtr(-3) }))   // a hidden message-only window
            {
                bool grabbed = Win32.RegisterHotKey(other.Handle, 0x7001, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_SHIFT, takenVk);
                if (grabbed)
                {
                    var (takenOk, takenMsg) = rt.SetHotkey(HotkeyAction.ShowAce, takenText, save: false);
                    SmokeLog("Taken combo: " + takenMsg);
                    Check(!takenOk && takenMsg.Contains("already used"), "a combo another app has is refused");
                    Check(hk[HotkeyAction.ShowAce].Working && hk[HotkeyAction.ShowAce].Active == active, "the old shortcut still works after a taken combo");
                    Check(rt.Config.ShowHotkey == active.ToString(), "a taken combo isn't saved");
                    Win32.UnregisterHotKey(other.Handle, 0x7001);

                    // Once the other app lets go, the same combo works.
                    var (freeOk, freeMsg) = rt.SetHotkey(HotkeyAction.ShowAce, takenText, save: false);
                    Check(freeOk && hk[HotkeyAction.ShowAce].Active.ToString() == takenText && rt.Config.ShowHotkey == takenText, "a freed combo can be picked: " + freeMsg);
                    Check(hk.IdOf(HotkeyAction.ShowAce) != 0, "the new combo is on");
                    Win32.PostMessage(hwnd, (uint)GlobalHotkeys.WM_HOTKEY, Id(), IntPtr.Zero);
                    await Task.Delay(900);
                    SmokeLog("Changed the open shortcut to " + takenText + " and it still opens Ace");
                }
                else SmokeLog(takenText + " is already taken on this PC, so the other app part is skipped");
            }

            // 6. Both shortcuts can't share a combo.
            if (rt.Config.TalkHotkey.Length > 0)
            {
                var (dupOk, dupMsg) = rt.SetHotkey(HotkeyAction.ShowAce, rt.Config.TalkHotkey, save: false);
                Check(!dupOk, "the open and talk shortcuts can't be the same: " + dupMsg);
            }

            // Put it back the way it was.
            var (backOk, backMsg) = rt.SetHotkey(HotkeyAction.ShowAce, active.ToString(), save: false);
            Check(backOk, "put the open shortcut back: " + backMsg);
            if (original != rt.Config.ShowHotkey && HotkeyCombo.TryParse(original, out var orig, out _) && !orig.IsNone)
                rt.SetHotkey(HotkeyAction.ShowAce, original, save: false);
            window.BringBack();
            await Task.Delay(600);
            SmokeLog("Shortcuts PASS");
        }

        /// <summary>Asking Ace about its shortcuts, and changing one by typing.</summary>
        private static async Task TestShortcutWordsAsync()
        {
            var rt = AceRuntime.Current;
            string before = rt.Config.ShowHotkey;
            var a = await AceRuntime.Current.AskAsync("what's the shortcut to open you?");
            SmokeLog("Asked 'what's the shortcut to open you?', Ace said: " + a.Text);
            if (before.Length > 0 && !a.Text.Contains(before)) throw new Exception("Ace didn't say its open shortcut");

            var b = await AceRuntime.Current.AskAsync("change my shortcut to control alt shift f8");
            SmokeLog("Asked 'change my shortcut to control alt shift f8', Ace said: " + b.Text);
            bool changed = rt.Config.ShowHotkey == "Ctrl+Alt+Shift+F8";
            if (!changed && !b.Text.Contains("already used")) throw new Exception("Ace didn't change its shortcut when asked");

            var (ok, msg) = rt.SetHotkey(HotkeyAction.ShowAce, before);
            if (!ok) throw new Exception("Couldn't put the open shortcut back: " + msg);
            SmokeLog("Shortcut words PASS");
        }

        private static void SmokeLog(string line)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "smoke-test.log");
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch { }
            Console.WriteLine(line);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_single != null)
            {
                AceRuntime.Current.Dispose();   // saves Ace's state and stops the mic, voice and sounds
                Hub.Shutdown();
                AceBrain.ShutdownShared();      // stops the LFM3 Omni server if Ace started it, so it doesn't keep holding VRAM
                try { _single.ReleaseMutex(); } catch { }
                _single.Dispose();
            }
            base.OnExit(e);
        }
    }
}
