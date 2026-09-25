using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Core.Abstractions;

public sealed class CacheEntryInfo
{
    public string Key { get; set; } = string.Empty;
    public CacheTier Tier { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? ETag { get; set; }
    public long SizeBytes { get; set; }
}

/// <summary>
/// Two-level (memory + SQLite) cache with tiered TTL, ETag support and
/// stale-while-error degradation.
/// </summary>
public interface ICacheStore
{
    Task<T?> GetAsync<T>(string key, CacheTier tier, CancellationToken ct = default);

    /// <summary>Returns an expired-but-present value, used for graceful degradation.</summary>
    Task<T?> GetStaleAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, CacheTier tier, TimeSpan? ttl = null,
        string? etag = null, CancellationToken ct = default);

    /// <summary>
    /// Returns a fresh value if cached, otherwise runs <paramref name="factory"/> once
    /// (concurrent callers are coalesced). On failure, a stale value is served when
    /// available instead of throwing (stale-while-error degradation).
    /// </summary>
    Task<CacheFetchResult<T>> GetOrCreateAsync<T>(string key, CacheTier tier,
        Func<CancellationToken, Task<T>> factory, bool allowStaleOnError = true,
        CancellationToken ct = default);

    Task<string?> GetETagAsync(string key, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task ClearAsync(CacheTier? tier = null, CancellationToken ct = default);
    Task PurgeExpiredAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CacheEntryInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>Statistics for the cache diagnostics panel.</summary>
    CacheStats Stats { get; }
}

public sealed class CacheStats
{
    public long Hits { get; set; }
    public long Misses { get; set; }
    public long StaleHits { get; set; }
    public long Coalesced { get; set; }
    public long Sets { get; set; }

    public double HitRate
    {
        get
        {
            var total = Hits + StaleHits + Misses;
            return total == 0 ? 0 : (double)(Hits + StaleHits) / total;
        }
    }
}

public sealed class CacheFetchResult<T>
{
    public T? Value { get; init; }
    public bool FromCache { get; init; }
    public bool Stale { get; init; }
    public bool Coalesced { get; init; }
    public bool NotModified { get; init; }
}
