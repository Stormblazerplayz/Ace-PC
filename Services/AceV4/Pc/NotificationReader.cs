using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Nimbus.AceV4.Pc
{
    public sealed record PcNotification(string Id, string App, string Title, string Body, DateTime When)
    {
        /// <summary>Messages, reminders, failures and security notes need you. Promos and "now playing" don't.</summary>
        public bool NeedsAttention
        {
            get
            {
                string a = App.ToLowerInvariant();
                string t = (Title + " " + Body).ToLowerInvariant();
                if (Regex.IsMatch(t, @"\b(urgent|failed|failure|error|missed call|mentioned you|replied|reminder|due|expires?|expiring|security|threat|virus|restart required|restart to|action needed|action required|verify|low battery|disk.*full|out of space|declined|overdue|tomorrow|today at)\b")) return true;
                if (Regex.IsMatch(a, @"discord|teams|outlook|mail|whatsapp|messenger|telegram|signal|slack|calendar|security|defender|windows update|github|zoom|messages")) return true;
                return false;
            }
        }

        /// <summary>One line, with sign-in codes hidden so Ace never reads them out.</summary>
        public string Line
        {
            get
            {
                string text = string.IsNullOrWhiteSpace(Body) ? Title : Title + ": " + Body;
                text = Regex.Replace(text, @"\b\d{4,8}\b", "••••");
                text = Regex.Replace(text, @"\s+", " ").Trim();
                if (text.Length > 120) text = text[..120] + "…";
                return App + " · " + text;
            }
        }
    }

    /// <summary>
    /// Reads the notifications Windows kept (the ones in the notification center).
    /// First it asks Windows politely (Settings > Privacy > Notifications must allow it);
    /// if that isn't allowed for this app, it reads Windows' own notification list file, read-only.
    /// Everything stays on the PC.
    /// </summary>
    public static class NotificationReader
    {
        public static string LastProblem { get; private set; } = "";

        public static async Task<List<PcNotification>> RecentAsync(TimeSpan within)
        {
            var since = DateTime.Now - within;
            var list = await FromListenerAsync().ConfigureAwait(false);
            if (list == null) list = FromDatabase();
            return list.Where(n => n.When >= since)
                       .GroupBy(n => n.Id).Select(g => g.First())
                       .OrderByDescending(n => n.When).ToList();
        }

        private static async Task<List<PcNotification>?> FromListenerAsync()
        {
            try
            {
                var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                var access = await listener.RequestAccessAsync();
                if (access != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed) return null;
                var items = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
                var list = new List<PcNotification>();
                foreach (var n in items)
                {
                    try
                    {
                        string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "An app";
                        var binding = n.Notification?.Visual?.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
                        var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
                        list.Add(new PcNotification("l" + n.Id, app, texts.FirstOrDefault() ?? "", string.Join(" ", texts.Skip(1)), n.CreationTime.LocalDateTime));
                    }
                    catch { }
                }
                return list;
            }
            catch (Exception ex)
            {
                LastProblem = ex.Message;
                return null;   // unpackaged apps often aren't allowed to use the listener: use the file instead
            }
        }

        private static List<PcNotification> FromDatabase()
        {
            var list = new List<PcNotification>();
            string db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                     "Microsoft", "Windows", "Notifications", "wpndatabase.db");
            if (!File.Exists(db)) { LastProblem = "Windows' notification list wasn't found"; return list; }

            // Windows keeps the file open, so read a copy (with its -wal journal, which holds the newest ones).
            string tmp = Path.Combine(Path.GetTempPath(), "nimbus-wpn-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                string copy = Path.Combine(tmp, "wpn.db");
                CopyShared(db, copy);
                if (File.Exists(db + "-wal")) CopyShared(db + "-wal", copy + "-wal");
                if (File.Exists(db + "-shm")) CopyShared(db + "-shm", copy + "-shm");

                using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT n.Id, h.PrimaryId, n.Payload, n.ArrivalTime
                                    FROM Notification n JOIN NotificationHandler h ON n.HandlerId = h.RecordId
                                    WHERE n.Type = 'toast' ORDER BY n.ArrivalTime DESC LIMIT 200";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    try
                    {
                        string id = "d" + r.GetValue(0);
                        string app = FriendlyApp(r.IsDBNull(1) ? "" : r.GetString(1));
                        string xml = r.IsDBNull(2) ? "" : r.GetValue(2) is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : r.GetValue(2)?.ToString() ?? "";
                        long ft = r.IsDBNull(3) ? 0 : r.GetInt64(3);
                        var when = ft > 0 ? DateTime.FromFileTimeUtc(ft).ToLocalTime() : DateTime.MinValue;
                        var texts = Regex.Matches(xml, @"<text[^>]*>(.*?)</text>", RegexOptions.Singleline)
                                         .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).Trim())
                                         .Where(t => t.Length > 0).ToList();
                        if (texts.Count == 0) continue;
                        list.Add(new PcNotification(id, app, texts[0], string.Join(" ", texts.Skip(1)), when));
                    }
                    catch { }
                }
            }
            catch (Exception ex) { LastProblem = "couldn't read Windows' notification list: " + ex.Message; }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tmp, true); } catch { }
            }
            return list;
        }

        private static void CopyShared(string from, string to)
        {
            using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = File.Create(to);
            src.CopyTo(dst);
        }

        /// <summary>"Microsoft.Windows.SecurityHealth" or "C:\...\Discord.exe" or "com.squirrel.Discord.Discord" to a short name.</summary>
        public static string FriendlyApp(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "An app";
            string s = id;
            if (s.Contains('!')) s = s[..s.IndexOf('!')];
            if (s.Contains('\\')) s = Path.GetFileNameWithoutExtension(s);
            var parts = s.Split('.', '_').Where(p => p.Length > 1 && !Regex.IsMatch(p, @"^(com|microsoft|windows|squirrel|app|8wekyb3d8bbwe|[0-9a-z]{13})$", RegexOptions.IgnoreCase)).ToList();
            string name = parts.LastOrDefault() ?? s;
            if (id.Contains("SecurityHealth", StringComparison.OrdinalIgnoreCase) || id.Contains("Defender", StringComparison.OrdinalIgnoreCase)) return "Windows Security";
            if (id.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase)) return "Windows Update";
            if (id.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
            if (id.Contains("Edge", StringComparison.OrdinalIgnoreCase)) return "Edge";
            name = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            return name.Length > 24 ? name[..24] : name;
        }
    }
}
