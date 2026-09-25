using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;
using LlmUsageMonitor.Infrastructure.Http;
using LlmUsageMonitor.Providers.Adapters;

namespace LlmUsageMonitor.Providers;

public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<ProviderKind, IProviderAdapter> _adapters;

    public ProviderRegistry(HttpProvider http, HttpJsonClient json)
    {
        _adapters = new Dictionary<ProviderKind, IProviderAdapter>
        {
            [ProviderKind.OpenAI] = new OpenAiAdapter(http, json),
            [ProviderKind.OpenRouter] = new OpenRouterAdapter(http, json),
            [ProviderKind.Anthropic] = new AnthropicAdapter(http, json),
            [ProviderKind.DeepSeek] = new BalanceOnlyAdapter(ProviderKind.DeepSeek, http, json,
                "/user/balance", Currency.CNY, BalanceOnlyAdapter.DeepSeekParser),
            [ProviderKind.Moonshot] = new BalanceOnlyAdapter(ProviderKind.Moonshot, http, json,
                "/users/me/balance", Currency.CNY, BalanceOnlyAdapter.MoonshotParser),
            [ProviderKind.SiliconFlow] = new BalanceOnlyAdapter(ProviderKind.SiliconFlow, http, json,
                "/user/info", Currency.CNY, BalanceOnlyAdapter.SiliconFlowParser)
        };

        foreach (var kind in new[]
                 {
                     ProviderKind.Zhipu, ProviderKind.AlibabaDashScope, ProviderKind.VolcengineArk,
                     ProviderKind.GoogleGemini, ProviderKind.AzureOpenAI, ProviderKind.Custom
                 })
        {
            _adapters[kind] = new OpenAiCompatibleAdapter(kind, http, json);
        }
    }

    public IReadOnlyList<ProviderDescriptor> Descriptors => ProviderCatalog.All;

    public ProviderDescriptor GetDescriptor(ProviderKind kind) => ProviderCatalog.Get(kind);

    public IProviderAdapter GetAdapter(ProviderKind kind)
        => _adapters.TryGetValue(kind, out var adapter) ? adapter : _adapters[ProviderKind.Custom];

    public IProviderAdapter? FindAdapter(string providerNameOrBaseUrl)
    {
        var kind = ProviderCatalog.FromAlias(providerNameOrBaseUrl);
        return kind is not null ? GetAdapter(kind.Value) : null;
    }
}
