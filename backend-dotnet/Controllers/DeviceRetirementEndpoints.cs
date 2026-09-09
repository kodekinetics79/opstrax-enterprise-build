using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private static async Task<IResult> DeviceRetirementCreate(
        HttpContext http,
        long id,
        DeviceRetirementRequest? body,
        Database db,
        AuditService audit,
        CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        var validation = DeviceRetirementPolicy.Validate(body, DateTimeOffset.UtcNow);
        if (validation.Error is not null)
            return Results.BadRequest(ApiResponse<object>.Fail(validation.Error));
        var input = validation.Value!;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = GetUserId(http);
        if (actorId <= 0) return Results.Unauthorized();

        var visibleDevice = await db.QuerySingleAsync(
            @"SELECT id,branch_id,device_serial,status,device_state,row_version
                FROM eld_devices
               WHERE company_id=@company AND id=@device AND deleted_at IS NULL
                 AND (@branch::BIGINT IS NULL OR branch_id=@branch)",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@device", id);
                command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            }, ct);
        if (visibleDevice is null) return Results.NotFound(ApiResponse<object>.Fail("Device not found"));
        var expectedConfirmation = DeviceRetirementPolicy.ConfirmationFor(visibleDevice["deviceSerial"]!.ToString()!);
        if (!string.Equals(input.SafetyConfirmation, expectedConfirmation, StringComparison.Ordinal))
            return Results.BadRequest(ApiResponse<object>.Fail($"Enter {expectedConfirmation} exactly to retire this device."));

        Dictionary<string, object?> retirement;
        bool idempotentReplay;
        try
        {
            (retirement, idempotentReplay) = await db.RunInSystemTransactionAsync(async () =>
            {
                await db.ExecuteAsync(
                    @"SELECT pg_advisory_xact_lock(hashtextextended(identity,0))
                        FROM unnest(@identities::TEXT[]) identity ORDER BY identity",
                    command => command.Parameters.AddWithValue(
                        "@identities", NpgsqlDbType.Array | NpgsqlDbType.Text,
                        new[]
                        {
                            $"device-install-resource:{companyId}:device:{id}",
                            $"connectivity-device:{companyId}:{id}",
                            $"device-retirement:{companyId}:{id}",
                            $"device-retirement-idempotency:{companyId}:{input.IdempotencyKey:D}",
                        }.OrderBy(value => value).ToArray()), ct);

                var replay = await LoadDeviceRetirementByIdempotency(
                    db, companyId, input.IdempotencyKey, ct);
                if (replay is not null)
                {
                    if (!RetirementReplayMatches(replay, id, actorId, input))
                        throw new InvalidOperationException("retirement_idempotency_mismatch");
                    return (replay, true);
                }

                var device = await db.QuerySingleAsync(
                    @"SELECT id,branch_id,device_serial,status,device_state,row_version,revoked_at
                        FROM eld_devices
                       WHERE company_id=@company AND id=@device AND deleted_at IS NULL
                         AND (@branch::BIGINT IS NULL OR branch_id=@branch)
                       FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
                    }, ct);
                if (device is null) throw new InvalidOperationException("retirement_device_changed");
                var serial = device["deviceSerial"]!.ToString()!;
                if (!string.Equals(input.SafetyConfirmation,
                        DeviceRetirementPolicy.ConfirmationFor(serial), StringComparison.Ordinal))
                    throw new InvalidOperationException("retirement_confirmation_changed");
                if (device["status"]?.ToString() == "Retired" || device["deviceState"]?.ToString() == "Retired")
                    throw new InvalidOperationException("retirement_legacy_terminal");
                if (Convert.ToInt64(device["rowVersion"]!) != input.ExpectedRowVersion)
                    throw new InvalidOperationException("retirement_version_changed");

                var currentInstallation = await db.QuerySingleAsync(
                    @"SELECT id,vehicle_id,status FROM device_installations
                       WHERE company_id=@company AND device_id=@device
                         AND effective_to IS NULL AND status IN ('Installed','Verified')
                       ORDER BY effective_from DESC,id DESC LIMIT 1 FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@device", id);
                    }, ct);
                if (currentInstallation is not null)
                    throw new InvalidOperationException("retirement_active_installation");

                var currentConnectivity = await db.QuerySingleAsync(
                    @"SELECT id,effective_from FROM device_connectivity_profiles
                       WHERE company_id=@company AND device_id=@device
                         AND effective_to IS NULL AND assignment_status='Assigned'
                       FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@device", id);
                    }, ct);
                long? endedConnectivityProfileId = null;
                if (currentConnectivity is not null)
                {
                    var assignedAt = currentConnectivity["effectiveFrom"] switch
                    {
                        DateTimeOffset timestamp => timestamp.ToUniversalTime(),
                        DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                        _ => throw new InvalidOperationException("retirement_connectivity_history_invalid"),
                    };
                    if (input.EffectiveAt <= assignedAt)
                        throw new InvalidOperationException("retirement_connectivity_time");
                    endedConnectivityProfileId = Convert.ToInt64(currentConnectivity["id"]!);
                    await db.ExecuteAsync(
                        @"UPDATE device_connectivity_profiles
                             SET assignment_status='Ended',effective_to=@effectiveAt,
                                 end_reason=@reason,ended_by=@actor,updated_at=NOW()
                           WHERE company_id=@company AND id=@profile AND device_id=@device
                             AND effective_to IS NULL AND assignment_status='Assigned'",
                        command =>
                        {
                            command.Parameters.AddWithValue("@effectiveAt", input.EffectiveAt);
                            command.Parameters.AddWithValue("@reason", input.RetirementReason);
                            command.Parameters.AddWithValue("@actor", actorId);
                            command.Parameters.AddWithValue("@company", companyId);
                            command.Parameters.AddWithValue("@profile", endedConnectivityProfileId.Value);
                            command.Parameters.AddWithValue("@device", id);
                        }, ct);
                }

                var priorStatus = device["status"]?.ToString() ?? "Unknown";
                var priorState = device["deviceState"]?.ToString() ?? "Unknown";
                var updated = await db.QuerySingleAsync(
                    @"UPDATE eld_devices
                         SET status='Retired',device_state='Retired',retired_at=@effectiveAt,
                             revoked_at=CASE WHEN revoked_at IS NULL OR revoked_at>@effectiveAt THEN @effectiveAt ELSE revoked_at END,
                             credential_revoked_reason='operator_retirement',api_key_hash=NULL,
                             hmac_secret=NULL,hmac_secret_encrypted=NULL,
                             api_key_previous_hash=NULL,api_key_previous_valid_until=NULL,
                             hmac_previous_secret_encrypted=NULL,hmac_previous_valid_until=NULL,
                             updated_at=NOW(),row_version=row_version+1
                       WHERE company_id=@company AND id=@device AND deleted_at IS NULL
                         AND row_version=@expectedVersion
                       RETURNING row_version,retired_at,revoked_at",
                    command =>
                    {
                        command.Parameters.AddWithValue("@effectiveAt", input.EffectiveAt);
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@expectedVersion", input.ExpectedRowVersion);
                    }, ct);
                if (updated is null) throw new InvalidOperationException("retirement_version_changed");
                var rowVersionAfter = Convert.ToInt64(updated["rowVersion"]!);

                await AppendDeviceTransitionAsync(db, companyId,
                    device["branchId"] is null or DBNull ? null : Convert.ToInt64(device["branchId"]),
                    id, priorState, "Retired", actorId, "operator_retirement",
                    input.RetirementReason, http.TraceIdentifier, ct);

                var retirementId = await db.InsertAsync(
                    @"INSERT INTO device_retirement_records
                        (company_id,branch_id,device_id,device_serial_snapshot,retirement_reason,
                         disposition_plan,source_reference,effective_at,prior_status,prior_device_state,
                         row_version_before,row_version_after,ended_connectivity_profile_id,
                         idempotency_key,retired_by)
                      VALUES(@company,@branch,@device,@serial,@reason,@disposition,@source,@effectiveAt,
                             @priorStatus,@priorState,@before,@after,@profile,@key,@actor)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@branch", device["branchId"] is null or DBNull ? DBNull.Value : device["branchId"]!);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@serial", serial);
                        command.Parameters.AddWithValue("@reason", input.RetirementReason);
                        command.Parameters.AddWithValue("@disposition", input.DispositionPlan);
                        command.Parameters.AddWithValue("@source", input.SourceReference);
                        command.Parameters.AddWithValue("@effectiveAt", input.EffectiveAt);
                        command.Parameters.AddWithValue("@priorStatus", priorStatus);
                        command.Parameters.AddWithValue("@priorState", priorState);
                        command.Parameters.AddWithValue("@before", input.ExpectedRowVersion);
                        command.Parameters.AddWithValue("@after", rowVersionAfter);
                        command.Parameters.AddWithValue("@profile", (object?)endedConnectivityProfileId ?? DBNull.Value);
                        command.Parameters.AddWithValue("@key", input.IdempotencyKey);
                        command.Parameters.AddWithValue("@actor", actorId);
                    }, ct);
                await audit.LogAsync(http, "device.retired", "DeviceRetirementRecord", retirementId,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        deviceId = id,
                        serial,
                        input.DispositionPlan,
                        input.SourceReference,
                        input.EffectiveAt,
                        endedConnectivityProfileId,
                        physicalDispositionClaim = false,
                        certificationClaim = false,
                    }), ct);
                return (await LoadDeviceRetirementById(db, companyId, retirementId, ct)
                    ?? throw new InvalidOperationException("retirement_receipt_missing"), false);
            }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "retirement_active_installation")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "Remove the current installation before retiring the device. Retirement cannot assert that physical removal occurred."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "retirement_connectivity_time")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "The retirement time must be later than the current connectivity profile assignment."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "retirement_idempotency_mismatch")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "That idempotency key was already used for a different device retirement."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "retirement_legacy_terminal")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "This device is already marked retired without a governed receipt. Resolve the legacy lifecycle record before retrying."));
        }
        catch (InvalidOperationException ex) when (ex.Message is
                   "retirement_device_changed" or "retirement_confirmation_changed" or
                   "retirement_version_changed" or "retirement_connectivity_history_invalid" or
                   "retirement_receipt_missing")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "The device changed while retirement was being recorded. Refresh and try again."));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "The retirement conflicted with a concurrent lifecycle change. Refresh and try again."));
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ck_stage125_device_not_in_pool")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "Remove the device from its spare-pool plan before retiring it."));
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ck_stage126_device_support_tier_active")
        {
            return Results.Conflict(ApiResponse<object>.Fail(
                "End the device's active support-tier plan before retiring it."));
        }

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            retirement,
            idempotentReplay,
            credentialsRevoked = true,
            physicalDispositionClaim = false,
            certificationClaim = false,
            note = "Software retirement and credential invalidation are recorded. The disposition plan is operator intent; physical disposition and certification remain unverified."
        }, idempotentReplay ? "Device retirement already recorded" : "Device retired"));
    }

    private static Task<Dictionary<string, object?>?> LoadDeviceRetirementByIdempotency(
        Database db, long companyId, Guid idempotencyKey, CancellationToken ct) => db.QuerySingleAsync(
        RetirementProjection + " WHERE company_id=@company AND idempotency_key=@key",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@key", idempotencyKey);
        }, ct);

    private static Task<Dictionary<string, object?>?> LoadDeviceRetirementById(
        Database db, long companyId, long retirementId, CancellationToken ct) => db.QuerySingleAsync(
        RetirementProjection + " WHERE company_id=@company AND id=@retirement",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@retirement", retirementId);
        }, ct);

    private const string RetirementProjection = @"SELECT id,device_id,device_serial_snapshot,
        retirement_reason,disposition_plan,source_reference,effective_at,prior_status,
        prior_device_state,row_version_before,row_version_after,ended_connectivity_profile_id,
        credentials_revoked,record_status,physical_disposition_status,
        physical_disposition_claim,certification_claim,retired_by,created_at
        FROM device_retirement_records";

    private static bool RetirementReplayMatches(
        Dictionary<string, object?> row,
        long deviceId,
        long actorId,
        ValidatedDeviceRetirement input) =>
        Convert.ToInt64(row["deviceId"]!) == deviceId &&
        Convert.ToInt64(row["retiredBy"]!) == actorId &&
        Convert.ToInt64(row["rowVersionBefore"]!) == input.ExpectedRowVersion &&
        string.Equals(row["retirementReason"]?.ToString(), input.RetirementReason, StringComparison.Ordinal) &&
        string.Equals(row["dispositionPlan"]?.ToString(), input.DispositionPlan, StringComparison.Ordinal) &&
        string.Equals(row["sourceReference"]?.ToString(), input.SourceReference, StringComparison.Ordinal) &&
        StoredInstantEquals(row["effectiveAt"], input.EffectiveAt);
}
