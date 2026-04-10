using EventLogTracer.Core.Enums;

namespace EventLogTracer.Core.Models;

public class AlertRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public EventFilter Filter { get; set; } = new();
    public NotificationType NotificationType { get; set; }
    public string NotificationTarget { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
