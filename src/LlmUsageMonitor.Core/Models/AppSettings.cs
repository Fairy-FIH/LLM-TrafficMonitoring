namespace LlmUsageMonitor.Core.Models;

/// <summary>Well-known UI module ids that can each carry their own accent color.</summary>
public static class ModuleKeys
{
    public const string Dashboard = "Dashboard";
    public const string Usage = "Usage";
    public const string Providers = "Providers";
    public const string WebCapture = "WebCapture";
    public const string Pricing = "Pricing";
    public const string Budgets = "Budgets";
    public const string Proxy = "Proxy";
    public const string Export = "Export";
    public const string Settings = "Settings";
    public const string Logs = "Logs";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Dashboard, Usage, WebCapture, Pricing, Budgets, Proxy, Export, Settings, Logs
    };
}

public sealed class ThemeSettings
{
    public ThemeMode Mode { get; set; } = ThemeMode.Dark;

    /// <summary>Per-module accent color, keyed by ModuleKeys.*</summary>
    public Dictionary<string, string> ModuleColors { get; set; } = new()
    {
        [ModuleKeys.Dashboard] = "#4F8CFF",
        [ModuleKeys.Usage] = "#22C1A8",
        [ModuleKeys.Providers] = "#F2994A",
        [ModuleKeys.WebCapture] = "#E566B5",
        [ModuleKeys.Pricing] = "#9B7BFF",
        [ModuleKeys.Budgets] = "#E85D75",
        [ModuleKeys.Proxy] = "#39A0ED",
        [ModuleKeys.Export] = "#2EBD85",
        [ModuleKeys.Settings] = "#8E9BAE",
        [ModuleKeys.Logs] = "#C9A227"
    };

    public string ColorFor(string module) =>
        ModuleColors.TryGetValue(module, out var c) && !string.IsNullOrWhiteSpace(c) ? c : "#4F8CFF";
}

public sealed class AppSettings
{
    public ThemeSettings Theme { get; set; } = new();
    public CacheTtlSettings CacheTtl { get; set; } = new();
    public PricingSourceSettings PricingSources { get; set; } = new();
    public ExchangeRateSettings ExchangeRate { get; set; } = new();
    public ProxySettings Proxy { get; set; } = new();
    public LocalProxySettings LocalProxy { get; set; } = new();

    public int AutoRefreshSeconds { get; set; } = 60;
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool DesktopNotifications { get; set; } = true;
    public bool SeedPricingOnFirstRun { get; set; } = true;

    public AppSettings Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}
