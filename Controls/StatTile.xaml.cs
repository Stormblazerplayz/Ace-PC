using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nimbus.Motion;

namespace Nimbus.Controls
{
    /// <summary>
    /// Glance tile. Bind Value/Percent to live numbers; the bar springs every time Percent changes.
    /// Example: &lt;c:StatTile Label="CPU" Unit="%" Value="{Binding CpuText}" Percent="{Binding Cpu}"/&gt;
    /// </summary>
    public partial class StatTile : UserControl
    {
        public StatTile()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty LabelProperty = Reg(nameof(Label), typeof(string), "", (t, v) => t.LabelBlock.Text = (string)v);
        public static readonly DependencyProperty ValueProperty = Reg(nameof(Value), typeof(string), "", (t, v) => t.ValueBlock.Text = (string)v);
        public static readonly DependencyProperty UnitProperty = Reg(nameof(Unit), typeof(string), "", (t, v) => t.UnitBlock.Text = (string)v);
        public static readonly DependencyProperty FlagProperty = Reg(nameof(Flag), typeof(string), "", (t, v) => t.ShowFlag((string)v));
        public static readonly DependencyProperty PercentProperty = Reg(nameof(Percent), typeof(double), 0.0, (t, v) => t.SpringBar((double)v));
        public static readonly DependencyProperty BarBrushProperty = Reg(nameof(BarBrush), typeof(Brush), null, (t, v) => t.Paint());
        public static readonly DependencyProperty IsWarnProperty = Reg(nameof(IsWarn), typeof(bool), false, (t, v) => t.Warn((bool)v));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        /// <summary>Small amber pill next to the label, like "Low". Empty hides it.</summary>
        public string Flag { get => (string)GetValue(FlagProperty); set => SetValue(FlagProperty, value); }
        /// <summary>0 to 100.</summary>
        public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
        public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
        /// <summary>Turns the tile amber (low disk, hot CPU...).</summary>
        public bool IsWarn { get => (bool)GetValue(IsWarnProperty); set => SetValue(IsWarnProperty, value); }

        private static DependencyProperty Reg(string name, Type type, object? def, Action<StatTile, object> changed) =>
            DependencyProperty.Register(name, type, typeof(StatTile),
                new PropertyMetadata(def, (d, e) => changed((StatTile)d, e.NewValue)));

        private void SpringBar(double percent)
        {
            double target = Math.Max(0, Math.Min(100, double.IsNaN(percent) ? 0 : percent)) / 100;
            if (Math.Abs(FillScale.ScaleX - target) < 0.004) return;   // no wobble for tiny changes
            Spring.To(FillScale, ScaleTransform.ScaleXProperty, target, Spring.DurationMs, Spring.Medium);
        }

        private void ShowFlag(string text)
        {
            FlagBlock.Text = text;
            FlagPill.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Warn(bool on)
        {
            Paint();
            if (on) Spring.Bump(ValueBlock);
        }

        private void Paint()
        {
            if (IsWarn)
            {
                Card.BorderBrush = (Brush)FindResource("WarnBrush");
                Fill.Background = (Brush)FindResource("WarnBrush");
            }
            else
            {
                Card.ClearValue(Border.BorderBrushProperty);
                Fill.Background = BarBrush ?? (Brush)FindResource("GoodBrush");
            }
        }
    }
}
