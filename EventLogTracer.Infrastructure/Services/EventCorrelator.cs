using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using Serilog;

namespace EventLogTracer.Infrastructure.Services;

/// <summary>
/// Correlates events using four complementary strategies, then deduplicates
/// so that each event belongs to at most one correlation (highest-priority wins).
///
/// Priority ranking (highest → lowest): AuthSequence > ErrorCascade > ServiceLifecycle > Burst
/// </summary>
public class EventCorrelator : IEventCorrelator
{
    // ── Auth event IDs (Windows Security log) ────────────────────────────────
    private static readonly HashSet<int> AuthEventIds = [4624, 4625, 4648, 4672, 4776];

    // ── Service lifecycle event IDs ───────────────────────────────────────────
    private static readonly HashSet<int> ServiceEventIds = [7036, 7040, 1000, 1001];

    // ── Error/Warning levels for cascade detection ────────────────────────────
    private static readonly HashSet<EventLevel> ErrorLevels =
        [EventLevel.Critical, EventLevel.Error];

    private static readonly HashSet<EventLevel> CascadeLevels =
        [EventLevel.Critical, EventLevel.Error, EventLevel.Warning];

    // ── Correlation type priority (higher = more specific) ───────────────────
    private static readonly Dictionary<string, int> TypePriority = new()
    {
        ["AuthSequence"]     = 4,
        ["ErrorCascade"]     = 3,
        ["ServiceLifecycle"] = 2,
        ["Burst"]            = 1,
    };

    // ── Minimum event count per correlation type ──────────────────────────────
    private static readonly Dictionary<string, int> MinSize = new()
    {
        ["Burst"]            = 3,
        ["ErrorCascade"]     = 2,
        ["AuthSequence"]     = 2,
        ["ServiceLifecycle"] = 2,
    };

    // ── Entry point ───────────────────────────────────────────────────────────

    public async Task<IEnumerable<EventCorrelation>> CorrelateEventsAsync(
        IList<EventEntry> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0) return [];

        try
        {
            var tasks = new[]
            {
                Task.Run(() => DetectBursts(events),           cancellationToken),
                Task.Run(() => DetectErrorCascades(events),    cancellationToken),
                Task.Run(() => DetectAuthSequences(events),    cancellationToken),
                Task.Run(() => DetectServiceLifecycle(events), cancellationToken),
            };

            var batches = await Task.WhenAll(tasks);
            return Deduplicate(batches.SelectMany(b => b));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Event correlation failed — returning empty result");
            return [];
        }
    }

    // ── A: Temporal Burst — same source, 30-second window, ≥3 events ──────────

    private static List<EventCorrelation> DetectBursts(IList<EventEntry> events)
    {
        var results = new List<EventCorrelation>();
        var window  = TimeSpan.FromSeconds(30);

        var bySource = events
            .GroupBy(e => e.Source)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() >= 3);

        foreach (var srcGroup in bySource)
        {
            var sorted = srcGroup.OrderBy(e => e.TimeCreated).ToList();
            var i = 0;

            while (i < sorted.Count)
            {
                var anchor = sorted[i];
                var burst  = sorted
                    .Skip(i)
                    .TakeWhile(e => e.TimeCreated - anchor.TimeCreated <= window)
                    .ToList();

                if (burst.Count >= 3)
                {
                    results.Add(new EventCorrelation
                    {
                        Name            = $"Event Burst: {srcGroup.Key}",
                        CorrelationType = "Burst",
                        Description     = $"{burst.Count} events from '{srcGroup.Key}' within {window.TotalSeconds:F0}s",
                        EventEntries    = burst,
                        DetectedAt      = DateTime.UtcNow,
                    });
                    i += burst.Count; // advance past the burst
                }
                else
                {
                    i++;
                }
            }
        }

        return results;
    }

    // ── B: Error Cascade — Critical/Error trigger + follow-on events, 5 min ──

    private static List<EventCorrelation> DetectErrorCascades(IList<EventEntry> events)
    {
        var results = new List<EventCorrelation>();
        var window  = TimeSpan.FromMinutes(5);

        var relevantEvents = events.Where(e => CascadeLevels.Contains(e.Level)).ToList();

        var byMachine = relevantEvents
            .GroupBy(e => e.MachineName)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

        foreach (var machGroup in byMachine)
        {
            var sorted = machGroup.OrderBy(e => e.TimeCreated).ToList();
            var i = 0;

            while (i < sorted.Count)
            {
                // Only start a cascade on a Critical or Error event
                if (!ErrorLevels.Contains(sorted[i].Level))
                {
                    i++;
                    continue;
                }

                var trigger  = sorted[i];
                var cascade  = sorted
                    .Where(e => e.TimeCreated >= trigger.TimeCreated &&
                                e.TimeCreated - trigger.TimeCreated <= window)
                    .ToList();

                if (cascade.Count >= 2)
                {
                    results.Add(new EventCorrelation
                    {
                        Name            = $"Error Cascade: {trigger.Source}",
                        CorrelationType = "ErrorCascade",
                        Description     = $"[{trigger.Level}] '{trigger.Source}' on {trigger.MachineName} — {cascade.Count} related events within {window.TotalMinutes:F0}m",
                        EventEntries    = cascade,
                        DetectedAt      = DateTime.UtcNow,
                    });

                    // Advance i past the window to avoid highly-overlapping cascades
                    var next = sorted.FindIndex(i + 1,
                        e => e.TimeCreated > trigger.TimeCreated + window);
                    i = next < 0 ? sorted.Count : next;
                }
                else
                {
                    i++;
                }
            }
        }

        return results;
    }

    // ── C: Auth Sequence — known auth event IDs, 2-minute window ─────────────

    private static List<EventCorrelation> DetectAuthSequences(IList<EventEntry> events)
    {
        var results    = new List<EventCorrelation>();
        var window     = TimeSpan.FromMinutes(2);
        var authEvents = events.Where(e => AuthEventIds.Contains(e.EventId)).ToList();

        if (authEvents.Count < 2) return results;

        var byMachine = authEvents
            .GroupBy(e => e.MachineName)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

        foreach (var machGroup in byMachine)
        {
            var sorted = machGroup.OrderBy(e => e.TimeCreated).ToList();
            var i = 0;

            while (i < sorted.Count)
            {
                var anchor = sorted[i];
                var seq    = sorted
                    .Where(e => e.TimeCreated >= anchor.TimeCreated &&
                                e.TimeCreated - anchor.TimeCreated <= window)
                    .ToList();

                if (seq.Count >= 2)
                {
                    var hasFailure = seq.Any(e => e.EventId == 4625);
                    results.Add(new EventCorrelation
                    {
                        Name = hasFailure
                            ? $"Auth Failure Sequence: {anchor.MachineName}"
                            : $"Authentication Sequence: {anchor.MachineName}",
                        CorrelationType = "AuthSequence",
                        Description     = $"{seq.Count} authentication events on {anchor.MachineName}" +
                                          (hasFailure ? " (includes login failures)" : " (successful flow)"),
                        EventEntries    = seq,
                        DetectedAt      = DateTime.UtcNow,
                    });

                    var next = sorted.FindIndex(i + 1,
                        e => e.TimeCreated > anchor.TimeCreated + window);
                    i = next < 0 ? sorted.Count : next;
                }
                else
                {
                    i++;
                }
            }
        }

        return results;
    }

    // ── D: Service Lifecycle — service event IDs, 10-minute window ───────────

    private static List<EventCorrelation> DetectServiceLifecycle(IList<EventEntry> events)
    {
        var results       = new List<EventCorrelation>();
        var window        = TimeSpan.FromMinutes(10);
        var serviceEvents = events.Where(e => ServiceEventIds.Contains(e.EventId)).ToList();

        if (serviceEvents.Count < 2) return results;

        var bySource = serviceEvents
            .GroupBy(e => e.Source)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

        foreach (var srcGroup in bySource)
        {
            var sorted = srcGroup.OrderBy(e => e.TimeCreated).ToList();
            var i = 0;

            while (i < sorted.Count)
            {
                var anchor    = sorted[i];
                var lifecycle = sorted
                    .Where(e => e.TimeCreated >= anchor.TimeCreated &&
                                e.TimeCreated - anchor.TimeCreated <= window)
                    .ToList();

                if (lifecycle.Count >= 2)
                {
                    results.Add(new EventCorrelation
                    {
                        Name            = $"Service Lifecycle: {anchor.Source}",
                        CorrelationType = "ServiceLifecycle",
                        Description     = $"{lifecycle.Count} service lifecycle events from '{anchor.Source}' within {window.TotalMinutes:F0}m",
                        EventEntries    = lifecycle,
                        DetectedAt      = DateTime.UtcNow,
                    });

                    var next = sorted.FindIndex(i + 1,
                        e => e.TimeCreated > anchor.TimeCreated + window);
                    i = next < 0 ? sorted.Count : next;
                }
                else
                {
                    i++;
                }
            }
        }

        return results;
    }

    // ── Deduplication ─────────────────────────────────────────────────────────
    // Each event is assigned to the single highest-priority correlation that
    // contains it.  Correlations that fall below minimum size after reassignment
    // are dropped.

    private static List<EventCorrelation> Deduplicate(IEnumerable<EventCorrelation> allCorrelations)
    {
        var all = allCorrelations.ToList();
        if (all.Count == 0) return all;

        static int Priority(string t) => TypePriority.TryGetValue(t, out var p) ? p : 0;

        // Map each event reference → the correlation that "owns" it
        // (we process highest priority first so the first writer wins)
        var eventOwner = new Dictionary<EventEntry, Guid>(ReferenceEqualityComparer.Instance);

        foreach (var corr in all.OrderByDescending(c => Priority(c.CorrelationType)))
        {
            foreach (var evt in corr.EventEntries)
                eventOwner.TryAdd(evt, corr.Id);
        }

        var result = new List<EventCorrelation>();

        foreach (var corr in all)
        {
            var kept = corr.EventEntries
                .Where(e => eventOwner.TryGetValue(e, out var ownerId) && ownerId == corr.Id)
                .ToList();

            var min = MinSize.TryGetValue(corr.CorrelationType, out var m) ? m : 2;
            if (kept.Count < min) continue;

            corr.EventEntries = kept;
            result.Add(corr);
        }

        return [.. result.OrderByDescending(c => c.DetectedAt)];
    }
}
