using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Infrastructure.Data;

public sealed class CredentialStore : ICredentialStore
{
    private readonly Database _db;
    private readonly ISecretProtector _protector;

    public CredentialStore(Database db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<KeyGroup>> GetGroupsAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,description,color_hex,created_at FROM key_groups ORDER BY name";
        var list = new List<KeyGroup>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new KeyGroup
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Description = reader.GetStringOrNull(2),
                ColorHex = reader.GetStringOrNull(3) ?? "#4F8CFF",
                CreatedAt = reader.GetDateTimeOffset(4)
            });
        }
        return list;
    }

    public async Task SaveGroupAsync(KeyGroup group, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO key_groups(id,name,description,color_hex,created_at)
            VALUES($id,$name,$desc,$color,$created)
            ON CONFLICT(id) DO UPDATE SET name=$name,description=$desc,color_hex=$color;
            """;
        cmd.Add("$id", group.Id.ToString());
        cmd.Add("$name", group.Name);
        cmd.Add("$desc", group.Description);
        cmd.Add("$color", group.ColorHex);
        cmd.Add("$created", SqliteExtensions.Dt(group.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM key_groups WHERE id=$id; UPDATE credentials SET group_id=NULL WHERE group_id=$id;";
        cmd.Add("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<ApiCredential>> GetCredentialsAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,group_id,provider,name,secret,admin_secret,base_url,organization_id,
                   collection_mode,enabled,proxy_override,tags,created_at
            FROM credentials ORDER BY provider,name
            """;
        var list = new List<ApiCredential>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list;
    }

    public async Task<ApiCredential?> GetCredentialAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,group_id,provider,name,secret,admin_secret,base_url,organization_id,
                   collection_mode,enabled,proxy_override,tags,created_at
            FROM credentials WHERE id=$id
            """;
        cmd.Add("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task SaveCredentialAsync(ApiCredential credential, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO credentials(id,group_id,provider,name,secret,admin_secret,base_url,organization_id,
                                    collection_mode,enabled,proxy_override,tags,created_at)
            VALUES($id,$group,$provider,$name,$secret,$admin,$base,$org,$mode,$enabled,$proxy,$tags,$created)
            ON CONFLICT(id) DO UPDATE SET
                group_id=$group, provider=$provider, name=$name, secret=$secret, admin_secret=$admin,
                base_url=$base, organization_id=$org, collection_mode=$mode, enabled=$enabled,
                proxy_override=$proxy, tags=$tags;
            """;
        cmd.Add("$id", credential.Id.ToString());
        cmd.Add("$group", credential.GroupId?.ToString());
        cmd.Add("$provider", (int)credential.Provider);
        cmd.Add("$name", credential.Name);
        cmd.Add("$secret", Protect(credential.Secret));
        cmd.Add("$admin", Protect(credential.AdminSecret));
        cmd.Add("$base", credential.BaseUrl);
        cmd.Add("$org", credential.OrganizationId);
        cmd.Add("$mode", (int)credential.CollectionMode);
        cmd.Add("$enabled", credential.Enabled ? 1 : 0);
        cmd.Add("$proxy", credential.ProxyOverride);
        cmd.Add("$tags", credential.Tags);
        cmd.Add("$created", SqliteExtensions.Dt(credential.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteCredentialAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM credentials WHERE id=$id";
        cmd.Add("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string? Protect(string? value)
        => string.IsNullOrEmpty(value) ? value : _protector.Protect(value);

    private string? Unprotect(string? value)
        => string.IsNullOrEmpty(value) ? value : _protector.Unprotect(value);

    private ApiCredential Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetGuid(0),
        GroupId = r.GetGuidOrNull(1),
        Provider = (ProviderKind)r.GetInt(2),
        Name = r.GetString(3),
        Secret = Unprotect(r.GetStringOrNull(4)) ?? string.Empty,
        AdminSecret = Unprotect(r.GetStringOrNull(5)),
        BaseUrl = r.GetStringOrNull(6),
        OrganizationId = r.GetStringOrNull(7),
        CollectionMode = (CollectionMode)r.GetInt(8),
        Enabled = r.GetInt(9) != 0,
        ProxyOverride = r.GetStringOrNull(10),
        Tags = r.GetStringOrNull(11),
        CreatedAt = r.GetDateTimeOffset(12)
    };
}
