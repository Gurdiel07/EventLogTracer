namespace EventLogTracer.Core.Models;

public class EventCorrelation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<EventEntry> EventEntries { get; set; } = new();
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public string CorrelationType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
