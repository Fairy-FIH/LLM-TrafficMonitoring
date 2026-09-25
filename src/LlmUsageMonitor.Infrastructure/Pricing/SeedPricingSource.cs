using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Infrastructure.Pricing;

/// <summary>
/// Built-in offline fallback prices for models the online catalogues often miss,
/// mainly domestic platforms. Values are approximate and fully editable in the UI.
/// </summary>
public sealed class SeedPricingSource : IPricingSource
{
    public string Id => "seed";
    public string DisplayName => "内置定价（离线兜底）";

    public Task<PricingFetchResult> FetchAsync(CancellationToken ct = default)
    {
        var items = new List<ModelPricing>();
        void Add(ProviderKind provider, string model, decimal input, decimal output,
            Currency currency = Currency.CNY, decimal? cached = null)
        {
            items.Add(new ModelPricing
            {
                Provider = provider.ToString(),
                Model = model,
                InputPerMillion = input,
                OutputPerMillion = output,
                CachedInputPerMillion = cached,
                Currency = currency,
                Source = "seed"
            });
        }

        // Zhipu GLM (CNY / 1M tokens)
        Add(ProviderKind.Zhipu, "glm-4-plus", 50m, 50m);
        Add(ProviderKind.Zhipu, "glm-4-air", 1m, 1m);
        Add(ProviderKind.Zhipu, "glm-4-flash", 0m, 0m);
        Add(ProviderKind.Zhipu, "glm-4-long", 1m, 1m);
        Add(ProviderKind.Zhipu, "glm-4.5", 2m, 8m);
        Add(ProviderKind.Zhipu, "glm-4.6", 4m, 16m);

        // Alibaba DashScope / Bailian (CNY / 1M tokens)
        Add(ProviderKind.AlibabaDashScope, "qwen-max", 20m, 60m);
        Add(ProviderKind.AlibabaDashScope, "qwen-plus", 0.8m, 2m);
        Add(ProviderKind.AlibabaDashScope, "qwen-turbo", 0.3m, 0.6m);
        Add(ProviderKind.AlibabaDashScope, "qwen-long", 0.5m, 2m);
        Add(ProviderKind.AlibabaDashScope, "qwen2.5-72b-instruct", 4m, 12m);
        Add(ProviderKind.AlibabaDashScope, "qwen3-max", 6m, 24m);

        // Volcengine Ark / Doubao (CNY / 1M tokens)
        Add(ProviderKind.VolcengineArk, "doubao-pro-32k", 0.8m, 2m);
        Add(ProviderKind.VolcengineArk, "doubao-pro-128k", 5m, 9m);
        Add(ProviderKind.VolcengineArk, "doubao-lite-4k", 0.3m, 0.6m);
        Add(ProviderKind.VolcengineArk, "doubao-1.5-pro-32k", 0.8m, 2m);

        // DeepSeek (CNY / 1M tokens)
        Add(ProviderKind.DeepSeek, "deepseek-chat", 2m, 3m, Currency.CNY, 0.5m);
        Add(ProviderKind.DeepSeek, "deepseek-reasoner", 4m, 16m, Currency.CNY, 1m);

        // Moonshot / Kimi (CNY / 1M tokens)
        Add(ProviderKind.Moonshot, "moonshot-v1-8k", 12m, 12m);
        Add(ProviderKind.Moonshot, "moonshot-v1-32k", 24m, 24m);
        Add(ProviderKind.Moonshot, "moonshot-v1-128k", 60m, 60m);
        Add(ProviderKind.Moonshot, "kimi-k2-0711-preview", 4m, 16m);

        // SiliconFlow (CNY / 1M tokens)
        Add(ProviderKind.SiliconFlow, "Qwen/Qwen2.5-7B-Instruct", 0.35m, 0.35m);
        Add(ProviderKind.SiliconFlow, "Qwen/Qwen2.5-72B-Instruct", 4m, 4m);
        Add(ProviderKind.SiliconFlow, "deepseek-ai/DeepSeek-V3", 2m, 8m);

        // OpenAI fallback prices (USD / 1M tokens)
        Add(ProviderKind.OpenAI, "gpt-4o", 2.5m, 10m, Currency.USD, 1.25m);
        Add(ProviderKind.OpenAI, "gpt-4o-mini", 0.15m, 0.6m, Currency.USD, 0.075m);
        Add(ProviderKind.OpenAI, "gpt-4.1", 2m, 8m, Currency.USD, 0.5m);
        Add(ProviderKind.OpenAI, "o3-mini", 1.1m, 4.4m, Currency.USD, 0.55m);

        return Task.FromResult(new PricingFetchResult { Items = items });
    }
}
