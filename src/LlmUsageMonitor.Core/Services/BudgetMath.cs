using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Core.Services;

public sealed record BudgetThresholdHit(BudgetRule Rule, int Percent, decimal SpentUsd, AlertLevel Level);

/// <summary>Pure budget threshold evaluation shared by the alert service and tests.</summary>
public static class BudgetMath
{
    public static (DateTimeOffset From, DateTimeOffset To) PeriodRange(BudgetPeriod period, DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        if (period == BudgetPeriod.Daily)
        {
            var from = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
            return (from, from.AddDays(1));
        }

        var monthStart = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
        return (monthStart, monthStart.AddMonths(1));
    }

    public static int UsedPercent(decimal spent, decimal limit)
        => limit <= 0 ? 0 : (int)Math.Floor(spent / limit * 100m);

    public static AlertLevel LevelFor(int percent)
        => percent >= 100 ? AlertLevel.Critical : percent >= 90 ? AlertLevel.Warning : AlertLevel.Info;

    /// <summary>Highest configured threshold that the spend has reached, or null.</summary>
    public static BudgetThresholdHit? Evaluate(BudgetRule rule, decimal spentUsd)
    {
        if (!rule.Enabled || rule.LimitUsd <= 0) return null;
        var percent = UsedPercent(spentUsd, rule.LimitUsd);
        var hit = rule.Thresholds.Where(t => percent >= t).OrderByDescending(t => t).FirstOrDefault();
        if (hit == 0 && !rule.Thresholds.Contains(0)) return null;
        return new BudgetThresholdHit(rule, hit, spentUsd, LevelFor(percent));
    }
}
