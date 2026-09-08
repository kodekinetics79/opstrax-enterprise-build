using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private static async Task<IResult> DeviceSupportTierActionCreate(
        HttpContext http,
        long id,
        DeviceSupportTierActionRequest? body,
        Database db,
        AuditService audit,
        CancellationToken ct)
    {
        if (RequirePermission(http, "telematics:devices:rma") is { } denied) return denied;
        var validation = DeviceSupportTierPolicy.Validate(body, DateTimeOffset.UtcNow);
        if (validation.Error is not null)
            return Results.BadRequest(ApiResponse<object>.Fail(validation.Error));
        var input = validation.Value!;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = GetUserId(http);
        if (actorId <= 0) return Results.Unauthorized();

        Dictionary<string, object?> supportEvent;
        bool idempotentReplay;
        try
        {
            (supportEvent, idempotentReplay) = await db.RunInSystemTransactionAsync(async () =>
            {
                await db.ExecuteAsync(
                    @"SELECT pg_advisory_xact_lock(hashtextextended(identity,0))
                        FROM unnest(@identities::TEXT[]) identity ORDER BY identity",
                    command => command.Parameters.AddWithValue("@identities",
                        NpgsqlDbType.Array | NpgsqlDbType.Text, new[]
                        {
                            $"device-support-tier:{companyId}:{id}",
                            $"device-support-tier-idempotency:{companyId}:{input.IdempotencyKey:D}",
                        }.OrderBy(value => value).ToArray()), ct);

                var replay = await LoadSupportTierEventByIdempotency(
                    db, companyId, branchId, input.IdempotencyKey, ct);
                if (replay is not null)
                {
                    if (!SupportTierReplayMatches(replay, id, actorId, input))
                        throw new InvalidOperationException("support_tier_idempotency_mismatch");
                    return (replay, true);
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
                if (device is null) throw new InvalidOperationException("support_tier_device_missing");
                long? deviceBranch = device["branchId"] is null or DBNull
                    ? null
                    : Convert.ToInt64(device["branchId"]!);
                var prior = await LoadLatestSupportTierEvent(db, companyId, id, ct);

                string storedAction;
                string stateAfter;
                string tier;
                string coverage;
                int responseTarget;
                string escalation;
                string commercial;
                if (input.ActionType == "Assign")
                {
                    if (prior is not null && prior["stateAfter"]?.ToString() == "Assigned")
                        throw new InvalidOperationException("support_tier_already_assigned");
                    storedAction = "Assigned";
                    stateAfter = "Assigned";
                    (tier, coverage, responseTarget, escalation, commercial) = InputPlan(input);
                }
                else if (input.ActionType == "Change")
                {
                    if (prior is null || prior["stateAfter"]?.ToString() != "Assigned")
                        throw new InvalidOperationException("support_tier_not_assigned");
                    storedAction = "Changed";
                    stateAfter = "Assigned";
                    (tier, coverage, responseTarget, escalation, commercial) = InputPlan(input);
                }
                else
                {
                    if (prior is null || prior["stateAfter"]?.ToString() != "Assigned")
                        throw new InvalidOperationException("support_tier_not_assigned");
                    storedAction = "Ended";
                    stateAfter = "NotAssigned";
                    tier = prior["tierCode"]!.ToString()!;
                    coverage = prior["coverageWindow"]!.ToString()!;
                    responseTarget = Convert.ToInt32(prior["routingResponseTargetMinutes"]!);
                    escalation = prior["escalationPolicyReference"]!.ToString()!;
                    commercial = prior["commercialReference"]!.ToString()!;
                }

                if (storedAction != "Ended" &&
                    (device["status"]?.ToString() is "Revoked" or "Retired" ||
                     device["deviceState"]?.ToString() is "Decommissioned" or "Retired"))
                    throw new InvalidOperationException("support_tier_device_terminal");

                var eventId = await db.InsertAsync(
                    @"INSERT INTO device_support_tier_events
                        (company_id,branch_id,device_id,device_serial_snapshot,action_type,state_after,
                         tier_code,coverage_window,routing_response_target_minutes,
                         escalation_policy_reference,commercial_reference,action_reason,source_reference,
                         effective_at,idempotency_key,recorded_by)
                      VALUES(@company,@branch,@device,@serial,@action,@state,@tier,@coverage,@target,
                             @escalation,@commercial,@reason,@source,@effectiveAt,@key,@actor)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@branch", (object?)deviceBranch ?? DBNull.Value);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@serial", device["deviceSerial"]!);
                        command.Parameters.AddWithValue("@action", storedAction);
                        command.Parameters.AddWithValue("@state", stateAfter);
                        command.Parameters.AddWithValue("@tier", tier);
                        command.Parameters.AddWithValue("@coverage", coverage);
                        command.Parameters.AddWithValue("@target", responseTarget);
                        command.Parameters.AddWithValue("@escalation", escalation);
                        command.Parameters.AddWithValue("@commercial", commercial);
                        command.Parameters.AddWithValue("@reason", input.ActionReason);
                        command.Parameters.AddWithValue("@source", input.SourceReference);
                        command.Parameters.AddWithValue("@effectiveAt", input.EffectiveAt);
                        command.Parameters.AddWithValue("@key", input.IdempotencyKey);
                        command.Parameters.AddWithValue("@actor", actorId);
                    }, ct);
                await audit.LogAsync(http, $"device.support_tier.{storedAction.ToLowerInvariant()}",
                    "DeviceSupportTierEvent", eventId,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        deviceId = id, actionType = storedAction, stateAfter, tierCode = tier,
                        coverageWindow = coverage, routingResponseTargetMinutes = responseTarget,
                        input.SourceReference, commercialEntitlementVerifiedClaim = false,
                        providerSupportClaim = false, hardwareSupportabilityClaim = false,
                        certificationClaim = false,
                    }), ct);
                return (await LoadSupportTierEventById(db, companyId, eventId, ct)
                    ?? throw new InvalidOperationException("support_tier_receipt_missing"), false);
            }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "support_tier_idempotency_mismatch")
        {
            return Results.Conflict(ApiResponse<object>.Fail("That idempotency key was already used for a different support-tier action."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "support_tier_device_missing")
        {
            return Results.NotFound(ApiResponse<object>.Fail("Device not found in the current scope."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "support_tier_device_terminal")
        {
            return Results.Conflict(ApiResponse<object>.Fail("A terminal device cannot receive an active support-tier plan."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "support_tier_already_assigned")
        {
            return Results.Conflict(ApiResponse<object>.Fail("This device already has an active support tier. Record a change instead."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "support_tier_not_assigned")
        {
            return Results.Conflict(ApiResponse<object>.Fail("This device does not have an active support tier for that action."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "support_tier_receipt_missing")
        {
            return Results.Conflict(ApiResponse<object>.Fail("The support-tier action was recorded but its receipt could not be read."));
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("The support-tier action conflicted with current device or tier history. Refresh and try again."));
        }

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            supportTierEvent = supportEvent,
            idempotentReplay,
            commercialEntitlementVerifiedClaim = false,
            providerSupportClaim = false,
            hardwareSupportabilityClaim = false,
            certificationClaim = false,
            note = "Support routing was recorded. Commercial entitlement, provider support, hardware supportability, and certification remain unverified."
        }, idempotentReplay ? "Support-tier action already recorded" : "Support-tier action recorded"));
    }

    private const string SupportTierProjection = @"SELECT id,device_id,device_serial_snapshot,action_type,
        state_after,tier_code,coverage_window,routing_response_target_minutes,
        escalation_policy_reference,commercial_reference,action_reason,source_reference,effective_at,
        record_status,commercial_entitlement_verified_claim,provider_support_claim,
        hardware_supportability_claim,certification_claim,recorded_by,created_at
        FROM device_support_tier_events";

    private static Task<Dictionary<string, object?>?> LoadLatestSupportTierEvent(
        Database db, long companyId, long deviceId, CancellationToken ct) => db.QuerySingleAsync(
        SupportTierProjection + @" WHERE company_id=@company AND device_id=@device
            ORDER BY effective_at DESC,id DESC LIMIT 1 FOR UPDATE",
        command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@device", deviceId); }, ct);
    private static Task<Dictionary<string, object?>?> LoadSupportTierEventById(
        Database db, long companyId, long eventId, CancellationToken ct) => db.QuerySingleAsync(
        SupportTierProjection + " WHERE company_id=@company AND id=@event",
        command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@event", eventId); }, ct);
    private static Task<Dictionary<string, object?>?> LoadSupportTierEventByIdempotency(
        Database db, long companyId, long? branchId, Guid key, CancellationToken ct) => db.QuerySingleAsync(
        SupportTierProjection + @" WHERE company_id=@company AND idempotency_key=@key
            AND (@branch::BIGINT IS NULL OR branch_id=@branch)",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
        }, ct);

    private static (string Tier, string Coverage, int Target, string Escalation, string Commercial)
        InputPlan(ValidatedDeviceSupportTierAction input) =>
        (input.TierCode!, input.CoverageWindow!, input.RoutingResponseTargetMinutes!.Value,
         input.EscalationPolicyReference!, input.CommercialReference!);

    private static bool SupportTierReplayMatches(
        Dictionary<string, object?> row, long deviceId, long actorId, ValidatedDeviceSupportTierAction input)
    {
        var storedAction = row["actionType"]?.ToString();
        var expectedAction = input.ActionType switch { "Assign" => "Assigned", "Change" => "Changed", _ => "Ended" };
        return string.Equals(storedAction, expectedAction, StringComparison.Ordinal) &&
            Convert.ToInt64(row["deviceId"]!) == deviceId && Convert.ToInt64(row["recordedBy"]!) == actorId &&
            (input.ActionType == "End" ||
             (string.Equals(row["tierCode"]?.ToString(), input.TierCode, StringComparison.Ordinal) &&
              string.Equals(row["coverageWindow"]?.ToString(), input.CoverageWindow, StringComparison.Ordinal) &&
              Convert.ToInt32(row["routingResponseTargetMinutes"]!) == input.RoutingResponseTargetMinutes &&
              string.Equals(row["escalationPolicyReference"]?.ToString(), input.EscalationPolicyReference, StringComparison.Ordinal) &&
              string.Equals(row["commercialReference"]?.ToString(), input.CommercialReference, StringComparison.Ordinal))) &&
            string.Equals(row["actionReason"]?.ToString(), input.ActionReason, StringComparison.Ordinal) &&
            string.Equals(row["sourceReference"]?.ToString(), input.SourceReference, StringComparison.Ordinal) &&
            StoredInstantEquals(row["effectiveAt"], input.EffectiveAt);
    }
}
