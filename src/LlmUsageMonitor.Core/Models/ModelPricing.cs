namespace LlmUsageMonitor.Core.Models;

/// <summary>Price of a model, normalized to price per 1,000,000 tokens.</summary>
public sealed class ModelPricing
{
    public string Provider { get; set; } = string.Empty;

    /// <summary>Canonical model id as returned by the provider (or the pricing source).</summary>
    public string Model { get; set; } = string.Empty;

    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
    public decimal? CachedInputPerMillion { get; set; }
    public decimal? CacheWritePerMillion { get; set; }

    /// <summary>Native currency of the price values (USD or CNY).</summary>
    public Currency Currency { get; set; } = Currency.USD;

    /// <summary>Where the price came from (litellm / openrouter / manual / seed).</summary>
    public string Source { get; set; } = "seed";

    /// <summary>True when a user edited this price by hand; such rows are never overwritten by sync.</summary>
    public bool IsManual { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public ModelPricing Clone() => (ModelPricing)MemberwiseClone();
}
