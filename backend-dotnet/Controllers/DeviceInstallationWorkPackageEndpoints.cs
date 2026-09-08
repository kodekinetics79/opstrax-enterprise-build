using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private sealed record DeviceInstallationWorkPackageCreateBody(
        long VehicleId,
        string WorkOrderReference,
        DateTimeOffset AppointmentStart,
        DateTimeOffset AppointmentEnd,
        string ServiceLocation,
        string WorkScope,
        string IdempotencyKey);

    private sealed record DeviceInstallationChecklistObservationBody(
        string ChecklistItem,
        string ObservedResult,
        string EvidenceReference,
        string ObservationNotes,
        DateTimeOffset? ObservedAt,
        string IdempotencyKey);

    private sealed record DeviceInstallationArtifactReferenceBody(
        string ArtifactType,
        string ObjectKey,
        string Sha256,
        DateTimeOffset? CapturedAt,
        string IdempotencyKey);

    private static readonly HashSet<string> InstallationChecklistItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "DeviceIdentity", "VehicleIdentity", "Mounting", "PrimaryPower", "Ground", "Ignition",
        "GNSSAntenna", "CellularAntenna", "Harness", "CANBus", "CameraAlignment", "SensorPlacement"
    };

    private static readonly HashSet<string> InstallationChecklistResults = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pass", "Fail", "NotObserved", "NotApplicable"
    };

    private static readonly HashSet<string> InstallationArtifactTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "InstallationPhoto", "SerialLabel", "WiringPhoto", "PowerReading", "TechnicianChecklist",
        "CommissioningReport", "RemovalPhoto", "OtherDocument"
    };

    private static async Task<IResult> DeviceInstallationWorkPackageList(
        HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.read") is { } denied) return denied;
        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        if (await DeviceVisibleAsync(db, companyId, branchId, id, ct) == 0)
            return Results.NotFound(ApiResponse<object>.Fail("Device not found"));

        var (workPackages, checklist, artifacts) = await LoadInstallationWorkPackagesAsync(
            db, companyId, branchId, id, ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            workPackages,
            checklistObservations = checklist,
            artifactReferences = artifacts,
            physicalAppointmentClaim = false,
            physicalWorkClaim = false,
            certificationClaim = false,
            note = "Appointments, checklist observations, and artifact hashes are operator-recorded and unverified. They do not prove attendance, physical work, or certification."
        }, "Installation work packages"));
    }

    private static async Task<IResult> DeviceInstallationWorkPackageCreate(
        HttpContext http, long id, DeviceInstallationWorkPackageCreateBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        var reference = Clean(body.WorkOrderReference);
        var location = Clean(body.ServiceLocation);
        var scope = Clean(body.WorkScope);
        var idempotencyKey = Clean(body.IdempotencyKey);
        if (reference is null || reference.Length > 120 || location is null || location.Length > 160 ||
            scope is null or { Length: < 5 } || scope.Length > 1000 || idempotencyKey is null || idempotencyKey.Length > 120)
            return Results.BadRequest(ApiResponse<object>.Fail("Enter a work-order reference, service location, work scope, and idempotency key within the supported lengths"));
        var appointmentStart = body.AppointmentStart.ToUniversalTime();
        var appointmentEnd = body.AppointmentEnd.ToUniversalTime();
        if (appointmentEnd <= appointmentStart || appointmentStart < DateTimeOffset.UtcNow.AddYears(-1) ||
            appointmentEnd > DateTimeOffset.UtcNow.AddYears(2))
            return Results.BadRequest(ApiResponse<object>.Fail("Appointment end must follow the start and the window must be within the supported planning horizon"));

        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        if (actorId <= 0) return Results.Unauthorized();

        try
        {
            return await db.RunInTenantTransactionAsync(companyId, async () =>
            {
                var resources = await db.QuerySingleAsync(
                    @"SELECT d.branch_id device_branch,v.branch_id vehicle_branch,u.branch_id installer_branch,
                             u.full_name installer_name,d.device_serial,v.vehicle_code
                        FROM eld_devices d
                        JOIN vehicles v ON v.company_id=d.company_id AND v.id=@vehicle AND v.deleted_at IS NULL
                        JOIN users u ON u.company_id=d.company_id AND u.id=@actor AND u.status='Active'
                       WHERE d.company_id=@company AND d.id=@device AND d.deleted_at IS NULL
                         AND (@branch::BIGINT IS NULL OR (d.branch_id=@branch AND v.branch_id=@branch))
                       FOR SHARE OF d,v,u",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@vehicle", body.VehicleId);
                        command.Parameters.AddWithValue("@actor", actorId);
                        command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
                    }, ct);
                if (resources is null)
                    return Results.BadRequest(ApiResponse<object>.Fail("Device, vehicle, or installer is outside the active tenant and branch scope"));
                var deviceBranch = NullableLong(resources.GetValueOrDefault("deviceBranch"));
                var vehicleBranch = NullableLong(resources.GetValueOrDefault("vehicleBranch"));
                var installerBranch = NullableLong(resources.GetValueOrDefault("installerBranch"));
                if (deviceBranch != vehicleBranch || (installerBranch.HasValue && installerBranch != vehicleBranch))
                    return Results.Conflict(ApiResponse<object>.Fail("Device, vehicle, and branch-bound installer must belong to the same branch"));

                var existing = await db.QuerySingleAsync(
                    @"SELECT id,device_id,vehicle_id,assigned_installer_user_id,work_order_reference,
                             appointment_start,appointment_end,service_location,work_scope,
                             physical_appointment_claim,physical_work_claim,certification_claim,created_at
                        FROM device_installation_work_packages
                       WHERE company_id=@company AND idempotency_key=@key",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@key", idempotencyKey);
                    }, ct);
                if (existing is not null)
                {
                    var same = Convert.ToInt64(existing["deviceId"]!) == id &&
                               Convert.ToInt64(existing["vehicleId"]!) == body.VehicleId &&
                               Convert.ToInt64(existing["assignedInstallerUserId"]!) == actorId &&
                               string.Equals(existing["workOrderReference"]?.ToString(), reference, StringComparison.Ordinal) &&
                               StoredInstantEquals(existing["appointmentStart"], appointmentStart) &&
                               StoredInstantEquals(existing["appointmentEnd"], appointmentEnd) &&
                               string.Equals(existing["serviceLocation"]?.ToString(), location, StringComparison.Ordinal) &&
                               string.Equals(existing["workScope"]?.ToString(), scope, StringComparison.Ordinal);
                    return same
                        ? Results.Ok(ApiResponse<object>.Ok(existing, "Installation work package already recorded"))
                        : Results.Conflict(ApiResponse<object>.Fail("Idempotency key was already used for a different installation work package"));
                }

                var workPackageId = await db.InsertAsync(
                    @"INSERT INTO device_installation_work_packages
                        (company_id,branch_id,device_id,vehicle_id,assigned_installer_user_id,
                         work_order_reference,appointment_start,appointment_end,service_location,
                         work_scope,idempotency_key,created_by)
                      VALUES(@company,@branch,@device,@vehicle,@actor,@reference,@start,@end,@location,@scope,@key,@actor)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@branch", (object?)vehicleBranch ?? DBNull.Value);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@vehicle", body.VehicleId);
                        command.Parameters.AddWithValue("@actor", actorId);
                        command.Parameters.AddWithValue("@reference", reference);
                        command.Parameters.AddWithValue("@start", appointmentStart);
                        command.Parameters.AddWithValue("@end", appointmentEnd);
                        command.Parameters.AddWithValue("@location", location);
                        command.Parameters.AddWithValue("@scope", scope);
                        command.Parameters.AddWithValue("@key", idempotencyKey);
                    }, ct);
                await audit.LogAsync(http, "device.installation.work_package.created", "DeviceInstallationWorkPackage", workPackageId,
                    System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, body.VehicleId, reference, appointmentStart, appointmentEnd }), ct);
                return Results.Created($"/api/telemetry/devices/{id}/installation-work-packages/{workPackageId}",
                    ApiResponse<object>.Ok(new
                    {
                        id = workPackageId,
                        deviceId = id,
                        body.VehicleId,
                        assignedInstallerUserId = actorId,
                        installerName = resources["installerName"],
                        workOrderReference = reference,
                        appointmentStart,
                        appointmentEnd,
                        serviceLocation = location,
                        workScope = scope,
                        readinessStatus = "AwaitingChecklist",
                        physicalAppointmentClaim = false,
                        physicalWorkClaim = false,
                        certificationClaim = false
                    }, "Installation work package recorded; attendance and physical work remain unverified"));
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("The installation work package conflicts with the current governed inventory; refresh and retry"));
        }
    }

    private static async Task<IResult> DeviceInstallationChecklistObservationCreate(
        HttpContext http, long id, long workPackageId, DeviceInstallationChecklistObservationBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        var item = InstallationChecklistItems.FirstOrDefault(value => value.Equals(Clean(body.ChecklistItem), StringComparison.OrdinalIgnoreCase));
        var result = InstallationChecklistResults.FirstOrDefault(value => value.Equals(Clean(body.ObservedResult), StringComparison.OrdinalIgnoreCase));
        var reference = Clean(body.EvidenceReference);
        var notes = Clean(body.ObservationNotes);
        var idempotencyKey = Clean(body.IdempotencyKey);
        if (item is null || result is null)
            return Results.BadRequest(ApiResponse<object>.Fail("Select a supported checklist item and explicitly observed result"));
        if (reference is null or { Length: < 3 } || reference.Length > 240 || notes is null or { Length: < 3 } ||
            notes.Length > 1000 || idempotencyKey is null || idempotencyKey.Length > 120)
            return Results.BadRequest(ApiResponse<object>.Fail("Checklist evidence reference, notes, and idempotency key are required within the supported lengths"));
        var observedAt = body.ObservedAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (observedAt > DateTimeOffset.UtcNow.AddMinutes(5) || observedAt < DateTimeOffset.UtcNow.AddYears(-1))
            return Results.BadRequest(ApiResponse<object>.Fail("Checklist observation time is outside the supported range"));

        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        if (actorId <= 0) return Results.Unauthorized();
        try
        {
            return await db.RunInTenantTransactionAsync(companyId, async () =>
            {
                var work = await LoadVisibleWorkPackageAsync(db, companyId, branchId, id, workPackageId, actorId, ct);
                if (work is null) return Results.NotFound(ApiResponse<object>.Fail("Installation work package not found"));
                var existing = await db.QuerySingleAsync(
                    @"SELECT id,work_package_id,checklist_item,observed_result,evidence_reference,observation_notes,
                             observed_at,assurance_status,physical_evidence_claim,certification_claim,recorded_by,created_at
                        FROM device_installation_checklist_observations
                       WHERE company_id=@company AND work_package_id=@work AND idempotency_key=@key",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@work", workPackageId);
                        command.Parameters.AddWithValue("@key", idempotencyKey);
                    }, ct);
                if (existing is not null)
                {
                    var same = string.Equals(existing["checklistItem"]?.ToString(), item, StringComparison.Ordinal) &&
                               string.Equals(existing["observedResult"]?.ToString(), result, StringComparison.Ordinal) &&
                               string.Equals(existing["evidenceReference"]?.ToString(), reference, StringComparison.Ordinal) &&
                               string.Equals(existing["observationNotes"]?.ToString(), notes, StringComparison.Ordinal) &&
                               Convert.ToInt64(existing["recordedBy"]!) == actorId &&
                               (!body.ObservedAt.HasValue || StoredInstantEquals(existing["observedAt"], observedAt));
                    return same
                        ? Results.Ok(ApiResponse<object>.Ok(existing, "Checklist observation already recorded"))
                        : Results.Conflict(ApiResponse<object>.Fail("Idempotency key was already used for a different checklist observation"));
                }
                var observationId = await db.InsertAsync(
                    @"INSERT INTO device_installation_checklist_observations
                        (company_id,branch_id,device_id,work_package_id,checklist_item,observed_result,
                         evidence_reference,observation_notes,observed_at,idempotency_key,recorded_by)
                      VALUES(@company,@branch,@device,@work,@item,@result,@reference,@notes,@observed,@key,@actor)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@branch", work["branchId"] ?? DBNull.Value);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@work", workPackageId);
                        command.Parameters.AddWithValue("@item", item);
                        command.Parameters.AddWithValue("@result", result);
                        command.Parameters.AddWithValue("@reference", reference);
                        command.Parameters.AddWithValue("@notes", notes);
                        command.Parameters.AddWithValue("@observed", observedAt);
                        command.Parameters.AddWithValue("@key", idempotencyKey);
                        command.Parameters.AddWithValue("@actor", actorId);
                    }, ct);
                await audit.LogAsync(http, "device.installation.checklist_observation.created", "DeviceInstallationWorkPackage", workPackageId,
                    System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, observationId, item, result, reference }), ct);
                return Results.Created($"/api/telemetry/devices/{id}/installation-work-packages/{workPackageId}/checklist-observations/{observationId}",
                    ApiResponse<object>.Ok(new
                    {
                        id = observationId,
                        workPackageId,
                        checklistItem = item,
                        observedResult = result,
                        evidenceReference = reference,
                        observationNotes = notes,
                        observedAt,
                        assuranceStatus = "Unverified",
                        physicalEvidenceClaim = false,
                        certificationClaim = false
                    }, "Operator checklist observation recorded; independent physical verification remains outstanding"));
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("The checklist observation conflicts with the governed work package; refresh and retry"));
        }
    }

    private static async Task<IResult> DeviceInstallationArtifactReferenceCreate(
        HttpContext http, long id, long workPackageId, DeviceInstallationArtifactReferenceBody body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequirePermission(http, "telemetry.devices.manage") is { } denied) return denied;
        var artifactType = InstallationArtifactTypes.FirstOrDefault(value => value.Equals(Clean(body.ArtifactType), StringComparison.OrdinalIgnoreCase));
        var objectKey = Clean(body.ObjectKey);
        var sha256 = Clean(body.Sha256)?.ToLowerInvariant();
        var idempotencyKey = Clean(body.IdempotencyKey);
        if (artifactType is null)
            return Results.BadRequest(ApiResponse<object>.Fail("Select a supported installation artifact type"));
        if (objectKey is null || objectKey.Length > 1024 || objectKey.StartsWith('/') || objectKey.Contains('\\') ||
            objectKey.Contains("..", StringComparison.Ordinal) || objectKey.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(objectKey, UriKind.Absolute, out _))
            return Results.BadRequest(ApiResponse<object>.Fail("objectKey must be a relative governed-storage key"));
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            return Results.BadRequest(ApiResponse<object>.Fail("sha256 must contain exactly 64 hexadecimal characters"));
        if (idempotencyKey is null || idempotencyKey.Length > 120)
            return Results.BadRequest(ApiResponse<object>.Fail("Idempotency key is required within the supported length"));
        var capturedAt = body.CapturedAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (capturedAt > DateTimeOffset.UtcNow.AddMinutes(5) || capturedAt < DateTimeOffset.UtcNow.AddYears(-1))
            return Results.BadRequest(ApiResponse<object>.Fail("Artifact capture time is outside the supported range"));

        var companyId = GetCompanyId(http);
        var branchId = GetBranchId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        if (actorId <= 0) return Results.Unauthorized();
        try
        {
            return await db.RunInTenantTransactionAsync(companyId, async () =>
            {
                var work = await LoadVisibleWorkPackageAsync(db, companyId, branchId, id, workPackageId, actorId, ct);
                if (work is null) return Results.NotFound(ApiResponse<object>.Fail("Installation work package not found"));
                var existing = await db.QuerySingleAsync(
                    @"SELECT id,work_package_id,artifact_type,object_key,sha256,captured_at,
                             content_verification_status,physical_evidence_claim,certification_claim,recorded_by,created_at
                        FROM device_installation_artifact_references
                       WHERE company_id=@company AND work_package_id=@work
                         AND (idempotency_key=@key OR (artifact_type=@type AND sha256=@sha))
                       ORDER BY id LIMIT 1",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@work", workPackageId);
                        command.Parameters.AddWithValue("@key", idempotencyKey);
                        command.Parameters.AddWithValue("@type", artifactType);
                        command.Parameters.AddWithValue("@sha", sha256);
                    }, ct);
                if (existing is not null)
                {
                    var same = string.Equals(existing["artifactType"]?.ToString(), artifactType, StringComparison.Ordinal) &&
                               string.Equals(existing["objectKey"]?.ToString(), objectKey, StringComparison.Ordinal) &&
                               string.Equals(existing["sha256"]?.ToString(), sha256, StringComparison.Ordinal) &&
                               Convert.ToInt64(existing["recordedBy"]!) == actorId &&
                               (!body.CapturedAt.HasValue || StoredInstantEquals(existing["capturedAt"], capturedAt));
                    return same
                        ? Results.Ok(ApiResponse<object>.Ok(existing, "Installation artifact reference already recorded"))
                        : Results.Conflict(ApiResponse<object>.Fail("Idempotency key or artifact hash was already used for a different reference"));
                }
                var artifactId = await db.InsertAsync(
                    @"INSERT INTO device_installation_artifact_references
                        (company_id,branch_id,device_id,work_package_id,artifact_type,object_key,sha256,
                         captured_at,idempotency_key,recorded_by)
                      VALUES(@company,@branch,@device,@work,@type,@key,@sha,@captured,@idempotency,@actor)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@company", companyId);
                        command.Parameters.AddWithValue("@branch", work["branchId"] ?? DBNull.Value);
                        command.Parameters.AddWithValue("@device", id);
                        command.Parameters.AddWithValue("@work", workPackageId);
                        command.Parameters.AddWithValue("@type", artifactType);
                        command.Parameters.AddWithValue("@key", objectKey);
                        command.Parameters.AddWithValue("@sha", sha256);
                        command.Parameters.AddWithValue("@captured", capturedAt);
                        command.Parameters.AddWithValue("@idempotency", idempotencyKey);
                        command.Parameters.AddWithValue("@actor", actorId);
                    }, ct);
                await audit.LogAsync(http, "device.installation.artifact_reference.created", "DeviceInstallationWorkPackage", workPackageId,
                    System.Text.Json.JsonSerializer.Serialize(new { deviceId = id, artifactId, artifactType, sha256 }), ct);
                return Results.Created($"/api/telemetry/devices/{id}/installation-work-packages/{workPackageId}/artifact-references/{artifactId}",
                    ApiResponse<object>.Ok(new
                    {
                        id = artifactId,
                        workPackageId,
                        artifactType,
                        objectKey,
                        sha256,
                        capturedAt,
                        contentVerificationStatus = "Unverified",
                        physicalEvidenceClaim = false,
                        certificationClaim = false
                    }, "Installation artifact reference recorded; content and physical work remain unverified"));
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return Results.Conflict(ApiResponse<object>.Fail("The artifact reference conflicts with the governed work package; refresh and retry"));
        }
    }

    private static async Task<Dictionary<string, object?>?> LoadVisibleWorkPackageAsync(
        Database db, long companyId, long? branchId, long deviceId, long workPackageId, long actorId, CancellationToken ct)
        => await db.QuerySingleAsync(
            @"SELECT w.id,w.branch_id,w.device_id,w.vehicle_id
                FROM device_installation_work_packages w
                JOIN eld_devices d ON d.company_id=w.company_id AND d.id=w.device_id AND d.deleted_at IS NULL
                JOIN users u ON u.company_id=w.company_id AND u.id=@actor AND u.status='Active'
               WHERE w.company_id=@company AND w.id=@work AND w.device_id=@device
                 AND (@branch::BIGINT IS NULL OR (w.branch_id=@branch AND d.branch_id=@branch))
               FOR SHARE OF w,d,u",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@work", workPackageId);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@actor", actorId);
                command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            }, ct);

    private static async Task<(List<Dictionary<string, object?>> WorkPackages,
        List<Dictionary<string, object?>> Checklist, List<Dictionary<string, object?>> Artifacts)>
        LoadInstallationWorkPackagesAsync(Database db, long companyId, long? branchId, long deviceId, CancellationToken ct)
    {
        var workPackages = await db.QueryAsync(
            @"SELECT w.id,w.device_id,w.vehicle_id,w.assigned_installer_user_id,u.full_name installer_name,
                     w.work_order_reference,w.appointment_start,w.appointment_end,w.service_location,w.work_scope,
                     w.physical_appointment_claim,w.physical_work_claim,w.certification_claim,w.created_at,
                     v.vehicle_code
                FROM device_installation_work_packages w
                JOIN users u ON u.company_id=w.company_id AND u.id=w.assigned_installer_user_id
                JOIN vehicles v ON v.company_id=w.company_id AND v.id=w.vehicle_id
               WHERE w.company_id=@company AND w.device_id=@device
                 AND (@branch::BIGINT IS NULL OR w.branch_id=@branch)
               ORDER BY w.appointment_start DESC,w.id DESC LIMIT 100",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            }, ct);
        var checklist = await db.QueryAsync(
            @"SELECT o.id,o.work_package_id,o.checklist_item,o.observed_result,o.evidence_reference,
                     o.observation_notes,o.observed_at,o.assurance_status,o.physical_evidence_claim,
                     o.certification_claim,o.recorded_by,u.full_name recorded_by_name,o.created_at
                FROM device_installation_checklist_observations o
                JOIN device_installation_work_packages w ON w.company_id=o.company_id AND w.id=o.work_package_id
                JOIN users u ON u.company_id=o.company_id AND u.id=o.recorded_by
               WHERE o.company_id=@company AND o.device_id=@device
                 AND (@branch::BIGINT IS NULL OR o.branch_id=@branch)
               ORDER BY o.observed_at DESC,o.id DESC LIMIT 1000",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            }, ct);
        var artifacts = await db.QueryAsync(
            @"SELECT a.id,a.work_package_id,a.artifact_type,a.object_key,a.sha256,a.captured_at,
                     a.content_verification_status,a.physical_evidence_claim,a.certification_claim,
                     a.recorded_by,u.full_name recorded_by_name,a.created_at
                FROM device_installation_artifact_references a
                JOIN device_installation_work_packages w ON w.company_id=a.company_id AND w.id=a.work_package_id
                JOIN users u ON u.company_id=a.company_id AND u.id=a.recorded_by
               WHERE a.company_id=@company AND a.device_id=@device
                 AND (@branch::BIGINT IS NULL OR a.branch_id=@branch)
               ORDER BY a.captured_at DESC,a.id DESC LIMIT 1000",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
            }, ct);
        return (workPackages, checklist, artifacts);
    }

    private static long? NullableLong(object? value) => value is null or DBNull ? null : Convert.ToInt64(value);

    private static bool StoredInstantEquals(object? value, DateTimeOffset expected)
        => value is not null and not DBNull &&
           new DateTimeOffset(Convert.ToDateTime(value).ToUniversalTime()) == expected.ToUniversalTime();
}
