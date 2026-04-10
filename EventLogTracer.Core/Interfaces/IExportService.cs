using EventLogTracer.Core.Models;

namespace EventLogTracer.Core.Interfaces;

public interface IExportService
{
    Task ExportToCsvAsync(IEnumerable<EventEntry> entries, string path, CancellationToken cancellationToken = default);
    Task ExportToJsonAsync(IEnumerable<EventEntry> entries, string path, CancellationToken cancellationToken = default);
    Task ExportToXmlAsync(IEnumerable<EventEntry> entries, string path, CancellationToken cancellationToken = default);
}
