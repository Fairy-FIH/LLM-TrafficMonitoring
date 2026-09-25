using System.Collections.Concurrent;
using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;

namespace LlmUsageMonitor.Infrastructure.Data;

/// <summary>
/// Two-level cache: an in-memory layer for hot values and a SQLite layer for
/// durability. Supports tiered TTLs, ETag storage, single-flight coalescing and
/// stale-while-error degradation.
/// </summary>
public sealed class CacheStore : ICacheStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Database _db;
    private readonly IMemoryCache _memory;
    private readonly CacheTtlSettings _ttl;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, byte> _memoryKeys = new();

    public CacheStore(Database db, IMemoryCache memory, CacheTtlSettings ttl)
    {
        _db = db;
        _memory = memory;
        _ttl = ttl;
    }

    public CacheStats Stats { get; } = new();

    public async Task<T?> GetAsync<T>(string key, CacheTier tier, CancellationToken ct = default)
    {
        var (found, value) = await TryGetAsync<T>(key, tier, ct);
        return found ? value : default;
    }

    private async Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CacheTier tier, CancellationToken ct)
    {
        if (_memory.TryGetValue(key, out var mem) && mem is T typed)
        {
            Stats.Hits++;
            return (true, typed);
        }

        var stored = await ReadRowAsync(key, ct);
        if (stored is null || stored.ExpiresAt <= DateTimeOffset.Now)
        {
            Stats.Misses++;
            return (false, default);
        }

        Stats.Hits++;
        var value = Deserialize<T>(stored.Value);
        if (value is not null) PutMemory(key, value);
        return (true, value);
    }

    public async Task<T?> GetStaleAsync<T>(string key, CancellationToken ct = default)
    {
        if (_memory.TryGetValue(key, out var mem) && mem is T typed)
        {
            Stats.StaleHits++;
            return typed;
        }

        var stored = await ReadRowAsync(key, ct);
        if (stored is null) return default;

        Stats.StaleHits++;
        var value = Deserialize<T>(stored.Value);
        if (value is not null) PutMemory(key, value);
        return value;
    }

    public async Task<CacheFetchResult<T>> GetOrCreateAsync<T>(string key, CacheTier tier,
        Func<CancellationToken, Task<T>> factory, bool allowStaleOnError = true, CancellationToken ct = default)
    {
        var (found, fresh) = await TryGetAsync<T>(key, tier, ct);
        if (found)
            return new CacheFetchResult<T> { Value = fresh, FromCache = true };

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        var coalesced = gate.CurrentCount == 0;

        await gate.WaitAsync(ct);
        try
        {
            // Another caller may have populated the cache while we waited.
            var (foundAgain, recheck) = await TryGetAsync<T>(key, tier, ct);
            if (foundAgain)
            {
                if (coalesced) Stats.Coalesced++;
                return new CacheFetchResult<T> { Value = recheck, FromCache = true, Coalesced = coalesced };
            }

            if (coalesced) Stats.Coalesced++;

            try
            {
                var value = await factory(ct);
                await SetAsync(key, value, tier, null, null, ct);
                return new CacheFetchResult<T> { Value = value };
            }
            catch when (allowStaleOnError)
            {
                var stale = await GetStaleAsync<T>(key, ct);
                if (stale is not null)
                {
                    Stats.StaleHits++;
                    return new CacheFetchResult<T> { Value = stale, Stale = true };
                }
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetAsync<T>(string key, T value, CacheTier tier, TimeSpan? ttl = null,
        string? etag = null, CancellationToken ct = default)
    {
        var effectiveTtl = ttl ?? _ttl.ForTier(tier);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var now = DateTimeOffset.Now;

        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cache_entries(key,tier,value,etag,fetched_at,expires_at,size_bytes)
            VALUES($key,$tier,$value,$etag,$fetched,$expires,$size)
            ON CONFLICT(key) DO UPDATE SET tier=$tier,value=$value,etag=$etag,
                fetched_at=$fetched,expires_at=$expires,size_bytes=$size;
            """;
        cmd.Add("$key", key);
        cmd.Add("$tier", (int)tier);
        cmd.Add("$value", json);
        cmd.Add("$etag", etag);
        cmd.Add("$fetched", SqliteExtensions.Dt(now));
        cmd.Add("$expires", SqliteExtensions.Dt(now + effectiveTtl));
        cmd.Add("$size", System.Text.Encoding.UTF8.GetByteCount(json));
        await cmd.ExecuteNonQueryAsync(ct);

        PutMemory(key, value, effectiveTtl);
        Stats.Sets++;
    }

    public async Task<string?> GetETagAsync(string key, CancellationToken ct = default)
    {
        var row = await ReadRowAsync(key, ct);
        return row?.ETag;
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _memory.Remove(key);
        _memoryKeys.TryRemove(key, out _);
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cache_entries WHERE key=$key";
        cmd.Add("$key", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ClearAsync(CacheTier? tier = null, CancellationToken ct = default)
    {
        foreach (var key in _memoryKeys.Keys) _memory.Remove(key);
        _memoryKeys.Clear();

        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = tier is null ? "DELETE FROM cache_entries" : "DELETE FROM cache_entries WHERE tier=$tier";
        if (tier is not null) cmd.Add("$tier", (int)tier.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PurgeExpiredAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cache_entries WHERE expires_at < $now";
        cmd.Add("$now", SqliteExtensions.Dt(DateTimeOffset.Now));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CacheEntryInfo>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key,tier,fetched_at,expires_at,etag,size_bytes FROM cache_entries ORDER BY expires_at";
        var list = new List<CacheEntryInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CacheEntryInfo
            {
                Key = reader.GetString(0),
                Tier = (CacheTier)reader.GetInt(1),
                FetchedAt = reader.GetDateTimeOffset(2),
                ExpiresAt = reader.GetDateTimeOffset(3),
                ETag = reader.GetStringOrNull(4),
                SizeBytes = reader.GetLong(5)
            });
        }
        return list;
    }

    private void PutMemory<T>(string key, T value, TimeSpan? ttl = null)
    {
        var effective = ttl ?? TimeSpan.FromMinutes(30);
        // A non-positive TTL means "do not keep in memory"; still persist to SQLite.
        if (effective <= TimeSpan.Zero) return;

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = effective
        };
        _memory.Set(key, value, options);
        _memoryKeys.TryAdd(key, 0);
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch { return default; }
    }

    private async Task<CacheRow?> ReadRowAsync(string key, CancellationToken ct)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT key,tier,value,etag,fetched_at,expires_at,size_bytes
            FROM cache_entries WHERE key=$key
            """;
        cmd.Add("$key", key);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new CacheRow
        {
            Key = reader.GetString(0),
            Tier = (CacheTier)reader.GetInt(1),
            Value = reader.GetStringOrNull(2),
            ETag = reader.GetStringOrNull(3),
            FetchedAt = reader.GetDateTimeOffset(4),
            ExpiresAt = reader.GetDateTimeOffset(5),
            SizeBytes = reader.GetLong(6)
        };
    }

    public void Dispose()
    {
        foreach (var gate in _locks.Values) gate.Dispose();
        _locks.Clear();
        _memory.Dispose();
    }

    private sealed class CacheRow
    {
        public string Key { get; init; } = string.Empty;
        public CacheTier Tier { get; init; }
        public string? Value { get; init; }
        public string? ETag { get; init; }
        public DateTimeOffset FetchedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public long SizeBytes { get; init; }
    }
}
