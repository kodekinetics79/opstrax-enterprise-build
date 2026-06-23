using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// DataRetentionService — Scoped
//
// Manages tenant-level data retention policies.
// Physical deletion is never triggered from this service — it only stores policy.
// A separate, explicitly-tested retention worker (not built here) would execute
// deletions after verifying the policy and the legal_hold_active flag.
//
// SAFETY: When legal_hold_active = 1, no data must be deleted for any category.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DataRetentionService(Database db, AuditService audit)
{
    public static RetentionPolicy Defaults(long companyId) => new()
    {
        CompanyId            = companyId,
        AuditLogDays         = 90,
        TelemetryDays        = 90,
        NotificationDays     = 30,
        ReportExecutionDays  = 180,
        SecurityEventDays    = 365,
        SoftDeleteOnly       = true,
        LegalHoldActive      = false,
    };

    public async Task<RetentionPolicy> GetAsync(long companyId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT * FROM data_retention_policies WHERE company_id = @cid LIMIT 1",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);

        if (row is null) return Defaults(companyId);

        return new RetentionPolicy
        {
            CompanyId           = companyId,
            AuditLogDays        = Convert.ToInt32(row["auditLogDays"]        ?? 90),
            TelemetryDays       = Convert.ToInt32(row["telemetryDays"]       ?? 90),
            NotificationDays    = Convert.ToInt32(row["notificationDays"]    ?? 30),
            ReportExecutionDays = Convert.ToInt32(row["reportExecutionDays"] ?? 180),
            SecurityEventDays   = Convert.ToInt32(row["securityEventDays"]   ?? 365),
            SoftDeleteOnly      = Convert.ToBoolean(row["softDeleteOnly"]    ?? true),
            LegalHoldActive     = Convert.ToBoolean(row["legalHoldActive"]   ?? false),
            LegalHoldReason     = row.GetValueOrDefault("legalHoldReason")?.ToString(),
            LegalHoldSetAt      = row.GetValueOrDefault("legalHoldSetAt") is DateTime d ? d : null,
            LegalHoldSetBy      = row.GetValueOrDefault("legalHoldSetBy")?.ToString(),
            UpdatedAt           = row.GetValueOrDefault("updatedAt")    is DateTime u ? u : null,
            UpdatedBy           = row.GetValueOrDefault("updatedBy")?.ToString(),
        };
    }

    public async Task UpsertAsync(
        long companyId,
        RetentionPolicy policy,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var updatedBy = http.Items.TryGetValue("opstrax.auth.user_id", out var uid)
            ? $"user:{uid}"
            : "system";

        // Cannot disable legal hold via this method — requires separate explicit action
        var existing = await GetAsync(companyId, ct);
        var legalHoldActive = existing.LegalHoldActive || policy.LegalHoldActive;

        await db.ExecuteAsync(
            @"INSERT INTO data_retention_policies
                (company_id, audit_log_days, telemetry_days, notification_days,
                 report_execution_days, security_event_days, soft_delete_only,
                 legal_hold_active, legal_hold_reason, legal_hold_set_at, legal_hold_set_by,
                 created_at, updated_at, updated_by)
              VALUES
                (@cid, @audit, @tele, @notif,
                 @report, @sec, @soft,
                 @hold, @holdReason, @holdAt, @holdBy,
                 NOW(), NOW(), @upd)
              ON DUPLICATE KEY UPDATE
                audit_log_days        = VALUES(audit_log_days),
                telemetry_days        = VALUES(telemetry_days),
                notification_days     = VALUES(notification_days),
                report_execution_days = VALUES(report_execution_days),
                security_event_days   = VALUES(security_event_days),
                soft_delete_only      = VALUES(soft_delete_only),
                legal_hold_active     = @hold,
                legal_hold_reason     = COALESCE(@holdReason, legal_hold_reason),
                legal_hold_set_at     = IF(@hold = 1 AND legal_hold_active = 0, NOW(), legal_hold_set_at),
                legal_hold_set_by     = IF(@hold = 1 AND legal_hold_active = 0, @upd, legal_hold_set_by),
                updated_at            = NOW(),
                updated_by            = VALUES(updated_by)",
            c =>
            {
                c.Parameters.AddWithValue("@cid",        companyId);
                c.Parameters.AddWithValue("@audit",      Math.Max(30, policy.AuditLogDays));
                c.Parameters.AddWithValue("@tele",       Math.Max(7,  policy.TelemetryDays));
                c.Parameters.AddWithValue("@notif",      Math.Max(7,  policy.NotificationDays));
                c.Parameters.AddWithValue("@report",     Math.Max(30, policy.ReportExecutionDays));
                c.Parameters.AddWithValue("@sec",        Math.Max(90, policy.SecurityEventDays));
                c.Parameters.AddWithValue("@soft",       policy.SoftDeleteOnly ? 1 : 0);
                c.Parameters.AddWithValue("@hold",       legalHoldActive ? 1 : 0);
                c.Parameters.AddWithValue("@holdReason", (object?)policy.LegalHoldReason ?? DBNull.Value);
                c.Parameters.AddWithValue("@holdAt",     (object?)policy.LegalHoldSetAt  ?? DBNull.Value);
                c.Parameters.AddWithValue("@holdBy",     (object?)policy.LegalHoldSetBy  ?? DBNull.Value);
                c.Parameters.AddWithValue("@upd",        updatedBy);
            }, ct);

        await audit.LogAsync(http, "data_retention.policy.updated", "data_retention_policies",
            companyId, null, ct);
    }
}

public sealed class RetentionPolicy
{
    public long CompanyId { get; init; }
    public int AuditLogDays { get; init; } = 90;
    public int TelemetryDays { get; init; } = 90;
    public int NotificationDays { get; init; } = 30;
    public int ReportExecutionDays { get; init; } = 180;
    public int SecurityEventDays { get; init; } = 365;
    public bool SoftDeleteOnly { get; init; } = true;
    public bool LegalHoldActive { get; init; }
    public string? LegalHoldReason { get; init; }
    public DateTime? LegalHoldSetAt { get; init; }
    public string? LegalHoldSetBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}
