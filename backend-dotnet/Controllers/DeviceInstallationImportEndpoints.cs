using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private const string InstallationImportIdempotencyOperation = "device.installation.bulk-import";

    private sealed record InstallationImportCandidate(
        int RowNumber,
        string Key,
        string Serial,
        string VehicleCode,
        long? BranchId,
        bool BranchScopeResolved,
        string Role,
        bool IsPrimary,
        DateTimeOffset? EffectiveFrom,
        string? Location,
        decimal? Odometer,
        string? Method,
        string Reason,
        string IdempotencyKey,
        List<string> Errors,
        long? DeviceId = null,
        long? VehicleId = null,
        string? DeviceState = null,
        string? RequestHash = null,
        bool Replay = false,
        bool IdempotencyConflict = false);

    private sealed class InstallationImportPersistenceException(string message) : Exception(message);

    private static IResult DeviceInstallationsImportTemplate(HttpContext http)
    {
        if (RequireAnyDirectPermission(http, "telemetry.devices.manage") is { } denied) return denied;
        const string csv = "deviceSerial,branchCode,vehicleCode,deviceRole,isPrimary,effectiveFrom,installationLocation,odometerAtInstallation,commissioningMethod,assignmentReason,idempotencyKey\n" +
                           "CLHQ-DEV-0001,CL-HQ,CLHQ-V-0001,GPS,true,2026-08-26T12:00:00Z,Front dashboard,100010,CSV onboarding,Initial governed installation,CERT-LARGE-CLHQ-0001\n";
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "device-installations-import-template.csv");
    }

    private static bool InstallationImportExceedsLimit(Dictionary<string, object?> body) =>
        body.TryGetValue("rows", out var raw) && raw is JsonElement element &&
        element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > ImportMaxRows;

    private static string InstallationImportRequestHash(InstallationImportCandidate candidate)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            branchId = candidate.BranchId,
            deviceId = candidate.DeviceId,
            vehicleId = candidate.VehicleId,
            deviceRole = candidate.Role,
            isPrimary = candidate.IsPrimary,
            effectiveFrom = candidate.EffectiveFrom?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            installationLocation = candidate.Location,
            odometerAtInstallation = candidate.Odometer?.ToString("G29", CultureInfo.InvariantCulture),
            commissioningMethod = candidate.Method,
            assignmentReason = candidate.Reason
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string InstallationImportScopedIdempotencyKey(InstallationImportCandidate candidate)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.IdempotencyKey)))
            .ToLowerInvariant();
        return $"bulk:{candidate.BranchId}:{digest}";
    }

    private static async Task<bool> LockInstallationImportResourcesAsync(
        Database db, long companyId, IReadOnlyList<InstallationImportCandidate> candidates, CancellationToken ct)
    {
        var branchIds = candidates.Where(row => row.BranchId.HasValue).Select(row => row.BranchId!.Value)
            .Distinct().OrderBy(id => id).ToArray();
        var devices = candidates
            .Where(row => row.DeviceId.HasValue && row.BranchId.HasValue)
            .Select(row => new { Id = row.DeviceId!.Value, row.Serial, BranchId = row.BranchId!.Value })
            .Distinct().OrderBy(row => row.Id).ThenBy(row => row.Serial, StringComparer.Ordinal).ToArray();
        var vehicles = candidates
            .Where(row => row.VehicleId.HasValue && row.BranchId.HasValue)
            .Select(row => new { Id = row.VehicleId!.Value, row.VehicleCode, BranchId = row.BranchId!.Value })
            .Distinct().OrderBy(row => row.Id).ThenBy(row => row.VehicleCode, StringComparer.Ordinal).ToArray();
        if (branchIds.Length > 0)
        {
            var lockedBranches = await db.QueryAsync(
                @"SELECT b.id,b.status,b.deleted_at FROM branches b
                   WHERE b.company_id=@cid AND b.id=ANY(@ids)
                   ORDER BY b.id FOR UPDATE",
                command =>
                {
                    command.Parameters.AddWithValue("@cid", companyId);
                    command.Parameters.AddWithValue("@ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, branchIds);
                }, ct);
            if (lockedBranches.Count != branchIds.Length || lockedBranches.Any(row =>
                    row["deletedAt"] is not null and not DBNull ||
                    !string.Equals(row["status"]?.ToString(), "Active", StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        if (devices.Length > 0)
        {
            var lockedDevices = await db.QueryAsync(
                @"SELECT d.id,UPPER(BTRIM(d.device_serial)) device_key,d.branch_id,d.deleted_at,
                         d.device_state,d.status device_status,d.revoked_at
                    FROM eld_devices d
                   WHERE d.company_id=@cid AND d.id=ANY(@ids)
                   ORDER BY d.id FOR UPDATE",
                command =>
                {
                    command.Parameters.AddWithValue("@cid", companyId);
                    command.Parameters.AddWithValue("@ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, devices.Select(row => row.Id).ToArray());
                }, ct);
            if (lockedDevices.Count != devices.Length) return false;
            var lockedById = lockedDevices.ToDictionary(row => Convert.ToInt64(row["id"], CultureInfo.InvariantCulture));
            if (devices.Any(expected => !lockedById.TryGetValue(expected.Id, out var locked) ||
                    !string.Equals(locked["deviceKey"]?.ToString(), expected.Serial, StringComparison.Ordinal) ||
                    locked["branchId"] is null or DBNull ||
                    Convert.ToInt64(locked["branchId"], CultureInfo.InvariantCulture) != expected.BranchId ||
                    locked["deletedAt"] is not null and not DBNull || !IsDeviceInstallEligible(locked)))
                return false;
        }
        if (vehicles.Length > 0)
        {
            var lockedVehicles = await db.QueryAsync(
                @"SELECT v.id,UPPER(BTRIM(v.vehicle_code)) vehicle_key,v.branch_id,v.deleted_at
                    FROM vehicles v
                   WHERE v.company_id=@cid AND v.id=ANY(@ids)
                   ORDER BY v.id FOR UPDATE",
                command =>
                {
                    command.Parameters.AddWithValue("@cid", companyId);
                    command.Parameters.AddWithValue("@ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, vehicles.Select(row => row.Id).ToArray());
                }, ct);
            if (lockedVehicles.Count != vehicles.Length) return false;
            var lockedById = lockedVehicles.ToDictionary(row => Convert.ToInt64(row["id"], CultureInfo.InvariantCulture));
            if (vehicles.Any(expected => !lockedById.TryGetValue(expected.Id, out var locked) ||
                    !string.Equals(locked["vehicleKey"]?.ToString(), expected.VehicleCode, StringComparison.Ordinal) ||
                    locked["branchId"] is null or DBNull ||
                    Convert.ToInt64(locked["branchId"], CultureInfo.InvariantCulture) != expected.BranchId ||
                    locked["deletedAt"] is not null and not DBNull))
                return false;
        }
        return true;
    }

    private static async Task<bool> MarkInstallationImportDevicesInstalledAsync(
        Database db, long companyId, IReadOnlyList<InstallationImportCandidate> candidates, CancellationToken ct)
    {
        var deviceIds = candidates.Where(row => !row.Replay).Select(row => row.DeviceId!.Value)
            .Distinct().OrderBy(id => id).ToArray();
        if (deviceIds.Length == 0) return true;
        var updated = await db.QueryAsync(
            @"WITH eligible AS MATERIALIZED (
                  SELECT id FROM eld_devices
                   WHERE company_id=@cid AND id=ANY(@ids) AND deleted_at IS NULL AND revoked_at IS NULL
                     AND LOWER(COALESCE(status,'')) NOT IN ('revoked','suspended','retired','decommissioned')
                     AND LOWER(COALESCE(device_state,'')) NOT IN
                         ('suspended','quarantined','lost','decommissioning','decommissioned','retired')
              ), changed AS (
                  UPDATE eld_devices d SET device_state='Installed',updated_at=NOW()
                    FROM eligible e
                   WHERE d.company_id=@cid AND d.id=e.id
                     AND (SELECT COUNT(*) FROM eligible)=@expected
                  RETURNING d.id
              ) SELECT id FROM changed ORDER BY id",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, deviceIds);
                command.Parameters.AddWithValue("@expected", deviceIds.Length);
            }, ct);
        return updated.Count == deviceIds.Length;
    }

    private static async Task<IReadOnlyDictionary<int, long>> PersistInstallationImportCandidatesAsync(
        Database db, HttpContext http, long companyId, long actorId,
        IReadOnlyList<InstallationImportCandidate> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0) return new Dictionary<int, long>();
        var payload = JsonSerializer.Serialize(candidates.Select(candidate => new
        {
            row_number = candidate.RowNumber,
            scoped_key = InstallationImportScopedIdempotencyKey(candidate),
            request_hash = candidate.RequestHash,
            branch_id = candidate.BranchId!.Value,
            device_id = candidate.DeviceId!.Value,
            vehicle_id = candidate.VehicleId!.Value,
            device_state = candidate.DeviceState,
            role = candidate.Role,
            is_primary = candidate.IsPrimary,
            effective_from = candidate.EffectiveFrom!.Value,
            location = candidate.Location,
            odometer = candidate.Odometer,
            method = candidate.Method,
            reason = candidate.Reason
        }));
        var persisted = await db.QueryAsync(
            @"WITH input AS MATERIALIZED (
                  SELECT * FROM jsonb_to_recordset(@rows::jsonb) AS row(
                    row_number INT,scoped_key TEXT,request_hash TEXT,branch_id BIGINT,device_id BIGINT,
                    vehicle_id BIGINT,device_state TEXT,role TEXT,is_primary BOOLEAN,effective_from TIMESTAMPTZ,
                    location TEXT,odometer NUMERIC,method TEXT,reason TEXT)
              ), inserted_keys AS (
                  INSERT INTO idempotency_keys
                    (tenant_id,operation,idempotency_key,request_hash,status,expires_at,created_at)
                  SELECT @cid,@operation,scoped_key,request_hash,'processing',NOW()+INTERVAL '24 hours',NOW()
                    FROM input ORDER BY row_number
                  RETURNING idempotency_key,request_hash
              ), installations AS (
                  INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,installer_user_id,installed_by,status,
                     device_role,is_primary,effective_from,installed_at,installation_location,
                     odometer_at_installation,commissioning_method,assignment_reason,source,
                     correlation_id,idempotency_key,created_at)
                  SELECT @cid,input.branch_id,input.device_id,input.vehicle_id,@actor,@actor,'Installed',
                         input.role,input.is_primary,input.effective_from,input.effective_from,input.location,
                         input.odometer,input.method,input.reason,'operator',@correlation,input.scoped_key,NOW()
                    FROM input
                    JOIN inserted_keys key ON key.idempotency_key=input.scoped_key
                                         AND key.request_hash=input.request_hash
                   ORDER BY input.row_number
                  RETURNING id,idempotency_key
              )
              SELECT input.row_number,installations.id installation_id
                FROM input JOIN installations ON installations.idempotency_key=input.scoped_key
               ORDER BY input.row_number",
            command =>
            {
                command.Parameters.AddWithValue("@rows", NpgsqlDbType.Jsonb, payload);
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@operation", InstallationImportIdempotencyOperation);
                command.Parameters.AddWithValue("@actor", NpgsqlDbType.Bigint, actorId > 0 ? actorId : DBNull.Value);
                command.Parameters.AddWithValue("@correlation", http.TraceIdentifier);
            }, ct);
        if (persisted.Count != candidates.Count)
            throw new InstallationImportPersistenceException(
                "Bulk installation persistence returned an incomplete installation set.");

        var persistedPayload = JsonSerializer.Serialize(persisted.Select(row => new
        {
            row_number = Convert.ToInt32(row["rowNumber"], CultureInfo.InvariantCulture),
            installation_id = Convert.ToInt64(row["installationId"], CultureInfo.InvariantCulture)
        }));
        var finalized = await db.QuerySingleAsync(
            @"WITH input AS MATERIALIZED (
                  SELECT * FROM jsonb_to_recordset(@rows::jsonb) AS row(
                    row_number INT,scoped_key TEXT,request_hash TEXT,branch_id BIGINT,device_id BIGINT,
                    vehicle_id BIGINT,device_state TEXT,role TEXT,is_primary BOOLEAN,effective_from TIMESTAMPTZ,
                    location TEXT,odometer NUMERIC,method TEXT,reason TEXT)
              ), persisted AS MATERIALIZED (
                  SELECT * FROM jsonb_to_recordset(@persisted::jsonb)
                    AS row(row_number INT,installation_id BIGINT)
              ), completed_keys AS (
                  UPDATE idempotency_keys key
                     SET status='completed',response_reference=persisted.installation_id::TEXT
                    FROM input JOIN persisted USING (row_number)
                   WHERE key.tenant_id=@cid AND key.operation=@operation
                     AND key.idempotency_key=input.scoped_key AND key.request_hash=input.request_hash
                  RETURNING key.idempotency_key
              ), transitions AS (
                  INSERT INTO device_state_transitions
                    (company_id,branch_id,device_id,from_state,to_state,reason_code,reason,
                     actor_user_id,correlation_id,occurred_at)
                  SELECT @cid,input.branch_id,input.device_id,input.device_state,'Installed','installation_created',
                         input.reason,@actor,@transitionCorrelation,NOW()
                    FROM input JOIN persisted USING (row_number)
                   ORDER BY input.row_number
                  RETURNING device_id
              )
              SELECT (SELECT COUNT(*) FROM completed_keys) completed_count,
                     (SELECT COUNT(*) FROM transitions) transition_count",
            command =>
            {
                command.Parameters.AddWithValue("@rows", NpgsqlDbType.Jsonb, payload);
                command.Parameters.AddWithValue("@persisted", NpgsqlDbType.Jsonb, persistedPayload);
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@operation", InstallationImportIdempotencyOperation);
                command.Parameters.AddWithValue("@actor", NpgsqlDbType.Bigint, actorId > 0 ? actorId : DBNull.Value);
                command.Parameters.AddWithValue("@transitionCorrelation", CorrelationUuid(http.TraceIdentifier));
            }, ct);
        var completedCount = Convert.ToInt32(finalized?["completedCount"] ?? 0, CultureInfo.InvariantCulture);
        var transitionCount = Convert.ToInt32(finalized?["transitionCount"] ?? 0, CultureInfo.InvariantCulture);
        if (completedCount != candidates.Count || transitionCount != candidates.Count)
            throw new InstallationImportPersistenceException(
                "Bulk installation persistence returned an incomplete ledger or transition set.");

        return persisted.ToDictionary(
            row => Convert.ToInt32(row["rowNumber"], CultureInfo.InvariantCulture),
            row => Convert.ToInt64(row["installationId"], CultureInfo.InvariantCulture));
    }

    private static bool IsInstallationImportPersistenceConflict(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.UniqueViolation or
            PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation or
            PostgresErrorCodes.DeadlockDetected;

    private static async Task RollbackInstallationImportSavepointAsync(Database db, CancellationToken ct)
    {
        await db.ExecuteAsync("ROLLBACK TO SAVEPOINT installation_import_mutation", ct: ct);
        await db.ExecuteAsync("RELEASE SAVEPOINT installation_import_mutation", ct: ct);
    }

    private static async Task<List<InstallationImportCandidate>> ValidateInstallationImportAsync(
        HttpContext http, IReadOnlyList<Dictionary<string, object?>> rows, Database db, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var callerBranchId = GetBranchId(http);
        var callerBranchActive = true;
        var ambiguousBranchCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, long> branches;
        if (callerBranchId is { } authorizedBranchId)
        {
            var authorized = await db.QueryAsync(
                @"SELECT id,lower(btrim(branch_code)) normalized_code FROM branches
                   WHERE company_id=@cid AND id=@branch AND deleted_at IS NULL AND status='Active'",
                command =>
                {
                    command.Parameters.AddWithValue("@cid", companyId);
                    command.Parameters.AddWithValue("@branch", authorizedBranchId);
                }, ct);
            callerBranchActive = authorized.Count == 1;
            branches = authorized.ToDictionary(
                row => row["normalizedCode"]?.ToString() ?? "",
                row => Convert.ToInt64(row["id"], CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var submittedCodes = rows.Select(row => ImportStr(row, "branchCode"))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var matchingBranches = submittedCodes.Length == 0
                ? new List<Dictionary<string, object?>>()
                : await db.QueryAsync(
                    @"SELECT id,lower(btrim(branch_code)) normalized_code FROM branches
                       WHERE company_id=@cid AND deleted_at IS NULL AND status='Active'
                         AND lower(btrim(branch_code))=ANY(@codes)",
                    command =>
                    {
                        command.Parameters.AddWithValue("@cid", companyId);
                        command.Parameters.AddWithValue("@codes", NpgsqlDbType.Array | NpgsqlDbType.Text, submittedCodes);
                    }, ct);
            var branchGroups = matchingBranches.GroupBy(
                row => row["normalizedCode"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var ambiguous in branchGroups.Where(group => group.Count() > 1))
                ambiguousBranchCodes.Add(ambiguous.Key);
            branches = branchGroups.Where(group => group.Count() == 1).ToDictionary(
                group => group.Key,
                group => Convert.ToInt64(group.Single()["id"], CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);
        }
        var candidates = new List<InstallationImportCandidate>(rows.Count);
        var fileDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileKeys = new HashSet<string>(StringComparer.Ordinal);
        var filePrimarySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var errors = new List<string>();
            var serial = ImportStr(row, "deviceSerial")?.ToUpperInvariant() ?? "";
            var vehicleCode = ImportStr(row, "vehicleCode")?.ToUpperInvariant() ?? "";
            var roleRaw = ImportStr(row, "deviceRole") ?? "";
            var role = InstallationRoles.Contains(roleRaw) ? NormalizeInstallationRole(roleRaw) : "";
            var primaryRaw = ImportStr(row, "isPrimary") ?? "";
            var isPrimary = bool.TryParse(primaryRaw, out var parsedPrimary) && parsedPrimary;
            var effectiveRaw = ImportStr(row, "effectiveFrom");
            var hasExplicitTimezone = effectiveRaw is not null &&
                System.Text.RegularExpressions.Regex.IsMatch(effectiveRaw, "(?:[zZ]|[+-]\\d{2}:\\d{2})$");
            var parsedEffective = default(DateTimeOffset);
            var hasEffective = hasExplicitTimezone && DateTimeOffset.TryParse(effectiveRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out parsedEffective);
            var effective = hasEffective ? parsedEffective.ToUniversalTime() : (DateTimeOffset?)null;
            var odometerRaw = ImportStr(row, "odometerAtInstallation");
            var hasOdometer = odometerRaw is null || decimal.TryParse(odometerRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
            decimal? odometer = odometerRaw is not null && decimal.TryParse(odometerRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedOdometer)
                ? parsedOdometer : null;
            var location = ImportStr(row, "installationLocation");
            var method = ImportStr(row, "commissioningMethod");
            var reason = ImportStr(row, "assignmentReason") ?? "";
            var idempotencyKey = ImportStr(row, "idempotencyKey") ?? "";
            var submittedBranchCode = ImportStr(row, "branchCode");
            var normalizedBranchCode = submittedBranchCode?.Trim().ToLowerInvariant();
            var resolvedBranch = normalizedBranchCode is not null && ambiguousBranchCodes.Contains(normalizedBranchCode)
                ? (BranchId: (long?)null, Error: "Submitted branch identity is ambiguous; resolve duplicate branch codes before import.")
                : ResolveImportBranch(submittedBranchCode, callerBranchId, branches);
            if (callerBranchId.HasValue && (!callerBranchActive || resolvedBranch.Error is not null))
                resolvedBranch = (null, "Submitted branch is outside the authorized branch.");

            if (serial.Length is < 4 or > 120 || !System.Text.RegularExpressions.Regex.IsMatch(serial, "^[A-Z0-9][A-Z0-9._:/-]*$"))
                errors.Add("deviceSerial must identify a registered device using 4-120 supported characters.");
            else if (!fileDevices.Add(serial)) errors.Add($"Duplicate device '{serial}' earlier in this file.");
            if (vehicleCode.Length is < 1 or > 80) errors.Add("vehicleCode is required and must be 80 characters or fewer.");
            if (role.Length == 0) errors.Add("deviceRole is required and must be a supported hardware role.");
            if (!bool.TryParse(primaryRaw, out _)) errors.Add("isPrimary must be true or false.");
            if (!hasEffective) errors.Add("effectiveFrom is required and must be an ISO-8601 timestamp with timezone.");
            else if (effective > DateTimeOffset.UtcNow) errors.Add("effectiveFrom cannot be in the future.");
            if (!hasOdometer || odometer is < 0 or > 9_999_999_999.99m) errors.Add("odometerAtInstallation is outside the supported range.");
            if (!ValidLength(location, 160) || !ValidLength(method, 80)) errors.Add("Installation location or commissioning method exceeds its supported length.");
            if (reason.Length is < 4 or > 500) errors.Add("assignmentReason must contain 4 to 500 characters.");
            if (idempotencyKey.Length is < 8 or > 120) errors.Add("idempotencyKey must contain 8 to 120 characters.");
            else if (!fileKeys.Add(idempotencyKey)) errors.Add($"Duplicate idempotencyKey '{idempotencyKey}' earlier in this file.");
            if (resolvedBranch.Error is not null) errors.Add(resolvedBranch.Error);
            if (isPrimary && vehicleCode.Length > 0 && role.Length > 0 &&
                !filePrimarySlots.Add($"{resolvedBranch.BranchId}:{vehicleCode}:{role}"))
                errors.Add($"Duplicate primary {role} installation for vehicle '{vehicleCode}' earlier in this file.");

            candidates.Add(new InstallationImportCandidate(index + 1, serial, serial, vehicleCode,
                resolvedBranch.BranchId, resolvedBranch.Error is null, role, isPrimary, effective, location, odometer, method, reason,
                idempotencyKey, errors));
        }

        var serials = candidates.Where(row => row.BranchScopeResolved && row.Serial.Length > 0).Select(row => row.Serial).Distinct().ToArray();
        var vehicleCodes = candidates.Where(row => row.BranchScopeResolved && row.VehicleCode.Length > 0).Select(row => row.VehicleCode).Distinct().ToArray();
        var keys = candidates.Where(row => row.BranchScopeResolved && row.BranchId.HasValue && row.IdempotencyKey.Length > 0)
            .Select(InstallationImportScopedIdempotencyKey).Distinct().ToArray();
        var installationKeys = keys.Concat(candidates
                .Where(row => row.BranchScopeResolved && row.IdempotencyKey.Length > 0)
                .Select(row => row.IdempotencyKey))
            .Distinct(StringComparer.Ordinal).ToArray();
        var devices = await db.QueryAsync(
            @"SELECT id,UPPER(BTRIM(device_serial)) device_key,branch_id,device_state,status device_status,revoked_at
                FROM eld_devices WHERE company_id=@cid AND deleted_at IS NULL
                  AND (@caller_branch::BIGINT IS NULL OR branch_id=@caller_branch)
                  AND UPPER(BTRIM(device_serial))=ANY(@serials)",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@caller_branch", (object?)callerBranchId ?? DBNull.Value);
                command.Parameters.AddWithValue("@serials", NpgsqlDbType.Array | NpgsqlDbType.Text, serials);
            }, ct);
        var vehicles = await db.QueryAsync(
            @"SELECT id,UPPER(BTRIM(vehicle_code)) vehicle_key,branch_id
                FROM vehicles WHERE company_id=@cid AND deleted_at IS NULL
                  AND (@caller_branch::BIGINT IS NULL OR branch_id=@caller_branch)
                  AND UPPER(BTRIM(vehicle_code))=ANY(@codes)",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@caller_branch", (object?)callerBranchId ?? DBNull.Value);
                command.Parameters.AddWithValue("@codes", NpgsqlDbType.Array | NpgsqlDbType.Text, vehicleCodes);
            }, ct);
        var earliestEffectiveFrom = candidates.Where(row => row.EffectiveFrom.HasValue)
            .Select(row => row.EffectiveFrom!.Value).DefaultIfEmpty(DateTimeOffset.UtcNow).Min();
        var installations = await db.QueryAsync(
            @"SELECT i.id,i.device_id,i.vehicle_id,i.device_role,i.is_primary,i.idempotency_key,
                     i.effective_from,i.effective_to,i.status,
                     UPPER(BTRIM(d.device_serial)) device_key,UPPER(BTRIM(v.vehicle_code)) vehicle_key
                FROM device_installations i
                JOIN eld_devices d ON d.id=i.device_id AND d.company_id=i.company_id
                JOIN vehicles v ON v.id=i.vehicle_id AND v.company_id=i.company_id
               WHERE i.company_id=@cid AND
                 (@caller_branch::BIGINT IS NULL OR
                    (i.branch_id=@caller_branch AND d.branch_id=@caller_branch AND v.branch_id=@caller_branch)) AND
                 ((i.status IN ('Installed','Verified','Removed') AND
                    (i.effective_to IS NULL OR i.effective_to>@earliest) AND
                    (UPPER(BTRIM(d.device_serial))=ANY(@serials) OR UPPER(BTRIM(v.vehicle_code))=ANY(@codes)))
                   OR i.idempotency_key=ANY(@keys))",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@caller_branch", (object?)callerBranchId ?? DBNull.Value);
                command.Parameters.AddWithValue("@serials", NpgsqlDbType.Array | NpgsqlDbType.Text, serials);
                command.Parameters.AddWithValue("@codes", NpgsqlDbType.Array | NpgsqlDbType.Text, vehicleCodes);
                command.Parameters.AddWithValue("@keys", NpgsqlDbType.Array | NpgsqlDbType.Text, installationKeys);
                command.Parameters.AddWithValue("@earliest", earliestEffectiveFrom);
            }, ct);
        var idempotencyEntries = await db.QueryAsync(
            @"SELECT idempotency_key,request_hash,status,response_reference
                FROM idempotency_keys
               WHERE tenant_id=@cid AND operation=@operation AND idempotency_key=ANY(@keys)",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@operation", InstallationImportIdempotencyOperation);
                command.Parameters.AddWithValue("@keys", NpgsqlDbType.Array | NpgsqlDbType.Text, keys);
            }, ct);

        var deviceMap = devices.GroupBy(row => row["deviceKey"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var vehicleMap = vehicles.GroupBy(row => row["vehicleKey"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.ToArray())
        {
            if (!candidate.BranchScopeResolved)
            {
                candidates[candidate.RowNumber - 1] = candidate with
                {
                    DeviceId = null, VehicleId = null, DeviceState = null, RequestHash = null,
                    Replay = false, IdempotencyConflict = false
                };
                continue;
            }
            deviceMap.TryGetValue(candidate.Serial, out var matchingDevices);
            vehicleMap.TryGetValue(candidate.VehicleCode, out var matchingVehicles);
            if (matchingDevices is { Length: > 1 })
                candidate.Errors.Add("Device identity is ambiguous or quarantined; resolve identity quarantine before import.");
            if (matchingVehicles is { Length: > 1 })
                candidate.Errors.Add("Vehicle identity is ambiguous; resolve duplicate vehicle codes before import.");
            if (matchingDevices is { Length: > 1 } || matchingVehicles is { Length: > 1 })
            {
                candidates[candidate.RowNumber - 1] = candidate with
                {
                    DeviceId = null, VehicleId = null, DeviceState = null, RequestHash = null,
                    Replay = false, IdempotencyConflict = false
                };
                continue;
            }
            var device = matchingDevices?.SingleOrDefault();
            var vehicle = matchingVehicles?.SingleOrDefault();
            if (device is null) candidate.Errors.Add("Device is not registered in this tenant.");
            if (vehicle is null) candidate.Errors.Add("Vehicle is not active in this tenant.");
            var deviceId = device is null ? (long?)null : Convert.ToInt64(device["id"], CultureInfo.InvariantCulture);
            var vehicleId = vehicle is null ? (long?)null : Convert.ToInt64(vehicle["id"], CultureInfo.InvariantCulture);
            var deviceBranch = device?["branchId"] is null or DBNull ? (long?)null : Convert.ToInt64(device["branchId"], CultureInfo.InvariantCulture);
            var vehicleBranch = vehicle?["branchId"] is null or DBNull ? (long?)null : Convert.ToInt64(vehicle["branchId"], CultureInfo.InvariantCulture);
            if (candidate.BranchId is { } expectedBranch && device is not null && deviceBranch != expectedBranch)
                candidate.Errors.Add("Device is outside the selected or authorized branch.");
            if (candidate.BranchId is { } expectedVehicleBranch && vehicle is not null && vehicleBranch != expectedVehicleBranch)
                candidate.Errors.Add("Vehicle is outside the selected or authorized branch.");
            if (device is not null && !IsDeviceInstallEligible(device))
                candidate.Errors.Add("Device is revoked, retired, quarantined, or otherwise ineligible for installation.");

            var scopedIdempotencyKey = InstallationImportScopedIdempotencyKey(candidate);
            var byKey = installations.FirstOrDefault(row =>
                string.Equals(row["idempotencyKey"]?.ToString(), scopedIdempotencyKey, StringComparison.Ordinal) ||
                string.Equals(row["idempotencyKey"]?.ToString(), candidate.IdempotencyKey, StringComparison.Ordinal));
            var currentDevice = installations.FirstOrDefault(row => deviceId.HasValue &&
                Convert.ToInt64(row["deviceId"], CultureInfo.InvariantCulture) == deviceId.Value &&
                row["effectiveTo"] is null or DBNull &&
                (row["status"]?.ToString() is "Installed" or "Verified"));
            var ledger = idempotencyEntries.FirstOrDefault(row =>
                string.Equals(row["idempotencyKey"]?.ToString(), scopedIdempotencyKey, StringComparison.Ordinal));
            var resolvedCandidate = candidate with { DeviceId = deviceId, VehicleId = vehicleId };
            var requestHash = deviceId.HasValue && vehicleId.HasValue && candidate.BranchId.HasValue && candidate.EffectiveFrom.HasValue
                ? InstallationImportRequestHash(resolvedCandidate)
                : null;
            var idempotencyConflict = false;
            var replay = false;
            if (byKey is not null || ledger is not null)
            {
                var ledgerMatches = ledger is not null && requestHash is not null &&
                    string.Equals(ledger["requestHash"]?.ToString(), requestHash, StringComparison.Ordinal) &&
                    string.Equals(ledger["status"]?.ToString(), "completed", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(ledger["responseReference"]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var responseId) &&
                    byKey is not null && Convert.ToInt64(byKey["id"], CultureInfo.InvariantCulture) == responseId;
                replay = ledgerMatches;
                if (!ledgerMatches)
                {
                    idempotencyConflict = true;
                    candidate.Errors.Add(byKey is not null && ledger is null
                        ? "idempotencyKey belongs to a legacy installation without a verifiable request fingerprint; use a new key."
                        : "idempotencyKey was already used for a different or incomplete installation request.");
                }
            }
            if (currentDevice is not null && !replay) candidate.Errors.Add("Device already has an active installation; use the governed transfer workflow.");
            if (currentDevice is null && !replay && candidate.EffectiveFrom.HasValue && deviceId.HasValue && installations.Any(row =>
                    Convert.ToInt64(row["deviceId"], CultureInfo.InvariantCulture) == deviceId.Value &&
                    row["effectiveTo"] is not (null or DBNull) &&
                    (row["status"]?.ToString() is "Installed" or "Verified" or "Removed") &&
                    new DateTimeOffset(Convert.ToDateTime(row["effectiveTo"], CultureInfo.InvariantCulture).ToUniversalTime()) > candidate.EffectiveFrom.Value))
                candidate.Errors.Add("effectiveFrom overlaps closed installation history for this device; choose a timestamp at or after the prior installation ended.");
            var currentPrimary = candidate.IsPrimary && vehicleId.HasValue && installations.Any(row =>
                    row["effectiveTo"] is null or DBNull && (row["status"]?.ToString() is "Installed" or "Verified") &&
                    Convert.ToInt64(row["vehicleId"], CultureInfo.InvariantCulture) == vehicleId.Value &&
                    Convert.ToBoolean(row["isPrimary"], CultureInfo.InvariantCulture) &&
                    string.Equals(row["deviceRole"]?.ToString(), candidate.Role, StringComparison.Ordinal) &&
                    (!deviceId.HasValue || Convert.ToInt64(row["deviceId"], CultureInfo.InvariantCulture) != deviceId.Value));
            if (currentPrimary)
                candidate.Errors.Add($"Vehicle already has an active primary {candidate.Role} installation.");
            if (!currentPrimary && !replay && candidate.IsPrimary && candidate.EffectiveFrom.HasValue && vehicleId.HasValue && installations.Any(row =>
                    row["effectiveTo"] is not (null or DBNull) &&
                    (row["status"]?.ToString() is "Installed" or "Verified" or "Removed") &&
                    Convert.ToInt64(row["vehicleId"], CultureInfo.InvariantCulture) == vehicleId.Value &&
                    Convert.ToBoolean(row["isPrimary"], CultureInfo.InvariantCulture) &&
                    string.Equals(row["deviceRole"]?.ToString(), candidate.Role, StringComparison.Ordinal) &&
                    new DateTimeOffset(Convert.ToDateTime(row["effectiveTo"], CultureInfo.InvariantCulture).ToUniversalTime()) > candidate.EffectiveFrom.Value))
                candidate.Errors.Add($"effectiveFrom overlaps closed primary {candidate.Role} assignment history for this vehicle; choose a timestamp at or after the prior assignment ended.");
            candidates[candidate.RowNumber - 1] = candidate with
            {
                DeviceId = deviceId,
                VehicleId = vehicleId,
                DeviceState = device?["deviceState"]?.ToString(),
                RequestHash = requestHash,
                Replay = replay && candidate.Errors.Count == 0,
                IdempotencyConflict = idempotencyConflict
            };
        }
        return candidates;
    }

    private static object InstallationImportPreviewResult(IReadOnlyList<InstallationImportCandidate> rows) => new
    {
        total = rows.Count,
        creates = rows.Count(row => row.Errors.Count == 0 && !row.Replay),
        updates = 0,
        skipped = rows.Count(row => row.Replay),
        invalid = rows.Count(row => row.Errors.Count > 0),
        rows = rows.Select(row => new
        {
            rowNumber = row.RowNumber,
            key = $"{row.Serial} → {row.VehicleCode}",
            action = row.Errors.Count > 0 ? "error" : row.Replay ? "skip" : "create",
            errors = row.Errors,
            message = row.Replay ? "Installation already recorded; replay will not create history." : null
        })
    };

    private static async Task<IResult> DeviceInstallationsImportPreview(
        HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "telemetry.devices.manage") is { } denied) return denied;
        if (InstallationImportExceedsLimit(body)) return Results.BadRequest(ApiResponse<object>.Fail($"Import is limited to {ImportMaxRows} rows"));
        var rows = ImportRows(body);
        if (rows.Count == 0) return Results.BadRequest(ApiResponse<object>.Fail("No installation rows to import."));
        var candidates = await ValidateInstallationImportAsync(http, rows, db, ct);
        return Results.Ok(ApiResponse<object>.Ok(InstallationImportPreviewResult(candidates)));
    }

    private static async Task<IResult> DeviceInstallationsImportCommit(
        HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "telemetry.devices.manage") is { } denied) return denied;
        if (InstallationImportExceedsLimit(body)) return Results.BadRequest(ApiResponse<object>.Fail($"Import is limited to {ImportMaxRows} rows"));
        var rows = ImportRows(body);
        if (rows.Count == 0) return Results.BadRequest(ApiResponse<object>.Fail("No installation rows to import."));
        var companyId = GetCompanyId(http);
        var actorId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
            {
                var advisoryScope = GetBranchId(http)?.ToString(CultureInfo.InvariantCulture) ?? "tenant";
                var lockKeys = rows.SelectMany(row => new[]
                    {
                        $"device-install:{companyId}:{advisoryScope}:device:{ImportStr(row, "deviceSerial")?.ToUpperInvariant()}",
                        $"device-install:{companyId}:{advisoryScope}:vehicle:{ImportStr(row, "vehicleCode")?.ToUpperInvariant()}",
                        $"device-install:{companyId}:{advisoryScope}:request:{ImportStr(row, "idempotencyKey")}"
                    })
                    .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                await db.ExecuteAsync(
                    @"SELECT pg_advisory_xact_lock(hashtextextended(identity,0))
                        FROM unnest(@identities::TEXT[]) identity ORDER BY identity",
                    command => command.Parameters.AddWithValue("@identities", NpgsqlDbType.Array | NpgsqlDbType.Text, lockKeys), ct);
                var preLockCandidates = await ValidateInstallationImportAsync(http, rows, db, ct);
                await LockInstallationResourceIdentitiesAsync(db, companyId,
                    preLockCandidates.Where(row => row.DeviceId.HasValue).Select(row => row.DeviceId!.Value),
                    preLockCandidates.Where(row => row.VehicleId.HasValue).Select(row => row.VehicleId!.Value), ct);
                if (!await LockInstallationImportResourcesAsync(db, companyId, preLockCandidates, ct))
                    return Results.Conflict(ApiResponse<object>.Fail(
                        "A device, vehicle, or branch identity changed during import. No rows changed; refresh and retry."));
                var candidates = await ValidateInstallationImportAsync(http, rows, db, ct);
                var resourceIdentityChanged = candidates.Any(row =>
                {
                    var before = preLockCandidates[row.RowNumber - 1];
                    return before.DeviceId != row.DeviceId || before.VehicleId != row.VehicleId ||
                           before.BranchId != row.BranchId ||
                           !string.Equals(before.Serial, row.Serial, StringComparison.Ordinal) ||
                           !string.Equals(before.VehicleCode, row.VehicleCode, StringComparison.Ordinal);
                });
                if (resourceIdentityChanged)
                    return Results.Conflict(ApiResponse<object>.Fail(
                        "A device, vehicle, or branch identity changed during import. No rows changed; refresh and retry."));
                var concurrentResourceConflict = candidates.Any(row =>
                    preLockCandidates[row.RowNumber - 1].Errors.Count == 0 && row.Errors.Count > 0);
                var invalid = candidates.Where(row => row.Errors.Count > 0).ToList();
                if (invalid.Count > 0)
                {
                    var first = invalid.FirstOrDefault(row => row.IdempotencyConflict) ?? invalid[0];
                    var failure = ApiResponse<object>.Fail(
                        $"Row {first.RowNumber} ({first.Key}): {string.Join("; ", first.Errors)} No rows changed.");
                    return concurrentResourceConflict || invalid.Any(row => row.IdempotencyConflict)
                        ? Results.Conflict(failure)
                        : Results.BadRequest(failure);
                }
                await db.ExecuteAsync("SAVEPOINT installation_import_mutation", ct: ct);
                try
                {
                    if (!await MarkInstallationImportDevicesInstalledAsync(db, companyId, candidates, ct))
                    {
                        await db.ExecuteAsync("RELEASE SAVEPOINT installation_import_mutation", ct: ct);
                        return Results.Conflict(ApiResponse<object>.Fail(
                            "A device lifecycle state changed during import. No rows changed; refresh and retry."));
                    }

                    var createdCandidates = candidates.Where(candidate => !candidate.Replay).ToArray();
                    var persistedByRow = await PersistInstallationImportCandidatesAsync(
                        db, http, companyId, actorId, createdCandidates, ct);
                    var created = 0;
                    var skipped = new List<object>();
                    var rowResults = new List<object>();
                    var createdAudits = new List<AuditService.BatchEntry>(createdCandidates.Length);
                    foreach (var candidate in candidates)
                    {
                        if (candidate.Replay)
                        {
                            skipped.Add(new
                            {
                                rowNumber = candidate.RowNumber,
                                key = candidate.Key,
                                errors = new[] { "Installation already recorded; no duplicate history created." }
                            });
                            rowResults.Add(new { rowNumber = candidate.RowNumber, key = candidate.Key, action = "skip" });
                            continue;
                        }
                        var installationId = persistedByRow[candidate.RowNumber];
                        createdAudits.Add(new AuditService.BatchEntry(installationId,
                            JsonSerializer.Serialize(new { deviceId = candidate.DeviceId, vehicleId = candidate.VehicleId, effectiveFrom = candidate.EffectiveFrom, source = "bulk-csv" })));
                        created++;
                        rowResults.Add(new { rowNumber = candidate.RowNumber, key = candidate.Key, action = "create", installationId });
                    }
                    await audit.LogBatchAsync(http, "device.installation.created", "DeviceInstallation", createdAudits, ct);
                    await audit.LogAsync(http, "device.installations.imported", "DeviceInstallation", null,
                        JsonSerializer.Serialize(new { created, skipped = skipped.Count, total = candidates.Count }), ct);
                    await db.ExecuteAsync("RELEASE SAVEPOINT installation_import_mutation", ct: ct);
                    return Results.Ok(ApiResponse<object>.Ok(new { created, updated = 0, skipped, total = candidates.Count, rows = rowResults }));
                }
                catch (PostgresException ex) when (IsInstallationImportPersistenceConflict(ex))
                {
                    await RollbackInstallationImportSavepointAsync(db, ct);
                    return Results.Conflict(ApiResponse<object>.Fail(
                        "A device, vehicle, or primary role changed after preview. No rows changed; refresh the CSV and retry."));
                }
                catch (InstallationImportPersistenceException)
                {
                    await RollbackInstallationImportSavepointAsync(db, ct);
                    return Results.Conflict(ApiResponse<object>.Fail(
                        "The complete installation batch could not be persisted. No rows changed; refresh the CSV and retry."));
                }
            }, ct);
    }
}
