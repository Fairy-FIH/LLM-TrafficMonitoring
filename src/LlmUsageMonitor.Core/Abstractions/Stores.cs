using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Core.Abstractions;

public interface ISettingsStore
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
    event EventHandler<AppSettings>? Changed;
}

public interface ICredentialStore
{
    Task<IReadOnlyList<KeyGroup>> GetGroupsAsync(CancellationToken ct = default);
    Task SaveGroupAsync(KeyGroup group, CancellationToken ct = default);
    Task DeleteGroupAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiCredential>> GetCredentialsAsync(CancellationToken ct = default);
    Task<ApiCredential?> GetCredentialAsync(Guid id, CancellationToken ct = default);
    Task SaveCredentialAsync(ApiCredential credential, CancellationToken ct = default);
    Task DeleteCredentialAsync(Guid id, CancellationToken ct = default);

    event EventHandler? Changed;
}

public interface IUsageStore
{
    /// <summary>Insert records, skipping duplicates by <see cref="UsageRecord.DedupKey"/>.</summary>
    Task<int> AddRangeAsync(IEnumerable<UsageRecord> records, CancellationToken ct = default);

    Task<UsageSummary> GetSummaryAsync(UsageQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<UsageAggregate>> GetByProviderAsync(UsageQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<UsageAggregate>> GetByModelAsync(UsageQuery query, int top = 10, CancellationToken ct = default);
    Task<IReadOnlyList<UsageAggregate>> GetByGroupAsync(UsageQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<UsageAggregate>> GetDailyAsync(UsageQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<UsageRecord>> GetRecentAsync(int take = 200, UsageQuery? query = null, CancellationToken ct = default);

    Task<DateTimeOffset?> GetCursorAsync(ProviderKind provider, Guid? credentialId, CancellationToken ct = default);
    Task SetCursorAsync(ProviderKind provider, Guid? credentialId, DateTimeOffset cursor, CancellationToken ct = default);

    Task<int> DeleteAsync(UsageQuery query, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
}

public interface IPricingStore
{
    Task UpsertAsync(IEnumerable<ModelPricing> items, CancellationToken ct = default);
    Task<ModelPricing?> FindAsync(string provider, string model, CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> SearchAsync(string? text, string? provider, int limit = 500, CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Store a user-supplied price. Manual rows are protected from sync overwrite.</summary>
    Task SetManualAsync(ModelPricing pricing, CancellationToken ct = default);
    Task DeleteAsync(string provider, string model, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public interface IBudgetStore
{
    Task<IReadOnlyList<BudgetRule>> GetRulesAsync(CancellationToken ct = default);
    Task SaveRuleAsync(BudgetRule rule, CancellationToken ct = default);
    Task DeleteRuleAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<AlertRecord>> GetAlertsAsync(int take = 100, CancellationToken ct = default);
    Task AddAlertAsync(AlertRecord alert, CancellationToken ct = default);
    Task AcknowledgeAsync(long alertId, CancellationToken ct = default);
    Task ClearAlertsAsync(CancellationToken ct = default);
}

public interface IExchangeRateStore
{
    Task<ExchangeRateInfo> GetAsync(CancellationToken ct = default);
    Task SaveAsync(ExchangeRateInfo info, CancellationToken ct = default);
}
