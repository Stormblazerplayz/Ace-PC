using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Nimbus.Services
{
    /// <summary>
    /// The one place pages share things: live stats, the alert count and "go to this tab" requests.
    /// </summary>
    public static class Hub
    {
        public static SystemStats Stats { get; } = new SystemStats();

        /// <summary>Ask the main window to open a tab ("glance", "memory", "settings", ...).</summary>
        public static event Action<string>? NavigateRequested;
        public static void Navigate(string page) => NavigateRequested?.Invoke(page);

        public static int AlertCount { get; private set; }
        public static event Action? AlertsChanged;
        public static void SetAlertCount(int count)
        {
            AlertCount = count;
            AlertsChanged?.Invoke();
        }

        /// <summary>The Glance "Clear alerts" button uses this; the Alerts tab listens.</summary>
        public static event Action? ClearAlertsRequested;
        public static void ClearAlerts() => ClearAlertsRequested?.Invoke();

        private static Timer? _timer;
        private static int _busy;

        /// <summary>Starts sampling once a second. Safe to call more than once.</summary>
        public static void Start()
        {
            if (_timer != null) return;
            var ui = Application.Current.Dispatcher;
            Task.Run(Stats.OpenSensors);
            _timer = new Timer(_ =>
            {
                if (Interlocked.Exchange(ref _busy, 1) == 1) return;   // last reading still running
                try
                {
                    Stats.Sample();
                    ui.BeginInvoke(new Action(Stats.Notify));
                }
                catch { /* never let a reading crash the app */ }
                finally { Interlocked.Exchange(ref _busy, 0); }
            }, null, 0, 1000);
        }

        public static void Shutdown()
        {
            _timer?.Dispose();
            _timer = null;
            Stats.Dispose();
        }
    }
}
