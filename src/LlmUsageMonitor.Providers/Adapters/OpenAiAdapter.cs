using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Providers.Adapters;

/// <summary>OpenAI platform with the organization usage API (requires an admin key).</summary>
public sealed class OpenAiAdapter : OpenAiCompatibleAdapter
{
    public OpenAiAdapter(HttpProvider http, HttpJsonClient json) : base(ProviderKind.OpenAI, http, json) { }

    public override async Task<IReadOnlyList<UsageRecord>> FetchUsageAsync(ApiCredential credential,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(credential.AdminSecret) ? credential.Secret : credential.AdminSecret!;
        using var client = Http.Create(credential, TimeSpan.FromSeconds(60));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer " + key
        };

        var start = from.ToUnixTimeSeconds();
        var end = to.ToUnixTimeSeconds();
        var url = $"{ResolveBaseUrl(credential)}/organization/usage/completions" +
                  $"?start_time={start}&end_time={end}&bucket_width=1d&limit=180&" +
                  $"{Uri.EscapeDataString("group_by[]")}=model";

        if (!string.IsNullOrWhiteSpace(credential.OrganizationId))
            headers["OpenAI-Organization"] = credential.OrganizationId!;

        var result = await Json.GetAsync<JsonElement>(client, url, headers, ct: ct);
        var records = new List<UsageRecord>();
        if (result.Data.ValueKind != JsonValueKind.Object ||
            !result.Data.TryGetProperty("data", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
            return records;

        foreach (var bucket in buckets.EnumerateArray())
        {
            var bucketStart = bucket.TryGetProperty("start_time", out var st) && st.TryGetInt64(out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : from;
            if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in results.EnumerateArray())
            {
                var model = item.TryGetProperty("model", out var m) ? m.GetString() : null;
                var input = ReadLong(item, "input_tokens");
                var output = ReadLong(item, "output_tokens");
                var requests = ReadLong(item, "num_model_requests");
                if (input == 0 && output == 0 && requests == 0) continue;

                records.Add(new UsageRecord
                {
                    Provider = ProviderKind.OpenAI,
                    Model = model ?? "unknown",
                    Timestamp = bucketStart,
                    InputTokens = input,
                    OutputTokens = output,
                    Requests = Math.Max(1, requests),
                    Source = UsageSource.OfficialApi,
                    DedupKey = $"openai|{model}|{bucketStart.ToUnixTimeSeconds()}"
                });
            }
        }
        return records;
    }
}
