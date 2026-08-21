using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private sealed record DeviceInstallationCreateBody(
        long VehicleId,
        string DeviceRole,
        bool IsPrimary = true,
        DateTimeOffset? EffectiveFrom = null,
        string? InstallationLocation = null,
        decimal? OdometerAtInstallation = null,
        string? CommissioningMethod = null,
        string? AssignmentReason = null,
        string? IdempotencyKey = null);

    private sealed record DeviceInstallationCommissionBody(
        string Result,
        string? VerificationReference = null,
        int? ExpectedRowVersion = null);

    private sealed record DeviceInstallationRemoveBody(
        string RemovalReason,
        DateTimeOffset? EffectiveTo = null,
        int? ExpectedRowVersion = null);

    private sealed record DeviceInstallationTransferBody(
        long VehicleId,
        long? CurrentInstallationId,
        string RemovalReason,
        string AssignmentReason,
        string DeviceRole,
        bool IsPrimary = true,
        DateTimeOffset? EffectiveAt = null,
        string? InstallationLocation = null,
        decimal? OdometerAtInstallation = null,
        string? CommissioningMethod = null,
        int? ExpectedRowVersion = null,
        string? IdempotencyKey = null);

    private sealed record DeviceInstallationQuarantineResolveBody(
        string ResolutionNotes,
        string? CorrectedDeviceSerial = null,
        string? CorrectedImei = null);

    private sealed record DeviceInstallationEvidenceBody(
        string EvidenceType,
        string ObjectKey,
        string Sha256,
        DateTimeOffset? CapturedAt = null);

    private static readonly HashSet<string> InstallationRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "GPS", "ELD", "Dashcam", "OBD-II", "J1939/CAN", "Temperature", "Fuel", "Tire", "BLE Gateway", "Other"
    };

    private static readonly HashSet<string> InstallationEvidenceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "installation-photo", "serial-label", "wiring-photo", "technician-checklist",
        "commissioning-report", "removal-photo"
    };

    private static async Task<IResult> DeviceInstallationHistory(
        HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.read") is { } denied) return denied;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        if (await DeviceVisibleAsync(db, companyId, branchId, id, ct) == 0)
            return Results.NotFound(ApiResponse<object>.Fail("Device not found"));

        var rows = await db.QueryAsync(
            @"SELECT i.id,i.device_id,i.vehicle_id,i.device_role,i.is_primary,i.status,
                     i.effective_from,i.effective_to,i.installed_by,i.removed_by,
                     i.installation_location,i.odometer_at_installation,
                     i.commissioning_method,i.commissioning_result,i.activation_verified_at,
                     i.verification_reference,i.assignment_reason,i.removal_reason,
                     i.source,i.correlation_id,i.row_version,i.created_at,i.updated_at,
                     v.vehicle_code,v.vin,v.plate_number
              FROM device_installations i
              JOIN vehicles v ON v.id=i.vehicle_id AND v.company_id=i.company_id
              WHERE i.company_id=@cid AND i.device_id=@did
              ORDER BY i.effective_from DESC,i.id DESC",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); }, ct);
        var evidence = await db.QueryAsync(
            @"SELECT e.id,e.installation_id,e.evidence_type,e.object_key,e.sha256,e.captured_at,e.captured_by
                FROM device_installation_evidence e
                JOIN device_installations i ON i.id=e.installation_id AND i.company_id=e.company_id
               WHERE e.company_id=@cid AND i.device_id=@did
               ORDER BY e.captured_at DESC,e.id DESC",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            currentInstallation = rows.FirstOrDefault(row =>
                row.GetValueOrDefault("effectiveTo") is null or DBNull &&
                row.GetValueOrDefault("status")?.ToString() is "Installed" or "Verified"),
            installationHistory = rows,
            installationEvidence = evidence
        }, "Device installations"));
    }

    private static async Task<IResult> DeviceInstallationEvidenceList(
        HttpContext http, long id, long installationId, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.read") is { } denied) return denied;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        if (await InstallationVisibleAsync(db, companyId, branchId, id, installationId, ct) == 0)
            return Results.NotFound(ApiResponse<object>.Fail("Installation not found"));
        var rows = await db.QueryAsync(
            @"SELECT id,installation_id,evidence_type,object_key,sha256,captured_at,captured_by
                FROM device_installation_evidence
               WHERE company_id=@cid AND installation_id=@iid
               ORDER BY captured_at DESC,id DESC",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@iid", installationId); }, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows, "Installation evidence"));
    }

    private static async Task<IResult> DeviceInstallationEvidenceCreate(
        HttpContext http, long id, long installationId, DeviceInstallationEvidenceBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        var evidenceType = Clean(body.EvidenceType)?.ToLowerInvariant();
        var objectKey = Clean(body.ObjectKey);
        var sha256 = Clean(body.Sha256)?.ToLowerInvariant();
        if (evidenceType is null || !InstallationEvidenceTypes.Contains(evidenceType))
            return Results.BadRequest(ApiResponse<object>.Fail("Unsupported installation evidence type"));
        if (objectKey is null || objectKey.Length > 1024 || objectKey.StartsWith('/') || objectKey.Contains('\\') ||
            objectKey.Contains("..", StringComparison.Ordinal) ||
            objectKey.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(objectKey, UriKind.Absolute, out _))
            return Results.BadRequest(ApiResponse<object>.Fail("objectKey must be a relative governed-storage key"));
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            return Results.BadRequest(ApiResponse<object>.Fail("sha256 must contain exactly 64 hexadecimal characters"));
        var capturedAt = body.CapturedAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (capturedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return Results.BadRequest(ApiResponse<object>.Fail("Evidence capture time cannot be in the future"));

        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
            await LockInstallationIdentityAsync(db, companyId, id, null, ct);
            var installation = await db.QuerySingleAsync(
                @"SELECT i.id,i.branch_id,i.effective_from,i.effective_to
                    FROM device_installations i
                    JOIN eld_devices d ON d.id=i.device_id AND d.company_id=i.company_id
                   WHERE i.id=@iid AND i.device_id=@did AND i.company_id=@cid
                     AND (@branch::BIGINT IS NULL OR d.branch_id=@branch)
                   FOR SHARE OF i",
                c =>
                {
                    c.Parameters.AddWithValue("@iid", installationId); c.Parameters.AddWithValue("@did", id);
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
                }, ct);
            if (installation is null) return Results.NotFound(ApiResponse<object>.Fail("Installation not found"));
            var effectiveFrom = new DateTimeOffset(Convert.ToDateTime(installation["effectiveFrom"]).ToUniversalTime());
            if (capturedAt < effectiveFrom)
                return Results.BadRequest(ApiResponse<object>.Fail("Evidence capture time cannot predate the installation"));
            if (evidenceType == "removal-photo" && installation["effectiveTo"] is (null or DBNull))
                return Results.Conflict(ApiResponse<object>.Fail("Removal evidence can only be attached after the installation is removed"));

            var existing = await db.QuerySingleAsync(
                @"SELECT id,installation_id,evidence_type,object_key,sha256,captured_at,captured_by
                    FROM device_installation_evidence
                   WHERE company_id=@cid AND installation_id=@iid AND evidence_type=@type AND sha256=@sha
                   ORDER BY id LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@iid", installationId);
                    c.Parameters.AddWithValue("@type", evidenceType); c.Parameters.AddWithValue("@sha", sha256);
                }, ct);
            if (existing is not null)
                return Results.Ok(ApiResponse<object>.Ok(existing, "Installation evidence already recorded"));

            var evidenceId = await db.InsertAsync(
                @"INSERT INTO device_installation_evidence
                    (company_id,branch_id,installation_id,evidence_type,object_key,sha256,captured_at,captured_by)
                  VALUES (@cid,@branch,@iid,@type,@key,@sha,@captured,@actor)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@branch", installation["branchId"] ?? DBNull.Value);
                    c.Parameters.AddWithValue("@iid", installationId); c.Parameters.AddWithValue("@type", evidenceType);
                    c.Parameters.AddWithValue("@key", objectKey); c.Parameters.AddWithValue("@sha", sha256);
                    c.Parameters.AddWithValue("@captured", capturedAt);
                    c.Parameters.AddWithValue("@actor", actorId > 0 ? actorId : DBNull.Value);
                }, ct);
            await audit.LogAsync(http, "device.installation.evidence.created", "DeviceInstallation", installationId,
                System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, evidenceId, evidenceType, sha256 }), ct);
            return Results.Created($"/api/telemetry/devices/{id}/installations/{installationId}/evidence/{evidenceId}",
                ApiResponse<object>.Ok(new { id = evidenceId, installationId, evidenceType, objectKey, sha256, capturedAt }));
        }, ct);
    }

    private static async Task<IResult> DeviceInstallationCreate(
        HttpContext http, long id, DeviceInstallationCreateBody body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        if (Clean(body.DeviceRole) is not { } requestedRole || !InstallationRoles.Contains(requestedRole))
            return Results.BadRequest(ApiResponse<object>.Fail("Unsupported device role"));
        if (body.OdometerAtInstallation is < 0 or > 9_999_999_999.99m)
            return Results.BadRequest(ApiResponse<object>.Fail("Installation odometer is outside the supported range"));
        if (!ValidLength(body.InstallationLocation, 160) || !ValidLength(body.CommissioningMethod, 80) ||
            !ValidLength(body.AssignmentReason, 500) || !ValidLength(body.IdempotencyKey, 120))
            return Results.BadRequest(ApiResponse<object>.Fail("Installation metadata exceeds its supported length"));
        if (Clean(body.AssignmentReason) is not { Length: >= 4 })
            return Results.BadRequest(ApiResponse<object>.Fail("Assignment reason must contain at least 4 characters"));
        var effectiveFrom = body.EffectiveFrom?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (effectiveFrom > DateTimeOffset.UtcNow)
            return Results.BadRequest(ApiResponse<object>.Fail("Installation effective time cannot be in the future"));
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);

        try
        {
            return await db.RunInTenantTransactionAsync(companyId, async () =>
            {
                await LockInstallationIdentityAsync(db, companyId, id, body.VehicleId, ct);
                var resources = await LoadInstallationResourcesAsync(db, companyId, branchId, id, body.VehicleId, ct);
                if (resources is null)
                    return Results.BadRequest(ApiResponse<object>.Fail("Device and vehicle are not available in this tenant and branch"));
                if (!IsDeviceInstallEligible(resources))
                    return Results.Conflict(ApiResponse<object>.Fail("Device is revoked, retired, quarantined, or otherwise ineligible for installation"));
                var installationBranchId = resources["vehicleBranchId"] is null or DBNull
                    ? (long?)null : Convert.ToInt64(resources["vehicleBranchId"]);
                var deviceBranchId = resources["deviceBranchId"] is null or DBNull
                    ? (long?)null : Convert.ToInt64(resources["deviceBranchId"]);
                if (deviceBranchId.HasValue && deviceBranchId != installationBranchId)
                    return Results.Conflict(ApiResponse<object>.Fail("Device and vehicle must belong to the same branch"));

                if (!string.IsNullOrWhiteSpace(body.IdempotencyKey))
                {
                    var existing = await db.QuerySingleAsync(
                        @"SELECT id,status,row_version,device_id,vehicle_id,device_role,is_primary
                          FROM device_installations WHERE company_id=@cid AND idempotency_key=@key",
                        c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@key", body.IdempotencyKey.Trim()); }, ct);
                    if (existing is not null)
                    {
                        if (Convert.ToInt64(existing["deviceId"]) != id || Convert.ToInt64(existing["vehicleId"]) != body.VehicleId ||
                            !string.Equals(existing["deviceRole"]?.ToString(),NormalizeInstallationRole(body.DeviceRole),StringComparison.Ordinal) ||
                            Convert.ToBoolean(existing["isPrimary"]) != body.IsPrimary)
                            return Results.Conflict(ApiResponse<object>.Fail("Idempotency key was already used for a different installation"));
                        return Results.Ok(ApiResponse<object>.Ok(existing, "Installation already recorded"));
                    }
                }

                var installationId = await db.InsertAsync(
                    @"INSERT INTO device_installations
                        (company_id,branch_id,device_id,vehicle_id,installer_user_id,installed_by,
                         status,device_role,is_primary,effective_from,installed_at,
                         installation_location,odometer_at_installation,commissioning_method,
                         assignment_reason,source,correlation_id,idempotency_key,created_at)
                      VALUES (@cid,@branch,@did,@vid,@actor,@actor,'Installed',@role,@primary,@effective,@effective,
                              @location,@odometer,@method,@reason,'operator',@correlation,@idempotency,NOW())",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@branch", (object?)installationBranchId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@did", id); c.Parameters.AddWithValue("@vid", body.VehicleId);
                        c.Parameters.AddWithValue("@actor", actorId > 0 ? actorId : DBNull.Value);
                        c.Parameters.AddWithValue("@role", NormalizeInstallationRole(body.DeviceRole));
                        c.Parameters.AddWithValue("@primary", body.IsPrimary);
                        c.Parameters.AddWithValue("@effective", effectiveFrom);
                        c.Parameters.AddWithValue("@location", (object?)Clean(body.InstallationLocation) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@odometer", (object?)body.OdometerAtInstallation ?? DBNull.Value);
                        c.Parameters.AddWithValue("@method", (object?)Clean(body.CommissioningMethod) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@reason", (object?)Clean(body.AssignmentReason) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@correlation", http.TraceIdentifier);
                        c.Parameters.AddWithValue("@idempotency", (object?)Clean(body.IdempotencyKey) ?? DBNull.Value);
                    }, ct);
                await db.ExecuteAsync(
                    "UPDATE eld_devices SET device_state='Installed',updated_at=NOW() WHERE company_id=@cid AND id=@did",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); }, ct);
                await AppendDeviceTransitionAsync(db, companyId, installationBranchId, id,
                    resources["deviceState"]?.ToString(), "Installed", actorId,
                    "installation_created", body.AssignmentReason, http.TraceIdentifier, ct);
                await audit.LogAsync(http, "device.installation.created", "DeviceInstallation", installationId,
                    System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, body.VehicleId, effectiveFrom }), ct);
                return Results.Created($"/api/telemetry/devices/{id}/installations/{installationId}",
                    ApiResponse<object>.Ok(new { id = installationId, deviceId = id, body.VehicleId, status = "Installed", effectiveFrom, rowVersion = 1 }));
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("The device or primary vehicle role already has an overlapping installation"));
        }
    }

    private static async Task<IResult> DeviceInstallationCommission(
        HttpContext http, long id, long installationId, DeviceInstallationCommissionBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        var result = body.Result.Trim().ToLowerInvariant();
        if (result is not ("passed" or "failed"))
            return Results.BadRequest(ApiResponse<object>.Fail("Commissioning result must be Passed or Failed"));
        if (body.ExpectedRowVersion is null or <= 0)
            return Results.BadRequest(ApiResponse<object>.Fail("expectedRowVersion is required for commissioning"));
        if (!ValidLength(body.VerificationReference, 500) ||
            (result == "failed" && Clean(body.VerificationReference) is not { Length: >= 8 }))
            return Results.BadRequest(ApiResponse<object>.Fail("Failed commissioning requires a failure reference of 8 to 500 characters"));
        var companyId = GetCompanyId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        // Commissioning proof spans location_events and the system-owned canonical
        // telemetry ledger. The runtime app role is intentionally denied direct
        // access to canonical_telemetry_events, so use the system transaction just
        // like installation creation does. Every query below remains explicitly
        // constrained by company_id/device_id/installation_id.
        return await db.RunInSystemTransactionAsync<IResult>(async () =>
        {
            Dictionary<string, object?>? telemetryProof = null;
            if (result == "passed")
            {
                telemetryProof = await db.QuerySingleAsync(
                    @"SELECT proof_reference,event_time FROM (
                          SELECT 'location-event:'||id::TEXT proof_reference,event_time
                            FROM location_events
                           WHERE company_id=@cid AND installation_id=@iid
                          UNION ALL
                          SELECT 'canonical-telemetry:'||id::TEXT proof_reference,event_time
                            FROM canonical_telemetry_events
                           WHERE company_id=@cid AND installation_id=@iid
                       ) proof ORDER BY event_time DESC LIMIT 1",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@iid", installationId); }, ct);
                if (telemetryProof is null)
                    return Results.Conflict(ApiResponse<object>.Fail("Commissioning requires a persisted authenticated telemetry event from this installation"));
            }
            var proofReference = telemetryProof?["proofReference"]?.ToString() ?? Clean(body.VerificationReference);
            var updated = await db.QuerySingleAsync(
                @"UPDATE device_installations
                  SET status=CASE WHEN @passed THEN 'Verified' ELSE 'Failed' END,
                      commissioning_result=CASE WHEN @passed THEN 'Passed' ELSE 'Failed' END,
                      verification_reference=@reference,
                      failure_reason=CASE WHEN @passed THEN NULL ELSE COALESCE(@reference,'Commissioning failed') END,
                      updated_at=NOW(),row_version=row_version+1
                  WHERE id=@iid AND device_id=@did AND company_id=@cid AND effective_to IS NULL
                    AND status='Installed' AND row_version=@version
                    AND (NOT @passed OR activation_verified_at IS NOT NULL)
                  RETURNING status,commissioning_result,verification_reference,activation_verified_at,row_version,updated_at",
                c =>
                {
                    c.Parameters.AddWithValue("@iid", installationId); c.Parameters.AddWithValue("@did", id);
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@passed", result == "passed");
                    c.Parameters.AddWithValue("@reference", (object?)proofReference ?? DBNull.Value);
                    c.Parameters.AddWithValue("@version", (object?)body.ExpectedRowVersion ?? DBNull.Value);
                }, ct);
            if (updated is null)
                return Results.Conflict(ApiResponse<object>.Fail(result == "passed"
                    ? "Commissioning requires the expected row version and an authenticated device heartbeat"
                    : "Installation not found, closed, or changed"));
            await db.ExecuteAsync(
                @"UPDATE eld_devices SET device_state=CASE WHEN @passed THEN 'Verified' ELSE 'Quarantined' END,
                      updated_at=NOW() WHERE company_id=@cid AND id=@did",
                c => { c.Parameters.AddWithValue("@passed", result == "passed"); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); }, ct);
            await AppendDeviceTransitionAsync(db, companyId, GetBranchId(http), id, "Installed",
                result == "passed" ? "Verified" : "Quarantined", actorId, "commissioning", body.VerificationReference,
                http.TraceIdentifier, ct);
            await audit.LogAsync(http, "device.installation.commissioned", "DeviceInstallation", installationId,
                System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, result, proofReference, operatorReference = Clean(body.VerificationReference) }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new
            {
                id = installationId,
                status = updated["status"],
                commissioningResult = updated["commissioningResult"],
                verificationReference = updated["verificationReference"],
                activationVerifiedAt = updated["activationVerifiedAt"],
                rowVersion = updated["rowVersion"]
            }));
        }, ct);
    }

    private static async Task<IResult> DeviceInstallationRemove(
        HttpContext http, long id, long installationId, DeviceInstallationRemoveBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(body.RemovalReason))
            return Results.BadRequest(ApiResponse<object>.Fail("Removal reason is required"));
        if (Clean(body.RemovalReason) is not { Length: >= 4 } || !ValidLength(body.RemovalReason, 500))
            return Results.BadRequest(ApiResponse<object>.Fail("Removal reason must contain 4 to 500 characters"));
        if (body.ExpectedRowVersion is null or <= 0)
            return Results.BadRequest(ApiResponse<object>.Fail("expectedRowVersion is required for removal"));
        var effectiveTo = body.EffectiveTo?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (effectiveTo > DateTimeOffset.UtcNow)
            return Results.BadRequest(ApiResponse<object>.Fail("Removal effective time cannot be in the future"));
        var companyId = GetCompanyId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
            await LockInstallationIdentityAsync(db, companyId, id, null, ct);
            if (await InstallationHasEventAtOrAfterAsync(db, companyId, installationId, effectiveTo, ct) != 0)
                return Results.Conflict(ApiResponse<object>.Fail(
                    "Removal cannot predate telemetry already attributed to this installation; use the current time or an append-only correction workflow"));
            var affected = await db.ExecuteAsync(
                @"UPDATE device_installations
                  SET status='Removed',effective_to=@effective,removed_at=@effective,removed_by=@actor,
                      removal_reason=@reason,updated_at=NOW(),row_version=row_version+1
                  WHERE id=@iid AND device_id=@did AND company_id=@cid AND effective_to IS NULL
                    AND status IN ('Installed','Verified') AND effective_from<@effective
                    AND row_version=@version",
                c =>
                {
                    c.Parameters.AddWithValue("@iid", installationId); c.Parameters.AddWithValue("@did", id);
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@effective", effectiveTo);
                    c.Parameters.AddWithValue("@actor", actorId > 0 ? actorId : DBNull.Value);
                    c.Parameters.AddWithValue("@reason", body.RemovalReason.Trim());
                    c.Parameters.AddWithValue("@version", body.ExpectedRowVersion.Value);
                }, ct);
            if (affected == 0) return Results.Conflict(ApiResponse<object>.Fail("Installation not found, closed, or changed"));
            await db.ExecuteAsync(
                "UPDATE eld_devices SET device_state='Registered',updated_at=NOW() WHERE company_id=@cid AND id=@did",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); }, ct);
            await AppendDeviceTransitionAsync(db, companyId, GetBranchId(http), id, "Installed", "Registered", actorId,
                "installation_removed", body.RemovalReason, http.TraceIdentifier, ct);
            await audit.LogAsync(http, "device.installation.removed", "DeviceInstallation", installationId,
                System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, effectiveTo, body.RemovalReason }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id = installationId, status = "Removed", effectiveTo }));
        }, ct);
    }

    private static async Task<IResult> DeviceInstallationTransfer(
        HttpContext http, long id, DeviceInstallationTransferBody body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(body.RemovalReason) || string.IsNullOrWhiteSpace(body.AssignmentReason))
            return Results.BadRequest(ApiResponse<object>.Fail("Removal and assignment reasons are required"));
        if (Clean(body.RemovalReason) is not { Length: >= 4 } || !ValidLength(body.RemovalReason, 500) ||
            Clean(body.AssignmentReason) is not { Length: >= 4 } || !ValidLength(body.AssignmentReason, 500))
            return Results.BadRequest(ApiResponse<object>.Fail("Removal and assignment reasons must contain 4 to 500 characters"));
        if (Clean(body.DeviceRole) is not { } requestedRole || !InstallationRoles.Contains(requestedRole))
            return Results.BadRequest(ApiResponse<object>.Fail("Unsupported device role"));
        if (body.OdometerAtInstallation is < 0 or > 9_999_999_999.99m ||
            !ValidLength(body.InstallationLocation, 160) || !ValidLength(body.CommissioningMethod, 80) ||
            !ValidLength(body.IdempotencyKey, 120))
            return Results.BadRequest(ApiResponse<object>.Fail("Transfer installation metadata is invalid"));
        if (body.CurrentInstallationId is null or <= 0 || body.ExpectedRowVersion is null or <= 0)
            return Results.BadRequest(ApiResponse<object>.Fail("currentInstallationId and expectedRowVersion are required for transfer"));
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        var effectiveAt = body.EffectiveAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (effectiveAt > DateTimeOffset.UtcNow)
            return Results.BadRequest(ApiResponse<object>.Fail("Transfer effective time cannot be in the future"));
        try
        {
            return await db.RunInTenantTransactionAsync(companyId, async () =>
            {
                await LockInstallationIdentityAsync(db, companyId, id, body.VehicleId, ct);
                var resources = await LoadInstallationResourcesAsync(db, companyId, branchId, id, body.VehicleId, ct);
                if (resources is null)
                    return Results.BadRequest(ApiResponse<object>.Fail("Device and target vehicle must exist in the same tenant and branch"));
                if (!IsDeviceInstallEligible(resources))
                    return Results.Conflict(ApiResponse<object>.Fail("Device is revoked, retired, quarantined, or otherwise ineligible for transfer"));
                var installationBranchId = resources["vehicleBranchId"] is null or DBNull
                    ? (long?)null : Convert.ToInt64(resources["vehicleBranchId"]);
                var deviceBranchId = resources["deviceBranchId"] is null or DBNull
                    ? (long?)null : Convert.ToInt64(resources["deviceBranchId"]);
                if (deviceBranchId.HasValue && deviceBranchId != installationBranchId)
                    return Results.Conflict(ApiResponse<object>.Fail("Device and target vehicle must belong to the same branch"));
                if (!string.IsNullOrWhiteSpace(body.IdempotencyKey))
                {
                    var replay = await db.QuerySingleAsync(
                        @"SELECT id,replaced_installation_id,device_id,vehicle_id,status,effective_from,row_version
                          FROM device_installations WHERE company_id=@cid AND idempotency_key=@key",
                        c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@key", body.IdempotencyKey.Trim()); }, ct);
                    if (replay is not null)
                    {
                        if (Convert.ToInt64(replay["deviceId"]) != id || Convert.ToInt64(replay["vehicleId"]) != body.VehicleId)
                            return Results.Conflict(ApiResponse<object>.Fail("Idempotency key was already used for a different transfer"));
                        return Results.Ok(ApiResponse<object>.Ok(replay,"Device transfer already recorded"));
                    }
                }
                var prior = await db.QuerySingleAsync(
                    @"SELECT id,row_version,effective_from FROM device_installations
                      WHERE company_id=@cid AND device_id=@did AND effective_to IS NULL
                        AND status IN ('Installed','Verified') FOR UPDATE",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); }, ct);
                if (prior is null) return Results.Conflict(ApiResponse<object>.Fail("Device has no current installation to transfer"));
                if (Convert.ToInt64(prior["id"]) != body.CurrentInstallationId.Value)
                    return Results.Conflict(ApiResponse<object>.Fail("Installation changed; refresh before transfer"));
                if (Convert.ToInt32(prior["rowVersion"]) != body.ExpectedRowVersion.Value)
                    return Results.Conflict(ApiResponse<object>.Fail("Installation changed; refresh before transfer"));
                if (effectiveAt <= new DateTimeOffset(Convert.ToDateTime(prior["effectiveFrom"]).ToUniversalTime()))
                    return Results.BadRequest(ApiResponse<object>.Fail("Transfer time must follow the current installation start"));

                var priorId = Convert.ToInt64(prior["id"]);
                if (await InstallationHasEventAtOrAfterAsync(db, companyId, priorId, effectiveAt, ct) != 0)
                    return Results.Conflict(ApiResponse<object>.Fail(
                        "Transfer cannot predate telemetry already attributed to the current installation; use the current time or an append-only correction workflow"));
                await db.ExecuteAsync(
                    @"UPDATE device_installations SET status='Removed',effective_to=@at,removed_at=@at,removed_by=@actor,
                         removal_reason=@reason,updated_at=NOW(),row_version=row_version+1 WHERE id=@id",
                    c => { c.Parameters.AddWithValue("@at", effectiveAt); c.Parameters.AddWithValue("@actor", actorId > 0 ? actorId : DBNull.Value); c.Parameters.AddWithValue("@reason", body.RemovalReason.Trim()); c.Parameters.AddWithValue("@id", priorId); }, ct);
                var newId = await db.InsertAsync(
                    @"INSERT INTO device_installations
                      (company_id,branch_id,device_id,vehicle_id,installer_user_id,installed_by,status,
                       device_role,is_primary,effective_from,installed_at,installation_location,
                       odometer_at_installation,commissioning_method,assignment_reason,source,
                       correlation_id,idempotency_key,replaced_installation_id,created_at)
                      VALUES (@cid,@branch,@did,@vid,@actor,@actor,'Installed',@role,@primary,@at,@at,@location,
                              @odometer,@method,@reason,'operator',@correlation,@idempotency,@prior,NOW())",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branch", (object?)installationBranchId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@did", id); c.Parameters.AddWithValue("@vid", body.VehicleId);
                        c.Parameters.AddWithValue("@actor", actorId > 0 ? actorId : DBNull.Value);
                        c.Parameters.AddWithValue("@role", NormalizeInstallationRole(body.DeviceRole)); c.Parameters.AddWithValue("@primary", body.IsPrimary);
                        c.Parameters.AddWithValue("@at", effectiveAt); c.Parameters.AddWithValue("@location", (object?)Clean(body.InstallationLocation) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@odometer", (object?)body.OdometerAtInstallation ?? DBNull.Value);
                        c.Parameters.AddWithValue("@method", (object?)Clean(body.CommissioningMethod) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@reason", body.AssignmentReason.Trim()); c.Parameters.AddWithValue("@correlation", http.TraceIdentifier);
                        c.Parameters.AddWithValue("@idempotency", (object?)Clean(body.IdempotencyKey) ?? DBNull.Value); c.Parameters.AddWithValue("@prior", priorId);
                    }, ct);
                await db.ExecuteAsync(
                    "UPDATE eld_devices SET device_state='Installed',updated_at=NOW() WHERE company_id=@cid AND id=@did",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); }, ct);
                await AppendDeviceTransitionAsync(db, companyId, installationBranchId, id, "Installed", "Installed", actorId,
                    "installation_transferred", $"{body.RemovalReason}; {body.AssignmentReason}", http.TraceIdentifier, ct);
                await audit.LogAsync(http, "device.installation.transferred", "DeviceInstallation", newId,
                    System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, priorInstallationId = priorId, body.VehicleId, effectiveAt }), ct);
                return Results.Ok(ApiResponse<object>.Ok(new { priorInstallationId = priorId, id = newId, body.VehicleId, effectiveFrom = effectiveAt, status = "Installed" }, "Device transferred"));
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("Target installation conflicts with an active device or primary vehicle role"));
        }
    }

    private static Task<long> DeviceVisibleAsync(Database db, long companyId, long? branchId, long deviceId, CancellationToken ct) =>
        db.ScalarLongAsync("SELECT COUNT(*) FROM eld_devices WHERE id=@id AND company_id=@cid AND deleted_at IS NULL AND (@branch::bigint IS NULL OR branch_id=@branch)",
            c => { c.Parameters.AddWithValue("@id", deviceId); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value); }, ct);

    private static Task<long> InstallationVisibleAsync(
        Database db, long companyId, long? branchId, long deviceId, long installationId, CancellationToken ct) =>
        db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM device_installations i
                JOIN eld_devices d ON d.id=i.device_id AND d.company_id=i.company_id
               WHERE i.id=@iid AND i.device_id=@did AND i.company_id=@cid
                 AND d.deleted_at IS NULL AND (@branch::BIGINT IS NULL OR d.branch_id=@branch)",
            c =>
            {
                c.Parameters.AddWithValue("@iid", installationId); c.Parameters.AddWithValue("@did", deviceId);
                c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            }, ct);

    private static Task<long> VehicleVisibleAsync(Database db, long companyId, long? branchId, long vehicleId, CancellationToken ct) =>
        db.ScalarLongAsync("SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@cid AND deleted_at IS NULL AND (@branch::bigint IS NULL OR branch_id=@branch)",
            c => { c.Parameters.AddWithValue("@id", vehicleId); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value); }, ct);

    private static Task<long> InstallationHasEventAtOrAfterAsync(
        Database db, long companyId, long installationId, DateTimeOffset effectiveTo, CancellationToken ct) =>
        db.ScalarLongAsync(
            @"SELECT CASE WHEN EXISTS (
                   SELECT 1 FROM location_events
                    WHERE company_id=@cid AND installation_id=@iid AND event_time>=@effective
                 ) OR EXISTS (
                   SELECT 1 FROM canonical_telemetry_events
                    WHERE company_id=@cid AND installation_id=@iid AND event_time>=@effective
                 ) THEN 1 ELSE 0 END",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@iid", installationId);
                c.Parameters.AddWithValue("@effective", effectiveTo);
            }, ct);

    private static async Task<IResult> DeviceInstallationQuarantineList(
        HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.read") is { } denied) return denied;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var rows = await db.RunInSystemScopeAsync(() => db.QueryAsync(
            @"SELECT q.id,q.device_id,q.vehicle_id,q.installation_id,q.reason_code,q.evidence_json,
                     q.detected_at,q.resolved_at,q.resolved_by,q.resolution_notes,
                     d.device_serial,d.imei,d.device_state,v.vehicle_code
                FROM device_installation_quarantine q
                LEFT JOIN eld_devices d ON d.id=q.device_id AND d.company_id=q.company_id
                LEFT JOIN vehicles v ON v.id=q.vehicle_id AND v.company_id=q.company_id
               WHERE q.company_id=@cid AND q.resolved_at IS NULL
                 AND (@branch::BIGINT IS NULL OR d.branch_id=@branch OR v.branch_id=@branch)
               ORDER BY q.detected_at,q.id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            }, ct), ct);
        return Results.Ok(ApiResponse<object>.Ok(rows, "Unresolved fleet identity quarantine"));
    }

    private static async Task<IResult> DeviceInstallationQuarantineResolve(
        HttpContext http, long id, DeviceInstallationQuarantineResolveBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        var notes = Clean(body.ResolutionNotes);
        if (notes is null || notes.Length < 8 || notes.Length > 2000)
            return Results.BadRequest(ApiResponse<object>.Fail("Resolution notes must contain 8 to 2000 characters"));
        var serial = Clean(body.CorrectedDeviceSerial);
        var imei = Clean(body.CorrectedImei);
        if (serial is not null && (serial.Length < 4 || serial.Length > 120 ||
            !serial.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':')))
            return Results.BadRequest(ApiResponse<object>.Fail("Corrected serial must be 4 to 120 letters, numbers, dashes, underscores, periods, or colons"));
        if (imei is not null && (imei.Length != 15 || !imei.All(char.IsDigit)))
            return Results.BadRequest(ApiResponse<object>.Fail("Corrected IMEI must contain exactly 15 digits"));
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);

        try
        {
            return await db.RunInSystemTransactionAsync<IResult>(async () =>
            {
                var row = await db.QuerySingleAsync(
                    @"SELECT q.id,q.device_id,q.installation_id,q.reason_code,d.branch_id
                        FROM device_installation_quarantine q
                        LEFT JOIN eld_devices d ON d.id=q.device_id AND d.company_id=q.company_id
                        LEFT JOIN vehicles v ON v.id=q.vehicle_id AND v.company_id=q.company_id
                       WHERE q.id=@id AND q.company_id=@cid AND q.resolved_at IS NULL
                         AND (@branch::BIGINT IS NULL OR d.branch_id=@branch OR v.branch_id=@branch)
                       FOR UPDATE OF q",
                    c =>
                    {
                        c.Parameters.AddWithValue("@id", id);
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
                    }, ct);
                if (row is null) return Results.NotFound(ApiResponse<object>.Fail("Unresolved quarantine record not found"));
                var reasonCode = row["reasonCode"]?.ToString() ?? "";
                var deviceId = row["deviceId"] is null or DBNull ? (long?)null : Convert.ToInt64(row["deviceId"]);

                var needsSerialCorrection = string.Equals(reasonCode, "duplicate_normalized_device_serial", StringComparison.Ordinal);
                var needsImeiCorrection = string.Equals(reasonCode, "duplicate_normalized_imei", StringComparison.Ordinal);
                var needsIdentifierCorrection = needsSerialCorrection || needsImeiCorrection ||
                    string.Equals(reasonCode, "ambiguous_device_identifier", StringComparison.Ordinal);
                if (needsIdentifierCorrection)
                {
                    if (deviceId is null || (serial is null && imei is null) ||
                        (needsSerialCorrection && serial is null) || (needsImeiCorrection && imei is null))
                        return Results.BadRequest(ApiResponse<object>.Fail(
                            needsSerialCorrection ? "Duplicate serial quarantine requires a corrected serial" :
                            needsImeiCorrection ? "Duplicate IMEI quarantine requires a corrected IMEI" :
                            "Identifier ambiguity requires a corrected serial or IMEI"));
                    var conflicts = await db.ScalarLongAsync(
                        @"SELECT COUNT(*) FROM eld_devices other
                           WHERE other.id<>@did AND other.deleted_at IS NULL
                             AND ((@serial::TEXT IS NOT NULL AND
                                   (LOWER(BTRIM(other.device_serial))=LOWER(BTRIM(@serial)) OR LOWER(BTRIM(other.imei))=LOWER(BTRIM(@serial))))
                               OR (@imei::TEXT IS NOT NULL AND
                                   (LOWER(BTRIM(other.imei))=LOWER(BTRIM(@imei)) OR LOWER(BTRIM(other.device_serial))=LOWER(BTRIM(@imei)))))",
                        c =>
                        {
                            c.Parameters.AddWithValue("@did", deviceId.Value);
                            c.Parameters.AddWithValue("@serial", (object?)serial ?? DBNull.Value);
                            c.Parameters.AddWithValue("@imei", (object?)imei ?? DBNull.Value);
                        }, ct);
                    if (conflicts != 0)
                        return Results.Conflict(ApiResponse<object>.Fail("Corrected identifiers still conflict with another registered device"));
                    await db.ExecuteAsync(
                        @"UPDATE eld_devices SET device_serial=COALESCE(@serial,device_serial),imei=COALESCE(@imei,imei),updated_at=NOW()
                           WHERE id=@did AND company_id=@cid",
                        c =>
                        {
                            c.Parameters.AddWithValue("@serial", (object?)serial ?? DBNull.Value);
                            c.Parameters.AddWithValue("@imei", (object?)imei ?? DBNull.Value);
                            c.Parameters.AddWithValue("@did", deviceId.Value);
                            c.Parameters.AddWithValue("@cid", companyId);
                        }, ct);
                }

                await db.ExecuteAsync(
                    @"UPDATE device_installation_quarantine
                         SET resolved_at=NOW(),resolved_by=@actor,resolution_notes=@notes
                       WHERE id=@id AND company_id=@cid AND resolved_at IS NULL",
                    c =>
                    {
                        c.Parameters.AddWithValue("@actor", actorId > 0 ? actorId : DBNull.Value);
                        c.Parameters.AddWithValue("@notes", notes);
                        c.Parameters.AddWithValue("@id", id);
                        c.Parameters.AddWithValue("@cid", companyId);
                    }, ct);
                if (deviceId is { } resolvedDeviceId)
                {
                    await db.ExecuteAsync(
                        @"UPDATE eld_devices d SET device_state=CASE
                              WHEN EXISTS (SELECT 1 FROM device_installations i WHERE i.company_id=d.company_id AND i.device_id=d.id AND i.status='Verified' AND i.effective_to IS NULL) THEN 'Verified'
                              WHEN EXISTS (SELECT 1 FROM device_installations i WHERE i.company_id=d.company_id AND i.device_id=d.id AND i.status='Installed' AND i.effective_to IS NULL) THEN 'Installed'
                              ELSE 'Registered' END,updated_at=NOW()
                           WHERE d.id=@did AND d.company_id=@cid AND d.device_state='Quarantined'
                             AND NOT EXISTS (SELECT 1 FROM device_installation_quarantine q WHERE q.company_id=d.company_id AND q.device_id=d.id AND q.resolved_at IS NULL)",
                        c => { c.Parameters.AddWithValue("@did", resolvedDeviceId); c.Parameters.AddWithValue("@cid", companyId); }, ct);
                }
                await audit.LogAsync(http, "device.installation.quarantine.resolved", "DeviceInstallationQuarantine", id,
                    System.Text.Json.JsonSerializer.Serialize(new { reasonCode, deviceId, correctedSerial = serial is not null, correctedImei = imei is not null, notes }), ct);
                return Results.Ok(ApiResponse<object>.Ok(new { id, reasonCode, resolved = true }, "Fleet identity quarantine resolved"));
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("Corrected device identity conflicts with the governed registry"));
        }
    }

    private static Task<Dictionary<string, object?>?> LoadInstallationResourcesAsync(
        Database db,long companyId,long? authorizedBranchId,long deviceId,long vehicleId,CancellationToken ct) =>
        db.QuerySingleAsync(
            @"SELECT d.branch_id device_branch_id,d.device_state,d.status device_status,d.revoked_at,
                     v.branch_id vehicle_branch_id
              FROM eld_devices d JOIN vehicles v ON v.company_id=d.company_id
              WHERE d.company_id=@cid AND d.id=@did AND v.id=@vid
                AND d.deleted_at IS NULL AND v.deleted_at IS NULL
                AND (@branch::BIGINT IS NULL OR (d.branch_id=@branch OR d.branch_id IS NULL) AND v.branch_id=@branch)",
            c =>
            {
                c.Parameters.AddWithValue("@cid",companyId); c.Parameters.AddWithValue("@did",deviceId);
                c.Parameters.AddWithValue("@vid",vehicleId); c.Parameters.AddWithValue("@branch",(object?)authorizedBranchId ?? DBNull.Value);
            },ct);

    private static bool IsDeviceInstallEligible(Dictionary<string, object?> row)
    {
        var state = row["deviceState"]?.ToString() ?? "";
        var status = row["deviceStatus"]?.ToString() ?? "";
        return row["revokedAt"] is null or DBNull
            && !new[] { "Suspended","Quarantined","Lost","Decommissioning","Decommissioned","Retired" }
                .Contains(state,StringComparer.OrdinalIgnoreCase)
            && !new[] { "Revoked","Suspended","Retired","Decommissioned" }
                .Contains(status,StringComparer.OrdinalIgnoreCase);
    }

    private static async Task LockInstallationIdentityAsync(Database db, long companyId, long deviceId, long? vehicleId, CancellationToken ct)
    {
        await db.ExecuteAsync("SELECT pg_advisory_xact_lock(@key)", c => c.Parameters.AddWithValue("@key", HashCode.Combine(companyId, deviceId)), ct);
        if (vehicleId is { } value)
            await db.ExecuteAsync("SELECT pg_advisory_xact_lock(@key)", c => c.Parameters.AddWithValue("@key", HashCode.Combine(companyId, value, 80)), ct);
    }

    private static Task AppendDeviceTransitionAsync(Database db, long companyId, long? branchId, long deviceId,
        string? fromState, string toState, long actorId, string reasonCode, string? reason, string correlationId, CancellationToken ct) =>
        db.ExecuteAsync(@"INSERT INTO device_state_transitions
          (company_id,branch_id,device_id,from_state,to_state,reason_code,reason,actor_user_id,correlation_id,occurred_at)
          VALUES (@cid,@branch,@did,@from,@to,@code,@reason,@actor,@correlation,NOW())", c =>
        {
            c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            c.Parameters.AddWithValue("@did", deviceId); c.Parameters.AddWithValue("@from", (object?)fromState ?? DBNull.Value);
            c.Parameters.AddWithValue("@to", toState); c.Parameters.AddWithValue("@code", reasonCode);
            c.Parameters.AddWithValue("@reason", (object?)Clean(reason) ?? DBNull.Value);
            c.Parameters.AddWithValue("@actor", actorId > 0 ? actorId : DBNull.Value);
            c.Parameters.AddWithValue("@correlation", CorrelationUuid(correlationId));
        }, ct);

    private static string NormalizeInstallationRole(string role) => InstallationRoles.First(candidate => candidate.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase));
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidLength(string? value, int maximum)
    {
        var cleaned = Clean(value);
        return cleaned is null || cleaned.Length <= maximum;
    }
    private static Guid CorrelationUuid(string value)
    {
        if (Guid.TryParse(value,out var parsed)) return parsed;
        var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0,16));
    }
}
