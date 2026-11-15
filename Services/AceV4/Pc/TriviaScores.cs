using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nimbus.AceV4.Pc
{
    /// <summary>One finished (or stopped) trivia game.</summary>
    public sealed class TriviaResult
    {
        public DateTime When { get; set; } = DateTime.Now;
        /// <summary>"" for mixed trivia, or "space", "animals"…</summary>
        public string Category { get; set; } = "";
        public int Score { get; set; }
        public int OutOf { get; set; }
        /// <summary>Most right answers in a row during the game.</summary>
        public int Streak { get; set; }
        /// <summary>All the questions were played (stopped games don't count for high scores).</summary>
        public bool Full { get; set; }
    }

    /// <summary>Trivia high scores and best streaks, saved in trivia-scores.json on this PC.</summary>
    public static class TriviaScores
    {
        private static readonly object Gate = new();
        private static List<TriviaResult>? _all;
        private static string FilePath => Path.Combine(AceV4Config.Folder, "trivia-scores.json");
        public static event Action? Changed;

        private static List<TriviaResult> All
        {
            get
            {
                if (_all != null) return _all;
                try { if (File.Exists(FilePath)) _all = JsonSerializer.Deserialize<List<TriviaResult>>(File.ReadAllText(FilePath)); } catch { }
                return _all ??= new List<TriviaResult>();
            }
        }

        public static string CategoryName(string category) => category.Length == 0 ? "mixed" : category;

        /// <summary>The best full game in a category ("" is mixed), or null if none was finished yet.</summary>
        public static TriviaResult? Best(string category)
        {
            lock (Gate) return BestOf(All, category);
        }

        private static TriviaResult? BestOf(IEnumerable<TriviaResult> list, string category) =>
            list.Where(r => r.Full && r.Category == category && r.OutOf > 0)
                .OrderByDescending(r => (double)r.Score / r.OutOf).ThenByDescending(r => r.Score).ThenBy(r => r.When).FirstOrDefault();

        /// <summary>The best full game in every category that has one, highest first.</summary>
        public static List<TriviaResult> Bests()
        {
            lock (Gate)
                return All.Select(r => r.Category).Distinct().Select(c => BestOf(All, c)).Where(r => r != null).Select(r => r!)
                          .OrderByDescending(r => (double)r.Score / r.OutOf).ThenBy(r => r.Category).ToList();
        }

        public static int BestStreak { get { lock (Gate) return All.Count == 0 ? 0 : All.Max(r => r.Streak); } }
        public static int Games { get { lock (Gate) return All.Count; } }
        public static int TotalRight { get { lock (Gate) return All.Sum(r => r.Score); } }
        public static int TotalAsked { get { lock (Gate) return All.Sum(r => r.OutOf); } }

        /// <summary>Newest first.</summary>
        public static List<TriviaResult> Recent(int n) { lock (Gate) return All.OrderByDescending(r => r.When).Take(n).ToList(); }

        /// <summary>
        /// Saves a game. NewBest is true when a full game beat the old best in its category (FirstBest when it's
        /// the first full game there); NewStreak when the streak beat every earlier game's (and is at least 3).
        /// </summary>
        public static (bool NewBest, bool FirstBest, TriviaResult? OldBest, bool NewStreak) Record(string category, int score, int outOf, int streak, bool full)
        {
            if (outOf <= 0) return (false, false, null, false);
            TriviaResult? old;
            int oldStreak;
            var r = new TriviaResult { Category = category, Score = score, OutOf = outOf, Streak = streak, Full = full };
            lock (Gate)
            {
                old = BestOf(All, category);
                oldStreak = All.Count == 0 ? 0 : All.Max(x => x.Streak);
                All.Add(r);
                // Keep the file small: the best game per category stays, plus the newest 200.
                if (All.Count > 250)
                {
                    var keep = All.Select(x => x.Category).Distinct().Select(c => BestOf(All, c)).Where(x => x != null).Select(x => x!).ToHashSet();
                    var streakGame = All.OrderByDescending(x => x.Streak).First();
                    keep.Add(streakGame);
                    foreach (var x in All.OrderByDescending(x => x.When).Take(200)) keep.Add(x);
                    _all = All.Where(keep.Contains).OrderBy(x => x.When).ToList();
                }
                Save();
            }
            Changed?.Invoke();
            bool first = full && old == null;
            bool newBest = full && old != null && ((double)score / outOf > (double)old.Score / old.OutOf + 1e-9 ||
                                                   (Math.Abs((double)score / outOf - (double)old.Score / old.OutOf) < 1e-9 && score > old.Score));
            return (newBest, first, old, streak >= 3 && streak > oldStreak);
        }

        public static int Clear()
        {
            int n;
            lock (Gate) { n = All.Count; All.Clear(); Save(); }
            Changed?.Invoke();
            return n;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AceV4Config.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(All));
            }
            catch { }
        }
    }
}
