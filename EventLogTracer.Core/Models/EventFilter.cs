using EventLogTracer.Core.Enums;

namespace EventLogTracer.Core.Models;

public class EventFilter
{
    public List<int>? EventIds { get; set; }
    public List<EventLevel>? Levels { get; set; }
    public List<string>? Sources { get; set; }
    public List<string>? LogNames { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? SearchText { get; set; }
    public bool IsRegex { get; set; }
    public List<string>? Tags { get; set; }
}
