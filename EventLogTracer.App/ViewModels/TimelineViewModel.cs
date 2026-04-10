using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SkiaSharp;

namespace EventLogTracer.App.ViewModels;

public class TimelineEventItem
{
    public Guid EventId { get; set; }
    public DateTime TimeCreated { get; set; }
    public string Level { get; set; } = string.Empty;
    public string LevelColor { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string ShortMessage { get; set; } = string.Empty;
    public int EventCode { get; set; }
    public string LogName { get; set; } = string.Empty;
    public double YPosition { get; set; }
}

public class TimelinePoint
{
    public DateTime Time { get; set; }
    public double LevelY { get; set; }
    public EventEntry OriginalEntry { get; set; } = new();
}

public partial class TimelineViewModel : ViewModelBase
{
    private const double MinZoomLevel = 0.5;
    private const double MaxZoomLevel = 10.0;
    private static readonly TimeSpan BaseRange = TimeSpan.FromHours(24);

    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Guid, EventEntry> _entryLookup = new();

    private static readonly SKColor CriticalColor = Hex("#E81123");
    private static readonly SKColor ErrorColor = Hex("#D13438");
    private static readonly SKColor WarningColor = Hex("#FF8C00");
    private static readonly SKColor InformationColor = Hex("#0078D4");
    private static readonly SKColor VerboseColor = Hex("#767676");
    private static readonly SKColor AxisTextColor = Hex("#999999");
    private static readonly SKColor GridColor = Hex("#333333");
    private static readonly SKColor LabelColor = Hex("#CCCCCC");

    [ObservableProperty]
    private ObservableCollection<TimelineEventItem> _timelineEvents = [];

    [ObservableProperty]
    private TimelineEventItem? _selectedTimelineEvent;

    [ObservableProperty]
    private EventEntry? _selectedEventDetail;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private DateTime _visibleRangeStart = DateTime.UtcNow.AddHours(-24);

    [ObservableProperty]
    private DateTime _visibleRangeEnd = DateTime.UtcNow;

    [ObservableProperty]
    private string _selectedLevel = "All";

    [ObservableProperty]
    private string _selectedLogName = "All";

    [ObservableProperty]
    private int _totalVisible;

    [ObservableProperty]
    private ISeries[] _timelineSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] _timelineXAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _timelineYAxes = Array.Empty<Axis>();

    public List<string> LevelOptions { get; } =
        ["All", "Critical", "Error", "Warning", "Information", "Verbose"];

    public List<string> LogNameOptions { get; } =
        ["All", "Application", "Security", "System", "Setup", "ForwardedEvents"];

    public TimelineViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        ConfigureAxes();
        _ = LoadTimelineAsync();
    }

    partial void OnSelectedLevelChanged(string value) => _ = LoadTimelineAsync();
    partial void OnSelectedLogNameChanged(string value) => _ = LoadTimelineAsync();

    partial void OnSelectedTimelineEventChanged(TimelineEventItem? value)
    {
        if (value is null)
        {
            SelectedEventDetail = null;
            return;
        }

        if (_entryLookup.TryGetValue(value.EventId, out var entry))
            SelectedEventDetail = entry;
    }

    [RelayCommand]
    private async Task LoadTimelineAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();

            var filter = new EventFilter
            {
                StartDate = VisibleRangeStart.ToUniversalTime(),
                EndDate = VisibleRangeEnd.ToUniversalTime()
            };

            if (SelectedLevel != "All" &&
                Enum.TryParse<EventLevel>(SelectedLevel, ignoreCase: true, out var level))
            {
                filter.Levels = [level];
            }

            if (SelectedLogName != "All")
                filter.LogNames = [SelectedLogName];

            var entries = (await repo.GetByFilterAsync(filter))
                .OrderBy(e => e.TimeCreated)
                .ToList();

            RebuildTimeline(entries);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load timeline data");
            TimelineEvents = [];
            TimelineSeries = Array.Empty<ISeries>();
            TotalVisible = 0;
            SelectedTimelineEvent = null;
            SelectedEventDetail = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ZoomInAsync()
    {
        ApplyZoom(ZoomLevel * 1.5);
        await LoadTimelineAsync();
    }

    [RelayCommand]
    private async Task ZoomOutAsync()
    {
        ApplyZoom(ZoomLevel / 1.5);
        await LoadTimelineAsync();
    }

    [RelayCommand]
    private async Task ResetZoomAsync()
    {
        ZoomLevel = 1.0;
        VisibleRangeStart = DateTime.UtcNow.AddHours(-24);
        VisibleRangeEnd = DateTime.UtcNow;
        ConfigureAxes();
        await LoadTimelineAsync();
    }

    [RelayCommand]
    private async Task GoToNowAsync()
    {
        var duration = CurrentVisibleDuration();
        var half = TimeSpan.FromTicks(duration.Ticks / 2);
        var now = DateTime.UtcNow;

        VisibleRangeStart = now - half;
        VisibleRangeEnd = now + half;
        ConfigureAxes();
        await LoadTimelineAsync();
    }

    [RelayCommand]
    private async Task NavigateForwardAsync()
    {
        ShiftVisibleRange(0.25);
        await LoadTimelineAsync();
    }

    [RelayCommand]
    private async Task NavigateBackwardAsync()
    {
        ShiftVisibleRange(-0.25);
        await LoadTimelineAsync();
    }

    [RelayCommand]
    private void SelectChartPoint(ChartPoint? chartPoint)
    {
        if (chartPoint?.Context?.DataSource is not TimelinePoint point)
            return;

        SelectedEventDetail = point.OriginalEntry;
        SelectedTimelineEvent = TimelineEvents.FirstOrDefault(e => e.EventId == point.OriginalEntry.Id);
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

        await PersistEventUpdateAsync(entry);
        SelectedEventDetail = entry;
    }

    [RelayCommand]
    private async Task SaveBookmarkAsync()
    {
        if (SelectedEventDetail is null) return;

        SelectedEventDetail.IsBookmarked = true;
        await PersistEventUpdateAsync(SelectedEventDetail);
    }

    private void RebuildTimeline(List<EventEntry> entries)
    {
        _entryLookup.Clear();
        foreach (var entry in entries)
            _entryLookup[entry.Id] = entry;

        TimelineEvents = new ObservableCollection<TimelineEventItem>(
            entries.Select(entry => new TimelineEventItem
            {
                EventId = entry.Id,
                TimeCreated = entry.TimeCreated,
                Level = entry.Level.ToString(),
                LevelColor = GetLevelColorHex(entry.Level),
                Source = entry.Source,
                ShortMessage = Truncate(entry.Message, 100),
                EventCode = entry.EventId,
                LogName = entry.LogName,
                YPosition = GetLevelLane(entry.Level)
            }));

        TotalVisible = TimelineEvents.Count;
        BuildTimelineSeries(entries);

        if (SelectedEventDetail is not null &&
            _entryLookup.TryGetValue(SelectedEventDetail.Id, out var refreshedEntry))
        {
            SelectedEventDetail = refreshedEntry;
            SelectedTimelineEvent = TimelineEvents.FirstOrDefault(e => e.EventId == refreshedEntry.Id);
        }
        else
        {
            SelectedEventDetail = null;
            SelectedTimelineEvent = null;
        }
    }

    private void BuildTimelineSeries(List<EventEntry> entries)
    {
        TimelineSeries =
        [
            BuildScatterSeries("Critical", EventLevel.Critical, CriticalColor, entries),
            BuildScatterSeries("Error", EventLevel.Error, ErrorColor, entries),
            BuildScatterSeries("Warning", EventLevel.Warning, WarningColor, entries),
            BuildScatterSeries("Information", EventLevel.Information, InformationColor, entries),
            BuildScatterSeries("Verbose", EventLevel.Verbose, VerboseColor, entries)
        ];

        ConfigureAxes();
    }

    private ISeries BuildScatterSeries(
        string name,
        EventLevel level,
        SKColor color,
        List<EventEntry> entries)
    {
        var points = entries
            .Where(e => e.Level == level)
            .Select(e => new TimelinePoint
            {
                Time = e.TimeCreated.ToUniversalTime(),
                LevelY = GetLevelLane(level),
                OriginalEntry = e
            })
            .ToList();

        return new ScatterSeries<TimelinePoint>
        {
            Name = name,
            Values = points,
            GeometrySize = 10,
            Fill = new SolidColorPaint(color),
            Stroke = new SolidColorPaint(color) { StrokeThickness = 1 },
            Mapping = (model, index) => new Coordinate(model.LevelY, model.Time.Ticks)
        };
    }

    private void ConfigureAxes()
    {
        var range = CurrentVisibleDuration();
        var minStep = GetMinStep(range);

        TimelineXAxes =
        [
            new Axis
            {
                Labeler = value => new DateTime((long)value, DateTimeKind.Utc).ToLocalTime().ToString("MMM dd HH:mm"),
                MinLimit = VisibleRangeStart.ToUniversalTime().Ticks,
                MaxLimit = VisibleRangeEnd.ToUniversalTime().Ticks,
                MinStep = minStep,
                UnitWidth = TimeSpan.FromHours(1).Ticks,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(AxisTextColor),
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 0.5f }
            }
        ];

        TimelineYAxes =
        [
            new Axis
            {
                Labels = ["Verbose", "Information", "Warning", "Error", "Critical"],
                MinLimit = -0.5,
                MaxLimit = 4.5,
                MinStep = 1,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(LabelColor),
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 0.5f }
            }
        ];
    }

    private void ApplyZoom(double newZoomLevel)
    {
        var clampedZoom = Math.Clamp(newZoomLevel, MinZoomLevel, MaxZoomLevel);
        var center = VisibleRangeStart + TimeSpan.FromTicks((VisibleRangeEnd - VisibleRangeStart).Ticks / 2);
        var newDuration = TimeSpan.FromTicks((long)(BaseRange.Ticks / clampedZoom));
        var half = TimeSpan.FromTicks(newDuration.Ticks / 2);

        ZoomLevel = clampedZoom;
        VisibleRangeStart = center - half;
        VisibleRangeEnd = center + half;
        ConfigureAxes();
    }

    private void ShiftVisibleRange(double factor)
    {
        var shift = TimeSpan.FromTicks((long)(CurrentVisibleDuration().Ticks * factor));
        VisibleRangeStart = VisibleRangeStart.Add(shift);
        VisibleRangeEnd = VisibleRangeEnd.Add(shift);
        ConfigureAxes();
    }

    private TimeSpan CurrentVisibleDuration() => VisibleRangeEnd - VisibleRangeStart;

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

    partial void OnSelectedEventDetailChanged(EventEntry? value)
    {
        SelectedEventTags.Clear();
        if (value is null) return;
        foreach (var tag in value.Tags)
            SelectedEventTags.Add(tag);
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (SelectedEventDetail is null) return;
        var tag = NewTagText.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        if (SelectedEventDetail.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return;

        SelectedEventDetail.Tags.Add(tag);
        SelectedEventTags.Add(tag);
        NewTagText = string.Empty;
        await PersistEventUpdateAsync(SelectedEventDetail);
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string tag)
    {
        if (SelectedEventDetail is null) return;
        SelectedEventDetail.Tags.Remove(tag);
        SelectedEventTags.Remove(tag);
        await PersistEventUpdateAsync(SelectedEventDetail);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static double GetLevelLane(EventLevel level) => level switch
    {
        EventLevel.Critical => 4,
        EventLevel.Error => 3,
        EventLevel.Warning => 2,
        EventLevel.Information => 1,
        EventLevel.Verbose => 0,
        _ => 0
    };

    private static string GetLevelColorHex(EventLevel level) => level switch
    {
        EventLevel.Critical => "#E81123",
        EventLevel.Error => "#D13438",
        EventLevel.Warning => "#FF8C00",
        EventLevel.Information => "#0078D4",
        EventLevel.Verbose => "#767676",
        _ => "#767676"
    };

    private static double GetMinStep(TimeSpan range)
    {
        if (range.TotalHours <= 6) return TimeSpan.FromMinutes(30).Ticks;
        if (range.TotalHours <= 24) return TimeSpan.FromHours(2).Ticks;
        if (range.TotalHours <= 48) return TimeSpan.FromHours(4).Ticks;
        return TimeSpan.FromHours(8).Ticks;
    }

    private static SKColor Hex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex[0..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return new SKColor(r, g, b);
    }
}
