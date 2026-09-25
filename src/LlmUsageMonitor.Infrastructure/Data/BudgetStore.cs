using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using Microsoft.Data.Sqlite;

namespace LlmUsageMonitor.Infrastructure.Data;

public sealed class BudgetStore : IBudgetStore
{
    private readonly Database _db;
    public BudgetStore(Database db) => _db = db;

    public async Task<IReadOnlyList<BudgetRule>> GetRulesAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,scope_type,scope_id,period,limit_usd,thresholds,enabled,notify_desktop,created_at FROM budgets";
        var list = new List<BudgetRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BudgetRule
            {
                Id = reader.GetGuid(0),
                ScopeType = (BudgetScopeType)reader.GetInt(1),
                ScopeId = reader.GetGuidOrNull(2),
                Period = (BudgetPeriod)reader.GetInt(3),
                LimitUsd = reader.GetDecimal(4),
                Thresholds = ParseThresholds(reader.GetStringOrNull(5)),
                Enabled = reader.GetInt(6) != 0,
                NotifyDesktop = reader.GetInt(7) != 0,
                CreatedAt = reader.GetDateTimeOffset(8)
            });
        }
        return list;
    }

    public async Task SaveRuleAsync(BudgetRule rule, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO budgets(id,scope_type,scope_id,period,limit_usd,thresholds,enabled,notify_desktop,created_at)
            VALUES($id,$scope,$scopeId,$period,$limit,$thresholds,$enabled,$notify,$created)
            ON CONFLICT(id) DO UPDATE SET scope_type=$scope,scope_id=$scopeId,period=$period,
                limit_usd=$limit,thresholds=$thresholds,enabled=$enabled,notify_desktop=$notify;
            """;
        cmd.Add("$id", rule.Id.ToString());
        cmd.Add("$scope", (int)rule.ScopeType);
        cmd.Add("$scopeId", rule.ScopeId?.ToString());
        cmd.Add("$period", (int)rule.Period);
        cmd.Add("$limit", SqliteExtensions.D(rule.LimitUsd));
        cmd.Add("$thresholds", string.Join(",", rule.Thresholds));
        cmd.Add("$enabled", rule.Enabled ? 1 : 0);
        cmd.Add("$notify", rule.NotifyDesktop ? 1 : 0);
        cmd.Add("$created", SqliteExtensions.Dt(rule.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM budgets WHERE id=$id";
        cmd.Add("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AlertRecord>> GetAlertsAsync(int take = 100, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,budget_id,level,title,message,value_usd,limit_usd,threshold_percent,acknowledged,created_at
            FROM alerts ORDER BY created_at DESC LIMIT $take
            """;
        cmd.Add("$take", take);
        var list = new List<AlertRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AlertRecord
            {
                Id = reader.GetInt64(0),
                BudgetId = reader.GetGuidOrNull(1),
                Level = (AlertLevel)reader.GetInt(2),
                Title = reader.GetStringOrNull(3) ?? string.Empty,
                Message = reader.GetStringOrNull(4) ?? string.Empty,
                ValueUsd = reader.GetDecimal(5),
                LimitUsd = reader.GetDecimal(6),
                ThresholdPercent = reader.GetInt(7),
                Acknowledged = reader.GetInt(8) != 0,
                CreatedAt = reader.GetDateTimeOffset(9)
            });
        }
        return list;
    }

    public async Task AddAlertAsync(AlertRecord alert, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alerts(budget_id,level,title,message,value_usd,limit_usd,threshold_percent,acknowledged,created_at)
            VALUES($budget,$level,$title,$message,$value,$limit,$percent,$ack,$created);
            """;
        cmd.Add("$budget", alert.BudgetId?.ToString());
        cmd.Add("$level", (int)alert.Level);
        cmd.Add("$title", alert.Title);
        cmd.Add("$message", alert.Message);
        cmd.Add("$value", SqliteExtensions.D(alert.ValueUsd));
        cmd.Add("$limit", SqliteExtensions.D(alert.LimitUsd));
        cmd.Add("$percent", alert.ThresholdPercent);
        cmd.Add("$ack", alert.Acknowledged ? 1 : 0);
        cmd.Add("$created", SqliteExtensions.Dt(alert.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AcknowledgeAsync(long alertId, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE alerts SET acknowledged=1 WHERE id=$id";
        cmd.Add("$id", alertId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ClearAlertsAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM alerts";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static List<int> ParseThresholds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<int> { 80, 100 };
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
    }
}
