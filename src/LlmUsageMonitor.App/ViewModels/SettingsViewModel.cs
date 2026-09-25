using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.ViewModels;

public sealed partial class ModuleColorItem : ObservableObject
{
    public ModuleColorItem(string key, string name, string color)
    {
        Key = key;
        Name = name;
        _color = color;
    }

    public string Key { get; }
    public string Name { get; }

    [ObservableProperty] private string _color;

    public string Preview => Color;
}

public sealed partial class SettingsViewModel : AppViewModelBase
{
    private readonly ICacheStore _cache;
    private readonly ILocalProxyServer _proxy;
    private readonly ThemeManager _theme;
    private readonly LogService _log;
    private readonly IUsageStore _usage;
    private readonly IBudgetStore _budgets;

    public SettingsViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        ICacheStore cache, ILocalProxyServer proxy, ThemeManager theme, LogService log,
        IUsageStore usage, IBudgetStore budgets)
        : base(settings, cost, exchange)
    {
        _cache = cache;
        _proxy = proxy;
        _theme = theme;
        _usage = usage;
        _budgets = budgets;
        _log = log;

        ThemeModes = new[]
        {
            new Choice<ThemeMode>("跟随系统", ThemeMode.System),
            new Choice<ThemeMode>("深色", ThemeMode.Dark),
            new Choice<ThemeMode>("浅色", ThemeMode.Light)
        };
        CurrencyChoices = new[]
        {
            new Choice<Currency>("人民币 ¥", Currency.CNY),
            new Choice<Currency>("美元 $", Currency.USD)
        };
        ProxyModes = new[]
        {
            new Choice<ProxyMode>("不使用", ProxyMode.None),
            new Choice<ProxyMode>("系统代理", ProxyMode.System),
            new Choice<ProxyMode>("自定义", ProxyMode.Custom)
        };
        ProxyProtocols = new[]
        {
            new Choice<ProxyProtocol>("HTTP", ProxyProtocol.Http),
            new Choice<ProxyProtocol>("SOCKS5", ProxyProtocol.Socks5)
        };

        LoadFromSettings();
    }

    public IReadOnlyList<Choice<ThemeMode>> ThemeModes { get; }
    public IReadOnlyList<Choice<Currency>> CurrencyChoices { get; }
    public IReadOnlyList<Choice<ProxyMode>> ProxyModes { get; }
    public IReadOnlyList<Choice<ProxyProtocol>> ProxyProtocols { get; }
    public ObservableCollection<ModuleColorItem> ModuleColors { get; } = new();

    [ObservableProperty] private Choice<ThemeMode>? _selectedTheme;
    [ObservableProperty] private Choice<Currency>? _selectedCurrency;
    [ObservableProperty] private Choice<ProxyMode>? _selectedProxyMode;
    [ObservableProperty] private Choice<ProxyProtocol>? _selectedProxyProtocol;

    [ObservableProperty] private string _manualRate = "7.2";
    [ObservableProperty] private bool _rateLocked;
    [ObservableProperty] private string _rateStatus = string.Empty;

    [ObservableProperty] private bool _useLiteLlm = true;
    [ObservableProperty] private bool _useOpenRouter = true;
    [ObservableProperty] private string _proxyHost = "127.0.0.1";
    [ObservableProperty] private string _proxyPort = "7890";
    [ObservableProperty] private string _proxyUser = string.Empty;
    [ObservableProperty] private string _proxyPassword = string.Empty;
    [ObservableProperty] private bool _proxyBypassLocal = true;

    [ObservableProperty] private bool _localProxyEnabled;
    [ObservableProperty] private string _localProxyAddress = "127.0.0.1";
    [ObservableProperty] private string _localProxyPort = "8787";
    [ObservableProperty] private string _localProxyToken = string.Empty;
    [ObservableProperty] private string _localProxyStatus = "未启动";

    [ObservableProperty] private string _pricingTtl = "1440";
    [ObservableProperty] private string _exchangeTtl = "360";
    [ObservableProperty] private string _usageTtl = "5";
    [ObservableProperty] private string _modelsTtl = "720";

    [ObservableProperty] private string _autoRefreshSeconds = "60";
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _desktopNotifications = true;

    private void LoadFromSettings()
    {
        var s = Settings.Current;
        SelectedTheme = ThemeModes.FirstOrDefault(m => m.Value == s.Theme.Mode) ?? ThemeModes[1];
        SelectedCurrency = CurrencyChoices.FirstOrDefault(c => c.Value == s.ExchangeRate.DisplayCurrency) ?? CurrencyChoices[0];
        SelectedProxyMode = ProxyModes.FirstOrDefault(m => m.Value == s.Proxy.Mode) ?? ProxyModes[0];
        SelectedProxyProtocol = ProxyProtocols.FirstOrDefault(m => m.Value == s.Proxy.Protocol) ?? ProxyProtocols[0];

        ManualRate = s.ExchangeRate.ManualRate.ToString("0.####");
        RateLocked = s.ExchangeRate.Locked;

        UseLiteLlm = s.PricingSources.UseLiteLlm;
        UseOpenRouter = s.PricingSources.UseOpenRouter;

        ProxyHost = s.Proxy.Host;
        ProxyPort = s.Proxy.Port.ToString();
        ProxyUser = s.Proxy.Username ?? string.Empty;
        ProxyPassword = s.Proxy.Password ?? string.Empty;
        ProxyBypassLocal = s.Proxy.BypassLocal;

        LocalProxyEnabled = s.LocalProxy.Enabled;
        LocalProxyAddress = s.LocalProxy.ListenAddress;
        LocalProxyPort = s.LocalProxy.Port.ToString();
        LocalProxyToken = s.LocalProxy.AccessToken ?? string.Empty;

        PricingTtl = s.CacheTtl.PricingMinutes.ToString();
        ExchangeTtl = s.CacheTtl.ExchangeRateMinutes.ToString();
        UsageTtl = s.CacheTtl.UsageMinutes.ToString();
        ModelsTtl = s.CacheTtl.ModelsMinutes.ToString();

        AutoRefreshSeconds = s.AutoRefreshSeconds.ToString();
        MinimizeToTray = s.MinimizeToTray;
        DesktopNotifications = s.DesktopNotifications;

        ModuleColors.Clear();
        foreach (var key in ModuleKeys.All)
            ModuleColors.Add(new ModuleColorItem(key, ModuleDisplayName(key), s.Theme.ColorFor(key)));
    }

    private static string ModuleDisplayName(string key) => key switch
    {
        ModuleKeys.Dashboard => "仪表盘",
        ModuleKeys.Usage => "用量明细",
        ModuleKeys.Providers => "密钥管理",
        ModuleKeys.WebCapture => "网页登录",
        ModuleKeys.Pricing => "模型定价",
        ModuleKeys.Budgets => "预算告警",
        ModuleKeys.Proxy => "本地代理",
        ModuleKeys.Export => "报表导出",
        ModuleKeys.Settings => "设置",
        ModuleKeys.Logs => "运行日志",
        _ => key
    };

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = Settings.Current;
        s.Theme.Mode = SelectedTheme?.Value ?? ThemeMode.Dark;
        foreach (var item in ModuleColors) s.Theme.ModuleColors[item.Key] = item.Color;

        s.ExchangeRate.DisplayCurrency = SelectedCurrency?.Value ?? Currency.CNY;
        s.ExchangeRate.Locked = RateLocked;
        if (decimal.TryParse(ManualRate, out var rate) && rate > 0) s.ExchangeRate.ManualRate = rate;

        s.PricingSources.UseLiteLlm = UseLiteLlm;
        s.PricingSources.UseOpenRouter = UseOpenRouter;

        s.Proxy.Mode = SelectedProxyMode?.Value ?? ProxyMode.None;
        s.Proxy.Protocol = SelectedProxyProtocol?.Value ?? ProxyProtocol.Http;
        s.Proxy.Host = ProxyHost;
        s.Proxy.Port = int.TryParse(ProxyPort, out var p) ? p : 7890;
        s.Proxy.Username = string.IsNullOrWhiteSpace(ProxyUser) ? null : ProxyUser;
        s.Proxy.Password = string.IsNullOrWhiteSpace(ProxyPassword) ? null : ProxyPassword;
        s.Proxy.BypassLocal = ProxyBypassLocal;

        s.LocalProxy.Enabled = LocalProxyEnabled;
        s.LocalProxy.ListenAddress = LocalProxyAddress;
        s.LocalProxy.Port = int.TryParse(LocalProxyPort, out var lp) ? lp : 8787;
        s.LocalProxy.AccessToken = string.IsNullOrWhiteSpace(LocalProxyToken) ? null : LocalProxyToken;

        s.CacheTtl.PricingMinutes = ParseInt(PricingTtl, 1440);
        s.CacheTtl.ExchangeRateMinutes = ParseInt(ExchangeTtl, 360);
        s.CacheTtl.UsageMinutes = ParseInt(UsageTtl, 5);
        s.CacheTtl.ModelsMinutes = ParseInt(ModelsTtl, 720);

        s.AutoRefreshSeconds = ParseInt(AutoRefreshSeconds, 60);
        s.MinimizeToTray = MinimizeToTray;
        s.DesktopNotifications = DesktopNotifications;

        await Settings.SaveAsync(s);
        _theme.Initialize(s.Theme);
        if (RateLocked && decimal.TryParse(ManualRate, out var lockedRate) && lockedRate > 0)
            await Exchange.LockAsync(lockedRate);

        await ApplyLocalProxyAsync(s);
        RaiseCurrencyChanged();
        StatusText = "设置已保存（含 AES 加密）";
        _log.Info("设置已保存。");
    }

    [RelayCommand]
    private async Task RefreshExchangeRateAsync()
    {
        RateStatus = "刷新中…";
        try
        {
            var info = await Exchange.GetAsync(forceRefresh: true);
            ManualRate = info.Rate.ToString("0.####");
            RateStatus = $"最新汇率 1 USD = {info.Rate:0.####} CNY（{info.FetchedAt:HH:mm:ss}）";
        }
        catch (Exception ex)
        {
            RateStatus = "刷新失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task LockRateAsync()
    {
        if (!decimal.TryParse(ManualRate, out var rate) || rate <= 0) return;
        RateLocked = true;
        await Exchange.LockAsync(rate);
        RateStatus = $"已锁定 1 USD = {rate:0.####} CNY";
        _log.Info($"汇率已锁定为 {rate:0.####}。");
    }

    [RelayCommand]
    private async Task UnlockRateAsync()
    {
        RateLocked = false;
        await Exchange.UnlockAsync();
        RateStatus = "已解锁，将自动刷新";
    }

    [RelayCommand]
    private async Task ApplyProxyAsync()
    {
        await SaveAsync();
        _log.Info("代理设置已应用。");
    }

    [RelayCommand]
    private async Task ToggleLocalProxyAsync()
    {
        try
        {
            if (_proxy.IsRunning)
            {
                await _proxy.StopAsync();
                LocalProxyStatus = "未启动";
            }
            else
            {
                var s = Settings.Current;
                s.LocalProxy.Enabled = true;
                s.LocalProxy.ListenAddress = LocalProxyAddress;
                s.LocalProxy.Port = ParseInt(LocalProxyPort, 8787);
                s.LocalProxy.AccessToken = string.IsNullOrWhiteSpace(LocalProxyToken) ? null : LocalProxyToken;
                await Settings.SaveAsync(s);
                await _proxy.StartAsync();
                LocalProxyStatus = $"运行中 · http://{LocalProxyAddress}:{_proxy.Port}/v1";
            }
        }
        catch (Exception ex)
        {
            LocalProxyStatus = "启动失败：" + ex.Message;
            _log.Error($"本地代理启动失败: {ex.Message}");
        }
    }

    private async Task ApplyLocalProxyAsync(AppSettings s)
    {
        try
        {
            if (s.LocalProxy.Enabled && !_proxy.IsRunning)
            {
                await _proxy.StartAsync();
                LocalProxyStatus = $"运行中 · http://{s.LocalProxy.ListenAddress}:{_proxy.Port}/v1";
            }
            else if (!s.LocalProxy.Enabled && _proxy.IsRunning)
            {
                await _proxy.StopAsync();
                LocalProxyStatus = "未启动";
            }
        }
        catch (Exception ex)
        {
            LocalProxyStatus = "启动失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _cache.ClearAsync();
        _log.Warn("已清空缓存。");
        StatusText = "缓存已清空";
    }

    [RelayCommand]
    private async Task ClearAllDataAsync()
    {
        var confirm = MessageBox.Show(
            "将清空所有用量记录、缓存、告警与预算，清除网页登录会话，并把设置恢复为默认。\n\n此操作不可撤销，确定继续？",
            "清除所有数据", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (_proxy.IsRunning) await _proxy.StopAsync();
            await _usage.ClearAllAsync();
            await _cache.ClearAsync();
            await _budgets.ClearAlertsAsync();
            foreach (var rule in await _budgets.GetRulesAsync()) await _budgets.DeleteRuleAsync(rule.Id);

            await Settings.SaveAsync(new AppSettings());
            _theme.Initialize(Settings.Current.Theme);

            try
            {
                var webViewFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LlmUsageMonitor", "webview2");
                if (System.IO.Directory.Exists(webViewFolder))
                    System.IO.Directory.Delete(webViewFolder, true);
            }
            catch
            {
                // Session folder may be locked while the browser is alive; ignore.
            }

            _log.Warn("已清除所有数据。");
            StatusText = "已清除所有数据";
            LoadFromSettings();
        }
        catch (Exception ex)
        {
            _log.Error($"清除数据失败: {ex.Message}");
        }
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text, out var value) && value >= 0 ? value : fallback;
}
