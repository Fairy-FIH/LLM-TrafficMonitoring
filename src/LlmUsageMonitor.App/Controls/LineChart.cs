using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LlmUsageMonitor.App.ViewModels;

namespace LlmUsageMonitor.App.Controls;

/// <summary>Line chart of cost over time with a hover guide showing the exact value.</summary>
public sealed class LineChart : FrameworkElement
{
    private const double PadLeft = 10;
    private const double PadRight = 10;
    private const double PadTop = 16;
    private const double PadBottom = 22;

    private List<ChartPoint> _points = new();
    private double _stepX;
    private double _max;
    private int _hoverIndex = -1;

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable<ChartPoint>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(string), typeof(LineChart),
        new FrameworkPropertyMetadata("¥", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<ChartPoint>? Items
    {
        get => (IEnumerable<ChartPoint>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    // Let the whole surface receive mouse events even where nothing is drawn.
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_points.Count == 0 || _stepX <= 0) return;

        var x = e.GetPosition(this).X - PadLeft;
        var index = (int)Math.Round(x / _stepX);
        index = Math.Clamp(index, 0, _points.Count - 1);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        _points = Items?.ToList() ?? new List<ChartPoint>();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        if (_points.Count == 0)
        {
            DrawText(dc, "暂无数据", new Point(width / 2 - 24, height / 2 - 8), TextBrush, 12);
            return;
        }

        var plotWidth = Math.Max(1, width - PadLeft - PadRight);
        var plotHeight = Math.Max(1, height - PadTop - PadBottom);

        _max = _points.Max(p => p.DisplayValue);
        if (_max <= 0) _max = 1;
        _stepX = _points.Count > 1 ? plotWidth / (_points.Count - 1) : 0;

        double X(int i) => PadLeft + _stepX * i;
        double Y(double v) => PadTop + plotHeight - (v / _max) * plotHeight;

        // Area fill
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(_points[0].DisplayValue)), true, true);
            for (var i = 1; i < _points.Count; i++)
                ctx.LineTo(new Point(X(i), Y(_points[i].DisplayValue)), true, false);
            ctx.LineTo(new Point(X(_points.Count - 1), PadTop + plotHeight), true, false);
            ctx.LineTo(new Point(X(0), PadTop + plotHeight), true, false);
        }
        geometry.Freeze();

        var accent = (LineBrush as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var fill = new SolidColorBrush(accent) { Opacity = 0.16 };
        fill.Freeze();
        dc.DrawGeometry(fill, null, geometry);

        // Line
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(_points[0].DisplayValue)), false, false);
            for (var i = 1; i < _points.Count; i++)
                ctx.LineTo(new Point(X(i), Y(_points[i].DisplayValue)), true, false);
        }
        line.Freeze();
        var pen = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);

        // Hover guide
        if (_hoverIndex >= 0 && _hoverIndex < _points.Count)
        {
            var point = _points[_hoverIndex];
            var hx = X(_hoverIndex);
            var hy = Y(point.DisplayValue);

            var guidePen = new Pen(TextBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            guidePen.Freeze();
            dc.DrawLine(guidePen, new Point(hx, PadTop), new Point(hx, PadTop + plotHeight));

            var dot = new SolidColorBrush(accent);
            dot.Freeze();
            dc.DrawEllipse(dot, null, new Point(hx, hy), 4, 4);

            var label = $"{point.Label}  {point.DisplayText}";
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 11, TextBrush, 96);
            var boxWidth = ft.Width + 12;
            var boxX = Math.Clamp(hx - boxWidth / 2, 0, width - boxWidth);
            var boxY = Math.Max(0, hy - 28);
            var boxBrush = new SolidColorBrush(Color.FromArgb(230, 30, 34, 44));
            boxBrush.Freeze();
            dc.DrawRoundedRectangle(boxBrush, null, new Rect(boxX, boxY, boxWidth, ft.Height + 8), 6, 6);
            dc.DrawText(ft, new Point(boxX + 6, boxY + 4));
        }

        // Axis labels
        DrawText(dc, $"{Symbol}{_max:0.##}", new Point(PadLeft, 0), TextBrush, 10);
        DrawText(dc, _points[0].Label, new Point(PadLeft, height - PadBottom + 4), TextBrush, 10);
        DrawRight(dc, _points[^1].Label, new Point(width - PadRight, height - PadBottom + 4), TextBrush, 10);
    }

    private static void DrawText(DrawingContext dc, string text, Point origin, Brush brush, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, 96);
        dc.DrawText(ft, origin);
    }

    private static void DrawRight(DrawingContext dc, string text, Point origin, Brush brush, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, 96);
        dc.DrawText(ft, new Point(origin.X - ft.Width, origin.Y));
    }
}
