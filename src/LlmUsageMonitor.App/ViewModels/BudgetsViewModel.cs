using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmUsageMonitor.App.Infrastructure;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.App.ViewModels;

public sealed class BudgetRuleRow
{
    public BudgetRule Rule { get; init; } = new();
    public string PeriodText { get; init; } = string.Empty;
    public string LimitText { get; init; } = string.Empty;
    public string ThresholdsText { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed class AlertRow
{
    public string LevelText { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
}

public sealed partial class BudgetsViewModel : AppViewModelBase
{
    private readonly IBudgetStore _budgets;
    private readonly IBudgetService _budgetService;
    private readonly LogService _log;

    public BudgetsViewModel(ISettingsStore settings, ICostCalculator cost, IExchangeRateService exchange,
        IBudgetStore budgets, IBudgetService budgetService, LogService log) : base(settings, cost, exchange)
    {
        _budgets = budgets;
        _budgetService = budgetService;
        _log = log;
        _budgetService.AlertRaised += (_, alert) => _log.Warn(alert.Message);
    }

    public ObservableCollection<BudgetRuleRow> Rules { get; } = new();
    public ObservableCollection<AlertRow> Alerts { get; } = new();
    public IReadOnlyList<BudgetPeriod> Periods { get; } = new[] { BudgetPeriod.Daily, BudgetPeriod.Monthly };

    [ObservableProperty] private BudgetRuleRow? _selectedRule;
    [ObservableProperty] private BudgetPeriod _editPeriod = BudgetPeriod.Monthly;
    [ObservableProperty] private string _editLimit = "10";
    [ObservableProperty] private string _editThresholds = "80,100";

    partial void OnSelectedRuleChanged(BudgetRuleRow? value)
    {
        if (value is null) return;
        EditPeriod = value.Rule.Period;
        EditLimit = Cost.Convert(value.Rule.LimitUsd, DisplayCurrency, UsdToCny).ToString("0.##");
        EditThresholds = string.Join(",", value.Rule.Thresholds);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await RefreshRateAsync();

            var rules = await _budgets.GetRulesAsync();
            Rules.Clear();
            foreach (var rule in rules)
            {
                Rules.Add(new BudgetRuleRow
                {
                    Rule = rule,
                    PeriodText = rule.Period == BudgetPeriod.Daily ? "每日" : "每月",
                    LimitText = Money(rule.LimitUsd),
                    ThresholdsText = string.Join(", ", rule.Thresholds) + "%",
                    Enabled = rule.Enabled
                });
            }

            var alerts = await _budgets.GetAlertsAsync(100);
            Alerts.Clear();
            foreach (var alert in alerts)
            {
                Alerts.Add(new AlertRow
                {
                    LevelText = alert.Level switch
                    {
                        AlertLevel.Critical => "严重",
                        AlertLevel.Warning => "警告",
                        _ => "提示"
                    },
                    Title = alert.Title,
                    Message = alert.Message,
                    TimeText = alert.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                });
            }

            StatusText = $"{Rules.Count} 条预算 · {Alerts.Count} 条告警";
        }
        catch (Exception ex)
        {
            _log.Error($"加载预算失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewRule()
    {
        SelectedRule = null;
        EditPeriod = BudgetPeriod.Monthly;
        EditLimit = "100";
        EditThresholds = "80,100";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var rule = SelectedRule?.Rule ?? new BudgetRule();
        rule.ScopeType = BudgetScopeType.Global;
        rule.ScopeId = null;
        rule.Period = EditPeriod;

        var entered = decimal.TryParse(EditLimit, out var limit) ? limit : 0;
        // Limit is entered in the display currency; store it in USD.
        rule.LimitUsd = DisplayCurrency == Currency.CNY && UsdToCny > 0 ? entered / UsdToCny : entered;

        rule.Thresholds = EditThresholds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        if (rule.Thresholds.Count == 0) rule.Thresholds = new List<int> { 80, 100 };

        await _budgets.SaveRuleAsync(rule);
        _log.Info($"已保存预算（{Money(rule.LimitUsd)}）。");
        SelectedRule = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedRule is null) return;
        await _budgets.DeleteRuleAsync(SelectedRule.Rule.Id);
        _log.Warn("已删除预算规则。");
        SelectedRule = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task EvaluateAsync()
    {
        var raised = await _budgetService.EvaluateAsync();
        _log.Info(raised.Count == 0 ? "预算检查完成，无新告警。" : $"触发 {raised.Count} 条告警。");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearAlertsAsync()
    {
        await _budgets.ClearAlertsAsync();
        await LoadAsync();
    }
}
