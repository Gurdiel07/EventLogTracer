using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using EventLogTracer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventLogTracer.App.ViewModels;

public partial class SearchViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISearchEngine _searchEngine;
    private CancellationTokenSource _validationCts = new();

    // ── Search bar ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private string _saveQueryName = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _isQueryValid;

    // ── Results ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<EventEntry> _searchResults = new();

    [ObservableProperty]
    private EventEntry? _selectedResult;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _resultCount;

    [ObservableProperty]
    private string _searchDuration = string.Empty;

    [ObservableProperty]
    private bool _hasSearched;

    // ── Date filters ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    // ── Saved queries ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<SavedQuery> _savedQueries = new();

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>True when a search returned zero results.</summary>
    public bool ShowNoResults => HasSearched && ResultCount == 0;

    /// <summary>Sidebar: at least one saved query exists.</summary>
    public bool HasSavedQueries => SavedQueries.Count > 0;

    /// <summary>Sidebar: empty state label visibility.</summary>
    public bool NoSavedQueries => SavedQueries.Count == 0;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SearchViewModel(IServiceProvider serviceProvider, ISearchEngine searchEngine)
    {
        _serviceProvider = serviceProvider;
        _searchEngine = searchEngine;

        // Update sidebar computed properties whenever the collection changes
        SavedQueries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedQueries));
            OnPropertyChanged(nameof(NoSavedQueries));
        };

        _ = LoadSavedQueriesAsync();
    }

    // ── Property-change hooks ─────────────────────────────────────────────────

    partial void OnQueryTextChanged(string value) => _ = ValidateWithDebounceAsync(value);

    partial void OnResultCountChanged(int value)  => OnPropertyChanged(nameof(ShowNoResults));
    partial void OnHasSearchedChanged(bool value) => OnPropertyChanged(nameof(ShowNoResults));

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits 300 ms of inactivity before validating the query, so we don't
    /// validate on every keypress.
    /// </summary>
    private async Task ValidateWithDebounceAsync(string query)
    {
        _validationCts.Cancel();
        _validationCts.Dispose();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;

        try
        {
            await Task.Delay(300, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ApplyValidation(query);
    }

    /// <summary>Validates immediately, bypassing the debounce delay.</summary>
    private void ApplyValidation(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ValidationMessage = string.Empty;
            IsQueryValid = false;
            return;
        }

        var result = _searchEngine.Validate(query);
        ValidationMessage = result.IsValid ? string.Empty : (result.ErrorMessage ?? "Invalid query");
        IsQueryValid = result.IsValid;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all events from DB, applies optional date range in the repository
    /// layer, then runs the advanced query engine in a background thread.
    /// </summary>
    [RelayCommand]
    private async Task ExecuteSearchAsync()
    {
        if (!IsQueryValid) return;

        IsSearching = true;
        SearchDuration = string.Empty;
        HasSearched = false;
        var sw = Stopwatch.StartNew();

        try
        {
            List<EventEntry> allEvents;

            using (var scope = _serviceProvider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();

                var filter = new EventFilter
                {
                    StartDate = DateFrom?.Date,
                    EndDate   = DateTo.HasValue
                                    ? DateTo.Value.Date.AddDays(1).AddTicks(-1)
                                    : null
                };

                allEvents = (await repo.GetByFilterAsync(filter)).ToList();
            }

            // SearchEngine is CPU-bound; offload to avoid blocking the UI thread
            var results = await Task.Run(() => _searchEngine.Search(allEvents, QueryText));

            sw.Stop();

            SearchResults = new ObservableCollection<EventEntry>(results);
            ResultCount   = results.Count;
            HasSearched   = true;
            SearchDuration = $"Found {ResultCount} result{(ResultCount == 1 ? "" : "s")} in {sw.ElapsedMilliseconds}ms";
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Search execution failed");
            SearchDuration = "Search failed — check logs for details";
            HasSearched = true;
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Saves the current query to the database using <see cref="SaveQueryName"/>
    /// as the display name (falls back to a truncated query + timestamp).
    /// </summary>
    [RelayCommand]
    private async Task SaveQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(QueryText)) return;

        var name = string.IsNullOrWhiteSpace(SaveQueryName)
            ? $"{(QueryText.Length > 30 ? QueryText[..30] + "…" : QueryText)} ({DateTime.Now:HH:mm})"
            : SaveQueryName.Trim();

        var savedQuery = new SavedQuery { Name = name, QueryExpression = QueryText };

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();
            db.SavedQueries.Add(savedQuery);
            await db.SaveChangesAsync();

            SavedQueries.Insert(0, savedQuery);
            SaveQueryName = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save query");
        }
    }

    /// <summary>
    /// Loads a saved query into the search bar, increments its usage stats,
    /// and executes the search immediately.
    /// </summary>
    [RelayCommand]
    private async Task LoadSavedQueryAsync(SavedQuery savedQuery)
    {
        // Cancel in-flight debounce so it doesn't overwrite our immediate validation
        _validationCts.Cancel();

        QueryText = savedQuery.QueryExpression;
        ApplyValidation(QueryText);

        savedQuery.UseCount++;
        savedQuery.LastUsedAt = DateTime.UtcNow;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();
            db.SavedQueries.Update(savedQuery);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update saved query usage stats");
        }

        if (IsQueryValid)
            await ExecuteSearchAsync();
    }

    /// <summary>Removes a saved query from the database and from the sidebar list.</summary>
    [RelayCommand]
    private async Task DeleteSavedQueryAsync(SavedQuery savedQuery)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();
            db.SavedQueries.Remove(savedQuery);
            await db.SaveChangesAsync();

            SavedQueries.Remove(savedQuery);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete saved query");
        }
    }

    /// <summary>Resets all search state to its initial values.</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        _validationCts.Cancel();
        QueryText       = string.Empty;
        SearchResults.Clear();
        SearchDuration  = string.Empty;
        SelectedResult  = null;
        ResultCount     = 0;
        HasSearched     = false;
        ValidationMessage = string.Empty;
        IsQueryValid    = false;
        SaveQueryName   = string.Empty;
    }

    // ── Bookmark support (mirrors EventViewerViewModel) ───────────────────────

    [RelayCommand]
    private async Task ToggleBookmarkAsync(EventEntry? entry)
    {
        if (entry is null) return;

        entry.IsBookmarked = !entry.IsBookmarked;
        if (!entry.IsBookmarked)
        {
            entry.BookmarkComment = null;
            entry.BookmarkColor   = null;
        }

        ForceRowRefresh(entry);
        await PersistEventUpdateAsync(entry);
    }

    [RelayCommand]
    private async Task SaveBookmarkAsync()
    {
        if (SelectedResult is null) return;
        SelectedResult.IsBookmarked = true;
        ForceRowRefresh(SelectedResult);
        await PersistEventUpdateAsync(SelectedResult);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the item in-place to force the DataGrid to re-render the row,
    /// since EventEntry does not implement INotifyPropertyChanged.
    /// </summary>
    private void ForceRowRefresh(EventEntry entry)
    {
        var idx = SearchResults.IndexOf(entry);
        if (idx < 0) return;
        SearchResults.RemoveAt(idx);
        SearchResults.Insert(idx, entry);
        SelectedResult = entry;
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
            Log.Error(ex, "Failed to update event bookmark");
        }
    }

    private async Task LoadSavedQueriesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();

            var queries = await db.SavedQueries
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync();

            SavedQueries.Clear();
            foreach (var q in queries)
                SavedQueries.Add(q);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load saved queries");
        }
    }

    // ── Tag management ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _newTagText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _selectedEventTags = [];

    partial void OnSelectedResultChanged(EventEntry? value)
    {
        SelectedEventTags.Clear();
        if (value is null) return;
        foreach (var tag in value.Tags)
            SelectedEventTags.Add(tag);
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (SelectedResult is null) return;
        var tag = NewTagText.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        if (SelectedResult.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return;

        SelectedResult.Tags.Add(tag);
        SelectedEventTags.Add(tag);
        NewTagText = string.Empty;
        await PersistEventUpdateAsync(SelectedResult);
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string tag)
    {
        if (SelectedResult is null) return;
        SelectedResult.Tags.Remove(tag);
        SelectedEventTags.Remove(tag);
        await PersistEventUpdateAsync(SelectedResult);
    }
}
