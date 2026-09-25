using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.WebCapture;

/// <summary>A JSON response observed while the user browses a provider console.</summary>
public sealed class CapturedResponse
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string Url { get; init; } = string.Empty;
    public string Method { get; init; } = "GET";
    public int StatusCode { get; init; }
    public string Body { get; init; } = string.Empty;
    public bool LooksLikeUsage { get; init; }
    public int Score { get; init; }
    public int ExtractedCount { get; init; }
    public string Summary { get; init; } = string.Empty;

    public string TimeText => Timestamp.ToString("HH:mm:ss");
    public string StatusText => $"{StatusCode} {Method}";
    public string ShortUrl
    {
        get
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
                return uri.Host + uri.AbsolutePath;
            return Url;
        }
    }
}

/// <summary>Result of scanning one captured JSON body for billable usage.</summary>
public sealed record ExtractionResult(
    bool LooksLikeUsage,
    int Score,
    string Summary,
    IReadOnlyList<UsageRecord> Records);
