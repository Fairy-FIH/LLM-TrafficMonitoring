using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Infrastructure.Data;

namespace LlmUsageMonitor.Tests;

public class UsageStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"llmusage-{Guid.NewGuid():N}.db");
    private readonly Database _db;
    private readonly UsageStore _store;

    public UsageStoreTests()
    {
        _db = new Database(_dbPath);
        _db.InitializeAsync().GetAwaiter().GetResult();
        _store = new UsageStore(_db);
    }

    [Fact]
    public async Task Summarizes_records_within_local_date_range()
    {
        await _store.AddRangeAsync(new[]
        {
            new UsageRecord
            {
                Provider = ProviderKind.DeepSeek,
                Model = "deepseek-flash",
                Timestamp = DateTimeOffset.Now,
                InputTokens = 100,
                OutputTokens = 50,
                Requests = 1,
                CostUsd = 0.001m,
                Source = UsageSource.LocalProxy,
                DedupKey = "t1"
            }
        });

        // This is the exact construction that used to throw on non-UTC machines.
        var from = new DateTimeOffset(DateTime.Today.AddDays(-29));
        var summary = await _store.GetSummaryAsync(new UsageQuery { From = from });

        Assert.Equal(1, summary.Requests);
        Assert.Equal(100, summary.InputTokens);
        Assert.Equal(50, summary.OutputTokens);
        Assert.Equal(150, summary.TotalTokens);
        Assert.Equal(0.001m, summary.CostUsd);
    }

    [Fact]
    public async Task Deduplicates_by_dedup_key()
    {
        var record = new UsageRecord
        {
            Provider = ProviderKind.DeepSeek,
            Model = "m",
            Timestamp = DateTimeOffset.Now,
            InputTokens = 1,
            DedupKey = "dup"
        };

        await _store.AddRangeAsync(new[] { record });
        var inserted = await _store.AddRangeAsync(new[] { record });

        Assert.Equal(0, inserted);
        var summary = await _store.GetSummaryAsync(new UsageQuery());
        Assert.Equal(1, summary.Requests);
    }

    [Fact]
    public async Task Groups_by_provider_and_model()
    {
        await _store.AddRangeAsync(new[]
        {
            new UsageRecord { Provider = ProviderKind.DeepSeek, Model = "flash", Timestamp = DateTimeOffset.Now, InputTokens = 10, CostUsd = 0.01m, DedupKey = "a" },
            new UsageRecord { Provider = ProviderKind.DeepSeek, Model = "pro", Timestamp = DateTimeOffset.Now, InputTokens = 20, CostUsd = 0.02m, DedupKey = "b" }
        });

        var byModel = await _store.GetByModelAsync(new UsageQuery(), 10);

        Assert.Equal(2, byModel.Count);
        Assert.Contains(byModel, a => a.Key == $"{(int)ProviderKind.DeepSeek}|flash");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var file = _dbPath + suffix;
            if (File.Exists(file)) { try { File.Delete(file); } catch { /* ignore */ } }
        }
    }
}
