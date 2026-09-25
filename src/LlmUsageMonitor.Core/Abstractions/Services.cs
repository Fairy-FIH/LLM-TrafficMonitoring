using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Core.Abstractions;

public sealed class PricingFetchResult
{
    public bool NotModified { get; init; }
    public IReadOnlyList<ModelPricing> Items { get; init; } = Array.Empty<ModelPricing>();
    public string? ETag { get; init; }
}

/// <summary>A remote catalog of model prices (LiteLLM, OpenRouter, ...).</summary>
public interface IPricingSource
{
    string Id { get; }
    string DisplayName { get; }
    Task<PricingFetchResult> FetchAsync(CancellationToken ct = default);
}

public interface IPricingService
{
    /// <summary>Pull all enabled sources into the store (respecting cache TTL + degradation).</summary>
    Task<int> RefreshAsync(CancellationToken ct = default);

    Task<ModelPricing?> ResolveAsync(ProviderKind provider, string model, CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> SearchAsync(string? text, string? provider, int limit = 500, CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken ct = default);
    Task SetManualAsync(ModelPricing pricing, CancellationToken ct = default);
    Task DeleteAsync(string provider, string model, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);

    event EventHandler? Changed;
}

public interface IExchangeRateService
{
    Task<ExchangeRateInfo> GetAsync(bool forceRefresh = false, CancellationToken ct = default);
    Task<decimal> GetUsdToCnyAsync(bool forceRefresh = false, CancellationToken ct = default);
    Task LockAsync(decimal rate, CancellationToken ct = default);
    Task UnlockAsync(CancellationToken ct = default);
    event EventHandler<ExchangeRateInfo>? Changed;
}

public interface ICostCalculator
{
    /// <summary>Cost in USD given token usage and the resolved price for the model.</summary>
    decimal ComputeUsd(TokenUsage usage, ModelPricing? pricing, decimal usdToCny = 7.2m);

    /// <summary>Convert a USD amount into the display currency using the given USD->target rate.</summary>
    decimal Convert(decimal usd, Currency target, decimal usdToTargetRate);

    string Format(decimal usd, Currency currency, decimal usdToTargetRate);
    string Symbol(Currency currency);
}

public interface ITokenEstimator
{
    /// <summary>Count input tokens for text. Uses a real tokenizer when available, otherwise a heuristic.</summary>
    (long Tokens, bool Exact) CountTokens(string model, string text);

    TokenEstimate Estimate(string model, string prompt, int maxOutputTokens,
        ModelPricing? pricing = null, decimal usdToCny = 7.2m);
}

public sealed class SyncProgress
{
    public int Completed { get; init; }
    public int Total { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class SyncResult
{
    public int CredentialsProcessed { get; set; }
    public int RecordsAdded { get; set; }
    public int Failures { get; set; }
    public List<string> Messages { get; } = new();
}

public interface IUsageSyncService
{
    Task<SyncResult> SyncAllAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default);
    Task<SyncResult> SyncCredentialAsync(ApiCredential credential, CancellationToken ct = default);
}

public interface IBudgetService
{
    Task<IReadOnlyList<AlertRecord>> EvaluateAsync(CancellationToken ct = default);
    event EventHandler<AlertRecord>? AlertRaised;
}

public interface IReportExporter
{
    Task ExportAsync(ReportFormat format, string path, UsageQuery query, CancellationToken ct = default);
}

public interface ILocalProxyServer
{
    bool IsRunning { get; }
    int Port { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    event EventHandler<UsageRecord>? UsageCaptured;
    event EventHandler<string>? Log;
}
