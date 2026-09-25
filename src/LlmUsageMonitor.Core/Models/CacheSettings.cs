namespace LlmUsageMonitor.Core.Models;

public sealed class CacheTtlSettings
{
    public int PricingMinutes { get; set; } = 60 * 24;
    public int ExchangeRateMinutes { get; set; } = 60 * 6;
    public int UsageMinutes { get; set; } = 5;
    public int ModelsMinutes { get; set; } = 60 * 12;

    public TimeSpan ForTier(CacheTier tier) => tier switch
    {
        CacheTier.Pricing => TimeSpan.FromMinutes(PricingMinutes),
        CacheTier.ExchangeRate => TimeSpan.FromMinutes(ExchangeRateMinutes),
        CacheTier.Usage => TimeSpan.FromMinutes(UsageMinutes),
        CacheTier.Models => TimeSpan.FromMinutes(ModelsMinutes),
        _ => TimeSpan.FromMinutes(30)
    };
}

public sealed class PricingSourceSettings
{
    public bool UseLiteLlm { get; set; } = true;
    public bool UseOpenRouter { get; set; } = true;
    public string LiteLlmUrl { get; set; } =
        "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";
    public string OpenRouterUrl { get; set; } = "https://openrouter.ai/api/v1/models";
}

public sealed class ExchangeRateSettings
{
    public Currency DisplayCurrency { get; set; } = Currency.CNY;
    public string PrimaryUrl { get; set; } = "https://open.er-api.com/v6/latest/USD";
    public string FallbackUrl { get; set; } = "https://api.frankfurter.app/latest?from=USD&to=CNY";
    public decimal ManualRate { get; set; } = 7.2m;
    public bool Locked { get; set; }
}
