using EventLogTracer.Core.Enums;

namespace EventLogTracer.Core.Models;

public class AnomalyResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public AnomalySeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<EventEntry> RelatedEvents { get; set; } = new();

    /// <summary>Value between 0.0 and 1.0 representing ML model confidence.</summary>
    public double Confidence { get; set; }
}
