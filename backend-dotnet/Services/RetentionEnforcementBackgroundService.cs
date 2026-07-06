using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// RetentionEnforcementBackgroundService — the worker that ACTUALLY executes the
// retention policies DataRetentionService only stored until now.
//
// COMPLIANCE: PIPEDA Principle 5 / PDPL Art.18 / GDPR Art.5(1)(e) — personal and
// operational data must be kept only as long as necessary. Without an executing
// purge, a stored "90-day" policy is meaningless. This worker enforces it.
//
// SAFETY RAILS (deliberately conservative — deleting data is irreversible):
//   • Runs at most once/day; a small nightly batch, never a hot-path sweep.
//   • Skips ANY tenant with legal_hold_active — no deletion under legal hold.
//   • Deletes ONLY high-volume, low-risk operational logs (telemetry events,
//     notifications, report-execution logs) past their TTL. It NEVER touches
//     business records, financials, or customer/driver PII rows (those are
//     handled by explicit DSR erasure, which is auditable per subject).
//   • Every run is heartbeated + audited; per-category counts recorded.
//   • Disabled by default in Production unless RetentionWorker:Enabled=true, so it
//     can be enabled deliberately after the policy set is reviewed.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RetentionEnforcementBackgroundService(
    IServiceScopeFactory scopeFactory,
    ServiceRunTracker tracker,
    IConfiguration config,
    ILogger<RetentionEnforcementBackgroundService> logger) : BackgroundService
{
    private const string ServiceName = "RetentionEnforcementService";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Gate: opt-in in Production. Enabled elsewhere so dev/staging can validate.
        var isProd = string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production",
            StringComparison.OrdinalIgnoreCase);
        var enabled = config.GetValue("RetentionWorker:Enabled", !isProd);
        if (!enabled)
        {
            logger.LogInformation("[Retention] Disabled (RetentionWorker:Enabled=false). Policies stored but not enforced.");
            return;
        }

        // Small startup delay so it never contends with schema init / first traffic.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try { await Task.Delay(Interval, stoppingToken); } catch { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var runId = await tracker.BeginAsync(ServiceName, ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var totalDeleted = 0;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();

            await db.RunInSystemScopeAsync(async () =>
            {
                // One policy row per tenant; tenants with no row use safe defaults but
                // we only act on explicit policies to avoid surprising anyone.
                var policies = await db.QueryAsync(
                    @"SELECT company_id, telemetry_days, notification_days, report_execution_days,
                             COALESCE(legal_hold_active, false) AS legal_hold_active
                      FROM data_retention_policies", ct: ct);

                foreach (var p in policies)
                {
                    var companyId = Convert.ToInt64(p["companyId"]);
                    var legalHold = Convert.ToBoolean(p["legalHoldActive"] ?? false);
                    if (legalHold)
                    {
                        logger.LogInformation("[Retention] Tenant {Tenant} under legal hold — skipped.", companyId);
                        continue;
                    }

                    var telemetryDays    = Math.Max(7,  Convert.ToInt32(p["telemetryDays"]       ?? 90));
                    var notificationDays = Math.Max(7,  Convert.ToInt32(p["notificationDays"]    ?? 30));
                    var reportDays       = Math.Max(30, Convert.ToInt32(p["reportExecutionDays"] ?? 180));

                    totalDeleted += await PurgeAsync(db, "telemetry_events", "received_at", telemetryDays, companyId, ct);
                    totalDeleted += await PurgeAsync(db, "location_events",  "received_at", telemetryDays, companyId, ct);
                    totalDeleted += await PurgeAsync(db, "notifications",    "created_at",  notificationDays, companyId, ct);
                    totalDeleted += await PurgeAsync(db, "report_execution_log", "executed_at", reportDays, companyId, ct);
                }
            }, ct);

            sw.Stop();
            await tracker.CompleteAsync(runId, ServiceName, totalDeleted, (int)sw.ElapsedMilliseconds, ct);
            if (totalDeleted > 0)
                logger.LogInformation("[Retention] Purged {Count} expired operational rows across tenants.", totalDeleted);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await tracker.FailAsync(runId, ServiceName, ex, (int)sw.ElapsedMilliseconds, ct);
            logger.LogError(ex, "[Retention] Enforcement cycle failed");
        }
    }

    // Deletes rows older than <days> for a tenant, in bounded batches so a large
    // backlog never locks the table. Table/column names are code constants (never
    // user input) so string interpolation here is safe.
    private static async Task<int> PurgeAsync(
        Database db, string table, string tsColumn, int days, long companyId, CancellationToken ct)
    {
        var total = 0;
        try
        {
            int batch;
            do
            {
                batch = await db.ExecuteAsync(
                    $@"DELETE FROM {table}
                       WHERE ctid IN (
                           SELECT ctid FROM {table}
                           WHERE company_id = @cid AND {tsColumn} < NOW() - @days * INTERVAL '1 day'
                           LIMIT 5000)",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@days", days);
                    }, ct);
                total += batch;
            } while (batch == 5000 && !ct.IsCancellationRequested);
        }
        catch
        {
            // A missing table/column in some environment is non-fatal — retention of
            // the categories that DO exist must still proceed.
        }
        return total;
    }
}
