namespace LlmUsageMonitor.Core.Models;

/// <summary>A logical group of API credentials (e.g. a project or team).</summary>
public sealed class KeyGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Group";
    public string? Description { get; set; }
    /// <summary>Accent color used by the UI for this group (hex, e.g. #4F8CFF).</summary>
    public string ColorHex { get; set; } = "#4F8CFF";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>An API credential belonging to a platform, optionally inside a group.</summary>
public sealed class ApiCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GroupId { get; set; }
    public ProviderKind Provider { get; set; }
    public string Name { get; set; } = "New Key";

    /// <summary>Plain secret. Encrypted at rest; never persisted in cleartext.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Optional admin/management key for official usage APIs.</summary>
    public string? AdminSecret { get; set; }

    /// <summary>Override base URL (proxy/gateway). Empty means provider default.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional per-credential organization / project id.</summary>
    public string? OrganizationId { get; set; }

    public CollectionMode CollectionMode { get; set; } = CollectionMode.Both;
    public bool Enabled { get; set; } = true;

    /// <summary>Optional per-credential proxy override string, e.g. http://127.0.0.1:7890.</summary>
    public string? ProxyOverride { get; set; }

    public string? Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Masked display form, e.g. sk-…a1b2.</summary>
    public string MaskedSecret => Mask(Secret);

    public static string Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        if (secret.Length <= 8) return "****";
        return $"{secret[..4]}…{secret[^4..]}";
    }
}
