using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nimbus.AceV4;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;

namespace Nimbus.Controls
{
    /// <summary>
    /// Views for the upgrade cards: homework steps, memories, notes, shopping list, translations, trivia,
    /// 20 questions, what's playing, screen text, screen time, conversions and the script library (code with colors).
    /// </summary>
    public static partial class AceCardViews
    {
        /// <summary>A card button wants to say something to Ace (a trivia answer, "yes" in 20 questions).</summary>
        public static event Action<string>? AskAce;

        public static void Ask(string text) => AskAce?.Invoke(text);

        private static FrameworkElement? MoreCard(AceCard card, bool compact) => card switch
        {
            StepsCard s => Steps(s, compact),
            MemoryCard m => Memory(m, compact),
            NotesCard => Notes(compact),
            ShoppingCard => Shopping(compact),
            TranslateCard t => Translate(t, compact),
            TriviaCard t => TriviaView(t, compact),
            TwentyQCard t => TwentyQ(t, compact),
            MusicCard m => Music(m, compact),
            ScreenTextCard s => ScreenTextView(s, compact),
            ScreenTimeCard => ScreenTimeView(compact),
            ConvertCard c => Convert(c, compact),
            CodeCard c => Code(c.Script, compact),
            ScriptListCard l => ScriptList(l, compact),
            ExplainCard e => Explain(e, compact),
            TriviaScoresCard => TriviaScoresView(compact),
            _ => null,
        };

        // ===================================================================== Shared bits

        private static void CopyText(string text, Button? button = null)
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    if (button != null)
                    {
                        object old = button.Content;
                        button.Content = "Copied ✓";
                        var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                        back.Tick += (_, _) => { back.Stop(); button.Content = old; };
                        back.Start();
                    }
                    return;
                }
                catch { System.Threading.Thread.Sleep(40); }   // the clipboard is sometimes busy for a moment
            }
        }

        private static Button Delete(string tip)
        {
            var b = SmallButton("✕");
            b.ToolTip = tip;
            b.Margin = new Thickness(8, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Top;
            return b;
        }

        private static TextBox Input(string hint)
        {
            var box = new TextBox { MinWidth = 120, Padding = new Thickness(8, 4, 8, 4), FontSize = 12.5, ToolTip = hint, VerticalContentAlignment = VerticalAlignment.Center };
            box.SetResourceReference(Control.BackgroundProperty, "SoftBrush");
            box.SetResourceReference(Control.ForegroundProperty, "FgBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "CardBorderBrush");
            box.SetResourceReference(TextBox.CaretBrushProperty, "FgBrush");
            var hintText = Text(hint, 12, "MutedBrush", wrap: false);
            hintText.IsHitTestVisible = false;
            hintText.Margin = new Thickness(11, 0, 0, 0);
            hintText.VerticalAlignment = VerticalAlignment.Center;
            box.Tag = hintText;
            return box;
        }

        /// <summary>A text box with a grey hint and an Add button; Enter works too.</summary>
        private static FrameworkElement AddRow(string hint, string buttonLabel, Action<string> add)
        {
            var box = Input(hint);
            var hintText = (TextBlock)box.Tag;
            var grid = new Grid();
            grid.Children.Add(box);
            grid.Children.Add(hintText);
            box.TextChanged += (_, _) => hintText.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var go = SmallButton(buttonLabel, hot: true);
            go.Margin = new Thickness(6, 0, 0, 0);
            void Submit()
            {
                string t = box.Text.Trim();
                if (t.Length == 0) return;
                box.Clear();
                add(t);
            }
            go.Click += (_, _) => Submit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Submit(); } };
            var dock = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(go, Dock.Right);
            dock.Children.Add(go);
            dock.Children.Add(grid);
            return dock;
        }

        /// <summary>Hooks a live list to a store's Changed event while the card is on screen.</summary>
        private static void Live(FrameworkElement view, Action rebuild, Action<Action> subscribe, Action<Action> unsubscribe)
        {
            Action changed = () => Ui(rebuild);
            view.Loaded += (_, _) => { unsubscribe(changed); subscribe(changed); rebuild(); };
            view.Unloaded += (_, _) => unsubscribe(changed);
            rebuild();
        }

        // ===================================================================== Homework

        private static FrameworkElement Steps(StepsCard card, bool compact)
        {
            var w = card.Work;
            var stack = new StackPanel();
            stack.Children.Add(Header("Homework · " + (w.Title.Length > 0 ? w.Title : "Step by step"), compact, "AmberBrush"));
            if (w.Problem.Length > 0)
            {
                var p = Text(w.Problem, compact ? 15 : 18, "FgBrush", bold: true, font: "MonoFont");
                p.Margin = new Thickness(0, 0, 0, 8);
                stack.Children.Add(p);
            }
            int n = 1;
            int max = compact ? 4 : 30;
            foreach (var step in w.Steps.Take(max))
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
                var num = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Top };
                num.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
                var nt = Text((n++).ToString(), 10.5, "AmberBrush", bold: true, wrap: false);
                nt.HorizontalAlignment = HorizontalAlignment.Center;
                nt.VerticalAlignment = VerticalAlignment.Center;
                num.Child = nt;
                DockPanel.SetDock(num, Dock.Left);
                row.Children.Add(num);
                row.Children.Add(Text(step, 12.5, "FgBrush"));
                stack.Children.Add(row);
            }
            if (w.Steps.Count > max) stack.Children.Add(Text($"+{w.Steps.Count - max} more steps on the Ace page", 11, "MutedBrush"));
            if (w.Answer.Length > 0)
            {
                var ans = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                ans.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
                var at = new TextBlock { FontSize = compact ? 14 : 16 };
                at.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                var label = new System.Windows.Documents.Run("Answer: ");
                label.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "MutedBrush");
                var value = new System.Windows.Documents.Run(w.Answer) { FontWeight = FontWeights.Bold };
                value.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "GoodBrush");
                at.Inlines.Add(label);
                at.Inlines.Add(value);
                ans.Child = at;
                stack.Children.Add(ans);
            }
            if (!compact)
                stack.Children.Add(Text("Tip: try the next one yourself, then ask me to check it!", 11, "MutedBrush"));
            return Box(stack, compact, "AmberBrush");
        }

        // ===================================================================== Memory

        private static FrameworkElement Memory(MemoryCard card, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header("What I remember about you", compact));
            var list = new StackPanel();
            stack.Children.Add(list);
            void Rebuild()
            {
                list.Children.Clear();
                var items = card.Items();
                if (items.Count == 0)
                {
                    list.Children.Add(Text("Nothing yet. Tell me things like \"remember that my favorite color is green\".", 12, "MutedBrush"));
                    return;
                }
                foreach (var (id, text) in items.Take(compact ? 5 : 60))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 3) };
                    var del = Delete("Forget this");
                    string forgetId = id;
                    del.Click += (_, _) => { if (card.Forget(forgetId)) { Rebuild(); CardSaid?.Invoke("Okay, I forgot that."); } };
                    DockPanel.SetDock(del, Dock.Right);
                    row.Children.Add(del);
                    row.Children.Add(Text("• " + text, 12.5, "FgBrush"));
                    list.Children.Add(row);
                }
                if (items.Count > (compact ? 5 : 60)) list.Children.Add(Text($"+{items.Count - (compact ? 5 : 60)} more", 11, "MutedBrush"));
            }
            Rebuild();
            if (!compact)
                stack.Children.Add(AddRow("Teach me something about you…", "Remember", t => Ask(t.StartsWith("remember", StringComparison.OrdinalIgnoreCase) ? t : "remember that " + t)));
            stack.Loaded += (_, _) => Rebuild();
            return Box(stack, compact);
        }

        // ===================================================================== Notes

        private static FrameworkElement Notes(bool compact)
        {
            var stack = new StackPanel();
            var head = Header("Notes", compact, "PebbleBrush");
            stack.Children.Add(head);
            var list = new StackPanel();
            stack.Children.Add(list);
            void Rebuild()
            {
                list.Children.Clear();
                var notes = NoteStore.List();
                head.Text = notes.Count == 0 ? "NOTES" : $"NOTES · {notes.Count}";
                if (notes.Count == 0) { list.Children.Add(Text("No notes yet.", 12, "MutedBrush")); return; }
                foreach (var n in notes.Take(compact ? 4 : 50))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
                    var del = Delete("Delete this note");
                    string id = n.Id;
                    del.Click += (_, _) => NoteStore.Remove(id);
                    DockPanel.SetDock(del, Dock.Right);
                    row.Children.Add(del);
                    var col = new StackPanel();
                    col.Children.Add(Text(n.Text, 12.5, "FgBrush"));
                    col.Children.Add(Text(NaturalAge(n.Created), 10.5, "MutedBrush", wrap: false));
                    row.Children.Add(col);
                    list.Children.Add(row);
                }
                if (notes.Count > (compact ? 4 : 50)) list.Children.Add(Text($"+{notes.Count - (compact ? 4 : 50)} more", 11, "MutedBrush"));
            }
            if (!compact) stack.Children.Add(AddRow("Write a note…", "Add", t => NoteStore.Add(t)));
            Live(stack, Rebuild, a => NoteStore.Changed += a, a => NoteStore.Changed -= a);
            return Box(stack, compact, "PebbleBrush");
        }

        private static string NaturalAge(DateTime when)
        {
            var ago = DateTime.Now - when;
            if (ago < TimeSpan.FromMinutes(1)) return "just now";
            if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} min ago";
            if (when.Date == DateTime.Today) return "today " + when.ToString("h:mm tt");
            if (when.Date == DateTime.Today.AddDays(-1)) return "yesterday " + when.ToString("h:mm tt");
            return when.ToString("MMM d");
        }

        // ===================================================================== Shopping list

        private static FrameworkElement Shopping(bool compact)
        {
            var stack = new StackPanel();
            var headRow = new DockPanel();
            var head = Header("Shopping list", compact, "GoodBrush");
            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var copy = SmallButton("Copy");
            copy.ToolTip = "Copy the list to send to someone";
            copy.Click += (_, _) => CopyText(string.Join(Environment.NewLine, ShoppingList.Items().Where(i => !i.Checked).Select(i => "• " + ShoppingList.Describe(i))), copy);
            var clear = SmallButton("Clear ticked");
            clear.Margin = new Thickness(6, 0, 0, 0);
            clear.Click += (_, _) => ShoppingList.Clear(onlyChecked: true);
            tools.Children.Add(copy);
            tools.Children.Add(clear);
            if (!compact) { DockPanel.SetDock(tools, Dock.Right); headRow.Children.Add(tools); }
            headRow.Children.Add(head);
            stack.Children.Add(headRow);
            var list = new StackPanel();
            stack.Children.Add(list);
            void Rebuild()
            {
                list.Children.Clear();
                var items = ShoppingList.Items();
                int left = items.Count(i => !i.Checked);
                head.Text = items.Count == 0 ? "SHOPPING LIST" : $"SHOPPING LIST · {left} TO GET";
                if (items.Count == 0) { list.Children.Add(Text("Empty. Say \"add milk to my shopping list\".", 12, "MutedBrush")); return; }
                foreach (var it in items.OrderBy(i => i.Checked).Take(compact ? 6 : 80))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var del = Delete("Remove");
                    string id = it.Id;
                    del.Click += (_, _) => ShoppingList.RemoveId(id);
                    DockPanel.SetDock(del, Dock.Right);
                    if (!compact) row.Children.Add(del);
                    var box = new CheckBox { IsChecked = it.Checked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                    box.Click += (_, _) => ShoppingList.SetChecked(id, box.IsChecked == true);
                    DockPanel.SetDock(box, Dock.Left);
                    row.Children.Add(box);
                    var t = Text(ShoppingList.Describe(it) + (it.Recipe.Length > 0 && !compact ? "   · " + it.Recipe : ""), 12.5, it.Checked ? "MutedBrush" : "FgBrush");
                    if (it.Checked) t.TextDecorations = TextDecorations.Strikethrough;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.Cursor = Cursors.Hand;
                    t.MouseLeftButtonUp += (_, _) => ShoppingList.SetChecked(id, !it.Checked);
                    row.Children.Add(t);
                    list.Children.Add(row);
                }
                int max = compact ? 6 : 80;
                if (items.Count > max) list.Children.Add(Text($"+{items.Count - max} more", 11, "MutedBrush"));
            }
            if (!compact) stack.Children.Add(AddRow("Add something, like 2 cups flour…", "Add", t => { foreach (var part in t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) ShoppingList.Add(part); }));
            Live(stack, Rebuild, a => ShoppingList.Changed += a, a => ShoppingList.Changed -= a);
            return Box(stack, compact, "GoodBrush");
        }

        // ===================================================================== Translate

        private static FrameworkElement Translate(TranslateCard t, bool compact)
        {
            var stack = new StackPanel();
            string head = t.Language.Length > 0 ? (t.From.Length > 0 && t.To.Length > 0 && t.Language.Length > 0 ? $"{t.Language}" : "Translate") : "Translate";
            stack.Children.Add(Header("Translate · " + head + (t.Offline ? " · offline phrase book" : " · online"), compact, "AccentBrush"));
            stack.Children.Add(Text(t.From, 12.5, "MutedBrush"));
            var big = Text(t.To, compact ? 17 : 22, "FgBrush", bold: true, font: "DisplayFont");
            big.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(big);
            if (t.Note.Length > 0) stack.Children.Add(Text(t.Note, 11.5, "MutedBrush"));
            if (!compact)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var copy = SmallButton("Copy");
                copy.Click += (_, _) => CopyText(t.To, copy);
                row.Children.Add(copy);
                stack.Children.Add(row);
            }
            return Box(stack, compact, "AccentBrush");
        }

        // ===================================================================== Trivia

        private static FrameworkElement TriviaView(TriviaCard card, bool compact)
        {
            var game = card.Game;
            var stack = new StackPanel();
            var head = Header("", compact, "AmberBrush");
            stack.Children.Add(head);
            var question = Text("", compact ? 13.5 : 15, "FgBrush", bold: true);
            stack.Children.Add(question);
            var choices = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(choices);
            var score = Text("", 11.5, "MutedBrush");
            score.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(score);
            int shownAsked = -1;
            void Rebuild()
            {
                string cat = game.Category.Length > 0 ? Composer.Capital(game.Category) + " trivia" : "Trivia";
                head.Text = game.Over ? $"{cat.ToUpperInvariant()} · GAME OVER" : $"{cat.ToUpperInvariant()} · QUESTION {Math.Min(game.Asked, 10)} OF 10";
                var best = TriviaScores.Best(game.Category);
                score.Text = $"Score: {game.Score}"
                    + (game.Streak >= 2 && !game.Over ? $"   ·   🔥 {game.Streak} in a row" : "")
                    + (best != null ? $"   ·   Best: {best.Score}/{best.OutOf}" : "")
                    + (game.Over ? "" : "   ·   tap an answer or say A, B, C or D");
                if (shownAsked == game.Asked && !game.Over) return;
                shownAsked = game.Asked;
                question.Text = game.Current?.Question ?? "";
                choices.Children.Clear();
                if (game.Over || game.Current == null) return;
                for (int i = 0; i < game.Choices.Count; i++)
                {
                    string letter = ((char)('A' + i)).ToString();
                    var b = SmallButton($"{letter})  {game.Choices[i]}");
                    b.Margin = new Thickness(0, 0, 6, 6);
                    b.Click += (_, _) =>
                    {
                        if (game.Over || game.Marked) return;
                        foreach (var c in choices.Children.OfType<Button>()) c.IsEnabled = false;
                        Ask(letter);
                    };
                    choices.Children.Add(b);
                }
            }
            Action changed = () => Ui(Rebuild);
            stack.Loaded += (_, _) => { game.Changed -= changed; game.Changed += changed; Rebuild(); };
            stack.Unloaded += (_, _) => game.Changed -= changed;
            Rebuild();
            return Box(stack, compact, "AmberBrush");
        }

        // ===================================================================== 20 questions

        private static FrameworkElement TwentyQ(TwentyQCard card, bool compact)
        {
            var game = card.Game;
            var stack = new StackPanel();
            var head = Header("", compact, "NovaBrush");
            stack.Children.Add(head);
            var meter = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 8) };
            meter.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.BackgroundProperty, "NovaBrush");
            meter.Child = fill;
            stack.Children.Add(meter);
            var buttons = new WrapPanel();
            stack.Children.Add(buttons);
            void Rebuild()
            {
                head.Text = game.Over ? "20 QUESTIONS · DONE" : $"20 QUESTIONS · {game.Count} OF 20 USED" + (game.AceGuesses ? " · I'M GUESSING" : " · YOU'RE GUESSING");
                fill.Width = Math.Max(0, Math.Min(1, game.Count / 20.0)) * (compact ? 220 : 320);
                buttons.Children.Clear();
                if (game.Over) return;
                var labels = game.AceGuesses ? new[] { "Yes", "No", "Sometimes", "Don't know" } : new[] { "Hint", "I give up" };
                foreach (var label in labels)
                {
                    var b = SmallButton(label, hot: label == "Yes");
                    b.Margin = new Thickness(0, 0, 6, 6);
                    string say = label;
                    b.Click += (_, _) => Ask(say);
                    buttons.Children.Add(b);
                }
            }
            Action changed = () => Ui(Rebuild);
            stack.Loaded += (_, _) => { game.Changed -= changed; game.Changed += changed; Rebuild(); };
            stack.Unloaded += (_, _) => game.Changed -= changed;
            Rebuild();
            return Box(stack, compact, "NovaBrush");
        }

        // ===================================================================== What's playing

        private static FrameworkElement Music(MusicCard card, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header("Now playing", compact, "PinkTextBrush"));
            var dock = new DockPanel();
            stack.Children.Add(dock);
            int artSize = compact ? 54 : 76;
            var art = new Border { Width = artSize, Height = artSize, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0) };
            art.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
            var note = Text("♪", 24, "MutedBrush", wrap: false);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.VerticalAlignment = VerticalAlignment.Center;
            art.Child = note;
            DockPanel.SetDock(art, Dock.Left);
            dock.Children.Add(art);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Text("", compact ? 14 : 16, "FgBrush", bold: true, font: "DisplayFont");
            var artist = Text("", 12, "MutedBrush");
            var app = Text("", 10.5, "MutedBrush", wrap: false);
            info.Children.Add(title);
            info.Children.Add(artist);
            info.Children.Add(app);
            dock.Children.Add(info);

            var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 10, 0, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
            var barFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            barFill.SetResourceReference(Border.BackgroundProperty, "PinkTextBrush");
            bar.Child = barFill;
            stack.Children.Add(bar);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var prev = SmallButton("⏮");
            var play = SmallButton("⏯", hot: true);
            var next = SmallButton("⏭");
            prev.ToolTip = "Previous"; play.ToolTip = "Play or pause"; next.ToolTip = "Next song";
            play.Margin = next.Margin = new Thickness(6, 0, 0, 0);
            controls.Children.Add(prev);
            controls.Children.Add(play);
            controls.Children.Add(next);
            stack.Children.Add(controls);

            SongInfo? song = card.Song;
            string artKey = "";
            void Show()
            {
                if (song == null)
                {
                    title.Text = "Nothing playing";
                    artist.Text = "Start a song in Spotify, YouTube or any music app.";
                    app.Text = "";
                    barFill.Width = 0;
                    art.Background = null;
                    art.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
                    art.Child = note;
                    artKey = "";
                    return;
                }
                title.Text = song.Title;
                artist.Text = song.Artist + (song.Album.Length > 0 && song.Album != song.Title ? " · " + song.Album : "");
                app.Text = (song.Playing ? "▶ Playing" : "⏸ Paused") + (song.App.Length > 0 ? " in " + song.App : "");
                double width = bar.ActualWidth > 0 ? bar.ActualWidth : (compact ? 220 : 300);
                barFill.Width = song.Length.TotalSeconds > 0 ? Math.Clamp(song.Position.TotalSeconds / song.Length.TotalSeconds, 0, 1) * width : 0;
                string key = song.Title + "|" + song.Artist;
                if (song.Art is { Length: > 0 } bytes && key != artKey)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 160;
                        bmp.StreamSource = new MemoryStream(bytes);
                        bmp.EndInit();
                        bmp.Freeze();
                        art.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                        art.Child = null;
                        artKey = key;
                    }
                    catch { }
                }
            }
            bool busy = false;
            async Task RefreshAsync()
            {
                if (busy) return;
                busy = true;
                try
                {
                    string before = song == null ? "" : song.Title + "|" + song.Artist;
                    var fresh = await NowPlaying.GetAsync(withArt: false);
                    if (fresh != null && (fresh.Title + "|" + fresh.Artist) != before) fresh = await NowPlaying.GetAsync(withArt: true) ?? fresh;
                    else if (fresh != null && song != null) fresh.Art = song.Art;
                    song = fresh;
                    Show();
                }
                catch { }
                finally { busy = false; }
            }
            prev.Click += async (_, _) => { await NowPlaying.PreviousAsync(); await Task.Delay(400); await RefreshAsync(); };
            play.Click += async (_, _) => { await NowPlaying.PlayPauseAsync(); await Task.Delay(300); await RefreshAsync(); };
            next.Click += async (_, _) => { await NowPlaying.NextAsync(); await Task.Delay(400); await RefreshAsync(); };

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            clock.Tick += async (_, _) => await RefreshAsync();
            stack.Loaded += async (_, _) => { clock.Start(); if (song == null) await RefreshAsync(); else Show(); };
            stack.Unloaded += (_, _) => clock.Stop();
            Show();
            return Box(stack, compact, "PinkTextBrush");
        }

        // ===================================================================== Screen text

        private static FrameworkElement ScreenTextView(ScreenTextCard card, bool compact)
        {
            var s = card.Screen;
            var stack = new StackPanel();
            stack.Children.Add(Header($"On your screen{(s.App.Length > 0 ? " · " + s.App : "")} · {s.Words} words", compact, "AccentBrush"));
            if (s.Lines.Count == 0)
            {
                stack.Children.Add(Text("I couldn't find any text on your screen.", 12, "MutedBrush"));
                return Box(stack, compact, "AccentBrush");
            }
            var text = new TextBox
            {
                Text = string.Join(Environment.NewLine, s.Lines.Take(compact ? 8 : 400)),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = compact ? 120 : 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Padding = new Thickness(8),
            };
            text.SetResourceReference(Control.BackgroundProperty, "SoftBrush");
            text.SetResourceReference(Control.ForegroundProperty, "FgBrush");
            text.SetResourceReference(Control.FontFamilyProperty, "BodyFont");
            stack.Children.Add(text);
            if (!compact)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var copy = SmallButton("Copy all text");
                copy.Click += (_, _) => CopyText(s.Text, copy);
                row.Children.Add(copy);
                var solve = SmallButton("Solve math on it");
                solve.Margin = new Thickness(6, 0, 0, 0);
                solve.Click += (_, _) => Ask("solve the math problem on my screen");
                row.Children.Add(solve);
                stack.Children.Add(row);
            }
            return Box(stack, compact, "AccentBrush");
        }

        // ===================================================================== Screen time

        private static FrameworkElement ScreenTimeView(bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header("Screen time today", compact, "AceBrush"));
            var total = Text("", compact ? 20 : 26, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            stack.Children.Add(total);
            var limit = Text("", 11.5, "MutedBrush");
            stack.Children.Add(limit);
            var apps = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(apps);
            var week = new Grid { Height = compact ? 0 : 64, Margin = new Thickness(0, 10, 0, 0) };
            if (!compact) stack.Children.Add(week);
            void Rebuild()
            {
                var now = DateTime.Now;
                var today = ScreenTime.Total(now);
                total.Text = ScreenTime.Span(today);
                int lim = ScreenTime.LimitMinutes;
                var appLimits = ScreenTime.AppLimits;
                var parts = new List<string>();
                if (lim > 0) parts.Add(today.TotalMinutes >= lim ? $"Past your {ScreenTime.Span(TimeSpan.FromMinutes(lim))} limit" : $"{ScreenTime.Span(TimeSpan.FromMinutes(lim) - today)} left of {ScreenTime.Span(TimeSpan.FromMinutes(lim))}");
                foreach (var (app, m) in appLimits) parts.Add($"{app}: {ScreenTime.Span(TimeSpan.FromMinutes(m))} a day");
                limit.Text = parts.Count > 0 ? string.Join("   ·   ", parts) : "No limits set. Try \"set my screen time limit to 2 hours\".";
                apps.Children.Clear();
                var day = ScreenTime.Day(now);
                double top = day.Count > 0 ? day[0].Seconds : 1;
                foreach (var (app, sec) in day.Take(compact ? 3 : 6))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var amount = Text(ScreenTime.Span(TimeSpan.FromSeconds(sec)), 11, "MutedBrush", wrap: false);
                    amount.Width = 110;
                    amount.TextAlignment = TextAlignment.Right;
                    DockPanel.SetDock(amount, Dock.Right);
                    row.Children.Add(amount);
                    var name = Text(app, 12, "FgBrush", wrap: false);
                    name.Width = compact ? 90 : 130;
                    DockPanel.SetDock(name, Dock.Left);
                    row.Children.Add(name);
                    var barBack = new Border { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    barBack.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
                    var barFill = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
                    barFill.SetResourceReference(Border.BackgroundProperty, appLimits.ContainsKey(app) && sec / 60 >= appLimits[app] ? "WarnBrush" : "AceBrush");
                    double share = sec / top;
                    barBack.SizeChanged += (_, e) => barFill.Width = Math.Max(4, e.NewSize.Width * share);
                    barBack.Child = barFill;
                    row.Children.Add(barBack);
                    apps.Children.Add(row);
                }
                if (day.Count == 0) apps.Children.Add(Text("Nothing counted yet today.", 12, "MutedBrush"));

                if (compact) return;
                week.Children.Clear();
                week.ColumnDefinitions.Clear();
                var days = ScreenTime.Week(now);
                double most = Math.Max(1, days.Max(d => d.Total.TotalMinutes));
                for (int i = 0; i < days.Count; i++)
                {
                    week.ColumnDefinitions.Add(new ColumnDefinition());
                    var col = new DockPanel { Margin = new Thickness(3, 0, 3, 0), ToolTip = $"{days[i].Day:dddd}: {ScreenTime.Span(days[i].Total)}" };
                    var label = Text(days[i].Day.ToString("ddd")[..2], 10, i == days.Count - 1 ? "FgBrush" : "MutedBrush", wrap: false);
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    DockPanel.SetDock(label, Dock.Bottom);
                    col.Children.Add(label);
                    var b = new Border { CornerRadius = new CornerRadius(4, 4, 2, 2), VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(3, days[i].Total.TotalMinutes / most * 44) };
                    b.SetResourceReference(Border.BackgroundProperty, i == days.Count - 1 ? "AceBrush" : "SoftBrush");
                    col.Children.Add(b);
                    Grid.SetColumn(col, i);
                    week.Children.Add(col);
                }
            }
            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            clock.Tick += (_, _) => Rebuild();
            Action changed = () => Ui(Rebuild);
            stack.Loaded += (_, _) => { ScreenTime.Changed -= changed; ScreenTime.Changed += changed; Rebuild(); clock.Start(); };
            stack.Unloaded += (_, _) => { ScreenTime.Changed -= changed; clock.Stop(); };
            Rebuild();
            return Box(stack, compact, "AceBrush");
        }

        // ===================================================================== Conversions

        private static FrameworkElement Convert(ConvertCard card, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(card.Title, compact, "PebbleBrush"));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int r = 0;
            foreach (var (value, unit) in card.Rows.Take(compact ? 4 : 20))
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var v = Text(value, 13, "FgBrush", bold: true, wrap: false, font: "MonoFont");
                v.Margin = new Thickness(0, 1, 12, 1);
                v.TextAlignment = TextAlignment.Right;
                Grid.SetRow(v, r);
                grid.Children.Add(v);
                var u = Text(unit, 12.5, "MutedBrush", wrap: false);
                u.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(u, r);
                Grid.SetColumn(u, 1);
                grid.Children.Add(u);
                r++;
            }
            stack.Children.Add(grid);
            return Box(stack, compact, "PebbleBrush");
        }

        // ===================================================================== Code

        private static readonly Dictionary<ScriptLibrary.Tok, string> TokBrush = new()
        {
            [ScriptLibrary.Tok.Plain] = "FgBrush",
            [ScriptLibrary.Tok.Keyword] = "AccentBrush",
            [ScriptLibrary.Tok.Type] = "AceBrush",
            [ScriptLibrary.Tok.String] = "GoodBrush",
            [ScriptLibrary.Tok.Comment] = "MutedBrush",
            [ScriptLibrary.Tok.Number] = "AmberBrush",
            [ScriptLibrary.Tok.Builtin] = "PebbleBrush",
            [ScriptLibrary.Tok.Preproc] = "PinkTextBrush",
        };

        /// <summary>The code with syntax colors, line numbers and copy/save buttons.</summary>
        public static FrameworkElement Code(ScriptEntry s, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header($"{s.LangName} · {s.KindName}", compact, s.Lang == "lua" ? "AceBrush" : "AccentBrush"));
            stack.Children.Add(Text(s.Name, compact ? 15 : 18, "FgBrush", bold: true, font: "DisplayFont"));
            if (s.Summary.Length > 0) stack.Children.Add(Text(s.Summary, 12, "MutedBrush"));

            string code = s.Code;
            var lines = code.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
            var codeBlock = new TextBlock { FontSize = compact ? 11 : 12.5, TextWrapping = TextWrapping.NoWrap, LineHeight = compact ? 15 : 17.5 };
            codeBlock.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            var gutter = new TextBlock { FontSize = codeBlock.FontSize, LineHeight = codeBlock.LineHeight, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 12, 0) };
            gutter.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            gutter.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            bool inComment = false;
            int shown = compact ? Math.Min(lines.Length, 12) : lines.Length;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) { codeBlock.Inlines.Add(new System.Windows.Documents.LineBreak()); gutter.Inlines.Add(new System.Windows.Documents.LineBreak()); }
                gutter.Inlines.Add(new System.Windows.Documents.Run((i + 1).ToString()));
                foreach (var (part, tok) in ScriptLibrary.Colorize(lines[i].Replace("\t", "    "), s.Lang, ref inComment))
                {
                    var run = new System.Windows.Documents.Run(part);
                    run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, TokBrush[tok]);
                    if (tok == ScriptLibrary.Tok.Comment) run.FontStyle = FontStyles.Italic;
                    if (tok == ScriptLibrary.Tok.Keyword) run.FontWeight = FontWeights.SemiBold;
                    codeBlock.Inlines.Add(run);
                }
            }
            var codeRow = new DockPanel();
            DockPanel.SetDock(gutter, Dock.Left);
            codeRow.Children.Add(gutter);
            codeRow.Children.Add(codeBlock);
            var scroll = new ScrollViewer
            {
                Content = codeRow,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = compact ? 200 : 420,
                Padding = new Thickness(10, 8, 10, 8),
            };
            var codeBox = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 8, 0, 0), Child = scroll };
            codeBox.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
            stack.Children.Add(codeBox);
            if (compact && lines.Length > shown) stack.Children.Add(Text($"+{lines.Length - shown} more lines (Copy gets all of it)", 11, "MutedBrush"));

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var copy = SmallButton("Copy code", hot: true);
            copy.Click += (_, _) => CopyText(code, copy);
            buttons.Children.Add(copy);
            var save = SmallButton("Save file");
            save.Margin = new Thickness(6, 0, 0, 0);
            save.ToolTip = "Saves it in Documents\\Nimbus Scripts";
            save.Click += (_, _) =>
            {
                string? path = SaveScript(s, code);
                if (path != null) { save.Content = "Saved ✓"; CardSaid?.Invoke($"Saved {Path.GetFileName(path)} in Documents\\Nimbus Scripts."); Open(Path.GetDirectoryName(path)!); }
                else save.Content = "Couldn't save";
            };
            buttons.Children.Add(save);
            var explain = SmallButton("💡 Explain it");
            explain.Margin = new Thickness(6, 0, 0, 0);
            explain.ToolTip = "Explains the script part by part, in plain words";
            buttons.Children.Add(explain);
            stack.Children.Add(buttons);
            var explainHost = new ContentControl { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(explainHost);
            explain.Click += (_, _) =>
            {
                if (explainHost.Content == null)
                {
                    var steps = ScriptExplainer.Explain(s);
                    var panel = new StackPanel();
                    panel.Children.Add(Text($"How it works · {steps.Count} parts", 11, "MutedBrush", bold: true, wrap: false));
                    panel.Children.Add(ExplainSteps(s, steps, 0, compact, compact ? 6 : int.MaxValue));
                    explainHost.Content = panel;
                }
                bool show = explainHost.Visibility != Visibility.Visible;
                explainHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                explain.Content = show ? "Hide explanation" : "💡 Explain it";
            };

            if (s.Where.Length > 0)
            {
                var where = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 8, 0, 0) };
                where.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                var wt = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                wt.SetResourceReference(TextBlock.FontFamilyProperty, "BodyFont");
                var lead = new System.Windows.Documents.Run("Where to put it: ") { FontWeight = FontWeights.Bold };
                lead.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "FgBrush");
                var body = new System.Windows.Documents.Run(s.Where);
                body.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "FgBrush");
                wt.Inlines.Add(lead);
                wt.Inlines.Add(body);
                where.Child = wt;
                stack.Children.Add(where);
            }
            return Box(stack, compact, s.Lang == "lua" ? "AceBrush" : "AccentBrush");
        }

        /// <summary>Saves a script to Documents\Nimbus Scripts (an Arduino sketch gets its own folder, like the Arduino app wants).</summary>
        public static string? SaveScript(ScriptEntry s, string code)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nimbus Scripts");
                string name = s.Id.Length > 0 ? s.Id : "script";
                string folder = s.Kind == "Arduino" ? Path.Combine(root, name.Replace('-', '_')) : root;
                Directory.CreateDirectory(folder);
                string file = s.Kind == "Arduino" ? name.Replace('-', '_') + ".ino" : name + s.Extension;
                string path = Path.Combine(folder, file);
                File.WriteAllText(path, code.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
                return path;
            }
            catch { return null; }
        }

        // ===================================================================== Explain a script

        private static FrameworkElement Explain(ExplainCard card, bool compact)
        {
            var s = card.Script;
            var stack = new StackPanel();
            stack.Children.Add(Header($"Explained · {s.LangName}", compact, s.Lang == "lua" ? "AceBrush" : "AccentBrush"));
            stack.Children.Add(Text(card.Focus > 0 ? $"{s.Name}: line {card.Focus}" : $"How the {s.Name} works", compact ? 15 : 18, "FgBrush", bold: true, font: "DisplayFont"));
            if (s.Summary.Length > 0 && card.Focus == 0)
            {
                var sum = Text(s.Summary, 12, "MutedBrush");
                sum.Margin = new Thickness(0, 2, 0, 0);
                stack.Children.Add(sum);
            }
            var host = new ContentControl { Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(host);
            int first = compact ? (card.Focus > 0 ? 1 : 5) : int.MaxValue;
            host.Content = ExplainSteps(s, card.Steps, card.Focus, compact, first);
            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (card.Steps.Count > first)
            {
                var all = SmallButton($"Show all {card.Steps.Count} parts");
                all.Click += (_, _) => { host.Content = ExplainSteps(s, card.Steps, card.Focus, compact, int.MaxValue); buttons.Children.Remove(all); };
                buttons.Children.Add(all);
            }
            var copy = SmallButton("Copy code");
            copy.Margin = new Thickness(buttons.Children.Count > 0 ? 6 : 0, 0, 0, 0);
            copy.Click += (_, _) => CopyText(s.Code, copy);
            buttons.Children.Add(copy);
            stack.Children.Add(buttons);
            return Box(stack, compact, s.Lang == "lua" ? "AceBrush" : "AccentBrush");
        }

        /// <summary>The parts of a script: line numbers, the code with colors, and what it does.</summary>
        private static FrameworkElement ExplainSteps(ScriptEntry s, List<ExplainStep> steps, int focus, bool compact, int max)
        {
            var list = new StackPanel();
            if (steps.Count == 0) { list.Children.Add(Text("This script is empty.", 12, "MutedBrush")); return list; }
            IEnumerable<(ExplainStep Step, int Index)> pick = steps.Select((x, i) => (x, i));
            if (focus > 0 && max < steps.Count)
            {
                var hit = ScriptExplainer.StepFor(steps, focus);
                pick = pick.Where(p => p.Step == hit);
            }
            else pick = pick.Take(max);
            foreach (var (step, index) in pick)
            {
                bool lit = focus > 0 && focus >= step.FromLine && focus <= step.ToLine;
                var row = new DockPanel();
                var num = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Top };
                num.SetResourceReference(Border.BackgroundProperty, lit ? "AccentSoftBrush" : "SoftBrush");
                var nt = Text((index + 1).ToString(), 10.5, s.Lang == "lua" ? "AceBrush" : "AccentBrush", bold: true, wrap: false);
                nt.HorizontalAlignment = HorizontalAlignment.Center;
                nt.VerticalAlignment = VerticalAlignment.Center;
                num.Child = nt;
                DockPanel.SetDock(num, Dock.Left);
                row.Children.Add(num);

                var col = new StackPanel();
                col.Children.Add(Text(step.Lines.ToUpperInvariant(), 9.5, "MutedBrush", bold: true, wrap: false));
                var codeText = new TextBlock { FontSize = compact ? 10.5 : 11.5, TextWrapping = TextWrapping.NoWrap };
                codeText.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                bool inComment = false;
                var lines = step.Code.Split('\n');
                int shownLines = compact ? Math.Min(lines.Length, 4) : lines.Length;
                // Keep the indents lined up but drop the shared leading spaces.
                int indent = lines.Where(x => x.Trim().Length > 0).Select(x => x.Length - x.TrimStart().Length).DefaultIfEmpty(0).Min();
                for (int i = 0; i < shownLines; i++)
                {
                    if (i > 0) codeText.Inlines.Add(new System.Windows.Documents.LineBreak());
                    string line = lines[i].Length >= indent ? lines[i][indent..] : lines[i].TrimStart();
                    foreach (var (part, tok) in ScriptLibrary.Colorize(line, s.Lang, ref inComment))
                    {
                        var run = new System.Windows.Documents.Run(part);
                        run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, TokBrush[tok]);
                        if (tok == ScriptLibrary.Tok.Keyword) run.FontWeight = FontWeights.SemiBold;
                        if (tok == ScriptLibrary.Tok.Comment) run.FontStyle = FontStyles.Italic;
                        codeText.Inlines.Add(run);
                    }
                }
                if (lines.Length > shownLines)
                {
                    codeText.Inlines.Add(new System.Windows.Documents.LineBreak());
                    var more = new System.Windows.Documents.Run($"… {lines.Length - shownLines} more lines");
                    more.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "MutedBrush");
                    codeText.Inlines.Add(more);
                }
                var codeBox = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 3, 0, 4),
                    Child = new ScrollViewer { Content = codeText, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
                };
                codeBox.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
                col.Children.Add(codeBox);
                col.Children.Add(Text(step.Plain, compact ? 12 : 13, "FgBrush"));
                row.Children.Add(col);

                var holder = new Border { CornerRadius = new CornerRadius(10), Padding = lit ? new Thickness(8, 6, 8, 6) : new Thickness(0), Margin = new Thickness(0, 0, 0, 10), Child = row };
                if (lit) holder.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                list.Children.Add(holder);
            }
            return list;
        }

        // ===================================================================== Trivia high scores

        private static FrameworkElement TriviaScoresView(bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header("Trivia · High scores", compact, "AmberBrush"));
            var body = new StackPanel();
            stack.Children.Add(body);
            void Rebuild()
            {
                body.Children.Clear();
                int games = TriviaScores.Games;
                if (games == 0)
                {
                    body.Children.Add(Text("No games yet. Play trivia to set your first high score!", 12.5, "MutedBrush"));
                    return;
                }
                var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
                int asked = TriviaScores.TotalAsked;
                foreach (var (value, label) in new[]
                {
                    (TriviaScores.BestStreak.ToString(), "best streak"),
                    (games.ToString(), games == 1 ? "game" : "games"),
                    (asked == 0 ? "0%" : $"{Math.Round(100.0 * TriviaScores.TotalRight / asked)}%", "right"),
                })
                {
                    var cell = new StackPanel();
                    cell.Children.Add(Text(value, compact ? 18 : 22, "FgBrush", bold: true, wrap: false, font: "DisplayFont"));
                    cell.Children.Add(Text(label, 10.5, "MutedBrush", wrap: false));
                    stats.Children.Add(cell);
                }
                body.Children.Add(stats);

                var bests = TriviaScores.Bests();
                if (bests.Count == 0) body.Children.Add(Text("Finish all 10 questions in a game to set a high score.", 12, "MutedBrush"));
                foreach (var b in bests.Take(compact ? 4 : 20))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 3) };
                    var sc = Text($"{b.Score}/{b.OutOf}", 13, b.Score == b.OutOf ? "GoodBrush" : "FgBrush", bold: true, wrap: false, font: "DisplayFont");
                    DockPanel.SetDock(sc, Dock.Right);
                    row.Children.Add(sc);
                    var name = new StackPanel();
                    name.Children.Add(Text("🏆 " + Composer.Capital(TriviaScores.CategoryName(b.Category)) + " trivia", 12.5, "FgBrush", wrap: false));
                    name.Children.Add(Text("set " + AceV4Brain.DayPhrase(b.When), 10.5, "MutedBrush", wrap: false));
                    row.Children.Add(name);
                    body.Children.Add(row);
                }
                if (!compact)
                {
                    var recent = TriviaScores.Recent(8);
                    var h = Text("RECENT GAMES", 10.5, "MutedBrush", bold: true, wrap: false);
                    h.Margin = new Thickness(0, 10, 0, 4);
                    body.Children.Add(h);
                    foreach (var r in recent)
                        body.Children.Add(Text($"{Composer.Capital(TriviaScores.CategoryName(r.Category))} · {r.Score}/{r.OutOf}{(r.Full ? "" : " (stopped early)")} · {AceV4Brain.DayPhrase(r.When)}", 11.5, "FgBrush", wrap: false));
                }
            }
            var play = SmallButton("Play trivia", hot: true);
            play.Margin = new Thickness(0, 8, 0, 0);
            play.HorizontalAlignment = HorizontalAlignment.Left;
            play.Click += (_, _) => Ask("let's play trivia");
            stack.Children.Add(play);
            Live(stack, Rebuild, a => TriviaScores.Changed += a, a => TriviaScores.Changed -= a);
            return Box(stack, compact, "AmberBrush");
        }

        // ===================================================================== Script list

        private static FrameworkElement ScriptList(ScriptListCard card, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(card.Title, compact, "AceBrush"));
            var holder = new StackPanel();
            stack.Children.Add(holder);
            FrameworkElement? open = null;
            foreach (var s in card.Scripts.Take(compact ? 5 : 30))
                holder.Children.Add(ScriptRow(s, compact, view =>
                {
                    if (open != null) stack.Children.Remove(open);
                    open = view;
                    view.Margin = new Thickness(0, 8, 0, 0);
                    stack.Children.Add(view);
                }));
            if (!compact)
            {
                var all = SmallButton("Open the Scripts page");
                all.Margin = new Thickness(0, 8, 0, 0);
                all.HorizontalAlignment = HorizontalAlignment.Left;
                all.Click += (_, _) => { try { Services.Hub.Navigate("scripts"); } catch { } };
                stack.Children.Add(all);
            }
            return Box(stack, compact, "AceBrush");
        }

        /// <summary>One script in a list: name, kind and summary. Clicking it shows the code.</summary>
        public static FrameworkElement ScriptRow(ScriptEntry s, bool compact, Action<FrameworkElement> show)
        {
            var row = new DockPanel();
            var tag = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Top };
            tag.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
            tag.Child = Text(s.Lang == "lua" ? "Lua" : s.Kind == "Arduino" ? "Arduino" : s.Kind == "Unreal" ? "Unreal" : "C++", 10, s.Lang == "lua" ? "AceBrush" : "AccentBrush", bold: true, wrap: false);
            DockPanel.SetDock(tag, Dock.Left);
            row.Children.Add(tag);
            var col = new StackPanel();
            col.Children.Add(Text(s.Name, 12.5, "FgBrush", bold: true, wrap: false));
            if (!compact && s.Summary.Length > 0) col.Children.Add(Text(s.Summary, 11, "MutedBrush"));
            row.Children.Add(col);
            var b = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(-6, 0, -6, 2), ToolTip = "Show the code" };
            b.SetResourceReference(FrameworkElement.StyleProperty, "FlatRowButton");
            b.Click += (_, _) => show(Code(s, compact));
            return b;
        }
    }
}
