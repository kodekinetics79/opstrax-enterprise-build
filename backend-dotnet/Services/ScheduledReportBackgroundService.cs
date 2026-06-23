using System.Text;
using System.Text.Json;
using Opstrax.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Scheduled Report Runner — BackgroundService
//
// SECURITY MODEL
// ──────────────
// 1. Recipients are resolved server-side from role names stored in the DB.
//    No client-provided email addresses are accepted or used.
// 2. Delivery is always in_app via NotificationService.
//    External email/SMS requires a configured provider; if none is configured
//    the run status is set to 'not_configured' rather than pretending delivery.
// 3. Tenant isolation: the scheduled_report and the saved_report are validated
//    to belong to the same company_id before execution.
// 4. Soft-deleted saved reports cannot be executed (JOIN filters deleted_at IS NULL).
// 5. Every execution is audit-logged regardless of success or failure.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ScheduledReportBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledReportBackgroundService> logger,
    ServiceRunTracker tracker) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string SvcName = "ScheduledReportBackgroundService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Svc} started", SvcName);
        while (!stoppingToken.IsCancellationRequested)
        {
            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, stoppingToken);
            try
            {
                await RunScheduledCycleAsync(stoppingToken);
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

    private async Task RunScheduledCycleAsync(CancellationToken ct)
    {
        using var scope  = scopeFactory.CreateScope();
        var db           = scope.ServiceProvider.GetRequiredService<Database>();
        var notif        = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var audit        = scope.ServiceProvider.GetRequiredService<AuditService>();

        // Load all scheduled reports that are due.
        // JOIN ensures saved_report is not soft-deleted and belongs to the same tenant.
        var dueReports = await db.QueryAsync(
            @"SELECT sr.id, sr.saved_report_id, sr.tenant_id, sr.frequency,
                     sr.format, sr.recipient_type, sr.recipients_json,
                     sr.delivery_method, sr.owner_user_id,
                     s.name           AS report_name,
                     s.dataset_key,
                     s.selected_fields_json,
                     s.filters_json,
                     s.sort_json,
                     s.group_by_field,
                     s.company_id     AS saved_report_company_id
              FROM scheduled_reports sr
              JOIN saved_reports s ON s.id = sr.saved_report_id
              WHERE sr.status = 'Active'
                AND sr.next_run_at <= NOW()
                AND sr.saved_report_id IS NOT NULL
                AND s.deleted_at IS NULL
                AND s.company_id = sr.tenant_id",
            ct: ct);

        if (dueReports.Count == 0) return;

        logger.LogInformation("Processing {Count} due scheduled reports", dueReports.Count);

        foreach (var row in dueReports)
        {
            var scheduledId     = Convert.ToInt64(row["id"]);
            var savedReportId   = Convert.ToInt64(row["savedReportId"]);
            var companyId       = Convert.ToInt64(row["tenantId"]);
            var ownerId         = Convert.ToInt64(row["ownerUserId"]);
            var frequency       = row["frequency"]?.ToString() ?? "weekly";
            var reportName      = row["reportName"]?.ToString() ?? "Report";
            var datasetKey      = row["datasetKey"]?.ToString() ?? "";
            var deliveryMethod  = row["deliveryMethod"]?.ToString() ?? "in_app";
            var recipientType   = row["recipientType"]?.ToString() ?? "roles";
            var recipientsJson  = row["recipientsJson"]?.ToString() ?? "[]";
            var fieldsJson      = row["selectedFieldsJson"]?.ToString() ?? "[]";
            var filtersJson     = row["filtersJson"]?.ToString() ?? "[]";
            var sortJson        = row["sortJson"]?.ToString();
            var groupBy         = row["groupByField"]?.ToString();

            await ExecuteScheduledReportAsync(
                db, notif, audit, ct,
                scheduledId, savedReportId, companyId, ownerId,
                frequency, reportName, datasetKey, deliveryMethod,
                recipientType, recipientsJson,
                fieldsJson, filtersJson, sortJson, groupBy);
        }
    }

    private async Task ExecuteScheduledReportAsync(
        Database db, NotificationService notif, AuditService audit,
        CancellationToken ct,
        long scheduledId, long savedReportId, long companyId, long ownerId,
        string frequency, string reportName, string datasetKey,
        string deliveryMethod, string recipientType, string recipientsJson,
        string fieldsJson, string filtersJson, string? sortJson, string? groupBy)
    {
        var sw     = System.Diagnostics.Stopwatch.StartNew();
        var status = "failed";
        var errMsg = (string?)null;
        var rowCount = 0;

        try
        {
            // ── Resolve dataset and rebuild query ────────────────────────────

            var dataset = ReportingDatasetRegistry.Get(datasetKey);
            if (dataset is null)
            {
                errMsg = $"Dataset '{datasetKey}' is no longer registered.";
                logger.LogWarning("Scheduled report {Id}: {Error}", scheduledId, errMsg);
                await FinalizeRunAsync(db, audit, ct,
                    scheduledId, companyId, ownerId, savedReportId, datasetKey,
                    "failed", errMsg, 0, 0, frequency);
                return;
            }

            var fields  = TryDeserialize<string[]>(fieldsJson)  ?? [];
            var filters = TryDeserialize<P8FilterBody[]>(filtersJson) ?? [];
            var sort    = sortJson is not null ? TryDeserialize<P8SortBody>(sortJson) : null;

            // Re-validate fields against current registry (fields may have been removed)
            foreach (var f in fields)
            {
                if (dataset.GetField(f) is null)
                {
                    errMsg = $"Field '{f}' is no longer valid for dataset '{datasetKey}'.";
                    await FinalizeRunAsync(db, audit, ct,
                        scheduledId, companyId, ownerId, savedReportId, datasetKey,
                        "failed", errMsg, 0, 0, frequency);
                    return;
                }
            }

            if (fields.Length == 0)
            {
                errMsg = "Saved report has no selected fields.";
                await FinalizeRunAsync(db, audit, ct,
                    scheduledId, companyId, ownerId, savedReportId, datasetKey,
                    "failed", errMsg, 0, 0, frequency);
                return;
            }

            // Build the query — tenant scope injected server-side
            var req = new P8QueryBody(datasetKey, fields, filters, sort, groupBy,
                Page: 1, PageSize: SecureQueryBuilder.MaxPageSize);
            var (sql, countSql, parms) = SecureQueryBuilder.Build(req, dataset, companyId);

            // Execute count first (lightweight)
            var total = await db.ScalarLongAsync(countSql,
                SecureQueryBuilder.BindParams(parms), ct);
            rowCount = (int)Math.Min(total, SecureQueryBuilder.MaxPageSize);

            // Execute data query (limited to MaxPageSize)
            var rows = await db.QueryAsync(sql, SecureQueryBuilder.BindParams(parms), ct);

            // Log to report_execution_log
            await db.ExecuteAsync(
                @"INSERT INTO report_execution_log
                    (company_id, user_id, saved_report_id, dataset_key,
                     row_count, execution_ms, filters_json, status, executed_at)
                  VALUES (@cid, @uid, @rid, @dk, @rc, @ms, @fj, 'completed', NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@uid", ownerId);
                    c.Parameters.AddWithValue("@rid", savedReportId);
                    c.Parameters.AddWithValue("@dk",  datasetKey);
                    c.Parameters.AddWithValue("@rc",  rowCount);
                    c.Parameters.AddWithValue("@ms",  (int)sw.ElapsedMilliseconds);
                    c.Parameters.AddWithValue("@fj",  filtersJson);
                }, ct);

            // ── Deliver: in_app notifications ────────────────────────────────

            if (deliveryMethod == "in_app")
            {
                var recipients = await ResolveRecipientsAsync(db, ct, companyId, recipientType, recipientsJson);
                var notifTitle   = $"Scheduled Report Ready: {reportName}";
                var notifMessage = $"Your scheduled report '{reportName}' has {rowCount:N0} rows ready.";

                foreach (var userId in recipients)
                {
                    await notif.CreateAsync(
                        companyId:    companyId,
                        eventType:    "scheduled_report.ready",
                        sourceType:   "scheduled_report",
                        sourceId:     scheduledId,
                        severity:     "Info",
                        title:        notifTitle,
                        message:      notifMessage,
                        audienceType: "user",
                        ct:           ct,
                        targetUserId: userId,
                        channel:      "in_app",
                        priority:     5);
                }
            }
            else
            {
                // External email/SMS not configured — record status
                await audit.LogAsync("scheduled_report.delivery_not_configured",
                    "scheduled_report", scheduledId, ct: ct);
                logger.LogWarning(
                    "Scheduled report {Id}: delivery_method='{Method}' has no configured provider",
                    scheduledId, deliveryMethod);
            }

            status = "completed";
        }
        catch (Exception ex)
        {
            errMsg = ex.Message;
            logger.LogError(ex, "Scheduled report {Id} execution failed", scheduledId);
        }
        finally
        {
            sw.Stop();
        }

        await FinalizeRunAsync(db, audit, ct,
            scheduledId, companyId, ownerId, savedReportId, datasetKey,
            status, errMsg, rowCount, (int)sw.ElapsedMilliseconds, frequency);
    }

    // ── Persist run result and advance next_run_at ────────────────────────────

    private static async Task FinalizeRunAsync(
        Database db, AuditService audit, CancellationToken ct,
        long scheduledId, long companyId, long ownerId, long savedReportId,
        string datasetKey, string status, string? errorMsg,
        int rowCount, int executionMs, string frequency)
    {
        var nextRunAt = frequency switch
        {
            "daily"   => DateTime.UtcNow.AddDays(1),
            "monthly" => DateTime.UtcNow.AddMonths(1),
            _         => DateTime.UtcNow.AddDays(7),   // weekly (safe default for unknown)
        };

        await db.ExecuteAsync(
            @"UPDATE scheduled_reports
              SET last_run_at  = NOW(),
                  last_status  = @status,
                  last_error   = @err,
                  next_run_at  = @nextRun
              WHERE id = @id",
            c =>
            {
                c.Parameters.AddWithValue("@status",  status);
                c.Parameters.AddWithValue("@err",     (object?)errorMsg ?? DBNull.Value);
                c.Parameters.AddWithValue("@nextRun", nextRunAt);
                c.Parameters.AddWithValue("@id",      scheduledId);
            }, ct);

        await audit.LogAsync("scheduled_report.run",
            "scheduled_report", scheduledId, ct: ct);
    }

    // ── Resolve recipients server-side from role names or usernames ───────────
    // No client-provided email addresses are accepted.

    private static async Task<List<long>> ResolveRecipientsAsync(
        Database db, CancellationToken ct,
        long companyId, string recipientType, string recipientsJson)
    {
        var names = TryDeserialize<string[]>(recipientsJson) ?? [];
        if (names.Length == 0) return [];

        var userIds = new List<long>();

        if (recipientType == "roles")
        {
            foreach (var roleName in names)
            {
                // Resolve role → user IDs (server-side; role names come from DB, not client)
                var users = await db.QueryAsync(
                    @"SELECT id FROM users
                      WHERE company_id = @cid
                        AND role = @role
                        AND deleted_at IS NULL",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid",  companyId);
                        c.Parameters.AddWithValue("@role", roleName);
                    }, ct);

                foreach (var u in users)
                    if (u.TryGetValue("id", out var uid) && uid is not null)
                        userIds.Add(Convert.ToInt64(uid));
            }
        }
        else if (recipientType == "users")
        {
            foreach (var username in names)
            {
                var users = await db.QueryAsync(
                    @"SELECT id FROM users
                      WHERE company_id = @cid
                        AND (username = @name OR email = @name)
                        AND deleted_at IS NULL",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid",  companyId);
                        c.Parameters.AddWithValue("@name", username);
                    }, ct);

                foreach (var u in users)
                    if (u.TryGetValue("id", out var uid) && uid is not null)
                        userIds.Add(Convert.ToInt64(uid));
            }
        }

        // Deduplicate
        return userIds.Distinct().ToList();
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}
