using System.Windows;
using LlmUsageMonitor.App.ViewModels;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;
using LlmUsageMonitor.Infrastructure;
using LlmUsageMonitor.Infrastructure.Data;
using LlmUsageMonitor.Infrastructure.Exchange;
using LlmUsageMonitor.Infrastructure.Http;
using LlmUsageMonitor.Infrastructure.Pricing;
using LlmUsageMonitor.Infrastructure.Proxy;
using LlmUsageMonitor.Infrastructure.Security;
using LlmUsageMonitor.Infrastructure.Services;
using LlmUsageMonitor.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.App.Infrastructure;

public static class ServiceRegistration
{
    public static ServiceProvider Build(AppPaths paths, ISettingsStore settingsStore,
        ISecretProtector protector, Database db)
    {
        var settings = settingsStore.Current;
        var services = new ServiceCollection();

        services.AddSingleton(paths);
        services.AddSingleton(settings);
        services.AddSingleton(protector);
        services.AddSingleton(db);
        services.AddSingleton<LogService>();

        // Reuse the already-loaded settings store instance.
        services.AddSingleton(settingsStore);
        services.AddSingleton<ICredentialStore>(sp => new CredentialStore(db, protector));
        services.AddSingleton<IUsageStore>(_ => new UsageStore(db));
        services.AddSingleton<IPricingStore>(_ => new PricingStore(db));
        services.AddSingleton<IBudgetStore>(_ => new BudgetStore(db));
        services.AddSingleton<IExchangeRateStore>(_ => new ExchangeRateStore(db));

        services.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton<ICacheStore>(sp => new CacheStore(db, sp.GetRequiredService<IMemoryCache>(), settings.CacheTtl));

        services.AddSingleton<ICostCalculator, CostCalculator>();
        services.AddSingleton<ITokenEstimator, TokenEstimator>();

        services.AddSingleton<HttpProvider>();
        services.AddSingleton<HttpJsonClient>();

        services.AddSingleton<IPricingSource>(sp => new SeedPricingSource());
        services.AddSingleton<IPricingSource>(sp => new LiteLlmPricingSource(
            sp.GetRequiredService<HttpProvider>(), sp.GetRequiredService<HttpJsonClient>(),
            sp.GetRequiredService<ICacheStore>(), () => settings.PricingSources.LiteLlmUrl));
        services.AddSingleton<IPricingSource>(sp => new OpenRouterPricingSource(
            sp.GetRequiredService<HttpProvider>(), sp.GetRequiredService<HttpJsonClient>(),
            sp.GetRequiredService<ICacheStore>(), () => settings.PricingSources.OpenRouterUrl));

        services.AddSingleton<IPricingService, PricingService>();
        services.AddSingleton<IExchangeRateService, ExchangeRateService>();
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IUsageSyncService, UsageSyncService>();
        services.AddSingleton<IBudgetService, BudgetService>();
        services.AddSingleton<IReportExporter, ReportExporter>();
        services.AddSingleton<ILocalProxyServer, LocalProxyServer>();

        // UI services and view models
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<UsageViewModel>();
        services.AddSingleton<WebCaptureViewModel>();
        services.AddSingleton<PricingViewModel>();
        services.AddSingleton<BudgetsViewModel>();
        services.AddSingleton<ProxyViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
