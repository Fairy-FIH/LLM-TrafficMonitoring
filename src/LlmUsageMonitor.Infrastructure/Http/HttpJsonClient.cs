using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LlmUsageMonitor.Infrastructure.Http;

public sealed class HttpJsonResult<T>
{
    public T? Data { get; init; }
    public bool NotModified { get; init; }
    public string? ETag { get; init; }
    public HttpStatusCode Status { get; init; }
}

/// <summary>Small JSON GET/POST helper with manual retry + exponential backoff.</summary>
public sealed class HttpJsonClient
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly int _maxAttempts;

    public HttpJsonClient(int maxAttempts = 3) => _maxAttempts = Math.Max(1, maxAttempts);

    public async Task<HttpJsonResult<T>> GetAsync<T>(HttpClient client, string url,
        IDictionary<string, string>? headers = null, string? etag = null, CancellationToken ct = default)
    {
        return await SendAsync<T>(client, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, headers);
            if (!string.IsNullOrEmpty(etag) && etag.StartsWith("\"", StringComparison.Ordinal))
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
            else if (!string.IsNullOrEmpty(etag))
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            return request;
        }, ct);
    }

    public async Task<HttpJsonResult<T>> PostAsync<T>(HttpClient client, string url, HttpContent content,
        IDictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        return await SendAsync<T>(client, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            ApplyHeaders(request, headers);
            return request;
        }, ct);
    }

    private async Task<HttpJsonResult<T>> SendAsync<T>(HttpClient client,
        Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                using var request = factory();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode == HttpStatusCode.NotModified)
                    return new HttpJsonResult<T> { NotModified = true, ETag = response.Headers.ETag?.Tag, Status = response.StatusCode };

                if ((int)response.StatusCode is >= 500 or 429)
                {
                    last = new HttpRequestException($"HTTP {(int)response.StatusCode} from {request.RequestUri}");
                    await DelayAsync(attempt, response, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadAsync(response, ct);
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} from {request.RequestUri}: {Truncate(body)}");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var data = await JsonSerializer.DeserializeAsync<T>(stream, Options, ct);
                return new HttpJsonResult<T>
                {
                    Data = data,
                    ETag = response.Headers.ETag?.Tag,
                    Status = response.StatusCode
                };
            }
            catch (Exception ex) when (attempt < _maxAttempts && ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                await DelayAsync(attempt, null, ct);
            }
        }

        throw last ?? new HttpRequestException("Request failed");
    }

    private static void ApplyHeaders(HttpRequestMessage request, IDictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (key, value) in headers)
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (!request.Headers.TryAddWithoutValidation(key, value))
                request.Content?.Headers.TryAddWithoutValidation(key, value);
        }
    }

    private static async Task DelayAsync(int attempt, HttpResponseMessage? response, CancellationToken ct)
    {
        var seconds = Math.Pow(2, attempt - 1);
        if (response?.Headers.RetryAfter?.Delta is { } delta)
            seconds = Math.Max(seconds, delta.TotalSeconds);
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(seconds, 30)), ct);
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
