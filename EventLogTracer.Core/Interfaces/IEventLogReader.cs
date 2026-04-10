using EventLogTracer.Core.Models;

namespace EventLogTracer.Core.Interfaces;

public interface IEventLogReader
{
    Task<IEnumerable<EventEntry>> GetEventsAsync(EventFilter filter, CancellationToken cancellationToken = default);
    void StartMonitoring(Action<EventEntry> onEventReceived);
    void StopMonitoring();
    bool IsMonitoring { get; }
}
