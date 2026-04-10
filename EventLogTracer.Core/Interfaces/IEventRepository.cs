using EventLogTracer.Core.Models;

namespace EventLogTracer.Core.Interfaces;

public interface IEventRepository
{
    Task<IEnumerable<EventEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<EventEntry>> GetByFilterAsync(EventFilter filter, CancellationToken cancellationToken = default);
    Task<EventEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(EventEntry entry, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<EventEntry> entries, CancellationToken cancellationToken = default);
    Task UpdateAsync(EventEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(EventFilter? filter = null, CancellationToken cancellationToken = default);
}
