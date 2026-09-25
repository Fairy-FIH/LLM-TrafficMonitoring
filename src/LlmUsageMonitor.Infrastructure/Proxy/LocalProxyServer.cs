using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;
using LlmUsageMonitor.Infrastructure.Http;

namespace LlmUsageMonitor.Infrastructure.Proxy;

/// <summary>
/// A tiny OpenAI/Anthropic-compatible reverse proxy. Point any client's base URL
/// at http://127.0.0.1:{port}/v1 and the app forwards each request to the chosen
/// platform while recording exact request/token usage.
/// </summary>
public sealed class LocalProxyServer : ILocalProxyServer
{
    private const int MaxCaptureBytes = 8 * 1024 * 1024;

    private readonly ISettingsStore _settings;
    private readonly ICredentialStore _credentials;
    private readonly IUsageStore _usage;
    private readonly IPricingService _pricing;
    private readonly ICostCalculator _cost;
    private readonly IExchangeRateService _exchange;
    private readonly HttpProvider _http;
    private readonly HttpJsonClient _json = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public LocalProxyServer(ISettingsStore settings, ICredentialStore credentials, IUsageStore usage,
        IPricingService pricing, ICostCalculator cost, IExchangeRateService exchange, HttpProvider http)
    {
        _settings = settings;
        _credentials = credentials;
        _usage = usage;
        _pricing = pricing;
        _cost = cost;
        _exchange = exchange;
        _http = http;
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public event EventHandler<UsageRecord>? UsageCaptured;
    public event EventHandler<string>? Log;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        var config = _settings.Current.LocalProxy;
        Port = config.Port;
        var address = IPAddress.TryParse(config.ListenAddress, out var ip) ? ip : IPAddress.Loopback;
        _listener = new TcpListener(address, Port);
        _listener.Start();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRunning = true;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token), CancellationToken.None);
        Log?.Invoke(this, $"本地代理已启动: http://{address}:{Port}/v1");
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* ignore */ }
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* ignore */ }
        }
        _cts?.Dispose();
        _cts = null;
        Log?.Invoke(this, "本地代理已停止");
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, ct);
                if (request is null) return;

                if (request.Method == "GET" && (request.Path == "/" || request.Path == "/health"))
                {
                    await WriteSimpleAsync(stream, 200, "{\"status\":\"ok\",\"service\":\"LlmUsageMonitor\"}", ct);
                    return;
                }

                var config = _settings.Current.LocalProxy;
                if (!string.IsNullOrEmpty(config.AccessToken) && !IsAuthorized(request, config.AccessToken))
                {
                    await WriteSimpleAsync(stream, 401, "{\"error\":\"unauthorized\"}", ct);
                    return;
                }

                var credential = await ResolveCredentialAsync(request, config, ct);
                if (credential is null && string.IsNullOrWhiteSpace(config.DefaultBaseUrl))
                {
                    await WriteSimpleAsync(stream, 400,
                        "{\"error\":\"no upstream configured; set a default provider/base url or send X-Llm-Provider\"}", ct);
                    return;
                }

                await ForwardAsync(stream, request, credential, config, ct);
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"代理错误: {ex.Message}");
                try
                {
                    await using var stream = client.GetStream();
                    await WriteSimpleAsync(stream, 502, $"{{\"error\":\"{Escape(ex.Message)}\"}}", ct);
                }
                catch { /* ignore */ }
            }
        }
    }

    private async Task ForwardAsync(NetworkStream clientStream, ProxyRequest request,
        ApiCredential? credential, LocalProxySettings config, CancellationToken ct)
    {
        var provider = credential?.Provider ?? config.DefaultProvider;
        var baseUrl = credential is null
            ? config.DefaultBaseUrl!
            : (string.IsNullOrWhiteSpace(credential.BaseUrl)
                ? ProviderCatalog.Get(credential.Provider).DefaultBaseUrl
                : credential.BaseUrl!);
        var targetUri = BuildTargetUri(baseUrl, request.Path, request.Query);

        var clientHasAuth = request.Headers.ContainsKey("authorization") ||
                            request.Headers.ContainsKey("x-api-key");
        var passThrough = config.PassThroughAuth && clientHasAuth;

        using var http = _http.Create(credential, TimeSpan.FromMinutes(10));
        using var upstream = new HttpRequestMessage(new HttpMethod(request.Method), targetUri)
        {
            Content = request.Body.Length > 0 ? new ByteArrayContent(request.Body) : null
        };

        CopyContentHeaders(request, upstream, preserveAuth: passThrough);
        if (!passThrough && credential is not null) ApplyUpstreamAuth(upstream, credential);

        using var response = await http.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var upstreamStream = await response.Content.ReadAsStreamAsync(ct);

        // Write status line + headers to the client.
        var head = new StringBuilder();
        head.Append($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n");
        foreach (var header in response.Headers)
        {
            if (IsHopByHop(header.Key)) continue;
            head.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
        }
        foreach (var header in response.Content.Headers)
        {
            if (IsHopByHop(header.Key)) continue;
            head.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
        }
        head.Append("Connection: close\r\n\r\n");
        var headBytes = Encoding.ASCII.GetBytes(head.ToString());
        await clientStream.WriteAsync(headBytes, ct);

        // Stream body through while capturing it for usage parsing.
        using var capture = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await upstreamStream.ReadAsync(buffer, ct)) > 0)
        {
            await clientStream.WriteAsync(buffer.AsMemory(0, read), ct);
            if (capture.Length < MaxCaptureBytes) capture.Write(buffer, 0, read);
        }
        await clientStream.FlushAsync(ct);

        var responseContentType = response.Content.Headers.ContentType?.MediaType;
        await RecordUsageAsync(request, provider, credential, responseContentType, capture.ToArray(), ct);
    }

    private async Task RecordUsageAsync(ProxyRequest request, ProviderKind provider, ApiCredential? credential,
        string? responseContentType, byte[] body, CancellationToken ct)
    {
        if (body.Length == 0) return;

        var usage = ExtractUsage(body, responseContentType);
        if (usage is null) return;

        var model = usage.Model ?? request.ModelHint ?? "unknown";
        var rate = await _exchange.GetUsdToCnyAsync(ct: ct);
        var pricing = await _pricing.ResolveAsync(provider, model, ct);

        var record = new UsageRecord
        {
            Provider = provider,
            CredentialId = credential?.Id,
            GroupId = credential?.GroupId,
            Model = model,
            Timestamp = DateTimeOffset.Now,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CachedTokens = usage.CachedTokens,
            Requests = 1,
            Source = UsageSource.LocalProxy,
            DedupKey = $"proxy|{Guid.NewGuid():N}",
            CostUsd = _cost.ComputeUsd(usage, pricing, rate)
        };

        await _usage.AddRangeAsync(new[] { record }, ct);
        UsageCaptured?.Invoke(this, record);
    }

    private static TokenUsage? ExtractUsage(byte[] body, string? contentType)
    {
        var text = Encoding.UTF8.GetString(body);

        if (contentType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
        {
            TokenUsage? last = null;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = trimmed[5..].Trim();
                if (payload.Length == 0 || payload == "[DONE]") continue;
                var parsed = ParseUsageJson(payload);
                if (parsed is not null) last = parsed;
            }
            return last;
        }

        return ParseUsageJson(text);
    }

    private static TokenUsage? ParseUsageJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;

            // Anthropic style: usage may be top-level or inside message.
            if (TryReadUsage(root, model, out var anthropic))
                return anthropic;
            if (root.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.Object &&
                TryReadUsage(message, model, out var nested))
                return nested;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadUsage(JsonElement element, string? model, out TokenUsage usage)
    {
        usage = new TokenUsage();
        if (!element.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return false;

        var input = ReadLong(u, "prompt_tokens") ?? ReadLong(u, "input_tokens") ?? 0;
        var output = ReadLong(u, "completion_tokens") ?? ReadLong(u, "output_tokens") ?? 0;
        var cached = ReadLong(u, "cached_tokens")
                     ?? (u.TryGetProperty("prompt_tokens_details", out var d) ? ReadLong(d, "cached_tokens") : null)
                     ?? (u.TryGetProperty("cache_read_input_tokens", out _) ? ReadLong(u, "cache_read_input_tokens") : null)
                     ?? 0;

        if (input == 0 && output == 0) return false;

        usage = new TokenUsage
        {
            InputTokens = input,
            OutputTokens = output,
            CachedTokens = cached,
            Model = model
        };
        return true;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l : (long)value.GetDouble(),
            JsonValueKind.String => long.TryParse(value.GetString(), out var l) ? l : null,
            _ => null
        };
    }

    private async Task<ApiCredential?> ResolveCredentialAsync(ProxyRequest request,
        LocalProxySettings config, CancellationToken ct)
    {
        var all = (await _credentials.GetCredentialsAsync(ct)).Where(c => c.Enabled).ToList();
        if (all.Count == 0) return null;

        if (request.CredentialId is { } id)
        {
            var byId = all.FirstOrDefault(c => c.Id == id);
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(request.ProviderName))
        {
            var kind = ProviderCatalog.FromAlias(request.ProviderName);
            var byProvider = all.FirstOrDefault(c => kind is not null && c.Provider == kind)
                             ?? all.FirstOrDefault(c => c.Provider.ToString()
                                 .Equals(request.ProviderName, StringComparison.OrdinalIgnoreCase));
            if (byProvider is not null) return byProvider;
        }

        if (config.DefaultCredentialId is { } defaultId)
        {
            var byDefault = all.FirstOrDefault(c => c.Id == defaultId);
            if (byDefault is not null) return byDefault;
        }

        // If the model hint identifies a known provider, prefer that credential.
        if (!string.IsNullOrWhiteSpace(request.ModelHint))
        {
            foreach (var credential in all)
            {
                if (request.ModelHint.Contains(credential.Provider.ToString(), StringComparison.OrdinalIgnoreCase))
                    return credential;
            }
        }

        return all.Count == 1 ? all[0] : null;
    }

    private static void ApplyUpstreamAuth(HttpRequestMessage upstream, ApiCredential credential)
    {
        switch (credential.Provider)
        {
            case ProviderKind.Anthropic:
                upstream.Headers.TryAddWithoutValidation("x-api-key", credential.Secret);
                if (!upstream.Headers.Contains("anthropic-version"))
                    upstream.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                break;
            case ProviderKind.AzureOpenAI:
                upstream.Headers.TryAddWithoutValidation("api-key", credential.Secret);
                break;
            default:
                upstream.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret);
                break;
        }
    }

    private static void CopyContentHeaders(ProxyRequest request, HttpRequestMessage upstream, bool preserveAuth)
    {
        if (upstream.Content is null) return;
        foreach (var (key, value) in request.Headers)
        {
            if (key.Equals("host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("connection", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("x-llm-", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!preserveAuth &&
                (key.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase)))
                continue;

            upstream.Content.Headers.TryAddWithoutValidation(key, value);
        }
    }

    private static bool IsAuthorized(ProxyRequest request, string token)
    {
        if (!request.Headers.TryGetValue("authorization", out var auth)) return false;
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(auth["Bearer ".Length..].Trim(), token, StringComparison.Ordinal);
    }

    private static bool IsHopByHop(string header) =>
        header.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        header.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        header.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
        header.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps the local OpenAI-style "/v1" prefix onto the upstream base path.
    /// e.g. base ".../api/v1" + "/v1/chat/completions" -> ".../api/v1/chat/completions";
    ///      base ".../v1"     + "/v1/chat/completions" -> ".../v1/chat/completions".
    /// </summary>
    private static Uri BuildTargetUri(string baseUrl, string path, string query)
    {
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        var basePath = baseUri.AbsolutePath.TrimEnd('/');   // "" or "/v1" or "/api/v1"
        var incoming = string.IsNullOrEmpty(path) ? "/" : path;

        string combined;
        if (basePath.Length == 0)
        {
            combined = incoming;
        }
        else
        {
            // Strip one leading "/v1" so it is replaced by the upstream base path.
            var remainder = incoming.Equals("/v1", StringComparison.OrdinalIgnoreCase) ||
                            incoming.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase)
                ? incoming[3..]
                : incoming;
            combined = basePath + remainder;
        }

        if (combined.Length == 0) combined = "/";

        var builder = new UriBuilder(baseUri.Scheme, baseUri.Host, baseUri.Port, combined);
        if (!string.IsNullOrEmpty(query)) builder.Query = query;
        return builder.Uri;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static async Task WriteSimpleAsync(NetworkStream stream, int status, string json, CancellationToken ct)
    {
        var reason = status switch
        {
            200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 502 => "Bad Gateway", _ => "OK"
        };
        var bytes = Encoding.UTF8.GetBytes(json);
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\n" +
                   $"Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<ProxyRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var headBytes = new List<byte>(1024);
        var single = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(single, ct);
            if (n == 0) return null;
            headBytes.Add(single[0]);
            if (headBytes.Count >= 4 &&
                headBytes[^4] == 13 && headBytes[^3] == 10 &&
                headBytes[^2] == 13 && headBytes[^1] == 10)
                break;
            if (headBytes.Count > 128 * 1024) return null;
        }

        var headText = Encoding.ASCII.GetString(headBytes.ToArray());
        var lines = headText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(':');
            if (idx <= 0) continue;
            headers[lines[i][..idx].Trim()] = lines[i][(idx + 1)..].Trim();
        }

        var body = Array.Empty<byte>();
        if (headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var length) && length > 0)
        {
            body = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var n = await stream.ReadAsync(body.AsMemory(offset, length - offset), ct);
                if (n == 0) break;
                offset += n;
            }
            if (offset < length) body = body[..offset];
        }
        else if (headers.TryGetValue("transfer-encoding", out var te) &&
                 te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await ReadChunkedAsync(stream, ct);
        }

        var path = requestLine[1];
        var query = string.Empty;
        var qIndex = path.IndexOf('?');
        if (qIndex >= 0)
        {
            query = path[(qIndex + 1)..];
            path = path[..qIndex];
        }

        var (credentialId, providerName) = ParseSelector(headers, query);
        var modelHint = TryReadModel(body);
        var contentType = headers.TryGetValue("content-type", out var ctHeader) ? ctHeader : null;

        return new ProxyRequest
        {
            Method = requestLine[0],
            Path = path,
            Query = query,
            Headers = headers,
            Body = body,
            CredentialId = credentialId,
            ProviderName = providerName,
            ModelHint = modelHint,
            ContentType = contentType
        };
    }

    private static (Guid? CredentialId, string? ProviderName) ParseSelector(
        Dictionary<string, string> headers, string query)
    {
        Guid? id = null;
        string? provider = null;

        if (headers.TryGetValue("x-llm-credential", out var cid) && Guid.TryParse(cid, out var parsedId))
            id = parsedId;
        if (headers.TryGetValue("x-llm-provider", out var p)) provider = p;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0].Equals("credential", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(kv[1], out var qid)) id = qid;
            if (kv[0].Equals("provider", StringComparison.OrdinalIgnoreCase)) provider = kv[1];
        }

        return (id, provider);
    }

    private static string? TryReadModel(byte[] body)
    {
        if (body.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadChunkedAsync(NetworkStream stream, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var line = new StringBuilder();
        var single = new byte[1];

        async Task<string?> ReadLineAsync()
        {
            line.Clear();
            while (true)
            {
                var n = await stream.ReadAsync(single, ct);
                if (n == 0) return null;
                if (single[0] == 10)
                {
                    if (line.Length > 0 && line[^1] == '\r') line.Length--;
                    return line.ToString();
                }
                line.Append((char)single[0]);
            }
        }

        while (true)
        {
            var sizeLine = await ReadLineAsync();
            if (sizeLine is null) break;
            var semi = sizeLine.IndexOf(';');
            if (semi >= 0) sizeLine = sizeLine[..semi];
            if (!int.TryParse(sizeLine.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var size)) break;
            if (size == 0)
            {
                await ReadLineAsync();
                break;
            }

            var chunk = new byte[size];
            var offset = 0;
            while (offset < size)
            {
                var n = await stream.ReadAsync(chunk.AsMemory(offset, size - offset), ct);
                if (n == 0) break;
                offset += n;
            }
            output.Write(chunk, 0, offset);
            await ReadLineAsync();
        }

        return output.ToArray();
    }

    private sealed class ProxyRequest
    {
        public string Method { get; init; } = "POST";
        public string Path { get; init; } = "/";
        public string Query { get; init; } = string.Empty;
        public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[] Body { get; init; } = Array.Empty<byte>();
        public Guid? CredentialId { get; init; }
        public string? ProviderName { get; init; }
        public string? ModelHint { get; init; }
        public string? ContentType { get; init; }
    }
}
