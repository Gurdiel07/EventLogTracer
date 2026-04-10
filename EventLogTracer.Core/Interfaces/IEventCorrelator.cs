using EventLogTracer.Core.Models;

namespace EventLogTracer.Core.Interfaces;

public interface IEventCorrelator
{
    Task<IEnumerable<EventCorrelation>> CorrelateEventsAsync(
        IList<EventEntry> events,
        CancellationToken cancellationToken = default);
}
