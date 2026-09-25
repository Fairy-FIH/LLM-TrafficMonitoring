using System.Globalization;
using Microsoft.Data.Sqlite;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Infrastructure.Data;

/// <summary>Owns the SQLite connection string and schema migration.</summary>
public sealed class Database
{
    private readonly string _path;

    public Database(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string ConnectionString { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Schema;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS key_groups (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT,
            color_hex TEXT,
            created_at TEXT
        );

        CREATE TABLE IF NOT EXISTS credentials (
            id TEXT PRIMARY KEY,
            group_id TEXT,
            provider INTEGER NOT NULL,
            name TEXT NOT NULL,
            secret TEXT,
            admin_secret TEXT,
            base_url TEXT,
            organization_id TEXT,
            collection_mode INTEGER NOT NULL DEFAULT 3,
            enabled INTEGER NOT NULL DEFAULT 1,
            proxy_override TEXT,
            tags TEXT,
            created_at TEXT
        );

        CREATE TABLE IF NOT EXISTS usage_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            provider INTEGER NOT NULL,
            credential_id TEXT,
            group_id TEXT,
            model TEXT NOT NULL,
            timestamp TEXT NOT NULL,
            input_tokens INTEGER NOT NULL DEFAULT 0,
            output_tokens INTEGER NOT NULL DEFAULT 0,
            cached_tokens INTEGER NOT NULL DEFAULT 0,
            requests INTEGER NOT NULL DEFAULT 1,
            cost_usd TEXT NOT NULL DEFAULT '0',
            source INTEGER NOT NULL DEFAULT 0,
            dedup_key TEXT
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_usage_dedup ON usage_records(dedup_key) WHERE dedup_key IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_usage_ts ON usage_records(timestamp);
        CREATE INDEX IF NOT EXISTS ix_usage_provider ON usage_records(provider);
        CREATE INDEX IF NOT EXISTS ix_usage_cred ON usage_records(credential_id);

        CREATE TABLE IF NOT EXISTS pricing (
            provider TEXT NOT NULL,
            model TEXT NOT NULL,
            input_per_million TEXT NOT NULL DEFAULT '0',
            output_per_million TEXT NOT NULL DEFAULT '0',
            cached_input_per_million TEXT,
            cache_write_per_million TEXT,
            currency INTEGER NOT NULL DEFAULT 0,
            source TEXT,
            is_manual INTEGER NOT NULL DEFAULT 0,
            updated_at TEXT,
            PRIMARY KEY (provider, model)
        );

        CREATE TABLE IF NOT EXISTS cache_entries (
            key TEXT PRIMARY KEY,
            tier INTEGER NOT NULL,
            value TEXT,
            etag TEXT,
            fetched_at TEXT NOT NULL,
            expires_at TEXT NOT NULL,
            size_bytes INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_cache_expiry ON cache_entries(expires_at);

        CREATE TABLE IF NOT EXISTS cursors (
            provider INTEGER NOT NULL,
            credential_id TEXT NOT NULL DEFAULT '',
            cursor TEXT NOT NULL,
            PRIMARY KEY (provider, credential_id)
        );

        CREATE TABLE IF NOT EXISTS budgets (
            id TEXT PRIMARY KEY,
            scope_type INTEGER NOT NULL,
            scope_id TEXT,
            period INTEGER NOT NULL,
            limit_usd TEXT NOT NULL DEFAULT '0',
            thresholds TEXT,
            enabled INTEGER NOT NULL DEFAULT 1,
            notify_desktop INTEGER NOT NULL DEFAULT 1,
            created_at TEXT
        );

        CREATE TABLE IF NOT EXISTS alerts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            budget_id TEXT,
            level INTEGER NOT NULL,
            title TEXT,
            message TEXT,
            value_usd TEXT,
            limit_usd TEXT,
            threshold_percent INTEGER,
            acknowledged INTEGER NOT NULL DEFAULT 0,
            created_at TEXT
        );

        CREATE TABLE IF NOT EXISTS exchange_rates (
            target INTEGER PRIMARY KEY,
            rate TEXT NOT NULL,
            source TEXT,
            fetched_at TEXT,
            is_locked INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS meta (
            key TEXT PRIMARY KEY,
            value TEXT
        );
        """;
}

internal static class SqliteExtensions
{
    public static void Add(this SqliteCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    public static string? GetStringOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static Guid? GetGuidOrNull(this SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : Guid.TryParse(r.GetString(i), out var g) ? g : null;
    public static Guid GetGuid(this SqliteDataReader r, int i) => Guid.Parse(r.GetString(i));
    public static long GetLong(this SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);
    public static int GetInt(this SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);

    public static decimal GetDecimal(this SqliteDataReader r, int i)
        => r.IsDBNull(i) ? 0m : ParseDecimal(r.GetString(i));

    public static decimal? GetDecimalOrNull(this SqliteDataReader r, int i)
        => r.IsDBNull(i) ? null : ParseDecimal(r.GetString(i));

    public static DateTimeOffset GetDateTimeOffset(this SqliteDataReader r, int i)
        => r.IsDBNull(i) ? default : DateTimeOffset.Parse(r.GetString(i), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? GetDateTimeOffsetOrNull(this SqliteDataReader r, int i)
        => r.IsDBNull(i) ? null : DateTimeOffset.Parse(r.GetString(i), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    public static decimal ParseDecimal(string s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public static string D(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Dt(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);
    public static string Dt(DateTime? value) => value?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty;
}
