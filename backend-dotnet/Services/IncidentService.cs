using Opstrax.Api.Data;
using Opstrax.Api.Observability;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// IncidentService — Scoped
//
// Creates, queries, and resolves operational platform incidents.
// Incidents are platform-level (company_id nullable) and are created
// automatically by ServiceRunTracker on repeated background service failures.
//
// SECURITY: No tenant customer/driver data leaks into incident records.
// All descriptions are safe_description — pre-sanitized by the caller.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class IncidentService(Database db)
{
    // ── Create ────────────────────────────────────────────────────────────────

    /// <summary>Creates an incident only if no open/investigating incident exists for
    /// the same sourceService+sourceEvent combination. Idempotent.</summary>
    public async Task<long> CreateIfNotExistsAsync(
        string severity,
        string sourceService,
        string sourceEvent,
        string title,
        string? safeDescription = null,
        long? companyId         = null,
        CancellationToken ct    = default)
    {
        var existing = await db.ScalarLongAsync(
            @"SELECT id FROM platform_incidents
              WHERE source_service = @svc AND source_event = @evt
                AND status IN ('open','investigating')
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@svc", sourceService);
                c.Parameters.AddWithValue("@evt", sourceEvent);
            }, ct);

        if (existing > 0) return existing;

        // Auto-link the incident to the trace + deployment that surfaced it (from the
        // ambient TelemetryContext when created inside a request), so an operator can
        // pivot straight from the incident to the failing request's trace.
        var tc = TelemetryContext.Current;

        return await db.ScalarLongAsync(
            @"INSERT INTO platform_incidents
                (company_id, severity, source_service, source_event,
                 status, title, safe_description, opened_at,
                 affected_service, trace_id, deployment_version)
              VALUES (@cid, @sev, @svc, @evt, 'open', @title, @desc, NOW(),
                 @svc, @trace, @ver)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid",   (object?)companyId ?? DBNull.Value);
                c.Parameters.AddWithValue("@sev",   severity);
                c.Parameters.AddWithValue("@svc",   sourceService);
                c.Parameters.AddWithValue("@evt",   sourceEvent);
                c.Parameters.AddWithValue("@title", title);
                c.Parameters.AddWithValue("@desc",  (object?)safeDescription ?? DBNull.Value);
                c.Parameters.AddWithValue("@trace", (object?)tc?.TraceId ?? DBNull.Value);
                c.Parameters.AddWithValue("@ver",   (object?)tc?.DeploymentVersion ?? BuildInfo.Version);
            }, ct);
    }

    // ── Acknowledge / Resolve (audit trail) ─────────────────────────────────────

    /// <summary>Records acknowledgement (sets acknowledged_at + who), moving the
    /// incident to 'investigating'. Idempotent — first ack wins.</summary>
    public Task AcknowledgeAsync(long id, string acknowledgedBy, CancellationToken ct = default) =>
        db.ExecuteAsync(
            @"UPDATE platform_incidents
              SET status = CASE WHEN status = 'open' THEN 'investigating' ELSE status END,
                  acknowledged_at = COALESCE(acknowledged_at, NOW()),
                  acknowledged_by = COALESCE(acknowledged_by, @by)
              WHERE id = @id",
            c =>
            {
                c.Parameters.AddWithValue("@by", acknowledgedBy);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

    /// <summary>Resolves an incident with a root-cause + actions-taken record.</summary>
    public Task ResolveAsync(
        long id, string? rootCause, string? actionsTaken, string? resolvedBy,
        CancellationToken ct = default) =>
        db.ExecuteAsync(
            @"UPDATE platform_incidents
              SET status = 'resolved',
                  resolved_at = COALESCE(resolved_at, NOW()),
                  root_cause = @rc,
                  actions_taken = @at,
                  assigned_to = COALESCE(@by, assigned_to)
              WHERE id = @id",
            c =>
            {
                c.Parameters.AddWithValue("@rc", (object?)rootCause ?? DBNull.Value);
                c.Parameters.AddWithValue("@at", (object?)actionsTaken ?? DBNull.Value);
                c.Parameters.AddWithValue("@by", (object?)resolvedBy ?? DBNull.Value);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

    // ── Update status ─────────────────────────────────────────────────────────

    public async Task UpdateStatusAsync(
        long id, string newStatus, string? assignedTo = null,
        CancellationToken ct = default)
    {
        var resolvedAt = newStatus == "resolved" ? "NOW()" : "NULL";
        await db.ExecuteAsync(
            $@"UPDATE platform_incidents
              SET status = @status,
                  assigned_to = @assignedTo,
                  resolved_at = {(newStatus == "resolved" ? "NOW()" : "resolved_at")}
              WHERE id = @id",
            c =>
            {
                c.Parameters.AddWithValue("@status",     newStatus);
                c.Parameters.AddWithValue("@assignedTo", (object?)assignedTo ?? DBNull.Value);
                c.Parameters.AddWithValue("@id",         id);
            }, ct);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    private const string IncidentColumns =
        @"id, company_id, severity, source_service, source_event,
          status, title, safe_description, opened_at, resolved_at, assigned_to,
          acknowledged_at, acknowledged_by, affected_service, affected_tenants,
          root_cause, actions_taken, trace_id, deployment_version";

    public Task<List<Dictionary<string, object?>>> GetOpenAsync(CancellationToken ct = default) =>
        db.QueryAsync(
            $@"SELECT {IncidentColumns}
              FROM platform_incidents
              WHERE status IN ('open','investigating','mitigated')
              ORDER BY opened_at DESC
              LIMIT 200", ct: ct);

    public Task<List<Dictionary<string, object?>>> GetRecentAsync(
        int hours = 48, CancellationToken ct = default) =>
        db.QueryAsync(
            $@"SELECT {IncidentColumns}
              FROM platform_incidents
              WHERE opened_at >= NOW() - @h * INTERVAL '1 hour'
              ORDER BY opened_at DESC
              LIMIT 500",
            c => c.Parameters.AddWithValue("@h", hours), ct);
}
