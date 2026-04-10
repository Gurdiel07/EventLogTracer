using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventLogTracer.App.ViewModels;

public partial class EventViewerViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<EventEntry> _allEvents = [];
    private const int MaxVisibleEvents = 1000;

    [ObservableProperty]
    private ObservableCollection<EventEntry> _events = [];

    [ObservableProperty]
    private EventEntry? _selectedEvent;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _selectedLogName = "All";

    [ObservableProperty]
    private string _selectedLevel = "All";

    [ObservableProperty]
    private int _visibleCount;

    public List<string> LogNameOptions { get; } =
        ["All", "Application", "Security", "System", "Setup", "ForwardedEvents"];

    public List<string> LevelOptions { get; } =
        ["All", "Critical", "Error", "Warning", "Information", "Verbose"];

    public EventViewerViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>Load the most recent 200 events from the database on startup.</summary>
    public async Task LoadRecentEventsAsync()
    {
        IsLoading = true;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();

            var entries = (await repo.GetByFilterAsync(new EventFilter()))
                .Take(200)
                .ToList();

            _allEvents.Clear();
            _allEvents.AddRange(entries);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load recent events from database");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Called on the UI thread by MainWindowViewModel when a new monitored event arrives.
    /// Inserts at top (most recent first), caps at <see cref="MaxVisibleEvents"/>,
    /// and persists to SQLite in the background.
    /// </summary>
    public void AddEventToView(EventEntry entry)
    {
        _allEvents.Insert(0, entry);
        if (_allEvents.Count > MaxVisibleEvents)
            _allEvents.RemoveAt(_allEvents.Count - 1);

        if (MatchesCurrentFilter(entry))
        {
            Events.Insert(0, entry);
            if (Events.Count > MaxVisibleEvents)
                Events.RemoveAt(Events.Count - 1);
            VisibleCount = Events.Count;
        }

        _ = Task.Run(() => PersistEventAsync(entry));
    }

    // Reactive filter triggers
    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLogNameChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var selected = SelectedEvent;
        Events = new ObservableCollection<EventEntry>(_allEvents.Where(MatchesCurrentFilter));
        VisibleCount = Events.Count;

        if (selected is not null && Events.Contains(selected))
            SelectedEvent = selected;
        else
            SelectedEvent = null;
    }

    private bool MatchesCurrentFilter(EventEntry entry)
    {
        if (SelectedLogName != "All" &&
            !string.Equals(entry.LogName, SelectedLogName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (SelectedLevel != "All" && entry.Level.ToString() != SelectedLevel)
            return false;

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var text = FilterText;
            if (!entry.Source.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                !entry.Message.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                !entry.EventId.ToString().Contains(text, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    [RelayCommand]
    private async Task RefreshEventsAsync()
    {
        await LoadRecentEventsAsync();
    }

    [RelayCommand]
    private void ClearEvents()
    {
        _allEvents.Clear();
        Events.Clear();
        VisibleCount = 0;
        SelectedEvent = null;
    }

    [RelayCommand]
    private async Task ToggleBookmarkAsync(EventEntry? entry)
    {
        if (entry is null) return;

        entry.IsBookmarked = !entry.IsBookmarked;
        if (!entry.IsBookmarked)
        {
            entry.BookmarkComment = null;
            entry.BookmarkColor = null;
        }

        ForceRowRefresh(entry);
        await PersistEventUpdateAsync(entry);
    }

    [RelayCommand]
    private async Task SaveBookmarkAsync()
    {
        if (SelectedEvent is null) return;
        SelectedEvent.IsBookmarked = true;
        ForceRowRefresh(SelectedEvent);
        await PersistEventUpdateAsync(SelectedEvent);
    }

    /// <summary>
    /// Replace an item in-place to force the DataGrid to re-render the row,
    /// since EventEntry does not implement INotifyPropertyChanged.
    /// </summary>
    private void ForceRowRefresh(EventEntry entry)
    {
        var idx = Events.IndexOf(entry);
        if (idx < 0) return;
        Events.RemoveAt(idx);
        Events.Insert(idx, entry);
        SelectedEvent = entry;
    }

    private async Task PersistEventAsync(EventEntry entry)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            await repo.AddAsync(entry);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to persist event {EventId}", entry.EventId);
        }
    }

    private async Task PersistEventUpdateAsync(EventEntry entry)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            await repo.UpdateAsync(entry);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update event {EventId}", entry.EventId);
        }
    }

    // ── Tag management ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _newTagText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _selectedEventTags = [];

    partial void OnSelectedEventChanged(EventEntry? value)
    {
        SelectedEventTags.Clear();
        if (value is null) return;
        foreach (var tag in value.Tags)
            SelectedEventTags.Add(tag);
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (SelectedEvent is null) return;
        var tag = NewTagText.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        if (SelectedEvent.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return;

        SelectedEvent.Tags.Add(tag);
        SelectedEventTags.Add(tag);
        NewTagText = string.Empty;
        await PersistEventUpdateAsync(SelectedEvent);
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string tag)
    {
        if (SelectedEvent is null) return;
        SelectedEvent.Tags.Remove(tag);
        SelectedEventTags.Remove(tag);
        await PersistEventUpdateAsync(SelectedEvent);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
