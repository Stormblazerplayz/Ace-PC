using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nimbus.Services.Calendar;

namespace Nimbus.AceV4
{
    /// <summary>One line in Ace's visible "thinking" list.</summary>
    public sealed record ThoughtStep(string Label, string Detail = "");

    /// <summary>Base for the rich cards Ace can show under an answer.</summary>
    public abstract class AceCard { }

    public sealed class RecipeCard : AceCard
    {
        public string Title = "";
        public string Source = "";
        public string Url = "";
        public string ImageUrl = "";
        public string Yield = "";
        public string TotalTime = "";
        public List<string> Ingredients = new();
        public List<string> Steps = new();
    }

    /// <summary>
    /// Cooking mode: one recipe, one step at a time, with a timer for steps that wait ("Bake for 25 minutes").
    /// The brain and every view of the card share this, so "next" by voice and the Next button stay in step.
    /// </summary>
    public sealed class CookingSession
    {
        public Brain.Recipe Recipe { get; }
        public int Step { get; private set; }
        public DateTime LastUsed { get; private set; } = DateTime.Now;
        public bool Finished { get; private set; }
        /// <summary>Raised (on any thread) when the step changes or cooking ends.</summary>
        public event Action? Changed;

        public CookingSession(Brain.Recipe recipe) { Recipe = recipe; }

        public int Count => Recipe.Steps.Count;
        public string Current => Recipe.Steps[Math.Clamp(Step, 0, Count - 1)];
        public Brain.StepWait? Wait => Brain.StepTimer.Read(Current);
        public bool IsLast => Step >= Count - 1;

        public void GoTo(int index) { Step = Math.Clamp(index, 0, Count - 1); LastUsed = DateTime.Now; Changed?.Invoke(); }
        public bool Next() { if (IsLast) return false; GoTo(Step + 1); return true; }
        public bool Back() { if (Step == 0) return false; GoTo(Step - 1); return true; }
        public void Finish() { Finished = true; LastUsed = DateTime.Now; Changed?.Invoke(); }

        /// <summary>Text for a step: "Step 3 of 7: Bake for 25 minutes."</summary>
        public string Say(int? index = null)
        {
            int i = index ?? Step;
            return $"Step {i + 1} of {Count}: {Recipe.Steps[i]}";
        }
    }

    public sealed class CookingCard : AceCard
    {
        public CookingSession Session = null!;
    }

    /// <summary>Every running timer with a live countdown and its own cancel button.</summary>
    public sealed class TimersCard : AceCard
    {
        public string Title = "Timers";
    }

    /// <summary>The focus timer with a live countdown, round dots and Pause / Skip / Stop buttons.</summary>
    public sealed class FocusCard : AceCard { }

    /// <summary>The weekly PC health report card: a big grade, a grade per part and tips.</summary>
    public sealed class ReportCard : AceCard
    {
        public string Title = "PC health report";
        public string Grade = "";
        public string Subtitle = "";
        public List<Pc.ReportLine> Lines = new();
        public List<string> Tips = new();
    }

    public sealed class ResultItem
    {
        public string Title = "";
        public string Subtitle = "";
        /// <summary>A web address, a file path or a folder path. Opening it does the right thing.</summary>
        public string Target = "";
        public bool IsFile;
        /// <summary>A picture to show (screenshots and image files).</summary>
        public string ImagePath = "";
    }

    public sealed class ResultsCard : AceCard
    {
        public string Title = "";
        public List<ResultItem> Items = new();
        /// <summary>Show pictures as a grid of thumbnails.</summary>
        public bool Thumbnails;
    }

    public sealed class InfoCard : AceCard
    {
        public string Title = "";
        public string Body = "";
        public string Source = "";
        public string Url = "";
        public string ImageUrl = "";
    }

    public sealed record StatRow(string Label, string Value, double Percent, bool Warn);

    public sealed class StatsCard : AceCard
    {
        public string Title = "";
        public List<StatRow> Rows = new();
    }

    public sealed class WeatherCard : AceCard
    {
        public string Place = "";
        public string Now = "";
        public string Detail = "";
        public List<(string Day, string Text)> Days = new();
    }

    public sealed class CalendarCard : AceCard
    {
        public string Title = "";
        public List<CalendarEvent> Events = new();
    }

    public sealed class SleepCard : AceCard
    {
        public string AvgBed = "";
        public string AvgWake = "";
        public string AvgSleep = "";
        public string Suggest = "";
        public int Nights;
    }

    /// <summary>A question with Yes / No buttons. Calendar changes and anything risky use this.</summary>
    public sealed class ConfirmCard : AceCard
    {
        public string Question = "";
        public string YesLabel = "Yes";
        public string NoLabel = "No";
        public Func<Task<string>> OnYes = () => Task.FromResult("Done.");
        public Func<string> OnNo = () => "Okay, I left it alone.";
        /// <summary>Set once a button was pressed (or "yes"/"no" was said), so it can't run twice.</summary>
        public bool Answered { get; private set; }
        public string Result { get; private set; } = "";
        /// <summary>Raised (on any thread) with what happened, so every view of the card can update.</summary>
        public event Action<string>? Resolved;

        public async Task<string?> AnswerAsync(bool yes)
        {
            lock (this) { if (Answered) return null; Answered = true; }
            string result;
            try { result = yes ? await OnYes().ConfigureAwait(false) : OnNo(); }
            catch (Exception ex) { result = "That didn't work: " + ex.Message; }
            Result = result;
            Resolved?.Invoke(result);
            return result;
        }
    }

    /// <summary>Shutdown/restart: counts down, then runs, unless Cancel is pressed or "cancel" is said.</summary>
    public sealed class CountdownCard : AceCard
    {
        public string Title = "";
        public int Seconds = 10;
        public Action OnFire = () => { };
        public Action OnCancel = () => { };
        public int Left { get; private set; }
        public bool Done { get; private set; }
        public bool Cancelled { get; private set; }
        /// <summary>Seconds left, once a second (on a background thread).</summary>
        public event Action<int>? Tick;
        public event Action? Finished;
        private System.Threading.CancellationTokenSource? _cts;

        public void Start()
        {
            lock (this) { if (_cts != null) return; _cts = new System.Threading.CancellationTokenSource(); }
            Left = Seconds;
            _ = RunAsync(_cts.Token);
        }

        private async Task RunAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                while (Left > 0)
                {
                    Tick?.Invoke(Left);
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    Left--;
                }
                lock (this) { if (Done) return; Done = true; }
                Tick?.Invoke(0);
                Finished?.Invoke();
                OnFire();
            }
            catch (TaskCanceledException) { }
        }

        public bool Cancel()
        {
            lock (this) { if (Done) return false; Done = true; Cancelled = true; }
            _cts?.Cancel();
            Finished?.Invoke();
            OnCancel();
            return true;
        }
    }

    /// <summary>What Ace says back.</summary>
    public sealed class AceAnswer
    {
        public string Text = "";
        /// <summary>What to say out loud, if different from Text (shorter, no lists).</summary>
        public string? Speak;
        public List<AceCard> Cards = new();
        public List<ThoughtStep> Thoughts = new();
        /// <summary>Which part of Ace answered: a skill name, "bank", "web", "learned", "helper" or "fallback".</summary>
        public string Source = "";
        /// <summary>The bank entry this answer came from, if any.</summary>
        public string BankId = "";
        /// <summary>0 to 1: how sure Ace is.</summary>
        public double Confidence = 1;
        public string Mood = "happy";   // happy, think, alert, sleepy, love, confused
        /// <summary>A little opener in front of the answer ("Ooh, I know this. "), so follow-ups can skip it.</summary>
        public string Opener = "";
        /// <summary>A question Ace offers to answer next ("What currency does France use?"). Saying "yes" answers it.</summary>
        public string FollowUp = "";
        /// <summary>Things to tap next (shown as chips under the answer).</summary>
        public List<string> Suggestions = new();

        public static AceAnswer Say(string text, string source, double confidence = 1) =>
            new() { Text = text, Source = source, Confidence = confidence };
    }

    // ===================================================================== Upgrade cards

    /// <summary>A homework problem worked out step by step.</summary>
    public sealed class StepsCard : AceCard
    {
        public Brain.MathWork Work = new();
    }

    /// <summary>Everything Ace remembers about you, each with a delete button.</summary>
    public sealed class MemoryCard : AceCard
    {
        public Func<List<(string Id, string Text)>> Items = () => new();
        public Func<string, bool> Forget = _ => false;
    }

    /// <summary>Your notes (live), with delete buttons.</summary>
    public sealed class NotesCard : AceCard { }

    /// <summary>The shopping list (live), with tick boxes.</summary>
    public sealed class ShoppingCard : AceCard { }

    public sealed class TranslateCard : AceCard
    {
        public string From = "";
        public string To = "";
        public string Language = "";
        public string Note = "";
        public bool Offline;
    }

    public sealed class TriviaCard : AceCard
    {
        public Brain.TriviaGame Game = null!;
    }

    public sealed class TwentyQCard : AceCard
    {
        public Brain.TwentyQuestions Game = null!;
    }

    /// <summary>What's playing, with album art and Previous / Play-Pause / Next buttons.</summary>
    public sealed class MusicCard : AceCard
    {
        public Pc.SongInfo? Song;
    }

    public sealed class ScreenTextCard : AceCard
    {
        public Pc.ScreenText Screen = new();
    }

    /// <summary>Today's screen time per app, the week's bars and the limits.</summary>
    public sealed class ScreenTimeCard : AceCard { }

    public sealed class ConvertCard : AceCard
    {
        public string Title = "";
        public List<(string Value, string Unit)> Rows = new();
    }

    /// <summary>One ready-made script with colored code, Copy and Save buttons, and where to put it.</summary>
    public sealed class CodeCard : AceCard
    {
        public Brain.ScriptEntry Script = null!;
    }

    /// <summary>A script explained part by part, in plain words.</summary>
    public sealed class ExplainCard : AceCard
    {
        public Brain.ScriptEntry Script = null!;
        public List<Brain.ExplainStep> Steps = new();
        /// <summary>When set, only the part with this line is highlighted ("what does line 12 do").</summary>
        public int Focus;
    }

    /// <summary>Trivia high scores, best streak and recent games.</summary>
    public sealed class TriviaScoresCard : AceCard { }

    /// <summary>A list of scripts; each opens its code card.</summary>
    public sealed class ScriptListCard : AceCard
    {
        public string Title = "";
        public List<Brain.ScriptEntry> Scripts = new();
    }
}
