using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Providers.Adapters;

/// <summary>Anthropic Claude with the Admin usage report API (requires an admin key).</summary>
public sealed class AnthropicAdapter : OpenAiCompatibleAdapter
{
    public AnthropicAdapter(HttpProvider http, HttpJsonClient json) : base(ProviderKind.Anthropic, http, json) { }

    public override async Task<IReadOnlyList<UsageRecord>> FetchUsageAsync(ApiCredential credential,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(credential.AdminSecret) ? credential.Secret : credential.AdminSecret!;
        using var client = Http.Create(credential, TimeSpan.FromSeconds(60));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-api-key"] = key,
            ["anthropic-version"] = "2023-06-01"
        };

        var url = $"{ResolveBaseUrl(credential)}/organizations/usage_report/messages" +
                  $"?starting_at={Uri.EscapeDataString(from.UtcDateTime.ToString("o"))}" +
                  $"&ending_at={Uri.EscapeDataString(to.UtcDateTime.ToString("o"))}" +
                  $"&bucket_width=1d&limit=31&{Uri.EscapeDataString("group_by[]")}=model";

        var result = await Json.GetAsync<JsonElement>(client, url, headers, ct: ct);
        var records = new List<UsageRecord>();
        if (result.Data.ValueKind != JsonValueKind.Object ||
            !result.Data.TryGetProperty("data", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
            return records;

        foreach (var bucket in buckets.EnumerateArray())
        {
            var start = bucket.TryGetProperty("starting_at", out var sa) && sa.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(sa.GetString(), out var parsed)
                ? parsed
                : from;
            if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in results.EnumerateArray())
            {
                var model = item.TryGetProperty("model", out var m) ? m.GetString() : null;
                var input = ReadLong(item, "input_tokens") + ReadLong(item, "cache_creation_input_tokens");
                var output = ReadLong(item, "output_tokens");
                var cached = ReadLong(item, "cache_read_input_tokens");
                if (input == 0 && output == 0) continue;

                records.Add(new UsageRecord
                {
                    Provider = ProviderKind.Anthropic,
                    Model = model ?? "unknown",
                    Timestamp = start,
                    InputTokens = input,
                    OutputTokens = output,
                    CachedTokens = cached,
                    Source = UsageSource.OfficialApi,
                    DedupKey = $"anthropic|{model}|{start.ToUnixTimeSeconds()}"
                });
            }
        }
        return records;
    }
}
