using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nimbus.AceV4.Pc
{
    /// <summary>One line of the "What's new" notes.</summary>
    public sealed record WhatsNewNote(string Title, string Text);

    /// <summary>
    /// The "What's new" notes that come with each version (Data/whats-new.json, keyed by version like "4.1.0").
    /// After an update Ace shows the notes for every version since the one you had.
    /// </summary>
    public static class WhatsNew
    {
        public static string DataPath => Path.Combine(AppContext.BaseDirectory, "Data", "whats-new.json");

        /// <summary>Every version's notes, newest version first. Empty if the file is missing or broken.</summary>
        public static List<(Version Version, string Name, List<WhatsNewNote> Notes)> Load(string? path = null)
        {
            var list = new List<(Version, string, List<WhatsNewNote>)>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path ?? DataPath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var v = AutoUpdater.ParseVersion(prop.Name);
                    if (v == null || prop.Value.ValueKind != JsonValueKind.Array) continue;
                    var notes = new List<WhatsNewNote>();
                    foreach (var n in prop.Value.EnumerateArray())
                    {
                        if (n.ValueKind == JsonValueKind.String) { notes.Add(new WhatsNewNote(n.GetString() ?? "", "")); continue; }
                        if (n.ValueKind != JsonValueKind.Object) continue;
                        string title = n.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        string text = n.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "";
                        if (title.Length > 0 || text.Length > 0) notes.Add(new WhatsNewNote(title, text));
                    }
                    if (notes.Count > 0) list.Add((v, AutoUpdater.Text(v), notes));
                }
            }
            catch { }
            return list.OrderByDescending(e => e.Item1).ToList();
        }

        /// <summary>The notes for versions newer than from, up to and including to (newest first).</summary>
        public static List<(Version Version, string Name, List<WhatsNewNote> Notes)> Between(string from, string to, string? path = null)
        {
            var a = AutoUpdater.ParseVersion(from);
            var b = AutoUpdater.ParseVersion(to);
            if (b == null) return new();
            return Load(path).Where(e => (a == null || e.Version > a) && e.Version <= b).ToList();
        }

        /// <summary>The notes for one version (a beta shows its full version's notes if it has none of its own).</summary>
        public static List<WhatsNewNote> For(string version, string? path = null)
        {
            var v = AutoUpdater.ParseVersion(version);
            if (v == null) return new();
            var all = Load(path);
            var exact = all.FirstOrDefault(e => e.Version == v);
            if (exact.Notes != null) return exact.Notes;
            var final = new Version(v.Major, v.Minor, Math.Max(0, v.Build), AutoUpdater.FinalRevision);
            return all.FirstOrDefault(e => e.Version == final).Notes ?? new();
        }
    }
}
