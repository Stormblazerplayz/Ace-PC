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
using Nimbus.AceV4;
using Nimbus.AceV4.Pc;
using Nimbus.Motion;
using Nimbus.Services.Calendar;

namespace Nimbus.Controls
{
    /// <summary>
    /// Turns Ace V4's answers and cards into WPF views. Used by the Ace window.
    /// Recipes get a tickable ingredient list and numbered steps, searches get rows or thumbnails, confirm cards get
    /// Yes/No buttons and countdowns get a big number with Cancel.
    /// </summary>
    public static partial class AceCardViews
    {
        /// <summary>Raised when a card button produced a new line from Ace (a confirm result), so the page can show it.</summary>
        public static event Action<string>? CardSaid;

        // ===================================================================== Whole answer

        public static FrameworkElement Answer(AceAnswer a, bool compact, int maxCards = 3)
        {
            var stack = new StackPanel();
            if (!string.IsNullOrWhiteSpace(a.Text))
                stack.Children.Add(Text(a.Text, compact ? 13 : 14, "FgBrush", wrap: true));
            foreach (var card in a.Cards.Take(maxCards))
            {
                var view = Card(card, compact);
                if (view == null) continue;
                view.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(view);
            }
            return stack;
        }

        public static FrameworkElement? Card(AceCard card, bool compact) => card switch
        {
            RecipeCard r => Recipe(r, compact),
            ResultsCard r => Results(r, compact),
            InfoCard i => Info(i, compact),
            StatsCard s => Stats(s, compact),
            WeatherCard w => Weather(w, compact),
            CalendarCard c => Calendar(c, compact),
            SleepCard s => Sleep(s, compact),
            ConfirmCard c => Confirm(c, compact),
            CountdownCard c => Countdown(c, compact),
            CookingCard c => Cooking(c, compact),
            TimersCard t => Timers(t, compact),
            FocusCard => Focus(compact),
            ReportCard r => Report(r, compact),
            _ => MoreCard(card, compact),
        };

        // ===================================================================== Building blocks

        public static TextBlock Text(string text, double size, string brushKey, bool bold = false, bool wrap = true, string font = "BodyFont")
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            t.SetResourceReference(TextBlock.FontFamilyProperty, font);
            if (bold) t.FontWeight = FontWeights.Bold;
            return t;
        }

        private static Border Box(UIElement child, bool compact, string? accentKey = null)
        {
            var b = new Border
            {
                Child = child,
                CornerRadius = new CornerRadius(compact ? 14 : 16),
                Padding = compact ? new Thickness(12, 10, 12, 10) : new Thickness(14, 12, 14, 12),
                BorderThickness = new Thickness(1),
            };
            b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            b.SetResourceReference(Border.BorderBrushProperty, accentKey ?? "CardBorderBrush");
            return b;
        }

        private static TextBlock Header(string text, bool compact, string brushKey = "AceBrush")
        {
            var t = Text(text.ToUpperInvariant(), compact ? 10 : 11, brushKey, bold: true, wrap: false);
            t.Margin = new Thickness(0, 0, 0, 6);
            return t;
        }

        private static Button SmallButton(string label, bool hot = false)
        {
            var b = new Button { Content = label };
            b.SetResourceReference(FrameworkElement.StyleProperty, hot ? "HotPillButton" : "SmallPillButton");
            if (hot) { b.Padding = new Thickness(12, 4, 12, 4); b.FontSize = 11; }
            return b;
        }

        public static void Open(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try { AppLauncher.Start(target); } catch { }
        }

        private static ImageSource? Picture(string pathOrUrl, int decodeWidth)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl)) return null;
            try
            {
                Uri uri;
                if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) uri = new Uri(pathOrUrl);
                else if (File.Exists(pathOrUrl)) uri = new Uri(Path.GetFullPath(pathOrUrl));
                else return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.DecodePixelWidth = decodeWidth;
                // Local files load fully so the file isn't locked; web pictures download in the background.
                bmp.CacheOption = uri.IsFile ? BitmapCacheOption.OnLoad : BitmapCacheOption.Default;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        private static void Ui(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null) return;
            if (d.CheckAccess()) a(); else d.BeginInvoke(a);
        }

        // ===================================================================== Recipe

        private static FrameworkElement Recipe(RecipeCard r, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header("Recipe", compact, "PebbleBrush"));

            var top = new DockPanel();
            var pic = Picture(r.ImageUrl, 240);
            if (pic != null)
            {
                var img = new Border { Width = compact ? 64 : 92, Height = compact ? 64 : 92, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0), Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } };
                DockPanel.SetDock(img, Dock.Left);
                top.Children.Add(img);
            }
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text(r.Title, compact ? 15 : 18, "FgBrush", bold: true, font: "DisplayFont"));
            var meta = new List<string>();
            if (r.TotalTime.Length > 0) meta.Add("⏱ " + r.TotalTime);
            if (r.Yield.Length > 0) meta.Add("🍽 " + r.Yield);
            if (r.Source.Length > 0) meta.Add(r.Source);
            if (meta.Count > 0) titles.Children.Add(Text(string.Join("   ·   ", meta), 11.5, "MutedBrush"));
            top.Children.Add(titles);
            stack.Children.Add(top);

            if (r.Ingredients.Count > 0)
            {
                var h = Header($"Ingredients · {r.Ingredients.Count}  (tap to tick off)", compact, "MutedBrush");
                h.Margin = new Thickness(0, 12, 0, 4);
                stack.Children.Add(h);
                int max = compact ? 8 : 40;
                foreach (var ing in r.Ingredients.Take(max)) stack.Children.Add(IngredientRow(ing));
                if (r.Ingredients.Count > max) stack.Children.Add(Text($"+{r.Ingredients.Count - max} more on the Ace page", 11, "MutedBrush"));
            }
            if (r.Steps.Count > 0)
            {
                var h = Header($"Steps · {r.Steps.Count}", compact, "MutedBrush");
                h.Margin = new Thickness(0, 12, 0, 4);
                stack.Children.Add(h);
                int max = compact ? 3 : 30;
                int n = 1;
                foreach (var step in r.Steps.Take(max)) stack.Children.Add(StepRow(n++, step, r.Title));
                if (r.Steps.Count > max) stack.Children.Add(Text($"+{r.Steps.Count - max} more steps", 11, "MutedBrush"));
            }
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (r.Ingredients.Count > 0)
            {
                var shop = SmallButton("🛒 Add to shopping list");
                shop.Margin = new Thickness(0, 0, 6, 0);
                shop.Click += (_, _) =>
                {
                    int n = 0;
                    foreach (var ing in r.Ingredients)
                    {
                        if (ShoppingList.Skip(ing)) continue;
                        ShoppingList.Add(ing, r.Title);
                        n++;
                    }
                    shop.Content = "Added ✓";
                    shop.IsEnabled = false;
                    CardSaid?.Invoke($"Added {n} ingredient{(n == 1 ? "" : "s")} for {r.Title} to your shopping list.");
                };
                actions.Children.Add(shop);
            }
            if (r.Url.Length > 0)
            {
                var open = SmallButton("Open full recipe ↗");
                open.Click += (_, _) => Open(r.Url);
                actions.Children.Add(open);
            }
            if (actions.Children.Count > 0) stack.Children.Add(actions);
            return Box(stack, compact);
        }

        private static FrameworkElement IngredientRow(string text)
        {
            var dot = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Top };
            dot.SetResourceReference(Border.BorderBrushProperty, "PebbleBrush");
            var check = Text("✓", 11, "InkBrush", bold: true, wrap: false);
            check.HorizontalAlignment = HorizontalAlignment.Center;
            check.VerticalAlignment = VerticalAlignment.Center;
            check.Visibility = Visibility.Hidden;
            dot.Child = check;
            var label = Text(text, 12.5, "FgBrush");
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(label);
            bool done = false;
            row.MouseLeftButtonUp += (_, _) =>
            {
                done = !done;
                check.Visibility = done ? Visibility.Visible : Visibility.Hidden;
                if (done) dot.SetResourceReference(Border.BackgroundProperty, "PebbleBrush"); else dot.Background = Brushes.Transparent;
                label.TextDecorations = done ? TextDecorations.Strikethrough : null;
                label.Opacity = done ? 0.5 : 1;
                Spring.Bump(dot);
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.6);
            };
            return row;
        }

        private static FrameworkElement StepRow(int n, string text, string recipeName = "")
        {
            var num = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Top };
            num.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            var t = Text(n.ToString(), 10.5, "FgBrush", bold: true, wrap: false);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            num.Child = t;
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            DockPanel.SetDock(num, Dock.Left);
            row.Children.Add(num);
            // A step with a wait ("Bake for 25 minutes") gets a timer button set to exactly that time.
            var wait = AceV4.Brain.StepTimer.Read(text);
            if (wait != null)
            {
                var timer = TimerButton(wait, recipeName);
                timer.Margin = new Thickness(8, 0, 0, 0);
                timer.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(timer, Dock.Right);
                row.Children.Add(timer);
            }
            row.Children.Add(Text(text, 12.5, "FgBrush"));
            return row;
        }

        /// <summary>"⏱ 25 min" — starts a labeled timer ("Fudgy Brownies: bake"). Several can run at once.</summary>
        private static Button TimerButton(AceV4.Brain.StepWait wait, string recipeName)
        {
            var b = SmallButton("⏱ " + AceV4.Brain.StepTimer.Short(wait.Length));
            b.ToolTip = $"Start a {AceV4.Brain.StepTimer.Say(wait.Length)} {wait.Label} timer";
            b.Click += (_, _) =>
            {
                string label = (recipeName.Length > 0 ? recipeName + ": " : "") + wait.Label;
                ReminderStore.Add(label, DateTime.Now + wait.Length, isTimer: true);
                b.Content = "✓ Started";
                b.IsEnabled = false;
                Spring.Bump(b);
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.7);
                CardSaid?.Invoke($"Started a {AceV4.Brain.StepTimer.Say(wait.Length)} {wait.Label} timer. I'll ping you when it's done.");
                // Let it be pressed again later (a second batch of cookies).
                var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                again.Tick += (_, _) => { again.Stop(); b.Content = "⏱ " + AceV4.Brain.StepTimer.Short(wait.Length); b.IsEnabled = true; };
                again.Start();
            };
            return b;
        }

        // ===================================================================== Cooking mode

        private static FrameworkElement Cooking(CookingCard c, bool compact)
        {
            var s = c.Session;
            var stack = new StackPanel();
            var header = Header("Cooking · " + s.Recipe.Name, compact, "PebbleBrush");
            stack.Children.Add(header);

            var progress = new ProgressBar { Height = 5, Minimum = 0, Maximum = Math.Max(1, s.Count), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(0) };
            progress.SetResourceReference(Control.ForegroundProperty, "PebbleBrush");
            progress.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush");
            stack.Children.Add(progress);

            var count = Text("", 11.5, "MutedBrush", bold: true, wrap: false);
            stack.Children.Add(count);
            var step = Text("", compact ? 15 : 19, "FgBrush", bold: true, font: "DisplayFont");
            step.Margin = new Thickness(0, 2, 0, 8);
            stack.Children.Add(step);

            var buttons = new WrapPanel();
            var back = SmallButton("← Back");
            var next = SmallButton("Next →", hot: true);
            var timer = SmallButton("⏱");
            var stop = SmallButton("Stop");
            foreach (var b in new[] { back, next, timer, stop }) { b.Margin = new Thickness(0, 0, 6, 4); buttons.Children.Add(b); }
            stack.Children.Add(buttons);

            void Refresh()
            {
                if (s.Finished)
                {
                    count.Text = "Done!";
                    step.Text = "Enjoy your " + s.Recipe.Name + ".";
                    progress.Value = progress.Maximum;
                    buttons.Visibility = Visibility.Collapsed;
                    return;
                }
                count.Text = $"STEP {s.Step + 1} OF {s.Count}";
                step.Text = s.Current;
                progress.Value = s.Step + 1;
                back.IsEnabled = s.Step > 0;
                next.Content = s.IsLast ? "Finish ✓" : "Next →";
                var wait = s.Wait;
                timer.Visibility = wait == null ? Visibility.Collapsed : Visibility.Visible;
                if (wait != null) { timer.Content = "⏱ " + AceV4.Brain.StepTimer.Short(wait.Length) + " " + wait.Label; timer.IsEnabled = true; }
                Spring.Bump(step);
            }
            Action changed = () => Ui(Refresh);
            stack.Loaded += (_, _) => { s.Changed -= changed; s.Changed += changed; Refresh(); };
            stack.Unloaded += (_, _) => s.Changed -= changed;
            s.Changed += changed;
            Refresh();

            next.Click += (_, _) =>
            {
                if (!s.Next()) { s.Finish(); CardSaid?.Invoke($"That was the last step! Enjoy your {s.Recipe.Name}."); }
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.6);
            };
            back.Click += (_, _) => { s.Back(); AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.6); };
            stop.Click += (_, _) => { s.Finish(); CardSaid?.Invoke("Cooking mode off. Your timers keep running."); };
            timer.Click += (_, _) =>
            {
                var wait = s.Wait;
                if (wait == null) return;
                ReminderStore.Add(s.Recipe.Name + ": " + wait.Label, DateTime.Now + wait.Length, isTimer: true);
                timer.Content = "✓ Timer on";
                timer.IsEnabled = false;
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.7);
                CardSaid?.Invoke($"Started a {AceV4.Brain.StepTimer.Say(wait.Length)} {wait.Label} timer. I'll ping you when it's done.");
            };
            return Box(stack, compact, "PebbleBrush");
        }

        // ===================================================================== Focus timer

        private static FrameworkElement Focus(bool compact)
        {
            var stack = new StackPanel();
            var header = Header("Focus timer", compact, "GoodBrush");
            stack.Children.Add(header);

            var top = new DockPanel();
            var time = Text("", compact ? 26 : 32, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            time.Width = compact ? 92 : 112;
            DockPanel.SetDock(time, Dock.Left);
            top.Children.Add(time);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var phase = Text("", 12.5, "FgBrush", bold: true, wrap: false);
            var task = Text("", 11.5, "MutedBrush", wrap: false);
            info.Children.Add(phase);
            info.Children.Add(task);
            top.Children.Add(info);
            stack.Children.Add(top);

            var progress = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 6), BorderThickness = new Thickness(0) };
            progress.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush");
            stack.Children.Add(progress);

            // One dot per round in a set of 4: filled = done.
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var dotList = new List<System.Windows.Shapes.Ellipse>();
            for (int i = 0; i < FocusTimer.RoundsBeforeLongBreak; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 6, 0), StrokeThickness = 1.4 };
                dot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "GoodBrush");
                dotList.Add(dot);
                dots.Children.Add(dot);
            }
            var roundText = Text("", 11, "MutedBrush", wrap: false);
            roundText.Margin = new Thickness(4, -2, 0, 0);
            dots.Children.Add(roundText);
            stack.Children.Add(dots);

            var buttons = new WrapPanel();
            var pause = SmallButton("Pause");
            var skip = SmallButton("Skip");
            var stop = SmallButton("Stop");
            var start = SmallButton("Start focus", hot: true);
            foreach (var b in new[] { start, pause, skip, stop }) { b.Margin = new Thickness(0, 0, 6, 4); buttons.Children.Add(b); }
            stack.Children.Add(buttons);

            void Refresh()
            {
                var now = DateTime.Now;
                bool on = FocusTimer.Running;
                start.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                pause.Visibility = skip.Visibility = stop.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (!on)
                {
                    int today = FocusTimer.MinutesToday(now);
                    time.Text = "--:--";
                    phase.Text = "Not running";
                    task.Text = today > 0 ? $"{FocusTimer.Say(today)} of focus today" : "25 min focus, 5 min break";
                    progress.Value = 0;
                    header.Text = "FOCUS TIMER";
                    foreach (var d in dotList) d.Fill = Brushes.Transparent;
                    roundText.Text = "";
                    return;
                }
                var left = FocusTimer.LeftAt(now);
                time.Text = left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");
                bool rest = FocusTimer.OnBreak;
                header.Text = rest ? "BREAK TIME" : "FOCUS TIMER";
                header.SetResourceReference(TextBlock.ForegroundProperty, rest ? "AceBrush" : "GoodBrush");
                progress.SetResourceReference(Control.ForegroundProperty, rest ? "AceBrush" : "GoodBrush");
                phase.Text = (rest ? FocusTimer.PhaseName : $"Round {FocusTimer.Round}") + (FocusTimer.Paused ? " · paused" : "");
                task.Text = FocusTimer.Task.Length > 0 ? AceV4.Brain.Composer.Capital(FocusTimer.Task) : rest ? "Stretch, drink some water, look away from the screen" : $"{FocusTimer.FocusMinutes} minutes of focus";
                progress.Value = FocusTimer.ProgressAt(now);
                pause.Content = FocusTimer.Paused ? "Resume" : "Pause";
                skip.Content = rest ? "Skip break" : "Skip";
                int inSet = FocusTimer.RoundsDone % FocusTimer.RoundsBeforeLongBreak;
                if (FocusTimer.Phase == FocusPhase.LongBreak) inSet = FocusTimer.RoundsBeforeLongBreak;
                for (int i = 0; i < dotList.Count; i++)
                {
                    if (i < inSet) dotList[i].SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "GoodBrush");
                    else dotList[i].Fill = Brushes.Transparent;
                }
                roundText.Text = FocusTimer.RoundsDone > 0 ? AceV4.Brain.Composer.Plural(FocusTimer.RoundsDone, "round", "rounds") + " done" : "";
            }

            var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (_, _) => Refresh();
            Action changed = () => Ui(Refresh);
            stack.Loaded += (_, _) => { FocusTimer.Changed -= changed; FocusTimer.Changed += changed; Refresh(); clock.Start(); };
            stack.Unloaded += (_, _) => { FocusTimer.Changed -= changed; clock.Stop(); };
            Refresh();

            start.Click += (_, _) =>
            {
                int minutes = AceRuntime.Current.Config.FocusMinutes;
                FocusTimer.Start(DateTime.Now, minutes);
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.7);
                CardSaid?.Invoke($"Focus timer on: {minutes} minutes. I'll tell you when it's break time.");
            };
            pause.Click += (_, _) =>
            {
                var now = DateTime.Now;
                if (FocusTimer.Paused) { FocusTimer.Resume(now); CardSaid?.Invoke("Back to it!"); }
                else { FocusTimer.Pause(now); CardSaid?.Invoke("Paused. Press Resume when you're ready."); }
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.6);
            };
            skip.Click += (_, _) =>
            {
                string? said = FocusTimer.Skip(DateTime.Now);
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.6);
                if (said != null) CardSaid?.Invoke(said);
            };
            stop.Click += (_, _) =>
            {
                int minutes = FocusTimer.Stop(DateTime.Now);
                AceRuntime.Current.Play(AceV4.Audio.Sfx.Tick, 0.6);
                CardSaid?.Invoke(minutes >= 1 ? $"Focus timer stopped. You focused for {FocusTimer.Say(minutes)}." : "Okay, I stopped the focus timer.");
            };
            return Box(stack, compact, "GoodBrush");
        }

        // ===================================================================== Weekly PC report card

        private static FrameworkElement Report(ReportCard r, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(r.Title, compact));
            string gradeBrush = r.Grade.StartsWith("A") || r.Grade.StartsWith("B") ? "GoodBrush" : r.Grade.StartsWith("C") ? "AmberBrush" : "BadBrush";

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var grade = Text(r.Grade, compact ? 34 : 44, gradeBrush, bold: true, wrap: false, font: "DisplayFont");
            grade.Margin = new Thickness(0, -6, 14, 0);
            grade.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(grade, Dock.Left);
            top.Children.Add(grade);
            var sub = Text(r.Subtitle, 11.5, "MutedBrush");
            sub.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(sub);
            stack.Children.Add(top);

            foreach (var line in r.Lines)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 84 : 100) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(Text(line.Label, 12, "MutedBrush", wrap: false));
                string lb = line.Grade.StartsWith("A") || line.Grade.StartsWith("B") ? "GoodBrush" : line.Grade.StartsWith("C") ? "AmberBrush" : "BadBrush";
                var lg = Text(line.Grade, 13, lb, bold: true, wrap: false);
                Grid.SetColumn(lg, 1);
                g.Children.Add(lg);
                var detail = Text(line.Detail, 12, "FgBrush");
                Grid.SetColumn(detail, 2);
                g.Children.Add(detail);
                stack.Children.Add(g);
            }
            if (r.Tips.Count > 0)
            {
                var tipsHeader = Text("TIPS", 10.5, "MutedBrush", bold: true, wrap: false);
                tipsHeader.Margin = new Thickness(0, 8, 0, 2);
                stack.Children.Add(tipsHeader);
                foreach (var tip in r.Tips.Take(compact ? 2 : 5))
                {
                    var t = Text("• " + tip, 12, "FgBrush");
                    t.Margin = new Thickness(0, 1, 0, 1);
                    stack.Children.Add(t);
                }
            }
            return Box(stack, compact);
        }

        // ===================================================================== Running timers

        private static FrameworkElement Timers(TimersCard card, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(card.Title, compact, "PebbleBrush"));
            var list = new StackPanel();
            stack.Children.Add(list);
            var empty = Text("No timers running.", 12, "MutedBrush");

            var rows = new Dictionary<string, (TextBlock Left, Reminder R)>();
            void Rebuild()
            {
                list.Children.Clear();
                rows.Clear();
                var timers = ReminderStore.Pending().Where(r => r.IsTimer).ToList();
                if (timers.Count == 0) { list.Children.Add(empty); return; }
                foreach (var r in timers)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    var cancel = SmallButton("✕");
                    cancel.ToolTip = "Cancel this timer";
                    cancel.Margin = new Thickness(8, 0, 0, 0);
                    string id = r.Id;
                    cancel.Click += (_, _) => { ReminderStore.Cancel(id); CardSaid?.Invoke("Cancelled the " + (r.Text.Length > 0 ? r.Text : "timer") + "."); };
                    DockPanel.SetDock(cancel, Dock.Right);
                    row.Children.Add(cancel);
                    var left = Text("", compact ? 15 : 17, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
                    left.Width = compact ? 70 : 84;
                    DockPanel.SetDock(left, Dock.Left);
                    row.Children.Add(left);
                    row.Children.Add(Text(r.Text.Length > 0 ? r.Text : "Timer", 12.5, "FgBrush", wrap: false));
                    list.Children.Add(row);
                    rows[id] = (left, r);
                }
                Tick();
            }
            void Tick()
            {
                foreach (var (left, r) in rows.Values)
                {
                    var span = r.Due - DateTime.Now;
                    if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                    left.Text = span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
                }
            }
            var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (_, _) => Tick();
            Action changed = () => Ui(Rebuild);
            stack.Loaded += (_, _) => { ReminderStore.Changed -= changed; ReminderStore.Changed += changed; Rebuild(); clock.Start(); };
            stack.Unloaded += (_, _) => { ReminderStore.Changed -= changed; clock.Stop(); };
            Rebuild();
            return Box(stack, compact, "PebbleBrush");
        }

        // ===================================================================== Search results

        private static FrameworkElement Results(ResultsCard r, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(r.Title.Length > 0 ? r.Title : "Results", compact));
            int max = compact ? 4 : 20;
            if (r.Thumbnails)
            {
                var wrap = new WrapPanel();
                foreach (var item in r.Items.Take(compact ? 6 : 24))
                {
                    var pic = Picture(item.ImagePath.Length > 0 ? item.ImagePath : item.Target, 220);
                    var tile = new Border { Width = compact ? 92 : 132, Height = compact ? 62 : 86, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = item.Title + (item.Subtitle.Length > 0 ? "\n" + item.Subtitle : "") };
                    if (pic != null) tile.Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill };
                    else { tile.SetResourceReference(Border.BackgroundProperty, "SoftBrush"); tile.Child = Text(item.Title, 10, "MutedBrush"); }
                    var target = item.Target;
                    tile.MouseLeftButtonUp += (_, _) => Open(target);
                    tile.MouseEnter += (_, _) => Spring.Bump(tile);
                    wrap.Children.Add(tile);
                }
                stack.Children.Add(wrap);
            }
            else
            {
                foreach (var item in r.Items.Take(max))
                {
                    var row = new StackPanel();
                    row.Children.Add(Text(item.Title, 13, "FgBrush", bold: true, wrap: false));
                    if (item.Subtitle.Length > 0) row.Children.Add(Text(item.Subtitle, 11.5, "MutedBrush", wrap: !compact));
                    var b = new Button { Content = row, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, -8, 0), ToolTip = item.Target };
                    b.SetResourceReference(FrameworkElement.StyleProperty, "FlatRowButton");
                    var target = item.Target;
                    b.Click += (_, _) => Open(target);
                    stack.Children.Add(b);
                }
            }
            if (r.Items.Count > max && !r.Thumbnails) stack.Children.Add(Text($"+{r.Items.Count - max} more", 11, "MutedBrush"));
            if (r.Items.Count == 0) stack.Children.Add(Text("Nothing found.", 12, "MutedBrush"));
            return Box(stack, compact);
        }

        // ===================================================================== Info, stats, weather

        private static FrameworkElement Info(InfoCard i, bool compact)
        {
            var dock = new DockPanel();
            var pic = Picture(i.ImageUrl, 200);
            if (pic != null)
            {
                var img = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(12), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } };
                DockPanel.SetDock(img, Dock.Right);
                dock.Children.Add(img);
            }
            var stack = new StackPanel();
            if (i.Title.Length > 0) stack.Children.Add(Text(i.Title, compact ? 14 : 16, "FgBrush", bold: true, font: "DisplayFont"));
            if (i.Body.Length > 0)
            {
                string body = compact && i.Body.Length > 260 ? i.Body[..257].TrimEnd() + "…" : i.Body;
                var t = Text(body, 12.5, "FgBrush");
                t.Margin = new Thickness(0, 4, 0, 0);
                t.Opacity = 0.9;
                stack.Children.Add(t);
            }
            if (i.Source.Length > 0 || i.Url.Length > 0)
            {
                var src = Text("From " + (i.Source.Length > 0 ? i.Source : "the web") + (i.Url.Length > 0 ? " ↗" : ""), 11, "AceBrush", wrap: false);
                src.Margin = new Thickness(0, 6, 0, 0);
                if (i.Url.Length > 0) { src.Cursor = Cursors.Hand; var url = i.Url; src.MouseLeftButtonUp += (_, _) => Open(url); }
                stack.Children.Add(src);
            }
            dock.Children.Add(stack);
            return Box(dock, compact);
        }

        private static FrameworkElement Stats(StatsCard s, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(s.Title.Length > 0 ? s.Title : "PC check", compact));
            foreach (var row in s.Rows.Take(compact ? 6 : 20))
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 86 : 110) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = Text(row.Label, 12, "MutedBrush", wrap: false);
                g.Children.Add(label);
                if (row.Percent >= 0)
                {
                    var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
                    var bg = new Border { CornerRadius = new CornerRadius(3) };
                    bg.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
                    var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                    fill.SetResourceReference(Border.BackgroundProperty, row.Warn ? "WarnBrush" : "AceBrush");
                    track.Children.Add(bg);
                    track.Children.Add(fill);
                    double pct = Math.Clamp(row.Percent, 0, 100) / 100;
                    track.SizeChanged += (_, e) => Spring.To(fill, FrameworkElement.WidthProperty, Math.Max(3, e.NewSize.Width * pct), 700, Spring.Soft);
                    Grid.SetColumn(track, 1);
                    g.Children.Add(track);
                }
                var value = Text(row.Value, 12, row.Warn ? "WarnBrush" : "FgBrush", bold: true, wrap: false);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(value, row.Percent >= 0 ? 2 : 1);
                if (row.Percent < 0) Grid.SetColumnSpan(value, 2);
                g.Children.Add(value);
                stack.Children.Add(g);
            }
            return Box(stack, compact);
        }

        private static FrameworkElement Weather(WeatherCard w, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header("Weather" + (w.Place.Length > 0 ? " · " + w.Place : ""), compact));
            stack.Children.Add(Text(w.Now, compact ? 20 : 26, "FgBrush", bold: true, font: "DisplayFont"));
            if (w.Detail.Length > 0) stack.Children.Add(Text(w.Detail, 12, "MutedBrush"));
            if (w.Days.Count > 0)
            {
                var days = new UniformGrid { Columns = Math.Min(compact ? 3 : 5, w.Days.Count), Margin = new Thickness(0, 8, 0, 0) };
                foreach (var (day, text) in w.Days.Take(compact ? 3 : 5))
                {
                    var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
                    cell.Children.Add(Text(day, 11, "AceBrush", bold: true, wrap: false));
                    cell.Children.Add(Text(text, 11.5, "FgBrush"));
                    days.Children.Add(cell);
                }
                stack.Children.Add(days);
            }
            return Box(stack, compact);
        }

        // ===================================================================== Calendar and sleep

        public static Brush EventBrush(string color) => color switch
        {
            "blue" => new SolidColorBrush(Color.FromRgb(0x5B, 0xB6, 0xFF)),
            "green" => new SolidColorBrush(Color.FromRgb(0x45, 0xD4, 0x83)),
            "amber" => new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)),
            "pink" => new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x8F)),
            "red" => new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x4E)),
            _ => new SolidColorBrush(Color.FromRgb(0x8F, 0x7C, 0xFF)),
        };

        public static FrameworkElement EventRow(CalendarEvent e, bool showDay)
        {
            var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = EventBrush(e.Color), Margin = new Thickness(0, 0, 10, 0) };
            var text = new StackPanel();
            text.Children.Add(Text(e.Title, 13, "FgBrush", bold: true, wrap: false));
            text.Children.Add(Text(showDay ? e.WhenText : (e.AllDay ? "All day" : e.Start.ToString("h:mm tt") + " – " + e.End.ToString("h:mm tt")), 11.5, "MutedBrush", wrap: false));
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            DockPanel.SetDock(bar, Dock.Left);
            row.Children.Add(bar);
            row.Children.Add(text);
            return row;
        }

        private static FrameworkElement Calendar(CalendarCard c, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(c.Title.Length > 0 ? c.Title : "Calendar", compact, "AccentBrush"));
            bool multiDay = c.Events.Select(e => e.Start.Date).Distinct().Count() > 1;
            foreach (var e in c.Events.Take(compact ? 4 : 20)) stack.Children.Add(EventRow(e, multiDay));
            if (c.Events.Count == 0) stack.Children.Add(Text("Nothing planned.", 12, "MutedBrush"));
            var open = SmallButton("Open calendar");
            open.Margin = new Thickness(0, 8, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (_, _) => Nimbus.Services.Hub.Navigate("calendar");
            stack.Children.Add(open);
            return Box(stack, compact);
        }

        private static FrameworkElement Sleep(SleepCard s, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header("Sleep" + (s.Nights > 0 ? $" · last {s.Nights} nights" : ""), compact, "AccentBrush"));
            stack.Children.Add(Text("Go to bed around " + s.Suggest, compact ? 16 : 20, "FgBrush", bold: true, font: "DisplayFont"));
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (label, value) in new[] { ("Usual bed", s.AvgBed), ("Usual wake", s.AvgWake), ("Usual sleep", s.AvgSleep) })
            {
                var cell = new StackPanel();
                cell.Children.Add(Text(label, 10.5, "MutedBrush", wrap: false));
                cell.Children.Add(Text(value.Length > 0 ? value : "–", 13, "FgBrush", bold: true, wrap: false));
                grid.Children.Add(cell);
            }
            stack.Children.Add(grid);
            return Box(stack, compact);
        }

        // ===================================================================== Confirm and countdown

        private static FrameworkElement Confirm(ConfirmCard c, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Text(c.Question, compact ? 13 : 14, "FgBrush", bold: true));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var yes = SmallButton(c.YesLabel, hot: true);
            var no = SmallButton(c.NoLabel);
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            stack.Children.Add(buttons);
            var result = Text("", 12, "MutedBrush");
            result.Margin = new Thickness(0, 8, 0, 0);
            result.Visibility = Visibility.Collapsed;
            stack.Children.Add(result);

            void ShowResult(string text)
            {
                buttons.Visibility = Visibility.Collapsed;
                result.Text = text;
                result.Visibility = Visibility.Visible;
                Spring.PopIn(result);
            }
            if (c.Answered) ShowResult(c.Result);
            Action<string> onResolved = text => Ui(() => ShowResult(text));
            c.Resolved += onResolved;
            stack.Unloaded += (_, _) => c.Resolved -= onResolved;
            stack.Loaded += (_, _) => { c.Resolved -= onResolved; c.Resolved += onResolved; if (c.Answered) ShowResult(c.Result); };

            async Task Press(bool answer)
            {
                yes.IsEnabled = no.IsEnabled = false;
                AceRuntime.Current.Play(answer ? AceV4.Audio.Sfx.Success : AceV4.Audio.Sfx.Pop, 0.8);
                string? said = await c.AnswerAsync(answer);
                if (said != null) CardSaid?.Invoke(said);
            }
            yes.Click += async (_, _) => await Press(true);
            no.Click += async (_, _) => await Press(false);
            return Box(stack, compact, "AccentBrush");
        }

        private static FrameworkElement Countdown(CountdownCard c, bool compact)
        {
            var stack = new StackPanel();
            stack.Children.Add(Header(c.Title.Length > 0 ? c.Title : "Countdown", compact, "BadBrush"));
            var row = new DockPanel();
            var number = Text(c.Left > 0 ? c.Left.ToString() : c.Seconds.ToString(), compact ? 34 : 44, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            number.RenderTransformOrigin = new Point(0.5, 0.5);
            var cancel = SmallButton("Cancel", hot: true);
            cancel.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(cancel, Dock.Right);
            row.Children.Add(cancel);
            row.Children.Add(number);
            stack.Children.Add(row);
            var note = Text("Press Cancel or say \"cancel\" to stop it.", 11.5, "MutedBrush");
            stack.Children.Add(note);

            void Refresh()
            {
                if (c.Cancelled) { number.Text = "Cancelled"; number.FontSize = 20; cancel.Visibility = Visibility.Collapsed; note.Text = "Okay, nothing will happen."; return; }
                if (c.Done) { number.Text = "Now"; cancel.Visibility = Visibility.Collapsed; note.Text = "Here it goes…"; return; }
                number.Text = c.Left.ToString();
                if (c.Left <= 3) number.SetResourceReference(TextBlock.ForegroundProperty, "BadBrush");
                Spring.Bump(number);
            }
            Action<int> onTick = _ => Ui(Refresh);
            Action onDone = () => Ui(Refresh);
            void Hook() { c.Tick -= onTick; c.Finished -= onDone; c.Tick += onTick; c.Finished += onDone; }
            void Unhook() { c.Tick -= onTick; c.Finished -= onDone; }
            stack.Loaded += (_, _) => { Hook(); Refresh(); };
            stack.Unloaded += (_, _) => Unhook();
            Hook();
            cancel.Click += (_, _) =>
            {
                if (c.Cancel()) CardSaid?.Invoke("Cancelled. Nothing will happen.");
            };
            return Box(stack, compact, "BadBrush");
        }
    }
}
