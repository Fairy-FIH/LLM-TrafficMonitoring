using System.Net;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Infrastructure.Http;

/// <summary>Builds <see cref="HttpClient"/> instances honouring global and per-credential proxy settings.</summary>
public sealed class HttpProvider
{
    private readonly ISettingsStore _settings;

    public HttpProvider(ISettingsStore settings) => _settings = settings;

    public HttpClient Create(ApiCredential? credential = null, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };

        var overrideProxy = credential?.ProxyOverride;
        if (!string.IsNullOrWhiteSpace(overrideProxy))
        {
            handler.Proxy = new WebProxy(overrideProxy);
            handler.UseProxy = true;
        }
        else
        {
            ApplyGlobalProxy(handler);
        }

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LlmUsageMonitor/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private void ApplyGlobalProxy(HttpClientHandler handler)
    {
        var proxy = _settings.Current.Proxy;
        switch (proxy.Mode)
        {
            case ProxyMode.None:
                handler.UseProxy = false;
                break;
            case ProxyMode.System:
                handler.UseProxy = true;
                handler.Proxy = WebRequest.GetSystemWebProxy();
                break;
            case ProxyMode.Custom:
                if (string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port <= 0) break;
                var webProxy = new WebProxy($"{proxy.ToUri()}")
                {
                    BypassProxyOnLocal = proxy.BypassLocal
                };
                if (!string.IsNullOrWhiteSpace(proxy.Username))
                {
                    webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password ?? string.Empty);
                }
                handler.Proxy = webProxy;
                handler.UseProxy = true;
                break;
        }
    }
}
