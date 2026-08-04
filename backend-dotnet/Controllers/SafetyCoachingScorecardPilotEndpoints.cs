using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private static string CoachingBranchScope(HttpContext http, string taskAlias = "ct") =>
        GetBranchId(http) is null
            ? string.Empty
            : $" AND EXISTS (SELECT 1 FROM drivers scoped_driver WHERE scoped_driver.id={taskAlias}.driver_id AND scoped_driver.company_id={taskAlias}.company_id AND scoped_driver.branch_id=@branchId AND scoped_driver.deleted_at IS NULL)";

    private static void BindTenantAndBranch(NpgsqlCommand command, HttpContext http)
    {
        command.Parameters.AddWithValue("@cid", GetCompanyId(http));
        if (GetBranchId(http) is { } branchId) command.Parameters.AddWithValue("@branchId", branchId);
    }

    private static long? BodyLong(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var raw) || raw is null || raw is DBNull) return null;
        return long.TryParse(raw.ToString(), out var value) ? value : null;
    }

    private static string? BodyText(Dictionary<string, object?> body, string key) =>
        body.TryGetValue(key, out var raw) && raw is not null && raw is not DBNull
            ? raw.ToString()?.Trim()
            : null;

    private static DateTimeOffset? BodyDate(Dictionary<string, object?> body, string key) =>
        DateTimeOffset.TryParse(BodyText(body, key), out var value) ? value : null;

    internal static bool AllowedCoachingTransition(string current, string target) =>
        (current.Trim().ToLowerInvariant(), target.Trim().ToLowerInvariant()) switch
        {
            ("draft", "assigned") or ("draft", "cancelled") => true,
            ("open", "assigned") or ("open", "cancelled") => true,
            ("assigned", "driver acknowledged") or
            ("assigned", "escalated") or ("assigned", "cancelled") => true,
            ("driver acknowledged", "completed") or ("driver acknowledged", "escalated") or
            ("driver acknowledged", "cancelled") => true,
            ("escalated", "assigned") or ("escalated", "completed") or ("escalated", "cancelled") => true,
            _ => false
        };

    // Server-side module-entitlement gate for the paid Driver Safety add-on (coaching +
    // safety scorecards). RBAC alone let a package_allowlist tenant WITHOUT the
    // fleet.driver_safety add-on read and exercise these endpoints. Mirrors
    // FleetTmsEndpoints.RequireModule; legacy_allow tenants (the default) are unaffected —
    // CheckModuleAsync returns allowed unless the module is explicitly disabled for the tenant.
    private static async Task<IResult?> RequireDriverSafetyModule(HttpContext http, Database db, CancellationToken ct)
    {
        var decision = await new EntitlementService(db)
            .CheckModuleAsync(GetCompanyId(http), "fleet.driver_safety", ct);
        return decision.Allowed
            ? null
            : Results.Json(ApiResponse<object>.Fail("Feature not entitled", decision.Reason ?? "fleet.driver_safety"),
                statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> PilotCoachingSummary(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "safety:view") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var scope = CoachingBranchScope(http);
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) FILTER (WHERE ct.status NOT IN ('Completed','Cancelled','Dismissed')) open_coaching_tasks,
                     COUNT(*) FILTER (WHERE ct.priority='Critical' AND ct.status NOT IN ('Completed','Cancelled','Dismissed')) critical_coaching,
                     COUNT(*) FILTER (WHERE ct.status='Assigned') assigned_tasks,
                     COUNT(*) FILTER (WHERE ct.driver_acknowledged=TRUE) driver_acknowledged,
                     COUNT(*) FILTER (WHERE ct.status='Completed' AND ct.completed_at >= NOW()-INTERVAL '30 days') completed_this_month,
                     COUNT(*) FILTER (WHERE ct.due_at<NOW() AND ct.status NOT IN ('Completed','Cancelled','Dismissed')) overdue_coaching,
                     COUNT(DISTINCT ct.driver_id) FILTER (WHERE repeats.active_count>1) repeat_coaching_drivers,
                     COUNT(*) FILTER (WHERE ct.after_safety_score>ct.before_safety_score) safety_score_improved,
                     COUNT(*) FILTER (WHERE ct.status='Escalated') escalated_coaching,
                     COALESCE(ROUND(AVG(EXTRACT(EPOCH FROM (ct.completed_at-ct.created_at))/3600) FILTER (WHERE ct.completed_at IS NOT NULL),1)::TEXT || 'h','--') average_completion_time
              FROM coaching_tasks ct
              LEFT JOIN (SELECT company_id,driver_id,COUNT(*) active_count FROM coaching_tasks
                         WHERE deleted_at IS NULL AND status NOT IN ('Completed','Cancelled','Dismissed') GROUP BY company_id,driver_id) repeats
                ON repeats.company_id=ct.company_id AND repeats.driver_id=ct.driver_id
              WHERE ct.company_id=@cid AND ct.deleted_at IS NULL" + scope,
            c => BindTenantAndBranch(c, http), ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> PilotCoachingTasks(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "safety:view") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        return await OkRows(db,
            CoachingSql + " WHERE ct.company_id=@cid AND ct.deleted_at IS NULL" + CoachingBranchScope(http) +
            " ORDER BY ARRAY_POSITION(ARRAY['Critical','High','Medium','Normal','Low'],ct.priority),ct.due_at NULLS LAST,ct.id",
            c => BindTenantAndBranch(c, http), ct: ct);
    }

    private static async Task<IResult> PilotCoachingTaskDetail(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "safety:view") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var companyId = GetCompanyId(http);
        var record = (await db.QueryAsync(
            CoachingSql + " WHERE ct.id=@id AND ct.company_id=@cid AND ct.deleted_at IS NULL" + CoachingBranchScope(http),
            c => { c.Parameters.AddWithValue("@id", id); BindTenantAndBranch(c, http); }, ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Coaching task not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            notes = await db.QueryAsync("SELECT * FROM coaching_notes WHERE coaching_task_id=@id AND company_id=@cid ORDER BY created_at DESC", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct),
            relatedSafetyEvents = await db.QueryAsync("SELECT * FROM safety_events WHERE id=@id AND company_id=@cid", c => { c.Parameters.AddWithValue("@id", record["safetyEventId"] ?? DBNull.Value); c.Parameters.AddWithValue("@cid", companyId); }, ct),
            relatedDashcamEvents = await db.QueryAsync("SELECT * FROM dashcam_events WHERE id=@id AND company_id=@cid", c => { c.Parameters.AddWithValue("@id", record["dashcamEventId"] ?? DBNull.Value); c.Parameters.AddWithValue("@cid", companyId); }, ct),
            // Recommendations currently have tenant ownership but no branch ownership metadata.
            // Do not expose tenant-wide narrative content to a branch-scoped principal.
            recommendations = GetBranchId(http) is null
                ? await TenantModuleRecommendations(db, companyId, "coaching", ct)
                : [],
            auditTrail = await TenantAuditRows(db, companyId, "CoachingTask", id, ct)
        }));
    }

    private static async Task<IResult> PilotCreateCoachingTask(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "safety:create", "safety:update", "safety:manage") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var driverId = BodyLong(body, "driverId");
        var coachingType = BodyText(body, "coachingType");
        var title = BodyText(body, "title");
        var description = BodyText(body, "description");
        if (driverId is null or <= 0 || string.IsNullOrWhiteSpace(coachingType) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["A positive driver ID, coaching type, title, and description are required."]));
        if (coachingType.Length > 120 || title.Length > 220 || description.Length > 4000 || !ValidCoachingPriority(BodyText(body, "priority") ?? "Medium"))
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["Coaching type, title, or description is too long, or priority is invalid."]));
        var dueAt = BodyDate(body, "dueAt");
        if (BodyText(body, "dueAt") is not null && dueAt is null)
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["Due date must be a valid date and time."]));
        var companyId = GetCompanyId(http);
        var driver = await db.QuerySingleAsync(
            "SELECT branch_id FROM drivers WHERE id=@driver AND company_id=@cid AND deleted_at IS NULL" + (GetBranchId(http) is null ? "" : " AND branch_id=@branchId"),
            c => { c.Parameters.AddWithValue("@driver", driverId.Value); BindTenantAndBranch(c, http); }, ct);
        if (driver is null) return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["Driver is outside your tenant or branch scope."]));

        var safetyEventId = BodyLong(body, "safetyEventId");
        var dashcamEventId = BodyLong(body, "dashcamEventId");
        var idempotencyKey = BodyText(body, "idempotencyKey");
        if (idempotencyKey?.Length > 160)
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["Idempotency key is too long."]));
        var assignedTo = BodyLong(body, "assignedToUserId");
        if (assignedTo is not null && !await ValidCoachingAssignee(db, http, assignedTo.Value, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["Assignee must be an active user in your tenant and authorized branch."]));
        if (safetyEventId is not null && await db.ScalarLongAsync("SELECT COUNT(*) FROM safety_events WHERE id=@id AND company_id=@cid AND driver_id=@driver AND deleted_at IS NULL", c => { c.Parameters.AddWithValue("@id", safetyEventId.Value); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@driver", driverId.Value); }, ct) == 0)
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["Linked safety event does not belong to this driver and tenant."]));
        if (dashcamEventId is not null && await db.ScalarLongAsync("SELECT COUNT(*) FROM dashcam_events WHERE id=@id AND company_id=@cid AND driver_id=@driver AND deleted_at IS NULL", c => { c.Parameters.AddWithValue("@id", dashcamEventId.Value); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@driver", driverId.Value); }, ct) == 0)
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching validation failed", ["Linked dashcam event does not belong to this driver and tenant."]));

        var requestHash = CoachingRequestHash(driverId.Value, safetyEventId, dashcamEventId, assignedTo,
            coachingType, BodyText(body, "priority") ?? "Medium", title, description,
            BodyText(body, "aiScript"), dueAt);
        try
        {
            var id = await db.InsertAsync(
                @"INSERT INTO coaching_tasks(company_id,branch_id,idempotency_key,idempotency_request_hash,task_number,driver_id,safety_event_id,dashcam_event_id,assigned_to_user_id,coaching_type,priority,status,title,description,ai_script,before_safety_score,due_at,row_version,updated_at)
                  VALUES(@cid,@branchId,@idempotencyKey,@requestHash,@number,@driver,@safety,@dashcam,@assigned,@type,@priority,'Draft',@title,@description,@script,
                         (SELECT score_30d FROM driver_safety_scores WHERE company_id=@cid AND driver_id=@driver),@due,0,NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@branchId", driver["branchId"] ?? DBNull.Value);
                    c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                    c.Parameters.AddWithValue("@requestHash", (object?)requestHash ?? DBNull.Value);
                    c.Parameters.AddWithValue("@number", $"COACH-{Guid.NewGuid():N}"[..22].ToUpperInvariant());
                    c.Parameters.AddWithValue("@driver", driverId.Value);
                    c.Parameters.AddWithValue("@safety", (object?)safetyEventId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@dashcam", (object?)dashcamEventId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@assigned", (object?)assignedTo ?? DBNull.Value);
                    c.Parameters.AddWithValue("@type", coachingType);
                    c.Parameters.AddWithValue("@priority", BodyText(body, "priority") ?? "Medium");
                    c.Parameters.AddWithValue("@title", title);
                    c.Parameters.AddWithValue("@description", description);
                    c.Parameters.AddWithValue("@script", (object?)BodyText(body, "aiScript") ?? DBNull.Value);
                    c.Parameters.AddWithValue("@due", (object?)dueAt ?? DBNull.Value);
                }, ct);
            await audit.LogAsync(http, "coaching.created", "CoachingTask", id, JsonSerializer.Serialize(new { driverId, safetyEventId, dashcamEventId }), ct);
            return Results.Created($"/api/coaching/tasks/{id}", ApiResponse<object>.Ok(new { id, rowVersion = 0 }, "Coaching task created"));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = await db.QuerySingleAsync("SELECT id,row_version,idempotency_request_hash FROM coaching_tasks WHERE company_id=@cid AND idempotency_key=@key AND deleted_at IS NULL", c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@key", idempotencyKey); }, ct);
                if (existing is not null)
                {
                    if (!string.Equals(existing["idempotencyRequestHash"]?.ToString(), requestHash, StringComparison.OrdinalIgnoreCase))
                        return Results.Conflict(ApiResponse<object>.Fail("Idempotency key was already used with different coaching data."));
                    return Results.Ok(ApiResponse<object>.Ok(new { id = existing["id"], rowVersion = existing["rowVersion"], replayed = true }, "Coaching task already created"));
                }
            }
            return Results.Conflict(ApiResponse<object>.Fail("An active coaching task already exists for this source event."));
        }
    }

    private static async Task<IResult> PilotUpdateCoachingTask(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "safety:update", "safety:manage") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var version = BodyLong(body, "rowVersion");
        if (version is null) return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required for safe updates."));
        if (body.ContainsKey("status")) return Results.BadRequest(ApiResponse<object>.Fail("Use a workflow action to change coaching status."));
        if (BodyText(body, "priority") is { } priority && !ValidCoachingPriority(priority))
            return Results.BadRequest(ApiResponse<object>.Fail("Priority must be Low, Normal, Medium, High, or Critical."));
        if ((body.ContainsKey("title") && string.IsNullOrWhiteSpace(BodyText(body, "title"))) ||
            (body.ContainsKey("coachingType") && string.IsNullOrWhiteSpace(BodyText(body, "coachingType"))) ||
            (body.ContainsKey("description") && string.IsNullOrWhiteSpace(BodyText(body, "description"))))
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching type, title, and description cannot be blank."));
        if (BodyText(body, "title") is { Length: > 220 } || BodyText(body, "coachingType") is { Length: > 120 } || BodyText(body, "description") is { Length: > 4000 })
            return Results.BadRequest(ApiResponse<object>.Fail("Coaching type, title, or description exceeds the allowed length."));
        if (BodyLong(body, "assignedToUserId") is { } assignee && !await ValidCoachingAssignee(db, http, assignee, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Assignee must be an active user in your tenant and authorized branch."));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
        var before = await db.QuerySingleAsync("SELECT ct.* FROM coaching_tasks ct WHERE ct.id=@id AND ct.company_id=@cid AND ct.deleted_at IS NULL" + CoachingBranchScope(http), c => { c.Parameters.AddWithValue("@id", id); BindTenantAndBranch(c, http); }, ct);
        if (before is null) return Results.NotFound(ApiResponse<object>.Fail("Coaching task not found"));
        if (before["status"]?.ToString() is "Completed" or "Cancelled")
            return Results.Conflict(ApiResponse<object>.Fail("Completed or cancelled coaching records are immutable."));
        var affected = await db.ExecuteAsync(
            @"UPDATE coaching_tasks ct SET assigned_to_user_id=COALESCE(@assigned,assigned_to_user_id),coaching_type=COALESCE(@type,coaching_type),
                     priority=COALESCE(@priority,priority),title=COALESCE(@title,title),description=COALESCE(@description,description),
                     ai_script=COALESCE(@script,ai_script),due_at=COALESCE(@due,due_at),updated_at=NOW(),row_version=row_version+1
              WHERE ct.id=@id AND ct.company_id=@cid AND ct.deleted_at IS NULL AND ct.row_version=@version" + CoachingBranchScope(http),
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@version", version.Value); BindTenantAndBranch(c, http); BindCoaching(c, body); }, ct);
        if (affected == 0) return Results.Conflict(ApiResponse<object>.Fail("Coaching task changed or is outside your scope. Refresh and retry."));
        await audit.LogAsync(http, "coaching.updated", "CoachingTask", id, JsonSerializer.Serialize(new { previousVersion = version, before, requested = body }), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, rowVersion = version + 1 }, "Coaching task updated"));
        }, ct);
    }

    private static async Task<IResult> PilotCoachingTransition(HttpContext http, long id, Dictionary<string, object?> body, string target, Database db, AuditService audit, CancellationToken ct)
    {
        var version = BodyLong(body, "rowVersion");
        if (version is null) return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required for safe workflow actions."));
        var completionNote = BodyText(body, "completionNote");
        decimal? afterSafetyScore = null;
        if (string.Equals(target, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(completionNote) || completionNote.Length > 4000 ||
                !decimal.TryParse(BodyText(body, "afterSafetyScore"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedScore) ||
                parsedScore is < 0 or > 100)
                return Results.BadRequest(ApiResponse<object>.Fail("A completion outcome and observed safety score from 0 to 100 are required."));
            afterSafetyScore = parsedScore;
        }
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
        var current = await db.QuerySingleAsync(
            "SELECT ct.status,ct.branch_id FROM coaching_tasks ct WHERE ct.id=@id AND ct.company_id=@cid AND ct.deleted_at IS NULL" + CoachingBranchScope(http) + " FOR UPDATE",
            c => { c.Parameters.AddWithValue("@id", id); BindTenantAndBranch(c, http); }, ct);
        if (current is null) return Results.NotFound(ApiResponse<object>.Fail("Coaching task not found"));
        var currentStatus = current["status"]?.ToString() ?? string.Empty;
        if (!AllowedCoachingTransition(currentStatus, target))
            return Results.Conflict(ApiResponse<object>.Fail($"Cannot move coaching task from {currentStatus} to {target}."));

        var affected = await db.ExecuteAsync(
            @"UPDATE coaching_tasks ct SET status=@target,driver_acknowledged=CASE WHEN @target='Driver Acknowledged' THEN TRUE ELSE driver_acknowledged END,
                     acknowledged_at=CASE WHEN @target='Driver Acknowledged' THEN NOW() ELSE acknowledged_at END,
                     completed_at=CASE WHEN @target='Completed' THEN NOW() ELSE completed_at END,
                     after_safety_score=CASE WHEN @target='Completed' THEN @afterSafetyScore ELSE after_safety_score END,
                     effectiveness_score=CASE WHEN @target='Completed' AND before_safety_score IS NOT NULL THEN
                         GREATEST(0,LEAST(100,50+(@afterSafetyScore-before_safety_score)*5)) ELSE effectiveness_score END,
                     effectiveness_formula_version=CASE WHEN @target='Completed' THEN 'observational-score-delta-v1' ELSE effectiveness_formula_version END,
                     effectiveness_evaluated_at=CASE WHEN @target='Completed' THEN NOW() ELSE effectiveness_evaluated_at END,
                     effectiveness_observation_json=CASE WHEN @target='Completed' THEN jsonb_build_object(
                         'beforeScore',before_safety_score,'afterScore',@afterSafetyScore,
                         'completionOutcome',@completionNote,
                         'warning','Manager-entered observational comparison; not a causal measure of coaching effectiveness') ELSE effectiveness_observation_json END,
                     updated_at=NOW(),row_version=row_version+1
              WHERE ct.id=@id AND ct.company_id=@cid AND ct.deleted_at IS NULL AND ct.row_version=@version" + CoachingBranchScope(http),
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", GetCompanyId(http)); c.Parameters.AddWithValue("@version", version.Value); c.Parameters.AddWithValue("@target", target); c.Parameters.AddWithValue("@afterSafetyScore", (object?)afterSafetyScore ?? DBNull.Value); c.Parameters.AddWithValue("@completionNote", (object?)completionNote ?? DBNull.Value); if (GetBranchId(http) is { } branchId) c.Parameters.AddWithValue("@branchId", branchId); }, ct);
        if (affected == 0) return Results.Conflict(ApiResponse<object>.Fail("Coaching task changed. Refresh and retry."));
        if (afterSafetyScore is not null)
            await db.ExecuteAsync(@"INSERT INTO coaching_notes(company_id,branch_id,coaching_task_id,note_type,note_text,created_by_user_id)
                VALUES(@cid,@branchId,@id,'Completion Outcome',@note,@user)", c =>
            {
                c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branchId", current["branchId"] ?? DBNull.Value);
                c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@note", completionNote!); c.Parameters.AddWithValue("@user", GetUserId(http));
            }, ct);
        await audit.LogAsync(http, $"coaching.{target.ToLowerInvariant().Replace(' ', '_')}", "CoachingTask", id, JsonSerializer.Serialize(new { from = currentStatus, to = target, previousVersion = version, completionNote, afterSafetyScore }), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = target, rowVersion = version + 1 }, $"Coaching task moved to {target}"));
        }, ct);
    }

    private static async Task<IResult> PilotCoachingAssign(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "safety:update", "safety:manage") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var assignee = BodyLong(body, "assignedToUserId") ?? GetUserId(http);
        if (!await ValidCoachingAssignee(db, http, assignee, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Assignee must be an active user in your tenant and authorized branch."));
        var version = BodyLong(body, "rowVersion");
        if (version is null) return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required for safe workflow actions."));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var current = await db.QuerySingleAsync("SELECT ct.status FROM coaching_tasks ct WHERE ct.id=@id AND ct.company_id=@cid AND ct.deleted_at IS NULL" + CoachingBranchScope(http) + " FOR UPDATE", c => { c.Parameters.AddWithValue("@id", id); BindTenantAndBranch(c, http); }, ct);
            if (current is null) return Results.NotFound(ApiResponse<object>.Fail("Coaching task not found"));
            var from = current["status"]?.ToString() ?? "";
            if (!AllowedCoachingTransition(from, "Assigned")) return Results.Conflict(ApiResponse<object>.Fail($"Cannot move coaching task from {from} to Assigned."));
            var affected = await db.ExecuteAsync("UPDATE coaching_tasks ct SET status='Assigned',assigned_to_user_id=@assigned,updated_at=NOW(),row_version=row_version+1 WHERE ct.id=@id AND ct.company_id=@cid AND ct.row_version=@version" + CoachingBranchScope(http), c => { c.Parameters.AddWithValue("@assigned", assignee); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@version", version.Value); BindTenantAndBranch(c, http); }, ct);
            if (affected == 0) return Results.Conflict(ApiResponse<object>.Fail("Coaching task changed. Refresh and retry."));
            await audit.LogAsync(http, "coaching.assigned", "CoachingTask", id, JsonSerializer.Serialize(new { from, to = "Assigned", assignee, previousVersion = version }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, status = "Assigned", assignedToUserId = assignee, rowVersion = version + 1 }, "Coaching task assigned"));
        }, ct);
    }

    private static async Task<IResult> PilotCoachingAcknowledge(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "driver:self") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var driverId = await GetDriverIdFromAuthAsync(http, db, ct);
        if (driverId < 0) return DriverIdentityNotFound();
        var version = BodyLong(body, "rowVersion");
        if (version is null) return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required for driver acknowledgement."));
        var note = BodyText(body, "note") ?? BodyText(body, "notes");
        if (note is { Length: > 2000 }) return Results.BadRequest(ApiResponse<object>.Fail("Acknowledgement note cannot exceed 2000 characters."));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
        var acknowledged = await db.QuerySingleAsync(@"UPDATE coaching_tasks SET driver_acknowledged=TRUE,acknowledged_at=NOW(),status='Driver Acknowledged',
                acknowledged_note=COALESCE(@note,acknowledged_note),updated_at=NOW(),row_version=row_version+1
            WHERE id=@id AND company_id=@cid AND driver_id=@driver AND deleted_at IS NULL AND status='Assigned' AND row_version=@version
            RETURNING branch_id",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@driver", driverId); c.Parameters.AddWithValue("@version", version.Value); c.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value); }, ct);
        if (acknowledged is null) return Results.Conflict(ApiResponse<object>.Fail("Only the assigned driver can acknowledge a current Assigned task. Refresh and retry."));
        if (note is not null)
            await db.ExecuteAsync(@"INSERT INTO coaching_notes(company_id,branch_id,coaching_task_id,note_type,note_text,created_by_user_id)
                VALUES(@cid,@branchId,@id,'Driver Acknowledgement',@note,@user)", c =>
            {
                c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branchId", acknowledged["branchId"] ?? DBNull.Value);
                c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@note", note); c.Parameters.AddWithValue("@user", GetUserId(http));
            }, ct);
        await audit.LogAsync(http, "driver.coaching.acknowledged", "CoachingTask", id, JsonSerializer.Serialize(new { driverId, previousVersion = version, acknowledgementNote = note }), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = "Driver Acknowledged", rowVersion = version + 1 }, "Coaching task acknowledged"));
        }, ct);
    }

    private static async Task<IResult> PilotCoachingComplete(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "safety:update", "safety:manage") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        return await PilotCoachingTransition(http, id, body, "Completed", db, audit, ct);
    }

    private static async Task<IResult> PilotCoachingAddNote(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "safety:review", "safety:update", "safety:manage") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var text = BodyText(body, "noteText");
        if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest(ApiResponse<object>.Fail("Note text is required."));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var noteId = await db.InsertAsync(@"WITH candidate AS MATERIALIZED (
                    SELECT ct.company_id,ct.branch_id,ct.id FROM coaching_tasks ct
                    WHERE ct.id=@id AND ct.company_id=@cid AND ct.deleted_at IS NULL" + CoachingBranchScope(http) + @"
                    FOR UPDATE)
                INSERT INTO coaching_notes(company_id,branch_id,coaching_task_id,note_type,note_text,created_by_user_id)
                SELECT company_id,branch_id,id,@type,@text,@user FROM candidate RETURNING id",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@type", BodyText(body, "noteType") ?? "Manager Note"); c.Parameters.AddWithValue("@text", text); c.Parameters.AddWithValue("@user", GetUserId(http)); BindTenantAndBranch(c, http); }, ct);
            if (noteId == 0) return Results.NotFound(ApiResponse<object>.Fail("Coaching task not found"));
            await audit.LogAsync(http, "coaching.note.added", "CoachingTask", id, JsonSerializer.Serialize(new { noteId }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id = noteId }, "Coaching note added"));
        }, ct);
    }

    private static async Task<IResult> PilotDeleteCoachingTask(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "safety:delete", "safety:manage") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var rawVersion = http.Request.Headers.IfMatch.FirstOrDefault()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(rawVersion)) rawVersion = http.Request.Query["rowVersion"].FirstOrDefault();
        if (!long.TryParse(rawVersion, out var version) || version < 0)
            return Results.Json(ApiResponse<object>.Fail("If-Match with the current row version is required."), statusCode: StatusCodes.Status428PreconditionRequired);
        var affected = await db.ExecuteAsync("UPDATE coaching_tasks ct SET deleted_at=NOW(),updated_at=NOW(),row_version=row_version+1 WHERE ct.id=@id AND ct.company_id=@cid AND ct.row_version=@version AND ct.status IN ('Draft','Cancelled') AND ct.deleted_at IS NULL" + CoachingBranchScope(http), c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@version", version); BindTenantAndBranch(c, http); }, ct);
        if (affected == 0) return Results.Conflict(ApiResponse<object>.Fail("Only current Draft or Cancelled tasks can be deleted."));
        await audit.LogAsync(http, "coaching.deleted", "CoachingTask", id, JsonSerializer.Serialize(new { previousVersion = version }), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Coaching task deleted"));
    }

    private static async Task<bool> ValidCoachingAssignee(Database db, HttpContext http, long userId, CancellationToken ct)
    {
        var count = await db.ScalarLongAsync(@"SELECT COUNT(*) FROM users WHERE id=@id AND company_id=@cid AND status='Active'
            AND (@branchId::bigint IS NULL OR branch_id=@branchId)", c =>
        {
            c.Parameters.AddWithValue("@id", userId);
            c.Parameters.AddWithValue("@cid", GetCompanyId(http));
            c.Parameters.AddWithValue("@branchId", (object?)GetBranchId(http) ?? DBNull.Value);
        }, ct);
        return count == 1;
    }

    private static string CoachingRequestHash(long driverId, long? safetyEventId, long? dashcamEventId, long? assignedTo,
        string coachingType, string priority, string title, string? description, string? aiScript, DateTimeOffset? dueAt)
    {
        var canonical = JsonSerializer.Serialize(new { driverId, safetyEventId, dashcamEventId,
            assignedToUserId = assignedTo, coachingType, priority, title, description, aiScript,
            dueAt = dueAt?.ToUniversalTime().ToString("O") });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool ValidCoachingPriority(string value) =>
        value is "Low" or "Normal" or "Medium" or "High" or "Critical";

    private static async Task<IResult> SafetyScorecardSummary(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "safety:view") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var branch = GetBranchId(http);
        var row = await db.QuerySingleAsync(
            @"WITH scoped_drivers AS (
                SELECT d.id,d.company_id FROM drivers d
                WHERE d.company_id=@cid AND d.deleted_at IS NULL AND (@branchId::BIGINT IS NULL OR d.branch_id=@branchId)),
              recent_events AS (
                SELECT se.* FROM safety_events se JOIN scoped_drivers d ON d.id=se.driver_id AND d.company_id=se.company_id
                WHERE se.event_time>=NOW()-INTERVAL '30 days' AND se.deleted_at IS NULL AND LOWER(se.status)<>'dismissed')
              SELECT (SELECT ROUND(AVG(dss.score_30d),1) FROM scoped_drivers d JOIN driver_safety_scores dss ON dss.driver_id=d.id AND dss.company_id=d.company_id AND dss.computed_at>=NOW()-INTERVAL '2 hours' AND dss.events_90d>0) fleet_safety_score,
                     (SELECT COUNT(*) FROM scoped_drivers) total_drivers,
                     (SELECT COUNT(*) FROM scoped_drivers d JOIN driver_safety_scores dss ON dss.driver_id=d.id AND dss.company_id=d.company_id AND dss.computed_at>=NOW()-INTERVAL '2 hours' AND dss.events_90d>0) scored_drivers,
                     (SELECT COUNT(*) FROM recent_events WHERE severity='Critical' AND status NOT IN ('resolved','dismissed')) critical_events,
                     (SELECT COUNT(*) FROM recent_events WHERE event_type='Harsh Braking') harsh_braking,
                     (SELECT COUNT(*) FROM recent_events WHERE event_type='Harsh Acceleration') harsh_acceleration,
                     (SELECT COUNT(*) FROM recent_events WHERE event_type='Speeding') speeding_events,
                     (SELECT COUNT(*) FROM recent_events WHERE status NOT IN ('resolved','dismissed')) open_incidents,
                     (SELECT COUNT(*) FROM scoped_drivers d JOIN driver_safety_scores dss ON dss.driver_id=d.id AND dss.company_id=d.company_id
                        WHERE dss.computed_at>=NOW()-INTERVAL '2 hours' AND dss.events_90d>0 AND dss.score_30d<70) coaching_needed",
            c => { c.Parameters.AddWithValue("@cid", GetCompanyId(http)); c.Parameters.AddWithValue("@branchId", (object?)branch ?? DBNull.Value); }, ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> SafetyVehicleScorecards(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "safety:view") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var (scope, branchId) = StrictBranchFilter(http, "v");
        return await OkRows(db, @"SELECT sc.*,v.vehicle_code,v.type FROM vehicle_safety_scorecards sc
                            JOIN vehicles v ON v.id=sc.vehicle_id AND v.company_id=sc.company_id
                            WHERE sc.company_id=@cid AND v.deleted_at IS NULL" + scope + " ORDER BY sc.risk_score DESC,sc.id",
            c => { c.Parameters.AddWithValue("@cid", GetCompanyId(http)); if (branchId is not null) c.Parameters.AddWithValue("@branchId", branchId); }, ct: ct);
    }

    private static async Task<IResult> SafetyDriverScorecardsPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "safety:view") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var (scope, branchId) = StrictBranchFilter(http, "d");
        return await OkRows(db,
            @"SELECT d.id,d.id driver_id,d.driver_code,d.full_name driver_name,d.status driver_status,
                     dss.score_30d safety_score,CASE WHEN dss.id IS NULL THEN NULL ELSE GREATEST(0,100-dss.score_30d) END risk_score,
                     COALESCE(dss.events_7d,0) events_7d,COALESCE(dss.events_30d,0) events_30d,COALESCE(dss.events_90d,0) events_90d,
                     COALESCE((dss.breakdown_json->'Harsh Braking'->>'count')::INT,0) harsh_braking_count,
                     COALESCE((dss.breakdown_json->'Harsh Acceleration'->>'count')::INT,0) harsh_acceleration_count,
                     COALESCE((dss.breakdown_json->'Speeding'->>'count')::INT,0) speeding_count,
                     (SELECT COUNT(*) FROM safety_events se WHERE se.company_id=d.company_id AND se.driver_id=d.id AND se.meta_json->>'source'='dashcam' AND se.event_time>=NOW()-INTERVAL '30 days') dashcam_event_count,
                     (SELECT COUNT(*) FROM coaching_tasks ct WHERE ct.company_id=d.company_id AND ct.driver_id=d.id AND ct.deleted_at IS NULL AND ct.status NOT IN ('Completed','Cancelled','Dismissed')) coaching_open_count,
                     (SELECT COUNT(*) FROM coaching_tasks ct WHERE ct.company_id=d.company_id AND ct.driver_id=d.id AND ct.deleted_at IS NULL AND ct.status='Completed') coaching_completed_count,
                     (SELECT COUNT(*) FROM incidents i WHERE i.company_id=d.company_id AND i.driver_id=d.id AND i.deleted_at IS NULL) incident_count,
                     COALESCE(dss.breakdown_json,'{}'::jsonb) score_breakdown,dss.computed_at,
                     CASE WHEN dss.computed_at IS NULL THEN NULL ELSE dss.computed_at-INTERVAL '30 days' END source_window_start,
                     dss.computed_at source_window_end,COALESCE(dss.events_30d,0) source_event_count,
                     CASE WHEN dss.id IS NULL OR dss.events_90d=0 THEN 'insufficient_data' WHEN dss.computed_at<NOW()-INTERVAL '2 hours' THEN 'stale' ELSE 'current' END score_status,
                     'safety-impact-v1' formula_version,'SafetyBackgroundService' calculation_source,
                     'Score = 100 minus non-dismissed event impact over the selected 30-day window; fleet score is the average of current driver scores.' score_formula
              FROM drivers d LEFT JOIN driver_safety_scores dss ON dss.company_id=d.company_id AND dss.driver_id=d.id
              WHERE d.company_id=@cid AND d.deleted_at IS NULL" + scope +
              " ORDER BY dss.score_30d NULLS LAST,COALESCE(dss.events_30d,0) DESC,d.full_name",
            c => { c.Parameters.AddWithValue("@cid", GetCompanyId(http)); if (branchId is not null) c.Parameters.AddWithValue("@branchId", branchId); }, ct: ct);
    }

    private static async Task<IResult> SafetyScorecardTrends(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "safety:view") is { } denied) return denied;
        if (await RequireDriverSafetyModule(http, db, ct) is { } gated) return gated;
        var branch = GetBranchId(http);
        return await OkRows(db,
            @"WITH days AS (SELECT generate_series(CURRENT_DATE-29,CURRENT_DATE,'1 day')::date trend_date),
              scoped_drivers AS (
                SELECT d.id FROM drivers d
                JOIN driver_safety_scores dss ON dss.company_id=d.company_id AND dss.driver_id=d.id
                  AND dss.computed_at>=NOW()-INTERVAL '2 hours' AND dss.events_90d>0
                WHERE d.company_id=@cid AND d.deleted_at IS NULL AND (@branchId::BIGINT IS NULL OR d.branch_id=@branchId)),
              driver_day AS (SELECT days.trend_date,d.id driver_id,GREATEST(0,100-COALESCE(SUM(se.score_impact),0)) score
                FROM days CROSS JOIN scoped_drivers d LEFT JOIN safety_events se ON se.company_id=@cid AND se.driver_id=d.id AND se.deleted_at IS NULL AND LOWER(se.status)<>'dismissed' AND se.event_time::date BETWEEN days.trend_date-29 AND days.trend_date GROUP BY days.trend_date,d.id),
              daily_events AS (SELECT se.event_time::date trend_date,se.event_type FROM safety_events se JOIN scoped_drivers d ON d.id=se.driver_id WHERE se.company_id=@cid AND se.deleted_at IS NULL AND LOWER(se.status)<>'dismissed' AND se.event_time>=CURRENT_DATE-29)
              SELECT days.trend_date,COUNT(daily_events.event_type) event_count,
                     COUNT(*) FILTER(WHERE daily_events.event_type='Harsh Braking') harsh_braking_count,
                     COUNT(*) FILTER(WHERE daily_events.event_type='Harsh Acceleration') harsh_acceleration_count,
                     COUNT(*) FILTER(WHERE daily_events.event_type='Speeding') speeding_count,
                     ROUND((SELECT AVG(score) FROM driver_day dd WHERE dd.trend_date=days.trend_date),2) fleet_safety_score,
                     (SELECT COUNT(*) FROM scoped_drivers) scored_drivers,'safety-impact-v1' formula_version
              FROM days LEFT JOIN daily_events USING(trend_date) GROUP BY days.trend_date ORDER BY days.trend_date",
            c => { c.Parameters.AddWithValue("@cid", GetCompanyId(http)); c.Parameters.AddWithValue("@branchId", (object?)branch ?? DBNull.Value); }, ct: ct);
    }
}
