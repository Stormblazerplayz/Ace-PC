using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Nimbus.AceV4
{
    /// <summary>
    /// Ace V4 settings, saved to %LOCALAPPDATA%\Nimbus\ace-v4.json. Every value has a good default,
    /// so the file is optional. The Ace tab and Settings change these live.
    /// </summary>
    public sealed class AceV4Config
    {
        public static string Folder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nimbus", "ace-v4");

        public static string ConfigPath => Path.Combine(Folder, "ace-v4.json");

        /// <summary>What Ace calls you.</summary>
        public string UserName { get; set; } = "Storm";

        // ---- Wake checks (the eyes) ----
        public bool WakeChecks { get; set; } = true;
        public int WakeMinMinutes { get; set; } = 15;
        public int WakeMaxMinutes { get; set; } = 30;
        /// <summary>Files at least this big count as "large" in the storage check.</summary>
        public int BigFileMb { get; set; } = 1024;
        /// <summary>Minutes with no mouse or keyboard before Ace says you're away.</summary>
        public int AwayMinutes { get; set; } = 5;
        /// <summary>The eyes stay hidden while a full-screen game or video is in front.</summary>
        public bool QuietDuringFullScreen { get; set; } = true;

        // ---- Voice ----
        /// <summary>Say "Hey Ace" (or "Ace") and then your question, hands-free.</summary>
        public bool WakeWord { get; set; } = true;
        /// <summary>Ace reads its answers out loud.</summary>
        public bool SpeakReplies { get; set; } = true;
        /// <summary>Windows voice name to use, or empty for the default one.</summary>
        public string TtsVoice { get; set; } = "";
        public int TtsRate { get; set; } = 1;
        /// <summary>Seconds of quiet that end a dictation.</summary>
        public double DictationSilenceSeconds { get; set; } = 1.3;
        public double DictationMaxSeconds { get; set; } = 20;
        /// <summary>"auto" (LFM3 Omni if it's running, Windows otherwise), "windows" or "omni".</summary>
        public string DictationEngine { get; set; } = "auto";
        /// <summary>Ace's sounds follow its eyes left and right.</summary>
        public bool SpatialSound { get; set; } = true;

        // ---- Sounds ----
        public bool Sounds { get; set; } = true;
        /// <summary>0 to 1.</summary>
        public double SoundVolume { get; set; } = 0.8;

        // ---- Everything else ----
        /// <summary>Start Nimbus when you sign in to Windows, so Ace can greet you each morning.</summary>
        public bool StartWithWindows { get; set; } = true;
        /// <summary>How long you want to sleep. Ace's bedtime tip works back from your usual wake-up time.</summary>
        public double SleepGoalHours { get; set; } = 9.5;
        /// <summary>For the weather card. Empty = Ace asks which city.</summary>
        public string HomeCity { get; set; } = "";
        /// <summary>"google", "bing", "duckduckgo" or "brave": where "search for ..." opens in the browser.</summary>
        public string SearchEngine { get; set; } = "google";
        /// <summary>Let LFM3 Omni / Qwen help with wording when one of them is running.</summary>
        public bool UseLlmHelper { get; set; } = true;
        /// <summary>When Ace isn't busy, its eyes follow your mouse around the screen.</summary>
        public bool EyesFollowMouse { get; set; } = true;
        /// <summary>Ace learns which apps you open at what times and offers to open them.</summary>
        public bool AppSuggestions { get; set; } = true;
        /// <summary>A PC health report card every Sunday.</summary>
        public bool WeeklyReport { get; set; } = true;
        /// <summary>Check GitHub for a new Ace (on start and every 6 hours) and install it by itself.</summary>
        public bool AutoUpdate { get; set; } = true;
        /// <summary>Minutes in one focus round of the focus timer.</summary>
        public int FocusMinutes { get; set; } = 25;
        /// <summary>The thinking loop stops after this many passes or seconds, whichever comes first.</summary>
        public int MaxThinkPasses { get; set; } = 8;
        public int MaxThinkSeconds { get; set; } = 60;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static AceV4Config Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return Clean(JsonSerializer.Deserialize<AceV4Config>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AceV4Config());
            }
            catch { }
            return new AceV4Config();
        }

        private static AceV4Config Clean(AceV4Config c)
        {
            c.WakeMinMinutes = Math.Clamp(c.WakeMinMinutes, 1, 240);
            c.WakeMaxMinutes = Math.Clamp(c.WakeMaxMinutes, c.WakeMinMinutes, 480);
            c.SoundVolume = Math.Clamp(c.SoundVolume, 0, 1);
            c.SleepGoalHours = Math.Clamp(c.SleepGoalHours, 5, 13);
            c.MaxThinkPasses = Math.Clamp(c.MaxThinkPasses, 1, 30);
            c.MaxThinkSeconds = Math.Clamp(c.MaxThinkSeconds, 5, 600);
            c.DictationSilenceSeconds = Math.Clamp(c.DictationSilenceSeconds, 0.5, 5);
            c.DictationMaxSeconds = Math.Clamp(c.DictationMaxSeconds, 3, 120);
            c.BigFileMb = Math.Clamp(c.BigFileMb, 50, 100_000);
            c.AwayMinutes = Math.Clamp(c.AwayMinutes, 1, 120);
            c.FocusMinutes = Math.Clamp(c.FocusMinutes, 5, 120);
            if (string.IsNullOrWhiteSpace(c.UserName)) c.UserName = "Storm";
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch { }
        }
    }

    /// <summary>Things Ace remembers between runs that aren't settings.</summary>
    public sealed class AceV4State
    {
        public static string StatePath => Path.Combine(AceV4Config.Folder, "ace-v4-state.json");

        /// <summary>The day ("yyyy-MM-dd") Ace last said good morning.</summary>
        public string LastGreetedDay { get; set; } = "";
        /// <summary>The night ("yyyy-MM-dd") Ace last gave a bedtime nudge.</summary>
        public string LastBedtimeNudge { get; set; } = "";
        public DateTime LastWakeCheck { get; set; }
        /// <summary>Notification ids Ace already told you about, so it doesn't repeat them.</summary>
        public List<string> SeenNotifications { get; set; } = new();
        /// <summary>Big files Ace already told you about.</summary>
        public List<string> SeenBigFiles { get; set; } = new();
        /// <summary>Documents seen in the last check, so new ones can be pointed out.</summary>
        public List<string> KnownDocuments { get; set; } = new();
        /// <summary>Findings saved while you were away, shown when you come back.</summary>
        public List<string> AwayFindings { get; set; } = new();
        /// <summary>The Sunday the last automatic weekly report was shown (yyyy-MM-dd).</summary>
        public string LastWeeklyReport { get; set; } = "";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static AceV4State Load()
        {
            try
            {
                if (File.Exists(StatePath))
                    return JsonSerializer.Deserialize<AceV4State>(File.ReadAllText(StatePath), JsonOptions) ?? new AceV4State();
            }
            catch { }
            return new AceV4State();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AceV4Config.Folder);
                if (SeenNotifications.Count > 500) SeenNotifications.RemoveRange(0, SeenNotifications.Count - 500);
                if (SeenBigFiles.Count > 500) SeenBigFiles.RemoveRange(0, SeenBigFiles.Count - 500);
                if (KnownDocuments.Count > 3000) KnownDocuments.RemoveRange(0, KnownDocuments.Count - 3000);
                if (AwayFindings.Count > 30) AwayFindings.RemoveRange(0, AwayFindings.Count - 30);
                string tmp = StatePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(tmp, StatePath, true);
            }
            catch { }
        }
    }
}
