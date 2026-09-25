namespace LlmUsageMonitor.Core.Models;

/// <summary>A spending limit attached to the global account, a group or a credential.</summary>
public sealed class BudgetRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public BudgetScopeType ScopeType { get; set; } = BudgetScopeType.Global;
    public Guid? ScopeId { get; set; }
    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;
    public decimal LimitUsd { get; set; } = 10m;

    /// <summary>Percent thresholds that raise alerts, e.g. 80 / 100.</summary>
    public List<int> Thresholds { get; set; } = new() { 80, 100 };

    public bool Enabled { get; set; } = true;
    public bool NotifyDesktop { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class AlertRecord
{
    public long Id { get; set; }
    public Guid? BudgetId { get; set; }
    public AlertLevel Level { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public decimal ValueUsd { get; set; }
    public decimal LimitUsd { get; set; }
    public int ThresholdPercent { get; set; }
    public bool Acknowledged { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class TokenEstimate
{
    public string Model { get; set; } = string.Empty;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public bool HasPricing { get; set; }
    public bool ExactTokenizer { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>Token accounting parsed from a provider response or proxy interception.</summary>
public sealed class TokenUsage
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedTokens { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
    public string? Model { get; set; }
    public long Requests { get; set; } = 1;
}
