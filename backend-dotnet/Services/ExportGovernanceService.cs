using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ExportGovernanceService — Scoped
//
// Manages export request/approval workflow when tenant has export_approval_required=1.
// All export activity is logged regardless of approval setting.
//
// Flow:
//   1. User requests export → creates export_requests row with status pending_approval
//   2. Admin approves/rejects → updates status + logs approver
//   3. On approval, actual export can proceed
//   4. If approval not required → status auto-set to 'approved' immediately
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ExportGovernanceService(Database db, AuditService audit, SecurityEventService secEvent)
{
    public async Task<(long requestId, bool requiresApproval)> RequestAsync(
        long companyId,
        long requestedByUserId,
        string requestedByName,
        string exportType,
        string? datasetName,
        int? rowCountEstimate,
        bool approvalRequired,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var initialStatus = approvalRequired ? "pending_approval" : "approved";

        var id = await db.InsertAsync(
            @"INSERT INTO export_requests
                (company_id, requested_by_user_id, requested_by_name, export_type,
                 dataset_name, row_count_estimate, status, created_at)
              VALUES
                (@cid, @uid, @name, @type,
                 @dataset, @rows, @status, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@cid",     companyId);
                c.Parameters.AddWithValue("@uid",     requestedByUserId);
                c.Parameters.AddWithValue("@name",    requestedByName);
                c.Parameters.AddWithValue("@type",    exportType);
                c.Parameters.AddWithValue("@dataset", (object?)datasetName     ?? DBNull.Value);
                c.Parameters.AddWithValue("@rows",    (object?)rowCountEstimate ?? DBNull.Value);
                c.Parameters.AddWithValue("@status",  initialStatus);
            }, ct);

        await audit.LogAsync(http, $"export.{(approvalRequired ? "approval_requested" : "auto_approved")}",
            "export_requests", id,
            JsonSerializer.Serialize(new { exportType, datasetName, rowCountEstimate }), ct);

        await secEvent.LogAsync(companyId, requestedByUserId,
            approvalRequired ? "export.requested" : "export.approved",
            "info", null, null, true,
            $"Export requested: {exportType}{(datasetName != null ? " / " + datasetName : "")}",
            new { requestId = id, approvalRequired }, ct);

        return (id, approvalRequired);
    }

    public Task<List<Dictionary<string, object?>>> GetRequestsAsync(
        long companyId, string? status = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            return db.QueryAsync(
                @"SELECT id, company_id, requested_by_user_id, requested_by_name,
                         export_type, dataset_name, row_count_estimate, status,
                         approved_by_name, reviewed_at, review_notes, created_at, completed_at
                  FROM export_requests
                  WHERE company_id = @cid AND status = @s
                  ORDER BY created_at DESC
                  LIMIT 200",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@s",   status);
                }, ct);
        }

        return db.QueryAsync(
            @"SELECT id, company_id, requested_by_user_id, requested_by_name,
                     export_type, dataset_name, row_count_estimate, status,
                     approved_by_name, reviewed_at, review_notes, created_at, completed_at
              FROM export_requests
              WHERE company_id = @cid
              ORDER BY created_at DESC
              LIMIT 200",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
    }

    public async Task ApproveAsync(
        long companyId, long requestId,
        long approverUserId, string approverName,
        string? notes,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var rows = await db.ExecuteAsync(
            @"UPDATE export_requests
              SET status = 'approved', approved_by_user_id = @approverId,
                  approved_by_name = @approverName, reviewed_at = NOW(),
                  review_notes = @notes
              WHERE id = @rid AND company_id = @cid AND status = 'pending_approval'",
            c =>
            {
                c.Parameters.AddWithValue("@approverId",   approverUserId);
                c.Parameters.AddWithValue("@approverName", approverName);
                c.Parameters.AddWithValue("@notes",        (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@rid",          requestId);
                c.Parameters.AddWithValue("@cid",          companyId);
            }, ct);

        if (rows == 0) throw new InvalidOperationException("Export request not found or not in pending_approval status");

        await audit.LogAsync(http, "export.approved", "export_requests", requestId, null, ct);
        await secEvent.LogAsync(companyId, approverUserId, "export.approved", "info",
            null, null, true, $"Export request {requestId} approved by {approverName}", new { requestId }, ct);
    }

    public async Task RejectAsync(
        long companyId, long requestId,
        long approverUserId, string approverName,
        string? notes,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var rows = await db.ExecuteAsync(
            @"UPDATE export_requests
              SET status = 'rejected', approved_by_user_id = @approverId,
                  approved_by_name = @approverName, reviewed_at = NOW(),
                  review_notes = @notes
              WHERE id = @rid AND company_id = @cid AND status = 'pending_approval'",
            c =>
            {
                c.Parameters.AddWithValue("@approverId",   approverUserId);
                c.Parameters.AddWithValue("@approverName", approverName);
                c.Parameters.AddWithValue("@notes",        (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@rid",          requestId);
                c.Parameters.AddWithValue("@cid",          companyId);
            }, ct);

        if (rows == 0) throw new InvalidOperationException("Export request not found or not in pending_approval status");

        await audit.LogAsync(http, "export.rejected", "export_requests", requestId,
            JsonSerializer.Serialize(new { notes }), ct);
        await secEvent.LogAsync(companyId, approverUserId, "export.denied", "medium",
            null, null, false, $"Export request {requestId} rejected: {notes ?? "no reason given"}", new { requestId }, ct);
    }
}
