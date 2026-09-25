using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.Infrastructure.Pricing;

public sealed class PricingService : IPricingService
{
    private readonly IReadOnlyList<IPricingSource> _sources;
    private readonly IPricingStore _store;
    private readonly ICacheStore _cache;
    private readonly ISettingsStore _settings;

    public PricingService(IEnumerable<IPricingSource> sources, IPricingStore store,
        ICacheStore cache, ISettingsStore settings)
    {
        // Seed first so online catalogues take precedence on conflict.
        _sources = sources.OrderBy(s => s.Id == "seed" ? 0 : 1).ToList();
        _store = store;
        _cache = cache;
        _settings = settings;
    }

    public event EventHandler? Changed;

    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        var config = _settings.Current.PricingSources;
        var total = 0;

        foreach (var source in _sources)
        {
            if (!IsEnabled(source.Id, config)) continue;

            var markerKey = $"pricing:refreshed:{source.Id}";
            var marker = await _cache.GetAsync<string>(markerKey, CacheTier.Pricing, ct);
            if (marker is not null) continue;

            try
            {
                var result = await source.FetchAsync(ct);
                if (!result.NotModified && result.Items.Count > 0)
                {
                    await _store.UpsertAsync(result.Items, ct);
                    total += result.Items.Count;
                }

                await _cache.SetAsync(markerKey, "1", CacheTier.Pricing, ct: ct);
            }
            catch
            {
                // Graceful degradation: keep whatever is already in the store.
            }
        }

        if (total > 0) Changed?.Invoke(this, EventArgs.Empty);
        return total;
    }

    public async Task<ModelPricing?> ResolveAsync(ProviderKind provider, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;

        var direct = await _store.FindAsync(provider.ToString(), model, ct);
        if (direct is not null) return direct;

        foreach (var alias in ProviderCatalog.AliasesFor(provider))
        {
            var hit = await _store.FindAsync(alias, model, ct);
            if (hit is not null) return hit;
        }

        // OpenRouter ids are "<vendor>/<model>"; try that shape as a fallback.
        if (provider != ProviderKind.OpenRouter)
        {
            var vendor = ProviderCatalog.AliasesFor(provider).FirstOrDefault();
            if (vendor is not null)
            {
                var slashHit = await _store.FindAsync(ProviderKind.OpenRouter.ToString(), $"{vendor}/{model}", ct);
                if (slashHit is not null) return slashHit;
            }
        }

        return null;
    }

    public Task<IReadOnlyList<ModelPricing>> SearchAsync(string? text, string? provider, int limit = 500, CancellationToken ct = default)
        => _store.SearchAsync(text, provider, limit, ct);

    public Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken ct = default)
        => _store.GetAllAsync(ct);

    public async Task SetManualAsync(ModelPricing pricing, CancellationToken ct = default)
    {
        await _store.SetManualAsync(pricing, ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(string provider, string model, CancellationToken ct = default)
    {
        await _store.DeleteAsync(provider, model, ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<int> CountAsync(CancellationToken ct = default) => _store.CountAsync(ct);

    private static bool IsEnabled(string sourceId, PricingSourceSettings config) => sourceId switch
    {
        "litellm" => config.UseLiteLlm,
        "openrouter" => config.UseOpenRouter,
        _ => true
    };
}
