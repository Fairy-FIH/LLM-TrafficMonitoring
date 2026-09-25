using System.Collections.ObjectModel;
using System.Windows.Media;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.ViewModels;

/// <summary>A labeled slice for the donut/bar charts.</summary>
public sealed class ChartSlice
{
    public string Label { get; init; } = string.Empty;

    /// <summary>Raw value in USD, used for sorting/thresholds.</summary>
    public double Value { get; init; }

    /// <summary>Value converted to the display currency, used for drawing.</summary>
    public double DisplayValue { get; init; }

    /// <summary>Pre-formatted value with the correct currency symbol.</summary>
    public string DisplayText { get; init; } = string.Empty;

    public decimal CostUsd { get; init; }
    public long Tokens { get; init; }
    public long Requests { get; init; }
    public string Color { get; init; } = "#4F8CFF";
    public bool IsAccentColor { get; init; }
}

public sealed class ChartPoint
{
    public string Label { get; init; } = string.Empty;
    public double Value { get; init; }
    public double DisplayValue { get; init; }
    public string DisplayText { get; init; } = string.Empty;
    public decimal CostUsd { get; init; }
}

/// <summary>A labeled choice for combo boxes.</summary>
public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>A selectable range for the dashboard.</summary>
public sealed record RangeOption(string Label, int Days)
{
    public static readonly RangeOption Today = new("今日", 1);
    public static readonly RangeOption Week = new("近 7 天", 7);
    public static readonly RangeOption Month = new("近 30 天", 30);
    public static readonly RangeOption Quarter = new("近 90 天", 90);
    public static readonly RangeOption All = new("全部", 0);

    public static IReadOnlyList<RangeOption> Options { get; } = new[] { Today, Week, Month, Quarter, All };
}
