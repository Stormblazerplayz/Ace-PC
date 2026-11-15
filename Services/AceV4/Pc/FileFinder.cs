using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;

namespace Nimbus.AceV4.Pc
{
    /// <summary>What someone asked to find, pulled apart.</summary>
    public sealed class FileQuery
    {
        public List<string> Words = new();
        public HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase);
        public string KindName = "";
        public DateTime? From, To;
        public string? Folder;
        public bool Newest;

        private static readonly Dictionary<string, string[]> Kinds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = new[] { ".pdf" },
            ["doc"] = new[] { ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".pdf" },
            ["document"] = new[] { ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".pdf", ".pages" },
            ["word"] = new[] { ".doc", ".docx" },
            ["spreadsheet"] = new[] { ".xls", ".xlsx", ".csv", ".ods" },
            ["excel"] = new[] { ".xls", ".xlsx", ".csv" },
            ["slides"] = new[] { ".ppt", ".pptx", ".odp", ".key" },
            ["presentation"] = new[] { ".ppt", ".pptx", ".odp", ".key" },
            ["picture"] = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic" },
            ["image"] = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic" },
            ["photo"] = new[] { ".png", ".jpg", ".jpeg", ".heic", ".webp" },
            ["video"] = new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv" },
            ["clip"] = new[] { ".mp4", ".mkv", ".mov", ".webm" },
            ["song"] = new[] { ".mp3", ".wav", ".flac", ".m4a", ".ogg" },
            ["music"] = new[] { ".mp3", ".wav", ".flac", ".m4a", ".ogg" },
            ["audio"] = new[] { ".mp3", ".wav", ".flac", ".m4a", ".ogg" },
            ["zip"] = new[] { ".zip", ".rar", ".7z", ".tar", ".gz" },
            ["blender"] = new[] { ".blend" },
            ["model"] = new[] { ".stl", ".obj", ".fbx", ".3mf", ".blend", ".glb", ".gltf", ".step" },
            ["stl"] = new[] { ".stl" },
            ["3mf"] = new[] { ".3mf" },
            ["code"] = new[] { ".cs", ".py", ".lua", ".js", ".ts", ".cpp", ".h", ".java", ".json", ".xaml" },
            ["script"] = new[] { ".lua", ".py", ".ps1", ".bat", ".js", ".cs" },
            ["installer"] = new[] { ".exe", ".msi" },
            ["exe"] = new[] { ".exe" },
        };

        private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
        {
            "find","search","look","locate","where","file","files","folder","called","named","my","the","a","an","for","is","are","of","from",
            "in","on","that","with","about","i","saved","made","downloaded","show","me","open","get","some","all","recent","latest","newest",
            "last","this","week","today","yesterday","month","pc","computer","drive","please","ace","can","you","which","has","have","it","to",
            "screenshot","screenshots","shot","shots","say","says","saying","said","words","word","text","there","was","were",
        };

        public static FileQuery Parse(string text)
        {
            var q = new FileQuery();
            string t = text.ToLowerInvariant();
            var now = DateTime.Now;
            if (Regex.IsMatch(t, @"\btoday\b")) { q.From = now.Date; }
            else if (Regex.IsMatch(t, @"\byesterday\b")) { q.From = now.Date.AddDays(-1); q.To = now.Date; }
            else if (Regex.IsMatch(t, @"\blast\s+week\b")) { q.From = now.Date.AddDays(-14); q.To = now.Date.AddDays(-7 - (int)now.DayOfWeek + 7); }
            else if (Regex.IsMatch(t, @"\bthis\s+week\b|\bpast\s+week\b|\blast\s+7\s+days\b")) { q.From = now.Date.AddDays(-7); }
            else if (Regex.IsMatch(t, @"\bthis\s+month\b|\bpast\s+month\b")) { q.From = now.Date.AddDays(-31); }
            else if (Regex.IsMatch(t, @"\blast\s+month\b")) { q.From = now.Date.AddDays(-62); q.To = now.Date.AddDays(-28); }
            else
            {
                var m = Regex.Match(t, @"\bon\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b|\bfrom\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b");
                if (m.Success)
                {
                    var w = NaturalTime.Parse(m.Value.Replace("from", "on"), now);
                    if (w.Date != null)
                    {
                        var d = w.Date.Value > now.Date ? w.Date.Value.AddDays(-7) : w.Date.Value;   // "from monday" means the last one
                        q.From = d; q.To = d.AddDays(1);
                    }
                }
            }
            q.Newest = Regex.IsMatch(t, @"\b(latest|newest|last|most recent|recent)\b");

            var folder = Regex.Match(t, @"\b(?:in|on|from)\s+(?:my\s+|the\s+)?(downloads|desktop|documents|pictures|videos|music|onedrive)\b");
            if (folder.Success)
            {
                string f = folder.Groups[1].Value;
                q.Folder = f switch
                {
                    "downloads" => Path.Combine(PcScan.Home, "Downloads"),
                    "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    _ => Environment.GetEnvironmentVariable("OneDrive"),
                };
                t = t.Remove(folder.Index, folder.Length);
            }

            // ".stl files", "pdfs", "pictures"
            foreach (Match m in Regex.Matches(t, @"\.([a-z0-9]{1,5})\b")) q.Extensions.Add("." + m.Groups[1].Value);
            foreach (var word in Regex.Matches(t, @"[a-z0-9]+").Select(m => m.Value))
            {
                string stem = Lexical.Stem(word);
                if (Kinds.TryGetValue(stem, out var exts) || Kinds.TryGetValue(word, out exts))
                {
                    foreach (var e in exts) q.Extensions.Add(e);
                    q.KindName = q.KindName.Length == 0 ? stem : q.KindName;
                    continue;
                }
                if (Filler.Contains(word) || word.Length < 2) continue;
                if (Regex.IsMatch(word, @"^(monday|tuesday|wednesday|thursday|friday|saturday|sunday|days?|ago)$")) continue;
                q.Words.Add(word);
            }
            return q;
        }

        public bool DateOk(DateTime modified) => (From == null || modified >= From) && (To == null || modified < To);
    }

    /// <summary>Finds files and screenshots. Can read the words inside screenshots (Windows' built-in OCR).</summary>
    public static class FileFinder
    {
        public static List<FoundFile> Search(FileQuery q, int take = 12, CancellationToken ct = default)
        {
            var roots = q.Folder != null && Directory.Exists(q.Folder) ? new List<string> { q.Folder } : PcScan.UserFolders();
            var hits = new List<(FoundFile File, double Score)>();
            PcScan.EachFile(roots, f =>
            {
                try
                {
                    if (q.Extensions.Count > 0 && !q.Extensions.Contains(f.Extension)) return;
                    if (!q.DateOk(f.LastWriteTime)) return;
                    double score = 0;
                    if (q.Words.Count > 0)
                    {
                        string name = Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant();
                        string folder = (f.DirectoryName ?? "").ToLowerInvariant();
                        foreach (var w in q.Words)
                        {
                            if (name.Contains(w)) score += 1;
                            else if (folder.Contains(w)) score += 0.4;
                            else if (w.Length >= 5 && Regex.Split(name, @"[^a-z0-9]+").Any(part => part.Length >= 4 && Lexical.Close(part, w))) score += 0.7;
                        }
                        if (score <= 0) return;
                        score /= q.Words.Count;
                    }
                    else score = 0.5;
                    // Newer files win ties.
                    double ageDays = (DateTime.Now - f.LastWriteTime).TotalDays;
                    score += Math.Max(0, 0.15 - ageDays / 365 * 0.15);
                    hits.Add((new FoundFile(f.FullName, f.Length, f.LastWriteTime), score));
                }
                catch { }
            }, 400_000, 15, ct);

            var ordered = q.Newest || q.Words.Count == 0
                ? hits.OrderByDescending(h => h.File.Modified)
                : hits.OrderByDescending(h => Math.Round(h.Score, 2)).ThenByDescending(h => h.File.Modified);
            return ordered.Take(take).Select(h => h.File).ToList();
        }

        // ================= Screenshots =================

        public static List<string> ScreenshotFolders()
        {
            string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string od = Environment.GetEnvironmentVariable("OneDrive") ?? "";
            var list = new List<string>
            {
                Path.Combine(pics, "Screenshots"),
                Path.Combine(vids, "Captures"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShareX", "Screenshots"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                pics,
            };
            if (od.Length > 0) { list.Add(Path.Combine(od, "Pictures", "Screenshots")); list.Add(Path.Combine(od, "Screenshots")); }
            return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

        /// <summary>Every screenshot Ace can find, newest first.</summary>
        public static List<FoundFile> AllScreenshots(int max = 3000)
        {
            var found = new Dictionary<string, FoundFile>(StringComparer.OrdinalIgnoreCase);
            var folders = ScreenshotFolders();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            foreach (var folder in folders)
            {
                // In Desktop and Pictures themselves only files named like screenshots count; in screenshot folders, everything does.
                bool onlyNamed = folder.Equals(desktop, StringComparison.OrdinalIgnoreCase) || folder.Equals(pics, StringComparison.OrdinalIgnoreCase);
                try
                {
                    var opts = new EnumerationOptions { RecurseSubdirectories = !onlyNamed, IgnoreInaccessible = true, MaxRecursionDepth = 3 };
                    foreach (var path in Directory.EnumerateFiles(folder, "*.*", opts))
                    {
                        if (!ImageExt.Contains(Path.GetExtension(path))) continue;
                        string name = Path.GetFileName(path).ToLowerInvariant();
                        if (onlyNamed && !(name.StartsWith("screenshot") || name.StartsWith("screen shot") || name.Contains("capture") || name.StartsWith("snip"))) continue;
                        var fi = new FileInfo(path);
                        found[path] = new FoundFile(path, fi.Length, fi.LastWriteTime);
                        if (found.Count >= max * 2) break;
                    }
                }
                catch { }
            }
            return found.Values.OrderByDescending(f => f.Modified).Take(max).ToList();
        }

        private static string OcrCachePath => Path.Combine(AceV4Config.Folder, "screenshot-text.json");
        private static Dictionary<string, string>? _ocrCache;
        private static readonly SemaphoreSlim OcrGate = new(1, 1);

        /// <summary>
        /// Screenshots that match the words (in the name, or in the picture itself), newest first.
        /// Reads up to ocrBudget new screenshots per search; what it read is saved, so the next search is instant.
        /// </summary>
        public static async Task<List<(FoundFile File, string Why)>> SearchScreenshotsAsync(FileQuery q, int take, Action<string>? progress, CancellationToken ct, int ocrBudget = 150)
        {
            var all = AllScreenshots().Where(f => q.DateOk(f.Modified)).ToList();
            if (q.Words.Count == 0) return all.Take(take).Select(f => (f, "")).ToList();

            await OcrGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                LoadCache();
                var results = new List<(FoundFile, string, double)>();
                int read = 0;
                foreach (var f in all)
                {
                    if (ct.IsCancellationRequested) break;
                    string key = f.Path + "|" + f.Modified.Ticks;
                    if (!_ocrCache!.TryGetValue(key, out var text))
                    {
                        if (read >= ocrBudget) continue;
                        read++;
                        if (read % 10 == 1) progress?.Invoke($"Reading screenshots ({read})…");
                        text = await Ocr.ReadAsync(f.Path).ConfigureAwait(false) ?? "";
                        _ocrCache[key] = text;
                    }
                    string hay = (Path.GetFileNameWithoutExtension(f.Path) + " " + text).ToLowerInvariant();
                    int hit = q.Words.Count(w => hay.Contains(w));
                    if (hit == 0) continue;
                    double score = hit / (double)q.Words.Count;
                    string why = Snippet(text, q.Words);
                    results.Add((f, why, score));
                }
                SaveCache();
                return results.OrderByDescending(r => r.Item3).ThenByDescending(r => r.Item1.Modified)
                              .Take(take).Select(r => (r.Item1, r.Item2)).ToList();
            }
            finally { OcrGate.Release(); }
        }

        private static string Snippet(string text, List<string> words)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string flat = Regex.Replace(text, @"\s+", " ");
            int i = words.Select(w => flat.IndexOf(w, StringComparison.OrdinalIgnoreCase)).Where(x => x >= 0).DefaultIfEmpty(-1).Min();
            if (i < 0) return "";
            int start = Math.Max(0, i - 30);
            int len = Math.Min(flat.Length - start, 90);
            return (start > 0 ? "…" : "") + flat.Substring(start, len).Trim() + (start + len < flat.Length ? "…" : "");
        }

        private static void LoadCache()
        {
            if (_ocrCache != null) return;
            try
            {
                if (File.Exists(OcrCachePath))
                    _ocrCache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(OcrCachePath));
            }
            catch { }
            _ocrCache ??= new Dictionary<string, string>();
        }

        private static void SaveCache()
        {
            try
            {
                if (_ocrCache == null) return;
                if (_ocrCache.Count > 8000)
                    foreach (var k in _ocrCache.Keys.Take(_ocrCache.Count - 8000).ToList()) _ocrCache.Remove(k);
                Directory.CreateDirectory(AceV4Config.Folder);
                File.WriteAllText(OcrCachePath, JsonSerializer.Serialize(_ocrCache));
            }
            catch { }
        }
    }

    /// <summary>Windows' built-in text reader (OCR). Works offline with the language packs Windows already has.</summary>
    public static class Ocr
    {
        private static Windows.Media.Ocr.OcrEngine? _engine;
        private static bool _tried;

        public static async Task<string?> ReadAsync(string path)
        {
            try
            {
                if (!_tried) { _tried = true; _engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages(); }
                if (_engine == null) return null;
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                uint max = Windows.Media.Ocr.OcrEngine.MaxImageDimension;
                var transform = new Windows.Graphics.Imaging.BitmapTransform();
                uint w = decoder.PixelWidth, h = decoder.PixelHeight;
                if (w > max || h > max)
                {
                    double s = Math.Min((double)max / w, (double)max / h);
                    transform.ScaledWidth = (uint)(w * s);
                    transform.ScaledHeight = (uint)(h * s);
                }
                using var bmp = await decoder.GetSoftwareBitmapAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, transform,
                    Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
                var result = await _engine.RecognizeAsync(bmp);
                return result.Text;
            }
            catch { return null; }
        }
    }
}
