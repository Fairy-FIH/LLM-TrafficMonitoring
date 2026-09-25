using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Providers.Adapters;

/// <summary>OpenRouter: credits balance plus the daily activity report.</summary>
public sealed class OpenRouterAdapter : OpenAiCompatibleAdapter
{
    public OpenRouterAdapter(HttpProvider http, HttpJsonClient json) : base(ProviderKind.OpenRouter, http, json) { }

    public override async Task ValidateAsync(ApiCredential credential, CancellationToken ct = default)
    {
        using var client = Http.Create(credential, TimeSpan.FromSeconds(20));
        await Json.GetAsync<JsonElement>(client, $"{ResolveBaseUrl(credential)}/key",
            BuildAuthHeaders(credential), ct: ct);
    }

    public override async Task<ProviderBalance?> GetBalanceAsync(ApiCredential credential, CancellationToken ct = default)
    {
        try
        {
            using var client = Http.Create(credential, TimeSpan.FromSeconds(20));
            var result = await Json.GetAsync<JsonElement>(client,
                $"{ResolveBaseUrl(credential)}/credits", BuildAuthHeaders(credential), ct: ct);
            if (result.Data.ValueKind != JsonValueKind.Object ||
                !result.Data.TryGetProperty("data", out var data)) return null;

            var total = ReadDecimal(data, "total_credits");
            var used = ReadDecimal(data, "total_usage");
            if (total is null) return null;
            return new ProviderBalance
            {
                Amount = total.Value - (used ?? 0),
                Currency = Currency.USD,
                Raw = "credits"
            };
        }
        catch
        {
            return null;
        }
    }

    public override async Task<IReadOnlyList<UsageRecord>> FetchUsageAsync(ApiCredential credential,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var client = Http.Create(credential, TimeSpan.FromSeconds(60));
        var headers = BuildAuthHeaders(credential);
        var records = new List<UsageRecord>();
        var baseUrl = ResolveBaseUrl(credential);

        var start = from.Date;
        var end = to.Date;
        if ((end - start).TotalDays > 31) start = end.AddDays(-31);

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var date = day.ToString("yyyy-MM-dd");
            JsonElement root;
            try
            {
                var result = await Json.GetAsync<JsonElement>(client, $"{baseUrl}/activity?date={date}", headers, ct: ct);
                root = result.Data;
            }
            catch
            {
                continue;
            }

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in data.EnumerateArray())
            {
                var model = item.TryGetProperty("model", out var m) ? m.GetString() : null;
                if (model is null) continue;

                var usage = ReadDecimal(item, "usage") ?? 0m;
                var requests = ReadLong(item, "requests");
                var prompt = ReadLong(item, "prompt_tokens");
                var completion = ReadLong(item, "completion_tokens");

                records.Add(new UsageRecord
                {
                    Provider = ProviderKind.OpenRouter,
                    Model = model,
                    Timestamp = new DateTimeOffset(day, TimeSpan.Zero),
                    InputTokens = prompt,
                    OutputTokens = completion,
                    Requests = Math.Max(1, requests),
                    CostUsd = usage,
                    Source = UsageSource.OfficialApi,
                    DedupKey = $"openrouter|{model}|{date}"
                });
            }
        }
        return records;
    }
}
