using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nimbus.AceV4.Brain;
using Nimbus.Controls;
using Nimbus.Motion;

namespace Nimbus.Pages
{
    /// <summary>
    /// Ace's built-in script library: ready-to-use Roblox Lua scripts and C++ (Arduino, console games, Unreal).
    /// Filter and search on the left, the code with colors, Copy and Save on the right. All offline.
    /// </summary>
    public sealed class ScriptsPage : UserControl, INimbusPage
    {
        private readonly Grid _root = new();
        private readonly StackPanel _list = new();
        private readonly ContentControl _code = new();
        private readonly TextBox _search = new();
        private readonly TextBlock _count;
        private string _filter = "";
        private ScriptEntry? _selected;

        public ScriptsPage()
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ================= Left: filters, search and the list
            var left = Card();
            left.Margin = new Thickness(0, 4, 10, 0);
            var leftDock = new DockPanel();
            left.Child = leftDock;

            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(AceCardViews.Text("Script library", 20, "FgBrush", bold: true, wrap: false, font: "DisplayFont"));
            _count = AceCardViews.Text("", 11.5, "MutedBrush");
            top.Children.Add(_count);

            var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            foreach (var (label, key) in new[] { ("All", ""), ("Roblox Lua", "lua"), ("Arduino", "arduino"), ("C++ games", "console"), ("Unreal", "unreal") })
            {
                var chip = new RadioButton { Content = label, GroupName = "scriptfilter", Margin = new Thickness(0, 0, 4, 4), IsChecked = key.Length == 0 };
                chip.SetResourceReference(StyleProperty, "FilterChip");
                string k = key;
                chip.Checked += (_, _) => { _filter = k; Fill(); };
                chips.Children.Add(chip);
            }
            top.Children.Add(chips);

            var hint = AceCardViews.Text("Search: kill brick, servo, door…", 12, "MutedBrush", wrap: false);
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(11, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            _search.Padding = new Thickness(8, 5, 8, 5);
            _search.FontSize = 12.5;
            _search.SetResourceReference(BackgroundProperty, "SoftBrush");
            _search.SetResourceReference(ForegroundProperty, "FgBrush");
            _search.SetResourceReference(BorderBrushProperty, "CardBorderBrush");
            _search.SetResourceReference(TextBox.CaretBrushProperty, "FgBrush");
            _search.TextChanged += (_, _) => { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Fill(); };
            _search.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                var first = Visible().FirstOrDefault();
                if (first != null) Select(first);
            };
            var searchGrid = new Grid();
            searchGrid.Children.Add(_search);
            searchGrid.Children.Add(hint);
            top.Children.Add(searchGrid);
            DockPanel.SetDock(top, Dock.Top);
            leftDock.Children.Add(top);

            var listScroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            leftDock.Children.Add(listScroll);
            Grid.SetColumn(left, 0);
            _root.Children.Add(left);

            // ================= Right: the code
            var right = new ScrollViewer { Content = _code, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetColumn(right, 1);
            _root.Children.Add(right);

            Content = _root;
            Fill();
        }

        public Panel PopRoot => _root;

        public void OnShown()
        {
            Fill();
            if (_selected == null)
            {
                var first = Visible().FirstOrDefault();
                if (first != null) Select(first);
                else ShowEmpty();
            }
        }

        private IEnumerable<ScriptEntry> Visible()
        {
            IEnumerable<ScriptEntry> pool = ScriptLibrary.InLang(_filter);
            string q = _search.Text.Trim();
            if (q.Length == 0) return pool.OrderBy(s => s.Lang).ThenBy(s => s.Category).ThenBy(s => s.Name);
            var hits = ScriptLibrary.Search(q, 60).Where(h => h.Score >= 0.2).Select(h => h.Script).ToList();
            var allowed = pool.ToHashSet();
            var found = hits.Where(allowed.Contains).ToList();
            // Plain text match too (name or summary), for short searches like "led".
            foreach (var s in pool)
                if (!found.Contains(s) && (s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    found.Add(s);
            return found;
        }

        private void Fill()
        {
            _list.Children.Clear();
            var all = ScriptLibrary.All;
            int lua = all.Count(s => s.Lang == "lua"), cpp = all.Count - lua;
            _count.Text = all.Count == 0 ? "The script files are missing. Reinstall Nimbus to get them back." : $"{lua} Roblox Lua scripts · {cpp} C++ scripts · works offline";
            var shown = Visible().ToList();
            if (shown.Count == 0)
            {
                _list.Children.Add(AceCardViews.Text(all.Count == 0 ? "" : "No scripts match that.", 12, "MutedBrush"));
                return;
            }
            bool searching = _search.Text.Trim().Length > 0;
            string lastGroup = "";
            foreach (var s in shown)
            {
                string group = searching ? "Best matches" : (s.Lang == "lua" ? "Roblox · " : s.Kind + " · ") + s.Category;
                if (group != lastGroup)
                {
                    var h = AceCardViews.Text(group.ToUpperInvariant(), 10.5, "MutedBrush", bold: true, wrap: false);
                    h.Margin = new Thickness(0, _list.Children.Count == 0 ? 0 : 10, 0, 4);
                    _list.Children.Add(h);
                    lastGroup = group;
                }
                var row = AceCardViews.ScriptRow(s, compact: true, _ => Select(s));
                if (s == _selected && row is Control c) c.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                _list.Children.Add(row);
            }
        }

        private void Select(ScriptEntry s)
        {
            _selected = s;
            var view = AceCardViews.Code(s, compact: false);
            _code.Content = view;
            Spring.PopIn(view);
            Fill();
        }

        private void ShowEmpty()
        {
            _code.Content = AceCardViews.Text("Pick a script on the left to see its code.", 13, "MutedBrush");
        }

        private static Border Card()
        {
            var b = new Border();
            b.SetResourceReference(StyleProperty, "Card");
            return b;
        }
    }
}
