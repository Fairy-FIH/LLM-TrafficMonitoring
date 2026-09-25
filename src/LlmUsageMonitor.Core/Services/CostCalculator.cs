using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
/// Pure cost math. Prices are stored per 1,000,000 tokens in their native
/// currency; CNY prices are converted to USD using the supplied USD->CNY rate.
/// </summary>
public sealed class CostCalculator : ICostCalculator
{
    private const decimal Million = 1_000_000m;

    public decimal ComputeUsd(TokenUsage usage, ModelPricing? pricing)
        => ComputeUsd(usage, pricing, 7.2m);

    public decimal ComputeUsd(TokenUsage usage, ModelPricing? pricing, decimal usdToCny)
    {
        if (pricing is null) return 0m;

        var divisor = pricing.Currency == Currency.CNY && usdToCny > 0 ? usdToCny : 1m;

        var inputPrice = pricing.InputPerMillion / divisor;
        var outputPrice = pricing.OutputPerMillion / divisor;
        var cachedPrice = pricing.CachedInputPerMillion is { } c ? c / divisor : (decimal?)null;

        var input = usage.InputTokens;
        var cached = Math.Clamp(usage.CachedTokens, 0, input);
        var regularInput = input - cached;

        var cost = regularInput * inputPrice
                   + cached * (cachedPrice ?? inputPrice)
                   + usage.OutputTokens * outputPrice;

        return cost / Million;
    }

    public decimal Convert(decimal usd, Currency target, decimal usdToTargetRate)
        => target switch
        {
            Currency.USD => usd,
            Currency.CNY => usd * (usdToTargetRate > 0 ? usdToTargetRate : 7.2m),
            _ => usd
        };

    public string Symbol(Currency currency) => currency == Currency.CNY ? "¥" : "$";

    public string Format(decimal usd, Currency currency, decimal usdToTargetRate)
    {
        var value = Convert(usd, currency, usdToTargetRate);
        return $"{Symbol(currency)}{value:N4}";
    }
}
