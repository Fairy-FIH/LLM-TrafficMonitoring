using CommunityToolkit.Mvvm.ComponentModel;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.ViewModels;

/// <summary>Shared plumbing for view models: settings, currency conversion and rate caching.</summary>
public abstract partial class AppViewModelBase : ObservableObject
{
    private decimal _usdToCny = 7.2m;

    protected AppViewModelBase(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange)
    {
        Settings = settings;
        Cost = cost;
        Exchange = exchange;
    }

    protected ISettingsStore Settings { get; }
    protected ICostCalculator Cost { get; }
    protected IExchangeRateService Exchange { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public Currency DisplayCurrency => Settings.Current.ExchangeRate.DisplayCurrency;
    public decimal UsdToCny => _usdToCny;

    public async Task RefreshRateAsync()
    {
        try { _usdToCny = await Exchange.GetUsdToCnyAsync(); }
        catch { /* keep last known */ }
    }

    public string Money(decimal usd) => Cost.Format(usd, DisplayCurrency, _usdToCny);

    public string CurrencySymbol => Cost.Symbol(DisplayCurrency);

    public void RaiseCurrencyChanged()
    {
        OnPropertyChanged(nameof(DisplayCurrency));
        OnPropertyChanged(nameof(CurrencySymbol));
    }
}
