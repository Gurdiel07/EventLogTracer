using EventLogTracer.Core.Models;

namespace EventLogTracer.Core.Interfaces;

public interface IAlertService
{
    Task CheckAndNotifyAsync(EventEntry entry, CancellationToken cancellationToken = default);
    Task<IEnumerable<AlertRule>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task AddRuleAsync(AlertRule rule, CancellationToken cancellationToken = default);
    Task UpdateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);
}
