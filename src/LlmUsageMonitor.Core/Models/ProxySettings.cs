namespace LlmUsageMonitor.Core.Models;

public sealed class ProxySettings
{
    public ProxyMode Mode { get; set; } = ProxyMode.None;
    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Http;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7890;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Do not route localhost traffic through the proxy.</summary>
    public bool BypassLocal { get; set; } = true;

    public bool IsConfigured =>
        Mode != ProxyMode.None &&
        (Mode == ProxyMode.System || (!string.IsNullOrWhiteSpace(Host) && Port > 0));

    public string ToUri() =>
        Protocol == ProxyProtocol.Socks5
            ? $"socks5://{Host}:{Port}"
            : $"http://{Host}:{Port}";
}

/// <summary>Settings for the built-in OpenAI-compatible capture proxy.</summary>
public sealed class LocalProxySettings
{
    public bool Enabled { get; set; }
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8787;

    /// <summary>Optional bearer token clients must present; empty disables auth.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Credential id used to resolve upstream when the model cannot be matched.</summary>
    public Guid? DefaultCredentialId { get; set; }

    /// <summary>Platform used when no stored credential matches the request.</summary>
    public ProviderKind DefaultProvider { get; set; } = ProviderKind.DeepSeek;

    /// <summary>Upstream base URL used when no credential matches (client's own key is forwarded).</summary>
    public string? DefaultBaseUrl { get; set; } = "https://api.deepseek.com/v1";

    /// <summary>Forward the client's Authorization header upstream instead of using a stored key.</summary>
    public bool PassThroughAuth { get; set; } = true;
}
