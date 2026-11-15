using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace Nimbus.AceV4.Pc
{
    /// <summary>
    /// Windows notifications (the pop-ups in the bottom-right corner that also go to the notification center).
    /// Nimbus isn't installed from the Store, so it registers its own app id under HKEY_CURRENT_USER once
    /// (no admin needed), which is what Windows needs before it shows a desktop app's notification.
    /// </summary>
    public static class Toast
    {
        public const string AppId = "Stormblazer.Nimbus.Ace";
        private static bool _registered;
        private static readonly object Gate = new();

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        private static void Register()
        {
            if (_registered) return;
            lock (Gate)
            {
                if (_registered) return;
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
                    key.SetValue("DisplayName", "Nimbus");
                    string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "nimbus.ico");
                    if (File.Exists(icon)) key.SetValue("IconUri", icon);
                    key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
                }
                catch (Exception ex) { Debug.WriteLine("Toast register: " + ex.Message); }
                try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { }
                _registered = true;
            }
        }

        /// <summary>Shows a notification (silent, since Ace plays its own sound). Never throws; returns false if Windows wouldn't show it.</summary>
        public static bool Show(string title, string body)
        {
            try
            {
                Register();
                string xml =
                    "<toast scenario=\"reminder\"><visual><binding template=\"ToastGeneric\">" +
                    $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>" +
                    "</binding></visual><actions><action content=\"Dismiss\" arguments=\"dismiss\" activationType=\"system\"/></actions>" +
                    "<audio silent=\"true\"/></toast>";
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(xml);
                var toast = new Windows.UI.Notifications.ToastNotification(doc) { ExpirationTime = DateTimeOffset.Now.AddHours(1) };
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Toast: " + ex.Message);
                return false;
            }
        }
    }
}
