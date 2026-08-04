using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Pilot-hardening handlers for DVIR and HOS/ELD. These intentionally live outside
// EndpointMappings.cs so the safety work remains reviewable while other modules are
// being completed in parallel.
public static partial class EndpointMappings
{
    private const string HosDriverAttestation = "I certify that this daily HOS record is true and correct.";
    private const string DvirDriverAttestation = "I certify that this DVIR is true and correct and that I completed this inspection.";
    private const string DvirRepairAcknowledgment = "I acknowledge that I reviewed the certified repairs for this DVIR.";

    private static long PilotUserId(HttpContext http)
        => Convert.ToInt64(http.Items[AuthUserIdItemKey], CultureInfo.InvariantCulture);

    private static string? PilotText(Dictionary<string, object?> body, string key, int maxLength = 500)
    {
        var value = Get(body, key);
        if (value is null or DBNull) return null;
        var text = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static bool PilotPositiveLong(object? value, out long parsed)
    {
        parsed = 0;
        return value is not null and not DBNull
            && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
            && parsed > 0;
    }

    private static void BindPilotScope(NpgsqlCommand command, HttpContext http)
    {
        command.Parameters.AddWithValue("@cid", GetCompanyId(http));
        command.Parameters.AddWithValue("@branchId", (object?)GetBranchId(http) ?? DBNull.Value);
    }

    private static string PilotBranchClause(string alias)
        => $" AND (@branchId::bigint IS NULL OR {alias}.branch_id=@branchId)";

    private static string PilotSha256(object value)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();

    private static DateTimeOffset PilotTimestamp(object value) => value switch
    {
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException("Expected a persisted timestamp")
    };

    private static long? PilotRowVersion(HttpContext http, Dictionary<string, object?>? body = null)
    {
        if (body is not null && PilotPositiveLong(Get(body, "rowVersion"), out var bodyVersion)) return bodyVersion;
        var raw = http.Request.Query["rowVersion"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) raw = http.Request.Headers.IfMatch.FirstOrDefault()?.Trim('"');
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) && version > 0 ? version : null;
    }

    private static string DvirCreateHash(Dictionary<string, object?> body, long driverId, long vehicleId,
        string inspectionType, string? requestedReportNumber)
        => PilotSha256(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["countryCode"] = PilotText(body, "countryCode", 12) ?? "US",
            ["driverId"] = driverId,
            ["inspectionType"] = inspectionType,
            ["notes"] = PilotText(body, "notes", 4000),
            ["recommendedAction"] = PilotText(body, "recommendedAction", 240),
            ["reportNumber"] = requestedReportNumber,
            ["riskScore"] = decimal.TryParse(Get(body, "riskScore")?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var risk) ? Math.Clamp(risk, 0, 100) : 0m,
            ["vehicleId"] = vehicleId
        });

    // Server-side module-entitlement gate for the paid DVIR / HOS / ELD compliance surface.
    // RBAC alone let a package_allowlist tenant WITHOUT the fleet.compliance add-on read and
    // exercise these endpoints. Mirrors FleetTmsEndpoints.RequireModule; legacy_allow tenants
    // (the default) are unaffected — CheckModuleAsync returns allowed unless the module is
    // explicitly disabled for the tenant.
    private static async Task<IResult?> RequireComplianceModule(HttpContext http, Database db, CancellationToken ct)
    {
        var decision = await new EntitlementService(db)
            .CheckModuleAsync(GetCompanyId(http), RevenueSchemaService.Modules.Compliance, ct);
        return decision.Allowed
            ? null
            : Results.Json(ApiResponse<object>.Fail("Feature not entitled", decision.Reason ?? RevenueSchemaService.Modules.Compliance),
                statusCode: StatusCodes.Status403Forbidden);
    }

    // ── DVIR reads ──────────────────────────────────────────────────────────

    private static async Task<IResult> DriverDvirReportsPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "driver:self") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        return await OkRows(db,
            @"SELECT dr.id,dr.report_number,dr.inspection_type,dr.inspection_status,dr.defects_found,
                     dr.safe_to_operate,dr.driver_signature_status,dr.mechanic_review_status,
                     dr.repair_certification_status,dr.driver_repair_acknowledged_at,dr.row_version,
                     dr.signature_attestation_text,dr.signature_hash,dr.signed_at,
                     dr.submitted_at,v.vehicle_code
              FROM dvir_reports dr
              JOIN drivers d ON d.id=dr.driver_id AND d.company_id=dr.company_id AND d.user_id=@uid
              JOIN vehicles v ON v.id=dr.vehicle_id AND v.company_id=dr.company_id
              WHERE dr.company_id=@cid AND dr.deleted_at IS NULL
              ORDER BY dr.submitted_at DESC,dr.id DESC LIMIT 30",
            c => { c.Parameters.AddWithValue("@uid", PilotUserId(http)); c.Parameters.AddWithValue("@cid", GetCompanyId(http)); }, ct: ct);
    }

    private static async Task<IResult> DvirReportsPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;

        return await OkRows(db,
            DvirBaseSql + @"
              WHERE dr.company_id=@cid AND dr.deleted_at IS NULL" + PilotBranchClause("dr") + @"
              ORDER BY dr.submitted_at DESC, dr.id DESC",
            c => BindPilotScope(c, http), ct: ct);
    }

    private static async Task<IResult> DvirSummaryPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) inspections_total,
                     COUNT(*) FILTER (WHERE submitted_at::date=CURRENT_DATE) inspections_today,
                     COUNT(*) FILTER (WHERE LOWER(inspection_type) IN ('pre-trip','pre_trip')) pre_trip_completed,
                     COUNT(*) FILTER (WHERE LOWER(inspection_type) IN ('post-trip','post_trip')) post_trip_completed,
                     COUNT(*) FILTER (WHERE defects_found>0) reports_with_defects,
                     COUNT(*) FILTER (WHERE safe_to_operate=FALSE) unsafe_vehicles,
                     COUNT(*) FILTER (WHERE mechanic_review_status='Pending' AND defects_found>0) pending_mechanic_review,
                     COUNT(*) FILTER (WHERE repair_certification_status='Pending' AND defects_found>0) pending_repair_certification,
                     COUNT(*) FILTER (WHERE driver_signature_status<>'Signed') missing_driver_signatures,
                     ROUND(100.0 * COUNT(*) FILTER (WHERE driver_signature_status='Signed') / NULLIF(COUNT(*),0),1) signature_completion_pct,
                     (SELECT COUNT(*) FROM dvir_defects dd
                       WHERE dd.company_id=@cid
                         AND (@branchId::bigint IS NULL OR dd.branch_id=@branchId)
                         AND dd.out_of_service=TRUE
                         AND LOWER(COALESCE(dd.status,'open')) NOT IN ('resolved','rejected')) open_out_of_service_defects
              FROM dvir_reports
              WHERE company_id=@cid AND deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR branch_id=@branchId)",
            c => BindPilotScope(c, http), ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> DvirDetailPilot(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var rows = await db.QueryAsync(
            DvirBaseSql + " WHERE dr.id=@id AND dr.company_id=@cid AND dr.deleted_at IS NULL" + PilotBranchClause("dr"),
            c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
        var record = rows.FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("DVIR report not found"));

        var defects = await db.QueryAsync(
            @"SELECT * FROM dvir_defects
              WHERE dvir_report_id=@id AND company_id=@cid
                AND (@branchId::bigint IS NULL OR branch_id=@branchId)
              ORDER BY out_of_service DESC,
                       ARRAY_POSITION(ARRAY['Critical','Major','Minor','High','Medium','Low'],severity),id",
            c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
        var workOrders = await db.QueryAsync(
            @"SELECT * FROM work_orders
              WHERE dvir_report_id=@id AND company_id=@cid AND deleted_at IS NULL
              ORDER BY created_at DESC",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", GetCompanyId(http)); }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { record, defects, workOrders }));
    }

    private static async Task<IResult> DvirTimelinePilot(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var owned = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM dvir_reports WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR branch_id=@branchId)",
            c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
        if (owned == 0) return Results.NotFound(ApiResponse<object>.Fail("DVIR report not found"));
        var rows = await db.QueryAsync(
            @"SELECT dd.id,'Defect' event_title,dd.defect_description event_description,
                     dd.created_at occurred_at,dd.severity
              FROM dvir_defects dd
              WHERE dd.dvir_report_id=@id AND dd.company_id=@cid
              UNION ALL
              SELECT al.id,al.action_name,al.actor_name,al.created_at,'Audit'
              FROM audit_logs al
              WHERE al.company_id=@cid AND al.entity_name='DVIR' AND al.entity_id=@id
              UNION ALL
              SELECT al.id,al.action_name,al.actor_name,al.created_at,'Audit'
              FROM audit_logs al
              JOIN dvir_defects dd ON dd.id=al.entity_id AND dd.company_id=al.company_id
              WHERE al.company_id=@cid AND al.entity_name='DvirDefect' AND dd.dvir_report_id=@id
              ORDER BY occurred_at DESC LIMIT 100",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", GetCompanyId(http)); }, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> DvirTemplatesPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var companyId = GetCompanyId(http);
        var templates = await db.QueryAsync(
            "SELECT * FROM dvir_templates WHERE company_id=@cid ORDER BY template_name,id",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        var items = await db.QueryAsync(
            @"SELECT ici.* FROM inspection_checklist_items ici
              JOIN dvir_templates t ON t.id=ici.template_id
              WHERE t.company_id=@cid ORDER BY ici.template_id,ici.sort_order,ici.id",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { templates, checklistItems = items }));
    }

    // ── DVIR mutations ──────────────────────────────────────────────────────

    private static async Task<IResult> CreateDvirTemplatePilot(HttpContext http,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:update", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var name = PilotText(body, "templateName", 160);
        var type = PilotText(body, "inspectionType", 40);
        if (name is null || type is null)
            return Results.BadRequest(ApiResponse<object>.Fail("Template name and inspection type are required"));
        var id = await db.InsertAsync(
            @"INSERT INTO dvir_templates(company_id,template_name,country_code,vehicle_type,inspection_type,status)
              VALUES(@cid,@name,@country,@vehicleType,@type,'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", GetCompanyId(http));
                c.Parameters.AddWithValue("@name", name);
                c.Parameters.AddWithValue("@country", PilotText(body, "countryCode", 12) ?? "US");
                c.Parameters.AddWithValue("@vehicleType", (object?)PilotText(body, "vehicleType", 80) ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", type);
            }, ct);
        await audit.LogAsync(http, "dvir.template.created", "DVIRTemplate", id, ct: ct);
        return Results.Created($"/api/dvir/templates/{id}", ApiResponse<object>.Ok(new { id }, "DVIR template created"));
    }

    private static async Task<IResult> UpdateDvirTemplatePilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:update", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var affected = await db.ExecuteAsync(
            @"UPDATE dvir_templates SET
                template_name=COALESCE(@name,template_name),country_code=COALESCE(@country,country_code),
                vehicle_type=COALESCE(@vehicleType,vehicle_type),inspection_type=COALESCE(@type,inspection_type),
                status=COALESCE(@status,status)
              WHERE id=@id AND company_id=@cid",
            c =>
            {
                c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", GetCompanyId(http));
                c.Parameters.AddWithValue("@name", (object?)PilotText(body, "templateName", 160) ?? DBNull.Value);
                c.Parameters.AddWithValue("@country", (object?)PilotText(body, "countryCode", 12) ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleType", (object?)PilotText(body, "vehicleType", 80) ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", (object?)PilotText(body, "inspectionType", 40) ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)PilotText(body, "status", 30) ?? DBNull.Value);
            }, ct);
        if (affected == 0) return Results.NotFound(ApiResponse<object>.Fail("DVIR template not found"));
        await audit.LogAsync(http, "dvir.template.updated", "DVIRTemplate", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "DVIR template updated"));
    }

    private static async Task<IResult> CreateDvirReportPilot(HttpContext http,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:create", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        if (!PilotPositiveLong(Get(body, "driverId"), out var driverId) ||
            !PilotPositiveLong(Get(body, "vehicleId"), out var vehicleId))
            return Results.BadRequest(ApiResponse<object>.Fail("A positive driverId and vehicleId are required"));

        var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim()
            ?? PilotText(body, "idempotencyKey", 100);
        if (idempotencyKey?.Length > 100)
            return Results.BadRequest(ApiResponse<object>.Fail("Idempotency key cannot exceed 100 characters"));
        var generatedNumber = $"DVIR-{GetCompanyId(http)}-{Guid.NewGuid():N}";
        var requestedReportNumber = PilotText(body, "reportNumber", 80);
        var reportNumber = requestedReportNumber
            ?? generatedNumber[..Math.Min(80, generatedNumber.Length)];
        var inspectionType = PilotText(body, "inspectionType", 40);
        if (inspectionType is null)
            return Results.BadRequest(ApiResponse<object>.Fail("Inspection type is required"));
        if (int.TryParse(Get(body, "defectsFound")?.ToString(), out var submittedDefects) && submittedDefects > 0)
            return Results.BadRequest(ApiResponse<object>.Fail("Defects require checklist evidence; use the inspection submission workflow"));
        var requestHash = DvirCreateHash(body, driverId, vehicleId, inspectionType, requestedReportNumber);

        var companyId = GetCompanyId(http);
        try
        {
            return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
            {
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    var existing = await db.QuerySingleAsync(
                        @"SELECT id,idempotency_request_hash FROM dvir_reports WHERE company_id=@cid AND idempotency_key=@key LIMIT 1",
                        c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@key", idempotencyKey); }, ct);
                    if (existing is not null && !string.Equals(existing["idempotencyRequestHash"]?.ToString(), requestHash, StringComparison.Ordinal))
                        return Results.Conflict(ApiResponse<object>.Fail("Idempotency key was already used with a different DVIR payload"));
                    if (existing is not null)
                        return Results.Ok(ApiResponse<object>.Ok(new { id = existing["id"], replayed = true }, "DVIR report already submitted"));
                }

                var ownership = await db.QuerySingleAsync(
                    @"SELECT d.branch_id driver_branch,v.branch_id vehicle_branch
                      FROM drivers d CROSS JOIN vehicles v
                      WHERE d.id=@did AND d.company_id=@cid AND d.deleted_at IS NULL
                        AND v.id=@vid AND v.company_id=@cid AND v.deleted_at IS NULL
                        AND (@branchId::bigint IS NULL OR (d.branch_id=@branchId AND v.branch_id=@branchId))
                      FOR UPDATE OF d,v",
                    c =>
                    {
                        c.Parameters.AddWithValue("@did", driverId); c.Parameters.AddWithValue("@vid", vehicleId);
                        BindPilotScope(c, http);
                    }, ct);
                if (ownership is null)
                    return Results.NotFound(ApiResponse<object>.Fail("Driver or vehicle not found in the authorized branch"));
                var driverBranch = ownership["driverBranch"];
                var vehicleBranch = ownership["vehicleBranch"];
                if (driverBranch is not null and not DBNull && vehicleBranch is not null and not DBNull &&
                    Convert.ToInt64(driverBranch, CultureInfo.InvariantCulture) != Convert.ToInt64(vehicleBranch, CultureInfo.InvariantCulture))
                    return Results.Conflict(ApiResponse<object>.Fail("Driver and vehicle must belong to the same branch"));

                var id = await db.InsertAsync(
                    @"INSERT INTO dvir_reports
                        (company_id,branch_id,report_number,idempotency_key,idempotency_request_hash,driver_id,vehicle_id,country_code,
                         inspection_type,inspection_status,defects_found,safe_to_operate,driver_signature_status,
                         mechanic_review_status,repair_certification_status,submitted_at,risk_score,recommended_action,notes)
                      VALUES(@cid,@branchId,@number,@key,@requestHash,@did,@vid,@country,@type,'Submitted',0,TRUE,'Pending',
                             'Pending','Pending',NOW(),@risk,@action,@notes) RETURNING id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@branchId", driverBranch is null or DBNull ? vehicleBranch ?? DBNull.Value : driverBranch);
                        c.Parameters.AddWithValue("@number", reportNumber);
                        c.Parameters.AddWithValue("@key", (object?)idempotencyKey ?? DBNull.Value);
                        c.Parameters.AddWithValue("@requestHash", requestHash);
                        c.Parameters.AddWithValue("@did", driverId); c.Parameters.AddWithValue("@vid", vehicleId);
                        c.Parameters.AddWithValue("@country", PilotText(body, "countryCode", 12) ?? "US");
                        c.Parameters.AddWithValue("@type", inspectionType);
                        c.Parameters.AddWithValue("@risk", decimal.TryParse(Get(body, "riskScore")?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var risk) ? Math.Clamp(risk, 0, 100) : 0m);
                        c.Parameters.AddWithValue("@action", (object?)PilotText(body, "recommendedAction", 240) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@notes", (object?)PilotText(body, "notes", 4000) ?? DBNull.Value);
                    }, ct);
                await audit.LogAsync(http, "dvir.created", "DVIR", id, $"report:{reportNumber}", ct);
                return Results.Created($"/api/dvir/reports/{id}", ApiResponse<object>.Ok(new { id, rowVersion = 1 }, "DVIR report created"));
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = await db.QuerySingleAsync(
                    "SELECT id,idempotency_request_hash FROM dvir_reports WHERE company_id=@cid AND idempotency_key=@key LIMIT 1",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@key", idempotencyKey); }, ct);
                if (existing is not null && !string.Equals(existing["idempotencyRequestHash"]?.ToString(), requestHash, StringComparison.Ordinal))
                    return Results.Conflict(ApiResponse<object>.Fail("Idempotency key was already used with a different DVIR payload"));
                if (existing is not null)
                    return Results.Ok(ApiResponse<object>.Ok(new { id = existing["id"], replayed = true }, "DVIR report already submitted"));
            }
            return Results.Conflict(ApiResponse<object>.Fail("DVIR report number or idempotency key already exists"));
        }
    }

    private static async Task<IResult> UpdateDvirReportPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:update", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var forbidden = new[] { "driverId", "vehicleId", "defectsFound", "safeToOperate", "inspectionStatus",
            "driverSignatureStatus", "mechanicReviewStatus", "repairCertificationStatus" };
        if (forbidden.Any(k => Get(body, k) is not DBNull))
            return Results.BadRequest(ApiResponse<object>.Fail("Ownership and lifecycle fields must use their dedicated workflow actions"));
        var expectedVersion = PilotRowVersion(http, body);
        if (expectedVersion is null)
            return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required for DVIR updates"));
        var affected = await db.ExecuteAsync(
            @"UPDATE dvir_reports SET country_code=COALESCE(@country,country_code),
                    inspection_type=COALESCE(@type,inspection_type),risk_score=COALESCE(@risk,risk_score),
                    recommended_action=COALESCE(@action,recommended_action),notes=COALESCE(@notes,notes),
                    row_version=row_version+1
              WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR branch_id=@branchId)
                AND inspection_status='Draft' AND row_version=@version",
            c =>
            {
                c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http);
                c.Parameters.AddWithValue("@country", (object?)PilotText(body, "countryCode", 12) ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", (object?)PilotText(body, "inspectionType", 40) ?? DBNull.Value);
                c.Parameters.AddWithValue("@risk", decimal.TryParse(Get(body, "riskScore")?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var risk) ? Math.Clamp(risk, 0, 100) : DBNull.Value);
                c.Parameters.AddWithValue("@action", (object?)PilotText(body, "recommendedAction", 240) ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)PilotText(body, "notes", 4000) ?? DBNull.Value);
                c.Parameters.AddWithValue("@version", expectedVersion.Value);
            }, ct);
        if (affected == 0)
            return Results.Conflict(ApiResponse<object>.Fail("Only a current Draft DVIR can be edited; refresh and retry"));
        await audit.LogAsync(http, "dvir.updated", "DVIR", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "DVIR report updated"));
    }

    private static async Task<IResult> DeleteDvirReportPilot(HttpContext http, long id,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:update", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var expectedVersion = PilotRowVersion(http);
        if (expectedVersion is null) return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required for DVIR archival"));
        var affected = await db.ExecuteAsync(
            @"UPDATE dvir_reports SET deleted_at=NOW(),inspection_status='Deleted',row_version=row_version+1
              WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR branch_id=@branchId)
                AND inspection_status='Draft' AND row_version=@version",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@version", expectedVersion.Value); BindPilotScope(c, http); }, ct);
        if (affected == 0) return Results.Conflict(ApiResponse<object>.Fail("Only a current Draft DVIR can be archived; refresh and retry"));
        await audit.LogAsync(http, "dvir.deleted", "DVIR", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "DVIR report archived"));
    }

    private static async Task<IResult> DvirMechanicReviewPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:update", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var expectedVersion = PilotRowVersion(http, body);
        if (expectedVersion is null) return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required"));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var report = await db.QuerySingleAsync(
                @"SELECT mechanic_review_status,row_version FROM dvir_reports
                  WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                    AND (@branchId::bigint IS NULL OR branch_id=@branchId) FOR UPDATE",
                c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
            if (report is null) return Results.NotFound(ApiResponse<object>.Fail("DVIR report not found"));
            if (Convert.ToInt64(report["rowVersion"], CultureInfo.InvariantCulture) != expectedVersion.Value)
                return Results.Conflict(ApiResponse<object>.Fail("DVIR report changed since it was loaded; refresh and retry"));
            if (string.Equals(report["mechanicReviewStatus"]?.ToString(), "Reviewed", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(ApiResponse<object>.Ok(new { id, replayed = true }, "Mechanic review already recorded"));
            var unresolved = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM dvir_defects WHERE dvir_report_id=@id AND company_id=@cid
                    AND LOWER(COALESCE(status,'open')) NOT IN ('resolved','rejected')",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct);
            await db.ExecuteAsync(
                @"UPDATE dvir_reports SET mechanic_review_status='Reviewed',mechanic_reviewed_at=NOW(),
                    reviewed_by=@uid,reviewed_at=NOW(),inspection_status=@status,row_version=row_version+1
                  WHERE id=@id AND company_id=@cid",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); c.Parameters.AddWithValue("@status", unresolved > 0 ? "Repair Required" : "Reviewed"); }, ct);
            await db.ExecuteAsync(
                @"UPDATE dvir_defects SET reviewed_at=COALESCE(reviewed_at,NOW()),reviewed_by=COALESCE(reviewed_by,@uid),row_version=row_version+1
                  WHERE dvir_report_id=@id AND company_id=@cid",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); }, ct);
            await audit.LogAsync(http, "dvir.mechanic.reviewed", "DVIR", id, $"unresolved_defects:{unresolved}", ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, unresolvedDefects = unresolved }, "Mechanic review completed"));
        }, ct);
    }

    private static async Task<IResult> DvirCertifyRepairPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:close", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var expectedVersion = PilotRowVersion(http, body);
        if (expectedVersion is null) return Results.BadRequest(ApiResponse<object>.Fail("rowVersion is required"));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var report = await db.QuerySingleAsync(
                @"SELECT defects_found,mechanic_review_status,repair_certification_status,row_version FROM dvir_reports
                  WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                    AND (@branchId::bigint IS NULL OR branch_id=@branchId) FOR UPDATE",
                c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
            if (report is null) return Results.NotFound(ApiResponse<object>.Fail("DVIR report not found"));
            if (Convert.ToInt64(report["rowVersion"], CultureInfo.InvariantCulture) != expectedVersion.Value)
                return Results.Conflict(ApiResponse<object>.Fail("DVIR report changed since it was loaded; refresh and retry"));
            if (string.Equals(report["repairCertificationStatus"]?.ToString(), "Certified", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(ApiResponse<object>.Ok(new { id, replayed = true }, "Repair already certified"));
            if (!string.Equals(report["mechanicReviewStatus"]?.ToString(), "Reviewed", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(ApiResponse<object>.Fail("Mechanic review is required before repair certification"));
            var defectEvidence = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM dvir_defects WHERE dvir_report_id=@id AND company_id=@cid",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct);
            var unresolved = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM dvir_defects WHERE dvir_report_id=@id AND company_id=@cid
                    AND LOWER(COALESCE(status,'open')) NOT IN ('resolved','rejected')",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct);
            if (Convert.ToInt32(report["defectsFound"], CultureInfo.InvariantCulture) > 0 && defectEvidence == 0)
                return Results.Conflict(ApiResponse<object>.Fail("Repair certification requires persisted defect evidence"));
            if (unresolved > 0)
                return Results.Conflict(ApiResponse<object>.Fail("All defects must be resolved or rejected before repair certification", $"{unresolved} unresolved defect(s) remain"));
            await db.ExecuteAsync(
                @"UPDATE dvir_reports SET repair_certification_status='Certified',repair_certified_at=NOW(),
                    safe_to_operate=FALSE,inspection_status='Awaiting Driver Acknowledgment',row_version=row_version+1
                  WHERE id=@id AND company_id=@cid",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct);
            await db.ExecuteAsync(
                @"UPDATE dvir_defects SET repair_certified_at=COALESCE(repair_certified_at,NOW()),
                    repair_certified_by=COALESCE(repair_certified_by,@uid),row_version=row_version+1
                  WHERE dvir_report_id=@id AND company_id=@cid",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); }, ct);
            await audit.LogAsync(http, "dvir.repair.certified", "DVIR", id, ct: ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, rowVersion = expectedVersion.Value + 1, safeToOperate = false }, "Repair certified; driver acknowledgment is required before vehicle release"));
        }, ct);
    }

    private static async Task<IResult> DvirDriverSignPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        // A back-office user must never impersonate the driver's certification. Paper-signature
        // capture needs an evidence upload workflow; this endpoint is only for the bound driver.
        if (RequireAnyDirectPermission(http, "driver:self") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var expectedVersion = PilotRowVersion(http, body);
        var attestation = PilotText(body, "attestation", 300);
        var accepted = bool.TryParse(Get(body, "attestationAccepted")?.ToString(), out var parsed) && parsed;
        if (expectedVersion is null || !accepted || !string.Equals(attestation, DvirDriverAttestation, StringComparison.Ordinal))
            return Results.BadRequest(ApiResponse<object>.Fail("rowVersion and the exact driver DVIR attestation with explicit acceptance are required"));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var report = await db.QuerySingleAsync(
                @"SELECT dr.report_number,dr.driver_id,dr.vehicle_id,dr.country_code,dr.inspection_type,
                         dr.inspection_status,dr.defects_found,dr.safe_to_operate,dr.submitted_at,dr.notes,
                         dr.row_version,dr.driver_signature_status
                  FROM dvir_reports dr JOIN drivers d ON d.id=dr.driver_id AND d.company_id=dr.company_id
                  WHERE dr.id=@id AND dr.company_id=@cid AND dr.deleted_at IS NULL AND d.user_id=@uid
                  FOR UPDATE",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); }, ct);
            if (report is null) return Results.NotFound(ApiResponse<object>.Fail("DVIR report not found for the authenticated driver"));
            if (Convert.ToInt64(report["rowVersion"], CultureInfo.InvariantCulture) != expectedVersion.Value)
                return Results.Conflict(ApiResponse<object>.Fail("DVIR report changed since it was loaded; refresh and retry"));
            if (string.Equals(report["driverSignatureStatus"]?.ToString(), "Signed", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(ApiResponse<object>.Fail("DVIR report is already certified"));
            if (string.Equals(report["inspectionStatus"]?.ToString(), "Draft", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(ApiResponse<object>.Fail("Submit the DVIR before driver certification"));

            var checklist = await db.QueryAsync(
                @"SELECT item_category,item_name,result,severity,notes FROM dvir_inspection_results
                  WHERE company_id=@cid AND dvir_report_id=@id ORDER BY id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", id); }, ct);
            var signatureHash = PilotSha256(new
            {
                companyId, reportId = id, attestation = DvirDriverAttestation,
                reportNumber = report.GetValueOrDefault("reportNumber"), driverId = report.GetValueOrDefault("driverId"),
                vehicleId = report.GetValueOrDefault("vehicleId"), countryCode = report.GetValueOrDefault("countryCode"),
                inspectionType = report.GetValueOrDefault("inspectionType"), inspectionStatus = report.GetValueOrDefault("inspectionStatus"),
                defectsFound = report.GetValueOrDefault("defectsFound"), safeToOperate = report.GetValueOrDefault("safeToOperate"),
                submittedAt = report.GetValueOrDefault("submittedAt"), notes = report.GetValueOrDefault("notes"), checklist
            });
            await db.ExecuteAsync(
                @"UPDATE dvir_reports SET driver_signature_status='Signed',signature_attestation_text=@attestation,
                        signature_hash=@signatureHash,signed_at=NOW(),signed_by=@uid,row_version=row_version+1
                  WHERE id=@id AND company_id=@cid AND row_version=@version",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); c.Parameters.AddWithValue("@version", expectedVersion.Value); c.Parameters.AddWithValue("@attestation", DvirDriverAttestation); c.Parameters.AddWithValue("@signatureHash", signatureHash); }, ct);
            await audit.LogAsync(http, "dvir.driver.signed", "DVIR", id,
                JsonSerializer.Serialize(new { attestationHash = PilotSha256(DvirDriverAttestation), signatureHash }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, rowVersion = expectedVersion.Value + 1, signatureHash }, "Driver certification recorded"));
        }, ct);
    }

    private static async Task<IResult> DvirDriverAcknowledgeRepairPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "driver:self") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var expectedVersion = PilotRowVersion(http, body);
        var attestation = PilotText(body, "attestation", 300);
        var accepted = bool.TryParse(Get(body, "attestationAccepted")?.ToString(), out var parsed) && parsed;
        if (expectedVersion is null || !accepted || !string.Equals(attestation, DvirRepairAcknowledgment, StringComparison.Ordinal))
            return Results.BadRequest(ApiResponse<object>.Fail("rowVersion and the exact repair-review acknowledgment are required"));

        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var report = await db.QuerySingleAsync(
                @"SELECT dr.id,dr.row_version,dr.repair_certification_status,dr.driver_repair_acknowledged_at
                  FROM dvir_reports dr JOIN drivers d ON d.id=dr.driver_id AND d.company_id=dr.company_id
                  WHERE dr.id=@id AND dr.company_id=@cid AND dr.deleted_at IS NULL AND d.user_id=@uid
                  FOR UPDATE",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); }, ct);
            if (report is null) return Results.NotFound(ApiResponse<object>.Fail("DVIR report not found for the authenticated driver"));
            if (report["driverRepairAcknowledgedAt"] is not null and not DBNull)
                return Results.Ok(ApiResponse<object>.Ok(new { id, replayed = true, safeToOperate = true }, "Certified repairs already acknowledged"));
            if (Convert.ToInt64(report["rowVersion"], CultureInfo.InvariantCulture) != expectedVersion.Value)
                return Results.Conflict(ApiResponse<object>.Fail("DVIR report changed since it was loaded; refresh and retry"));
            if (!string.Equals(report["repairCertificationStatus"]?.ToString(), "Certified", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(ApiResponse<object>.Fail("Repairs must be certified before driver acknowledgment"));
            var unresolved = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM dvir_defects WHERE dvir_report_id=@id AND company_id=@cid
                    AND LOWER(COALESCE(status,'open')) NOT IN ('resolved','rejected')",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct);
            if (unresolved > 0) return Results.Conflict(ApiResponse<object>.Fail("All defects must be closed before repair acknowledgment"));

            await db.ExecuteAsync(
                @"UPDATE dvir_reports SET driver_repair_acknowledged_at=NOW(),driver_repair_acknowledged_by=@uid,
                    safe_to_operate=TRUE,inspection_status='Certified',row_version=row_version+1
                  WHERE id=@id AND company_id=@cid AND row_version=@version",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); c.Parameters.AddWithValue("@version", expectedVersion.Value); }, ct);
            await db.ExecuteAsync(
                @"UPDATE dvir_defects SET driver_acknowledged_at=COALESCE(driver_acknowledged_at,NOW()),
                    driver_acknowledged_by=COALESCE(driver_acknowledged_by,@uid),row_version=row_version+1
                  WHERE dvir_report_id=@id AND company_id=@cid",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@uid", PilotUserId(http)); }, ct);
            await MaintenanceBackgroundService.UpdateVehicleAvailabilityAsync(db, ct);
            await audit.LogAsync(http, "dvir.repairs.driver_acknowledged", "DVIR", id,
                JsonSerializer.Serialize(new { attestationHash = PilotSha256(attestation!) }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, rowVersion = expectedVersion.Value + 1, safeToOperate = true }, "Certified repairs acknowledged; vehicle availability re-evaluated"));
        }, ct);
    }

    private static async Task<IResult> DvirDefectResolvePilot(long id, HttpContext http, Dictionary<string, object?> body,
        Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "maintenance:close", "maintenance:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var expectedVersion = PilotRowVersion(http, body);
        var notes = PilotText(body, "notes", 2000);
        if (expectedVersion is null || notes is null)
            return Results.BadRequest(ApiResponse<object>.Fail("rowVersion and repair notes are required"));
        var companyId = GetCompanyId(http);
        var affected = await db.ExecuteAsync(
            @"UPDATE dvir_defects SET status='Resolved',resolved_at=NOW(),resolved_by=@uid,
                    updated_at=NOW(),row_version=row_version+1
              WHERE id=@id AND company_id=@cid
                AND (@branchId::bigint IS NULL OR branch_id=@branchId)
                AND LOWER(COALESCE(status,'open')) NOT IN ('resolved','rejected') AND row_version=@version",
            c =>
            {
                c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http);
                c.Parameters.AddWithValue("@uid", PilotUserId(http));
                c.Parameters.AddWithValue("@version", expectedVersion.Value);
            }, ct);
        if (affected == 0)
            return Results.Conflict(ApiResponse<object>.Fail("Defect changed, is already closed, or is outside the authorized branch"));
        await audit.LogAsync(http, "defect.resolved", "DvirDefect", id, notes, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = "Resolved", rowVersion = expectedVersion.Value + 1 }, "Defect resolved; repair certification and driver acknowledgment are still required for vehicle release"));
    }

    // ── HOS / ELD ───────────────────────────────────────────────────────────

    private static async Task<IResult> DriverHosLogsPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "driver:self") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        return await OkRows(db,
            @"SELECT hl.id,hl.log_date,hl.status,hl.start_time,hl.end_time,hl.duration_minutes,
                     hl.location,hl.is_certified,hl.certified_at,
                     CASE WHEN hl.end_time IS NOT NULL THEN GREATEST(0,FLOOR(EXTRACT(EPOCH FROM (hl.end_time-hl.start_time))/60))::int END calculated_duration_minutes,
                     CASE WHEN hl.end_time IS NOT NULL AND ABS(hl.duration_minutes-FLOOR(EXTRACT(EPOCH FROM (hl.end_time-hl.start_time))/60))>1 THEN TRUE ELSE FALSE END duration_mismatch
              FROM hos_logs hl JOIN drivers d ON d.id=hl.driver_id AND d.company_id=hl.company_id AND d.user_id=@uid
              WHERE hl.company_id=@cid AND hl.deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR hl.branch_id=@branchId)
              ORDER BY hl.log_date DESC,hl.start_time,hl.id LIMIT 100",
            c => { c.Parameters.AddWithValue("@uid", PilotUserId(http)); BindPilotScope(c, http); }, ct: ct);
    }

    private static async Task<IResult> HosDriversPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        return await OkRows(db,
            @"SELECT hc.*,d.full_name driver_name,d.driver_code,d.status driver_status,cp.profile_name
              FROM hos_clocks hc
              JOIN drivers d ON d.id=hc.driver_id AND d.company_id=hc.company_id AND d.deleted_at IS NULL
              LEFT JOIN compliance_profiles cp ON cp.id=hc.profile_id
              WHERE hc.company_id=@cid AND (@branchId::bigint IS NULL OR hc.branch_id=@branchId)
              ORDER BY ARRAY_POSITION(ARRAY['Violation','Warning','OK'],hc.status),hc.drive_time_remaining_minutes,hc.id",
            c => BindPilotScope(c, http), ct: ct);
    }

    private static async Task<IResult> HosLogsPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        return await OkRows(db,
            @"SELECT hl.*,d.full_name driver_name,d.driver_code,v.vehicle_code,
                     CASE WHEN hl.end_time IS NOT NULL THEN GREATEST(0,FLOOR(EXTRACT(EPOCH FROM (hl.end_time-hl.start_time))/60))::int END calculated_duration_minutes,
                     CASE WHEN hl.end_time IS NOT NULL AND ABS(hl.duration_minutes-FLOOR(EXTRACT(EPOCH FROM (hl.end_time-hl.start_time))/60))>1 THEN TRUE ELSE FALSE END duration_mismatch
              FROM hos_logs hl
              JOIN drivers d ON d.id=hl.driver_id AND d.company_id=hl.company_id AND d.deleted_at IS NULL
              LEFT JOIN vehicles v ON v.id=hl.vehicle_id AND v.company_id=hl.company_id
              WHERE hl.company_id=@cid AND hl.deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR hl.branch_id=@branchId)
              ORDER BY hl.log_date DESC,hl.start_time DESC,hl.id DESC LIMIT 200",
            c => BindPilotScope(c, http), ct: ct);
    }

    private static async Task<IResult> HosDriverLogsPilot(HttpContext http, long driverId, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        return await OkRows(db,
            @"SELECT hl.*,v.vehicle_code
              FROM hos_logs hl
              JOIN drivers d ON d.id=hl.driver_id AND d.company_id=hl.company_id AND d.deleted_at IS NULL
              LEFT JOIN vehicles v ON v.id=hl.vehicle_id AND v.company_id=hl.company_id
              WHERE hl.driver_id=@id AND hl.company_id=@cid AND hl.deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR hl.branch_id=@branchId)
              ORDER BY hl.log_date DESC,hl.start_time DESC,hl.id DESC LIMIT 200",
            c => { c.Parameters.AddWithValue("@id", driverId); BindPilotScope(c, http); }, ct: ct);
    }

    private static async Task<IResult> HosSummaryPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var clocks = await db.QueryAsync(
            @"SELECT status,COUNT(*) cnt FROM hos_clocks WHERE company_id=@cid
                AND (@branchId::bigint IS NULL OR branch_id=@branchId) GROUP BY status",
            c => BindPilotScope(c, http), ct);
        var logs = await db.QueryAsync(
            @"SELECT log_date,COUNT(*) entries,SUM(duration_minutes) total_minutes,
                     COUNT(*) FILTER (WHERE is_certified) certified_entries
              FROM hos_logs WHERE company_id=@cid AND deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR branch_id=@branchId)
              GROUP BY log_date ORDER BY log_date DESC LIMIT 14",
            c => BindPilotScope(c, http), ct);
        var violations = await db.QueryAsync(
            @"SELECT * FROM compliance_violations WHERE company_id=@cid AND category='HOS'
                AND status IN ('Open','Escalated') AND (@branchId::bigint IS NULL OR branch_id=@branchId)
              ORDER BY ARRAY_POSITION(ARRAY['Critical','High','Medium','Low'],severity),detected_at DESC LIMIT 50",
            c => BindPilotScope(c, http), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { clocks, logs, violations, dataSource = "persisted_hos_logs_and_clocks" }, "HOS summary"));
    }

    private static async Task<IResult> HosCertifyPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "driver:self") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var attestation = PilotText(body, "attestation", 300);
        var accepted = bool.TryParse(Get(body, "attestationAccepted")?.ToString(), out var parsed) && parsed;
        if (!accepted || !string.Equals(attestation, HosDriverAttestation, StringComparison.Ordinal))
            return Results.BadRequest(ApiResponse<object>.Fail("The exact driver HOS attestation and explicit acceptance are required"));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var log = await db.QuerySingleAsync(
                @"SELECT hl.driver_id,hl.branch_id,hl.log_date,hl.end_time,hl.is_certified
                  FROM hos_logs hl JOIN drivers d ON d.id=hl.driver_id AND d.company_id=hl.company_id
                  WHERE hl.id=@id AND hl.company_id=@cid AND hl.deleted_at IS NULL AND d.user_id=@uid
                    AND (@branchId::bigint IS NULL OR hl.branch_id=@branchId)",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@uid", PilotUserId(http)); BindPilotScope(c, http); }, ct);
            if (log is null) return Results.NotFound(ApiResponse<object>.Fail("HOS log not found for the authenticated driver"));
            if (log["endTime"] is null or DBNull)
                return Results.Conflict(ApiResponse<object>.Fail("An open duty-status segment cannot be certified"));
            var driverId = log["driverId"] ?? throw new InvalidOperationException("HOS driver identity is missing");
            var ownerBranchId = log["branchId"] is null or DBNull ? (long?)null : Convert.ToInt64(log["branchId"], CultureInfo.InvariantCulture);
            var logDate = log["logDate"] ?? throw new InvalidOperationException("HOS log date is missing");
            // Serialize certification at daily-record scope before taking segment row locks.
            // Locking the clicked segment first could deadlock when two retries target
            // different segments from the same day.
            await db.ExecuteAsync(
                "SELECT pg_advisory_xact_lock(stage65_hos_day_lock_key(@cid::bigint,@driverId::bigint,@date::date,@ownerBranchId::bigint))",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@driverId", driverId);
                    c.Parameters.AddWithValue("@date", logDate); c.Parameters.AddWithValue("@ownerBranchId", (object?)ownerBranchId ?? DBNull.Value);
                }, ct);

            var segments = await db.QueryAsync(
                @"SELECT id,vehicle_id,country_code,profile_id,status,start_time,end_time,duration_minutes,
                         location,source,source_event_id
                  FROM hos_logs
                  WHERE company_id=@cid AND driver_id=@driverId AND log_date=@date AND deleted_at IS NULL
                    AND branch_id IS NOT DISTINCT FROM @ownerBranchId
                  ORDER BY start_time,id FOR UPDATE",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@driverId", driverId); c.Parameters.AddWithValue("@date", logDate); c.Parameters.AddWithValue("@ownerBranchId", (object?)ownerBranchId ?? DBNull.Value); }, ct);
            DateTimeOffset? previousEnd = null;
            var certifiedDate = DateOnly.FromDateTime(Convert.ToDateTime(logDate, CultureInfo.InvariantCulture));
            foreach (var segment in segments)
            {
                if (segment["startTime"] is null or DBNull || segment["endTime"] is null or DBNull)
                    return Results.Conflict(ApiResponse<object>.Fail("Close all duty-status segments before certifying the daily HOS record"));
                var start = PilotTimestamp(segment["startTime"]!);
                var end = PilotTimestamp(segment["endTime"]!);
                if (DateOnly.FromDateTime(start.UtcDateTime) != certifiedDate || DateOnly.FromDateTime(end.UtcDateTime) != certifiedDate)
                    return Results.Conflict(ApiResponse<object>.Fail("Every duty-status segment must fall within the selected HOS log date"));
                if (end < start)
                    return Results.Conflict(ApiResponse<object>.Fail("The daily HOS record contains invalid timestamp chronology"));
                var calculated = (long)Math.Floor((end - start).TotalMinutes);
                var stored = Convert.ToInt64(segment["durationMinutes"], CultureInfo.InvariantCulture);
                if (Math.Abs(calculated - stored) > 1)
                    return Results.Conflict(ApiResponse<object>.Fail("The daily HOS record contains a duration mismatch; submit a correction before certification"));
                if (previousEnd.HasValue && start < previousEnd.Value)
                    return Results.Conflict(ApiResponse<object>.Fail("The daily HOS record contains overlapping duty-status segments"));
                previousEnd = end;
            }
            if (segments.Count == 0) return Results.Conflict(ApiResponse<object>.Fail("No daily HOS segments are available to certify"));

            var sourceSnapshot = segments.Select(segment => new
            {
                id = Convert.ToInt64(segment["id"], CultureInfo.InvariantCulture),
                vehicleId = segment.GetValueOrDefault("vehicleId"), countryCode = segment.GetValueOrDefault("countryCode"),
                profileId = segment.GetValueOrDefault("profileId"), status = segment["status"]?.ToString(),
                start = segment["startTime"], end = segment["endTime"],
                duration = Convert.ToInt64(segment["durationMinutes"], CultureInfo.InvariantCulture),
                location = segment.GetValueOrDefault("location"), source = segment.GetValueOrDefault("source"),
                sourceEventId = segment.GetValueOrDefault("sourceEventId")
            }).ToArray();
            var revision = PilotSha256(sourceSnapshot);
            var sourceSnapshotJson = JsonSerializer.Serialize(sourceSnapshot);
            var attestationHash = PilotSha256(attestation!);
            var inserted = await db.ExecuteAsync(
                @"INSERT INTO hos_certifications(company_id,branch_id,driver_id,log_date,attestation_text,
                    attestation_hash,certified_by,certified_at,source_revision,source_snapshot)
                  VALUES(@cid,@branchId,@driverId,@date,@attestation,@attestationHash,@uid,NOW(),@revision,@snapshot::jsonb)
                  ON CONFLICT(company_id,driver_id,log_date,source_revision) DO NOTHING",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branchId", (object?)ownerBranchId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@driverId", driverId); c.Parameters.AddWithValue("@date", logDate);
                    c.Parameters.AddWithValue("@attestation", attestation!); c.Parameters.AddWithValue("@attestationHash", attestationHash);
                    c.Parameters.AddWithValue("@uid", PilotUserId(http)); c.Parameters.AddWithValue("@revision", revision);
                    c.Parameters.AddWithValue("@snapshot", sourceSnapshotJson);
                }, ct);
            var count = await db.ExecuteAsync(
                @"UPDATE hos_logs SET is_certified=TRUE,certified_at=COALESCE(certified_at,NOW()),
                    certified_by=COALESCE(certified_by,@uid)
                  WHERE company_id=@cid AND driver_id=@driverId AND log_date=@date AND deleted_at IS NULL
                    AND branch_id IS NOT DISTINCT FROM @ownerBranchId AND is_certified=FALSE",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@driverId", driverId);
                    c.Parameters.AddWithValue("@date", logDate); c.Parameters.AddWithValue("@uid", PilotUserId(http));
                    c.Parameters.AddWithValue("@ownerBranchId", (object?)ownerBranchId ?? DBNull.Value);
                }, ct);
            await audit.LogAsync(http, "hos.daily_log_certified", "HosLog", id,
                JsonSerializer.Serialize(new { driverId, logDate, entries = count, sourceRevision = revision, attestationHash }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, certifiedEntries = count, replayed = inserted == 0, sourceRevision = revision },
                inserted == 0 ? "Daily HOS record already certified" : "Daily HOS record certified by the authenticated driver; no external submission was performed"));
        }, ct);
    }

    private static async Task<IResult> HosRecommendationsPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        // ai_recommendations has no authoritative branch ownership column. Returning
        // tenant-wide narratives to a branch-scoped operator can disclose another
        // branch's drivers, violations, or operational posture, so fail closed until
        // recommendations carry persisted branch metadata.
        if (GetBranchId(http) is not null)
            return Results.Ok(ApiResponse<object>.Ok(Array.Empty<object>(),
                "No branch-scoped HOS/ELD recommendations are available"));
        return await OkRows(db,
            @"SELECT * FROM ai_recommendations WHERE company_id=@cid AND module_key='hos-eld'
              ORDER BY score DESC,id DESC LIMIT 10",
            c => c.Parameters.AddWithValue("@cid", GetCompanyId(http)), ct: ct);
    }

    private static async Task<IResult> EldDevicesPilot(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:view", "telematics:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        return await OkRows(db,
            @"SELECT e.id,e.company_id,e.branch_id,e.device_serial,e.device_model,e.provider,
                     e.vehicle_id,e.driver_id,e.status,e.malfunction_code,e.malfunction_description,
                     e.last_sync_at,e.firmware_version,e.created_at,e.updated_at,
                     e.malfunction_resolved_at,e.malfunction_resolved_by,e.resolution_evidence,
                     e.row_version,e.provider_sync_status,
                     v.vehicle_code,d.full_name driver_name,d.driver_code
              FROM eld_devices e
              LEFT JOIN vehicles v ON v.id=e.vehicle_id AND v.company_id=e.company_id
              LEFT JOIN drivers d ON d.id=e.driver_id AND d.company_id=e.company_id
              WHERE e.company_id=@cid AND e.deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR e.branch_id=@branchId)
              ORDER BY ARRAY_POSITION(ARRAY['Malfunction','Diagnostic','Active'],e.status),e.device_serial,e.id",
            c => BindPilotScope(c, http), ct: ct);
    }

    private static async Task<IResult> EldDevicePilot(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:view", "telematics:view") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var rows = await db.QueryAsync(
            @"SELECT e.id,e.company_id,e.branch_id,e.device_serial,e.device_model,e.provider,
                     e.vehicle_id,e.driver_id,e.status,e.malfunction_code,e.malfunction_description,
                     e.last_sync_at,e.firmware_version,e.created_at,e.updated_at,
                     e.malfunction_resolved_at,e.malfunction_resolved_by,e.resolution_evidence,
                     e.row_version,e.provider_sync_status,
                     v.vehicle_code,d.full_name driver_name,d.driver_code
              FROM eld_devices e
              LEFT JOIN vehicles v ON v.id=e.vehicle_id AND v.company_id=e.company_id
              LEFT JOIN drivers d ON d.id=e.driver_id AND d.company_id=e.company_id
              WHERE e.id=@id AND e.company_id=@cid AND e.deleted_at IS NULL
                AND (@branchId::bigint IS NULL OR e.branch_id=@branchId)",
            c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
        var row = rows.FirstOrDefault();
        return row is null ? Results.NotFound(ApiResponse<object>.Fail("ELD device not found")) : Results.Ok(ApiResponse<object>.Ok(row));
    }

    private static async Task<IResult> EldMarkMalfunctionPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:update", "compliance:manage", "telematics:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var code = PilotText(body, "malfunctionCode", 80);
        var description = PilotText(body, "malfunctionDescription", 2000);
        var expectedVersion = PilotRowVersion(http, body);
        if (code is null || description is null || expectedVersion is null)
            return Results.BadRequest(ApiResponse<object>.Fail("Malfunction code, description, and rowVersion are required"));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var device = await db.QuerySingleAsync(
                @"SELECT status,row_version,branch_id FROM eld_devices
                  WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                    AND (@branchId::bigint IS NULL OR branch_id=@branchId) FOR UPDATE",
                c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
            if (device is null) return Results.NotFound(ApiResponse<object>.Fail("ELD device not found in the authorized branch"));
            if (Convert.ToInt64(device["rowVersion"], CultureInfo.InvariantCulture) != expectedVersion.Value)
                return Results.Conflict(ApiResponse<object>.Fail("ELD device changed since it was loaded; refresh and retry"));
            var fromStatus = device["status"]?.ToString() ?? "Unknown";
            if (!new[] { "Active", "Diagnostic" }.Contains(fromStatus, StringComparer.OrdinalIgnoreCase))
                return Results.Conflict(ApiResponse<object>.Fail("Only an Active or Diagnostic ELD device can be marked malfunctioning"));

            await db.ExecuteAsync(
                @"UPDATE eld_devices SET status='Malfunction',malfunction_code=@code,
                        malfunction_description=@description,malfunction_resolved_at=NULL,malfunction_resolved_by=NULL,
                        resolution_evidence=NULL,updated_at=NOW(),row_version=row_version+1
                  WHERE id=@id AND company_id=@cid AND row_version=@version",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@description", description); c.Parameters.AddWithValue("@version", expectedVersion.Value); }, ct);
            await db.ExecuteAsync(
                @"INSERT INTO eld_malfunction_history(company_id,branch_id,eld_device_id,event_type,from_status,to_status,
                        malfunction_code,malfunction_description,actor_user_id)
                  VALUES(@cid,@branchId,@id,'Malfunction Reported',@fromStatus,'Malfunction',@code,@description,@uid)",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@branchId", device.GetValueOrDefault("branchId") ?? DBNull.Value); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@fromStatus", fromStatus); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@description", description); c.Parameters.AddWithValue("@uid", PilotUserId(http)); }, ct);
            await audit.LogAsync(http, "eld.malfunction", "EldDevice", id, $"code:{code}", ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, status = "Malfunction", rowVersion = expectedVersion.Value + 1 }, "ELD malfunction recorded"));
        }, ct);
    }

    private static async Task<IResult> EldResolveMalfunctionPilot(HttpContext http, long id,
        Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (RequireAnyDirectPermission(http, "compliance:update", "compliance:manage", "telematics:manage") is { } denied) return denied;
        if (await RequireComplianceModule(http, db, ct) is { } gated) return gated;
        var evidence = PilotText(body, "resolutionEvidence", 2000);
        var expectedVersion = PilotRowVersion(http, body);
        if (evidence is null || expectedVersion is null)
            return Results.BadRequest(ApiResponse<object>.Fail("resolutionEvidence and rowVersion are required"));
        var companyId = GetCompanyId(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var device = await db.QuerySingleAsync(
                @"SELECT status,row_version,last_sync_at,provider_sync_status,malfunction_code FROM eld_devices
                  WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                    AND (@branchId::bigint IS NULL OR branch_id=@branchId) FOR UPDATE",
                c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
            if (device is null) return Results.NotFound(ApiResponse<object>.Fail("ELD device not found"));
            if (Convert.ToInt64(device["rowVersion"], CultureInfo.InvariantCulture) != expectedVersion.Value)
                return Results.Conflict(ApiResponse<object>.Fail("ELD device changed since it was loaded; refresh and retry"));
            var fromStatus = device["status"]?.ToString() ?? "Unknown";
            if (!new[] { "Malfunction", "Diagnostic" }.Contains(fromStatus, StringComparer.OrdinalIgnoreCase))
                return Results.Conflict(ApiResponse<object>.Fail("Only a Malfunction or Diagnostic ELD device can be verified for recovery"));

            var lastSync = device["lastSyncAt"] switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                _ => (DateTimeOffset?)null
            };
            var syncStatus = device["providerSyncStatus"]?.ToString();
            var recentHealthySync = lastSync.HasValue && lastSync.Value >= DateTimeOffset.UtcNow.AddMinutes(-15)
                && syncStatus is not null && new[] { "Success", "Healthy", "Connected" }.Contains(syncStatus, StringComparer.OrdinalIgnoreCase);
            var credentials = await db.QuerySingleInSystemScopeAsync(
                @"SELECT api_key_hash,hmac_secret_encrypted FROM eld_devices
                  WHERE id=@id AND company_id=@cid AND deleted_at IS NULL
                    AND (@branchId::bigint IS NULL OR branch_id=@branchId)",
                c => { c.Parameters.AddWithValue("@id", id); BindPilotScope(c, http); }, ct);
            var credentialsValid = credentials?["apiKeyHash"]?.ToString() is { Length: 64 } apiHash
                && apiHash.All(Uri.IsHexDigit)
                && credentials["hmacSecretEncrypted"]?.ToString()?.Trim().Length >= 24;
            var verifiedForActive = recentHealthySync && credentialsValid;
            var targetStatus = verifiedForActive ? "Active" : "Diagnostic";
            await db.ExecuteAsync(
                @"UPDATE eld_devices SET status=@status,
                    malfunction_resolved_at=CASE WHEN status='Malfunction' THEN NOW() ELSE malfunction_resolved_at END,
                    malfunction_resolved_by=CASE WHEN status='Malfunction' THEN @uid ELSE malfunction_resolved_by END,
                    resolution_evidence=CASE WHEN status='Malfunction' THEN @evidence ELSE resolution_evidence END,
                    updated_at=NOW(),row_version=row_version+1
                  WHERE id=@id AND company_id=@cid AND row_version=@version",
                c =>
                {
                    c.Parameters.AddWithValue("@status", targetStatus); c.Parameters.AddWithValue("@uid", PilotUserId(http));
                    c.Parameters.AddWithValue("@evidence", evidence); c.Parameters.AddWithValue("@id", id);
                    c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@version", expectedVersion.Value);
                }, ct);
            await db.ExecuteAsync(
                @"INSERT INTO eld_malfunction_history(company_id,branch_id,eld_device_id,event_type,from_status,to_status,
                        malfunction_code,malfunction_description,evidence,actor_user_id)
                  SELECT company_id,branch_id,id,@eventType,@fromStatus,@toStatus,malfunction_code,
                         malfunction_description,@evidence,@uid
                  FROM eld_devices WHERE id=@id AND company_id=@cid",
                c =>
                {
                    c.Parameters.AddWithValue("@eventType", string.Equals(fromStatus, "Malfunction", StringComparison.OrdinalIgnoreCase) ? "Resolution Recorded" : "Recovery Verification");
                    c.Parameters.AddWithValue("@fromStatus", fromStatus); c.Parameters.AddWithValue("@toStatus", targetStatus);
                    c.Parameters.AddWithValue("@evidence", evidence); c.Parameters.AddWithValue("@uid", PilotUserId(http));
                    c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId);
                }, ct);
            await audit.LogAsync(http, "eld.malfunction.resolved", "EldDevice", id,
                JsonSerializer.Serialize(new { fromStatus, targetStatus, recentHealthySync, credentialsValid, lastSync, syncStatus, evidence }), ct);
            return Results.Ok(ApiResponse<object>.Ok(new
            {
                id, status = targetStatus, rowVersion = expectedVersion.Value + 1,
                verifiedRecentProviderSync = recentHealthySync, verifiedCredentials = credentialsValid, malfunctionCode = device.GetValueOrDefault("malfunctionCode")
            }, targetStatus == "Active"
                ? "ELD malfunction resolved after operator evidence and a recent healthy provider sync"
                : "ELD malfunction resolution recorded; device remains Diagnostic until a recent healthy provider sync is verified"));
        }, ct);
    }
}
