using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using Microsoft.ML;
using Microsoft.ML.Data;
using Serilog;

namespace EventLogTracer.ML.Services;

/// <summary>
/// Anomaly detector using three complementary strategies:
///   A. ML.NET IID Spike Detection on hourly event frequency
///   B. Heuristic: abnormal Critical+Error ratio in a time window
///   C. Heuristic: unknown or suddenly-busy event sources
/// </summary>
public class AnomalyDetector : IAnomalyDetector
{
    // ── Thread-safety primitives ───────────────────────────────────────────────

    private readonly SemaphoreSlim _trainSemaphore = new(1, 1);
    private readonly object _statsLock = new();

    // ── Baseline statistics (written during training, read during detection) ──

    private double _baselineMeanEventsPerHour;
    private double _baselineStdEventsPerHour = 1.0;
    private double _baselineCriticalErrorRatio;
    private IReadOnlySet<string> _knownSources = new HashSet<string>();
    private Dictionary<string, double> _knownSourceHourlyRates = new();

    public bool IsModelTrained { get; private set; }

    // ── ML.NET data models ────────────────────────────────────────────────────

    private sealed class EventRateData
    {
        public float EventCount { get; set; }
    }

    private sealed class EventRatePrediction
    {
        // [Alert (0/1), RawScore, P-Value]
        [VectorType(3)]
        public double[] Prediction { get; set; } = [];
    }

    // ── TrainModelAsync ───────────────────────────────────────────────────────

    public async Task TrainModelAsync(
        IList<EventEntry> trainingData,
        CancellationToken cancellationToken = default)
    {
        await _trainSemaphore.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() => ComputeBaseline(trainingData), cancellationToken);
            IsModelTrained = true;
            Log.Information(
                "AnomalyDetector trained on {Count} events. " +
                "Baseline: {Mean:F1} events/hour ±{Std:F1}, " +
                "crit+err ratio: {Ratio:P0}, known sources: {Sources}",
                trainingData.Count,
                _baselineMeanEventsPerHour,
                _baselineStdEventsPerHour,
                _baselineCriticalErrorRatio,
                _knownSources.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnomalyDetector training failed");
        }
        finally
        {
            _trainSemaphore.Release();
        }
    }

    private void ComputeBaseline(IList<EventEntry> events)
    {
        if (events.Count == 0)
        {
            lock (_statsLock)
            {
                _baselineMeanEventsPerHour  = 0;
                _baselineStdEventsPerHour   = 1;
                _baselineCriticalErrorRatio = 0;
                _knownSources              = new HashSet<string>();
                _knownSourceHourlyRates    = new Dictionary<string, double>();
            }
            return;
        }

        var hourlyGroups = GroupByHour(events);
        var hourlyCounts = hourlyGroups.Values.Select(g => (double)g.Count).ToList();

        var mean = hourlyCounts.Average();
        var variance = hourlyCounts.Count > 1
            ? hourlyCounts.Sum(x => Math.Pow(x - mean, 2)) / (hourlyCounts.Count - 1)
            : 1.0;
        var std = Math.Max(1.0, Math.Sqrt(variance));

        var critErrRatio = events.Count > 0
            ? (double)events.Count(e => e.Level is EventLevel.Critical or EventLevel.Error) / events.Count
            : 0.0;

        var hourCount = Math.Max(1, hourlyGroups.Count);
        var knownSources = events
            .Select(e => e.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet();

        var sourceRates = events
            .GroupBy(e => e.Source)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => (double)g.Count() / hourCount);

        lock (_statsLock)
        {
            _baselineMeanEventsPerHour  = mean;
            _baselineStdEventsPerHour   = std;
            _baselineCriticalErrorRatio = critErrRatio;
            _knownSources              = knownSources;
            _knownSourceHourlyRates    = sourceRates;
        }
    }

    // ── DetectAnomaliesAsync ──────────────────────────────────────────────────

    public async Task<IEnumerable<AnomalyResult>> DetectAnomaliesAsync(
        IList<EventEntry> events,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsModelTrained)
                await TrainModelAsync(events, cancellationToken);

            if (events.Count == 0)
                return [];

            // Snapshot baseline stats for consistent reads across parallel tasks
            double baselineMean, baselineCERate;
            IReadOnlySet<string> knownSources;
            Dictionary<string, double> knownRates;

            lock (_statsLock)
            {
                baselineMean  = _baselineMeanEventsPerHour;
                baselineCERate = _baselineCriticalErrorRatio;
                knownSources  = _knownSources;
                knownRates    = new Dictionary<string, double>(_knownSourceHourlyRates);
            }

            var hourlyGroups = GroupByHour(events);

            // Run the three detectors in parallel (each creates its own MLContext)
            var tasks = new[]
            {
                Task.Run(() => DetectFrequencySpikes(hourlyGroups, baselineMean), cancellationToken),
                Task.Run(() => DetectLevelAnomalies(hourlyGroups, baselineCERate), cancellationToken),
                Task.Run(() => DetectSourceAnomalies(hourlyGroups, knownSources, knownRates), cancellationToken)
            };

            var allBatches = await Task.WhenAll(tasks);
            var combined   = allBatches.SelectMany(b => b).ToList();

            // Deduplicate by description, keep highest-confidence hit
            return combined
                .GroupBy(r => r.Description)
                .Select(g => g.OrderByDescending(r => r.Confidence).First())
                .OrderByDescending(r => (int)r.Severity)
                .ThenByDescending(r => r.Confidence)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Anomaly detection failed — returning empty result");
            return [];
        }
    }

    // ── A: ML.NET IID Spike Detection on hourly event frequency ──────────────

    private List<AnomalyResult> DetectFrequencySpikes(
        Dictionary<DateTime, List<EventEntry>> hourlyGroups,
        double baselineMean)
    {
        var results = new List<AnomalyResult>();

        if (hourlyGroups.Count < 24)
            return results; // Insufficient data for IID spike detection

        try
        {
            // Fresh MLContext per invocation — not thread-safe to share
            var mlContext   = new MLContext(seed: 42);
            var orderedHours = hourlyGroups.OrderBy(kv => kv.Key).ToList();
            var data         = orderedHours
                .Select(kv => new EventRateData { EventCount = (float)kv.Value.Count })
                .ToList();

            var dataView            = mlContext.Data.LoadFromEnumerable(data);
            var pvalueHistoryLength = Math.Max(4, data.Count / 4);

            var pipeline  = mlContext.Transforms.DetectIidSpike(
                outputColumnName:    "Prediction",
                inputColumnName:     nameof(EventRateData.EventCount),
                confidence:          95.0,
                pvalueHistoryLength: pvalueHistoryLength);

            var model       = pipeline.Fit(dataView);
            var transformed = model.Transform(dataView);
            var predictions = mlContext.Data
                .CreateEnumerable<EventRatePrediction>(transformed, reuseRowObject: false)
                .ToList();

            for (var i = 0; i < predictions.Count; i++)
            {
                var pred = predictions[i];
                if (pred.Prediction is not { Length: >= 3 }) continue;
                if (pred.Prediction[0] != 1.0) continue;   // 1 = spike detected

                var hour       = orderedHours[i].Key;
                var hourEvents = orderedHours[i].Value;
                var pValue     = pred.Prediction[2];
                var confidence = Math.Clamp(1.0 - pValue, 0.0, 1.0);

                results.Add(new AnomalyResult
                {
                    Severity      = AnomalySeverity.High,
                    Description   = $"Unusual event spike: {hourEvents.Count} events at {hour.ToLocalTime():HH:mm} (baseline ~{baselineMean:F0}/hour)",
                    Confidence    = confidence,
                    RelatedEvents = hourEvents,
                    DetectedAt    = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ML spike detection failed — skipping frequency anomalies");
        }

        return results;
    }

    // ── B: Abnormal Critical+Error ratio in each hourly window ───────────────

    private List<AnomalyResult> DetectLevelAnomalies(
        Dictionary<DateTime, List<EventEntry>> hourlyGroups,
        double baselineCERate)
    {
        var results = new List<AnomalyResult>();

        // Threshold: 3× baseline ratio, with a floor of 30%
        var threshold = Math.Max(baselineCERate * 3.0, 0.30);

        foreach (var (hour, hourEvents) in hourlyGroups)
        {
            if (hourEvents.Count < 5) continue;

            var critErrCount = hourEvents.Count(e =>
                e.Level is EventLevel.Critical or EventLevel.Error);
            var ratio = (double)critErrCount / hourEvents.Count;

            if (ratio <= threshold) continue;

            var excess     = ratio - threshold;
            var confidence = Math.Min(1.0, excess / (1.0 - threshold));

            results.Add(new AnomalyResult
            {
                Severity      = AnomalySeverity.Medium,
                Description   = $"Abnormal error rate at {hour.ToLocalTime():HH:mm}: {ratio:P0} Critical/Error (baseline ~{baselineCERate:P0})",
                Confidence    = confidence,
                RelatedEvents = hourEvents
                    .Where(e => e.Level is EventLevel.Critical or EventLevel.Error)
                    .ToList(),
                DetectedAt = DateTime.UtcNow
            });
        }

        return results;
    }

    // ── C: Unknown sources and suddenly-busy sources ──────────────────────────

    private List<AnomalyResult> DetectSourceAnomalies(
        Dictionary<DateTime, List<EventEntry>> hourlyGroups,
        IReadOnlySet<string> knownSources,
        Dictionary<string, double> knownRates)
    {
        var results     = new List<AnomalyResult>();
        var allEvents   = hourlyGroups.Values.SelectMany(e => e).ToList();
        var hourCount   = Math.Max(1, hourlyGroups.Count);

        // ── C1: Sources not seen during training (new / unknown) ─────────────
        if (knownSources.Count > 0)
        {
            var newSources = allEvents
                .Select(e => e.Source)
                .Where(s => !string.IsNullOrWhiteSpace(s) && !knownSources.Contains(s))
                .Distinct()
                .ToList();

            foreach (var src in newSources)
            {
                var related = allEvents.Where(e => e.Source == src).ToList();
                results.Add(new AnomalyResult
                {
                    Severity      = AnomalySeverity.Low,
                    Description   = $"Unknown source detected: '{src}' ({related.Count} events)",
                    Confidence    = 0.75,
                    RelatedEvents = related,
                    DetectedAt    = DateTime.UtcNow
                });
            }
        }

        // ── C2: Known source with 10× its normal rate ─────────────────────────
        var currentRates = allEvents
            .GroupBy(e => e.Source)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => (double)g.Count() / hourCount);

        foreach (var (src, currentRate) in currentRates)
        {
            if (!knownRates.TryGetValue(src, out var baselineRate)) continue;

            var spikeThreshold = Math.Max(baselineRate * 10.0, 10.0);
            if (currentRate <= spikeThreshold) continue;

            var related    = allEvents.Where(e => e.Source == src).ToList();
            var confidence = Math.Min(1.0, currentRate / spikeThreshold);

            results.Add(new AnomalyResult
            {
                Severity      = AnomalySeverity.High,
                Description   = $"Source '{src}' unusually active: {currentRate:F1} events/hour (normal ~{baselineRate:F1}/hour)",
                Confidence    = confidence,
                RelatedEvents = related,
                DetectedAt    = DateTime.UtcNow
            });
        }

        return results;
    }

    // ── Shared helper ─────────────────────────────────────────────────────────

    private static Dictionary<DateTime, List<EventEntry>> GroupByHour(IList<EventEntry> events)
        => events
            .GroupBy(e =>
            {
                var t = e.TimeCreated.ToUniversalTime();
                return new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);
            })
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());
}
