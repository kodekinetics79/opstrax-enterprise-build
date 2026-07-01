using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Real-Postgres proof for the country-profile structure + tenant-creation cascade.
// No mocks, no seed fallbacks — every assertion reads back rows the code actually
// wrote to :5433. Named *PostgresTests so the CI unit filter excludes it, matching
// the other DB-backed integration tests.
//
// Covers:
//   STEP 1c — the two seeded profiles (SA, CA) return exact expected field values.
//   STEP 2c — cascade populates currency + auto-enabled entitlements per country,
//             and a post-creation override persists (defaults-not-locks).
public class CountryProfilePostgresTests
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    // ── STEP 1c: seeded profiles return exact expected values ────────────────────
    [Fact]
    public async Task Seeded_Profiles_SA_And_CA_Return_Exact_Expected_Values()
    {
        var db = CreateDatabase();
        await new CountryProfileSchemaService(db).EnsureAsync();
        var svc = new CountryProfileService(db);

        var sa = await svc.GetAsync("SA");
        Assert.NotNull(sa);
        Assert.Equal("SA", sa!.CountryCode);
        Assert.Equal("Saudi Arabia", sa.CountryName);
        Assert.Equal("SAR", sa.DefaultCurrency);
        Assert.Equal("ar-SA", sa.DefaultLocale);
        Assert.Equal("rtl", sa.TextDirection);
        Assert.Equal("gregorian_hijri_dual", sa.CalendarSystem);
        Assert.Equal("zatca_phase2", sa.InvoicingScheme);
        Assert.Equal(new[] { "zatca_invoicing", "hijri_calendar_toggle", "arabic_rtl" }, sa.AutoEnabledFeatures.ToArray());

        var ca = await svc.GetAsync("CA");
        Assert.NotNull(ca);
        Assert.Equal("CA", ca!.CountryCode);
        Assert.Equal("Canada", ca.CountryName);
        Assert.Equal("CAD", ca.DefaultCurrency);
        Assert.Equal("en-CA", ca.DefaultLocale);
        Assert.Equal("ltr", ca.TextDirection);
        Assert.Equal("gregorian", ca.CalendarSystem);
        Assert.Equal("standard", ca.InvoicingScheme);
        Assert.Empty(ca.AutoEnabledFeatures);

        // Case-insensitive lookup — the cascade normalizes country codes.
        Assert.NotNull(await svc.GetAsync("sa"));
    }

    // ── STEP 1b: profiles are manageable via CRUD (no code deploy for new country) ─
    [Fact]
    public async Task Upsert_And_Delete_A_New_Country_Profile_Roundtrips()
    {
        var db = CreateDatabase();
        await new CountryProfileSchemaService(db).EnsureAsync();
        var svc = new CountryProfileService(db);

        var draft = new CountryProfileService.CountryProfile(
            "AE", "United Arab Emirates", "AED", "ar-AE", "rtl",
            "gregorian_hijri_dual", "fta_einvoicing", "TRN", 0.0500m,
            "UAE data residency note", new[] { "arabic_rtl" });

        try
        {
            var saved = await svc.UpsertAsync(draft);
            Assert.Equal("AE", saved.CountryCode);
            Assert.Equal("AED", saved.DefaultCurrency);
            Assert.Equal(new[] { "arabic_rtl" }, saved.AutoEnabledFeatures.ToArray());

            // Update in place — currency change persists.
            var updated = await svc.UpsertAsync(saved with { DefaultCurrency = "USD" });
            Assert.Equal("USD", (await svc.GetAsync("AE"))!.DefaultCurrency);
            Assert.Equal("USD", updated.DefaultCurrency);
        }
        finally
        {
            Assert.True(await svc.DeleteAsync("AE"));
            Assert.Null(await svc.GetAsync("AE"));
        }
    }

    // ── STEP 2c: SA cascade enables the three features + SAR currency ─────────────
    [Fact]
    public async Task Create_SA_Tenant_Cascades_SAR_Currency_And_Three_Entitlements()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        await new CountryProfileSchemaService(db).EnsureAsync();
        var svc = new CountryProfileService(db);
        var companyCode = $"CP-SA-{NextCompanyId()}";

        try
        {
            var companyId = await CreateTenantWithSubscriptionAsync(db, companyCode, "USD");

            var cascade = await svc.ApplyToTenantAsync(companyId, "SA", "test@opstrax.io");
            Assert.NotNull(cascade);
            Assert.Equal("SA", cascade!.CountryCode);
            Assert.Equal("SAR", cascade.Currency);

            // companies row carries resolved defaults
            var company = await db.QuerySingleAsync(
                "SELECT country, currency, timezone FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.Equal("SA", company!["country"]);
            Assert.Equal("SAR", company["currency"]);
            Assert.Equal("Asia/Riyadh", company["timezone"]);

            // subscription billing currency mirrored
            var subCurrency = await db.QuerySingleAsync(
                "SELECT billing_currency FROM tenant_subscriptions WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.Equal("SAR", subCurrency!["billingCurrency"]);

            var enabled = await EnabledEntitlementsAsync(db, companyId);
            Assert.Contains("zatca_invoicing", enabled);
            Assert.Contains("hijri_calendar_toggle", enabled);
            Assert.Contains("arabic_rtl", enabled);

            // source is 'country' — proving these came from the cascade, not a package
            var source = await db.QuerySingleAsync(
                "SELECT source FROM tenant_entitlements WHERE company_id=@id AND module_key='zatca_invoicing'",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.Equal("country", source!["source"]);
        }
        finally
        {
            await CleanupAsync(db, companyCode);
        }
    }

    // ── STEP 2c: CA cascade → CAD, none of the SA-specific features present ───────
    [Fact]
    public async Task Create_CA_Tenant_Cascades_CAD_And_No_SA_Features()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        await new CountryProfileSchemaService(db).EnsureAsync();
        var svc = new CountryProfileService(db);
        var companyCode = $"CP-CA-{NextCompanyId()}";

        try
        {
            var companyId = await CreateTenantWithSubscriptionAsync(db, companyCode, "USD");

            var cascade = await svc.ApplyToTenantAsync(companyId, "CA", "test@opstrax.io");
            Assert.NotNull(cascade);
            Assert.Equal("CAD", cascade!.Currency);
            Assert.Empty(cascade.EnabledFeatures);

            var company = await db.QuerySingleAsync(
                "SELECT country, currency FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.Equal("CA", company!["country"]);
            Assert.Equal("CAD", company["currency"]);

            // No SA-specific entitlement rows exist for a CA tenant.
            var enabled = await EnabledEntitlementsAsync(db, companyId);
            Assert.DoesNotContain("zatca_invoicing", enabled);
            Assert.DoesNotContain("hijri_calendar_toggle", enabled);
            Assert.DoesNotContain("arabic_rtl", enabled);
        }
        finally
        {
            await CleanupAsync(db, companyCode);
        }
    }

    // ── STEP 2c: defaults-not-locks — an override toggling a country feature off
    //             persists, and re-running the cascade does NOT re-enable it ───────
    [Fact]
    public async Task Country_Defaults_Do_Not_Lock_Overrides_Persist()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        await new CountryProfileSchemaService(db).EnsureAsync();
        var svc = new CountryProfileService(db);
        var companyCode = $"CP-OV-{NextCompanyId()}";

        try
        {
            var companyId = await CreateTenantWithSubscriptionAsync(db, companyCode, "USD");
            await svc.ApplyToTenantAsync(companyId, "SA", "test@opstrax.io");
            Assert.Contains("zatca_invoicing", await EnabledEntitlementsAsync(db, companyId));

            // Platform admin explicitly turns the auto-enabled feature OFF (source='override').
            await db.ExecuteAsync(
                @"INSERT INTO tenant_entitlements (company_id, module_key, enabled, source, updated_by, updated_at)
                  VALUES (@cid, 'zatca_invoicing', false, 'override', 'admin@opstrax.io', NOW())
                  ON CONFLICT (company_id, module_key) DO UPDATE
                    SET enabled=false, source='override', updated_by='admin@opstrax.io', updated_at=NOW()",
                c => c.Parameters.AddWithValue("@cid", companyId));

            // Re-run the cascade (e.g. package reassignment) — the override must WIN.
            await svc.ApplyToTenantAsync(companyId, "SA", "test@opstrax.io");

            var row = await db.QuerySingleAsync(
                "SELECT enabled, source FROM tenant_entitlements WHERE company_id=@id AND module_key='zatca_invoicing'",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.Equal(false, row!["enabled"]);
            Assert.Equal("override", row["source"]);

            // Conversely: enabling a NON-default feature as an override persists too.
            await db.ExecuteAsync(
                @"INSERT INTO tenant_entitlements (company_id, module_key, enabled, source, updated_by, updated_at)
                  VALUES (@cid, 'custom_addon', true, 'override', 'admin@opstrax.io', NOW())",
                c => c.Parameters.AddWithValue("@cid", companyId));
            Assert.Contains("custom_addon", await EnabledEntitlementsAsync(db, companyId));
        }
        finally
        {
            await CleanupAsync(db, companyCode);
        }
    }

    // ── STEP 2: unknown country code yields no cascade (null result) ──────────────
    [Fact]
    public async Task Unknown_Country_Code_Returns_Null_Cascade()
    {
        var db = CreateDatabase();
        await new CountryProfileSchemaService(db).EnsureAsync();
        var svc = new CountryProfileService(db);
        Assert.Null(await svc.ApplyToTenantAsync(-1, "ZZ", "test@opstrax.io"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────
    private static async Task<long> CreateTenantWithSubscriptionAsync(Database db, string companyCode, string currency)
    {
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@code, @name, 'Logistics', 'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", companyCode);
                c.Parameters.AddWithValue("@name", $"Country Cascade {companyCode}");
            });

        await db.ExecuteAsync(
            @"INSERT INTO tenant_subscriptions (company_id, status, seat_limit, billing_currency, mrr_cents)
              VALUES (@cid, 'trial', 5, @cur, 0)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@cur", currency);
            });

        return companyId;
    }

    private static async Task<HashSet<string>> EnabledEntitlementsAsync(Database db, long companyId)
    {
        var rows = await db.QueryAsync(
            "SELECT module_key FROM tenant_entitlements WHERE company_id=@id AND enabled=true",
            c => c.Parameters.AddWithValue("@id", companyId));
        return rows.Select(r => r["moduleKey"]?.ToString() ?? "").ToHashSet();
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static long NextCompanyId() => Interlocked.Increment(ref _nextCompanyId);
    private static long _nextCompanyId = 92000;

    private static async Task CleanupAsync(Database db, string companyCode)
    {
        var companyId = await db.ScalarLongAsync("SELECT id FROM companies WHERE company_code=@code LIMIT 1",
            c => c.Parameters.AddWithValue("@code", companyCode));
        if (companyId <= 0) return;
        await db.ExecuteAsync("DELETE FROM tenant_entitlements WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        await db.ExecuteAsync("DELETE FROM tenant_subscriptions WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", companyId));
    }
}
