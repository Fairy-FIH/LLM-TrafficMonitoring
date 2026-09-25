using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.App.WebCapture;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using LlmUsageMonitor.Core.Services;

namespace LlmUsageMonitor.App.ViewModels;

/// <summary>
/// Drives the built-in browser-login capture: the user logs into a provider
/// console, we observe JSON responses, and import the ones that look like usage.
/// </summary>
public sealed partial class WebCaptureViewModel : AppViewModelBase
{
    private const int MaxCandidates = 200;

    private readonly IUsageStore _usage;
    private readonly IPricingService _pricing;
    private readonly LogService _log;

    public WebCaptureViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        IUsageStore usage, IPricingService pricing, LogService log) : base(settings, cost, exchange)
    {
        _usage = usage;
        _pricing = pricing;
        _log = log;

        Providers = ProviderCatalog.All
            .Where(d => !string.IsNullOrWhiteSpace(d.ConsoleUrl))
            .Select(d => new Choice<ProviderKind>(d.DisplayName, d.Kind))
            .ToList();

        SelectedProvider = Providers.FirstOrDefault(p => p.Value == ProviderKind.DeepSeek) ?? Providers.FirstOrDefault();
    }

    /// <summary>Raised when the user asks to navigate the embedded browser.</summary>
    public event Action<string>? NavigateRequested;
    public event Action? ReloadRequested;
    public event Action<string>? OpenExternalRequested;

    public IReadOnlyList<Choice<ProviderKind>> Providers { get; }
    public ObservableCollection<CapturedResponse> Candidates { get; } = new();

    [ObservableProperty] private Choice<ProviderKind>? _selectedProvider;
    [ObservableProperty] private string _navigationUrl = string.Empty;
    [ObservableProperty] private CapturedResponse? _selectedCandidate;
    [ObservableProperty] private string _previewText = string.Empty;
    [ObservableProperty] private bool _autoImport = true;
    [ObservableProperty] private string _capturedCountText = "已捕获 0 个响应";
    [ObservableProperty] private string _importedCountText = "已导入 0 条用量";
    [ObservableProperty] private bool _isReady;

    private int _importedTotal;

    partial void OnSelectedProviderChanged(Choice<ProviderKind>? value)
    {
        UpdateNavigationUrl();
    }

    public void UpdateNavigationUrl()
    {
        var descriptor = SelectedProvider is null ? null : ProviderCatalog.Get(SelectedProvider.Value);
        if (descriptor?.ConsoleUrl is { Length: > 0 } url) NavigationUrl = url;
    }

    partial void OnSelectedCandidateChanged(CapturedResponse? value)
        => PreviewText = value is null ? string.Empty : Pretty(value.Body);

    [RelayCommand]
    private void Navigate()
    {
        if (string.IsNullOrWhiteSpace(NavigationUrl)) UpdateNavigationUrl();
        NavigateRequested?.Invoke(NavigationUrl);
    }

    [RelayCommand]
    private void Reload() => ReloadRequested?.Invoke();

    [RelayCommand]
    private void OpenExternal()
    {
        if (!string.IsNullOrWhiteSpace(NavigationUrl)) OpenExternalRequested?.Invoke(NavigationUrl);
    }

    [RelayCommand]
    private void Clear()
    {
        Candidates.Clear();
        SelectedCandidate = null;
        CapturedCountText = "已捕获 0 个响应";
        StatusText = "已清空捕获列表";
    }

    [RelayCommand]
    private async Task ImportSelectedAsync()
    {
        if (SelectedCandidate is null) return;
        await ImportAsync(SelectedCandidate);
    }

    /// <summary>Called from the view when a JSON response has been intercepted.</summary>
    public async Task AddCandidateAsync(string url, string method, int status, string body)
    {
        var provider = SelectedProvider?.Value ?? ProviderKind.Custom;
        var rate = await Exchange.GetUsdToCnyAsync();
        var result = UsageExtractor.Extract(url, body, provider, rate);

        var candidate = new CapturedResponse
        {
            Url = url,
            Method = method,
            StatusCode = status,
            Body = body,
            LooksLikeUsage = result.LooksLikeUsage,
            Score = result.Score,
            ExtractedCount = result.Records.Count,
            Summary = result.Summary
        };

        Candidates.Insert(0, candidate);
        while (Candidates.Count > MaxCandidates) Candidates.RemoveAt(Candidates.Count - 1);
        CapturedCountText = $"已捕获 {Candidates.Count} 个响应";
        UpdateBestCandidate();

        if (result.LooksLikeUsage && AutoImport)
            await ImportAsync(candidate);
    }

    private void UpdateBestCandidate()
    {
        var best = Candidates.Where(c => c.LooksLikeUsage).OrderByDescending(c => c.Score).FirstOrDefault();
        if (best is not null && SelectedCandidate is null) SelectedCandidate = best;
    }

    private async Task ImportAsync(CapturedResponse candidate)
    {
        var provider = SelectedProvider?.Value ?? ProviderKind.Custom;
        var rate = await Exchange.GetUsdToCnyAsync();
        var result = UsageExtractor.Extract(candidate.Url, candidate.Body, provider, rate);
        if (result.Records.Count == 0)
        {
            StatusText = "该响应未识别到用量数据";
            return;
        }

        foreach (var record in result.Records)
        {
            if (record.CostUsd == 0 && (record.InputTokens > 0 || record.OutputTokens > 0))
            {
                var price = await _pricing.ResolveAsync(record.Provider, record.Model);
                if (price is not null)
                    record.CostUsd = Cost.ComputeUsd(new TokenUsage
                    {
                        InputTokens = record.InputTokens,
                        OutputTokens = record.OutputTokens,
                        CachedTokens = record.CachedTokens
                    }, price, rate);
            }
        }

        var inserted = await _usage.AddRangeAsync(result.Records);
        _importedTotal += inserted;
        ImportedCountText = $"已导入 {_importedTotal} 条用量";
        StatusText = inserted > 0
            ? $"已导入 {inserted} 条（去重后）"
            : "记录已存在（去重跳过）";
        _log.Info($"网页抓取导入：{provider} {inserted} 条（来源 {candidate.ShortUrl}）。");
    }

    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return pretty.Length > 6000 ? pretty[..6000] + "\n…（已截断）" : pretty;
        }
        catch
        {
            return json.Length > 6000 ? json[..6000] : json;
        }
    }
}
