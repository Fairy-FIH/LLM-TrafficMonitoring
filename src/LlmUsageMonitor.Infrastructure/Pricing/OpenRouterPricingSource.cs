using System.Globalization;
using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Infrastructure.Pricing;

/// <summary>Parses the live OpenRouter model catalogue (prices are USD per token as strings).</summary>
public sealed class OpenRouterPricingSource : IPricingSource
{
    private const string EtagKey = "pricing:etag:openrouter";

    private readonly HttpProvider _http;
    private readonly HttpJsonClient _json;
    private readonly ICacheStore _cache;
    private readonly Func<string> _url;

    public OpenRouterPricingSource(HttpProvider http, HttpJsonClient json, ICacheStore cache, Func<string> url)
    {
        _http = http;
        _json = json;
        _cache = cache;
        _url = url;
    }

    public string Id => "openrouter";
    public string DisplayName => "OpenRouter 实时定价";

    public async Task<PricingFetchResult> FetchAsync(CancellationToken ct = default)
    {
        var etag = await _cache.GetETagAsync(EtagKey, ct);
        using var client = _http.Create(timeout: TimeSpan.FromSeconds(60));
        var result = await _json.GetAsync<JsonElement>(client, _url(), etag: etag, ct: ct);
        if (result.NotModified) return new PricingFetchResult { NotModified = true };
        if (result.Data.ValueKind != JsonValueKind.Object ||
            !result.Data.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return new PricingFetchResult();
        }

        var items = new List<ModelPricing>();
        foreach (var model in data.EnumerateArray())
        {
            if (!model.TryGetProperty("id", out var idProp) || idProp.GetString() is not { Length: > 0 } id)
                continue;
            if (!model.TryGetProperty("pricing", out var pricing)) continue;

            var prompt = ReadPerToken(pricing, "prompt");
            var completion = ReadPerToken(pricing, "completion");
            if (prompt is null && completion is null) continue;

            items.Add(new ModelPricing
            {
                Provider = ProviderKind.OpenRouter.ToString(),
                Model = id,
                InputPerMillion = (prompt ?? 0m) * 1_000_000m,
                OutputPerMillion = (completion ?? 0m) * 1_000_000m,
                CachedInputPerMillion = Scaled(ReadPerToken(pricing, "input_cache_read")),
                CacheWritePerMillion = Scaled(ReadPerToken(pricing, "input_cache_write")),
                Currency = Currency.USD,
                Source = "openrouter"
            });
        }

        await _cache.SetAsync(EtagKey, result.ETag ?? string.Empty, CacheTier.Pricing,
            TimeSpan.FromDays(60), result.ETag, ct);

        return new PricingFetchResult { Items = items, ETag = result.ETag };
    }

    private static decimal? Scaled(decimal? perToken) => perToken is null ? null : perToken * 1_000_000m;

    private static decimal? ReadPerToken(JsonElement pricing, string name)
    {
        if (!pricing.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null
        };
    }
}
