using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using EventLogTracer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventLogTracer.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;

    // ── Export settings ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _exportPath = "exports";

    [ObservableProperty]
    private string _exportFormat = "CSV";

    [ObservableProperty]
    private DateTimeOffset? _exportDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _exportDateTo;

    [ObservableProperty]
    private string _exportLevelFilter = "All";

    [ObservableProperty]
    private int _exportEventCount;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    // ── App / database info ───────────────────────────────────────────────────

    [ObservableProperty]
    private string _appVersion = "1.0.0";

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _databaseSize = string.Empty;

    [ObservableProperty]
    private int _totalEventsInDb;

    [ObservableProperty]
    private int _totalSavedQueries;

    [ObservableProperty]
    private int _totalAlertRules;

    // ── Static option lists ───────────────────────────────────────────────────

    public List<string> FormatOptions      { get; } = ["CSV", "JSON", "XML"];
    public List<string> LevelFilterOptions { get; } = ["All", "Critical", "Error", "Warning", "Information", "Verbose"];

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _ = RefreshStatsAsync();
    }

    // ── Export commands ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PreviewExportAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            var events = await repo.GetByFilterAsync(BuildExportFilter());
            ExportEventCount = events.Count();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to preview export count");
        }
    }

    [RelayCommand]
    private async Task ExecuteExportAsync()
    {
        if (IsExporting) return;

        IsExporting = true;
        ExportStatusMessage = "Exporting…";

        try
        {
            Directory.CreateDirectory(ExportPath);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var ext       = ExportFormat.ToLower();
            var filename  = Path.Combine(ExportPath, $"events_{timestamp}.{ext}");

            List<EventEntry> events;
            using (var scope = _serviceProvider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();
                events = (await repo.GetByFilterAsync(BuildExportFilter())).ToList();
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IExportService>();
                switch (ExportFormat)
                {
                    case "CSV":  await svc.ExportToCsvAsync(events, filename);  break;
                    case "JSON": await svc.ExportToJsonAsync(events, filename); break;
                    case "XML":  await svc.ExportToXmlAsync(events, filename);  break;
                }
            }

            ExportEventCount  = events.Count;
            ExportStatusMessage = $"Exported {ExportEventCount} events to {filename}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export failed");
            ExportStatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        try
        {
            var fullPath = Path.GetFullPath(ExportPath);
            Directory.CreateDirectory(fullPath);
            Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open export folder");
        }
    }

    // ── Database commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshStatsAsync()
    {
        try
        {
            var dbFile   = new FileInfo("eventlogtracer.db");
            DatabasePath = dbFile.FullName;
            DatabaseSize = dbFile.Exists ? FormatSize(dbFile.Length) : "File not found";

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();

            TotalEventsInDb    = await db.EventEntries.CountAsync();
            TotalSavedQueries  = await db.SavedQueries.CountAsync();
            TotalAlertRules    = await db.AlertRules.CountAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh settings stats");
        }
    }

    [RelayCommand]
    private async Task VacuumDatabaseAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();
            await db.Database.ExecuteSqlRawAsync("VACUUM");
            ExportStatusMessage = "Database compacted successfully.";
            await RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database VACUUM failed");
            ExportStatusMessage = $"Compaction failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearOldEventsAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();

            var count = await db.EventEntries
                .Where(e => e.TimeCreated < cutoff)
                .ExecuteDeleteAsync();

            ExportStatusMessage = $"Deleted {count} events older than 30 days.";
            await RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear old events");
            ExportStatusMessage = $"Clear failed: {ex.Message}";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private EventFilter BuildExportFilter()
    {
        var filter = new EventFilter();

        if (ExportDateFrom.HasValue)
            filter.StartDate = ExportDateFrom.Value.UtcDateTime;

        if (ExportDateTo.HasValue)
            filter.EndDate = ExportDateTo.Value.UtcDateTime.Date.AddDays(1).AddTicks(-1);

        if (ExportLevelFilter != "All" &&
            Enum.TryParse<EventLevel>(ExportLevelFilter, ignoreCase: true, out var level))
        {
            filter.Levels = [level];
        }

        return filter;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024             => $"{bytes} B",
        < 1024 * 1024      => $"{bytes / 1024.0:F1} KB",
        _                  => $"{bytes / (1024.0 * 1024):F2} MB"
    };
}
