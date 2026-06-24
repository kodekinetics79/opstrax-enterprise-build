using Opstrax.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opstrax.Api.Services;

public sealed class EscalationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<EscalationBackgroundService> logger,
    ServiceRunTracker tracker) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string SvcName = "EscalationBackgroundService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Svc} started", SvcName);
        while (!stoppingToken.IsCancellationRequested)
        {
            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, stoppingToken);
            try
            {
                await RunEscalationCycleAsync(stoppingToken);
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, 0, (int)sw.ElapsedMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "{Svc} cycle failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            await Task.Delay(Interval, stoppingToken);
        }
        logger.LogInformation("{Svc} stopped", SvcName);
    }

    private async Task RunEscalationCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();

        // Load enabled escalation rules
        var rules = await db.QueryAsync(
            @"SELECT id, company_id, rule_name, event_type, severity,
                     initial_audience, escalation_audience,
                     time_to_escalate_minutes, repeat_interval_minutes, max_repeats
              FROM escalation_rules
              WHERE enabled=TRUE",
            ct: ct);

        if (rules.Count == 0) return;

        foreach (var rule in rules)
        {
            var ruleId                = Convert.ToInt64(rule["id"]);
            var companyId             = Convert.ToInt64(rule["companyId"]);
            var eventType             = rule["eventType"]?.ToString() ?? "";
            var severity              = rule["severity"]?.ToString() ?? "Medium";
            var escalationAudience    = rule["escalationAudience"]?.ToString() ?? "fleet_manager";
            var timeToEscalate        = Convert.ToInt32(rule["timeToEscalateMinutes"]);
            var repeatInterval        = Convert.ToInt32(rule["repeatIntervalMinutes"]);
            var maxRepeats            = Convert.ToInt32(rule["maxRepeats"]);

            // Find notifications that match this rule and are overdue for escalation
            var overdue = await db.QueryAsync(
                @"SELECT n.id, n.company_id, n.source_type, n.source_id, n.event_type, n.title, n.message
                  FROM notifications n
                  WHERE n.company_id=@cid
                    AND n.event_type=@evType
                    AND n.severity=@sev
                    AND n.status NOT IN ('read','acknowledged','suppressed','escalated')
                    AND n.escalated_from IS NULL
                    AND n.created_at < NOW() - @minutes * INTERVAL '1 minute'",
                c =>
                {
                    c.Parameters.AddWithValue("@cid",     companyId);
                    c.Parameters.AddWithValue("@evType",  eventType);
                    c.Parameters.AddWithValue("@sev",     severity);
                    c.Parameters.AddWithValue("@minutes", timeToEscalate);
                }, ct);

            foreach (var n in overdue)
            {
                var origId = Convert.ToInt64(n["id"]);
                var srcType = n["sourceType"]?.ToString() ?? "";
                var srcId  = n["sourceId"] is not null and not DBNull ? Convert.ToInt64(n["sourceId"]) : (long?)null;
                var title   = n["title"]?.ToString() ?? "Escalated Notification";
                var msg     = n["message"]?.ToString() ?? "";

                // Count existing escalations from same source to enforce max_repeats
                var escalationCount = await db.ScalarLongAsync(
                    @"SELECT COUNT(*) FROM notifications
                      WHERE escalated_from=@origId AND company_id=@cid",
                    c =>
                    {
                        c.Parameters.AddWithValue("@origId", origId);
                        c.Parameters.AddWithValue("@cid",    companyId);
                    }, ct);

                if (escalationCount >= maxRepeats)
                {
                    logger.LogDebug("Skipping escalation for notification {Id}: max_repeats {Max} reached", origId, maxRepeats);
                    continue;
                }

                // Check repeat interval — don't re-escalate too quickly
                if (escalationCount > 0)
                {
                    var lastEscalated = await db.ScalarLongAsync(
                        @"SELECT (EXTRACT(EPOCH FROM (NOW() - MAX(created_at))) / 60)::BIGINT FROM notifications
                          WHERE escalated_from=@origId AND company_id=@cid",
                        c =>
                        {
                            c.Parameters.AddWithValue("@origId", origId);
                            c.Parameters.AddWithValue("@cid",    companyId);
                        }, ct);

                    if (lastEscalated < repeatInterval)
                    {
                        logger.LogDebug("Skipping escalation for notification {Id}: repeat interval not met ({Min} min remaining)",
                            origId, repeatInterval - lastEscalated);
                        continue;
                    }
                }

                // Create escalated notification
                var escalatedId = await notif.CreateAsync(
                    companyId,
                    eventType,
                    srcType,
                    srcId,
                    severity,
                    $"[ESCALATED] {title}",
                    $"Escalated notification: {msg}",
                    escalationAudience,
                    ct,
                    priority: 1); // highest priority

                if (escalatedId > 0)
                {
                    // Set escalated_from link
                    await db.ExecuteAsync(
                        "UPDATE notifications SET escalated_from=@origId WHERE id=@id",
                        c =>
                        {
                            c.Parameters.AddWithValue("@origId", origId);
                            c.Parameters.AddWithValue("@id",     escalatedId);
                        }, ct);

                    logger.LogInformation("Escalated notification {OrigId} → {NewId} for company {Cid}",
                        origId, escalatedId, companyId);
                }
            }
        }
    }
}
