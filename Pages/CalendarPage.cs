using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Nimbus.AceV4;
using Nimbus.AceV4.Audio;
using Nimbus.Controls;
using Nimbus.Motion;
using Nimbus.Services.Calendar;

namespace Nimbus.Pages
{
    /// <summary>
    /// The Nimbus calendar: a month grid, the chosen day's events, and an editor to add, change and delete events.
    /// Everything is saved on this PC. Ace V4 reads and changes the same calendar (and asks first).
    /// </summary>
    public sealed class CalendarPage : UserControl, INimbusPage
    {
        private readonly Grid _root = new();
        private readonly UniformGrid _grid = new() { Columns = 7, Rows = 6 };
        private readonly TextBlock _monthTitle;
        private readonly TextBlock _dayTitle;
        private readonly StackPanel _dayList = new();
        private readonly Border _editorCard;
        private readonly TextBlock _editorTitle;
        private readonly TextBox _title = new(), _start = new(), _end = new(), _notes = new(), _date = new();
        private readonly CheckBox _allDay = new() { Content = "All day" };
        private readonly WrapPanel _colors = new(), _remind = new();
        private readonly TextBlock _editorError;
        private readonly Button _delete;
        private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime _selected = DateTime.Today;
        private CalendarEvent? _editing;
        private string _pickedColor = "violet";
        private int _pickedRemind = 10;
        private bool _confirmDelete;

        public CalendarPage()
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

            // ================= Month grid
            var monthCard = Card();
            monthCard.Margin = new Thickness(0, 4, 10, 0);
            var monthDock = new DockPanel();
            monthCard.Child = monthDock;

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var prev = Small("‹");
            prev.Click += (_, _) => ShowMonth(_month.AddMonths(-1));
            var next = Small("›");
            next.Click += (_, _) => ShowMonth(_month.AddMonths(1));
            var today = Small("Today");
            today.Click += (_, _) => { _selected = DateTime.Today; ShowMonth(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)); };
            var add = Hot("+ Add event");
            add.Click += (_, _) => OpenEditor(null);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(prev);
            buttons.Children.Add(next);
            buttons.Children.Add(today);
            buttons.Children.Add(add);
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            _monthTitle = AceCardViews.Text("", 20, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            _monthTitle.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(_monthTitle);
            DockPanel.SetDock(head, Dock.Top);
            monthDock.Children.Add(head);

            var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var d in new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" })
            {
                var t = AceCardViews.Text(d, 11, "MutedBrush", bold: true, wrap: false);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                weekdays.Children.Add(t);
            }
            DockPanel.SetDock(weekdays, Dock.Top);
            monthDock.Children.Add(weekdays);
            monthDock.Children.Add(_grid);
            _root.Children.Add(monthCard);

            // ================= Right: the day and the editor
            var side = new StackPanel { Margin = new Thickness(0, 4, 4, 0) };
            var sideScroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            Grid.SetColumn(sideScroll, 1);
            _root.Children.Add(sideScroll);

            var dayStack = new StackPanel();
            _dayTitle = AceCardViews.Text("", 15, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            _dayTitle.Margin = new Thickness(0, 0, 0, 6);
            dayStack.Children.Add(_dayTitle);
            dayStack.Children.Add(_dayList);
            var tip = AceCardViews.Text("Tip: ask Ace \"add pizza night Friday at 6\" or \"move soccer to 5\".", 11, "MutedBrush");
            tip.Margin = new Thickness(0, 8, 0, 0);
            dayStack.Children.Add(tip);
            var dayCard = Card();
            dayCard.Child = dayStack;
            side.Children.Add(dayCard);

            // Editor
            var ed = new StackPanel();
            _editorTitle = AceCardViews.Text("New event", 15, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            ed.Children.Add(_editorTitle);
            Pill(_title, "What is it?");
            Pill(_date, "Day, like Nov 20 or tomorrow");
            Pill(_start, "Start, like 3:30 PM");
            Pill(_end, "End, like 4:30 PM");
            Pill(_notes, "Notes (optional)");
            _notes.AcceptsReturn = true;
            _notes.TextWrapping = TextWrapping.Wrap;
            _notes.MinHeight = 54;
            ed.Children.Add(Field("Title", _title));
            ed.Children.Add(Field("Day", _date));
            _allDay.SetResourceReference(StyleProperty, "ToggleSwitch");
            _allDay.Margin = new Thickness(0, 4, 0, 4);
            _allDay.Checked += (_, _) => { _start.IsEnabled = _end.IsEnabled = false; };
            _allDay.Unchecked += (_, _) => { _start.IsEnabled = _end.IsEnabled = true; };
            ed.Children.Add(_allDay);
            var times = new Grid();
            times.ColumnDefinitions.Add(new ColumnDefinition());
            times.ColumnDefinitions.Add(new ColumnDefinition());
            var startField = Field("Start", _start);
            var endField = Field("End", _end);
            endField.Margin = new Thickness(6, 4, 0, 4);
            Grid.SetColumn(endField, 1);
            times.Children.Add(startField);
            times.Children.Add(endField);
            ed.Children.Add(times);

            ed.Children.Add(Label("Color"));
            foreach (var color in CalendarStore.Colors)
            {
                var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 6, 6), Background = AceCardViews.EventBrush(color), Cursor = Cursors.Hand, BorderThickness = new Thickness(2), Tag = color, ToolTip = color };
                dot.MouseLeftButtonUp += (_, _) => { _pickedColor = (string)dot.Tag; PaintColors(); Spring.Bump(dot); AceRuntime.Current.Play(Sfx.Tick, 0.5); };
                _colors.Children.Add(dot);
            }
            ed.Children.Add(_colors);

            ed.Children.Add(Label("Remind me"));
            foreach (var (label, minutes) in new[] { ("Off", 0), ("5 min", 5), ("10 min", 10), ("30 min", 30), ("1 hour", 60), ("1 day", 1440) })
            {
                var chip = new RadioButton { Content = label, GroupName = "calremind", Margin = new Thickness(0, 0, 4, 4), Tag = minutes };
                chip.SetResourceReference(StyleProperty, "FilterChip");
                chip.Checked += (_, _) => _pickedRemind = (int)chip.Tag;
                _remind.Children.Add(chip);
            }
            ed.Children.Add(_remind);
            ed.Children.Add(Field("Notes", _notes));

            _editorError = AceCardViews.Text("", 12, "BadBrush");
            _editorError.Visibility = Visibility.Collapsed;
            ed.Children.Add(_editorError);

            var edButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var save = Hot("Save");
            save.Click += (_, _) => Save();
            var cancel = Small("Cancel");
            cancel.Click += (_, _) => CloseEditor();
            _delete = Small("Delete");
            _delete.Click += (_, _) => Delete();
            edButtons.Children.Add(save);
            edButtons.Children.Add(cancel);
            edButtons.Children.Add(_delete);
            ed.Children.Add(edButtons);

            _editorCard = Card();
            _editorCard.Child = ed;
            _editorCard.Visibility = Visibility.Collapsed;
            _editorCard.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            side.Children.Add(_editorCard);

            Content = _root;
            CalendarStore.Changed += () => Dispatcher.BeginInvoke(() => { BuildGrid(); ShowDay(_selected); });
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && _editorCard.Visibility == Visibility.Visible) { CloseEditor(); e.Handled = true; }
            };
            ShowMonth(_month);
        }

        public Panel PopRoot => _root;

        public void OnShown()
        {
            BuildGrid();
            ShowDay(_selected);
        }

        // ===================================================================== Month and day

        private void ShowMonth(DateTime first)
        {
            _month = new DateTime(first.Year, first.Month, 1);
            if (_selected.Year != _month.Year || _selected.Month != _month.Month)
                _selected = _month.Year == DateTime.Today.Year && _month.Month == DateTime.Today.Month ? DateTime.Today : _month;
            BuildGrid();
            ShowDay(_selected);
            Spring.Bump(_monthTitle);
        }

        private void BuildGrid()
        {
            _monthTitle.Text = _month.ToString("MMMM yyyy");
            _grid.Children.Clear();
            var start = _month.AddDays(-(int)_month.DayOfWeek);
            var events = CalendarStore.Between(start, start.AddDays(42));
            for (int i = 0; i < 42; i++)
            {
                var day = start.AddDays(i);
                bool inMonth = day.Month == _month.Month;
                bool isToday = day == DateTime.Today;
                bool isSelected = day == _selected.Date;
                var dayEvents = events.Where(e => e.Start.Date <= day && (e.End > day || e.Start.Date == day)).OrderBy(e => e.Start).ToList();

                var cell = new Border { Margin = new Thickness(2), CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 4, 6, 4), Cursor = Cursors.Hand, BorderThickness = new Thickness(1.5) };
                if (isSelected) { cell.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); cell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); }
                else { cell.SetResourceReference(Border.BackgroundProperty, "SoftBrush"); cell.BorderBrush = Brushes.Transparent; }
                var stack = new StackPanel();
                var num = AceCardViews.Text(day.Day.ToString(), 12.5, isToday ? "AceBrush" : inMonth ? "FgBrush" : "MutedBrush", bold: isToday || isSelected, wrap: false);
                if (!inMonth) num.Opacity = 0.5;
                stack.Children.Add(num);
                foreach (var e in dayEvents.Take(2))
                {
                    var pill = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(0, 2, 0, 0), Background = AceCardViews.EventBrush(e.Color) };
                    pill.Background.Opacity = 0.85;
                    var t = AceCardViews.Text(e.Title, 10, "InkBrush", bold: true, wrap: false);
                    pill.Child = t;
                    stack.Children.Add(pill);
                }
                if (dayEvents.Count > 2) stack.Children.Add(AceCardViews.Text($"+{dayEvents.Count - 2} more", 9.5, "MutedBrush", wrap: false));
                cell.Child = stack;
                var d = day;
                cell.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount < 2) return;
                    e.Handled = true;
                    _selected = d;
                    OpenEditor(null);
                };
                cell.MouseLeftButtonUp += (_, _) =>
                {
                    if (d.Month != _month.Month) { _selected = d; ShowMonth(d); return; }
                    _selected = d;
                    BuildGrid();
                    ShowDay(d);
                    AceRuntime.Current.Play(Sfx.Tick, 0.4);
                };
                cell.MouseEnter += (_, _) => { if (cell.Background is SolidColorBrush) cell.Opacity = 0.92; };
                cell.MouseLeave += (_, _) => cell.Opacity = 1;
                _grid.Children.Add(cell);
            }
        }

        private void ShowDay(DateTime day)
        {
            _dayTitle.Text = day == DateTime.Today ? "Today · " + day.ToString("ddd MMM d") : day.ToString("dddd, MMMM d");
            _dayList.Children.Clear();
            var events = CalendarStore.OnDay(day);
            foreach (var e in events)
            {
                var row = AceCardViews.EventRow(e, showDay: false);
                var edit = new Button { Content = row, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(-6, 0, -6, 0), ToolTip = "Change or delete" + (e.Notes.Length > 0 ? "\n" + e.Notes : "") };
                edit.SetResourceReference(StyleProperty, "FlatRowButton");
                var ev = e;
                edit.Click += (_, _) => OpenEditor(ev);
                _dayList.Children.Add(edit);
            }
            if (events.Count == 0) _dayList.Children.Add(AceCardViews.Text("Nothing on this day. Double-click a day or press + Add event.", 12, "MutedBrush"));
            Spring.PopChildren(_dayList);
        }

        // ===================================================================== Editor

        private void OpenEditor(CalendarEvent? e)
        {
            _editing = e?.Copy();
            _confirmDelete = false;
            _delete.Content = "Delete";
            _delete.Visibility = e == null ? Visibility.Collapsed : Visibility.Visible;
            _editorError.Visibility = Visibility.Collapsed;
            _editorTitle.Text = e == null ? "New event" : "Change event";
            if (e == null)
            {
                var start = _selected.Date.AddHours(_selected.Date == DateTime.Today ? Math.Min(23, DateTime.Now.Hour + 1) : 16);
                _title.Text = "";
                _date.Text = start.ToString("ddd MMM d, yyyy");
                _allDay.IsChecked = false;
                _start.Text = start.ToString("h:mm tt");
                _end.Text = start.AddHours(1).ToString("h:mm tt");
                _notes.Text = "";
                _pickedColor = "violet";
                _pickedRemind = 10;
            }
            else
            {
                _title.Text = e.Title;
                _date.Text = e.Start.ToString("ddd MMM d, yyyy");
                _allDay.IsChecked = e.AllDay;
                _start.Text = e.Start.ToString("h:mm tt");
                _end.Text = e.End.ToString("h:mm tt");
                _notes.Text = e.Notes;
                _pickedColor = e.Color;
                _pickedRemind = e.RemindMinutes;
            }
            _start.IsEnabled = _end.IsEnabled = _allDay.IsChecked != true;
            PaintColors();
            foreach (var chip in _remind.Children.OfType<RadioButton>()) chip.IsChecked = (int)chip.Tag == _pickedRemind;
            if (!_remind.Children.OfType<RadioButton>().Any(c => c.IsChecked == true)) _remind.Children.OfType<RadioButton>().First().IsChecked = true;
            _editorCard.Visibility = Visibility.Visible;
            Spring.PopIn(_editorCard);
            _title.Focus();
        }

        private void CloseEditor()
        {
            _editing = null;
            _editorCard.Visibility = Visibility.Collapsed;
        }

        private void PaintColors()
        {
            foreach (var dot in _colors.Children.OfType<Border>())
            {
                if ((string)dot.Tag == _pickedColor) dot.SetResourceReference(Border.BorderBrushProperty, "FgBrush");
                else dot.BorderBrush = Brushes.Transparent;
            }
        }

        private void Save()
        {
            string title = _title.Text.Trim();
            if (title.Length == 0) { Error("Give it a name first."); return; }
            var day = ParseDay(_date.Text, _selected.Date);
            if (day == null) { Error("I couldn't read that day. Try \"Nov 20\", \"tomorrow\" or \"friday\"."); return; }
            bool allDay = _allDay.IsChecked == true;
            DateTime start, end;
            if (allDay)
            {
                start = day.Value.Date;
                end = start.AddDays(1);
            }
            else
            {
                var s = ParseClock(_start.Text);
                if (s == null) { Error("I couldn't read the start time. Try \"3:30 PM\"."); return; }
                var e = _end.Text.Trim().Length == 0 ? s.Value.Add(TimeSpan.FromHours(1)) : ParseClock(_end.Text);
                if (e == null) { Error("I couldn't read the end time. Try \"4:30 PM\"."); return; }
                start = day.Value.Date.Add(s.Value);
                end = day.Value.Date.Add(e.Value);
                if (end <= start) end = end.AddDays(1);   // "10 PM to 1 AM" goes past midnight
            }

            var ev = _editing ?? new CalendarEvent();
            bool timeChanged = ev.Start != start || ev.RemindMinutes != _pickedRemind;
            ev.Title = title;
            ev.Start = start;
            ev.End = end;
            ev.AllDay = allDay;
            ev.Notes = _notes.Text.Trim();
            ev.Color = _pickedColor;
            ev.RemindMinutes = _pickedRemind;
            if (timeChanged) ev.Reminded = false;
            if (_editing == null) CalendarStore.Add(ev); else CalendarStore.Update(ev);
            AceRuntime.Current.Play(Sfx.Success, 0.8);
            _selected = start.Date;
            CloseEditor();
            ShowMonth(new DateTime(start.Year, start.Month, 1));
        }

        private void Delete()
        {
            if (_editing == null) return;
            if (!_confirmDelete)
            {
                _confirmDelete = true;
                _delete.Content = "Sure? Tap again";
                _delete.SetResourceReference(BackgroundProperty, "BadBrush");
                Spring.Bump(_editorCard);
                return;
            }
            CalendarStore.Delete(_editing.Id);
            _delete.ClearValue(BackgroundProperty);
            AceRuntime.Current.Play(Sfx.Pop, 0.8);
            CloseEditor();
            BuildGrid();
            ShowDay(_selected);
        }

        private void Error(string text)
        {
            _editorError.Text = text;
            _editorError.Visibility = Visibility.Visible;
            Spring.Bump(_editorError);
            AceRuntime.Current.Play(Sfx.Error, 0.6);
        }

        // ===================================================================== Reading what was typed

        /// <summary>"Nov 20", "11/20", "2026-11-20", "tomorrow", "friday". Uses Ace's date reader for the words.</summary>
        public static DateTime? ParseDay(string text, DateTime fallback)
        {
            text = text.Trim();
            if (text.Length == 0) return fallback;
            var formats = new[] { "ddd MMM d, yyyy", "MMM d, yyyy", "MMMM d, yyyy", "ddd MMM d", "ddd MMM d yyyy", "MMM d", "MMM d yyyy", "MMMM d", "MMMM d yyyy", "M/d", "M/d/yyyy", "yyyy-MM-dd", "dddd, MMMM d" };
            if (DateTime.TryParseExact(text, formats, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
            {
                bool hasYear = Regex.IsMatch(text, @"\d{4}");
                if (!hasYear) exact = new DateTime(DateTime.Today.Year, exact.Month, exact.Day);
                return exact.Date;
            }
            try
            {
                var when = NaturalTime.Parse(text, DateTime.Now);
                if (when.Date != null) return when.Date.Value.Date;
            }
            catch { }
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var any)) return any.Date;
            return null;
        }

        /// <summary>"3", "3pm", "3:30 PM", "15:30", "noon", "midnight".</summary>
        public static TimeSpan? ParseClock(string text)
        {
            string t = text.Trim().ToLowerInvariant();
            if (t == "noon") return TimeSpan.FromHours(12);
            if (t == "midnight") return TimeSpan.Zero;
            var m = Regex.Match(t, @"^(\d{1,2})(?::(\d{2}))?\s*(a\.?m\.?|p\.?m\.?|am|pm|a|p)?$");
            if (!m.Success) return null;
            string ampm = m.Groups[3].Value.Replace(".", "");
            if (ampm == "a") ampm = "am";
            if (ampm == "p") ampm = "pm";
            return NaturalTime.ToTime(m.Groups[1].Value, m.Groups[2].Value, ampm);
        }

        // ===================================================================== Bits

        private static Border Card()
        {
            var b = new Border { Margin = new Thickness(0, 0, 0, 8) };
            b.SetResourceReference(StyleProperty, "Card");
            return b;
        }

        private static Button Small(string label)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
            b.SetResourceReference(StyleProperty, "SmallPillButton");
            return b;
        }

        private static Button Hot(string label)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
            b.SetResourceReference(StyleProperty, "HotPillButton");
            return b;
        }

        private static void Pill(TextBox box, string placeholder)
        {
            box.SetResourceReference(StyleProperty, "PillTextBox");
            box.Tag = placeholder;
            box.Margin = new Thickness(0, 2, 0, 0);
        }

        private static TextBlock Label(string text)
        {
            var t = AceCardViews.Text(text, 11, "MutedBrush", bold: true, wrap: false);
            t.Margin = new Thickness(0, 8, 0, 4);
            return t;
        }

        private static FrameworkElement Field(string label, Control box)
        {
            var s = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            var t = AceCardViews.Text(label, 11, "MutedBrush", bold: true, wrap: false);
            s.Children.Add(t);
            s.Children.Add(box);
            return s;
        }
    }
}
