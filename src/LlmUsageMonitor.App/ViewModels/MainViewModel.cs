using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.ViewModels;

public sealed partial class NavItem : ObservableObject
{
    public NavItem(string key, string title, string glyph, object viewModel)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        ViewModel = viewModel;
    }

    public string Key { get; }
    public string Title { get; }
    public string Glyph { get; }
    public object ViewModel { get; }

    [ObservableProperty] private bool _isSelected;
}

public sealed partial class MainViewModel : AppViewModelBase
{
    private readonly ThemeManager _theme;
    private readonly LogService _log;
    private readonly ILocalProxyServer _proxy;
    private readonly IPricingService _pricing;
    private readonly IUsageSyncService _sync;
    private readonly IBudgetService _budgets;
    private readonly Dictionary<string, Func<Task>> _reloaders = new();
    private readonly DispatcherTimer _timer = new();

    public MainViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        ThemeManager theme, LogService log, ILocalProxyServer proxy, IPricingService pricing,
        IUsageSyncService sync, IBudgetService budgets,
        DashboardViewModel dashboard, UsageViewModel usage,
        WebCaptureViewModel webCapture, PricingViewModel pricingVm, BudgetsViewModel budgetsVm,
        ProxyViewModel proxyVm, ExportViewModel export, SettingsViewModel settingsVm, LogsViewModel logs)
        : base(settings, cost, exchange)
    {
        _theme = theme;
        _log = log;
        _proxy = proxy;
        _pricing = pricing;
        _sync = sync;
        _budgets = budgets;

        Add(ModuleKeys.Dashboard, "仪表盘", "\uE80F", dashboard, dashboard.LoadAsync);
        Add(ModuleKeys.Usage, "用量明细", "\uE9D9", usage, usage.LoadAsync);
        Add(ModuleKeys.WebCapture, "网页登录", "\uE71B", webCapture, () => Task.CompletedTask);
        Add(ModuleKeys.Pricing, "模型定价", "\uE8EC", pricingVm, pricingVm.LoadAsync);
        Add(ModuleKeys.Budgets, "预算告警", "\uEA39", budgetsVm, budgetsVm.LoadAsync);
        Add(ModuleKeys.Proxy, "本地代理", "\uE774", proxyVm, proxyVm.LoadAsync);
        Add(ModuleKeys.Export, "报表与工具", "\uEDE1", export, () => Task.CompletedTask);
        Add(ModuleKeys.Settings, "设置", "\uE713", settingsVm, () => Task.CompletedTask);
        Add(ModuleKeys.Logs, "运行日志", "\uE81C", logs, () => Task.CompletedTask);

        SelectedNavItem = NavItems.First();
        UpdateCurrencyLabel();
        UpdateThemeLabel();

        _timer.Interval = TimeSpan.FromSeconds(Math.Max(15, settings.Current.AutoRefreshSeconds));
        _timer.Tick += async (_, _) => await AutoRefreshAsync();
    }

    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty] private object? _currentViewModel;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private string _currencyLabel = "¥ CNY";
    [ObservableProperty] private string _themeLabel = "深色";
    [ObservableProperty] private string _proxyBadge = "代理未启动";
    [ObservableProperty] private bool _isProxyRunning;
    [ObservableProperty] private bool _isSyncing;

    public string VersionText =>
        "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");

    public void Start()
    {
        _timer.Start();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _log.Info("正在刷新定价并同步用量…");
            await _pricing.RefreshAsync();
            var result = await _sync.SyncAllAsync();
            _log.Info($"初始同步完成：新增 {result.RecordsAdded} 条，失败 {result.Failures} 个。");
            if (Settings.Current.LocalProxy.Enabled && !_proxy.IsRunning)
                await _proxy.StartAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"初始化同步失败: {ex.Message}");
        }
        await ReloadCurrentAsync();
        UpdateProxyBadge();
    }

    private void Add(string key, string title, string glyph, object vm, Func<Task> reload)
    {
        NavItems.Add(new NavItem(key, title, glyph, vm));
        _reloaders[key] = reload;
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is null) return;
        foreach (var item in NavItems) item.IsSelected = ReferenceEquals(item, value);
        CurrentViewModel = value.ViewModel;
        _theme.SetModuleAccent(value.Key);
        _ = reloadCurrentFor(value.Key);
    }

    private async Task reloadCurrentFor(string key)
    {
        if (_reloaders.TryGetValue(key, out var reload))
        {
            try { await reload(); }
            catch (Exception ex) { _log.Error($"加载页面失败: {ex.Message}"); }
        }
        UpdateProxyBadge();
    }

    [RelayCommand]
    private Task ReloadCurrentAsync()
        => SelectedNavItem is null ? Task.CompletedTask : reloadCurrentFor(SelectedNavItem.Key);

    [RelayCommand]
    private async Task SelectAsync(NavItem item) => SelectedNavItem = item;

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        StatusText = "正在同步…";
        try
        {
            await RefreshRateAsync();
            await _pricing.RefreshAsync();
            var progress = new Progress<SyncProgress>(p => StatusText = p.Message);
            var result = await _sync.SyncAllAsync(progress);
            await _budgets.EvaluateAsync();
            _log.Info($"同步完成：新增 {result.RecordsAdded} 条，失败 {result.Failures} 个。");
            StatusText = $"同步完成（{DateTime.Now:HH:mm:ss}）";
        }
        catch (Exception ex)
        {
            _log.Error($"同步失败: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
            await ReloadCurrentAsync();
        }
    }

    [RelayCommand]
    private async Task CycleCurrencyAsync()
    {
        var settings = Settings.Current;
        settings.ExchangeRate.DisplayCurrency =
            settings.ExchangeRate.DisplayCurrency == Currency.CNY ? Currency.USD : Currency.CNY;
        await Settings.SaveAsync(settings);
        await BroadcastCurrencyAsync();
        _log.Info($"显示币种切换为 {settings.ExchangeRate.DisplayCurrency}。");
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var settings = Settings.Current;
        settings.Theme.Mode = settings.Theme.Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        await Settings.SaveAsync(settings);
        _theme.Apply(settings.Theme.Mode);
        UpdateThemeLabel();
    }

    private async Task BroadcastCurrencyAsync()
    {
        await RefreshRateAsync();
        UpdateCurrencyLabel();
        foreach (var item in NavItems)
        {
            if (item.ViewModel is AppViewModelBase vm) vm.RaiseCurrencyChanged();
        }
        await ReloadCurrentAsync();
    }

    private async Task AutoRefreshAsync()
    {
        try
        {
            var result = await _sync.SyncAllAsync();
            if (result.RecordsAdded > 0)
            {
                await _budgets.EvaluateAsync();
                await ReloadCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"自动刷新失败: {ex.Message}");
        }
        UpdateProxyBadge();
    }

    private void UpdateCurrencyLabel()
        => CurrencyLabel = Settings.Current.ExchangeRate.DisplayCurrency == Currency.CNY ? "¥ CNY" : "$ USD";

    private void UpdateThemeLabel()
        => ThemeLabel = Settings.Current.Theme.Mode == ThemeMode.Light ? "浅色" : "深色";

    private void UpdateProxyBadge()
    {
        IsProxyRunning = _proxy.IsRunning;
        ProxyBadge = _proxy.IsRunning ? $"本地代理 :{_proxy.Port}" : "代理未启动";
    }
}
