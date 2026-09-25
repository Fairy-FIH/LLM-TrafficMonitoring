using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.App.ViewModels;

public sealed partial class DashboardViewModel : AppViewModelBase
{
    private readonly IUsageStore _usage;
    private readonly IUsageSyncService _sync;
    private readonly IPricingService _pricing;
    private readonly ILocalProxyServer _proxy;
    private readonly IBudgetService _budgets;
    private readonly LogService _log;

    public DashboardViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        IUsageStore usage, IUsageSyncService sync, IPricingService pricing, ILocalProxyServer proxy,
        IBudgetService budgets, LogService log) : base(settings, cost, exchange)
    {
        _usage = usage;
        _sync = sync;
        _pricing = pricing;
        _proxy = proxy;
        _budgets = budgets;
        _log = log;
    }

    [ObservableProperty] private string _requestsText = "0";
    [ObservableProperty] private string _inputTokensText = "0";
    [ObservableProperty] private string _outputTokensText = "0";
    [ObservableProperty] private string _totalTokensText = "0";
    [ObservableProperty] private string _costText = "$0.0000";
    [ObservableProperty] private string _pricingCountText = "0 个模型价格";
    [ObservableProperty] private string _proxyStatusText = "本地代理未启动";
    [ObservableProperty] private RangeOption _range = RangeOption.Month;

    public IReadOnlyList<RangeOption> Ranges => RangeOption.Options;

    public ObservableCollection<ChartSlice> ProviderSlices { get; } = new();
    public ObservableCollection<ChartSlice> ModelSlices { get; } = new();
    public ObservableCollection<ChartPoint> DailyPoints { get; } = new();
    public ObservableCollection<UsageRecord> RecentRecords { get; } = new();

    partial void OnRangeChanged(RangeOption value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await RefreshRateAsync();
            var query = BuildQuery();
            var summary = await _usage.GetSummaryAsync(query);

            RequestsText = summary.Requests.ToString("N0");
            InputTokensText = FormatTokens(summary.InputTokens);
            OutputTokensText = FormatTokens(summary.OutputTokens);
            TotalTokensText = FormatTokens(summary.TotalTokens);
            CostText = Money(summary.CostUsd);

            ProviderSlices.Clear();
            foreach (var item in await _usage.GetByProviderAsync(query))
            {
                var kind = int.TryParse(item.Key, out var p) ? (ProviderKind)p : ProviderKind.Custom;
                ProviderSlices.Add(new ChartSlice
                {
                    Label = ProviderCatalog.Get(kind).DisplayName,
                    Value = (double)item.CostUsd,
                    DisplayValue = ToDisplay(item.CostUsd),
                    DisplayText = Money(item.CostUsd),
                    CostUsd = item.CostUsd,
                    Tokens = item.TotalTokens,
                    Requests = item.Requests,
                    Color = ProviderCatalog.Get(kind).DefaultColorHex,
                    IsAccentColor = true
                });
            }

            ModelSlices.Clear();
            foreach (var item in await _usage.GetByModelAsync(query, 8))
            {
                var (kind, model) = ParseModelKey(item.Key);
                var label = string.IsNullOrWhiteSpace(model) ||
                            model is "web-import" or "unknown" or "unknown-model"
                    ? "未标注模型"
                    : model;

                ModelSlices.Add(new ChartSlice
                {
                    Label = label,
                    Value = (double)item.CostUsd,
                    DisplayValue = ToDisplay(item.CostUsd),
                    DisplayText = Money(item.CostUsd),
                    CostUsd = item.CostUsd,
                    Tokens = item.TotalTokens,
                    Requests = item.Requests,
                    Color = ProviderCatalog.Get(kind).DefaultColorHex,
                    IsAccentColor = true
                });
            }

            DailyPoints.Clear();
            var daily = (await _usage.GetDailyAsync(query))
                .Where(d => d.Bucket is not null)
                .OrderBy(d => d.Bucket)
                .ToList();
            foreach (var d in daily)
            {
                DailyPoints.Add(new ChartPoint
                {
                    Label = d.Bucket!.Value.ToString("MM-dd"),
                    Value = (double)d.CostUsd,
                    DisplayValue = ToDisplay(d.CostUsd),
                    DisplayText = Money(d.CostUsd),
                    CostUsd = d.CostUsd
                });
            }

            RecentRecords.Clear();
            foreach (var record in await _usage.GetRecentAsync(15, query))
                RecentRecords.Add(record);

            PricingCountText = $"{await _pricing.CountAsync()} 个模型价格";
            ProxyStatusText = _proxy.IsRunning ? $"本地代理运行中 · 端口 {_proxy.Port}" : "本地代理未启动";
            StatusText = $"更新于 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _log.Error($"加载仪表盘失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _pricing.RefreshAsync();
            var progress = new Progress<SyncProgress>(p => StatusText = p.Message);
            var result = await _sync.SyncAllAsync(progress);
            await _budgets.EvaluateAsync();
            _log.Info($"同步完成：{result.CredentialsProcessed} 个密钥，新增 {result.RecordsAdded} 条记录，失败 {result.Failures}。");
            foreach (var message in result.Messages) _log.Info(message);
        }
        catch (Exception ex)
        {
            _log.Error($"同步失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            await LoadAsync();
        }
    }

    private double ToDisplay(decimal usd)
        => (double)Cost.Convert(usd, DisplayCurrency, UsdToCny);

    private static (ProviderKind Kind, string Model) ParseModelKey(string key)
    {
        var separator = key.IndexOf('|');
        if (separator > 0 &&
            int.TryParse(key[..separator], out var provider) &&
            Enum.IsDefined(typeof(ProviderKind), provider))
        {
            return ((ProviderKind)provider, key[(separator + 1)..]);
        }
        return (ProviderKind.Custom, key);
    }

    private UsageQuery BuildQuery()
    {
        if (Range.Days <= 0) return new UsageQuery();
        // Single-arg ctor keeps the local offset; passing TimeSpan.Zero with a Local
        // DateTime throws on non-UTC machines.
        var from = new DateTimeOffset(DateTime.Today.AddDays(-(Range.Days - 1)));
        return new UsageQuery { From = from };
    }

    public static string FormatTokens(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000d:0.00}B",
        >= 1_000_000 => $"{value / 1_000_000d:0.00}M",
        >= 1_000 => $"{value / 1_000d:0.0}K",
        _ => value.ToString("N0")
    };
}
