using System.Globalization;
using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Infrastructure.Exchange;

/// <summary>
/// Resolves USD -> CNY from a free online endpoint, with a secondary source,
/// user lock and manual override. Values are cached and degrade gracefully.
/// </summary>
public sealed class ExchangeRateService : IExchangeRateService
{
    private const string CacheKey = "exchange:usd:cny";

    private readonly IExchangeRateStore _store;
    private readonly ISettingsStore _settings;
    private readonly ICacheStore _cache;
    private readonly HttpProvider _http;
    private readonly HttpJsonClient _json;

    public ExchangeRateService(IExchangeRateStore store, ISettingsStore settings,
        ICacheStore cache, HttpProvider http, HttpJsonClient json)
    {
        _store = store;
        _settings = settings;
        _cache = cache;
        _http = http;
        _json = json;
    }

    public event EventHandler<ExchangeRateInfo>? Changed;

    public async Task<ExchangeRateInfo> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var config = _settings.Current.ExchangeRate;
        var current = await _store.GetAsync(ct);

        if (current.IsLocked && !forceRefresh)
            return current;

        if (!forceRefresh && current.FetchedAt != default &&
            DateTimeOffset.Now - current.FetchedAt < _settings.Current.CacheTtl.ForTier(CacheTier.ExchangeRate))
            return current;

        try
        {
            var rate = await FetchOnlineAsync(config, ct);
            if (rate is { } value && value > 0)
            {
                var info = new ExchangeRateInfo
                {
                    Target = Currency.CNY,
                    Rate = value,
                    Source = "online",
                    FetchedAt = DateTimeOffset.Now,
                    IsLocked = current.IsLocked
                };
                await _store.SaveAsync(info, ct);
                await _cache.SetAsync(CacheKey, value, CacheTier.ExchangeRate, ct: ct);
                Changed?.Invoke(this, info);
                return info;
            }
        }
        catch
        {
            // Fall through to degraded value below.
        }

        if (current.Rate > 0) return current;

        var fallback = new ExchangeRateInfo
        {
            Rate = config.ManualRate,
            Source = "manual-fallback",
            FetchedAt = DateTimeOffset.Now,
            IsLocked = current.IsLocked
        };
        await _store.SaveAsync(fallback, ct);
        return fallback;
    }

    public async Task<decimal> GetUsdToCnyAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<decimal?>(CacheKey, CacheTier.ExchangeRate, ct);
        if (cached is > 0 && !forceRefresh) return cached.Value;
        var info = await GetAsync(forceRefresh, ct);
        return info.Rate > 0 ? info.Rate : _settings.Current.ExchangeRate.ManualRate;
    }

    public async Task LockAsync(decimal rate, CancellationToken ct = default)
    {
        if (rate <= 0) return;
        var info = new ExchangeRateInfo
        {
            Target = Currency.CNY,
            Rate = rate,
            Source = "manual",
            FetchedAt = DateTimeOffset.Now,
            IsLocked = true
        };
        await _store.SaveAsync(info, ct);
        await _cache.SetAsync(CacheKey, rate, CacheTier.ExchangeRate, ct: ct);
        Changed?.Invoke(this, info);
    }

    public async Task UnlockAsync(CancellationToken ct = default)
    {
        var info = await _store.GetAsync(ct);
        info.IsLocked = false;
        await _store.SaveAsync(info, ct);
        Changed?.Invoke(this, info);
        await GetAsync(forceRefresh: true, ct);
    }

    private async Task<decimal?> FetchOnlineAsync(ExchangeRateSettings config, CancellationToken ct)
    {
        using var client = _http.Create(timeout: TimeSpan.FromSeconds(20));

        try
        {
            var primary = await _json.GetAsync<JsonElement>(client, config.PrimaryUrl, ct: ct);
            var rate = ReadRate(primary.Data, "CNY");
            if (rate is > 0) return rate;
        }
        catch
        {
            // try the fallback source
        }

        var secondary = await _json.GetAsync<JsonElement>(client, config.FallbackUrl, ct: ct);
        return ReadRate(secondary.Data, "CNY");
    }

    private static decimal? ReadRate(JsonElement root, string currency)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("rates", out var rates) && rates.ValueKind == JsonValueKind.Object &&
            rates.TryGetProperty(currency, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.Number => value.GetDecimal(),
                JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var d) ? d : null,
                _ => null
            };
        }
        return null;
    }
}
