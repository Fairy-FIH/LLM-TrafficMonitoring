using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LlmUsageMonitor.App.ViewModels;

namespace LlmUsageMonitor.App.Controls;

/// <summary>Ring chart for cost distribution.</summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable<ChartSlice>), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(string), typeof(DonutChart),
        new FrameworkPropertyMetadata("¥", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<ChartSlice>? Items
    {
        get => (IEnumerable<ChartSlice>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var items = Items?.Where(i => i.DisplayValue > 0).ToList() ?? new List<ChartSlice>();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (items.Count == 0)
        {
            var ft = new FormattedText("暂无花费", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, TextBrush, 96);
            dc.DrawText(ft, new Point(width / 2 - 28, height / 2 - 8));
            return;
        }

        var center = new Point(width / 2, height / 2);
        var outer = Math.Min(width, height) / 2 - 6;
        var inner = outer * 0.62;
        var total = items.Sum(i => i.DisplayValue);
        var start = -90.0;

        var accentColor = (AccentBrush as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var index = 0;
        foreach (var item in items)
        {
            var sweep = item.DisplayValue / total * 360.0;
            var color = ResolveColor(item, accentColor, index, items.Count);
            DrawSegment(dc, center, outer, inner, start, start + sweep, color);
            start += sweep;
            index++;
        }

        var totalText = new FormattedText($"{Symbol}{total:0.##}", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 14, TextBrush, 96);
        dc.DrawText(totalText, new Point(center.X - totalText.Width / 2, center.Y - totalText.Height / 2));
    }

    private static Color ResolveColor(ChartSlice slice, Color accent, int index, int count)
    {
        if (!string.IsNullOrWhiteSpace(slice.Color))
        {
            try { return (Color)ColorConverter.ConvertFromString(slice.Color); }
            catch { /* fall through */ }
        }

        var palette = new[]
        {
            accent, Colors.MediumSeaGreen, Colors.CornflowerBlue, Colors.Orange,
            Colors.MediumPurple, Colors.IndianRed, Colors.LightSeaGreen, Colors.Gold
        };
        return palette[index % palette.Length];
    }

    private static void DrawSegment(DrawingContext dc, Point center, double outer, double inner,
        double startAngle, double endAngle, Color color)
    {
        var a0 = startAngle * Math.PI / 180;
        var a1 = endAngle * Math.PI / 180;
        var large = endAngle - startAngle > 180;

        var outerStart = new Point(center.X + outer * Math.Cos(a0), center.Y + outer * Math.Sin(a0));
        var outerEnd = new Point(center.X + outer * Math.Cos(a1), center.Y + outer * Math.Sin(a1));
        var innerEnd = new Point(center.X + inner * Math.Cos(a1), center.Y + inner * Math.Sin(a1));
        var innerStart = new Point(center.X + inner * Math.Cos(a0), center.Y + inner * Math.Sin(a0));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, true, true);
            ctx.ArcTo(outerEnd, new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(innerEnd, true, false);
            ctx.ArcTo(innerStart, new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 1);
        pen.Freeze();
        dc.DrawGeometry(brush, pen, geometry);
    }
}

/// <summary>Horizontal bar list for per-model cost ranking.</summary>
public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable<ChartSlice>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<ChartSlice>? Items
    {
        get => (IEnumerable<ChartSlice>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var items = Items?.ToList() ?? new List<ChartSlice>();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (items.Count == 0)
        {
            var empty = new FormattedText("暂无数据", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, TextBrush, 96);
            dc.DrawText(empty, new Point(width / 2 - 24, height / 2 - 8));
            return;
        }

        const double rowHeight = 30;
        const double labelWidth = 150;
        const double valueWidth = 90;
        var max = items.Max(i => i.DisplayValue);
        if (max <= 0) max = 1;
        var barArea = Math.Max(20, width - labelWidth - valueWidth - 16);
        var accent = (AccentBrush as SolidColorBrush)?.Color ?? Colors.DodgerBlue;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var y = i * rowHeight;
            if (y + rowHeight > height + rowHeight) break;

            var label = new FormattedText(Truncate(item.Label, 22), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11.5, TextBrush, 96);
            dc.DrawText(label, new Point(0, y + 6));

            var barX = labelWidth;
            var barY = y + 7;
            var barH = 14;
            var trackBrush = new SolidColorBrush(accent) { Opacity = 0.12 };
            trackBrush.Freeze();
            dc.DrawRoundedRectangle(trackBrush, null, new Rect(barX, barY, barArea, barH), 7, 7);

            var color = string.IsNullOrWhiteSpace(item.Color)
                ? accent
                : (Color)ColorConverter.ConvertFromString(item.Color);
            var fill = new SolidColorBrush(color);
            fill.Freeze();
            var barWidth = Math.Max(4, barArea * (item.DisplayValue / max));
            dc.DrawRoundedRectangle(fill, null, new Rect(barX, barY, barWidth, barH), 7, 7);

            var valueText = string.IsNullOrEmpty(item.DisplayText) ? $"{item.DisplayValue:0.##}" : item.DisplayText;
            var value = new FormattedText(valueText, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 11, TextBrush, 96);
            dc.DrawText(value, new Point(width - valueWidth + 4, y + 5));
        }
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "…";
}
