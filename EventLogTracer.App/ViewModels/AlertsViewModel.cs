using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventLogTracer.App.ViewModels;

public partial class AlertsViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private ObservableCollection<AlertRule> _alertRules = [];

    [ObservableProperty]
    private AlertRule? _selectedRule;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isLoading;

    // ── Edit form ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private bool _editIsEnabled = true;

    [ObservableProperty]
    private NotificationType _editNotificationType = NotificationType.Desktop;

    [ObservableProperty]
    private string _editNotificationTarget = string.Empty;

    [ObservableProperty]
    private string _editFilterLevels = string.Empty;

    [ObservableProperty]
    private string _editFilterSources = string.Empty;

    [ObservableProperty]
    private string _editFilterLogNames = string.Empty;

    [ObservableProperty]
    private string _editFilterEventIds = string.Empty;

    [ObservableProperty]
    private string _editFilterSearchText = string.Empty;

    // ── Computed / static ─────────────────────────────────────────────────────

    public bool HasRules => AlertRules.Count > 0;
    public bool NoRules  => AlertRules.Count == 0;

    public string EditPanelTitle => SelectedRule is null ? "New Rule" : "Edit Rule";

    public string EditNotificationTargetWatermark => EditNotificationType switch
    {
        NotificationType.Desktop => "N/A — desktop notifications don't require a target",
        NotificationType.Email   => "email@example.com",
        NotificationType.Webhook => "https://hooks.example.com/notify",
        _                        => string.Empty
    };

    public static IEnumerable<NotificationType> NotificationTypeOptions { get; } =
        [NotificationType.Desktop, NotificationType.Email, NotificationType.Webhook];

    // ── Constructor ───────────────────────────────────────────────────────────

    public AlertsViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        AlertRules.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRules));
            OnPropertyChanged(nameof(NoRules));
        };

        _ = LoadRulesAsync();
    }

    // ── Property side-effects ─────────────────────────────────────────────────

    partial void OnEditNotificationTypeChanged(NotificationType value)
        => OnPropertyChanged(nameof(EditNotificationTargetWatermark));

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadRulesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IAlertService>();
            var rules = await svc.GetRulesAsync();

            AlertRules.Clear();
            foreach (var r in rules)
                AlertRules.Add(r);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load alert rules");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void NewRule()
    {
        SelectedRule = null;
        ClearEditForm();
        IsEditing = true;
        OnPropertyChanged(nameof(EditPanelTitle));
    }

    [RelayCommand]
    private void EditRule(AlertRule rule)
    {
        SelectedRule = rule;
        PopulateEditForm(rule);
        IsEditing = true;
        OnPropertyChanged(nameof(EditPanelTitle));
    }

    [RelayCommand]
    private async Task SaveRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
            return;

        var filter = new EventFilter
        {
            Levels    = ParseLevels(EditFilterLevels),
            Sources   = ParseList(EditFilterSources),
            LogNames  = ParseList(EditFilterLogNames),
            EventIds  = ParseEventIds(EditFilterEventIds),
            SearchText = string.IsNullOrWhiteSpace(EditFilterSearchText)
                ? null
                : EditFilterSearchText.Trim()
        };

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IAlertService>();

            if (SelectedRule is null)
            {
                var rule = new AlertRule
                {
                    Name               = EditName.Trim(),
                    IsEnabled          = EditIsEnabled,
                    NotificationType   = EditNotificationType,
                    NotificationTarget = EditNotificationTarget.Trim(),
                    Filter             = filter
                };
                await svc.AddRuleAsync(rule);
                AlertRules.Add(rule);
            }
            else
            {
                SelectedRule.Name               = EditName.Trim();
                SelectedRule.IsEnabled          = EditIsEnabled;
                SelectedRule.NotificationType   = EditNotificationType;
                SelectedRule.NotificationTarget = EditNotificationTarget.Trim();
                SelectedRule.Filter             = filter;
                await svc.UpdateRuleAsync(SelectedRule);

                // Force the ObservableCollection to refresh the card
                var idx = AlertRules.IndexOf(SelectedRule);
                if (idx >= 0)
                {
                    AlertRules.RemoveAt(idx);
                    AlertRules.Insert(idx, SelectedRule);
                }
            }

            IsEditing    = false;
            SelectedRule = null;
            ClearEditForm();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save alert rule");
        }
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(AlertRule rule)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IAlertService>();
            await svc.DeleteRuleAsync(rule.Id);
            AlertRules.Remove(rule);

            if (SelectedRule == rule)
            {
                SelectedRule = null;
                IsEditing    = false;
                ClearEditForm();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete alert rule {Id}", rule.Id);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing    = false;
        SelectedRule = null;
        ClearEditForm();
    }

    [RelayCommand]
    private async Task ToggleRuleAsync(AlertRule rule)
    {
        rule.IsEnabled = !rule.IsEnabled;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IAlertService>();
            await svc.UpdateRuleAsync(rule);

            // Refresh the card in the list
            var idx = AlertRules.IndexOf(rule);
            if (idx >= 0)
            {
                AlertRules.RemoveAt(idx);
                AlertRules.Insert(idx, rule);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to toggle alert rule {Id}", rule.Id);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ClearEditForm()
    {
        EditName               = string.Empty;
        EditIsEnabled          = true;
        EditNotificationType   = NotificationType.Desktop;
        EditNotificationTarget = string.Empty;
        EditFilterLevels       = string.Empty;
        EditFilterSources      = string.Empty;
        EditFilterLogNames     = string.Empty;
        EditFilterEventIds     = string.Empty;
        EditFilterSearchText   = string.Empty;
    }

    private void PopulateEditForm(AlertRule rule)
    {
        EditName               = rule.Name;
        EditIsEnabled          = rule.IsEnabled;
        EditNotificationType   = rule.NotificationType;
        EditNotificationTarget = rule.NotificationTarget;

        EditFilterLevels   = rule.Filter.Levels   is { Count: > 0 }
            ? string.Join(", ", rule.Filter.Levels)   : string.Empty;
        EditFilterSources  = rule.Filter.Sources  is { Count: > 0 }
            ? string.Join(", ", rule.Filter.Sources)  : string.Empty;
        EditFilterLogNames = rule.Filter.LogNames is { Count: > 0 }
            ? string.Join(", ", rule.Filter.LogNames) : string.Empty;
        EditFilterEventIds = rule.Filter.EventIds is { Count: > 0 }
            ? string.Join(", ", rule.Filter.EventIds) : string.Empty;
        EditFilterSearchText = rule.Filter.SearchText ?? string.Empty;
    }

    private static List<EventLevel>? ParseLevels(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var result = new List<EventLevel>();
        foreach (var s in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<EventLevel>(s, ignoreCase: true, out var lvl))
                result.Add(lvl);
        return result.Count > 0 ? result : null;
    }

    private static List<string>? ParseList(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var result = input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return result.Count > 0 ? result : null;
    }

    private static List<int>? ParseEventIds(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var result = new List<int>();
        foreach (var s in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(s, out var id))
                result.Add(id);
        return result.Count > 0 ? result : null;
    }
}
