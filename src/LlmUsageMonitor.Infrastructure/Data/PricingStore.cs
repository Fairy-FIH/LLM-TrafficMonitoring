using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using Microsoft.Data.Sqlite;

namespace LlmUsageMonitor.Infrastructure.Data;

public sealed class PricingStore : IPricingStore
{
    private readonly Database _db;
    public PricingStore(Database db) => _db = db;

    public event EventHandler? Changed;

    public async Task UpsertAsync(IEnumerable<ModelPricing> items, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT INTO pricing(provider,model,input_per_million,output_per_million,
                cached_input_per_million,cache_write_per_million,currency,source,is_manual,updated_at)
            VALUES($provider,$model,$in,$out,$cached,$write,$cur,$source,0,$updated)
            ON CONFLICT(provider,model) DO UPDATE SET
                input_per_million=excluded.input_per_million,
                output_per_million=excluded.output_per_million,
                cached_input_per_million=excluded.cached_input_per_million,
                cache_write_per_million=excluded.cache_write_per_million,
                currency=excluded.currency,
                source=excluded.source,
                updated_at=excluded.updated_at
            WHERE pricing.is_manual=0;
            """;
        var p = new[]
        {
            cmd.CreateParameter(), cmd.CreateParameter(), cmd.CreateParameter(), cmd.CreateParameter(),
            cmd.CreateParameter(), cmd.CreateParameter(), cmd.CreateParameter(), cmd.CreateParameter(),
            cmd.CreateParameter()
        };
        var names = new[] { "$provider", "$model", "$in", "$out", "$cached", "$write", "$cur", "$source", "$updated" };
        for (var i = 0; i < p.Length; i++) { p[i].ParameterName = names[i]; cmd.Parameters.Add(p[i]); }

        foreach (var m in items)
        {
            p[0].Value = m.Provider;
            p[1].Value = m.Model;
            p[2].Value = SqliteExtensions.D(m.InputPerMillion);
            p[3].Value = SqliteExtensions.D(m.OutputPerMillion);
            p[4].Value = m.CachedInputPerMillion is { } c ? SqliteExtensions.D(c) : DBNull.Value;
            p[5].Value = m.CacheWritePerMillion is { } w ? SqliteExtensions.D(w) : DBNull.Value;
            p[6].Value = (int)m.Currency;
            p[7].Value = m.Source;
            p[8].Value = SqliteExtensions.Dt(m.UpdatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ModelPricing?> FindAsync(string provider, string model, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM pricing WHERE provider=$p AND model=$m LIMIT 1";
        cmd.Add("$p", provider);
        cmd.Add("$m", model);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ModelPricing>> SearchAsync(string? text, string? provider, int limit = 500, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            clauses.Add("model LIKE $text");
            cmd.Add("$text", $"%{text}%");
        }
        if (!string.IsNullOrWhiteSpace(provider))
        {
            clauses.Add("provider = $p");
            cmd.Add("$p", provider);
        }
        var where = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        cmd.CommandText = $"SELECT {Columns} FROM pricing {where} ORDER BY provider, model LIMIT $limit";
        cmd.Add("$limit", limit);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM pricing ORDER BY provider, model";
        return await ReadListAsync(cmd, ct);
    }

    public async Task SetManualAsync(ModelPricing pricing, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pricing(provider,model,input_per_million,output_per_million,
                cached_input_per_million,cache_write_per_million,currency,source,is_manual,updated_at)
            VALUES($provider,$model,$in,$out,$cached,$write,$cur,'manual',1,$updated)
            ON CONFLICT(provider,model) DO UPDATE SET
                input_per_million=$in, output_per_million=$out,
                cached_input_per_million=$cached, cache_write_per_million=$write,
                currency=$cur, source='manual', is_manual=1, updated_at=$updated;
            """;
        cmd.Add("$provider", pricing.Provider);
        cmd.Add("$model", pricing.Model);
        cmd.Add("$in", SqliteExtensions.D(pricing.InputPerMillion));
        cmd.Add("$out", SqliteExtensions.D(pricing.OutputPerMillion));
        cmd.Add("$cached", pricing.CachedInputPerMillion is { } c ? SqliteExtensions.D(c) : DBNull.Value);
        cmd.Add("$write", pricing.CacheWritePerMillion is { } w ? SqliteExtensions.D(w) : DBNull.Value);
        cmd.Add("$cur", (int)pricing.Currency);
        cmd.Add("$updated", SqliteExtensions.Dt(DateTimeOffset.Now));
        await cmd.ExecuteNonQueryAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(string provider, string model, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pricing WHERE provider=$p AND model=$m";
        cmd.Add("$p", provider);
        cmd.Add("$m", model);
        await cmd.ExecuteNonQueryAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pricing";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private const string Columns =
        "provider,model,input_per_million,output_per_million,cached_input_per_million," +
        "cache_write_per_million,currency,source,is_manual,updated_at";

    private static async Task<IReadOnlyList<ModelPricing>> ReadListAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<ModelPricing>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list;
    }

    private static ModelPricing Map(SqliteDataReader r) => new()
    {
        Provider = r.GetString(0),
        Model = r.GetString(1),
        InputPerMillion = r.GetDecimal(2),
        OutputPerMillion = r.GetDecimal(3),
        CachedInputPerMillion = r.GetDecimalOrNull(4),
        CacheWritePerMillion = r.GetDecimalOrNull(5),
        Currency = (Currency)r.GetInt(6),
        Source = r.GetStringOrNull(7) ?? "unknown",
        IsManual = r.GetInt(8) != 0,
        UpdatedAt = r.GetDateTimeOffset(9)
    };
}
