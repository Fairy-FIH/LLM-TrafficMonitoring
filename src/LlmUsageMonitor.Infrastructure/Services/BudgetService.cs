using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.Infrastructure.Services;

/// <summary>Evaluates budget rules against stored usage and raises alerts once per threshold per period.</summary>
public sealed class BudgetService : IBudgetService
{
    private readonly IBudgetStore _budgets;
    private readonly IUsageStore _usage;
    private readonly ICostCalculator _cost;
    private readonly ISettingsStore _settings;
    private readonly IExchangeRateService _exchange;

    public BudgetService(IBudgetStore budgets, IUsageStore usage, ICostCalculator cost,
        ISettingsStore settings, IExchangeRateService exchange)
    {
        _budgets = budgets;
        _usage = usage;
        _cost = cost;
        _settings = settings;
        _exchange = exchange;
    }

    public event EventHandler<AlertRecord>? AlertRaised;

    public async Task<IReadOnlyList<AlertRecord>> EvaluateAsync(CancellationToken ct = default)
    {
        var rules = await _budgets.GetRulesAsync(ct);
        var existing = await _budgets.GetAlertsAsync(500, ct);
        var raised = new List<AlertRecord>();
        var now = DateTimeOffset.Now;

        foreach (var rule in rules)
        {
            var (from, to) = BudgetMath.PeriodRange(rule.Period, now);
            var query = new UsageQuery
            {
                From = from,
                To = to,
                GroupId = rule.ScopeType == BudgetScopeType.Group ? rule.ScopeId : null,
                CredentialId = rule.ScopeType == BudgetScopeType.Credential ? rule.ScopeId : null
            };

            var summary = await _usage.GetSummaryAsync(query, ct);
            var hit = BudgetMath.Evaluate(rule, summary.CostUsd);
            if (hit is null) continue;

            var already = existing.Any(a =>
                a.BudgetId == rule.Id &&
                a.ThresholdPercent == hit.Percent &&
                a.CreatedAt >= from);
            if (already) continue;

            var currency = _settings.Current.ExchangeRate.DisplayCurrency;
            decimal rate;
            try { rate = await _exchange.GetUsdToCnyAsync(ct: ct); }
            catch { rate = _settings.Current.ExchangeRate.ManualRate; }

            var alert = new AlertRecord
            {
                BudgetId = rule.Id,
                Level = hit.Level,
                Title = $"预算告警 · {ScopeLabel(rule)}",
                Message = $"{ScopeLabel(rule)} {rule.Period switch { BudgetPeriod.Daily => "当日", _ => "本月" }}" +
                          $"花费 {_cost.Format(summary.CostUsd, currency, rate)} / " +
                          $"{_cost.Format(rule.LimitUsd, currency, rate)}（{hit.Percent}%）已达到 {hit.Percent}% 阈值",
                ValueUsd = summary.CostUsd,
                LimitUsd = rule.LimitUsd,
                ThresholdPercent = hit.Percent,
                CreatedAt = now
            };

            await _budgets.AddAlertAsync(alert, ct);
            raised.Add(alert);
            AlertRaised?.Invoke(this, alert);
        }

        return raised;
    }

    private static string ScopeLabel(BudgetRule rule) => rule.ScopeType switch
    {
        BudgetScopeType.Global => "全局",
        BudgetScopeType.Group => "分组",
        BudgetScopeType.Credential => "密钥",
        _ => "预算"
    };
}
