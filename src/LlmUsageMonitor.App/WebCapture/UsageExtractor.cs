using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.WebCapture;

/// <summary>
/// Heuristically scans an arbitrary JSON payload for token / cost / request fields
/// and turns matches into importable <see cref="UsageRecord"/>s. Designed to be
/// resilient to unknown provider response shapes.
/// </summary>
public static class UsageExtractor
{
    private static readonly string[] TokenKeys =
    {
        "prompt_tokens", "completion_tokens", "input_tokens", "output_tokens",
        "cached_tokens", "cache_read_input_tokens", "cache_creation_input_tokens",
        "total_tokens", "tokens"
    };

    // Only explicit token fields are used; generic keys like "input"/"output" caused
    // false positives (e.g. a numeric id or count being read as tokens).
    private static readonly string[] InputKeys =
    {
        "prompt_tokens", "input_tokens", "input_token_count", "prompt_token_count", "input_token",
        "prompt_cache_miss_tokens"
    };
    private static readonly string[] OutputKeys =
    {
        "completion_tokens", "output_tokens", "output_token_count", "completion_token_count",
        "completion_token", "output_token", "generated_tokens"
    };
    private static readonly string[] CachedKeys =
    {
        "cached_tokens", "cache_read_input_tokens", "prompt_cache_hit_tokens",
        "cache_hit_tokens", "cached_input_tokens", "cache_read_tokens"
    };
    private static readonly string[] TotalKeys = { "total_tokens", "total_token_count", "tokens" };
    private static readonly string[] ModelKeys = { "model", "model_name", "model_id", "modelname" };
    private static readonly string[] TimeKeys =
    {
        "timestamp", "time", "date", "datetime", "created_at", "created", "start_time",
        "starting_at", "day", "period", "bucket"
    };
    private static readonly string[] RequestKeys = { "requests", "request_count", "count", "calls", "num_requests", "times" };
    private static readonly string[] CostKeys = { "cost", "amount", "total_cost", "total_amount", "spend", "fee", "price", "total_price", "consumed" };

    public static ExtractionResult Extract(string url, string body, ProviderKind provider, decimal usdToCny)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new ExtractionResult(false, 0, "空响应", Array.Empty<UsageRecord>());

        var trimmed = body.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            return new ExtractionResult(false, 0, "非 JSON", Array.Empty<UsageRecord>());

        try
        {
            using var doc = JsonDocument.Parse(body);
            var accumulator = new Accumulator();
            Walk(doc.RootElement, provider, usdToCny, accumulator);

            var looks = accumulator.Score >= 3 && accumulator.Records.Count > 0;
            var summary = accumulator.Records.Count > 0
                ? $"识别到 {accumulator.Records.Count} 条用量（评分 {accumulator.Score}）"
                : "已捕获（未识别到用量）";

            return new ExtractionResult(looks, accumulator.Score, summary, accumulator.Records);
        }
        catch
        {
            return new ExtractionResult(false, 0, "JSON 解析失败", Array.Empty<UsageRecord>());
        }
    }

    private sealed class Accumulator
    {
        public int Score;
        public List<UsageRecord> Records { get; } = new();
    }

    private static void Walk(JsonElement element, ProviderKind provider, decimal usdToCny, Accumulator acc)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                    map[property.Name] = property.Value;

                var hasToken = TokenKeys.Any(map.ContainsKey);
                var hasCost = CostKeys.Any(map.ContainsKey);

                if (hasToken || hasCost)
                {
                    var record = BuildRecord(map, provider, usdToCny, acc);
                    if (record is not null)
                    {
                        acc.Score += hasToken ? 3 : 2;
                        acc.Records.Add(record);
                    }
                }

                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, provider, usdToCny, acc);
                break;
            }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, provider, usdToCny, acc);
                break;
        }
    }

    private static UsageRecord? BuildRecord(Dictionary<string, JsonElement> map, ProviderKind provider,
        decimal usdToCny, Accumulator acc)
    {
        long ReadLong(string[] keys)
        {
            foreach (var key in keys)
            {
                if (map.TryGetValue(key, out var value) && TryLong(value, out var l)) return l;
            }
            return 0;
        }

        var input = ReadLong(InputKeys);
        var output = ReadLong(OutputKeys);
        var cached = ReadLong(CachedKeys);
        if (input == 0 && output == 0)
        {
            var total = ReadLong(TotalKeys);
            if (total > 0) output = total;   // keep the total visible when only a sum exists
        }

        decimal costUsd = 0;
        var hasCost = false;
        foreach (var key in CostKeys)
        {
            if (map.TryGetValue(key, out var value) && TryDecimal(value, out var amount))
            {
                var currency = DetectCurrency(map, key);
                costUsd = currency == Currency.CNY && usdToCny > 0 ? amount / usdToCny : amount;
                hasCost = true;
                break;
            }
        }

        if (input == 0 && output == 0 && !hasCost) return null;

        var model = ModelKeys.Select(k => map.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "web-import";

        var timestamp = ReadTimestamp(map);
        var requests = ReadLong(RequestKeys);
        if (requests <= 0) requests = 1;
        // Token rows usually represent an aggregate of many calls; a pure cost row is one data point.
        if (input == 0 && output == 0 && hasCost) requests = Math.Max(1, requests);

        var hash = ShortHash($"{provider}|{model}|{timestamp:o}|{input}|{output}|{cached}|{costUsd}|{requests}");

        return new UsageRecord
        {
            Provider = provider,
            Model = model!,
            Timestamp = timestamp,
            InputTokens = input,
            OutputTokens = output,
            CachedTokens = cached,
            Requests = requests,
            CostUsd = costUsd,
            Source = UsageSource.Import,
            DedupKey = "web|" + hash
        };
    }

    private static Currency DetectCurrency(Dictionary<string, JsonElement> map, string costKey)
    {
        foreach (var candidate in new[] { "currency", "unit", "money_type", "type" })
        {
            if (map.TryGetValue(candidate, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? string.Empty;
                if (text.Contains("CNY", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("RMB", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("¥", StringComparison.Ordinal))
                    return Currency.CNY;
                if (text.Contains("USD", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("$", StringComparison.Ordinal))
                    return Currency.USD;
            }
        }

        if (costKey.Contains("cny", StringComparison.OrdinalIgnoreCase) ||
            costKey.Contains("rmb", StringComparison.OrdinalIgnoreCase))
            return Currency.CNY;

        // Default to USD for international platforms; Chinese platforms usually state currency.
        return Currency.USD;
    }

    private static DateTimeOffset ReadTimestamp(Dictionary<string, JsonElement> map)
    {
        foreach (var key in TimeKeys)
        {
            if (!map.TryGetValue(key, out var value)) continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                if (number > 1_000_000_000_000) return DateTimeOffset.FromUnixTimeMilliseconds(number).ToLocalTime();
                if (number > 1_000_000_000) return DateTimeOffset.FromUnixTimeSeconds(number).ToLocalTime();
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (text.All(char.IsDigit) && long.TryParse(text, out var epoch))
                    {
                        if (epoch > 1_000_000_000_000) return DateTimeOffset.FromUnixTimeMilliseconds(epoch).ToLocalTime();
                        if (epoch > 1_000_000_000) return DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime();
                    }
                    if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeLocal, out var parsed))
                        return parsed;
                }
            }
        }
        return DateTimeOffset.Now;
    }

    private static bool TryLong(JsonElement value, out long result)
    {
        result = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetInt64(out result)) return true;
                if (value.TryGetDouble(out var d)) { result = (long)d; return true; }
                return false;
            case JsonValueKind.String:
                return long.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            default:
                return false;
        }
    }

    private static bool TryDecimal(JsonElement value, out decimal result)
    {
        result = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDecimal(out result);
            case JsonValueKind.String:
                return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            default:
                return false;
        }
    }

    private static string ShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 10);
    }
}
