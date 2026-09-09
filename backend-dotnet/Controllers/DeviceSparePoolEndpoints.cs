using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private static async Task<IResult> DeviceSparePoolActionCreate(
        HttpContext http,
        long id,
        DeviceSparePoolActionRequest? body,
        Database db,
        AuditService audit,
        CancellationToken ct)
    {
        if (RequirePermission(http, "telematics:devices:rma") is { } denied) return denied;
        var validation = DeviceSparePoolPolicy.Validate(body, DateTimeOffset.UtcNow);
        if (validation.Error is not null)
            return Results.BadRequest(ApiResponse<object>.Fail(validation.Error));
        var input = validation.Value!;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = GetUserId(http);
        if (actorId <= 0) return Results.Unauthorized();

        Dictionary<string, object?> entry;
        Dictionary<string, object?> poolEvent;
        bool idempotentReplay;
        try
        {
            (entry, poolEvent, idempotentReplay) = await db.RunInSystemTransactionAsync(async () =>
            {
                var locks = new List<string>
                {
                    $"device-spare-pool:{companyId}:{id}",
                    $"device-spare-pool-idempotency:{companyId}:{input.IdempotencyKey:D}",
                };
                if (input.RmaCaseId is not null)
                    locks.Add($"device-spare-pool-case:{companyId}:{input.RmaCaseId.Value}");
                await db.ExecuteAsync(
                    @"SELECT pg_advisory_xact_lock(hashtextextended(identity,0))
                        FROM unnest(@identities::TEXT[]) identity ORDER BY identity",
                    command => command.Parameters.AddWithValue("@identities",
                        NpgsqlDbType.Array | NpgsqlDbType.Text, locks.OrderBy(value => value).ToArray()), ct);

                var replay = await LoadSparePoolEventByIdempotency(
                    db, companyId, branchId, input.IdempotencyKey, ct);
                if (replay is not null)
                {
                    if (!SparePoolReplayMatches(replay, id, actorId, input))
                        throw new InvalidOperationException("spare_pool_idempotency_mismatch");
                    var replayEntry = await LoadSparePoolEntryById(
                        db, companyId, Convert.ToInt64(replay["entryId"]!), ct);
                    return (replayEntry ?? throw new InvalidOperationException("spare_pool_receipt_missing"), replay, true);
                }

                var device = await db.QuerySingleAsync(
                    @"SELECT id,branch_id,device_serial,status,device_state
                        FROM eld_devices WHERE company_id=@company AND id=@device AND deleted_at IS NULL
                         AND (@branch::BIGINT IS NULL OR branch_id=@branch) FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
                    }, ct);
                if (device is null) throw new InvalidOperationException("spare_pool_device_missing");
                if (device["status"]?.ToString() is "Revoked" or "Retired" ||
                    device["deviceState"]?.ToString() is "Decommissioned" or "Retired")
                    throw new InvalidOperationException("spare_pool_device_terminal");
                long? deviceBranch = device["branchId"] is null or DBNull
                    ? null
                    : Convert.ToInt64(device["branchId"]!);

                var storedEntry = await LoadSparePoolEntryByDevice(db, companyId, id, ct);
                Dictionary<string, object?>? prior = null;
                if (storedEntry is not null)
                    prior = await LoadLatestSparePoolEvent(db, companyId, Convert.ToInt64(storedEntry["id"]!), ct);

                long entryId;
                long? caseId = null;
                long? failedDeviceId = null;
                string storedAction;
                string stateAfter;
                if (input.ActionType == "Add")
                {
                    if (storedEntry is not null) throw new InvalidOperationException("spare_pool_entry_exists");
                    var installed = await db.ScalarLongAsync(
                        @"SELECT COUNT(*) FROM device_installations WHERE company_id=@company AND device_id=@device
                            AND effective_to IS NULL AND status IN ('Installed','Verified')",
                        command =>
                        {
                            command.Parameters.AddWithValue("@company", companyId);
                            command.Parameters.AddWithValue("@device", id);
                        }, ct);
                    if (installed > 0) throw new InvalidOperationException("spare_pool_device_installed");
                    entryId = await db.InsertAsync(
                        @"INSERT INTO device_spare_pool_entries
                            (company_id,branch_id,device_id,device_serial_snapshot,pool_name,entry_reason,
                             source_reference,idempotency_key,added_by)
                          VALUES(@company,@branch,@device,@serial,@pool,@reason,@source,@key,@actor)",
                        command =>
                        {
                            command.Parameters.AddWithValue("@company", companyId);
                            command.Parameters.AddWithValue("@branch", (object?)deviceBranch ?? DBNull.Value);
                            command.Parameters.AddWithValue("@device", id);
                            command.Parameters.AddWithValue("@serial", device["deviceSerial"]!);
                            command.Parameters.AddWithValue("@pool", input.PoolName!);
                            command.Parameters.AddWithValue("@reason", input.ActionReason);
                            command.Parameters.AddWithValue("@source", input.SourceReference);
                            command.Parameters.AddWithValue("@key", input.IdempotencyKey);
                            command.Parameters.AddWithValue("@actor", actorId);
                        }, ct);
                    storedAction = "Added";
                    stateAfter = "Available";
                }
                else
                {
                    if (storedEntry is null || prior is null)
                        throw new InvalidOperationException("spare_pool_entry_missing");
                    entryId = Convert.ToInt64(storedEntry["id"]!);
                    var currentState = prior["stateAfter"]?.ToString();
                    if (input.ActionType == "Reserve")
                    {
                        if (currentState != "Available") throw new InvalidOperationException("spare_pool_not_available");
                        var rmaCase = await db.QuerySingleAsync(
                            @"SELECT c.id,c.device_id,
                                COALESCE((SELECT case_status_after FROM device_rma_events e
                                  WHERE e.company_id=c.company_id AND e.case_id=c.id
                                  ORDER BY sequence_number DESC LIMIT 1),'Open') current_status
                                FROM device_rma_cases c
                               WHERE c.company_id=@company AND c.id=@case
                                 AND (@branch::BIGINT IS NULL OR c.branch_id=@branch) FOR UPDATE",
                            command =>
                            {
                                command.Parameters.AddWithValue("@company", companyId);
                                command.Parameters.AddWithValue("@case", input.RmaCaseId!.Value);
                                command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
                            }, ct);
                        if (rmaCase is null || rmaCase["currentStatus"]?.ToString() == "Resolved" ||
                            Convert.ToInt64(rmaCase["deviceId"]!) == id)
                            throw new InvalidOperationException("spare_pool_case_unavailable");
                        caseId = input.RmaCaseId;
                        failedDeviceId = Convert.ToInt64(rmaCase["deviceId"]!);
                        storedAction = "Reserved";
                        stateAfter = "Reserved";
                    }
                    else if (input.ActionType == "Release")
                    {
                        if (currentState != "Reserved") throw new InvalidOperationException("spare_pool_not_reserved");
                        caseId = Convert.ToInt64(prior["rmaCaseId"]!);
                        failedDeviceId = Convert.ToInt64(prior["failedDeviceId"]!);
                        storedAction = "Released";
                        stateAfter = "Available";
                    }
                    else
                    {
                        if (currentState != "Available") throw new InvalidOperationException("spare_pool_not_available");
                        storedAction = "Removed";
                        stateAfter = "Removed";
                    }
                }

                var eventId = await db.InsertAsync(
                    @"INSERT INTO device_spare_pool_events
                        (company_id,branch_id,entry_id,device_id,action_type,state_after,rma_case_id,
                         failed_device_id,action_reason,source_reference,effective_at,idempotency_key,recorded_by)
                      VALUES(@company,@branch,@entry,@device,@action,@state,@case,@failedDevice,@reason,
                             @source,@effectiveAt,@key,@actor)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@branch", (object?)deviceBranch ?? DBNull.Value);
                        command.Parameters.AddWithValue("@entry", entryId);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@action", storedAction);
                        command.Parameters.AddWithValue("@state", stateAfter);
                        command.Parameters.AddWithValue("@case", (object?)caseId ?? DBNull.Value);
                        command.Parameters.AddWithValue("@failedDevice", (object?)failedDeviceId ?? DBNull.Value);
                        command.Parameters.AddWithValue("@reason", input.ActionReason);
                        command.Parameters.AddWithValue("@source", input.SourceReference);
                        command.Parameters.AddWithValue("@effectiveAt", input.EffectiveAt);
                        command.Parameters.AddWithValue("@key", input.IdempotencyKey);
                        command.Parameters.AddWithValue("@actor", actorId);
                    }, ct);
                await audit.LogAsync(http, $"device.spare_pool.{storedAction.ToLowerInvariant()}",
                    "DeviceSparePoolEvent", eventId,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        deviceId = id, entryId, actionType = storedAction, stateAfter, rmaCaseId = caseId,
                        input.SourceReference, physicalPossessionClaim = false, conditionVerifiedClaim = false,
                        compatibilityClaim = false, certificationClaim = false,
                    }), ct);
                var createdEntry = await LoadSparePoolEntryById(db, companyId, entryId, ct)
                    ?? throw new InvalidOperationException("spare_pool_receipt_missing");
                var createdEvent = await LoadSparePoolEventById(db, companyId, eventId, ct)
                    ?? throw new InvalidOperationException("spare_pool_receipt_missing");
                return (createdEntry, createdEvent, false);
            }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_idempotency_mismatch")
        {
            return Results.Conflict(ApiResponse<object>.Fail("That idempotency key was already used for a different spare-pool action."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_device_missing")
        {
            return Results.NotFound(ApiResponse<object>.Fail("Device not found in the current scope."));
        }
        catch (InvalidOperationException ex) when (ex.Message is "spare_pool_device_terminal" or "spare_pool_device_installed")
        {
            return Results.Conflict(ApiResponse<object>.Fail("Only a non-terminal device without a current installation can enter the spare-pool plan."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_entry_exists")
        {
            return Results.Conflict(ApiResponse<object>.Fail("This device already has spare-pool history."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_entry_missing")
        {
            return Results.Conflict(ApiResponse<object>.Fail("Add this device to a spare pool before changing its pool state."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_case_unavailable")
        {
            return Results.Conflict(ApiResponse<object>.Fail("The RMA case is unavailable, resolved, out of scope, or belongs to this same device."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_not_available")
        {
            return Results.Conflict(ApiResponse<object>.Fail("The spare device is not currently available for that action."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_not_reserved")
        {
            return Results.Conflict(ApiResponse<object>.Fail("The spare device does not have an active reservation to release."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "spare_pool_receipt_missing")
        {
            return Results.Conflict(ApiResponse<object>.Fail("The spare-pool action was recorded but its receipt could not be read."));
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("The spare-pool action conflicted with current device, case, or pool history. Refresh and try again."));
        }

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            entry,
            poolEvent,
            idempotentReplay,
            physicalPossessionClaim = false,
            conditionVerifiedClaim = false,
            compatibilityClaim = false,
            certificationClaim = false,
            note = "Spare-pool planning was recorded. Physical possession, condition, compatibility, installation, and certification remain unverified."
        }, idempotentReplay ? "Spare-pool action already recorded" : "Spare-pool action recorded"));
    }

    private const string SparePoolEntryProjection = @"SELECT id,device_id,device_serial_snapshot,pool_name,
        entry_reason,source_reference,inventory_assurance_status,physical_possession_claim,
        condition_verified_claim,certification_claim,added_by,created_at FROM device_spare_pool_entries";
    private const string SparePoolEventProjection = @"SELECT event.id,event.entry_id,event.device_id,event.action_type,
        event.state_after,event.rma_case_id,event.failed_device_id,event.action_reason,event.source_reference,
        event.effective_at,event.event_status,event.physical_possession_claim,event.condition_verified_claim,
        event.compatibility_claim,event.certification_claim,event.recorded_by,event.created_at,entry.pool_name
        FROM device_spare_pool_events event JOIN device_spare_pool_entries entry
          ON entry.company_id=event.company_id AND entry.id=event.entry_id";

    private static Task<Dictionary<string, object?>?> LoadSparePoolEntryByDevice(
        Database db, long companyId, long deviceId, CancellationToken ct) => db.QuerySingleAsync(
        SparePoolEntryProjection + " WHERE company_id=@company AND device_id=@device",
        command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@device", deviceId); }, ct);
    private static Task<Dictionary<string, object?>?> LoadSparePoolEntryById(
        Database db, long companyId, long entryId, CancellationToken ct) => db.QuerySingleAsync(
        SparePoolEntryProjection + " WHERE company_id=@company AND id=@entry",
        command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@entry", entryId); }, ct);
    private static Task<Dictionary<string, object?>?> LoadLatestSparePoolEvent(
        Database db, long companyId, long entryId, CancellationToken ct) => db.QuerySingleAsync(
        SparePoolEventProjection + @" WHERE event.company_id=@company AND event.entry_id=@entry
            ORDER BY event.effective_at DESC,event.id DESC LIMIT 1 FOR UPDATE",
        command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@entry", entryId); }, ct);
    private static Task<Dictionary<string, object?>?> LoadSparePoolEventById(
        Database db, long companyId, long eventId, CancellationToken ct) => db.QuerySingleAsync(
        SparePoolEventProjection + " WHERE event.company_id=@company AND event.id=@event",
        command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@event", eventId); }, ct);
    private static Task<Dictionary<string, object?>?> LoadSparePoolEventByIdempotency(
        Database db, long companyId, long? branchId, Guid key, CancellationToken ct) => db.QuerySingleAsync(
        SparePoolEventProjection + @" WHERE event.company_id=@company AND event.idempotency_key=@key
            AND (@branch::BIGINT IS NULL OR event.branch_id=@branch)",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
        }, ct);

    private static bool SparePoolReplayMatches(
        Dictionary<string, object?> row, long deviceId, long actorId, ValidatedDeviceSparePoolAction input)
    {
        var storedAction = row["actionType"]?.ToString();
        return string.Equals(storedAction, input.ActionType switch
            { "Add" => "Added", "Reserve" => "Reserved", "Release" => "Released", _ => "Removed" }, StringComparison.Ordinal) &&
            Convert.ToInt64(row["deviceId"]!) == deviceId && Convert.ToInt64(row["recordedBy"]!) == actorId &&
            (input.ActionType != "Add" || string.Equals(row["poolName"]?.ToString(), input.PoolName, StringComparison.Ordinal)) &&
            (input.ActionType != "Reserve" || Convert.ToInt64(row["rmaCaseId"]!) == input.RmaCaseId) &&
            string.Equals(row["actionReason"]?.ToString(), input.ActionReason, StringComparison.Ordinal) &&
            string.Equals(row["sourceReference"]?.ToString(), input.SourceReference, StringComparison.Ordinal) &&
            StoredInstantEquals(row["effectiveAt"], input.EffectiveAt);
    }
}
