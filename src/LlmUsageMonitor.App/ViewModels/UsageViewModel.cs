using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.App.ViewModels;

/// <summary>A formatted usage row for the detail grid (cost in the display currency).</summary>
public sealed class UsageRow
{
    public string TimeText { get; init; } = string.Empty;
    public string ProviderText { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CachedTokens { get; init; }
    public long Requests { get; init; }
    public string CostText { get; init; } = string.Empty;
}

public sealed partial class UsageViewModel : AppViewModelBase
{
    private readonly IUsageStore _usage;
    private readonly LogService _log;

    public UsageViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        IUsageStore usage, LogService log) : base(settings, cost, exchange)
    {
        _usage = usage;
        _log = log;
        ProviderOptions = new[] { new Choice<ProviderKind?>("全部平台", null) }
            .Concat(ProviderCatalog.All.Select(d => new Choice<ProviderKind?>(d.DisplayName, d.Kind)))
            .ToList();
        SourceOptions = new[]
        {
            new Choice<UsageSource?>("全部来源", null),
            new Choice<UsageSource?>("官方 API", UsageSource.OfficialApi),
            new Choice<UsageSource?>("本地代理", UsageSource.LocalProxy),
            new Choice<UsageSource?>("导入", UsageSource.Import),
            new Choice<UsageSource?>("手动", UsageSource.Manual)
        };
    }

    [ObservableProperty] private RangeOption _range = RangeOption.Month;
    [ObservableProperty] private Choice<ProviderKind?>? _selectedProvider;
    [ObservableProperty] private Choice<UsageSource?>? _selectedSource;
    [ObservableProperty] private string _modelFilter = string.Empty;
    [ObservableProperty] private string _requestsText = "0";
    [ObservableProperty] private string _tokensText = "0";
    [ObservableProperty] private string _costText = "$0.0000";

    public IReadOnlyList<RangeOption> Ranges => RangeOption.Options;
    public IReadOnlyList<Choice<ProviderKind?>> ProviderOptions { get; }
    public IReadOnlyList<Choice<UsageSource?>> SourceOptions { get; }
    public ObservableCollection<UsageRow> Rows { get; } = new();

    partial void OnRangeChanged(RangeOption value) => _ = LoadAsync();
    partial void OnSelectedProviderChanged(Choice<ProviderKind?>? value) => _ = LoadAsync();
    partial void OnSelectedSourceChanged(Choice<UsageSource?>? value) => _ = LoadAsync();

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
            TokensText = DashboardViewModel.FormatTokens(summary.TotalTokens);
            CostText = Money(summary.CostUsd);

            Rows.Clear();
            foreach (var record in await _usage.GetRecentAsync(500, query))
            {
                Rows.Add(new UsageRow
                {
                    TimeText = record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    ProviderText = ProviderCatalog.Get(record.Provider).DisplayName,
                    Model = record.Model,
                    SourceText = record.Source switch
                    {
                        UsageSource.OfficialApi => "官方 API",
                        UsageSource.LocalProxy => "本地代理",
                        UsageSource.Import => "导入",
                        UsageSource.Manual => "手动",
                        _ => record.Source.ToString()
                    },
                    InputTokens = record.InputTokens,
                    OutputTokens = record.OutputTokens,
                    CachedTokens = record.CachedTokens,
                    Requests = record.Requests,
                    CostText = Money(record.CostUsd)
                });
            }
            StatusText = $"共 {Rows.Count} 条明细";
        }
        catch (Exception ex)
        {
            _log.Error($"加载用量失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteFilteredAsync()
    {
        try
        {
            var deleted = await _usage.DeleteAsync(BuildQuery());
            _log.Warn($"已删除 {deleted} 条用量记录。");
        }
        catch (Exception ex)
        {
            _log.Error($"删除失败: {ex.Message}");
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        try
        {
            await _usage.ClearAllAsync();
            _log.Warn("已清空全部用量记录。");
        }
        catch (Exception ex)
        {
            _log.Error($"清空失败: {ex.Message}");
        }
        await LoadAsync();
    }

    private UsageQuery BuildQuery()
    {
        DateTimeOffset? from = Range.Days <= 0
            ? null
            : new DateTimeOffset(DateTime.Today.AddDays(-(Range.Days - 1)));
        return new UsageQuery
        {
            From = from,
            Provider = SelectedProvider?.Value,
            Source = SelectedSource?.Value,
            Model = string.IsNullOrWhiteSpace(ModelFilter) ? null : ModelFilter.Trim()
        };
    }
}
