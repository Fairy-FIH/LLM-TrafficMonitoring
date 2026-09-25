using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Providers.Adapters;

/// <summary>An OpenAI-compatible platform that only exposes a balance endpoint.</summary>
public sealed class BalanceOnlyAdapter : OpenAiCompatibleAdapter
{
    private readonly string _balancePath;
    private readonly Currency _currency;
    private readonly Func<JsonElement, decimal?> _parser;

    public BalanceOnlyAdapter(ProviderKind kind, HttpProvider http, HttpJsonClient json,
        string balancePath, Currency currency, Func<JsonElement, decimal?> parser)
        : base(kind, http, json)
    {
        _balancePath = balancePath;
        _currency = currency;
        _parser = parser;
    }

    public override async Task<ProviderBalance?> GetBalanceAsync(ApiCredential credential, CancellationToken ct = default)
    {
        try
        {
            using var client = Http.Create(credential, TimeSpan.FromSeconds(20));
            var root = ResolveBaseUrl(credential);
            // Balance endpoints usually sit above the /v1 chat path.
            if (_balancePath.StartsWith('/') && root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                root = root[..^3];

            var url = root.TrimEnd('/') + _balancePath;
            var result = await Json.GetAsync<JsonElement>(client, url, BuildAuthHeaders(credential), ct: ct);
            var amount = _parser(result.Data);
            return amount is null ? null : new ProviderBalance { Amount = amount.Value, Currency = _currency };
        }
        catch
        {
            return null;
        }
    }

    // Common parsers for domestic platforms.
    public static decimal? DeepSeekParser(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array)
            return null;
        decimal total = 0;
        var found = false;
        foreach (var info in infos.EnumerateArray())
        {
            if (info.TryGetProperty("total_balance", out var balance) &&
                decimal.TryParse(balance.GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
            {
                total += d;
                found = true;
            }
        }
        return found ? total : null;
    }

    public static decimal? MoonshotParser(JsonElement root)
        => ReadNested(root, "data", "available_balance");

    public static decimal? SiliconFlowParser(JsonElement root)
        => ReadNested(root, "data", "balance") ?? ReadNested(root, "data", "totalBalance");

    private static decimal? ReadNested(JsonElement root, string parent, string child)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(parent, out var p) || p.ValueKind != JsonValueKind.Object ||
            !p.TryGetProperty(child, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(value.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null
        };
    }
}
