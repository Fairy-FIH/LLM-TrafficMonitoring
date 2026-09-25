using LlmUsageMonitor.Infrastructure.Data;
using LlmUsageMonitor.Infrastructure.Security;

namespace LlmUsageMonitor.Tests;

public class SecretProtectorTests
{
    [Fact]
    public void Round_trips_a_secret()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llmtest-{Guid.NewGuid():N}.key");
        try
        {
            var protector = new AesSecretProtector(path);
            var token = protector.Protect("sk-super-secret-value");

            Assert.True(protector.IsProtected(token));
            Assert.DoesNotContain("sk-super-secret-value", token);
            Assert.Equal("sk-super-secret-value", protector.Unprotect(token));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Produces_different_ciphertext_each_time()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llmtest-{Guid.NewGuid():N}.key");
        try
        {
            var protector = new AesSecretProtector(path);
            Assert.NotEqual(protector.Protect("value"), protector.Protect("value"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class CacheStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"llmtest-{Guid.NewGuid():N}.db");
    private readonly Database _db;
    private readonly CacheStore _cache;

    public CacheStoreTests()
    {
        _db = new Database(_dbPath);
        _db.InitializeAsync().GetAwaiter().GetResult();
        _cache = new CacheStore(_db,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            new LlmUsageMonitor.Core.Models.CacheTtlSettings());
    }

    [Fact]
    public async Task Set_then_get_round_trips()
    {
        await _cache.SetAsync("k1", new List<string> { "a", "b" }, LlmUsageMonitor.Core.Models.CacheTier.Generic);

        var value = await _cache.GetAsync<List<string>>("k1", LlmUsageMonitor.Core.Models.CacheTier.Generic);

        Assert.NotNull(value);
        Assert.Equal(new[] { "a", "b" }, value!);
    }

    [Fact]
    public async Task Coalesces_concurrent_factory_calls()
    {
        var calls = 0;
        async Task<int> Factory(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(80, ct);
            return 42;
        }

        var tasks = Enumerable.Range(0, 8).Select(_ =>
            _cache.GetOrCreateAsync("coalesce", LlmUsageMonitor.Core.Models.CacheTier.Generic,
                (Func<CancellationToken, Task<int>>)(ct => Factory(ct))));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(42, r.Value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Serves_stale_value_when_factory_fails()
    {
        await _cache.SetAsync("stale", "old", LlmUsageMonitor.Core.Models.CacheTier.Usage,
            TimeSpan.FromMilliseconds(-1));

        var result = await _cache.GetOrCreateAsync("stale", LlmUsageMonitor.Core.Models.CacheTier.Usage,
            (Func<CancellationToken, Task<string>>)(_ => throw new InvalidOperationException("network down")));

        Assert.True(result.Stale);
        Assert.Equal("old", result.Value);
    }

    public void Dispose()
    {
        _cache.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var file = _dbPath + suffix;
            if (File.Exists(file)) { try { File.Delete(file); } catch { /* ignore */ } }
        }
    }
}
