using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using Serilog;

namespace EventLogTracer.Infrastructure.Services;

/// <summary>
/// Generates realistic fake Windows Event Log entries for development and testing.
/// Fires a random event every 1–3 seconds while monitoring is active.
/// </summary>
public sealed class MockEventLogReader : IEventLogReader, IDisposable
{
    private static readonly Random _rng = new();
    private readonly ILogger _logger = Log.ForContext<MockEventLogReader>();
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    public bool IsMonitoring => _monitoringTask is { IsCompleted: false };

    // -------------------------------------------------------------------------
    // Static data pools for realistic event generation
    // -------------------------------------------------------------------------

    private static readonly string[] LogNames =
        ["Application", "Security", "System", "Setup", "ForwardedEvents"];

    private static readonly string[] MachineNames =
        ["DESKTOP-ABC123", "SERVER-DC01", "LAPTOP-XYZ789", "WEB-SERVER-01", "SQL-SERVER-02"];

    private static readonly (int Id, EventLevel Level, string Source, string Message)[] EventTemplates =
    [
        (4624, EventLevel.Information, "Microsoft-Windows-Security-Auditing",
            "An account was successfully logged on. Subject: Security ID: S-1-5-18. Logon Type: 3."),
        (4625, EventLevel.Warning, "Microsoft-Windows-Security-Auditing",
            "An account failed to log on. Failure Reason: Unknown user name or bad password."),
        (4648, EventLevel.Information, "Microsoft-Windows-Security-Auditing",
            "A logon was attempted using explicit credentials. Subject: SYSTEM."),
        (4672, EventLevel.Information, "Microsoft-Windows-Security-Auditing",
            "Special privileges assigned to new logon. Privileges: SeSecurityPrivilege."),
        (4688, EventLevel.Information, "Microsoft-Windows-Security-Auditing",
            "A new process has been created. Process Name: C:\\Windows\\System32\\svchost.exe."),
        (4720, EventLevel.Warning, "Microsoft-Windows-Security-Auditing",
            "A user account was created. New Account Name: TempUser."),
        (7036, EventLevel.Information, "Service Control Manager",
            "The Windows Update service entered the running state."),
        (7040, EventLevel.Warning, "Service Control Manager",
            "The start type of the Background Intelligent Transfer Service was changed from disabled to auto start."),
        (1001, EventLevel.Error, "Windows Error Reporting",
            "Fault bucket 123456789, type 5. Event Name: BEX64. Response: Not available."),
        (1000, EventLevel.Error, "Application Error",
            "Faulting application name: explorer.exe, version: 10.0.22621.1, time stamp: 0x00000000."),
        (6013, EventLevel.Information, "EventLog",
            "The system uptime is 345678 seconds."),
        (6008, EventLevel.Critical, "EventLog",
            "The previous system shutdown at 3:47:22 AM on 4/9/2026 was unexpected."),
        (41,   EventLevel.Critical, "Microsoft-Windows-Kernel-Power",
            "The system has rebooted without cleanly shutting down first."),
        (55,   EventLevel.Error,   "Ntfs",
            "The file system structure on the disk is corrupt and unusable. Run the Chkdsk utility on volume C:."),
        (10016, EventLevel.Warning, "Microsoft-Windows-DistributedCOM",
            "The application-specific permission settings do not grant Local Activation permission for the COM Server application."),
        (10010, EventLevel.Error,  "Microsoft-Windows-DistributedCOM",
            "The server {1B7CD997-E5FF-4932-A7A6-2A9E636DA385} did not register with DCOM within the required timeout."),
        (4098, EventLevel.Warning, "Group Policy",
            "The user 'Default Domain Policy' preference item in the 'Startup Scripts {GUID}' Group Policy Object did not apply."),
        (1102, EventLevel.Warning, "Microsoft-Windows-Eventlog",
            "The audit log was cleared. Subject: Security ID: S-1-5-21-xxx. Account Name: Administrator."),
        (100,  EventLevel.Information, "Microsoft-Windows-TaskScheduler",
            "Task Scheduler started the '{0BC7CF7A-6DDF-4B05-8C7C-AB0B4C5B0FD1}' instance of the '\\Microsoft\\Windows\\UpdateOrchestrator\\Schedule Scan' task."),
        (4776, EventLevel.Information, "Microsoft-Windows-Security-Auditing",
            "The computer attempted to validate the credentials for an account. Authentication Package: MICROSOFT_AUTHENTICATION_PACKAGE_V1_0."),
    ];

    private static readonly string[] Tags =
        ["auth", "network", "disk", "memory", "service", "scheduled-task", "security", "update", "startup", "crash"];

    // -------------------------------------------------------------------------
    // IEventLogReader
    // -------------------------------------------------------------------------

    public Task<IEnumerable<EventEntry>> GetEventsAsync(
        EventFilter filter,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("MockEventLogReader: GetEventsAsync called with filter {@Filter}", filter);

        var count = _rng.Next(20, 100);
        var entries = Enumerable.Range(0, count)
            .Select(_ => GenerateRandomEvent())
            .Where(e => MatchesFilter(e, filter))
            .ToList();

        return Task.FromResult<IEnumerable<EventEntry>>(entries);
    }

    public void StartMonitoring(Action<EventEntry> onEventReceived)
    {
        if (IsMonitoring)
        {
            _logger.Warning("MockEventLogReader: Monitoring is already active.");
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _monitoringTask = Task.Run(async () =>
        {
            _logger.Information("MockEventLogReader: Monitoring started.");
            while (!token.IsCancellationRequested)
            {
                var delayMs = _rng.Next(1000, 3001);
                await Task.Delay(delayMs, token).ConfigureAwait(false);

                if (!token.IsCancellationRequested)
                {
                    var entry = GenerateRandomEvent();
                    _logger.Verbose("MockEventLogReader: Emitting event {EventId} from {Source}", entry.EventId, entry.Source);
                    onEventReceived(entry);
                }
            }
        }, token);
    }

    public void StopMonitoring()
    {
        _cts?.Cancel();
        _logger.Information("MockEventLogReader: Monitoring stopped.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static EventEntry GenerateRandomEvent()
    {
        var template = EventTemplates[_rng.Next(EventTemplates.Length)];
        var tagCount = _rng.Next(0, 3);
        var tags = Tags.OrderBy(_ => _rng.Next()).Take(tagCount).ToList();

        return new EventEntry
        {
            Id = Guid.NewGuid(),
            EventId = template.Id,
            Level = template.Level,
            Source = template.Source,
            LogName = LogNames[_rng.Next(LogNames.Length)],
            TimeCreated = DateTime.UtcNow.AddSeconds(-_rng.Next(0, 3600)),
            MachineName = MachineNames[_rng.Next(MachineNames.Length)],
            Message = template.Message,
            Category = _rng.Next(2) == 0 ? PickCategory() : null,
            UserId = _rng.Next(3) == 0 ? $"S-1-5-21-{_rng.Next(100000, 999999)}" : null,
            Keywords = _rng.Next(2) == 0 ? "Audit Success" : "Audit Failure",
            Tags = tags
        };
    }

    private static string PickCategory() =>
        _rng.Next(5) switch
        {
            0 => "Process Creation",
            1 => "Logon",
            2 => "Object Access",
            3 => "Policy Change",
            _ => "System"
        };

    private static bool MatchesFilter(EventEntry entry, EventFilter filter)
    {
        if (filter.Levels?.Count > 0 && !filter.Levels.Contains(entry.Level))
            return false;

        if (filter.LogNames?.Count > 0 && !filter.LogNames.Contains(entry.LogName))
            return false;

        if (filter.Sources?.Count > 0 && !filter.Sources.Contains(entry.Source))
            return false;

        if (filter.EventIds?.Count > 0 && !filter.EventIds.Contains(entry.EventId))
            return false;

        if (filter.StartDate.HasValue && entry.TimeCreated < filter.StartDate.Value)
            return false;

        if (filter.EndDate.HasValue && entry.TimeCreated > filter.EndDate.Value)
            return false;

        if (!string.IsNullOrWhiteSpace(filter.SearchText) &&
            !entry.Message.Contains(filter.SearchText, StringComparison.OrdinalIgnoreCase) &&
            !entry.Source.Contains(filter.SearchText, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
