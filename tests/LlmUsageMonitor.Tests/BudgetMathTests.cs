using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.Tests;

public class BudgetMathTests
{
    [Fact]
    public void Detects_highest_reached_threshold()
    {
        var rule = new BudgetRule { LimitUsd = 100m, Thresholds = new List<int> { 80, 90, 100 } };

        var hit = BudgetMath.Evaluate(rule, 92m);

        Assert.NotNull(hit);
        Assert.Equal(90, hit!.Percent);
        Assert.Equal(AlertLevel.Warning, hit.Level);
    }

    [Fact]
    public void Returns_null_when_below_thresholds()
    {
        var rule = new BudgetRule { LimitUsd = 100m, Thresholds = new List<int> { 80, 100 } };
        Assert.Null(BudgetMath.Evaluate(rule, 10m));
    }

    [Fact]
    public void Critical_at_or_above_limit()
    {
        var rule = new BudgetRule { LimitUsd = 50m, Thresholds = new List<int> { 100 } };
        var hit = BudgetMath.Evaluate(rule, 50m);
        Assert.Equal(AlertLevel.Critical, hit!.Level);
    }

    [Fact]
    public void PeriodRange_covers_month()
    {
        var (from, to) = BudgetMath.PeriodRange(BudgetPeriod.Monthly,
            new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, from.Day);
        Assert.Equal(9, from.Month);
        Assert.Equal(1, to.Day);
        Assert.Equal(10, to.Month);
    }
}
