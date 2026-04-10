using System.Collections.Concurrent;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventLogTracer.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IEventLogReader _eventLogReader;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentQueue<DateTime> _eventTimestamps = new();
    private readonly Timer _rateTimer;
    private DateTime _lastEventTime = DateTime.MinValue;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _selectedNavItem = "Dashboard";

    [ObservableProperty]
    private int _totalEventCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    private bool _isMonitoring;

    [ObservableProperty]
    private string _lastEventInfo = "No events yet";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _eventRateText = string.Empty;

    public string ToggleButtonText => IsMonitoring ? "Stop Monitoring" : "Start Monitoring";

    public bool IsDashboardSelected   => SelectedNavItem == "Dashboard";
    public bool IsEventViewerSelected => SelectedNavItem == "EventViewer";
    public bool IsTimelineSelected    => SelectedNavItem == "Timeline";
    public bool IsAlertsSelected      => SelectedNavItem == "Alerts";
    public bool IsSearchSelected      => SelectedNavItem == "Search";
    public bool IsSettingsSelected    => SelectedNavItem == "Settings";

    // Nav pages
    public DashboardViewModel DashboardVm { get; }
    public EventViewerViewModel EventViewerVm { get; }
    public TimelineViewModel TimelineVm { get; }
    public AlertsViewModel AlertsVm { get; }
    public SearchViewModel SearchVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public MainWindowViewModel(
        DashboardViewModel dashboardVm,
        EventViewerViewModel eventViewerVm,
        TimelineViewModel timelineVm,
        AlertsViewModel alertsVm,
        SearchViewModel searchVm,
        SettingsViewModel settingsVm,
        IEventLogReader eventLogReader,
        IServiceProvider serviceProvider)
    {
        DashboardVm = dashboardVm;
        EventViewerVm = eventViewerVm;
        TimelineVm = timelineVm;
        AlertsVm = alertsVm;
        SearchVm = searchVm;
        SettingsVm = settingsVm;
        _eventLogReader = eventLogReader;
        _serviceProvider = serviceProvider;

        _currentPage = DashboardVm;

        // Recalculate event rate every 5 seconds
        _rateTimer = new Timer(UpdateEventRate, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await EventViewerVm.LoadRecentEventsAsync();
            TotalEventCount = EventViewerVm.Events.Count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize EventViewerViewModel");
        }
    }

    partial void OnSelectedNavItemChanged(string value)
    {
        OnPropertyChanged(nameof(IsDashboardSelected));
        OnPropertyChanged(nameof(IsEventViewerSelected));
        OnPropertyChanged(nameof(IsTimelineSelected));
        OnPropertyChanged(nameof(IsAlertsSelected));
        OnPropertyChanged(nameof(IsSearchSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    [RelayCommand]
    private void NavigateTo(string destination)
    {
        SelectedNavItem = destination;
        CurrentPage = destination switch
        {
            "Dashboard"   => DashboardVm,
            "EventViewer" => EventViewerVm,
            "Timeline"    => TimelineVm,
            "Alerts"      => AlertsVm,
            "Search"      => SearchVm,
            "Settings"    => SettingsVm,
            _             => DashboardVm
        };
    }

    public void NavigateToPage(string destination) => NavigateTo(destination);

    [RelayCommand]
    private void ToggleMonitoring()
    {
        if (IsMonitoring)
        {
            _eventLogReader.StopMonitoring();
            IsMonitoring = false;
            StatusMessage = "Monitoring stopped";
            EventRateText = string.Empty;
        }
        else
        {
            _eventLogReader.StartMonitoring(OnEventReceived);
            IsMonitoring = true;
            StatusMessage = "Monitoring active";
        }
    }

    /// <summary>Called on a background thread by IEventLogReader.</summary>
    private void OnEventReceived(EventEntry entry)
    {
        _eventTimestamps.Enqueue(DateTime.UtcNow);
        _lastEventTime = DateTime.UtcNow;

        Dispatcher.UIThread.Post(() =>
        {
            TotalEventCount++;
            LastEventInfo = $"EventId {entry.EventId} from {entry.Source} at {entry.TimeCreated:HH:mm:ss}";
            EventViewerVm.AddEventToView(entry);
        });

        // Fire-and-forget alert check — never blocks the UI thread
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alertService = scope.ServiceProvider.GetRequiredService<IAlertService>();
                await alertService.CheckAndNotifyAsync(entry);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Alert check failed for event {EventId}", entry.EventId);
            }
        });
    }

    /// <summary>Timer callback: prune old timestamps, compute events/min, update UI.</summary>
    private void UpdateEventRate(object? state)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-60);

        while (_eventTimestamps.TryPeek(out var ts) && ts < cutoff)
            _eventTimestamps.TryDequeue(out _);

        var count = _eventTimestamps.Count;
        var sinceLastEvent = now - _lastEventTime;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsMonitoring)
            {
                EventRateText = string.Empty;
                return;
            }

            if (count == 0 && sinceLastEvent.TotalSeconds > 30)
                EventRateText = "No recent events";
            else
                EventRateText = $"{count} events/min";

            StatusMessage = string.IsNullOrEmpty(EventRateText)
                ? "Monitoring active"
                : $"Monitoring active — {EventRateText}";
        });
    }

    public async Task RefreshCurrentPageAsync()
    {
        switch (CurrentPage)
        {
            case DashboardViewModel:
                await DashboardVm.RefreshCommand.ExecuteAsync(null);
                break;
            case EventViewerViewModel:
                await EventViewerVm.RefreshEventsCommand.ExecuteAsync(null);
                break;
            case TimelineViewModel:
                await TimelineVm.LoadTimelineCommand.ExecuteAsync(null);
                break;
            case AlertsViewModel:
                await AlertsVm.LoadRulesCommand.ExecuteAsync(null);
                break;
            case SearchViewModel:
                await SearchVm.ExecuteSearchCommand.ExecuteAsync(null);
                break;
            case SettingsViewModel:
                await SettingsVm.RefreshStatsCommand.ExecuteAsync(null);
                break;
        }
    }

    public void ClearCurrentSelectionOrClosePanels()
    {
        switch (CurrentPage)
        {
            case EventViewerViewModel:
                EventViewerVm.SelectedEvent = null;
                break;
            case TimelineViewModel:
                TimelineVm.SelectedTimelineEvent = null;
                TimelineVm.SelectedEventDetail = null;
                break;
            case SearchViewModel:
                SearchVm.SelectedResult = null;
                break;
            case AlertsViewModel when AlertsVm.IsEditing:
                AlertsVm.CancelEditCommand.Execute(null);
                break;
        }
    }

    public async Task QuickExportCurrentEventsAsync()
    {
        try
        {
            var exportDirectory = Path.GetFullPath("exports");
            Directory.CreateDirectory(exportDirectory);

            var filePath = Path.Combine(
                exportDirectory,
                $"eventviewer_quick_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");

            using var scope = _serviceProvider.CreateScope();
            var exportService = scope.ServiceProvider.GetRequiredService<IExportService>();

            var currentEvents = EventViewerVm.Events.ToList();
            await exportService.ExportToCsvAsync(currentEvents, filePath);

            StatusMessage = $"Quick export complete — {currentEvents.Count} events saved to {filePath}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Quick export failed");
            StatusMessage = "Quick export failed";
        }
    }

    public void Dispose()
    {
        _rateTimer.Dispose();
        if (IsMonitoring)
            _eventLogReader.StopMonitoring();
        GC.SuppressFinalize(this);
    }
}
