using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.ViewModels;

public sealed partial class LogsViewModel : AppViewModelBase
{
    private readonly LogService _log;

    public LogsViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        LogService log) : base(settings, cost, exchange)
    {
        _log = log;
        Entries = log.Entries;
    }

    public ObservableCollection<LogEntry> Entries { get; }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        StatusText = "日志已清空";
    }
}
