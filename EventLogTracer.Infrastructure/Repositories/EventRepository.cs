using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using EventLogTracer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventLogTracer.Infrastructure.Repositories;

public class EventRepository : IEventRepository
{
    private readonly EventLogTracerDbContext _context;

    public EventRepository(EventLogTracerDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<EventEntry>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.EventEntries.ToListAsync(cancellationToken);

    public async Task<IEnumerable<EventEntry>> GetByFilterAsync(
        EventFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.EventEntries.AsQueryable();

        if (filter.Levels?.Count > 0)
            query = query.Where(e => filter.Levels.Contains(e.Level));

        if (filter.LogNames?.Count > 0)
            query = query.Where(e => filter.LogNames.Contains(e.LogName));

        if (filter.Sources?.Count > 0)
            query = query.Where(e => filter.Sources.Contains(e.Source));

        if (filter.EventIds?.Count > 0)
            query = query.Where(e => filter.EventIds.Contains(e.EventId));

        if (filter.StartDate.HasValue)
            query = query.Where(e => e.TimeCreated >= filter.StartDate.Value);

        if (filter.EndDate.HasValue)
            query = query.Where(e => e.TimeCreated <= filter.EndDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var searchText = filter.SearchText;
            query = query.Where(e =>
                EF.Functions.Like(e.Message, $"%{searchText}%") ||
                EF.Functions.Like(e.Source, $"%{searchText}%"));
        }

        return await query
            .OrderByDescending(e => e.TimeCreated)
            .ToListAsync(cancellationToken);
    }

    public async Task<EventEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.EventEntries.FindAsync([id], cancellationToken);

    public async Task AddAsync(EventEntry entry, CancellationToken cancellationToken = default)
    {
        await _context.EventEntries.AddAsync(entry, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<EventEntry> entries, CancellationToken cancellationToken = default)
    {
        await _context.EventEntries.AddRangeAsync(entries, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(EventEntry entry, CancellationToken cancellationToken = default)
    {
        _context.EventEntries.Update(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _context.EventEntries.FindAsync([id], cancellationToken);
        if (entry is not null)
        {
            _context.EventEntries.Remove(entry);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> CountAsync(EventFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (filter is null)
            return await _context.EventEntries.CountAsync(cancellationToken);

        var query = _context.EventEntries.AsQueryable();

        if (filter.Levels?.Count > 0)
            query = query.Where(e => filter.Levels.Contains(e.Level));

        if (filter.LogNames?.Count > 0)
            query = query.Where(e => filter.LogNames.Contains(e.LogName));

        if (filter.Sources?.Count > 0)
            query = query.Where(e => filter.Sources.Contains(e.Source));

        if (filter.EventIds?.Count > 0)
            query = query.Where(e => filter.EventIds.Contains(e.EventId));

        if (filter.StartDate.HasValue)
            query = query.Where(e => e.TimeCreated >= filter.StartDate.Value);

        if (filter.EndDate.HasValue)
            query = query.Where(e => e.TimeCreated <= filter.EndDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var searchText = filter.SearchText;
            query = query.Where(e =>
                EF.Functions.Like(e.Message, $"%{searchText}%") ||
                EF.Functions.Like(e.Source, $"%{searchText}%"));
        }

        return await query.CountAsync(cancellationToken);
    }
}
