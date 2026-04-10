using EventLogTracer.Core.Models;

namespace EventLogTracer.Core.Interfaces;

public interface IAnomalyDetector
{
    Task<IEnumerable<AnomalyResult>> DetectAnomaliesAsync(
        IList<EventEntry> events,
        CancellationToken cancellationToken = default);

    Task TrainModelAsync(
        IList<EventEntry> trainingData,
        CancellationToken cancellationToken = default);

    bool IsModelTrained { get; }
}
