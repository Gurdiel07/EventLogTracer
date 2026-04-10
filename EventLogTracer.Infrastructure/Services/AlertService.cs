using System.Net.Http;
using System.Net.Http.Json;
using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using EventLogTracer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace EventLogTracer.Infrastructure.Services;

public class AlertService : IAlertService
{
    private readonly EventLogTracerDbContext _context;
    private readonly IDesktopNotifier _desktopNotifier;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger = Log.ForContext<AlertService>();

    public AlertService(
        EventLogTracerDbContext context,
        IDesktopNotifier desktopNotifier,
        HttpClient httpClient)
    {
        _context = context;
        _desktopNotifier = desktopNotifier;
        _httpClient = httpClient;
    }

    public async Task CheckAndNotifyAsync(EventEntry entry, CancellationToken cancellationToken = default)
    {
        var rules = await _context.AlertRules
            .Where(r => r.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            if (!MatchesRule(entry, rule))
                continue;

            _logger.Information(
                "Alert rule '{RuleName}' triggered for event {EventId} from {Source}",
                rule.Name, entry.EventId, entry.Source);

            await DispatchNotificationAsync(rule, entry);
        }
    }

    public async Task<IEnumerable<AlertRule>> GetRulesAsync(CancellationToken cancellationToken = default)
        => await _context.AlertRules.ToListAsync(cancellationToken);

    public async Task AddRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        await _context.AlertRules.AddAsync(rule, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        _context.AlertRules.Update(rule);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await _context.AlertRules.FindAsync([ruleId], cancellationToken);
        if (rule is not null)
        {
            _context.AlertRules.Remove(rule);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool MatchesRule(EventEntry entry, AlertRule rule)
    {
        var f = rule.Filter;

        if (f.Levels?.Count > 0 && !f.Levels.Contains(entry.Level))
            return false;

        if (f.Sources?.Count > 0 && !f.Sources.Contains(entry.Source))
            return false;

        if (f.LogNames?.Count > 0 && !f.LogNames.Contains(entry.LogName))
            return false;

        if (f.EventIds?.Count > 0 && !f.EventIds.Contains(entry.EventId))
            return false;

        if (!string.IsNullOrWhiteSpace(f.SearchText) &&
            !entry.Message.Contains(f.SearchText, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private async Task DispatchNotificationAsync(AlertRule rule, EventEntry entry)
    {
        var title = $"Alert: {rule.Name}";
        var message = $"[{entry.Level}] {entry.Source} — EventId {entry.EventId}";

        switch (rule.NotificationType)
        {
            case NotificationType.Desktop:
                _desktopNotifier.ShowNotification(title, message);
                break;

            case NotificationType.Email:
                _logger.Information(
                    "Email notification would be sent to {Target}: [{Level}] {Source} — EventId {EventId}",
                    rule.NotificationTarget, entry.Level, entry.Source, entry.EventId);
                break;

            case NotificationType.Webhook:
                await SendWebhookAsync(rule, entry);
                break;
        }
    }

    private async Task SendWebhookAsync(AlertRule rule, EventEntry entry)
    {
        if (string.IsNullOrWhiteSpace(rule.NotificationTarget))
            return;

        var payload = new
        {
            alertRule = rule.Name,
            eventId = entry.EventId,
            level = entry.Level.ToString(),
            source = entry.Source,
            message = entry.Message,
            timestamp = entry.TimeCreated.ToString("O")
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(rule.NotificationTarget, payload);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning(
                    "Webhook POST to {Target} returned {StatusCode}",
                    rule.NotificationTarget, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send webhook to {Target}", rule.NotificationTarget);
        }
    }
}
