using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Core.Abstractions;

public sealed class ProviderBalance
{
    public decimal Amount { get; set; }
    public Currency Currency { get; set; } = Currency.USD;
    public string? Raw { get; set; }
}

/// <summary>Platform-specific logic: usage pulling, validation, model listing, balance.</summary>
public interface IProviderAdapter
{
    ProviderKind Kind { get; }
    ProviderDescriptor Descriptor { get; }

    /// <summary>Cheap call to verify a credential works. Throws on failure.</summary>
    Task ValidateAsync(ApiCredential credential, CancellationToken ct = default);

    /// <summary>Pull billable usage for a window. May return empty when unsupported.</summary>
    Task<IReadOnlyList<UsageRecord>> FetchUsageAsync(ApiCredential credential,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListModelsAsync(ApiCredential credential, CancellationToken ct = default);

    Task<ProviderBalance?> GetBalanceAsync(ApiCredential credential, CancellationToken ct = default);
}

public interface IProviderRegistry
{
    IReadOnlyList<ProviderDescriptor> Descriptors { get; }
    ProviderDescriptor GetDescriptor(ProviderKind kind);
    IProviderAdapter GetAdapter(ProviderKind kind);
    IProviderAdapter? FindAdapter(string providerNameOrBaseUrl);
}
