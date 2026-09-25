using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Infrastructure.Services;

/// <summary>
/// Pulls billable usage from official platform APIs using per-credential cursors
/// for incremental fetches. Overlap is de-duplicated by <see cref="UsageRecord.DedupKey"/>.
/// </summary>
public sealed class UsageSyncService : IUsageSyncService
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    private readonly ICredentialStore _credentials;
    private readonly IUsageStore _usage;
    private readonly IProviderRegistry _registry;
    private readonly IPricingService _pricing;
    private readonly ICostCalculator _cost;
    private readonly IExchangeRateService _exchange;

    public UsageSyncService(ICredentialStore credentials, IUsageStore usage, IProviderRegistry registry,
        IPricingService pricing, ICostCalculator cost, IExchangeRateService exchange)
    {
        _credentials = credentials;
        _usage = usage;
        _registry = registry;
        _pricing = pricing;
        _cost = cost;
        _exchange = exchange;
    }

    public async Task<SyncResult> SyncAllAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default)
    {
        var credentials = (await _credentials.GetCredentialsAsync(ct))
            .Where(IsSyncable)
            .ToList();

        var result = new SyncResult();
        for (var i = 0; i < credentials.Count; i++)
        {
            var credential = credentials[i];
            progress?.Report(new SyncProgress
            {
                Completed = i,
                Total = credentials.Count,
                Message = $"同步 {credential.Name}…"
            });

            try
            {
                var single = await SyncCredentialAsync(credential, ct);
                result.CredentialsProcessed++;
                result.RecordsAdded += single.RecordsAdded;
                result.Messages.AddRange(single.Messages);
            }
            catch (Exception ex)
            {
                result.Failures++;
                result.Messages.Add($"{credential.Name}: {ex.Message}");
            }
        }

        progress?.Report(new SyncProgress
        {
            Completed = credentials.Count,
            Total = credentials.Count,
            Message = "同步完成"
        });
        return result;
    }

    public async Task<SyncResult> SyncCredentialAsync(ApiCredential credential, CancellationToken ct = default)
    {
        var result = new SyncResult { CredentialsProcessed = 1 };
        if (!IsSyncable(credential)) return result;

        var adapter = _registry.GetAdapter(credential.Provider);
        if (!adapter.Descriptor.SupportsOfficialUsage)
        {
            result.Messages.Add($"{credential.Name}: 该平台无官方用量接口，请使用本地代理。");
            return result;
        }

        var now = DateTimeOffset.Now;
        var cursor = await _usage.GetCursorAsync(credential.Provider, credential.Id, ct);
        var from = cursor ?? now - DefaultWindow;
        // Overlap a little to catch late-arriving records; dedupe removes repeats.
        if (cursor is not null) from -= TimeSpan.FromHours(1);
        if (from > now) from = now - TimeSpan.FromDays(1);

        var records = await adapter.FetchUsageAsync(credential, from, now, ct);
        if (records.Count == 0)
        {
            result.Messages.Add($"{credential.Name}: 无新增记录。");
            return result;
        }

        var rate = await _exchange.GetUsdToCnyAsync(ct: ct);
        foreach (var record in records)
        {
            record.CredentialId ??= credential.Id;
            record.GroupId ??= credential.GroupId;
            record.DedupKey ??= BuildDedupKey(credential, record);

            if (record.CostUsd == 0)
            {
                var pricing = await _pricing.ResolveAsync(record.Provider, record.Model, ct);
                record.CostUsd = _cost.ComputeUsd(new TokenUsage
                {
                    InputTokens = record.InputTokens,
                    OutputTokens = record.OutputTokens,
                    CachedTokens = record.CachedTokens
                }, pricing, rate);
            }
        }

        result.RecordsAdded = await _usage.AddRangeAsync(records, ct);
        await _usage.SetCursorAsync(credential.Provider, credential.Id, now, ct);
        result.Messages.Add($"{credential.Name}: 新增 {result.RecordsAdded} 条。");
        return result;
    }

    private static bool IsSyncable(ApiCredential credential) =>
        credential.Enabled &&
        credential.CollectionMode is CollectionMode.OfficialApi or CollectionMode.Both;

    private static string BuildDedupKey(ApiCredential credential, UsageRecord record)
        => $"{record.Provider}|{credential.Id}|{record.Model}|{record.Timestamp:yyyyMMddHHmmss}";
}
