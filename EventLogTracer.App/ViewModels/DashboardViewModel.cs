using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SkiaSharp;
using AnomalyDetectorService = EventLogTracer.Core.Interfaces.IAnomalyDetector;

namespace EventLogTracer.App.ViewModels;

// ── Auxiliary models ──────────────────────────────────────────────────────────

public class SourceStat
{
    public string Source { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public class LevelStat
{
    public string Level { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Color { get; set; } = string.Empty;
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventLogReader _eventLogReader;
    private readonly AnomalyDetectorService _anomalyDetector;
    private Timer? _autoRefreshTimer;

    // ── Legend paint (shared by charts) ──────────────────────────────────────

    public SolidColorPaint LegendTextPaint { get; } = new SolidColorPaint(new SKColor(0xCC, 0xCC, 0xCC));

    // ── Level counters ────────────────────────────────────────────────────────

    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _informationCount;
    [ObservableProperty] private int _verboseCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isLoading;

    public bool HasData => TotalCount > 0;
    public bool NoData  => TotalCount == 0;

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(NoData));
    }

    // ── Tabular data ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<SourceStat> _topSources = new();

    [ObservableProperty]
    private ObservableCollection<LevelStat> _levelDistribution = new();

    // ── Chart series ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private ISeries[] _levelPieSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private ISeries[] _eventsTimelineSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private ISeries[] _topSourcesBarSeries = Array.Empty<ISeries>();

    // ── Chart axes ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private Axis[] _timelineXAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _timelineYAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _sourcesXAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _sourcesYAxes = Array.Empty<Axis>();

    // ── Correlations ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<EventCorrelation> _correlations = [];

    [ObservableProperty]
    private int _correlationCount;

    public bool HasCorrelations => CorrelationCount > 0;
    public bool NoCorrelations  => CorrelationCount == 0;

    partial void OnCorrelationCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasCorrelations));
        OnPropertyChanged(nameof(NoCorrelations));
    }

    // ── Anomaly detection ─────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<AnomalyResult> _anomalyResults = [];

    [ObservableProperty]
    private bool _isDetectingAnomalies;

    [ObservableProperty]
    private int _anomalyCount;

    public bool HasAnomalies => AnomalyCount > 0;
    public bool NoAnomalies  => AnomalyCount == 0;

    partial void OnAnomalyCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasAnomalies));
        OnPropertyChanged(nameof(NoAnomalies));
    }

    // ── Static colors ─────────────────────────────────────────────────────────

    private static readonly SKColor CriticalColor    = Hex("#E81123");
    private static readonly SKColor ErrorColor       = Hex("#D13438");
    private static readonly SKColor WarningColor     = Hex("#FF8C00");
    private static readonly SKColor InfoColor        = Hex("#0078D4");
    private static readonly SKColor VerboseColor     = Hex("#767676");
    private static readonly SKColor TextColor        = Hex("#999999");
    private static readonly SKColor GridColor        = Hex("#333333");
    private static readonly SKColor LabelColor       = Hex("#CCCCCC");

    // ── Constructor ───────────────────────────────────────────────────────────

    public DashboardViewModel(
        IServiceProvider serviceProvider,
        IEventLogReader eventLogReader,
        AnomalyDetectorService anomalyDetector)
    {
        _serviceProvider  = serviceProvider;
        _eventLogReader   = eventLogReader;
        _anomalyDetector  = anomalyDetector;
        _ = InitializeAsync();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDashboardDataAsync();

    [RelayCommand]
    private async Task DetectAnomaliesAsync()
    {
        List<EventEntry> events;
        using (var scope = _serviceProvider.CreateScope())
        {
            var repo   = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            var filter = new EventFilter { StartDate = DateTime.UtcNow.AddHours(-24) };
            events = (await repo.GetByFilterAsync(filter)).ToList();
        }
        await RunAnomalyDetectionAsync(events);
    }

    // ── Initialization ────────────────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        await LoadDashboardDataAsync();

        // Auto-refresh every 30 seconds while the dashboard is alive
        _autoRefreshTimer = new Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.Post(
                     () => _ = LoadDashboardDataAsync()),
            state: null,
            dueTime:  TimeSpan.FromSeconds(30),
            period:   TimeSpan.FromSeconds(30));
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task LoadDashboardDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            List<EventEntry> events;

            using (var scope = _serviceProvider.CreateScope())
            {
                var repo   = scope.ServiceProvider.GetRequiredService<IEventRepository>();
                var filter = new EventFilter { StartDate = DateTime.UtcNow.AddHours(-24) };
                events = (await repo.GetByFilterAsync(filter)).ToList();
            }

            BuildCounters(events);
            BuildLevelDistribution();
            BuildPieSeries();
            BuildTimelineSeries(events);
            BuildSourceBarSeries(events);

            // Anomaly detection is CPU/ML-heavy — run in background
            _ = RunAnomalyDetectionAsync(events);

            // Correlation analysis — run in background after data is loaded
            _ = RunCorrelationAsync(events);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load dashboard data");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Anomaly detection runner ──────────────────────────────────────────────

    private async Task RunAnomalyDetectionAsync(List<EventEntry> events)
    {
        if (IsDetectingAnomalies) return;

        IsDetectingAnomalies = true;
        try
        {
            var anomalies = (await _anomalyDetector.DetectAnomaliesAsync(events)).ToList();

            AnomalyResults.Clear();
            foreach (var a in anomalies)
                AnomalyResults.Add(a);
            AnomalyCount = AnomalyResults.Count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Dashboard anomaly detection failed");
        }
        finally
        {
            IsDetectingAnomalies = false;
        }
    }

    private async Task RunCorrelationAsync(List<EventEntry> events)
    {
        try
        {
            List<EventCorrelation> correlations;
            using (var scope = _serviceProvider.CreateScope())
            {
                var correlator = scope.ServiceProvider.GetRequiredService<IEventCorrelator>();
                correlations = (await correlator.CorrelateEventsAsync(events)).ToList();
            }

            Correlations.Clear();
            foreach (var c in correlations)
                Correlations.Add(c);
            CorrelationCount = Correlations.Count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Dashboard event correlation failed");
        }
    }

    // ── Build helpers ─────────────────────────────────────────────────────────

    private void BuildCounters(List<EventEntry> events)
    {
        CriticalCount    = events.Count(e => e.Level == EventLevel.Critical);
        ErrorCount       = events.Count(e => e.Level == EventLevel.Error);
        WarningCount     = events.Count(e => e.Level == EventLevel.Warning);
        InformationCount = events.Count(e => e.Level == EventLevel.Information);
        VerboseCount     = events.Count(e => e.Level == EventLevel.Verbose);
        TotalCount       = events.Count;
    }

    private void BuildLevelDistribution()
    {
        LevelDistribution = new ObservableCollection<LevelStat>
        {
            new() { Level = "Critical",    Count = CriticalCount,    Color = "#E81123" },
            new() { Level = "Error",       Count = ErrorCount,       Color = "#D13438" },
            new() { Level = "Warning",     Count = WarningCount,     Color = "#FF8C00" },
            new() { Level = "Information", Count = InformationCount, Color = "#0078D4" },
            new() { Level = "Verbose",     Count = VerboseCount,     Color = "#767676" },
        };
    }

    private void BuildPieSeries()
    {
        var series = new List<ISeries>();

        void AddSlice(string name, int count, SKColor color)
        {
            // Include 0-count entries as invisible so the legend still shows all levels
            series.Add(new PieSeries<double>
            {
                Name      = name,
                Values    = new[] { (double)(count > 0 ? count : 0) },
                Fill      = new SolidColorPaint(color),
                IsVisible = count > 0,
            });
        }

        AddSlice("Critical",    CriticalCount,    CriticalColor);
        AddSlice("Error",       ErrorCount,       ErrorColor);
        AddSlice("Warning",     WarningCount,     WarningColor);
        AddSlice("Information", InformationCount, InfoColor);
        AddSlice("Verbose",     VerboseCount,     VerboseColor);

        // Placeholder when DB is empty so the chart doesn't render blank
        if (TotalCount == 0)
        {
            series.Clear();
            series.Add(new PieSeries<double>
            {
                Name   = "No data",
                Values = new[] { 1.0 },
                Fill   = new SolidColorPaint(new SKColor(0x44, 0x44, 0x44)),
            });
        }

        LevelPieSeries = series.ToArray();
    }

    private void BuildTimelineSeries(List<EventEntry> events)
    {
        // Fill all 24 slots (including empty hours) for a continuous time axis
        var now   = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
                        .AddHours(-23);

        var hourlyMap = events
            .GroupBy(e =>
            {
                var t = e.TimeCreated.ToUniversalTime();
                return new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);
            })
            .ToDictionary(g => g.Key, g => g.Count());

        var points = Enumerable.Range(0, 24)
            .Select(i =>
            {
                var hour = start.AddHours(i);
                return new DateTimePoint(hour, hourlyMap.TryGetValue(hour, out var c) ? c : 0);
            })
            .ToList();

        EventsTimelineSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values          = points,
                Name            = "Events/hour",
                Stroke          = new SolidColorPaint(InfoColor) { StrokeThickness = 2 },
                Fill            = null,
                GeometrySize    = 5,
                GeometryFill    = new SolidColorPaint(InfoColor),
                GeometryStroke  = null,
                LineSmoothness  = 0.4,
            }
        };

        TimelineXAxes = new[]
        {
            new Axis
            {
                Labeler         = value => new DateTime((long)value).ToLocalTime().ToString("HH:mm"),
                UnitWidth       = TimeSpan.FromHours(1).Ticks,
                MinStep         = TimeSpan.FromHours(2).Ticks,
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(TextColor),
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 0.5f },
            }
        };

        TimelineYAxes = new[]
        {
            new Axis
            {
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(TextColor),
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 0.5f },
                MinStep         = 1,
            }
        };
    }

    private void BuildSourceBarSeries(List<EventEntry> events)
    {
        var groups = events
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Source) ? "(unknown)" : e.Source)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => (Source: g.Key, Count: g.Count()))
            .ToList();

        TopSources = new ObservableCollection<SourceStat>(
            groups.Select(g => new SourceStat
            {
                Source     = g.Source,
                Count      = g.Count,
                Percentage = TotalCount > 0 ? g.Count * 100.0 / TotalCount : 0,
            }));

        if (!groups.Any())
        {
            TopSourcesBarSeries = Array.Empty<ISeries>();
            SourcesYAxes        = Array.Empty<Axis>();
            SourcesXAxes        = Array.Empty<Axis>();
            return;
        }

        TopSourcesBarSeries = new ISeries[]
        {
            new RowSeries<int>
            {
                Values      = groups.Select(g => g.Count).ToArray(),
                Name        = "Event Count",
                Fill        = new SolidColorPaint(InfoColor),
                MaxBarWidth = 24,
                Padding     = 2,
            }
        };

        SourcesYAxes = new[]
        {
            new Axis
            {
                Labels          = groups.Select(g => g.Source).ToArray(),
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(LabelColor),
                SeparatorsPaint = null,
            }
        };

        SourcesXAxes = new[]
        {
            new Axis
            {
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(TextColor),
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 0.5f },
                MinStep         = 1,
            }
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SKColor Hex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex[0..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return new SKColor(r, g, b);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _autoRefreshTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
