namespace LlmUsageMonitor.Core.Models;

/// <summary>A single usage event / aggregated bucket for a model on a timestamp.</summary>
public sealed class UsageRecord
{
    public long Id { get; set; }
    public ProviderKind Provider { get; set; }
    public Guid? CredentialId { get; set; }
    public Guid? GroupId { get; set; }
    public string Model { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedTokens { get; set; }
    public long Requests { get; set; } = 1;
    public decimal CostUsd { get; set; }
    public UsageSource Source { get; set; }

    /// <summary>Stable key used to deduplicate overlapping pulls.</summary>
    public string? DedupKey { get; set; }

    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>Result of an aggregation query used by the dashboard.</summary>
public sealed class UsageSummary
{
    public long Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedTokens { get; set; }
    public decimal CostUsd { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;

    public void Add(UsageRecord r)
    {
        Requests += r.Requests;
        InputTokens += r.InputTokens;
        OutputTokens += r.OutputTokens;
        CachedTokens += r.CachedTokens;
        CostUsd += r.CostUsd;
    }
}

/// <summary>Aggregated usage for charting / reports.</summary>
public sealed class UsageAggregate
{
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset? Bucket { get; set; }
    public long Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
}

public sealed class UsageQuery
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public ProviderKind? Provider { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? CredentialId { get; set; }
    public string? Model { get; set; }
    public UsageSource? Source { get; set; }
}
