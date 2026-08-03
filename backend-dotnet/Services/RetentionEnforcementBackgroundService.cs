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
//   • Every run is heartbeated + system-audited; total row counts are recorded.
//   • Production startup fails unless RetentionWorker:Enabled=true. A published
//     retention policy may never silently outlive its executor.
//
// BOUNDARY: this worker deletes only the three database categories named below.
// It does NOT delete uploaded files/object-store evidence and is not a substitute
// for the separately audited, per-subject DSR erasure process.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RetentionEnforcementBackgroundService(
    IServiceScopeFactory scopeFactory,
    ServiceRunTracker tracker,
    IConfiguration config,
    ILogger<RetentionEnforcementBackgroundService> logger) : BackgroundService
{
    private const string ServiceName = "RetentionEnforcementService";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan IdleHeartbeatInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    internal const int MaxBatchesPerCategoryPerCycle = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ConfigValidationService refuses Production startup unless this is explicit
        // true. The local/non-production default stays enabled for rehearsal coverage.
        var isProd = string.Equals(
            config["ASPNETCORE_ENVIRONMENT"] ?? config["DOTNET_ENVIRONMENT"], "Production",
            StringComparison.OrdinalIgnoreCase);
        var enabled = config.GetValue("RetentionWorker:Enabled", !isProd);
        if (!enabled)
        {
            logger.LogInformation("[Retention] Disabled (RetentionWorker:Enabled=false). Policies stored but not enforced.");
            return;
        }

        // Small startup delay so it never contends with schema init / first traffic.
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try { await WaitWithHeartbeatAsync(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task WaitWithHeartbeatAsync(TimeSpan delay, CancellationToken ct)
    {
        var remaining = delay;
        while (remaining > TimeSpan.Zero)
        {
            var slice = remaining < IdleHeartbeatInterval ? remaining : IdleHeartbeatInterval;
            await Task.Delay(slice, ct);
            remaining -= slice;
            await tracker.PulseAsync(ServiceName, ct);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var runId = await tracker.BeginAsync(ServiceName, ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var totalDeleted = 0;
        var failures = new List<RetentionPurgeFailure>();

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

                    totalDeleted += await TryPurgeAsync(db, "location_events",  "received_at", telemetryDays, companyId, failures, ct);
                    totalDeleted += await TryPurgeAsync(db, "notifications",    "created_at",  notificationDays, companyId, failures, ct);
                    totalDeleted += await TryPurgeAsync(db, "report_execution_log", "executed_at", reportDays, companyId, failures, ct);
                }
            }, ct);

            if (failures.Count > 0)
                throw new RetentionPurgeCycleException(failures);

            sw.Stop();
            await LogAuditOutcomeAsync("data_retention.enforcement.succeeded", runId, ct);
            await tracker.CompleteAsync(runId, ServiceName, totalDeleted, (int)sw.ElapsedMilliseconds, ct);
            logger.LogInformation(new EventId(20, "retention_cycle_succeeded"),
                "[Retention] Cycle succeeded; purged {Count} expired operational rows.", totalDeleted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("[Retention] Enforcement cycle cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            await tracker.FailAsync(runId, ServiceName, ex, (int)sw.ElapsedMilliseconds, ct);
            try
            {
                await LogAuditOutcomeAsync("data_retention.enforcement.failed", runId, ct);
            }
            catch (Exception auditEx)
            {
                logger.LogError(new EventId(22, "retention_failure_audit_failed"), auditEx,
                    "[Retention] Failed to persist the system audit for a failed cycle");
            }
            logger.LogError(new EventId(21, "retention_cycle_failed"), ex,
                "[Retention] Enforcement cycle failed; rows attempted before failure: {Count}; failed categories: {FailedCategoryCount}",
                totalDeleted, failures.Count);
        }
    }

    private async Task LogAuditOutcomeAsync(string action, long runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        await db.RunInSystemScopeAsync(
            () => audit.LogSystemAsync(action, "retention_cycle", runId, ct: ct), ct);
    }

    private async Task<int> TryPurgeAsync(
        Database db, string table, string tsColumn, int days, long companyId,
        List<RetentionPurgeFailure> failures, CancellationToken ct)
    {
        try
        {
            return await PurgeAsync(db, table, tsColumn, days, companyId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures.Add(new RetentionPurgeFailure(table, ex.GetType().Name));
            logger.LogError(new EventId(23, "retention_category_purge_failed"), ex,
                "[Retention] Category purge failed for {Category}; tenant {Tenant}; error code {ErrorCode}",
                table, companyId, ex.GetType().Name);
            return 0;
        }
    }

    // Deletes rows older than <days> for a tenant, in bounded batches so a large
    // backlog never locks the table. Table/column names are code constants (never
    // user input) so string interpolation here is safe.
    internal static async Task<int> PurgeAsync(
        Database db, string table, string tsColumn, int days, long companyId, CancellationToken ct)
    {
        var total = 0;
        int batch;
        var batches = 0;
        do
        {
            // The worker runs in an ambient system transaction in Production. A
            // savepoint keeps one category error from poisoning that transaction,
            // allowing a complete failure inventory before the cycle is failed.
            batch = await db.ExecuteWithSavepointAsync(
                $@"DELETE FROM {table}
                   WHERE ctid IN (
                       SELECT ctid FROM {table}
                       WHERE company_id = @cid AND {tsColumn} < NOW() - @days * INTERVAL '1 day'
                         AND EXISTS (
                           SELECT 1 FROM data_retention_policies policy
                           WHERE policy.company_id=@cid
                             AND COALESCE(policy.legal_hold_active,false)=false)
                       LIMIT 5000)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@days", days);
                }, ct);
            total += batch;
            batches++;
        } while (batch == 5000 && batches < MaxBatchesPerCategoryPerCycle && !ct.IsCancellationRequested);
        return total;
    }
}

internal sealed record RetentionPurgeFailure(string Category, string ErrorCode);

internal sealed class RetentionPurgeCycleException(IReadOnlyCollection<RetentionPurgeFailure> failures)
    : Exception($"Retention purge failed for {failures.Count} category operation(s): " +
                string.Join(",", failures.Select(f => $"{f.Category}:{f.ErrorCode}").Distinct(StringComparer.Ordinal)))
{
    internal IReadOnlyCollection<RetentionPurgeFailure> Failures { get; } = failures;
}
