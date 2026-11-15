using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Nimbus.AceV4;
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

                var loaded = await Task.WhenAny(AceRuntime.Current.WhenLoaded, Task.Delay(TimeSpan.FromSeconds(90)));
                if (loaded != AceRuntime.Current.WhenLoaded) throw new Exception("Ace's brain didn't load in 90 seconds");
                SmokeLog("Brain loaded");

                var answer = await AceRuntime.Current.AskAsync("what is 12 times 12");
                SmokeLog("Asked 'what is 12 times 12', Ace said: " + answer.Text);
                if (!answer.Text.Contains("144")) throw new Exception("Ace got 12 x 12 wrong");

                await Task.Delay(1500);
                windows = Windows.OfType<Window>().Where(w => w.IsVisible).ToList();
                if (windows.Count != 1) throw new Exception($"A pop-up window appeared: " + string.Join(", ", windows.Select(w => w.GetType().Name)));
                SmokeLog("PASS");
                code = 0;
            }
            catch (Exception ex) { SmokeLog("FAIL: " + ex.Message); }
            Shutdown(code);
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
