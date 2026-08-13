using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private sealed record DeviceInstallationCreateBody(
        long VehicleId,
        string DeviceRole = "GPS",
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
        string DeviceRole = "GPS",
        bool IsPrimary = true,
        DateTimeOffset? EffectiveAt = null,
        string? InstallationLocation = null,
        decimal? OdometerAtInstallation = null,
        string? CommissioningMethod = null,
        int? ExpectedRowVersion = null,
        string? IdempotencyKey = null);

    private static readonly HashSet<string> InstallationRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "GPS", "ELD", "Dashcam", "OBD-II", "J1939/CAN", "Temperature", "Fuel", "Tire", "BLE Gateway", "Other"
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
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            currentInstallation = rows.FirstOrDefault(row =>
                row.GetValueOrDefault("effectiveTo") is null or DBNull &&
                row.GetValueOrDefault("status")?.ToString() is "Installed" or "Verified"),
            installationHistory = rows
        }, "Device installations"));
    }

    private static async Task<IResult> DeviceInstallationCreate(
        HttpContext http, long id, DeviceInstallationCreateBody body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        if (!InstallationRoles.Contains(body.DeviceRole.Trim()))
            return Results.BadRequest(ApiResponse<object>.Fail("Unsupported device role"));
        if (body.OdometerAtInstallation is < 0)
            return Results.BadRequest(ApiResponse<object>.Fail("Installation odometer cannot be negative"));
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
                    "UPDATE eld_devices SET device_state='Installed',vehicle_id=@vid,driver_id=NULL,updated_at=NOW() WHERE company_id=@cid AND id=@did",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); c.Parameters.AddWithValue("@vid", body.VehicleId); }, ct);
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
        var companyId = GetCompanyId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
            var affected = await db.ExecuteAsync(
                @"UPDATE device_installations
                  SET status=CASE WHEN @passed THEN 'Verified' ELSE 'Failed' END,
                      commissioning_result=CASE WHEN @passed THEN 'Passed' ELSE 'Failed' END,
                      verification_reference=COALESCE(@reference,verification_reference),
                      failure_reason=CASE WHEN @passed THEN NULL ELSE COALESCE(@reference,'Commissioning failed') END,
                      updated_at=NOW(),row_version=row_version+1
                  WHERE id=@iid AND device_id=@did AND company_id=@cid AND effective_to IS NULL
                    AND status='Installed' AND row_version=@version
                    AND (NOT @passed OR activation_verified_at IS NOT NULL)",
                c =>
                {
                    c.Parameters.AddWithValue("@iid", installationId); c.Parameters.AddWithValue("@did", id);
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@passed", result == "passed");
                    c.Parameters.AddWithValue("@reference", (object?)Clean(body.VerificationReference) ?? DBNull.Value);
                    c.Parameters.AddWithValue("@version", (object?)body.ExpectedRowVersion ?? DBNull.Value);
                }, ct);
            if (affected == 0)
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
                System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, result }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id = installationId, status = result == "passed" ? "Verified" : "Failed" }));
        }, ct);
    }

    private static async Task<IResult> DeviceInstallationRemove(
        HttpContext http, long id, long installationId, DeviceInstallationRemoveBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(body.RemovalReason))
            return Results.BadRequest(ApiResponse<object>.Fail("Removal reason is required"));
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
        if (!InstallationRoles.Contains(body.DeviceRole.Trim()))
            return Results.BadRequest(ApiResponse<object>.Fail("Unsupported device role"));
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
                    "UPDATE eld_devices SET device_state='Installed',vehicle_id=@vid,driver_id=NULL,updated_at=NOW() WHERE company_id=@cid AND id=@did",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", id); c.Parameters.AddWithValue("@vid", body.VehicleId); }, ct);
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

    private static Task<long> VehicleVisibleAsync(Database db, long companyId, long? branchId, long vehicleId, CancellationToken ct) =>
        db.ScalarLongAsync("SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@cid AND deleted_at IS NULL AND (@branch::bigint IS NULL OR branch_id=@branch)",
            c => { c.Parameters.AddWithValue("@id", vehicleId); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value); }, ct);

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
    private static Guid CorrelationUuid(string value)
    {
        if (Guid.TryParse(value,out var parsed)) return parsed;
        var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0,16));
    }
}
