using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private static async Task<IResult> DeviceRmaSupportActionCreate(
        HttpContext http,
        long caseId,
        DeviceRmaSupportActionRequest? body,
        Database db,
        AuditService audit,
        CancellationToken ct)
    {
        if (RequirePermission(http, "telematics:devices:rma") is { } denied) return denied;
        var validation = DeviceRmaSupportPolicy.Validate(body, DateTimeOffset.UtcNow);
        if (validation.Error is not null)
            return Results.BadRequest(ApiResponse<object>.Fail(validation.Error));
        var input = validation.Value!;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = GetUserId(http);
        if (actorId <= 0) return Results.Unauthorized();

        Dictionary<string, object?> supportAction;
        bool idempotentReplay;
        try
        {
            (supportAction, idempotentReplay) = await db.RunInSystemTransactionAsync(async () =>
            {
                await db.ExecuteAsync(
                    @"SELECT pg_advisory_xact_lock(hashtextextended(identity,0))
                        FROM unnest(@identities::TEXT[]) identity ORDER BY identity",
                    command => command.Parameters.AddWithValue(
                        "@identities", NpgsqlDbType.Array | NpgsqlDbType.Text,
                        new[]
                        {
                            $"device-rma-support:{companyId}:{caseId}",
                            $"device-rma-support-idempotency:{companyId}:{input.IdempotencyKey:D}",
                        }.OrderBy(value => value).ToArray()), ct);

                var replay = await LoadRmaSupportActionByIdempotency(
                    db, companyId, branchId, input.IdempotencyKey, ct);
                if (replay is not null)
                {
                    if (!RmaSupportReplayMatches(replay, caseId, actorId, input))
                        throw new InvalidOperationException("rma_support_idempotency_mismatch");
                    return (replay, true);
                }

                var rmaCase = await db.QuerySingleAsync(
                    @"SELECT id,branch_id,device_id
                        FROM device_rma_cases
                       WHERE company_id=@company AND id=@case
                         AND (@branch::BIGINT IS NULL OR branch_id=@branch)
                       FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@case", caseId);
                        command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
                    }, ct);
                if (rmaCase is null) throw new InvalidOperationException("rma_support_case_missing");
                var caseStatusRow = await db.QuerySingleAsync(
                    @"SELECT COALESCE((SELECT case_status_after FROM device_rma_events
                              WHERE company_id=@company AND case_id=@case
                              ORDER BY sequence_number DESC LIMIT 1),'Open') current_status",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@case", caseId);
                    }, ct);
                if (caseStatusRow?.GetValueOrDefault("currentStatus")?.ToString() == "Resolved")
                    throw new InvalidOperationException("rma_support_case_resolved");

                var actor = await db.QuerySingleAsync(
                    @"SELECT id,full_name,branch_id FROM users
                       WHERE company_id=@company AND id=@actor AND status='Active'
                         AND (@caseBranch::BIGINT IS NULL OR branch_id IS NULL OR branch_id=@caseBranch)
                       FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@actor", actorId);
                        command.Parameters.AddWithValue("@caseBranch", rmaCase["branchId"] is null or DBNull
                            ? DBNull.Value : rmaCase["branchId"]!);
                    }, ct);
                if (actor is null) throw new InvalidOperationException("rma_support_actor_unavailable");

                var prior = await db.QuerySingleAsync(
                    RmaSupportProjection + @" WHERE company_id=@company AND case_id=@case
                        ORDER BY effective_at DESC,id DESC LIMIT 1 FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@case", caseId);
                    }, ct);

                string storedActionType;
                long ownerId;
                string ownerName;
                if (input.ActionType == "TakeOwnership")
                {
                    if (prior is not null && Convert.ToInt64(prior["ownerUserId"]!) == actorId)
                        throw new InvalidOperationException("rma_support_already_owned");
                    storedActionType = prior is null ? "OwnershipClaimed" : "OwnershipReassigned";
                    ownerId = actorId;
                    ownerName = actor["fullName"]!.ToString()!;
                }
                else
                {
                    if (prior is null) throw new InvalidOperationException("rma_support_owner_missing");
                    storedActionType = "Escalated";
                    ownerId = Convert.ToInt64(prior["ownerUserId"]!);
                    ownerName = prior["ownerNameSnapshot"]!.ToString()!;
                }

                var actionId = await db.InsertAsync(
                    @"INSERT INTO device_rma_support_actions
                        (company_id,branch_id,case_id,device_id,action_type,owner_user_id,
                         owner_name_snapshot,support_queue,escalation_severity,action_reason,
                         source_reference,effective_at,idempotency_key,recorded_by)
                      VALUES(@company,@branch,@case,@device,@actionType,@owner,@ownerName,
                             @queue,@severity,@reason,@source,@effectiveAt,@key,@actor)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@branch", rmaCase["branchId"] is null or DBNull ? DBNull.Value : rmaCase["branchId"]!);
                        command.Parameters.AddWithValue("@case", caseId);
                        command.Parameters.AddWithValue("@device", rmaCase["deviceId"]!);
                        command.Parameters.AddWithValue("@actionType", storedActionType);
                        command.Parameters.AddWithValue("@owner", ownerId);
                        command.Parameters.AddWithValue("@ownerName", ownerName);
                        command.Parameters.AddWithValue("@queue", input.SupportQueue);
                        command.Parameters.AddWithValue("@severity", (object?)input.EscalationSeverity ?? DBNull.Value);
                        command.Parameters.AddWithValue("@reason", input.ActionReason);
                        command.Parameters.AddWithValue("@source", input.SourceReference);
                        command.Parameters.AddWithValue("@effectiveAt", input.EffectiveAt);
                        command.Parameters.AddWithValue("@key", input.IdempotencyKey);
                        command.Parameters.AddWithValue("@actor", actorId);
                    }, ct);
                await audit.LogAsync(http,
                    storedActionType == "Escalated" ? "device.rma_support.escalated" : "device.rma_support.ownership_recorded",
                    "DeviceRmaSupportAction", actionId,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        caseId,
                        deviceId = Convert.ToInt64(rmaCase["deviceId"]!),
                        actionType = storedActionType,
                        ownerUserId = ownerId,
                        input.SupportQueue,
                        input.EscalationSeverity,
                        input.SourceReference,
                        supportResponseClaim = false,
                        physicalOutcomeClaim = false,
                        warrantyAcceptanceClaim = false,
                    }), ct);
                return (await LoadRmaSupportActionById(db, companyId, actionId, ct)
                    ?? throw new InvalidOperationException("rma_support_receipt_missing"), false);
            }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "rma_support_idempotency_mismatch")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "That idempotency key was already used for a different RMA support action."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "rma_support_case_missing")
        {
            return Results.NotFound(ApiResponse<object>.Fail("RMA case not found in the current scope."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "rma_support_case_resolved")
        {
            return Results.Conflict(ApiResponse<object>.Fail("A resolved RMA case cannot accept support actions."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "rma_support_owner_missing")
        {
            return Results.Conflict(ApiResponse<object>.Fail("Take ownership before escalating this RMA case."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "rma_support_already_owned")
        {
            return Results.Conflict(ApiResponse<object>.Fail("You already own this RMA case."));
        }
        catch (InvalidOperationException ex) when (ex.Message is "rma_support_actor_unavailable" or "rma_support_receipt_missing")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "The support action could not be recorded in the current user and case scope."));
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "The support action conflicted with the current case history. Refresh and try again."));
        }

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            supportAction,
            idempotentReplay,
            supportResponseClaim = false,
            physicalOutcomeClaim = false,
            warrantyAcceptanceClaim = false,
            note = "RMA ownership or escalation routing was recorded. Support response, warranty acceptance, and physical outcomes remain unverified."
        }, idempotentReplay ? "RMA support action already recorded" : "RMA support action recorded"));
    }

    private static Task<Dictionary<string, object?>?> LoadRmaSupportActionByIdempotency(
        Database db, long companyId, long? branchId, Guid idempotencyKey, CancellationToken ct) => db.QuerySingleAsync(
        RmaSupportProjection + @" WHERE company_id=@company AND idempotency_key=@key
            AND (@branch::BIGINT IS NULL OR branch_id=@branch)",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@key", idempotencyKey);
            command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
        }, ct);

    private static Task<Dictionary<string, object?>?> LoadRmaSupportActionById(
        Database db, long companyId, long actionId, CancellationToken ct) => db.QuerySingleAsync(
        RmaSupportProjection + " WHERE company_id=@company AND id=@action",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@action", actionId);
        }, ct);

    private const string RmaSupportProjection = @"SELECT id,case_id,device_id,action_type,
        owner_user_id,owner_name_snapshot,support_queue,escalation_severity,action_reason,
        source_reference,effective_at,support_action_status,support_response_claim,
        physical_outcome_claim,warranty_acceptance_claim,recorded_by,created_at
        FROM device_rma_support_actions";

    private static bool RmaSupportReplayMatches(
        Dictionary<string, object?> row,
        long caseId,
        long actorId,
        ValidatedDeviceRmaSupportAction input)
    {
        var storedType = row["actionType"]?.ToString();
        var typeMatches = input.ActionType == "TakeOwnership"
            ? storedType is "OwnershipClaimed" or "OwnershipReassigned"
            : storedType == "Escalated";
        return typeMatches &&
            Convert.ToInt64(row["caseId"]!) == caseId &&
            Convert.ToInt64(row["recordedBy"]!) == actorId &&
            (input.ActionType != "TakeOwnership" || Convert.ToInt64(row["ownerUserId"]!) == actorId) &&
            string.Equals(row["supportQueue"]?.ToString(), input.SupportQueue, StringComparison.Ordinal) &&
            string.Equals(row["escalationSeverity"] is null or DBNull ? null : row["escalationSeverity"]!.ToString(), input.EscalationSeverity, StringComparison.Ordinal) &&
            string.Equals(row["actionReason"]?.ToString(), input.ActionReason, StringComparison.Ordinal) &&
            string.Equals(row["sourceReference"]?.ToString(), input.SourceReference, StringComparison.Ordinal) &&
            StoredInstantEquals(row["effectiveAt"], input.EffectiveAt);
    }
}
