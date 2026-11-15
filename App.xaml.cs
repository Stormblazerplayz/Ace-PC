using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Nimbus.AceV4;
using Nimbus.Services;
using Nimbus.Services.Ace;
using Nimbus.Overlay;

namespace Nimbus
{
    /// <summary>Starts Ace: one window with the orb and the chat, like the iPad app. Only one Ace runs at a time.</summary>
    public partial class App : Application
    {
        private const string InstanceName = "Stormblazer.Ace.SingleInstance";
        private Mutex? _single;
        private EventWaitHandle? _wakeOther;
        private AceOverlay? _overlay;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            bool background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (background) { window.ShowActivated = false; window.WindowState = WindowState.Minimized; }
            window.Show();

            // The little pop-up with Ace's eyes, for when Ace talks while its window is hidden or minimized.
            _overlay = new AceOverlay { IsIslanded = () => false };
            window.Closed += (_, _) => { try { _overlay?.Close(); } catch { } };

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
