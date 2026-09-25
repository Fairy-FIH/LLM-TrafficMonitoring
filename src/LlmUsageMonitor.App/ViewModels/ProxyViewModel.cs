using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.App.ViewModels;

/// <summary>Status, upstream and quick controls for the built-in capture proxy.</summary>
public sealed partial class ProxyViewModel : AppViewModelBase
{
    private readonly ILocalProxyServer _proxy;
    private readonly IUsageStore _usage;
    private readonly LogService _log;

    public ProxyViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        ILocalProxyServer proxy, IUsageStore usage, LogService log) : base(settings, cost, exchange)
    {
        _proxy = proxy;
        _usage = usage;
        _log = log;
        Providers = ProviderCatalog.All
            .Select(d => new Choice<ProviderKind>(d.DisplayName, d.Kind))
            .ToList();
    }

    [ObservableProperty] private string _endpointText = "http://127.0.0.1:8787/v1";
    [ObservableProperty] private string _stateText = "未启动";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _capturedTodayText = "0 次请求";
    [ObservableProperty] private string _portText = "8787";
    [ObservableProperty] private Choice<ProviderKind>? _defaultProvider;
    [ObservableProperty] private string _defaultBaseUrl = "https://api.deepseek.com/v1";
    [ObservableProperty] private bool _passThroughAuth = true;

    public IReadOnlyList<Choice<ProviderKind>> Providers { get; }

    public string[] Instructions { get; } =
    {
        "启动后，将任意客户端的 OpenAI Base URL 指向下方地址即可自动记账。",
        "例如：OPENAI_BASE_URL = http://127.0.0.1:8787/v1",
        "客户端带上自己的 API Key 即可（默认透传）；多平台可用 X-Llm-Provider: DeepSeek 指定上游。",
        "未匹配到密钥时，使用下方“默认上游”转发客户端自带的 Authorization。"
    };

    [RelayCommand]
    public async Task LoadAsync()
    {
        var config = Settings.Current.LocalProxy;
        PortText = config.Port.ToString();
        EndpointText = $"http://{config.ListenAddress}:{config.Port}/v1";
        IsRunning = _proxy.IsRunning;
        StateText = IsRunning ? "运行中" : "未启动";
        DefaultProvider = Providers.FirstOrDefault(p => p.Value == config.DefaultProvider)
                          ?? Providers.FirstOrDefault();
        DefaultBaseUrl = config.DefaultBaseUrl ?? string.Empty;
        PassThroughAuth = config.PassThroughAuth;

        var today = new DateTimeOffset(DateTime.Today);
        var summary = await _usage.GetSummaryAsync(new UsageQuery
        {
            From = today,
            Source = UsageSource.LocalProxy
        });
        CapturedTodayText = $"{summary.Requests:N0} 次请求 · {DashboardViewModel.FormatTokens(summary.TotalTokens)} tokens · {Money(summary.CostUsd)}";
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        try
        {
            if (_proxy.IsRunning)
            {
                await _proxy.StopAsync();
                _log.Info("本地代理已停止。");
            }
            else
            {
                await SaveUpstreamAsync();
                await _proxy.StartAsync();
                _log.Info($"本地代理已启动：{EndpointText}");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"本地代理操作失败: {ex.Message}");
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SaveUpstreamAsync()
    {
        var settings = Settings.Current;
        settings.LocalProxy.DefaultProvider = DefaultProvider?.Value ?? ProviderKind.DeepSeek;
        settings.LocalProxy.DefaultBaseUrl = string.IsNullOrWhiteSpace(DefaultBaseUrl) ? null : DefaultBaseUrl.Trim();
        settings.LocalProxy.PassThroughAuth = PassThroughAuth;
        await Settings.SaveAsync(settings);
        StatusText = "上游设置已保存";
    }

    [RelayCommand]
    private async Task OpenBrowserAsync()
    {
        await RefreshRateAsync();
        try
        {
            var config = Settings.Current.LocalProxy;
            var endpoint = $"http://{config.ListenAddress}:{config.Port}/";
            Process.Start(new ProcessStartInfo(endpoint) { UseShellExecute = true });
            StatusText = $"已在浏览器打开 {endpoint}";
        }
        catch (Exception ex)
        {
            StatusText = "打开浏览器失败：" + ex.Message;
        }
    }
}
