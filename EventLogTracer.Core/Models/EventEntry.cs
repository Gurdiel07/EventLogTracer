using EventLogTracer.Core.Enums;

namespace EventLogTracer.Core.Models;

public class EventEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int EventId { get; set; }
    public EventLevel Level { get; set; }
    public string Source { get; set; } = string.Empty;

    /// <summary>Application, Security, System, Setup, ForwardedEvents</summary>
    public string LogName { get; set; } = string.Empty;

    public DateTime TimeCreated { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? UserId { get; set; }
    public string? Keywords { get; set; }
    public bool IsBookmarked { get; set; }
    public string? BookmarkColor { get; set; }
    public string? BookmarkComment { get; set; }
    public List<string> Tags { get; set; } = new();
}
