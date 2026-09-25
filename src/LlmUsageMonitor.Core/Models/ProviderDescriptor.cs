namespace LlmUsageMonitor.Core.Models;

/// <summary>A description of a platform that the UI and adapters share.</summary>
public sealed class ProviderDescriptor
{
    public ProviderKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string DefaultBaseUrl { get; init; } = string.Empty;

    /// <summary>True when requests use the OpenAI chat-completions wire format.</summary>
    public bool OpenAiCompatible { get; init; }

    /// <summary>True when the platform exposes a billable-usage API we can poll.</summary>
    public bool SupportsOfficialUsage { get; init; }

    /// <summary>Kind of admin API, consumed by the provider adapter.</summary>
    public string OfficialUsageApi { get; init; } = "none";

    public string DefaultColorHex { get; init; } = "#4F8CFF";
    public string? DocsUrl { get; init; }
    public string? Note { get; init; }

    /// <summary>Web console URL used by the built-in browser-login capture module.</summary>
    public string? ConsoleUrl { get; init; }
}
