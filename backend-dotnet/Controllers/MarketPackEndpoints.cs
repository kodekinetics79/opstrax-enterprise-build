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

    private static string ExpiryStatus(DateTime? expiry)
    {
        if (expiry is null) return "valid";
        var days = (expiry.Value.Date - DateTime.UtcNow.Date).TotalDays;
        return days < 0 ? "expired" : days <= 30 ? "expiring" : "valid";
    }

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
            "SELECT * FROM compliance_records WHERE company_id=@c AND pack_code='canada_na' AND subject_type IN ('driver','vehicle') ORDER BY expiry_date NULLS LAST, id DESC",
            c => c.Parameters.AddWithValue("@c", Company(h)), ct);
        return OkJson(new { items });
    }

    private static async Task<IResult> CreateDriverDocument(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var expiry = DateOf(body, "expiryDate");
        var status = ExpiryStatus(expiry);
        var id = await db.InsertAsync("""
            INSERT INTO compliance_records (company_id, pack_code, subject_type, subject_id, subject_name, doc_key, document_no, document_status, issuing_region, issuing_country, issue_date, expiry_date, metadata)
            VALUES (@c,'canada_na',@st,@sid,@sn,@dk,@dn,@status,@reg,@ctry,@issue,@expiry,@meta::jsonb) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
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
        await MaybeRaiseExpiry(db, companyId, "canada_na", id, Str(body, "subjectType"), Str(body, "subjectName"), Str(body, "docKey"), expiry, status, ct);
        return OkJson(await ById(db, companyId, id, ct));
    }

    private static async Task<IResult> UpdateDriverDocument(long id, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var expiry = DateOf(body, "expiryDate");
        var rows = await db.ExecuteAsync("""
            UPDATE compliance_records SET
                document_no = COALESCE(NULLIF(@dn,''), document_no),
                document_status = @status,
                expiry_date = COALESCE(@expiry, expiry_date),
                updated_at = NOW()
            WHERE id=@id AND company_id=@c AND pack_code='canada_na'
            """,
            c => { c.Parameters.AddWithValue("@dn", Str(body, "documentNo")); c.Parameters.AddWithValue("@status", ExpiryStatus(expiry)); c.Parameters.AddWithValue("@expiry", (object?)expiry ?? DBNull.Value); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); }, ct);
        if (rows == 0) return Results.NotFound(ApiResponse<object>.Fail("Document not found"));
        return OkJson(await ById(db, companyId, id, ct));
    }

    private static async Task<IResult> VehicleInspections(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var items = await db.QueryAsync("SELECT * FROM vehicle_inspection_records WHERE company_id=@c ORDER BY inspected_at DESC", c => c.Parameters.AddWithValue("@c", companyId), ct);
        var defects = await db.QueryAsync("SELECT * FROM inspection_defects WHERE company_id=@c ORDER BY id", c => c.Parameters.AddWithValue("@c", companyId), ct);
        return OkJson(new { items, defects });
    }

    private static async Task<IResult> CreateVehicleInspection(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var id = await db.InsertAsync("""
            INSERT INTO vehicle_inspection_records (company_id, template_key, vehicle_id, vehicle_label, inspector_name, inspection_type, status, notes)
            VALUES (@c,@tk,@vid,@vl,@insp,@type,@status,@notes) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@tk", (object?)NullIfEmpty(Str(body, "templateKey")) ?? "dvir_pre_trip");
                c.Parameters.AddWithValue("@vid", long.TryParse(Str(body, "vehicleId"), out var vid) ? vid : (object)DBNull.Value);
                c.Parameters.AddWithValue("@vl", (object?)NullIfEmpty(Str(body, "vehicleLabel")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@insp", (object?)NullIfEmpty(Str(body, "inspectorName")) ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", string.IsNullOrWhiteSpace(Str(body, "inspectionType")) ? "pre_trip" : Str(body, "inspectionType"));
                c.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(Str(body, "status")) ? "pass" : Str(body, "status"));
                c.Parameters.AddWithValue("@notes", (object?)NullIfEmpty(Str(body, "notes")) ?? DBNull.Value);
            }, ct);

        // Optional defects array
        if (body.TryGetValue("defects", out var defectsRaw) && defectsRaw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var d in je.EnumerateArray())
            {
                await db.ExecuteAsync("""
                    INSERT INTO inspection_defects (company_id, inspection_id, item_key, description, defect_severity, repair_required)
                    VALUES (@c,@iid,@ik,@desc,@sev,@rep)
                    """,
                    c =>
                    {
                        c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@iid", id);
                        c.Parameters.AddWithValue("@ik", (object?)(d.TryGetProperty("itemKey", out var ik) ? ik.GetString() : null) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@desc", (d.TryGetProperty("description", out var de) ? de.GetString() : null) ?? "Defect");
                        c.Parameters.AddWithValue("@sev", (d.TryGetProperty("severity", out var sv) ? sv.GetString() : null) ?? "minor");
                        c.Parameters.AddWithValue("@rep", d.TryGetProperty("repairRequired", out var rr) && rr.ValueKind == System.Text.Json.JsonValueKind.True);
                    }, ct);
            }
        }

        await Ent(db).RecordAsync(companyId, "inspection_records.monthly", 1, $"inspection:{id}", Actor(h), ct);
        return OkJson(await db.QuerySingleAsync("SELECT * FROM vehicle_inspection_records WHERE id=@id AND company_id=@c", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); }, ct)!);
    }

    private static async Task<IResult> UpdateVehicleInspection(long id, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var rows = await db.ExecuteAsync(
            "UPDATE vehicle_inspection_records SET status=COALESCE(NULLIF(@status,''),status), notes=COALESCE(NULLIF(@notes,''),notes) WHERE id=@id AND company_id=@c",
            c => { c.Parameters.AddWithValue("@status", Str(body, "status")); c.Parameters.AddWithValue("@notes", Str(body, "notes")); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); }, ct);
        if (rows == 0) return Results.NotFound(ApiResponse<object>.Fail("Inspection not found"));
        return OkJson(await db.QuerySingleAsync("SELECT * FROM vehicle_inspection_records WHERE id=@id AND company_id=@c", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); }, ct)!);
    }

    private static async Task<IResult> Expiries(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var items = await db.QueryAsync("SELECT * FROM compliance_expiry_events WHERE company_id=@c AND pack_code='canada_na' ORDER BY expiry_date NULLS LAST, id DESC", c => c.Parameters.AddWithValue("@c", Company(h)), ct);
        return OkJson(new { items });
    }

    private static async Task<IResult> IftaReadiness(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var mileage = await db.QueryAsync("SELECT province_state, country, SUM(distance) distance, distance_unit, tax_period FROM jurisdiction_mileage_records WHERE company_id=@c GROUP BY province_state, country, distance_unit, tax_period ORDER BY tax_period DESC, province_state", c => c.Parameters.AddWithValue("@c", companyId), ct);
        var fuel = await db.QueryAsync("SELECT province_state, country, SUM(fuel_volume) fuel_volume, fuel_unit, tax_period FROM jurisdiction_fuel_records WHERE company_id=@c GROUP BY province_state, country, fuel_unit, tax_period ORDER BY tax_period DESC, province_state", c => c.Parameters.AddWithValue("@c", companyId), ct);
        return OkJson(new { mileageByJurisdiction = mileage, fuelByJurisdiction = fuel, note = "IFTA readiness foundation — not an official filing." });
    }

    private static async Task<IResult> CreateJurisdictionMileage(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.CanadaNa, ct) is { } block) return block;
        var companyId = Company(h);
        var id = await db.InsertAsync("""
            INSERT INTO jurisdiction_mileage_records (company_id, vehicle_id, vehicle_label, province_state, country, distance, distance_unit, tax_period)
            VALUES (@c,@vid,@vl,@ps,@ctry,@dist,@unit,@period) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
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
        var companyId = Company(h);
        var id = await db.InsertAsync("""
            INSERT INTO jurisdiction_fuel_records (company_id, vehicle_id, vehicle_label, province_state, country, fuel_volume, fuel_unit, tax_period)
            VALUES (@c,@vid,@vl,@ps,@ctry,@vol,@unit,@period) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
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
        var duty = await db.QueryAsync("SELECT * FROM driver_duty_status_records WHERE company_id=@c ORDER BY recorded_at DESC LIMIT 50", c => c.Parameters.AddWithValue("@c", companyId), ct);
        var eld = await db.QueryAsync("SELECT * FROM eld_device_registry WHERE company_id=@c ORDER BY id", c => c.Parameters.AddWithValue("@c", companyId), ct);
        return OkJson(new { dutyStatusRecords = duty, eldDevices = eld, note = "HOS/ELD readiness foundation — no certified ELD provider is connected." });
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
        var items = await db.QueryAsync("SELECT * FROM compliance_records WHERE company_id=@c AND pack_code='saudi_gcc' ORDER BY expiry_date NULLS LAST, id DESC", c => c.Parameters.AddWithValue("@c", Company(h)), ct);
        return OkJson(new { items });
    }

    private static async Task<IResult> CreateSaudiDocument(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var companyId = Company(h);
        var expiry = DateOf(body, "gregorianExpiryDate");
        var status = ExpiryStatus(expiry);
        var id = await db.InsertAsync("""
            INSERT INTO compliance_records (company_id, pack_code, subject_type, subject_name, doc_key, document_no, document_status, expiry_date, hijri_expiry_date, metadata)
            VALUES (@c,'saudi_gcc',@st,@sn,@dk,@dn,@status,@expiry,@hijri,@meta::jsonb) RETURNING id
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
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
        await MaybeRaiseExpiry(db, companyId, "saudi_gcc", id, Str(body, "subjectType"), Str(body, "subjectName"), Str(body, "documentType"), expiry, status, ct);
        return OkJson(await ById(db, companyId, id, ct));
    }

    private static async Task<IResult> UpdateSaudiDocument(long id, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var companyId = Company(h);
        var expiry = DateOf(body, "gregorianExpiryDate");
        var rows = await db.ExecuteAsync("""
            UPDATE compliance_records SET document_no=COALESCE(NULLIF(@dn,''),document_no), document_status=@status,
                expiry_date=COALESCE(@expiry,expiry_date), hijri_expiry_date=COALESCE(NULLIF(@hijri,''),hijri_expiry_date), updated_at=NOW()
            WHERE id=@id AND company_id=@c AND pack_code='saudi_gcc'
            """,
            c => { c.Parameters.AddWithValue("@dn", Str(body, "transportDocumentNo")); c.Parameters.AddWithValue("@status", ExpiryStatus(expiry)); c.Parameters.AddWithValue("@expiry", (object?)expiry ?? DBNull.Value); c.Parameters.AddWithValue("@hijri", Str(body, "hijriExpiryDate")); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); }, ct);
        if (rows == 0) return Results.NotFound(ApiResponse<object>.Fail("Document not found"));
        return OkJson(await ById(db, companyId, id, ct));
    }

    private static async Task<IResult> SaudiExpiries(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var items = await db.QueryAsync("SELECT * FROM compliance_expiry_events WHERE company_id=@c AND pack_code='saudi_gcc' ORDER BY expiry_date NULLS LAST, id DESC", c => c.Parameters.AddWithValue("@c", Company(h)), ct);
        return OkJson(new { items });
    }

    private static async Task<IResult> SaudiVatReadiness(HttpContext h, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:view");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var row = await db.QuerySingleAsync("SELECT * FROM business_tax_readiness WHERE company_id=@c AND pack_code='saudi_gcc'", c => c.Parameters.AddWithValue("@c", Company(h)), ct);
        return OkJson(new { readiness = row, note = "VAT / e-invoice readiness foundation — not an official ZATCA integration." });
    }

    private static async Task<IResult> SetSaudiVatReadiness(HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(h, "compliance:manage");
        if (denied is not null) return denied;
        if (await RequirePack(h, db, MarketPackSchemaService.Packs.SaudiGcc, ct) is { } block) return block;
        var companyId = Company(h);
        await db.ExecuteAsync("""
            INSERT INTO business_tax_readiness (company_id, pack_code, vat_number, commercial_registration_no, e_invoice_readiness_status, updated_by, updated_at)
            VALUES (@c,'saudi_gcc',@vat,@cr,@status,@by,NOW())
            ON CONFLICT (company_id, pack_code) DO UPDATE SET vat_number=COALESCE(NULLIF(EXCLUDED.vat_number,''),business_tax_readiness.vat_number),
                commercial_registration_no=COALESCE(NULLIF(EXCLUDED.commercial_registration_no,''),business_tax_readiness.commercial_registration_no),
                e_invoice_readiness_status=EXCLUDED.e_invoice_readiness_status, updated_by=EXCLUDED.updated_by, updated_at=NOW()
            """,
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@vat", Str(body, "vatNumber")); c.Parameters.AddWithValue("@cr", Str(body, "commercialRegistrationNo")); c.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(Str(body, "eInvoiceReadinessStatus")) ? "not_ready" : Str(body, "eInvoiceReadinessStatus")); c.Parameters.AddWithValue("@by", Actor(h)); }, ct);
        await Ent(db).RecordAsync(companyId, "compliance_expiry_alerts.monthly", 0, "vat_readiness_changed", Actor(h), ct); // event marker
        return OkJson(await db.QuerySingleAsync("SELECT * FROM business_tax_readiness WHERE company_id=@c AND pack_code='saudi_gcc'", c => c.Parameters.AddWithValue("@c", companyId), ct)!);
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

    private static async Task<IResult> PlatformSetTenantMarketPack(long tenantId, HttpContext h, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(h, db, "platform:packages:manage", ct);
        if (error is not null) return error;
        var packCode = Str(body, "packCode");
        if (string.IsNullOrWhiteSpace(packCode)) return Results.BadRequest(ApiResponse<object>.Fail("packCode is required"));
        var status = string.IsNullOrWhiteSpace(Str(body, "status")) ? "active" : Str(body, "status");
        long? priceOverride = long.TryParse(Str(body, "priceOverrideCents"), out var po) ? po : null;
        await db.ExecuteAsync("""
            INSERT INTO tenant_market_packs (company_id, pack_code, status, price_override_cents, enabled_by, enabled_at, updated_at)
            VALUES (@c,@p,@s,@po,@by,NOW(),NOW())
            ON CONFLICT (company_id, pack_code) DO UPDATE SET status=EXCLUDED.status, price_override_cents=EXCLUDED.price_override_cents, enabled_by=EXCLUDED.enabled_by, updated_at=NOW()
            """,
            c => { c.Parameters.AddWithValue("@c", tenantId); c.Parameters.AddWithValue("@p", packCode); c.Parameters.AddWithValue("@s", status); c.Parameters.AddWithValue("@po", (object?)priceOverride ?? DBNull.Value); c.Parameters.AddWithValue("@by", principal!.Email); }, ct);

        // Mirror into tenant_entitlements so the feature module key is enabled/disabled.
        var moduleKey = packCode == MarketPackSchemaService.Packs.CanadaNa ? MarketPackSchemaService.Features.MarketCanadaNa : MarketPackSchemaService.Features.MarketSaudiGcc;
        await db.ExecuteAsync("""
            INSERT INTO tenant_entitlements (company_id, module_key, enabled, source, updated_by, updated_at)
            VALUES (@c,@m,@en,'market_pack',@by,NOW())
            ON CONFLICT (company_id, module_key) DO UPDATE SET enabled=EXCLUDED.enabled, source='market_pack', updated_by=EXCLUDED.updated_by, updated_at=NOW()
            """,
            c => { c.Parameters.AddWithValue("@c", tenantId); c.Parameters.AddWithValue("@m", moduleKey); c.Parameters.AddWithValue("@en", status == "active"); c.Parameters.AddWithValue("@by", principal!.Email); }, ct);

        await Ent(db).RecordAsync(tenantId, "market_packs.enabled", status == "active" ? 1 : 0, $"pack:{packCode}", principal!.Email, ct);
        return OkJson(new { ok = true, packCode, status });
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
    private static async Task<Dictionary<string, object?>> ById(Database db, long companyId, long id, CancellationToken ct)
        => (await db.QuerySingleAsync("SELECT * FROM compliance_records WHERE id=@id AND company_id=@c", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@c", companyId); }, ct))!;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string BuildMeta(Dictionary<string, object?> body, params string[] keys)
    {
        var pairs = keys
            .Select(k => (k, v: body.TryGetValue(k, out var val) ? val?.ToString() : null))
            .Where(p => !string.IsNullOrWhiteSpace(p.v))
            .Select(p => $"\"{p.k}\":\"{p.v!.Replace("\"", "'")}\"");
        return "{" + string.Join(",", pairs) + "}";
    }

    private static async Task MaybeRaiseExpiry(Database db, long companyId, string pack, long recordId, string subjType, string subjName, string docKey, DateTime? expiry, string status, CancellationToken ct)
    {
        if (status is not ("expiring" or "expired")) return;
        await db.ExecuteAsync("""
            INSERT INTO compliance_expiry_events (company_id, pack_code, record_id, subject_type, subject_name, doc_key, severity, message, expiry_date)
            VALUES (@c,@p,@rid,@st,@sn,@dk,@sev,@msg,@exp)
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@rid", recordId);
                c.Parameters.AddWithValue("@st", (object?)NullIfEmpty(subjType) ?? DBNull.Value);
                c.Parameters.AddWithValue("@sn", (object?)NullIfEmpty(subjName) ?? DBNull.Value);
                c.Parameters.AddWithValue("@dk", (object?)NullIfEmpty(docKey) ?? DBNull.Value);
                c.Parameters.AddWithValue("@sev", status == "expired" ? "critical" : "warning");
                c.Parameters.AddWithValue("@msg", status == "expired" ? "Document has expired." : "Document expiring within 30 days.");
                c.Parameters.AddWithValue("@exp", (object?)expiry ?? DBNull.Value);
            }, ct);
        await new EntitlementService(db).RecordAsync(companyId, "compliance_expiry_alerts.monthly", 1, $"record:{recordId}", "system", ct);
    }
}
