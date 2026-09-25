using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;
using Microsoft.Win32;

namespace LlmUsageMonitor.App.ViewModels;

public sealed partial class ExportViewModel : AppViewModelBase
{
    private readonly IReportExporter _exporter;
    private readonly ITokenEstimator _tokens;
    private readonly IPricingService _pricing;
    private readonly LogService _log;

    public ExportViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        IReportExporter exporter, ITokenEstimator tokens, IPricingService pricing, LogService log)
        : base(settings, cost, exchange)
    {
        _exporter = exporter;
        _tokens = tokens;
        _pricing = pricing;
        _log = log;
        ProviderFilters = new[] { new Choice<ProviderKind?>("全部平台", null) }
            .Concat(ProviderCatalog.All.Select(d => new Choice<ProviderKind?>(d.DisplayName, d.Kind)))
            .ToList();
    }

    // Export
    [ObservableProperty] private RangeOption _range = RangeOption.Month;
    [ObservableProperty] private Choice<ProviderKind?>? _selectedProvider;
    [ObservableProperty] private ReportFormat _format = ReportFormat.Excel;
    public IReadOnlyList<RangeOption> Ranges => RangeOption.Options;
    public IReadOnlyList<Choice<ProviderKind?>> ProviderFilters { get; }
    public IReadOnlyList<ReportFormat> Formats { get; } = new[] { ReportFormat.Excel, ReportFormat.Csv, ReportFormat.Json };

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"usage-{DateTime.Now:yyyyMMdd-HHmm}",
            Filter = SelectedFormatFilter(),
            InitialDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            await _exporter.ExportAsync(Format, dialog.FileName, BuildQuery());
            StatusText = "已导出：" + dialog.FileName;
            _log.Info($"报表已导出：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            _log.Error($"导出失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string SelectedFormatFilter() => Format switch
    {
        ReportFormat.Csv => "CSV 文件|*.csv",
        ReportFormat.Json => "JSON 文件|*.json",
        _ => "Excel 工作簿|*.xlsx"
    };

    private UsageQuery BuildQuery()
    {
        DateTimeOffset? from = Range.Days <= 0
            ? null
            : new DateTimeOffset(DateTime.Today.AddDays(-(Range.Days - 1)));
        return new UsageQuery { From = from, Provider = SelectedProvider?.Value };
    }

    // Token pre-calculation
    [ObservableProperty] private string _tokenModel = "gpt-4o-mini";
    [ObservableProperty] private string _tokenPrompt = string.Empty;
    [ObservableProperty] private string _tokenOutput = "1024";
    [ObservableProperty] private string _tokenInputResult = "0";
    [ObservableProperty] private string _tokenOutputResult = "0";
    [ObservableProperty] private string _tokenCostResult = "$0.0000";
    [ObservableProperty] private string _tokenNote = string.Empty;

    [RelayCommand]
    private async Task CalculateTokensAsync()
    {
        await RefreshRateAsync();
        var model = TokenModel.Trim();
        if (string.IsNullOrEmpty(model)) return;

        ModelPricing? pricing = null;
        ModelPricing? found = null;
        foreach (var kind in ProviderCatalog.All)
        {
            found = await _pricing.ResolveAsync(kind.Kind, model);
            if (found is not null) break;
        }
        pricing = found;

        var output = int.TryParse(TokenOutput, out var o) ? o : 0;
        var estimate = _tokens.Estimate(model, TokenPrompt, output, pricing, UsdToCny);

        TokenInputResult = estimate.InputTokens.ToString("N0");
        TokenOutputResult = estimate.OutputTokens.ToString("N0");
        TokenCostResult = estimate.HasPricing ? Money(estimate.CostUsd) : "无定价";
        TokenNote = estimate.ExactTokenizer
            ? (estimate.HasPricing ? "使用精确分词器，价格来自本地定价库" : "使用精确分词器，但未找到该模型定价")
            : (estimate.HasPricing ? "使用启发式估算（中文≈1字/token），价格来自本地定价库" : "使用启发式估算，且未找到该模型定价");
    }
}
