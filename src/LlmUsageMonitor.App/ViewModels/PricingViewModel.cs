using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.App.ViewModels;

public sealed partial class PricingViewModel : AppViewModelBase
{
    private readonly IPricingService _pricing;
    private readonly LogService _log;

    public PricingViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        IPricingService pricing, LogService log) : base(settings, cost, exchange)
    {
        _pricing = pricing;
        _log = log;
        ProviderFilters = new[] { new Choice<string>("全部平台", string.Empty) }
            .Concat(ProviderCatalog.All.Select(d => new Choice<string>(d.DisplayName, d.Kind.ToString())))
            .ToList();
    }

    public IReadOnlyList<Choice<string>> ProviderFilters { get; }
    public ObservableCollection<ModelPricing> Items { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Choice<string>? _selectedProviderFilter;
    [ObservableProperty] private ModelPricing? _selectedItem;
    [ObservableProperty] private string _editInput = "0";
    [ObservableProperty] private string _editOutput = "0";
    [ObservableProperty] private string _editCached = string.Empty;
    [ObservableProperty] private Currency _editCurrency = Currency.USD;

    public IReadOnlyList<Currency> Currencies { get; } = new[] { Currency.USD, Currency.CNY };

    partial void OnSelectedItemChanged(ModelPricing? value)
    {
        if (value is null) return;
        EditInput = value.InputPerMillion.ToString("0.####");
        EditOutput = value.OutputPerMillion.ToString("0.####");
        EditCached = value.CachedInputPerMillion?.ToString("0.####") ?? string.Empty;
        EditCurrency = value.Currency;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var provider = SelectedProviderFilter?.Value;
            var items = await _pricing.SearchAsync(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                string.IsNullOrWhiteSpace(provider) ? null : provider,
                1000);
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            StatusText = $"共 {Items.Count} 条价格（手工定价不会被在线刷新覆盖）";
        }
        catch (Exception ex)
        {
            _log.Error($"加载定价失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshSourcesAsync()
    {
        IsBusy = true;
        StatusText = "正在联网刷新定价…";
        try
        {
            var count = await _pricing.RefreshAsync();
            _log.Info($"定价刷新完成，更新 {count} 条。");
        }
        catch (Exception ex)
        {
            _log.Error($"定价刷新失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task SaveManualAsync()
    {
        if (SelectedItem is null) return;
        try
        {
            var pricing = new ModelPricing
            {
                Provider = SelectedItem.Provider,
                Model = SelectedItem.Model,
                InputPerMillion = decimal.TryParse(EditInput, out var i) ? i : 0,
                OutputPerMillion = decimal.TryParse(EditOutput, out var o) ? o : 0,
                CachedInputPerMillion = decimal.TryParse(EditCached, out var c) ? c : null,
                Currency = EditCurrency,
                Source = "manual"
            };
            await _pricing.SetManualAsync(pricing);
            _log.Info($"已保存手工定价 {pricing.Provider}/{pricing.Model}。");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"保存定价失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedItem is null) return;
        await _pricing.DeleteAsync(SelectedItem.Provider, SelectedItem.Model);
        _log.Warn($"已删除定价 {SelectedItem.Provider}/{SelectedItem.Model}。");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddBlankAsync()
    {
        SelectedItem = new ModelPricing { Provider = ProviderKind.Custom.ToString(), Model = "new-model" };
        Items.Insert(0, SelectedItem);
        await Task.CompletedTask;
    }
}
