using System.Windows;
using System.Windows.Threading;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.App.ViewModels;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Infrastructure;
using LlmUsageMonitor.Infrastructure.Data;
using LlmUsageMonitor.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("Services not ready");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var paths = new AppPaths();
            var protector = new AesSecretProtector(paths.KeyPath);
            var database = new Database(paths.DatabasePath);
            await database.InitializeAsync();

            var settingsStore = new SettingsStore(paths.SettingsPath, protector);
            await settingsStore.LoadAsync();

            _services = ServiceRegistration.Build(paths, settingsStore, protector, database);

            var theme = _services.GetRequiredService<ThemeManager>();
            theme.Initialize(settingsStore.Current.Theme);

            var log = _services.GetRequiredService<LogService>();
            var proxy = _services.GetRequiredService<ILocalProxyServer>();
            proxy.Log += (_, message) => log.Info(message);
            proxy.UsageCaptured += (_, record) =>
                log.Info($"代理记账 · {record.Model} · 输入 {record.InputTokens} / 输出 {record.OutputTokens} tokens");

            log.Info("LLM Usage Monitor 启动");
            log.Info($"数据目录：{paths.BaseDirectory}");

            var mainViewModel = _services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = mainViewModel };
            MainWindow = window;
            window.Show();

            mainViewModel.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败：{ex}", "LLM Usage Monitor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _services?.GetService<LogService>()?.Error($"未处理异常: {e.Exception.Message}");
        }
        catch
        {
            // ignore
        }
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            var proxy = _services?.GetService<ILocalProxyServer>();
            if (proxy is { IsRunning: true }) await proxy.StopAsync();
        }
        catch
        {
            // ignore
        }
        _services?.Dispose();
        base.OnExit(e);
    }
}
