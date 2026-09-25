using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Infrastructure.Data;

public sealed class ExchangeRateStore : IExchangeRateStore
{
    private readonly Database _db;
    public ExchangeRateStore(Database db) => _db = db;

    public async Task<ExchangeRateInfo> GetAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT target,rate,source,fetched_at,is_locked FROM exchange_rates WHERE target=$t";
        cmd.Add("$t", (int)Currency.CNY);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new ExchangeRateInfo { Target = Currency.CNY, Rate = 7.2m, Source = "default" };

        return new ExchangeRateInfo
        {
            Target = (Currency)reader.GetInt(0),
            Rate = reader.GetDecimal(1),
            Source = reader.GetStringOrNull(2) ?? "unknown",
            FetchedAt = reader.GetDateTimeOffset(3),
            IsLocked = reader.GetInt(4) != 0
        };
    }

    public async Task SaveAsync(ExchangeRateInfo info, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO exchange_rates(target,rate,source,fetched_at,is_locked)
            VALUES($t,$rate,$source,$fetched,$locked)
            ON CONFLICT(target) DO UPDATE SET rate=$rate,source=$source,fetched_at=$fetched,is_locked=$locked;
            """;
        cmd.Add("$t", (int)info.Target);
        cmd.Add("$rate", SqliteExtensions.D(info.Rate));
        cmd.Add("$source", info.Source);
        cmd.Add("$fetched", SqliteExtensions.Dt(info.FetchedAt));
        cmd.Add("$locked", info.IsLocked ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
