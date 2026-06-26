using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Seed;

// Demo data for the market packs. Gated behind the same ENABLE_FLEET_DEMO_SEED flag
// as FleetTmsSeeder — production never auto-assigns market packs or creates demo
// compliance rows. Reference data (packs, features, templates) is seeded
// unconditionally by MarketPackSchemaService; only tenant assignments + sample
// records live here. Idempotent: guards on existing rows per company.
public sealed class MarketPackSeeder(Database db, ILogger<MarketPackSeeder> log, IConfiguration configuration, IHostEnvironment environment)
{
    private bool DemoEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("ENABLE_FLEET_DEMO_SEED")
                  ?? configuration["Fleet:EnableDemoSeed"] ?? configuration["ENABLE_FLEET_DEMO_SEED"];
        if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw.Trim(), out var v)) return v;
        return environment.IsDevelopment();
    }

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (!DemoEnabled())
        {
            log.LogInformation("[MarketPackSeeder] demo seed skipped (ENABLE_FLEET_DEMO_SEED unset/false).");
            return;
        }

        var companies = await db.QueryAsync("SELECT id FROM companies WHERE status='Active' ORDER BY id LIMIT 2", ct: ct);
        if (companies.Count == 0) return;

        var canadaTenant = Convert.ToInt64(companies[0]["id"]);
        var saudiTenant = Convert.ToInt64(companies[Math.Min(1, companies.Count - 1)]["id"]);

        try
        {
            await AssignPack(canadaTenant, "canada_na", "market.canada_na", ct);
            await SeedCanada(canadaTenant, ct);
            await AssignPack(saudiTenant, "saudi_gcc", "market.saudi_gcc", ct);
            await SeedSaudi(saudiTenant, ct);
            log.LogInformation("[MarketPackSeeder] seeded market-pack demo data for tenants {A} (Canada) and {B} (Saudi)", canadaTenant, saudiTenant);
        }
        catch (Exception ex) { log.LogWarning(ex, "[MarketPackSeeder] demo seed failed"); }
    }

    private async Task AssignPack(long companyId, string pack, string moduleKey, CancellationToken ct)
    {
        await db.ExecuteAsync("""
            INSERT INTO tenant_market_packs (company_id, pack_code, status, enabled_by)
            VALUES (@c,@p,'active','seed') ON CONFLICT (company_id, pack_code) DO NOTHING
            """, c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", pack); }, ct);
        await db.ExecuteAsync("""
            INSERT INTO tenant_entitlements (company_id, module_key, enabled, source, updated_by)
            VALUES (@c,@m,true,'market_pack','seed') ON CONFLICT (company_id, module_key) DO UPDATE SET enabled=true, source='market_pack'
            """, c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@m", moduleKey); }, ct);
    }

    private async Task SeedCanada(long companyId, CancellationToken ct)
    {
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM compliance_records WHERE company_id=@c AND pack_code='canada_na'", c => c.Parameters.AddWithValue("@c", companyId), ct) > 0)
            return;

        // 3 driver qualification documents (one expiring soon, one valid, one vehicle doc)
        var docs = new (string Subj, string Name, string DocKey, string Region, int DaysToExpiry)[]
        {
            ("driver", "Jean Tremblay", "drivers_license", "QC", 120),
            ("driver", "Sarah Miller", "medical_certificate", "ON", 20),     // expiring
            ("vehicle", "Truck T-104", "annual_inspection", "NY", 200),
        };
        foreach (var d in docs)
        {
            var expiry = DateTime.UtcNow.Date.AddDays(d.DaysToExpiry);
            var status = d.DaysToExpiry < 0 ? "expired" : d.DaysToExpiry <= 30 ? "expiring" : "valid";
            var id = await db.InsertAsync("""
                INSERT INTO compliance_records (company_id, pack_code, subject_type, subject_name, doc_key, document_no, document_status, issuing_region, issuing_country, expiry_date)
                VALUES (@c,'canada_na',@st,@sn,@dk,@dn,@status,@reg,'CA',@exp) RETURNING id
                """, c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@st", d.Subj); c.Parameters.AddWithValue("@sn", d.Name); c.Parameters.AddWithValue("@dk", d.DocKey); c.Parameters.AddWithValue("@dn", $"DOC-{d.Region}-{Random.Shared.Next(1000, 9999)}"); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@reg", d.Region); c.Parameters.AddWithValue("@exp", expiry); }, ct);
            if (status is "expiring" or "expired")
                await db.ExecuteAsync("INSERT INTO compliance_expiry_events (company_id, pack_code, record_id, subject_type, subject_name, doc_key, severity, message, expiry_date) VALUES (@c,'canada_na',@r,@st,@sn,@dk,@sev,@msg,@exp)",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@r", id); c.Parameters.AddWithValue("@st", d.Subj); c.Parameters.AddWithValue("@sn", d.Name); c.Parameters.AddWithValue("@dk", d.DocKey); c.Parameters.AddWithValue("@sev", status == "expired" ? "critical" : "warning"); c.Parameters.AddWithValue("@msg", "Document expiring within 30 days."); c.Parameters.AddWithValue("@exp", expiry); }, ct);
        }

        // 4 inspection records (one with a defect)
        for (var i = 1; i <= 4; i++)
        {
            var status = i == 2 ? "conditional" : "pass";
            var inspId = await db.InsertAsync("INSERT INTO vehicle_inspection_records (company_id, template_key, vehicle_label, inspector_name, inspection_type, status) VALUES (@c,'dvir_pre_trip',@vl,@insp,'pre_trip',@s) RETURNING id",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@vl", $"Truck T-10{i}"); c.Parameters.AddWithValue("@insp", "M. Dupont"); c.Parameters.AddWithValue("@s", status); }, ct);
            if (i == 2)
                await db.ExecuteAsync("INSERT INTO inspection_defects (company_id, inspection_id, item_key, description, defect_severity, repair_required) VALUES (@c,@iid,'brakes','Brake pad wear beyond limit','major',true)",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@iid", inspId); }, ct);
        }

        // 5 jurisdiction mileage + 5 fuel records
        var jurisdictions = new[] { "QC", "ON", "NY", "VT", "ME" };
        var period = $"{DateTime.UtcNow:yyyy}-Q{(DateTime.UtcNow.Month - 1) / 3 + 1}";
        foreach (var j in jurisdictions)
        {
            var country = j is "NY" or "VT" or "ME" ? "US" : "CA";
            await db.ExecuteAsync("INSERT INTO jurisdiction_mileage_records (company_id, vehicle_label, province_state, country, distance, distance_unit, tax_period) VALUES (@c,'Truck T-101',@j,@ctry,@d,'km',@p)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", j); c.Parameters.AddWithValue("@ctry", country); c.Parameters.AddWithValue("@d", Random.Shared.Next(800, 3200)); c.Parameters.AddWithValue("@p", period); }, ct);
            await db.ExecuteAsync("INSERT INTO jurisdiction_fuel_records (company_id, vehicle_label, province_state, country, fuel_volume, fuel_unit, tax_period) VALUES (@c,'Truck T-101',@j,@ctry,@v,'liter',@p)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", j); c.Parameters.AddWithValue("@ctry", country); c.Parameters.AddWithValue("@v", Random.Shared.Next(200, 900)); c.Parameters.AddWithValue("@p", period); }, ct);
        }

        // 2 HOS readiness examples + 1 ELD placeholder (planned/not connected)
        foreach (var (driver, dutyStatus) in new[] { ("Jean Tremblay", "driving"), ("Sarah Miller", "on_duty") })
            await db.ExecuteAsync("INSERT INTO driver_duty_status_records (company_id, driver_name, duty_status, hos_cycle, log_certification_status) VALUES (@c,@d,@s,'cycle_1_70h','uncertified')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", driver); c.Parameters.AddWithValue("@s", dutyStatus); }, ct);
        await db.ExecuteAsync("INSERT INTO eld_device_registry (company_id, eld_provider_name, eld_connection_status, notes) VALUES (@c,'Generic ELD (placeholder)','planned','Not connected — readiness foundation only')",
            c => c.Parameters.AddWithValue("@c", companyId), ct);
    }

    private async Task SeedSaudi(long companyId, CancellationToken ct)
    {
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM compliance_records WHERE company_id=@c AND pack_code='saudi_gcc'", c => c.Parameters.AddWithValue("@c", companyId), ct) > 0)
            return;

        // 3 transport/compliance documents (one expiring) with Hijri/Gregorian
        var docs = new (string DocKey, string Name, int Days, string Hijri)[]
        {
            ("transport_permit", "Permit P-1001", 90, "1447-12-15"),
            ("operating_card", "Card OC-22", 15, "1447-09-01"),   // expiring
            ("istimara", "Reg IST-77", 240, "1448-05-20"),
        };
        foreach (var d in docs)
        {
            var expiry = DateTime.UtcNow.Date.AddDays(d.Days);
            var status = d.Days <= 30 ? "expiring" : "valid";
            var id = await db.InsertAsync("""
                INSERT INTO compliance_records (company_id, pack_code, subject_type, subject_name, doc_key, document_no, document_status, expiry_date, hijri_expiry_date)
                VALUES (@c,'saudi_gcc','transport',@sn,@dk,@dn,@status,@exp,@hijri) RETURNING id
                """, c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@sn", d.Name); c.Parameters.AddWithValue("@dk", d.DocKey); c.Parameters.AddWithValue("@dn", d.Name); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@exp", expiry); c.Parameters.AddWithValue("@hijri", d.Hijri); }, ct);
            if (status == "expiring")
                await db.ExecuteAsync("INSERT INTO compliance_expiry_events (company_id, pack_code, record_id, subject_type, subject_name, doc_key, severity, message, expiry_date) VALUES (@c,'saudi_gcc',@r,'transport',@sn,@dk,'warning','Document expiring within 30 days.',@exp)",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@r", id); c.Parameters.AddWithValue("@sn", d.Name); c.Parameters.AddWithValue("@dk", d.DocKey); c.Parameters.AddWithValue("@exp", expiry); }, ct);
        }

        // 3 Saudi National Address examples
        var addresses = new (string Bldg, string Add, string District, string City, string Region, string Postal)[]
        {
            ("8523", "2814", "Al Olaya", "Riyadh", "Riyadh", "12244"),
            ("4471", "9012", "Al Hamra", "Jeddah", "Makkah", "23324"),
            ("1190", "3344", "Al Faisaliyah", "Dammam", "Eastern Province", "32253"),
        };
        foreach (var a in addresses)
            await db.ExecuteAsync("""
                INSERT INTO market_addresses (company_id, pack_code, label, national_address_building_no, national_address_additional_no, district, city, region, postal_code, country)
                VALUES (@c,'saudi_gcc',@label,@b,@a,@d,@city,@r,@p,'SA')
                """, c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@label", $"{a.City} depot"); c.Parameters.AddWithValue("@b", a.Bldg); c.Parameters.AddWithValue("@a", a.Add); c.Parameters.AddWithValue("@d", a.District); c.Parameters.AddWithValue("@city", a.City); c.Parameters.AddWithValue("@r", a.Region); c.Parameters.AddWithValue("@p", a.Postal); }, ct);

        // 2 VAT readiness examples (one ready, one in progress) — single business row kept
        await db.ExecuteAsync("""
            INSERT INTO business_tax_readiness (company_id, pack_code, vat_number, commercial_registration_no, e_invoice_readiness_status, updated_by)
            VALUES (@c,'saudi_gcc','300000000000003','1010101010','in_progress','seed')
            ON CONFLICT (company_id, pack_code) DO NOTHING
            """, c => c.Parameters.AddWithValue("@c", companyId), ct);
    }
}
