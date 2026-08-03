using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Market-pack + regional compliance HTTP surface. Tenant-scoped (company_id) and
// entitlement-enforced: Canada/NA endpoints require the canada_na market pack,
// Saudi/GCC endpoints require saudi_gcc — DENY-BY-DEFAULT (paid add-ons). Platform
// Admin market-pack management reuses PlatformEndpoints.RequireAsync.
//
// Reuses Sprint-1 EntitlementService for pack checks + usage metering, and the
// existing fleet_tms_saudi_regions reference table for Saudi geography.
public static class MarketPackEndpoints
{
    internal const string ActiveMarketPackStatus = "active";
    internal const string DisabledMarketPackStatus = "disabled";

    public static void MapMarketPackEndpoints(this WebApplication app)
    {
        // ── Market catalog (tenant) ───────────────────────────────────────────
        app.MapGet("/api/market-packs", MarketPacks);
        app.MapGet("/api/market-packs/canada-na", (HttpContext h, Database db, CancellationToken ct) => PackDetail(h, db, MarketPackSchemaService.Packs.CanadaNa, ct));
        app.MapGet("/api/market-packs/canada-na/requirements", (HttpContext h, Database db, CancellationToken ct) => PackRequirements(h, db, MarketPackSchemaService.Packs.CanadaNa, ct));
        app.MapGet("/api/market-packs/saudi-gcc", (HttpContext h, Database db, CancellationToken ct) => PackDetail(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct));
        app.MapGet("/api/market-packs/saudi-gcc/requirements", (HttpContext h, Database db, CancellationToken ct) => PackRequirements(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct));

        // ── Canada / NA compliance ────────────────────────────────────────────
        app.MapGet("/api/fleet-compliance/driver-documents", DriverDocuments);
        app.MapPost("/api/fleet-compliance/driver-documents", CreateDriverDocument);
        app.MapPut("/api/fleet-compliance/driver-documents/{id:long}", UpdateDriverDocument);
        app.MapGet("/api/fleet-compliance/vehicle-inspections", VehicleInspections);
        app.MapPost("/api/fleet-compliance/vehicle-inspections", CreateVehicleInspection);
        app.MapPut("/api/fleet-compliance/vehicle-inspections/{id:long}", UpdateVehicleInspection);
        app.MapGet("/api/fleet-compliance/expiries", Expiries);
        app.MapGet("/api/fleet-compliance/ifta-readiness", IftaReadiness);
        app.MapPost("/api/fleet-compliance/jurisdiction-mileage", CreateJurisdictionMileage);
        app.MapPost("/api/fleet-compliance/jurisdiction-fuel", CreateJurisdictionFuel);
        app.MapGet("/api/fleet-compliance/hos-readiness", HosReadiness);

        // ── Saudi / GCC compliance ────────────────────────────────────────────
        app.MapGet("/api/fleet-compliance/saudi/regions", SaudiRegions);
        app.MapGet("/api/fleet-compliance/saudi/cities", SaudiCities);
        app.MapGet("/api/fleet-compliance/saudi/documents", SaudiDocuments);
        app.MapPost("/api/fleet-compliance/saudi/documents", CreateSaudiDocument);
        app.MapPut("/api/fleet-compliance/saudi/documents/{id:long}", UpdateSaudiDocument);
        app.MapGet("/api/fleet-compliance/saudi/expiries", SaudiExpiries);
        app.MapGet("/api/fleet-compliance/saudi/vat-readiness", SaudiVatReadiness);
        app.MapPut("/api/fleet-compliance/saudi/vat-readiness", SetSaudiVatReadiness);

        // ── Platform Admin (market-pack control) ──────────────────────────────
        app.MapGet("/api/platform/opstrax/market-packs", PlatformMarketPacks);
        app.MapGet("/api/platform/opstrax/tenants/{tenantId:long}/market-packs", PlatformTenantMarketPacks);
        app.MapPut("/api/platform/opstrax/tenants/{tenantId:long}/market-packs", PlatformSetTenantMarketPack);
        app.MapGet("/api/platform/opstrax/tenants/{tenantId:long}/compliance-usage", PlatformComplianceUsage);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static long Company(HttpContext h) => EndpointMappings.GetCompanyId(h);
    private static long? Branch(HttpContext h) => EndpointMappings.GetBranchId(h);
    private static string BranchScope(HttpContext h, string alias = "")
        => Branch(h) is null ? "" : $" AND {alias}branch_id=@branchId";
    private static void BindBranch(Npgsql.NpgsqlCommand command, HttpContext h)
    {
        if (Branch(h) is { } branchId) command.Parameters.AddWithValue("@branchId", branchId);
    }
    private static EntitlementService Ent(Database db) => new(db);
    private static string Actor(HttpContext h) => h.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null ? $"user:{u}" : "system";
    private static IResult OkJson(object data) => Results.Json(ApiResponse<object>.Ok(data));
    private static IResult Denied(string reason) => Results.Json(ApiResponse<object>.Fail("Feature not entitled", reason), statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult?> RequirePack(HttpContext h, Database db, string pack, CancellationToken ct)
    {
        var d = await Ent(db).CheckMarketPackAsync(Company(h), pack, ct);
        return d.Allowed ? null : Denied(d.Reason ?? pack);
    }

    private static string Str(Dictionary<string, object?> b, string k) => b.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
    private static DateTime? DateOf(Dictionary<string, object?> b, string k) => DateTime.TryParse(Str(b, k), out var d) ? d.Date : null;
    private static bool InvalidDate(Dictionary<string, object?> body, string key)
        => !string.IsNullOrWhiteSpace(Str(body, key)) && DateOf(body, key) is null;
    private static bool ValidHijriText(string value)
        => System.Text.RegularExpressions.Regex.IsMatch(value, @"^1[34]\d{2}[-/]((0?[1-9])|(1[0-2]))[-/]((0?[1-9])|([12]\d)|(3[01]))$");

    private static DateTime? HijriToGregorian(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidHijriText(value)) return null;
        var parts = value.Replace('/', '-').Split('-').Select(int.Parse).ToArray();
        try { return new System.Globalization.HijriCalendar().ToDateTime(parts[0], parts[1], parts[2], 0, 0, 0, 0).Date; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // Public + pure so it is unit-testable. Missing/unconvertible evidence is never healthy.
    public static string ExpiryStatus(DateTime? expiry, DateTime? today = null)
    {
        if (expiry is null) return "unknown";
        var days = (expiry.Value.Date - (today ?? DateTime.UtcNow).Date).TotalDays;
        return days < 0 ? "expired" : days <= 30 ? "expiring" : "valid";
    }

    private static void ApplyLiveExpiry(IEnumerable<Dictionary<string, object?>> records)
    {
        foreach (var record in records)
        {
            var expiry = record.GetValueOrDefault("expiryDate") switch
            {
                DateTime date => date,
                DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                _ => (DateTime?)null,
            };
            var hijriExpiry = HijriToGregorian(record.GetValueOrDefault("hijriExpiryDate")?.ToString());
            expiry ??= hijriExpiry;
            record["documentStatus"] = ExpiryStatus(expiry);
            record["expiryComputedAtUtc"] = DateTime.UtcNow;
            record["expiryBasis"] = record.GetValueOrDefault("expiryDate") is DateTime or DateOnly ? "gregorian" : hijriExpiry.HasValue ? "hijri_converted" : "none";
            record["effectiveExpiryDate"] = expiry;
            record["needsReview"] = expiry is null;
        }
    }

    public static string? ValidateVehicleInspectionInput(Dictionary<string, object?> body)
    {
        if (string.IsNullOrWhiteSpace(Str(body, "vehicleLabel")) && !long.TryParse(Str(body, "vehicleId"), out _))
            return "A vehicle identifier or label is required.";
        if (string.IsNullOrWhiteSpace(Str(body, "inspectorName")))
            return "Inspector name is required.";
        if (!Allowed(Str(body, "inspectionType"), "pre_trip", "post_trip", "annual")) return "Inspection type is invalid.";
        if (!Allowed(Str(body, "status"), "pass", "fail", "conditional", "needs_repair")) return "Inspection status is invalid.";
        if (Str(body, "vehicleLabel").Length > 160 || Str(body, "inspectorName").Length > 160 || Str(body, "notes").Length > 500)
            return "One or more inspection fields exceed their maximum length.";
        return null;
    }

    public static string? ValidateJurisdictionMileageInput(Dictionary<string, object?> body)
    {
        if (string.IsNullOrWhiteSpace(Str(body, "provinceState"))) return "Province or state is required.";
        if (!decimal.TryParse(Str(body, "distance"), out var distance) || distance <= 0) return "Distance must be greater than zero.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(Str(body, "taxPeriod"), @"^\d{4}-Q[1-4]$"))
            return "Tax period must use YYYY-Q1 through YYYY-Q4.";
        if (!Allowed(Str(body, "distanceUnit"), "km", "mi", "mile", "miles")) return "Distance unit must be km or mi.";
        return null;
    }

    public static string? ValidateJurisdictionFuelInput(Dictionary<string, object?> body)
    {
        if (string.IsNullOrWhiteSpace(Str(body, "provinceState"))) return "Province or state is required.";
        if (!decimal.TryParse(Str(body, "fuelVolume"), out var volume) || volume <= 0) return "Fuel volume must be greater than zero.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(Str(body, "taxPeriod"), @"^\d{4}-Q[1-4]$"))
            return "Tax period must use YYYY-Q1 through YYYY-Q4.";
        if (!Allowed(Str(body, "fuelUnit"), "liter", "litre", "l", "gallon_us", "us_gallon")) return "Fuel unit must be litre or US gallon.";
        return null;
    }

    private static bool Allowed(string? value, params string[] allowed)
        => string.IsNullOrWhiteSpace(value) || allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    // ════════════════════════════ Market catalog ════════════════════════════

    private static async Task<IResult> MarketPacks(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "dashboard:view");
        if (denied is not null) return denied;
        var companyId = Company(h);
        var packs = await db.QueryAsync("SELECT code, name, description, region, status, default_currency, default_distance_unit, default_fuel_unit, supported_languages, feature_keys, package_key, base_price_cents FROM market_packs ORDER BY name", ct: ct);
        var assigned = (await db.QueryAsync("SELECT pack_code, status FROM tenant_market_packs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId), ct))
            .ToDictionary(r => r["packCode"]?.ToString() ?? "", r => r["status"]?.ToString() ?? "");
        var items = packs.Select(p =>
        {
            var code = p["code"]?.ToString() ?? "";
            return new
            {
                code, name = p["name"], description = p["description"], region = p["region"], status = p["status"],
                defaultCurrency = p["defaultCurrency"], defaultDistanceUnit = p["defaultDistanceUnit"], defaultFuelUnit = p["defaultFuelUnit"],
                supportedLanguages = p["supportedLanguages"], featureKeys = p["featureKeys"], packageKey = p["packageKey"], basePriceCents = p["basePriceCents"],
                tenantEnabled = assigned.TryGetValue(code, out var s) && s == "active",
            };
        });
        return OkJson(new { items });
    }

    private static async Task<IResult> PackDetail(HttpContext h, Database db, string code, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "dashboard:view");
        if (denied is not null) return denied;
        var pack = await db.QuerySingleAsync("SELECT * FROM market_packs WHERE code=@c", c => c.Parameters.AddWithValue("@c", code), ct);
        if (pack is null) return Results.NotFound(ApiResponse<object>.Fail("Market pack not found"));
        var features = await db.QueryAsync("SELECT feature_key, name, tier, included FROM market_pack_features WHERE pack_code=@c ORDER BY id", c => c.Parameters.AddWithValue("@c", code), ct);
        var tenantEnabled = await Ent(db).HasMarketPackAsync(Company(h), code, ct);
        return OkJson(new { pack, features, tenantEnabled });
    }

    private static async Task<IResult> PackRequirements(HttpContext h, Database db, string code, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "dashboard:view");
        if (denied is not null) return denied;
        var driverReqs = await db.QueryAsync("SELECT requirement_key, name, mandatory FROM market_driver_requirements WHERE pack_code=@c ORDER BY id", c => c.Parameters.AddWithValue("@c", code), ct);
        var vehicleReqs = await db.QueryAsync("SELECT requirement_key, name, mandatory FROM market_vehicle_requirements WHERE pack_code=@c ORDER BY id", c => c.Parameters.AddWithValue("@c", code), ct);
        var docTypes = await db.QueryAsync("SELECT doc_key, name, applies_to, has_expiry FROM market_document_types WHERE pack_code=@c ORDER BY id", c => c.Parameters.AddWithValue("@c", code), ct);
        var templates = await db.QueryAsync("SELECT template_key, name, description FROM market_inspection_templates WHERE pack_code=@c ORDER BY id", c => c.Parameters.AddWithValue("@c", code), ct);
        var addressSchema = await db.QueryAsync("SELECT field_key, label_en, label_local, required, sort_order FROM market_address_schemas WHERE pack_code=@c ORDER BY sort_order", c => c.Parameters.AddWithValue("@c", code), ct);
        var units = await db.QuerySingleAsync("SELECT distance_unit, fuel_unit, weight_unit FROM market_unit_settings WHERE pack_code=@c", c => c.Parameters.AddWithValue("@c", code), ct);
        var currencies = await db.QueryAsync("SELECT currency, is_default FROM market_currency_settings WHERE pack_code=@c", c => c.Parameters.AddWithValue("@c", code), ct);
        var languages = await db.QueryAsync("SELECT language, is_default, rtl FROM market_language_settings WHERE pack_code=@c", c => c.Parameters.AddWithValue("@c", code), ct);
        var taxRules = await db.QueryAsync("SELECT rule_key, name, description FROM market_tax_reporting_rules WHERE pack_code=@c", c => c.Parameters.AddWithValue("@c", code), ct);
        return OkJson(new { driverRequirements = driverReqs, vehicleRequirements = vehicleReqs, documentTypes = docTypes, inspectionTemplates = templates, addressSchema, units, currencies, languages, taxRules });
    }

    // ════════════════════════════ Canada compliance ═════════════════════════

    private static async Task<IResult> DriverDocuments(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var items = await db.QueryAsync(
            "SELECT * FROM compliance_records WHERE company_id=@c AND pack_code='canada_na' AND subject_type IN ('driver','vehicle')" + BranchScope(h) + " ORDER BY expiry_date NULLS LAST, id DESC",
            c => { c.Parameters.AddWithValue("@c", Company(h)); BindBranch(c, h); }, ct);
        ApplyLiveExpiry(items);
        return OkJson(new { items });
    }

    private static async Task<IResult> CreateDriverDocument(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        if (string.IsNullOrWhiteSpace(Str(body, "subjectName"))) return Results.BadRequest(ApiResponse<object>.Fail("Subject name is required."));
        if (!Allowed(Str(body, "subjectType"), "driver", "vehicle")) return Results.BadRequest(ApiResponse<object>.Fail("Subject type is invalid."));
        if (InvalidDate(body, "expiryDate") || DateOf(body, "expiryDate") is null) return Results.BadRequest(ApiResponse<object>.Fail("A valid expiry date is required."));
        var expiry = DateOf(body, "expiryDate");
        var status = ExpiryStatus(expiry);
        var id = await db.InsertAsync("""
            INSERT INTO compliance_records (company_id, branch_id, pack_code, subject_type, subject_id, subject_name, doc_key, document_no, document_status, issuing_region, issuing_country, issue_date, expiry_date, metadata)
            VALUES (@c,@branchId,'canada_na',@st,@sid,@sn,@dk,@dn,@status,@reg,@ctry,@issue,@expiry,@meta::jsonb) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)Branch(h) ?? DBNull.Value);
                c.Parameters.AddWithValue("@st", string.IsNullOrWhiteSpace(Str(body, "subjectType")) ? "driver" : Str(body, "subjectType"));
                c.Parameters.AddWithValue("@sid", long.TryParse(Str(body, "subjectId"), out var sid) ? sid : (object)DBNull.Value);
                c.Parameters.AddWithValue("@sn", (object?)NullIfEmpty(Str(body, "subjectName")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@dk", string.IsNullOrWhiteSpace(Str(body, "docKey")) ? "drivers_license" : Str(body, "docKey"));
                c.Parameters.AddWithValue("@dn", (object?)NullIfEmpty(Str(body, "documentNo")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@reg", (object?)NullIfEmpty(Str(body, "licenseRegion")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@ctry", (object?)NullIfEmpty(Str(body, "licenseCountry")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@issue", (object?)DateOf(body, "issueDate") ?? DBNull.Value);
                c.Parameters.AddWithValue("@expiry", (object?)expiry ?? DBNull.Value);
                c.Parameters.AddWithValue("@meta", BuildMeta(body, "licenseClass", "endorsementType", "endorsementExpiryDate", "driverQualificationStatus", "medicalDocumentExpiryDate"));
            }, ct);

        await Ent(db).RecordAsync(companyId, "compliance_documents.count", 1, $"record:{id}", Actor(h), ct);
        await MaybeRaiseExpiry(db, companyId, Branch(h), "canada_na", id, Str(body, "subjectType"), Str(body, "subjectName"), Str(body, "docKey"), expiry, status, ct);
        return OkJson(await ById(db, h, id, ct));
    }

    private static async Task<IResult> UpdateDriverDocument(long id, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var existing = await db.QuerySingleAsync("SELECT * FROM compliance_records WHERE id=@id AND company_id=@c AND pack_code='canada_na'" + BranchScope(h),
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        if (existing is null) return Results.NotFound(ApiResponse<object>.Fail("Document not found"));
        if (InvalidDate(body, "expiryDate")) return Results.BadRequest(ApiResponse<object>.Fail("Expiry date is malformed."));
        var expiry = DateOf(body, "expiryDate") ?? (existing["expiryDate"] is DateTime currentExpiry ? currentExpiry : null);
        var status = ExpiryStatus(expiry);
        var rows = await db.ExecuteAsync($"""
            UPDATE compliance_records SET
                document_no = COALESCE(NULLIF(@dn,''), document_no),
                document_status = @status,
                expiry_date = COALESCE(@expiry, expiry_date),
                updated_at = NOW()
            WHERE id=@id AND company_id=@c AND pack_code='canada_na'{BranchScope(h)}
            """,
            c => { c.Parameters.AddWithValue("@dn", Str(body, "documentNo")); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@expiry", (object?)expiry ?? DBNull.Value); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        if (rows == 0) return Results.NotFound(ApiResponse<object>.Fail("Document not found"));
        await RefreshExpiry(db, companyId, Branch(h), "canada_na", id,
            existing.GetValueOrDefault("subjectType")?.ToString() ?? "", existing.GetValueOrDefault("subjectName")?.ToString() ?? "",
            existing.GetValueOrDefault("docKey")?.ToString() ?? "", expiry, status, ct);
        return OkJson(await ById(db, h, id, ct));
    }

    private static async Task<IResult> VehicleInspections(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var items = await db.QueryAsync("SELECT * FROM vehicle_inspection_records WHERE company_id=@c" + BranchScope(h) + " ORDER BY inspected_at DESC", c => { c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        var defects = await db.QueryAsync("SELECT * FROM inspection_defects WHERE company_id=@c" + BranchScope(h) + " ORDER BY id", c => { c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        return OkJson(new { items, defects });
    }

    private static async Task<IResult> CreateVehicleInspection(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        if (ValidateVehicleInspectionInput(body) is { } validationError)
            return Results.BadRequest(ApiResponse<object>.Fail(validationError));
        var companyId = Company(h);
        var branchId = Branch(h);
        long? vehicleId = long.TryParse(Str(body, "vehicleId"), out var parsedVehicleId) ? parsedVehicleId : null;
        var defects = new List<(string? ItemKey, string Description, string Severity, bool RepairRequired)>();
        if (body.TryGetValue("defects", out var defectsRaw) && defectsRaw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var d in je.EnumerateArray())
            {
                var description = (d.TryGetProperty("description", out var de) ? de.GetString() : null)?.Trim();
                var severity = (d.TryGetProperty("severity", out var sv) ? sv.GetString() : null)?.Trim().ToLowerInvariant() ?? "minor";
                if (string.IsNullOrWhiteSpace(description) || description.Length > 400 || !Allowed(severity, "minor", "major", "critical"))
                    return Results.BadRequest(ApiResponse<object>.Fail("Every defect requires a valid description and minor, major, or critical severity."));
                defects.Add(((d.TryGetProperty("itemKey", out var ik) ? ik.GetString() : null), description, severity,
                    d.TryGetProperty("repairRequired", out var rr) && rr.ValueKind == System.Text.Json.JsonValueKind.True));
            }
        var status = (string.IsNullOrWhiteSpace(Str(body, "status")) ? "pass" : Str(body, "status")).ToLowerInvariant();
        if (status == "pass" && defects.Count != 0) return Results.BadRequest(ApiResponse<object>.Fail("A passing inspection cannot contain unresolved defects."));
        if (status is "fail" or "needs_repair" && defects.Count == 0) return Results.BadRequest(ApiResponse<object>.Fail("A failed or repair-required inspection must include at least one defect."));
        var outOfService = status is "fail" or "needs_repair" || defects.Any(d => d.Severity == "critical" || d.RepairRequired);

        long id;
        try
        {
            id = await db.WithTransactionAsync(async (connection, transaction) =>
            {
                if (!vehicleId.HasValue)
                {
                    await using var resolve = new Npgsql.NpgsqlCommand(
                        "SELECT id FROM vehicles WHERE company_id=@c AND deleted_at IS NULL AND (lower(vehicle_code)=lower(@label) OR lower(COALESCE(plate_number,''))=lower(@label))" + (branchId is null ? "" : " AND branch_id=@branchId") + " ORDER BY id LIMIT 2", connection, transaction);
                    resolve.Parameters.AddWithValue("@c", companyId); resolve.Parameters.AddWithValue("@label", Str(body, "vehicleLabel").Trim());
                    if (branchId is not null) resolve.Parameters.AddWithValue("@branchId", branchId.Value);
                    var matches = new List<long>(); await using var reader = await resolve.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) matches.Add(reader.GetInt64(0));
                    await reader.DisposeAsync();
                    if (matches.Count != 1) throw new InvalidOperationException("Vehicle label must match exactly one tenant/branch vehicle code or plate number.");
                    vehicleId = matches[0];
                }
                if (vehicleId.HasValue)
                {
                    await using var ownership = new Npgsql.NpgsqlCommand(
                        "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c AND deleted_at IS NULL" + (branchId is null ? "" : " AND branch_id=@branchId"), connection, transaction);
                    ownership.Parameters.AddWithValue("@id", vehicleId.Value); ownership.Parameters.AddWithValue("@c", companyId);
                    if (branchId is not null) ownership.Parameters.AddWithValue("@branchId", branchId.Value);
                    if (Convert.ToInt64(await ownership.ExecuteScalarAsync(ct)) != 1) throw new InvalidOperationException("Vehicle not found for this tenant and branch.");
                }
                await using var insert = new Npgsql.NpgsqlCommand("""
                    INSERT INTO vehicle_inspection_records (company_id, branch_id, template_key, vehicle_id, vehicle_label, inspector_name, inspection_type, status, out_of_service, repair_status, notes)
                    VALUES (@c,@branchId,@tk,@vid,@vl,@insp,@type,@status,@oos,@repair,@notes) RETURNING id
                    """, connection, transaction);
                insert.Parameters.AddWithValue("@c", companyId); insert.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@tk", (object?)NullIfEmpty(Str(body, "templateKey")) ?? "dvir_pre_trip");
                insert.Parameters.AddWithValue("@vid", (object?)vehicleId ?? DBNull.Value); insert.Parameters.AddWithValue("@vl", (object?)NullIfEmpty(Str(body, "vehicleLabel")) ?? DBNull.Value);
                insert.Parameters.AddWithValue("@insp", Str(body, "inspectorName").Trim()); insert.Parameters.AddWithValue("@type", string.IsNullOrWhiteSpace(Str(body, "inspectionType")) ? "pre_trip" : Str(body, "inspectionType").Trim().ToLowerInvariant());
                insert.Parameters.AddWithValue("@status", status); insert.Parameters.AddWithValue("@oos", outOfService); insert.Parameters.AddWithValue("@repair", outOfService ? "open" : "not_required");
                insert.Parameters.AddWithValue("@notes", (object?)NullIfEmpty(Str(body, "notes")) ?? DBNull.Value);
                var inspectionId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
                foreach (var defect in defects)
                {
                    await using var defectInsert = new Npgsql.NpgsqlCommand("INSERT INTO inspection_defects (company_id,branch_id,inspection_id,item_key,description,defect_severity,repair_required) VALUES (@c,@branchId,@iid,@key,@description,@severity,@required)", connection, transaction);
                    defectInsert.Parameters.AddWithValue("@c", companyId); defectInsert.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); defectInsert.Parameters.AddWithValue("@iid", inspectionId);
                    defectInsert.Parameters.AddWithValue("@key", (object?)defect.ItemKey ?? DBNull.Value); defectInsert.Parameters.AddWithValue("@description", defect.Description); defectInsert.Parameters.AddWithValue("@severity", defect.Severity); defectInsert.Parameters.AddWithValue("@required", defect.RepairRequired || defect.Severity == "critical");
                    await defectInsert.ExecuteNonQueryAsync(ct);
                }
                if (outOfService && vehicleId.HasValue)
                {
                    await using var hold = new Npgsql.NpgsqlCommand("UPDATE vehicles SET out_of_service=true, availability_status='out_of_service' WHERE id=@id AND company_id=@c", connection, transaction);
                    hold.Parameters.AddWithValue("@id", vehicleId.Value); hold.Parameters.AddWithValue("@c", companyId); await hold.ExecuteNonQueryAsync(ct);
                }
                return inspectionId;
            }, ct);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(ApiResponse<object>.Fail(ex.Message)); }

        await Ent(db).RecordAsync(companyId, "inspection_records.monthly", 1, $"inspection:{id}", Actor(h), ct);
        return OkJson(await db.QuerySingleAsync("SELECT * FROM vehicle_inspection_records WHERE id=@id AND company_id=@c" + BranchScope(h), c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct)!);
    }

    private static async Task<IResult> UpdateVehicleInspection(long id, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        if (!Allowed(Str(body, "status"), "pass", "fail", "conditional", "needs_repair")) return Results.BadRequest(ApiResponse<object>.Fail("Inspection status is invalid."));
        var certifyRepair = bool.TryParse(Str(body, "repairCertified"), out var certified) && certified;
        if (certifyRepair && string.IsNullOrWhiteSpace(Str(body, "repairCertifiedBy"))) return Results.BadRequest(ApiResponse<object>.Fail("Repair certifier is required."));
        if (certifyRepair && !string.Equals(Str(body, "status"), "pass", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(ApiResponse<object>.Fail("Repair certification must transition the inspection to pass."));
        int rows;
        try
        {
            rows = await db.WithTransactionAsync(async (connection, transaction) =>
            {
                await using var inspect = new Npgsql.NpgsqlCommand("SELECT vehicle_id,out_of_service,repair_status FROM vehicle_inspection_records WHERE id=@id AND company_id=@c" + BranchScope(h) + " FOR UPDATE", connection, transaction);
                inspect.Parameters.AddWithValue("@id", id); inspect.Parameters.AddWithValue("@c", companyId); BindBranch(inspect, h);
                object? vehicle; bool wasOutOfService; await using (var reader = await inspect.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) return 0;
                    vehicle = reader.IsDBNull(0) ? DBNull.Value : reader.GetInt64(0); wasOutOfService = reader.GetBoolean(1);
                }
                if (certifyRepair)
                {
                    await using var defects = new Npgsql.NpgsqlCommand("UPDATE inspection_defects SET repair_certified_at=NOW(), repair_certified_by=@by, repair_notes=@notes WHERE inspection_id=@id AND company_id=@c AND repair_required AND repair_certified_at IS NULL", connection, transaction);
                    defects.Parameters.AddWithValue("@by", Str(body, "repairCertifiedBy")); defects.Parameters.AddWithValue("@notes", (object?)NullIfEmpty(Str(body, "repairNotes")) ?? DBNull.Value); defects.Parameters.AddWithValue("@id", id); defects.Parameters.AddWithValue("@c", companyId); await defects.ExecuteNonQueryAsync(ct);
                }
                await using var ownBlockers = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM inspection_defects WHERE inspection_id=@id AND company_id=@c AND repair_required AND repair_certified_at IS NULL", connection, transaction);
                ownBlockers.Parameters.AddWithValue("@id", id); ownBlockers.Parameters.AddWithValue("@c", companyId);
                var unresolvedOwn = Convert.ToInt64(await ownBlockers.ExecuteScalarAsync(ct));
                var requestedStatus = Str(body, "status").Trim().ToLowerInvariant();
                if (requestedStatus == "pass" && (unresolvedOwn > 0 || (wasOutOfService && !certifyRepair)))
                    throw new InvalidOperationException("Inspection cannot pass until every repair-required defect is certified.");
                await using var update = new Npgsql.NpgsqlCommand(
                    "UPDATE vehicle_inspection_records SET status=COALESCE(NULLIF(@status,''),status), notes=COALESCE(NULLIF(@notes,''),notes), repair_status=CASE WHEN @certify AND @unresolved=0 THEN 'certified' ELSE repair_status END, out_of_service=CASE WHEN @certify AND @unresolved=0 THEN false ELSE out_of_service END, repair_certified_at=CASE WHEN @certify AND @unresolved=0 THEN NOW() ELSE repair_certified_at END, repair_certified_by=CASE WHEN @certify AND @unresolved=0 THEN @by ELSE repair_certified_by END WHERE id=@id AND company_id=@c" + BranchScope(h), connection, transaction);
                update.Parameters.AddWithValue("@status", requestedStatus); update.Parameters.AddWithValue("@notes", Str(body, "notes")); update.Parameters.AddWithValue("@certify", certifyRepair); update.Parameters.AddWithValue("@unresolved", unresolvedOwn); update.Parameters.AddWithValue("@by", Str(body, "repairCertifiedBy")); update.Parameters.AddWithValue("@id", id); update.Parameters.AddWithValue("@c", companyId); BindBranch(update, h); await update.ExecuteNonQueryAsync(ct);
                if (vehicle is not DBNull && certifyRepair && unresolvedOwn == 0)
                {
                    await using var allBlockers = new Npgsql.NpgsqlCommand("""
                        SELECT COUNT(*) FROM vehicle_inspection_records i
                        WHERE i.company_id=@c AND i.vehicle_id=@vehicleId
                          AND (i.out_of_service OR i.repair_status='open' OR EXISTS (
                            SELECT 1 FROM inspection_defects d WHERE d.company_id=i.company_id AND d.inspection_id=i.id
                              AND d.repair_required AND d.repair_certified_at IS NULL))
                        """, connection, transaction);
                    allBlockers.Parameters.AddWithValue("@c", companyId); allBlockers.Parameters.AddWithValue("@vehicleId", Convert.ToInt64(vehicle));
                    var remaining = Convert.ToInt64(await allBlockers.ExecuteScalarAsync(ct));
                    if (remaining == 0)
                    {
                        await using var release = new Npgsql.NpgsqlCommand("UPDATE vehicles SET out_of_service=false, availability_status='available' WHERE id=@id AND company_id=@c AND out_of_service=true", connection, transaction);
                        release.Parameters.AddWithValue("@id", Convert.ToInt64(vehicle)); release.Parameters.AddWithValue("@c", companyId); await release.ExecuteNonQueryAsync(ct);
                    }
                }
                return 1;
            }, ct);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(ApiResponse<object>.Fail(ex.Message)); }
        if (rows == 0) return Results.NotFound(ApiResponse<object>.Fail("Inspection not found"));
        return OkJson(await db.QuerySingleAsync("SELECT * FROM vehicle_inspection_records WHERE id=@id AND company_id=@c" + BranchScope(h), c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct)!);
    }

    private static async Task<IResult> Expiries(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var items = await db.QueryAsync("SELECT * FROM compliance_expiry_events WHERE company_id=@c AND pack_code='canada_na' AND retired_at IS NULL" + BranchScope(h) + " ORDER BY expiry_date NULLS LAST, id DESC", c => { c.Parameters.AddWithValue("@c", Company(h)); BindBranch(c, h); }, ct);
        return OkJson(new { items });
    }

    private static async Task<IResult> IftaReadiness(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var mileage = await db.QueryAsync("SELECT province_state, country, SUM(distance) distance, distance_unit, tax_period FROM jurisdiction_mileage_records WHERE company_id=@c" + BranchScope(h) + " GROUP BY province_state, country, distance_unit, tax_period ORDER BY tax_period DESC, province_state", c => { c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        var fuel = await db.QueryAsync("SELECT province_state, country, SUM(fuel_volume) fuel_volume, fuel_unit, tax_period FROM jurisdiction_fuel_records WHERE company_id=@c" + BranchScope(h) + " GROUP BY province_state, country, fuel_unit, tax_period ORDER BY tax_period DESC, province_state", c => { c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        return OkJson(new { workflowStatus = "preview", operable = false, mileageByJurisdiction = mileage, fuelByJurisdiction = fuel, note = "Preview only: records are not normalized, reconciled, or suitable for an official IFTA filing." });
    }

    private static async Task<IResult> CreateJurisdictionMileage(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        if (ValidateJurisdictionMileageInput(body) is { } validationError)
            return Results.BadRequest(ApiResponse<object>.Fail(validationError));
        var companyId = Company(h);
        var id = await db.InsertAsync("""
            INSERT INTO jurisdiction_mileage_records (company_id, branch_id, vehicle_id, vehicle_label, province_state, country, distance, distance_unit, tax_period)
            VALUES (@c,@branchId,@vid,@vl,@ps,@ctry,@dist,@unit,@period) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)Branch(h) ?? DBNull.Value);
                c.Parameters.AddWithValue("@vid", long.TryParse(Str(body, "vehicleId"), out var vid) ? vid : (object)DBNull.Value);
                c.Parameters.AddWithValue("@vl", (object?)NullIfEmpty(Str(body, "vehicleLabel")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@ps", Str(body, "provinceState"));
                c.Parameters.AddWithValue("@ctry", string.IsNullOrWhiteSpace(Str(body, "country")) ? "CA" : Str(body, "country"));
                c.Parameters.AddWithValue("@dist", decimal.TryParse(Str(body, "distance"), out var d) ? d : 0);
                c.Parameters.AddWithValue("@unit", string.IsNullOrWhiteSpace(Str(body, "distanceUnit")) ? "km" : Str(body, "distanceUnit"));
                c.Parameters.AddWithValue("@period", string.IsNullOrWhiteSpace(Str(body, "taxPeriod")) ? DateTime.UtcNow.ToString("yyyy-'Q'") + ((DateTime.UtcNow.Month - 1) / 3 + 1) : Str(body, "taxPeriod"));
            }, ct);
        await Ent(db).RecordAsync(companyId, "jurisdiction_mileage.monthly", 1, $"mileage:{id}", Actor(h), ct);
        return OkJson(new { id });
    }

    private static async Task<IResult> CreateJurisdictionFuel(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        if (ValidateJurisdictionFuelInput(body) is { } validationError)
            return Results.BadRequest(ApiResponse<object>.Fail(validationError));
        var companyId = Company(h);
        var id = await db.InsertAsync("""
            INSERT INTO jurisdiction_fuel_records (company_id, branch_id, vehicle_id, vehicle_label, province_state, country, fuel_volume, fuel_unit, tax_period)
            VALUES (@c,@branchId,@vid,@vl,@ps,@ctry,@vol,@unit,@period) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)Branch(h) ?? DBNull.Value);
                c.Parameters.AddWithValue("@vid", long.TryParse(Str(body, "vehicleId"), out var vid) ? vid : (object)DBNull.Value);
                c.Parameters.AddWithValue("@vl", (object?)NullIfEmpty(Str(body, "vehicleLabel")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@ps", Str(body, "provinceState"));
                c.Parameters.AddWithValue("@ctry", string.IsNullOrWhiteSpace(Str(body, "country")) ? "CA" : Str(body, "country"));
                c.Parameters.AddWithValue("@vol", decimal.TryParse(Str(body, "fuelVolume"), out var d) ? d : 0);
                c.Parameters.AddWithValue("@unit", string.IsNullOrWhiteSpace(Str(body, "fuelUnit")) ? "liter" : Str(body, "fuelUnit"));
                c.Parameters.AddWithValue("@period", string.IsNullOrWhiteSpace(Str(body, "taxPeriod")) ? DateTime.UtcNow.ToString("yyyy") : Str(body, "taxPeriod"));
            }, ct);
        await Ent(db).RecordAsync(companyId, "jurisdiction_fuel.monthly", 1, $"fuel:{id}", Actor(h), ct);
        return OkJson(new { id });
    }

    private static async Task<IResult> HosReadiness(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var duty = await db.QueryAsync("SELECT * FROM driver_duty_status_records WHERE company_id=@c" + BranchScope(h) + " ORDER BY recorded_at DESC LIMIT 50", c => { c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        var eld = await db.QueryAsync("SELECT * FROM eld_device_registry WHERE company_id=@c" + BranchScope(h) + " ORDER BY id", c => { c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        return OkJson(new { workflowStatus = "preview", operable = false, dutyStatusRecords = duty, eldDevices = eld, note = "Preview only: no certified ELD provider is connected, so this is not an operable HOS compliance workflow." });
    }

    // ════════════════════════════ Saudi compliance ══════════════════════════

    private static async Task<IResult> SaudiRegions(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var rows = await db.QueryAsync("SELECT code, name_en, name_ar, country_code, is_gcc_ready, cities_json FROM fleet_tms_saudi_regions ORDER BY sort_order, name_en", ct: ct);
        return OkJson(new { items = rows });
    }

    private static async Task<IResult> SaudiCities(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var rows = await db.QueryAsync("SELECT code, name_en, cities_json FROM fleet_tms_saudi_regions ORDER BY sort_order", ct: ct);
        return OkJson(new { items = rows });
    }

    private static async Task<IResult> SaudiDocuments(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var items = await db.QueryAsync("SELECT * FROM compliance_records WHERE company_id=@c AND pack_code='saudi_gcc'" + BranchScope(h) + " ORDER BY expiry_date NULLS LAST, id DESC", c => { c.Parameters.AddWithValue("@c", Company(h)); BindBranch(c, h); }, ct);
        ApplyLiveExpiry(items);
        return OkJson(new { items });
    }

    private static async Task<IResult> CreateSaudiDocument(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var companyId = Company(h);
        if (string.IsNullOrWhiteSpace(Str(body, "subjectName"))) return Results.BadRequest(ApiResponse<object>.Fail("Subject name is required."));
        if (InvalidDate(body, "gregorianExpiryDate")) return Results.BadRequest(ApiResponse<object>.Fail("Gregorian expiry date is malformed."));
        var hijriText = Str(body, "hijriExpiryDate").Trim();
        if (!string.IsNullOrWhiteSpace(hijriText) && !ValidHijriText(hijriText)) return Results.BadRequest(ApiResponse<object>.Fail("Hijri expiry date must use YYYY-MM-DD."));
        if (!string.IsNullOrWhiteSpace(hijriText) && HijriToGregorian(hijriText) is null) return Results.BadRequest(ApiResponse<object>.Fail("Hijri expiry date is not a valid calendar date."));
        if (DateOf(body, "gregorianExpiryDate") is null && string.IsNullOrWhiteSpace(hijriText)) return Results.BadRequest(ApiResponse<object>.Fail("A Gregorian or Hijri expiry date is required."));
        var expiry = DateOf(body, "gregorianExpiryDate") ?? HijriToGregorian(hijriText);
        var status = ExpiryStatus(expiry);
        var id = await db.InsertAsync("""
            INSERT INTO compliance_records (company_id, branch_id, pack_code, subject_type, subject_name, doc_key, document_no, document_status, expiry_date, hijri_expiry_date, metadata)
            VALUES (@c,@branchId,'saudi_gcc',@st,@sn,@dk,@dn,@status,@expiry,@hijri,@meta::jsonb) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)Branch(h) ?? DBNull.Value);
                c.Parameters.AddWithValue("@st", string.IsNullOrWhiteSpace(Str(body, "subjectType")) ? "transport" : Str(body, "subjectType"));
                c.Parameters.AddWithValue("@sn", (object?)NullIfEmpty(Str(body, "subjectName")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@dk", string.IsNullOrWhiteSpace(Str(body, "documentType")) ? "transport_permit" : Str(body, "documentType"));
                c.Parameters.AddWithValue("@dn", (object?)NullIfEmpty(Str(body, "transportDocumentNo")) ?? (object?)NullIfEmpty(Str(body, "permitNo")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@expiry", (object?)expiry ?? DBNull.Value);
                c.Parameters.AddWithValue("@hijri", (object?)NullIfEmpty(Str(body, "hijriExpiryDate")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@meta", BuildMeta(body, "permitNo", "documentStatus"));
            }, ct);
        await Ent(db).RecordAsync(companyId, "compliance_documents.count", 1, $"record:{id}", Actor(h), ct);
        await MaybeRaiseExpiry(db, companyId, Branch(h), "saudi_gcc", id, Str(body, "subjectType"), Str(body, "subjectName"), Str(body, "documentType"), expiry, status, ct);
        return OkJson(await ById(db, h, id, ct));
    }

    private static async Task<IResult> UpdateSaudiDocument(long id, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        if (InvalidDate(body, "gregorianExpiryDate")) return Results.BadRequest(ApiResponse<object>.Fail("Gregorian expiry date is malformed."));
        if (!string.IsNullOrWhiteSpace(Str(body, "hijriExpiryDate")) && !ValidHijriText(Str(body, "hijriExpiryDate").Trim())) return Results.BadRequest(ApiResponse<object>.Fail("Hijri expiry date must use YYYY-MM-DD."));
        if (!string.IsNullOrWhiteSpace(Str(body, "hijriExpiryDate")) && HijriToGregorian(Str(body, "hijriExpiryDate").Trim()) is null) return Results.BadRequest(ApiResponse<object>.Fail("Hijri expiry date is not a valid calendar date."));
        var companyId = Company(h);
        var existing = await db.QuerySingleAsync("SELECT * FROM compliance_records WHERE id=@id AND company_id=@c AND pack_code='saudi_gcc'" + BranchScope(h),
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        if (existing is null) return Results.NotFound(ApiResponse<object>.Fail("Document not found"));
        var existingHijri = existing.GetValueOrDefault("hijriExpiryDate")?.ToString();
        var requestedHijri = NullIfEmpty(Str(body, "hijriExpiryDate")) ?? existingHijri;
        var expiry = DateOf(body, "gregorianExpiryDate") ?? (existing["expiryDate"] is DateTime currentExpiry ? (DateTime?)currentExpiry : null) ?? HijriToGregorian(requestedHijri);
        var status = ExpiryStatus(expiry);
        var rows = await db.ExecuteAsync($"""
            UPDATE compliance_records SET document_no=COALESCE(NULLIF(@dn,''),document_no), document_status=@status,
                expiry_date=COALESCE(@expiry,expiry_date), hijri_expiry_date=COALESCE(NULLIF(@hijri,''),hijri_expiry_date), updated_at=NOW()
            WHERE id=@id AND company_id=@c AND pack_code='saudi_gcc'{BranchScope(h)}
            """,
            c => { c.Parameters.AddWithValue("@dn", Str(body, "transportDocumentNo")); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@expiry", (object?)expiry ?? DBNull.Value); c.Parameters.AddWithValue("@hijri", Str(body, "hijriExpiryDate")); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); BindBranch(c, h); }, ct);
        if (rows == 0) return Results.NotFound(ApiResponse<object>.Fail("Document not found"));
        await RefreshExpiry(db, companyId, Branch(h), "saudi_gcc", id,
            existing.GetValueOrDefault("subjectType")?.ToString() ?? "", existing.GetValueOrDefault("subjectName")?.ToString() ?? "",
            existing.GetValueOrDefault("docKey")?.ToString() ?? "", expiry, status, ct);
        return OkJson(await ById(db, h, id, ct));
    }

    private static async Task<IResult> SaudiExpiries(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var records = await db.QueryAsync("SELECT * FROM compliance_records WHERE company_id=@c AND pack_code='saudi_gcc'" + BranchScope(h) + " ORDER BY expiry_date NULLS LAST, id DESC", c => { c.Parameters.AddWithValue("@c", Company(h)); BindBranch(c, h); }, ct);
        ApplyLiveExpiry(records);
        await ReconcileSaudiExpiryEvents(db, h, records, ct);
        var items = records.Where(r => (r.GetValueOrDefault("documentStatus")?.ToString() ?? "unknown") != "valid").Select(r => new
        {
            recordId = r["id"], subjectType = r["subjectType"], subjectName = r["subjectName"], docKey = r["docKey"],
            severity = r["documentStatus"]?.ToString() == "expired" ? "critical" : r["documentStatus"]?.ToString() == "unknown" ? "needs_review" : "warning",
            message = r["documentStatus"]?.ToString() == "expired" ? "Document has expired." : r["documentStatus"]?.ToString() == "unknown" ? "Expiry is missing or cannot be converted; review required." : "Document expiring within 30 days.",
            expiryDate = r.GetValueOrDefault("effectiveExpiryDate"), expiryBasis = r.GetValueOrDefault("expiryBasis"), needsReview = r.GetValueOrDefault("needsReview"),
        }).ToList();
        return OkJson(new { generatedAtUtc = DateTime.UtcNow, items });
    }

    private static async Task ReconcileSaudiExpiryEvents(Database db, HttpContext h, List<Dictionary<string, object?>> records, CancellationToken ct)
    {
        foreach (var record in records)
        {
            var id = Convert.ToInt64(record["id"]);
            var status = record.GetValueOrDefault("documentStatus")?.ToString() ?? "unknown";
            var effective = record.GetValueOrDefault("effectiveExpiryDate") as DateTime?;
            var active = await db.QuerySingleAsync("SELECT severity,expiry_date FROM compliance_expiry_events WHERE company_id=@c AND branch_id IS NOT DISTINCT FROM @branchId AND pack_code='saudi_gcc' AND record_id=@id AND retired_at IS NULL ORDER BY id DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", Company(h)); c.Parameters.AddWithValue("@branchId", (object?)Branch(h) ?? DBNull.Value); c.Parameters.AddWithValue("@id", id); }, ct);
            var wantedSeverity = status == "expired" ? "critical" : status == "unknown" ? "needs_review" : status == "expiring" ? "warning" : null;
            var activeExpiry = active?.GetValueOrDefault("expiryDate") switch { DateTime d => d.Date, DateOnly d => d.ToDateTime(TimeOnly.MinValue), _ => (DateTime?)null };
            if (wantedSeverity is null)
            {
                if (active is not null) await RefreshExpiry(db, Company(h), Branch(h), "saudi_gcc", id, "", "", "", effective, "valid", ct);
                continue;
            }
            if (active?.GetValueOrDefault("severity")?.ToString() == wantedSeverity && activeExpiry == effective?.Date) continue;
            await RefreshExpiry(db, Company(h), Branch(h), "saudi_gcc", id,
                record.GetValueOrDefault("subjectType")?.ToString() ?? "", record.GetValueOrDefault("subjectName")?.ToString() ?? "",
                record.GetValueOrDefault("docKey")?.ToString() ?? "", effective, status, ct);
        }
    }

    private static async Task<IResult> SaudiVatReadiness(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var row = await ReadSaudiVatProfile(db, h, ct);
        return OkJson(new { readiness = row, note = "VAT / e-invoice readiness foundation — not an official ZATCA integration." });
    }

    private static async Task<IResult> SetSaudiVatReadiness(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var companyId = Company(h);
        var profileBranchId = await ResolveMarketBranch(db, h, ct);
        var vat = Str(body, "vatNumber").Trim();
        var cr = Str(body, "commercialRegistrationNo").Trim();
        var readinessStatus = string.IsNullOrWhiteSpace(Str(body, "eInvoiceReadinessStatus")) ? "not_ready" : Str(body, "eInvoiceReadinessStatus").Trim();
        var evidenceRecordId = long.TryParse(Str(body, "evidenceRecordId"), out var parsedEvidence) && parsedEvidence > 0 ? parsedEvidence : (long?)null;
        if (vat.Length > 40 || cr.Length > 40) return Results.BadRequest(ApiResponse<object>.Fail("VAT and commercial registration numbers cannot exceed 40 characters."));
        if (!Allowed(readinessStatus, "not_ready", "in_progress", "ready")) return Results.BadRequest(ApiResponse<object>.Fail("E-invoice readiness status is invalid."));
        var vatValid = System.Text.RegularExpressions.Regex.IsMatch(vat, @"^3\d{13}3$");
        var crValid = System.Text.RegularExpressions.Regex.IsMatch(cr, @"^\d{10}$");
        var evidenceValid = evidenceRecordId.HasValue && await IsValidVatEvidence(db, companyId, profileBranchId, evidenceRecordId.Value, ct);
        if (readinessStatus == "ready" && (!vatValid || !crValid || !evidenceValid))
            return Results.BadRequest(ApiResponse<object>.Fail("Ready status requires a valid 15-digit Saudi VAT number, 10-digit commercial registration, and active canonical evidence record."));
        var derivedStatus = vatValid && crValid && evidenceValid ? "ready" : (!string.IsNullOrWhiteSpace(vat) || !string.IsNullOrWhiteSpace(cr) || evidenceRecordId.HasValue) ? "in_progress" : "not_ready";
        await db.ExecuteAsync("""
            INSERT INTO business_tax_readiness (company_id, branch_id, pack_code, vat_number, commercial_registration_no, evidence_record_id, e_invoice_readiness_status, updated_by, updated_at)
            VALUES (@c,@branchId,'saudi_gcc',@vat,@cr,@evidence,@status,@by,NOW())
            ON CONFLICT DO NOTHING
            """,
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@branchId", (object?)profileBranchId ?? DBNull.Value); c.Parameters.AddWithValue("@vat", vat); c.Parameters.AddWithValue("@cr", cr); c.Parameters.AddWithValue("@evidence", (object?)evidenceRecordId ?? DBNull.Value); c.Parameters.AddWithValue("@status", derivedStatus); c.Parameters.AddWithValue("@by", Actor(h)); }, ct);
        await db.ExecuteAsync("""
            UPDATE business_tax_readiness SET vat_number=COALESCE(NULLIF(@vat,''),vat_number),
                commercial_registration_no=COALESCE(NULLIF(@cr,''),commercial_registration_no), evidence_record_id=COALESCE(@evidence,evidence_record_id),
                e_invoice_readiness_status=@status, updated_by=@by, updated_at=NOW()
            WHERE company_id=@c AND branch_id IS NOT DISTINCT FROM @branchId AND pack_code='saudi_gcc'
            """,
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@branchId", (object?)profileBranchId ?? DBNull.Value); c.Parameters.AddWithValue("@vat", vat); c.Parameters.AddWithValue("@cr", cr); c.Parameters.AddWithValue("@evidence", (object?)evidenceRecordId ?? DBNull.Value); c.Parameters.AddWithValue("@status", derivedStatus); c.Parameters.AddWithValue("@by", Actor(h)); }, ct);
        await Ent(db).RecordAsync(companyId, "compliance_expiry_alerts.monthly", 0, "vat_readiness_changed", Actor(h), ct); // event marker
        return OkJson((await ReadSaudiVatProfile(db, h, ct))!);
    }

    private static async Task<Dictionary<string, object?>?> ReadSaudiVatProfile(Database db, HttpContext h, CancellationToken ct)
    {
        var profileBranchId = await ResolveMarketBranch(db, h, ct);
        var row = await db.QuerySingleAsync(@"SELECT * FROM business_tax_readiness
WHERE company_id=@c AND pack_code='saudi_gcc' AND branch_id IS NOT DISTINCT FROM @branchId
ORDER BY id DESC LIMIT 1", c =>
        {
            c.Parameters.AddWithValue("@c", Company(h));
            c.Parameters.AddWithValue("@branchId", (object?)profileBranchId ?? DBNull.Value);
        }, ct);
        if (row is null) return null;
        var vatValid = System.Text.RegularExpressions.Regex.IsMatch(row.GetValueOrDefault("vatNumber")?.ToString() ?? "", @"^3\d{13}3$");
        var crValid = System.Text.RegularExpressions.Regex.IsMatch(row.GetValueOrDefault("commercialRegistrationNo")?.ToString() ?? "", @"^\d{10}$");
        var evidenceId = row.GetValueOrDefault("evidenceRecordId") is { } raw && raw is not DBNull ? Convert.ToInt64(raw) : 0;
        var evidenceValid = evidenceId > 0 && await IsValidVatEvidence(db, Company(h), profileBranchId, evidenceId, ct);
        row["eInvoiceReadinessStatus"] = vatValid && crValid && evidenceValid ? "ready" : (vatValid || crValid || evidenceId > 0) ? "in_progress" : "not_ready";
        row["vatNumberValid"] = vatValid; row["commercialRegistrationValid"] = crValid; row["evidenceValid"] = evidenceValid;
        return row;
    }

    private static async Task<long?> ResolveMarketBranch(Database db, HttpContext h, CancellationToken ct)
    {
        if (Branch(h) is { } claimedBranch) return claimedBranch;
        var active = await db.QueryAsync(@"SELECT id FROM branches
WHERE company_id=@c AND status='Active' AND deleted_at IS NULL
ORDER BY id LIMIT 2", c => c.Parameters.AddWithValue("@c", Company(h)), ct);
        return active.Count == 1 ? Convert.ToInt64(active[0]["id"]) : null;
    }

    private static async Task<bool> IsValidVatEvidence(Database db, long companyId, long? branchId, long evidenceId, CancellationToken ct)
    {
        var evidence = await db.QuerySingleAsync("SELECT * FROM compliance_records WHERE id=@id AND company_id=@c AND branch_id IS NOT DISTINCT FROM @branchId AND pack_code='saudi_gcc'",
            c => { c.Parameters.AddWithValue("@id", evidenceId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); }, ct);
        if (evidence is null) return false;
        ApplyLiveExpiry(new[] { evidence });
        return evidence.GetValueOrDefault("documentStatus")?.ToString() is "valid" or "expiring";
    }

    // ════════════════════════════ Platform Admin ════════════════════════════

    private static async Task<IResult> PlatformMarketPacks(HttpContext h, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(h, db, "platform:packages:view", ct);
        if (error is not null) return error;
        var items = await db.QueryAsync("SELECT * FROM market_packs ORDER BY name", ct: ct);
        return OkJson(new { items });
    }

    private static async Task<IResult> PlatformTenantMarketPacks(long tenantId, HttpContext h, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(h, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        var items = await db.QueryAsync("SELECT * FROM tenant_market_packs WHERE company_id=@c ORDER BY pack_code", c => c.Parameters.AddWithValue("@c", tenantId), ct);
        return OkJson(new { items });
    }

    internal static async Task<IResult> PlatformSetTenantMarketPack(long tenantId, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(h, db, "platform:packages:manage", ct);
        if (error is not null) return error;
        var packCode = Str(body, "packCode").Trim();
        if (string.IsNullOrWhiteSpace(packCode))
            return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", "packCode is required"));

        // This is a commercial control-plane enum, not free-form workflow state.
        // Keep the accepted wire values explicit so typos can never create a paid
        // assignment that the deny-by-default read gate silently treats as disabled.
        var status = string.IsNullOrWhiteSpace(Str(body, "status")) ? ActiveMarketPackStatus : Str(body, "status").Trim();
        if (status is not (ActiveMarketPackStatus or DisabledMarketPackStatus))
            return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", "status must be active or disabled"));

        long? priceOverride = null;
        if (body.TryGetValue("priceOverrideCents", out var rawPrice) && rawPrice is not null && !string.IsNullOrWhiteSpace(rawPrice.ToString()))
        {
            if (!long.TryParse(rawPrice.ToString(), out var parsedPrice) || parsedPrice < 0)
                return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", "priceOverrideCents must be a non-negative whole number"));
            priceOverride = parsedPrice;
        }

        var reason = Str(body, "reason").Trim();
        if (reason.Length > 500)
            return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", "reason cannot exceed 500 characters"));

        return await db.RunInSystemTransactionAsync<IResult>(async () =>
        {
            // Lock the tenant row to serialize concurrent commercial mutations and
            // validate both sides of the assignment before any ledger is changed.
            var tenant = await db.QuerySingleAsync(
                "SELECT id, company_code, name FROM companies WHERE id=@id FOR UPDATE",
                c => c.Parameters.AddWithValue("@id", tenantId), ct);
            if (tenant is null)
                return Results.Json(ApiResponse<object>.Fail("Not found", "Tenant not found"), statusCode: StatusCodes.Status404NotFound);

            var pack = await db.QuerySingleAsync(
                "SELECT code, name, status FROM market_packs WHERE code=@code",
                c => c.Parameters.AddWithValue("@code", packCode), ct);
            if (pack is null)
                return Results.Json(ApiResponse<object>.Fail("Not found", "Market pack not found"), statusCode: StatusCodes.Status404NotFound);
            var catalogStatus = pack["status"]?.ToString() ?? "";
            if (status == ActiveMarketPackStatus && catalogStatus != ActiveMarketPackStatus)
                return Results.Json(ApiResponse<object>.Fail("Validation failed", "Market pack is not active in the catalog"), statusCode: StatusCodes.Status409Conflict);

            var moduleKey = MarketPackSchemaService.ModuleKeyForPack(packCode);
            var beforeAssignment = await db.QuerySingleAsync(
                "SELECT id, status, price_override_cents, enabled_by, enabled_at, updated_at FROM tenant_market_packs WHERE company_id=@c AND pack_code=@p",
                c => { c.Parameters.AddWithValue("@c", tenantId); c.Parameters.AddWithValue("@p", packCode); }, ct);
            var beforeEntitlement = await db.QuerySingleAsync(
                "SELECT enabled, source, updated_by, updated_at FROM tenant_entitlements WHERE company_id=@c AND module_key=@m",
                c => { c.Parameters.AddWithValue("@c", tenantId); c.Parameters.AddWithValue("@m", moduleKey); }, ct);

            var assignmentId = await db.InsertAsync("""
                INSERT INTO tenant_market_packs (company_id, pack_code, status, price_override_cents, enabled_by, enabled_at, updated_at)
                VALUES (@c,@p,@s,@po,@by,NOW(),NOW())
                ON CONFLICT (company_id, pack_code) DO UPDATE SET
                    status=EXCLUDED.status,
                    price_override_cents=EXCLUDED.price_override_cents,
                    enabled_by=EXCLUDED.enabled_by,
                    updated_at=NOW()
                RETURNING id
                """,
                c => { c.Parameters.AddWithValue("@c", tenantId); c.Parameters.AddWithValue("@p", packCode); c.Parameters.AddWithValue("@s", status); c.Parameters.AddWithValue("@po", (object?)priceOverride ?? DBNull.Value); c.Parameters.AddWithValue("@by", principal!.Email); }, ct);

            var enabled = status == ActiveMarketPackStatus;
            await db.ExecuteAsync("""
                INSERT INTO tenant_entitlements (company_id, module_key, enabled, source, updated_by, updated_at)
                VALUES (@c,@m,@en,'market_pack',@by,NOW())
                ON CONFLICT (company_id, module_key) DO UPDATE SET enabled=EXCLUDED.enabled, source='market_pack', updated_by=EXCLUDED.updated_by, updated_at=NOW()
                """,
                c => { c.Parameters.AddWithValue("@c", tenantId); c.Parameters.AddWithValue("@m", moduleKey); c.Parameters.AddWithValue("@en", enabled); c.Parameters.AddWithValue("@by", principal!.Email); }, ct);

            await Ent(db).RecordAsync(tenantId, "market_packs.enabled", enabled ? 1 : 0, $"pack:{packCode}", principal!.Email, ct);

            // Platform audit is the immutable commercial mutation record. Usage
            // metering remains separate evidence and cannot substitute for it.
            await PlatformEndpoints.AuditAsync(db, principal!, h, "tenant.market_pack.changed", "MarketPackAssignment",
                assignmentId, tenantId, new
                {
                    reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
                    pack = new { code = packCode, name = pack["name"], catalogStatus },
                    moduleKey,
                    before = new { assignment = beforeAssignment, entitlement = beforeEntitlement },
                    after = new { assignment = new { id = assignmentId, status, priceOverrideCents = priceOverride, enabledBy = principal.Email }, entitlement = new { enabled, source = "market_pack", updatedBy = principal.Email } },
                }, ct);

            return OkJson(new { ok = true, packCode, status, moduleKey, auditRecorded = true });
        }, ct);
    }

    private static async Task<IResult> PlatformComplianceUsage(long tenantId, HttpContext h, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(h, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        var period = EntitlementService.CurrentPeriodKey();
        var counters = await db.QueryAsync("""
            SELECT meter_key, period_key, value FROM usage_counters
            WHERE company_id=@c AND meter_key IN ('compliance_documents.count','compliance_expiry_alerts.monthly','inspection_records.monthly','market_packs.enabled')
            """, c => c.Parameters.AddWithValue("@c", tenantId), ct);
        var docs = await db.ScalarLongAsync("SELECT COUNT(*) FROM compliance_records WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", tenantId), ct);
        var inspections = await db.ScalarLongAsync("SELECT COUNT(*) FROM vehicle_inspection_records WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", tenantId), ct);
        var expiries = await db.ScalarLongAsync("SELECT COUNT(*) FROM compliance_expiry_events WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", tenantId), ct);
        return OkJson(new { period, counters, totals = new { complianceDocuments = docs, inspections, expiryEvents = expiries } });
    }

    // ── shared ────────────────────────────────────────────────────────────
    private static async Task<Dictionary<string, object?>> ById(Database db, HttpContext h, long id, CancellationToken ct)
        => (await db.QuerySingleAsync("SELECT * FROM compliance_records WHERE id=@id AND company_id=@c" + BranchScope(h), c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", Company(h)); BindBranch(c, h); }, ct))!;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string BuildMeta(Dictionary<string, object?> body, params string[] keys)
    {
        var pairs = keys
            .Select(k => (k, v: body.TryGetValue(k, out var val) ? val?.ToString() : null))
            .Where(p => !string.IsNullOrWhiteSpace(p.v))
            .Select(p => $"\"{p.k}\":\"{p.v!.Replace("\"", "'")}\"");
        return "{" + string.Join(",", pairs) + "}";
    }

    private static async Task MaybeRaiseExpiry(Database db, long companyId, long? branchId, string pack, long recordId, string subjType, string subjName, string docKey, DateTime? expiry, string status, CancellationToken ct)
    {
        if (status is not ("expiring" or "expired" or "unknown")) return;
        await db.ExecuteAsync("""
            INSERT INTO compliance_expiry_events (company_id, branch_id, pack_code, record_id, subject_type, subject_name, doc_key, severity, message, expiry_date)
            VALUES (@c,@branchId,@p,@rid,@st,@sn,@dk,@sev,@msg,@exp)
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@rid", recordId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                c.Parameters.AddWithValue("@st", (object?)NullIfEmpty(subjType) ?? DBNull.Value);
                c.Parameters.AddWithValue("@sn", (object?)NullIfEmpty(subjName) ?? DBNull.Value);
                c.Parameters.AddWithValue("@dk", (object?)NullIfEmpty(docKey) ?? DBNull.Value);
                c.Parameters.AddWithValue("@sev", status == "expired" ? "critical" : status == "unknown" ? "needs_review" : "warning");
                c.Parameters.AddWithValue("@msg", status == "expired" ? "Document has expired." : status == "unknown" ? "Expiry is missing or cannot be converted; review required." : "Document expiring within 30 days.");
                c.Parameters.AddWithValue("@exp", (object?)expiry ?? DBNull.Value);
            }, ct);
        await new EntitlementService(db).RecordAsync(companyId, "compliance_expiry_alerts.monthly", 1, $"record:{recordId}", "system", ct);
    }

    private static async Task RefreshExpiry(Database db, long companyId, long? branchId, string pack, long recordId, string subjType, string subjName, string docKey, DateTime? expiry, string status, CancellationToken ct)
    {
        await db.ExecuteAsync(
            "UPDATE compliance_expiry_events SET retired_at=NOW(), retirement_reason='Document renewed or expiry recalculated' WHERE company_id=@c AND branch_id IS NOT DISTINCT FROM @branchId AND pack_code=@pack AND record_id=@recordId AND retired_at IS NULL",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); c.Parameters.AddWithValue("@pack", pack); c.Parameters.AddWithValue("@recordId", recordId); }, ct);
        await MaybeRaiseExpiry(db, companyId, branchId, pack, recordId, subjType, subjName, docKey, expiry, status, ct);
    }
}
