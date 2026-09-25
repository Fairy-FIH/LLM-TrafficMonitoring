using System.Net.Http.Headers;
using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Providers.Adapters;

/// <summary>
/// Shared behaviour for OpenAI-compatible platforms: validation, model listing
/// and a best-effort balance lookup. Official usage APIs are added by subclasses.
/// </summary>
public class OpenAiCompatibleAdapter : IProviderAdapter
{
    private readonly ProviderKind _kind;

    public OpenAiCompatibleAdapter(ProviderKind kind, HttpProvider http, HttpJsonClient json)
    {
        _kind = kind;
        Http = http;
        Json = json;
    }

    protected HttpProvider Http { get; }
    protected HttpJsonClient Json { get; }

    public virtual ProviderKind Kind => _kind;
    public virtual ProviderDescriptor Descriptor => ProviderCatalog.Get(_kind);

    protected virtual string ResolveBaseUrl(ApiCredential credential)
        => string.IsNullOrWhiteSpace(credential.BaseUrl)
            ? Descriptor.DefaultBaseUrl
            : credential.BaseUrl!.TrimEnd('/');

    protected virtual Dictionary<string, string> BuildAuthHeaders(ApiCredential credential)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (credential.Provider)
        {
            case ProviderKind.Anthropic:
                headers["x-api-key"] = credential.Secret;
                headers["anthropic-version"] = "2023-06-01";
                break;
            case ProviderKind.AzureOpenAI:
                headers["api-key"] = credential.Secret;
                break;
            default:
                headers["Authorization"] = "Bearer " + credential.Secret;
                break;
        }
        return headers;
    }

    public virtual async Task ValidateAsync(ApiCredential credential, CancellationToken ct = default)
    {
        using var client = Http.Create(credential, TimeSpan.FromSeconds(20));
        var url = ResolveBaseUrl(credential) + "/models";
        var headers = BuildAuthHeaders(credential);
        // Throws on non-success.
        await Json.GetAsync<JsonElement>(client, url, headers, ct: ct);
    }

    public virtual async Task<IReadOnlyList<string>> ListModelsAsync(ApiCredential credential, CancellationToken ct = default)
    {
        using var client = Http.Create(credential, TimeSpan.FromSeconds(30));
        var url = ResolveBaseUrl(credential) + "/models";
        var headers = BuildAuthHeaders(credential);
        var result = await Json.GetAsync<JsonElement>(client, url, headers, ct: ct);

        var models = new List<string>();
        if (result.Data.ValueKind == JsonValueKind.Object &&
            result.Data.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value)
                    models.Add(value);
            }
        }
        return models;
    }

    public virtual Task<IReadOnlyList<UsageRecord>> FetchUsageAsync(ApiCredential credential,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsageRecord>>(Array.Empty<UsageRecord>());

    public virtual async Task<ProviderBalance?> GetBalanceAsync(ApiCredential credential, CancellationToken ct = default)
    {
        try
        {
            using var client = Http.Create(credential, TimeSpan.FromSeconds(20));
            var headers = BuildAuthHeaders(credential);
            var baseUrl = ResolveBaseUrl(credential);
            var start = DateTimeOffset.Now.AddDays(-30).ToString("yyyy-MM-dd");
            var end = DateTimeOffset.Now.AddDays(1).ToString("yyyy-MM-dd");

            var subscription = await Json.GetAsync<JsonElement>(client,
                $"{baseUrl}/dashboard/billing/subscription", headers, ct: ct);
            var hardLimit = ReadDecimal(subscription.Data, "hard_limit_usd");

            var usage = await Json.GetAsync<JsonElement>(client,
                $"{baseUrl}/dashboard/billing/usage?start_date={start}&end_date={end}", headers, ct: ct);
            var usedCents = ReadDecimal(usage.Data, "total_usage");

            if (hardLimit is null) return null;
            var remaining = hardLimit.Value - (usedCents ?? 0) / 100m;
            return new ProviderBalance { Amount = remaining, Currency = Currency.USD, Raw = "OpenAI 计费接口" };
        }
        catch
        {
            return null;
        }
    }

    protected static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(value.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null
        };
    }

    protected static long ReadLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l : 0,
            JsonValueKind.String => long.TryParse(value.GetString(), out var l) ? l : 0,
            _ => 0
        };
    }
}
