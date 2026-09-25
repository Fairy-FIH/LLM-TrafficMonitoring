using System.Globalization;
using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Infrastructure.Pricing;

/// <summary>Parses the community-maintained LiteLLM price catalogue (prices are USD per token).</summary>
public sealed class LiteLlmPricingSource : IPricingSource
{
    private const string EtagKey = "pricing:etag:litellm";

    private readonly HttpProvider _http;
    private readonly HttpJsonClient _json;
    private readonly ICacheStore _cache;
    private readonly Func<string> _url;

    public LiteLlmPricingSource(HttpProvider http, HttpJsonClient json, ICacheStore cache, Func<string> url)
    {
        _http = http;
        _json = json;
        _cache = cache;
        _url = url;
    }

    public string Id => "litellm";
    public string DisplayName => "LiteLLM 模型定价库";

    public async Task<PricingFetchResult> FetchAsync(CancellationToken ct = default)
    {
        var etag = await _cache.GetETagAsync(EtagKey, ct);
        using var client = _http.Create(timeout: TimeSpan.FromSeconds(90));
        var result = await _json.GetAsync<JsonElement>(client, _url(), etag: etag, ct: ct);
        if (result.NotModified) return new PricingFetchResult { NotModified = true };

        if (result.Data.ValueKind != JsonValueKind.Object)
            return new PricingFetchResult();

        var items = new List<ModelPricing>();
        foreach (var property in result.Data.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;

            var input = ReadDecimal(property.Value, "input_cost_per_token");
            var output = ReadDecimal(property.Value, "output_cost_per_token");
            if (input is null && output is null) continue;

            var vendor = property.Value.TryGetProperty("litellm_provider", out var lp) ? lp.GetString() : null;
            var kind = ProviderCatalog.FromAlias(vendor);
            var provider = kind?.ToString() ?? (vendor ?? "unknown").ToLowerInvariant();
            var model = StripPrefix(property.Name, kind);

            items.Add(new ModelPricing
            {
                Provider = provider,
                Model = model,
                InputPerMillion = (input ?? 0m) * 1_000_000m,
                OutputPerMillion = (output ?? 0m) * 1_000_000m,
                CachedInputPerMillion = Scaled(ReadDecimal(property.Value, "cache_read_input_token_cost")),
                CacheWritePerMillion = Scaled(ReadDecimal(property.Value, "cache_creation_input_token_cost")),
                Currency = Currency.USD,
                Source = "litellm"
            });
        }

        await _cache.SetAsync(EtagKey, result.ETag ?? string.Empty, CacheTier.Pricing,
            TimeSpan.FromDays(60), result.ETag, ct);

        return new PricingFetchResult { Items = items, ETag = result.ETag };
    }

    private static decimal? Scaled(decimal? perToken) => perToken is null ? null : perToken * 1_000_000m;

    private static string StripPrefix(string key, ProviderKind? kind)
    {
        if (kind is null) return key;
        var slash = key.IndexOf('/');
        if (slash <= 0) return key;
        var prefix = key[..slash];
        return ProviderCatalog.AliasesFor(kind.Value).Contains(prefix, StringComparer.OrdinalIgnoreCase)
            ? key[(slash + 1)..]
            : key;
    }

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null
        };
    }
}
