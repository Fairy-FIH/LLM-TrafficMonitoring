using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.Tests;

public class CostCalculatorTests
{
    private readonly CostCalculator _calc = new();

    [Fact]
    public void Computes_usd_cost_with_cached_discount()
    {
        var pricing = new ModelPricing
        {
            InputPerMillion = 2.5m,
            OutputPerMillion = 10m,
            CachedInputPerMillion = 1.25m,
            Currency = Currency.USD
        };
        var usage = new TokenUsage { InputTokens = 1000, OutputTokens = 500, CachedTokens = 200 };

        var cost = _calc.ComputeUsd(usage, pricing);

        // 800*2.5 + 200*1.25 + 500*10 = 7250 -> 0.00725
        Assert.Equal(0.00725m, cost);
    }

    [Fact]
    public void Converts_cny_prices_to_usd()
    {
        var pricing = new ModelPricing
        {
            InputPerMillion = 8m,
            OutputPerMillion = 0m,
            Currency = Currency.CNY
        };
        var usage = new TokenUsage { InputTokens = 1_000_000 };

        var cost = _calc.ComputeUsd(usage, pricing, usdToCny: 8m);

        Assert.Equal(1m, cost);
    }

    [Fact]
    public void Returns_zero_without_pricing()
    {
        Assert.Equal(0m, _calc.ComputeUsd(new TokenUsage { InputTokens = 100 }, null));
    }

    [Fact]
    public void Format_switches_symbol()
    {
        Assert.StartsWith("$", _calc.Format(1m, Currency.USD, 7.2m));
        Assert.StartsWith("¥", _calc.Format(1m, Currency.CNY, 7.2m));
    }
}
