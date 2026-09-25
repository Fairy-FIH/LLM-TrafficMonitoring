using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using Microsoft.Data.Sqlite;

namespace LlmUsageMonitor.Infrastructure.Data;

public sealed class UsageStore : IUsageStore
{
    private readonly Database _db;
    public UsageStore(Database db) => _db = db;

    public async Task<int> AddRangeAsync(IEnumerable<UsageRecord> records, CancellationToken ct = default)
    {
        var list = records.ToList();
        if (list.Count == 0) return 0;

        await using var conn = _db.Open();
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO usage_records
                (provider,credential_id,group_id,model,timestamp,input_tokens,output_tokens,
                 cached_tokens,requests,cost_usd,source,dedup_key)
            VALUES($provider,$cred,$group,$model,$ts,$in,$out,$cached,$req,$cost,$source,$dedup);
            """;

        var pProvider = cmd.CreateParameter(); pProvider.ParameterName = "$provider"; cmd.Parameters.Add(pProvider);
        var pCred = cmd.CreateParameter(); pCred.ParameterName = "$cred"; cmd.Parameters.Add(pCred);
        var pGroup = cmd.CreateParameter(); pGroup.ParameterName = "$group"; cmd.Parameters.Add(pGroup);
        var pModel = cmd.CreateParameter(); pModel.ParameterName = "$model"; cmd.Parameters.Add(pModel);
        var pTs = cmd.CreateParameter(); pTs.ParameterName = "$ts"; cmd.Parameters.Add(pTs);
        var pIn = cmd.CreateParameter(); pIn.ParameterName = "$in"; cmd.Parameters.Add(pIn);
        var pOut = cmd.CreateParameter(); pOut.ParameterName = "$out"; cmd.Parameters.Add(pOut);
        var pCached = cmd.CreateParameter(); pCached.ParameterName = "$cached"; cmd.Parameters.Add(pCached);
        var pReq = cmd.CreateParameter(); pReq.ParameterName = "$req"; cmd.Parameters.Add(pReq);
        var pCost = cmd.CreateParameter(); pCost.ParameterName = "$cost"; cmd.Parameters.Add(pCost);
        var pSource = cmd.CreateParameter(); pSource.ParameterName = "$source"; cmd.Parameters.Add(pSource);
        var pDedup = cmd.CreateParameter(); pDedup.ParameterName = "$dedup"; cmd.Parameters.Add(pDedup);

        var inserted = 0;
        foreach (var r in list)
        {
            pProvider.Value = (int)r.Provider;
            pCred.Value = (object?)r.CredentialId?.ToString() ?? DBNull.Value;
            pGroup.Value = (object?)r.GroupId?.ToString() ?? DBNull.Value;
            pModel.Value = r.Model;
            pTs.Value = r.Timestamp.ToString("o");
            pIn.Value = r.InputTokens;
            pOut.Value = r.OutputTokens;
            pCached.Value = r.CachedTokens;
            pReq.Value = r.Requests;
            pCost.Value = SqliteExtensions.D(r.CostUsd);
            pSource.Value = (int)r.Source;
            pDedup.Value = (object?)r.DedupKey ?? DBNull.Value;
            inserted += await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return inserted;
    }

    public async Task<UsageSummary> GetSummaryAsync(UsageQuery query, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COALESCE(SUM(requests),0), COALESCE(SUM(input_tokens),0),
                   COALESCE(SUM(output_tokens),0), COALESCE(SUM(cached_tokens),0),
                   COALESCE(SUM(CAST(cost_usd AS REAL)),0)
            FROM usage_records {BuildWhere(query, cmd)}
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new UsageSummary();
        return new UsageSummary
        {
            Requests = reader.GetInt64(0),
            InputTokens = reader.GetInt64(1),
            OutputTokens = reader.GetInt64(2),
            CachedTokens = reader.GetInt64(3),
            CostUsd = (decimal)reader.GetDouble(4)
        };
    }

    public Task<IReadOnlyList<UsageAggregate>> GetByProviderAsync(UsageQuery query, CancellationToken ct = default)
        => AggregateAsync(query, "provider", null, 0, ct);

    public async Task<IReadOnlyList<UsageAggregate>> GetByModelAsync(UsageQuery query, int top = 10, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        var limit = top > 0 ? top : 1000;
        cmd.CommandText = $"""
            SELECT provider, model, COALESCE(SUM(requests),0), COALESCE(SUM(input_tokens),0),
                   COALESCE(SUM(output_tokens),0), COALESCE(SUM(CAST(cost_usd AS REAL)),0)
            FROM usage_records {BuildWhere(query, cmd)}
            GROUP BY provider, model
            ORDER BY 6 DESC
            LIMIT $top
            """;
        cmd.Add("$top", limit);

        var list = new List<UsageAggregate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var provider = reader.GetInt(0);
            var model = reader.GetString(1);
            list.Add(new UsageAggregate
            {
                Key = provider + "|" + model,
                Requests = reader.GetInt64(2),
                InputTokens = reader.GetInt64(3),
                OutputTokens = reader.GetInt64(4),
                CostUsd = (decimal)reader.GetDouble(5)
            });
        }
        return list;
    }

    public Task<IReadOnlyList<UsageAggregate>> GetByGroupAsync(UsageQuery query, CancellationToken ct = default)
        => AggregateAsync(query, "group_id", "COALESCE(group_id,'(none)')", 0, ct);

    public Task<IReadOnlyList<UsageAggregate>> GetDailyAsync(UsageQuery query, CancellationToken ct = default)
        => AggregateAsync(query, "substr(timestamp,1,10)", "substr(timestamp,1,10)", 0, ct);

    private async Task<IReadOnlyList<UsageAggregate>> AggregateAsync(
        UsageQuery query, string groupExpr, string? selectExpr, int top, CancellationToken ct)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        var select = selectExpr ?? groupExpr;
        var limit = top > 0 ? "LIMIT " + top : string.Empty;
        cmd.CommandText = $"""
            SELECT {select} AS k, COALESCE(SUM(requests),0), COALESCE(SUM(input_tokens),0),
                   COALESCE(SUM(output_tokens),0), COALESCE(SUM(CAST(cost_usd AS REAL)),0)
            FROM usage_records {BuildWhere(query, cmd)}
            GROUP BY {groupExpr}
            ORDER BY 5 DESC
            {limit}
            """;
        var list = new List<UsageAggregate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = reader.IsDBNull(0) ? string.Empty : reader.GetValue(0).ToString() ?? string.Empty;
            DateTimeOffset? bucket = null;
            if (DateTime.TryParse(key, out var d)) bucket = new DateTimeOffset(d, TimeSpan.Zero);
            list.Add(new UsageAggregate
            {
                Key = key,
                Bucket = bucket,
                Requests = reader.GetInt64(1),
                InputTokens = reader.GetInt64(2),
                OutputTokens = reader.GetInt64(3),
                CostUsd = (decimal)reader.GetDouble(4)
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<UsageRecord>> GetRecentAsync(int take = 200, UsageQuery? query = null, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT id,provider,credential_id,group_id,model,timestamp,input_tokens,output_tokens,
                   cached_tokens,requests,cost_usd,source,dedup_key
            FROM usage_records {BuildWhere(query, cmd)}
            ORDER BY timestamp DESC, id DESC LIMIT $take
            """;
        cmd.Add("$take", take);
        var list = new List<UsageRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new UsageRecord
            {
                Id = reader.GetInt64(0),
                Provider = (ProviderKind)reader.GetInt(1),
                CredentialId = reader.GetGuidOrNull(2),
                GroupId = reader.GetGuidOrNull(3),
                Model = reader.GetString(4),
                Timestamp = reader.GetDateTimeOffset(5),
                InputTokens = reader.GetInt64(6),
                OutputTokens = reader.GetInt64(7),
                CachedTokens = reader.GetInt64(8),
                Requests = reader.GetInt64(9),
                CostUsd = reader.GetDecimal(10),
                Source = (UsageSource)reader.GetInt(11),
                DedupKey = reader.GetStringOrNull(12)
            });
        }
        return list;
    }

    public async Task<DateTimeOffset?> GetCursorAsync(ProviderKind provider, Guid? credentialId, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cursor FROM cursors WHERE provider=$p AND credential_id=$c";
        cmd.Add("$p", (int)provider);
        cmd.Add("$c", credentialId?.ToString() ?? string.Empty);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string s && DateTimeOffset.TryParse(s, out var dto) ? dto : null;
    }

    public async Task SetCursorAsync(ProviderKind provider, Guid? credentialId, DateTimeOffset cursor, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cursors(provider,credential_id,cursor) VALUES($p,$c,$cur)
            ON CONFLICT(provider,credential_id) DO UPDATE SET cursor=$cur;
            """;
        cmd.Add("$p", (int)provider);
        cmd.Add("$c", credentialId?.ToString() ?? string.Empty);
        cmd.Add("$cur", cursor.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteAsync(UsageQuery query, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM usage_records {BuildWhere(query, cmd)}";
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM usage_records; DELETE FROM cursors;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string BuildWhere(UsageQuery? q, SqliteCommand cmd)
    {
        if (q is null) return string.Empty;
        var clauses = new List<string>();

        if (q.From is { } from)
        {
            clauses.Add("timestamp >= $from");
            cmd.Add("$from", from.ToString("o"));
        }
        if (q.To is { } to)
        {
            clauses.Add("timestamp < $to");
            cmd.Add("$to", to.ToString("o"));
        }
        if (q.Provider is { } provider)
        {
            clauses.Add("provider = $providerFilter");
            cmd.Add("$providerFilter", (int)provider);
        }
        if (q.GroupId is { } group)
        {
            clauses.Add("group_id = $groupId");
            cmd.Add("$groupId", group.ToString());
        }
        if (q.CredentialId is { } cred)
        {
            clauses.Add("credential_id = $credId");
            cmd.Add("$credId", cred.ToString());
        }
        if (!string.IsNullOrWhiteSpace(q.Model))
        {
            clauses.Add("model = $modelFilter");
            cmd.Add("$modelFilter", q.Model);
        }
        if (q.Source is { } source)
        {
            clauses.Add("source = $sourceFilter");
            cmd.Add("$sourceFilter", (int)source);
        }

        return clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
    }
}
