using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

// ── Agentic Ops Copilot ─────────────────────────────────────────────────────────
// The flagship: the platform's dormant agentic skeleton, brought to life. OpsTrax
// already had every piece — a correlated event bus, an ai_reasoning_runs slot storing
// prompt_template + expected_schema, a recommendation→action-request→approval chain, and
// a live P4 dispatch state machine — but the MODEL was never plugged in. This worker fills
// that slot: it watches OPEN dispatch exceptions (a real operational problem signal),
// asks the Agentic Brain (Claude) to reason about each one, and writes a REAL reasoning
// run + an actionable recommendation with a proposed_action into the foundation tables.
// A dispatcher then approves it in the existing recommendations UI, and the executor
// applies it through existing dispatch endpoints — fully audited, human-gated.
//
// Why this is un-copyable: it requires owning BOTH the real-time signal (telemetry +
// dispatch exceptions) AND the back-office workflow (assignment state machine, approval
// policy). Telematics incumbents lack the workflow; TMS incumbents lack the live signal.
// OpsTrax owns the seam.
//
// Guardrails: recommendations are written as PROPOSED, never auto-executed. The brain is
// disabled unless an API key is configured (no key ⇒ this worker no-ops cleanly). Per
// exception it runs at most once (dedup by source_event_id). Cross-tenant loop reuses the
// proven EscalationBackgroundService system-scope pattern. Cooldown + per-tick cap bound
// model cost.
//
// Config (all optional):
//   Agentic:Ops:Enabled          (bool,  default true — but brain must also be enabled)
//   Agentic:Ops:IntervalSeconds  (int,   default 45, clamped 20..600)
//   Agentic:Ops:MaxPerTick       (int,   default 5  — cap model calls/tick for cost)
public sealed class AgenticOpsBackgroundService(
    IServiceScopeFactory scopeFactory,
    AgenticBrainService brain,
    ILogger<AgenticOpsBackgroundService> logger,
    ServiceRunTracker tracker,
    IConfiguration config) : BackgroundService
{
    private const string SvcName = "AgenticOpsBackgroundService";

    private const string SystemPrompt =
        "You are the OpsTrax Dispatch Copilot, an expert fleet operations dispatcher. You are given ONE open " +
        "dispatch exception (a delivery/pickup problem) with its assignment, driver, vehicle and job context. " +
        "Decide the single best next operational action a human dispatcher should take to recover the load and " +
        "protect the customer SLA. Choose action_type from EXACTLY this set: " +
        "\"reassign_driver\" (swap in another driver), \"reroute\" (change the route/stop order), " +
        "\"notify_customer\" (send a proactive ETA/delay update), \"open_work_order\" (vehicle needs service), " +
        "\"escalate\" (needs a human manager decision), or \"monitor\" (no action yet, watch it). " +
        "Respond with ONE JSON object and NOTHING else, matching this schema: " +
        "{\"title\": string (<=80 chars), \"summary\": string (<=200 chars, plain operational language), " +
        "\"action_type\": one of the above, \"action_detail\": string (<=200 chars, what specifically to do), " +
        "\"urgency\": number 0..1, \"confidence\": number 0..1, \"reason\": string (<=200 chars, why), " +
        "\"risk_level\": \"low\"|\"medium\"|\"high\"}.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = config.GetValue("Agentic:Ops:Enabled", true);
        if (!enabled) { logger.LogInformation("{Svc} disabled by configuration", SvcName); return; }
        if (!brain.Enabled)
        {
            logger.LogInformation("{Svc} idle — Agentic Brain has no API key configured; no model calls will be made.", SvcName);
            return;
        }

        var intervalSeconds = Math.Clamp(config.GetValue("Agentic:Ops:IntervalSeconds", 45), 20, 600);
        var maxPerTick = Math.Clamp(config.GetValue("Agentic:Ops:MaxPerTick", 5), 1, 25);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken).ContinueWith(_ => { }, stoppingToken);
        logger.LogInformation("{Svc} started — tick {Interval}s, max {Max} reasonings/tick", SvcName, intervalSeconds, maxPerTick);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, stoppingToken);
            var produced = 0;
            try
            {
                produced = await TickAsync(maxPerTick, stoppingToken);
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, produced, (int)sw.ElapsedMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "{Svc} tick failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<int> TickAsync(int maxPerTick, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        // Claim the oldest open dispatch exceptions that this copilot has NOT already
        // reasoned about (no ai_recommendation whose source_event_id points at them).
        // Runs under system scope (cross-tenant worker) like EscalationBackgroundService.
        List<Dictionary<string, object?>> rows = new();
        await db.RunInSystemScopeAsync(async () =>
        {
            rows = (await db.QueryAsync(
                @"SELECT dex.id, dex.company_id, dex.assignment_id, dex.job_id,
                         dex.exception_type, dex.notes, dex.status, dex.created_at,
                         da.assignment_status, COALESCE(j.job_number, j.job_code) job_number,
                         j.dropoff_address, j.sla_status, j.eta,
                         d.full_name driver_name, d.id driver_id,
                         v.vehicle_code, v.id vehicle_id
                  FROM dispatch_exceptions dex
                  JOIN dispatch_assignments da ON da.id = dex.assignment_id
                  LEFT JOIN jobs j ON j.id = dex.job_id
                  LEFT JOIN drivers d ON d.id = da.driver_id
                  LEFT JOIN vehicles v ON v.id = da.vehicle_id
                  WHERE dex.status = 'open'
                    AND NOT EXISTS (
                        SELECT 1 FROM ai_recommendations r
                        WHERE r.source_event_id = 'dispatch_exception:' || dex.id::text)
                  ORDER BY dex.created_at ASC
                  LIMIT @cap",
                c => c.Parameters.AddWithValue("@cap", maxPerTick), ct)).ToList();
        }, ct);

        if (rows.Count == 0) return 0;

        var produced = 0;
        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;
            try { if (await ReasonAboutExceptionAsync(row, ct)) produced++; }
            catch (Exception ex) { logger.LogWarning(ex, "{Svc} failed to reason about exception {Id}", SvcName, row.GetValueOrDefault("id")); }
        }
        return produced;
    }

    private async Task<bool> ReasonAboutExceptionAsync(Dictionary<string, object?> row, CancellationToken ct)
    {
        var tenantId = Convert.ToInt64(row["companyId"], System.Globalization.CultureInfo.InvariantCulture);
        var exceptionId = Convert.ToInt64(row["id"], System.Globalization.CultureInfo.InvariantCulture);
        var sourceEventId = $"dispatch_exception:{exceptionId}";

        // Compact, DATA-only context for the brain (no instructions here).
        var context = JsonSerializer.Serialize(new
        {
            exception_type = row.GetValueOrDefault("exceptionType"),
            reason = row.GetValueOrDefault("notes"),
            assignment_status = row.GetValueOrDefault("assignmentStatus"),
            job_number = row.GetValueOrDefault("jobNumber"),
            dropoff_address = row.GetValueOrDefault("dropoffAddress"),
            sla_status = row.GetValueOrDefault("slaStatus"),
            eta = row.GetValueOrDefault("eta")?.ToString(),
            driver = row.GetValueOrDefault("driverName"),
            vehicle = row.GetValueOrDefault("vehicleCode"),
        });

        var expectedSchema = "{\"title\":\"string\",\"summary\":\"string\",\"action_type\":\"string\",\"action_detail\":\"string\",\"urgency\":\"number\",\"confidence\":\"number\",\"reason\":\"string\",\"risk_level\":\"string\"}";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        var produced = false;
        await db.RunInSystemScopeAsync(async () =>
        {
            var foundation = new PostgresAiFoundationService(db);
            var run = foundation.StartReasoningRun(tenantId.ToString(), "dispatch_exception", context, SystemPrompt, expectedSchema);

            var result = await brain.DecideAsync(SystemPrompt, context, ct);
            if (!result.Ok)
            {
                foundation.FailReasoningRun(run, JsonSerializer.Serialize(new { error = result.Error }));
                return;
            }

            // Validate the model's structured output; bail safely if malformed.
            string title, summary, actionType, actionDetail, reason, riskLevel;
            decimal urgency, confidence;
            try
            {
                using var doc = JsonDocument.Parse(result.OutputJson);
                var el = doc.RootElement;
                title = Str(el, "title", "Dispatch exception needs attention");
                summary = Str(el, "summary", "Review this open dispatch exception.");
                actionType = NormalizeActionType(Str(el, "action_type", "escalate"));
                actionDetail = Str(el, "action_detail", "");
                reason = Str(el, "reason", "");
                riskLevel = NormalizeRisk(Str(el, "risk_level", "medium"));
                urgency = Dec(el, "urgency", 0.6m);
                confidence = result.Confidence;
            }
            catch
            {
                foundation.FailReasoningRun(run, JsonSerializer.Serialize(new { error = "unparseable model output", raw = result.OutputJson }));
                return;
            }

            foundation.CompleteReasoningRun(run, result.OutputJson, confidence);

            var proposedAction = JsonSerializer.Serialize(new
            {
                action_type = actionType,
                action_detail = actionDetail,
                resource_type = "dispatch_assignment",
                resource_id = row.GetValueOrDefault("assignmentId")?.ToString(),
                job_number = row.GetValueOrDefault("jobNumber")?.ToString(),
                requires_approval = true,
            });
            var impact = JsonSerializer.Serialize(new { sla_status = row.GetValueOrDefault("slaStatus"), exception_type = row.GetValueOrDefault("exceptionType") });
            var reasonJson = JsonSerializer.Serialize(new { reason, source = "agentic-ops-copilot", exception_id = exceptionId });

            // Written as PROPOSED — surfaced to the dispatcher, never auto-executed.
            foundation.CreateRecommendation(
                tenantId.ToString(),
                recommendationType: "dispatch_copilot",
                title: title,
                summary: summary,
                confidenceScore: confidence,
                urgencyScore: urgency,
                impactJson: impact,
                reasonJson: reasonJson,
                proposedActionJson: proposedAction,
                riskLevel: riskLevel,
                sourceEventId: sourceEventId,
                actorType: "agent",
                actorId: "dispatch-copilot",
                status: "proposed");

            logger.LogInformation("{Svc} proposed {Action} for exception {Id} (tenant {Tenant}, conf {Conf:P0})",
                SvcName, actionType, exceptionId, tenantId, confidence);
            produced = true;
        }, ct);
        return produced;
    }

    private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
        { "reassign_driver", "reroute", "notify_customer", "open_work_order", "escalate", "monitor" };

    private static string NormalizeActionType(string v) => ValidActions.Contains(v) ? v.ToLowerInvariant() : "escalate";
    private static string NormalizeRisk(string v) => v.ToLowerInvariant() is "low" or "medium" or "high" ? v.ToLowerInvariant() : "medium";

    private static string Str(JsonElement el, string key, string fallback) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? fallback) : fallback;

    private static decimal Dec(JsonElement el, string key, decimal fallback) =>
        el.TryGetProperty(key, out var v) && v.TryGetDecimal(out var d) ? Math.Clamp(d, 0m, 1m) : fallback;
}
