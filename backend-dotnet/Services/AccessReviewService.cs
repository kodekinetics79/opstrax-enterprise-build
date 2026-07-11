using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// AccessReviewService — Scoped
//
// Manages periodic access review campaigns. When a review is created, it
// snapshots all active users and their roles/permissions at that point in time.
// Reviewers can then approve or revoke each item.
//
// Design:
//   - All operations tenant-scoped via company_id
//   - Permissions snapshot is taken at creation time (not live)
//   - Revoke decisions create an explicit remediation requirement; they do not
//     silently disable an operational user without a separately approved action
//   - Completing a review auto-sets remaining pending items to status 'pending'
//     (reviewer must make explicit decisions before completing)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AccessReviewService(Database db, AuditService audit)
{
    public async Task<long> CreateAsync(
        long companyId,
        string title,
        long reviewerUserId,
        string? description,
        DateOnly? dueDate,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("title is required");

        var reviewer = await db.QuerySingleAsync(
            @"SELECT id FROM users
              WHERE id = @uid AND company_id = @cid
                AND status IN ('Active','active')
              LIMIT 1",
            c => { c.Parameters.AddWithValue("@uid", reviewerUserId); c.Parameters.AddWithValue("@cid", companyId); }, ct);
        if (reviewer is null)
            throw new ArgumentException("reviewer must be an active user in this tenant");

        var createdBy = http.Items.TryGetValue("opstrax.auth.user_id", out var uid)
            ? $"user:{uid}"
            : "system";

        var reviewId = await db.InsertAsync(
            @"INSERT INTO access_reviews
                (company_id, title, description, reviewer_user_id, status,
                 due_date, total_items, created_at, created_by)
              VALUES
                (@cid, @title, @desc, @reviewer, 'pending',
                 @due, 0, NOW(), @createdBy)",
            c =>
            {
                c.Parameters.AddWithValue("@cid",       companyId);
                c.Parameters.AddWithValue("@title",     title);
                c.Parameters.AddWithValue("@desc",      (object?)description ?? DBNull.Value);
                c.Parameters.AddWithValue("@reviewer",  reviewerUserId);
                c.Parameters.AddWithValue("@due",       dueDate.HasValue ? (object)dueDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
                c.Parameters.AddWithValue("@createdBy", createdBy);
            }, ct);

        // Snapshot all active users and their roles for this review
        var users = await db.QueryAsync(
            @"SELECT id, full_name, email, role_name
              FROM users
              WHERE company_id = @cid
                AND status IN ('Active', 'active')
              ORDER BY role_name, email
              LIMIT 500",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);

        var itemCount = 0;
        foreach (var user in users)
        {
            var userId    = Convert.ToInt64(user["id"]);
            var name      = user.GetValueOrDefault("fullName")?.ToString();
            var email     = user.GetValueOrDefault("email")?.ToString();
            var roleName  = user.GetValueOrDefault("roleName")?.ToString() ?? "Unknown";

            // Snapshot permissions from role_permissions table
            var perms = await db.QueryAsync(
                "SELECT permission_key FROM role_permissions WHERE role_id IN (SELECT role_id FROM users WHERE id=@uid AND company_id=@cid) LIMIT 100",
                c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@cid", companyId); }, ct);
            var permSnapshot = perms.Select(p => p.GetValueOrDefault("permissionKey")?.ToString())
                                    .Where(p => !string.IsNullOrWhiteSpace(p))
                                    .ToArray();

            await db.ExecuteAsync(
                @"INSERT INTO access_review_items
                    (review_id, company_id, target_user_id, target_user_name,
                     target_user_email, role_name, permissions_snapshot, status, created_at)
                  VALUES
                    (@rid, @cid, @uid, @name, @email, @role, @perms, 'pending', NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@rid",   reviewId);
                    c.Parameters.AddWithValue("@cid",   companyId);
                    c.Parameters.AddWithValue("@uid",   userId);
                    c.Parameters.AddWithValue("@name",  (object?)name  ?? DBNull.Value);
                    c.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
                    c.Parameters.AddWithValue("@role",  roleName);
                    c.Parameters.AddWithValue("@perms", JsonSerializer.Serialize(permSnapshot));
                }, ct);
            itemCount++;
        }

        // Update counts
        await db.ExecuteAsync(
            "UPDATE access_reviews SET total_items = @n, items_pending = @n, status = 'in_progress', started_at = NOW() WHERE id = @rid",
            c => { c.Parameters.AddWithValue("@n", itemCount); c.Parameters.AddWithValue("@rid", reviewId); }, ct);

        await audit.LogAsync(http, "access_review.created", "access_reviews", reviewId,
            System.Text.Json.JsonSerializer.Serialize(new { userCount = itemCount }), ct);

        return reviewId;
    }

    public Task<List<Dictionary<string, object?>>> GetForTenantAsync(
        long companyId, CancellationToken ct = default) =>
        db.QueryAsync(
            @"SELECT id, company_id, title, description, reviewer_user_id, status,
                     due_date, started_at, completed_at, total_items,
                     items_approved, items_revoked, items_pending, created_at, created_by
              FROM access_reviews
              WHERE company_id = @cid
              ORDER BY created_at DESC
              LIMIT 50",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);

    public async Task<Dictionary<string, object?>> GetDetailAsync(
        long companyId, long reviewId, CancellationToken ct = default)
    {
        var review = await db.QuerySingleAsync(
            @"SELECT id, company_id, title, description, reviewer_user_id, status,
                     due_date, started_at, completed_at, total_items,
                     items_approved, items_revoked, items_pending, created_at, created_by
              FROM access_reviews WHERE id = @rid AND company_id = @cid LIMIT 1",
            c => { c.Parameters.AddWithValue("@rid", reviewId); c.Parameters.AddWithValue("@cid", companyId); }, ct);

        if (review is null) throw new InvalidOperationException("Access review not found");

        var items = await db.QueryAsync(
            @"SELECT id, target_user_id, target_user_name, target_user_email,
                     role_name, permissions_snapshot, status, completed_at, completed_by, notes
              FROM access_review_items
              WHERE review_id = @rid AND company_id = @cid
              ORDER BY status, target_user_name
              LIMIT 500",
            c => { c.Parameters.AddWithValue("@rid", reviewId); c.Parameters.AddWithValue("@cid", companyId); }, ct);

        review["items"] = items;
        return review;
    }

    public async Task ApproveItemAsync(
        long companyId, long reviewId, long itemId,
        string? notes,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var actorId = GetActorId(http);
        await SetItemStatus(companyId, reviewId, itemId, "approved", actorId, notes, ct);
        await UpdateReviewCounts(companyId, reviewId, ct);
        await audit.LogAsync(http, "access_review.item.approved", "access_review_items", itemId, null, ct);
    }

    public async Task RevokeItemAsync(
        long companyId, long reviewId, long itemId,
        string? notes,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var actorId = GetActorId(http);
        await SetItemStatus(companyId, reviewId, itemId, "revoked", actorId, notes, ct);
        await UpdateReviewCounts(companyId, reviewId, ct);
        await audit.LogAsync(http, "access_review.item.revoked", "access_review_items", itemId,
            System.Text.Json.JsonSerializer.Serialize(new { notes }), ct);
    }

    public async Task CompleteAsync(
        long companyId, long reviewId,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        // Can only complete in_progress reviews
        var review = await db.QuerySingleAsync(
            "SELECT status, items_pending FROM access_reviews WHERE id = @rid AND company_id = @cid LIMIT 1",
            c => { c.Parameters.AddWithValue("@rid", reviewId); c.Parameters.AddWithValue("@cid", companyId); }, ct);

        if (review is null) throw new InvalidOperationException("Review not found");
        if (review["status"]?.ToString() == "completed") throw new InvalidOperationException("Review is already completed");
        if (Convert.ToInt32(review.GetValueOrDefault("itemsPending") ?? 0) > 0)
            throw new InvalidOperationException("Every access item requires an explicit decision before completion");

        await db.ExecuteAsync(
            "UPDATE access_reviews SET status = 'completed', completed_at = NOW() WHERE id = @rid AND company_id = @cid",
            c => { c.Parameters.AddWithValue("@rid", reviewId); c.Parameters.AddWithValue("@cid", companyId); }, ct);

        await audit.LogAsync(http, "access_review.completed", "access_reviews", reviewId, null, ct);
    }

    private async Task SetItemStatus(
        long companyId, long reviewId, long itemId,
        string status, string actorId, string? notes, CancellationToken ct)
    {
        var affected = await db.ExecuteAsync(
            @"UPDATE access_review_items
              SET status = @status, completed_at = NOW(), completed_by = @actor, notes = @notes
              WHERE id = @iid AND review_id = @rid AND company_id = @cid
                AND status = 'pending'
                AND EXISTS (SELECT 1 FROM access_reviews r
                            WHERE r.id = @rid AND r.company_id = @cid
                              AND r.status = 'in_progress')",
            c =>
            {
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@actor",  actorId);
                c.Parameters.AddWithValue("@notes",  (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@iid",    itemId);
                c.Parameters.AddWithValue("@rid",    reviewId);
                c.Parameters.AddWithValue("@cid",    companyId);
            }, ct);
        if (affected != 1)
            throw new InvalidOperationException("Pending access review item not found");
    }

    private Task UpdateReviewCounts(long companyId, long reviewId, CancellationToken ct) =>
        db.ExecuteAsync(
            @"UPDATE access_reviews
              SET items_approved = (SELECT COUNT(*) FROM access_review_items WHERE review_id = @rid AND company_id = @cid AND status = 'approved'),
                  items_revoked  = (SELECT COUNT(*) FROM access_review_items WHERE review_id = @rid AND company_id = @cid AND status = 'revoked'),
                  items_pending  = (SELECT COUNT(*) FROM access_review_items WHERE review_id = @rid AND company_id = @cid AND status = 'pending')
              WHERE id = @rid AND company_id = @cid",
            c => { c.Parameters.AddWithValue("@rid", reviewId); c.Parameters.AddWithValue("@cid", companyId); }, ct);

    private static string GetActorId(Microsoft.AspNetCore.Http.HttpContext http)
    {
        if (http.Items.TryGetValue("opstrax.auth.user_id", out var uid) && uid is not null)
            return $"user:{uid}";
        return "system";
    }
}
