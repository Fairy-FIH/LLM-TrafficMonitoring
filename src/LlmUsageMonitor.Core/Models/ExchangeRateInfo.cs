namespace LlmUsageMonitor.Core.Models;

/// <summary>USD -> target currency exchange rate snapshot.</summary>
public sealed class ExchangeRateInfo
{
    public Currency Target { get; set; } = Currency.CNY;
    public decimal Rate { get; set; } = 7.2m;
    public string Source { get; set; } = "default";
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.MinValue;

    /// <summary>When true the value is user-pinned and never auto-refreshed.</summary>
    public bool IsLocked { get; set; }

    public bool IsUsable(TimeSpan ttl) =>
        IsLocked || (Rate > 0 && DateTimeOffset.Now - FetchedAt < ttl);
}
