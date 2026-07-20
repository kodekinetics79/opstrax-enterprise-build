using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// COUNTRY PROFILE SERVICE — reusable read/write + tenant cascade.
//
// Kept independent of HttpContext so the exact cascade a platform admin triggers
// during tenant creation is provable by a real Postgres integration test with no
// web host. PlatformEndpoints delegates here; tests call the same methods.
//
// Cascade contract (ApplyToTenantAsync):
//   1. resolve the country_profiles row for the given country_code
//   2. write companies.country / companies.currency / companies.timezone from the
//      profile defaults
//   3. mirror the profile default currency onto tenant_subscriptions.billing_currency
//   4. for every key in auto_enabled_features, upsert a tenant_entitlements row as
//      source='country' — WITHOUT clobbering any pre-existing 'override' row.
//
// Defaults-not-locks: everything written here is a default. The existing
// PUT /entitlements override path (source='override') can later toggle any of these
// off, or turn a non-default feature on, and that override always wins.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CountryProfileService(Database db)
{
    public sealed record CountryProfile(
        string CountryCode,
        string CountryName,
        string DefaultCurrency,
        string DefaultLocale,
        string TextDirection,
        string CalendarSystem,
        string InvoicingScheme,
        string TaxIdLabel,
        decimal? DefaultTaxRate,
        string? DataResidencyNote,
        IReadOnlyList<string> AutoEnabledFeatures);

    public sealed record CascadeResult(
        string CountryCode,
        string Currency,
        string Timezone,
        IReadOnlyList<string> EnabledFeatures);

    // Timezone is not stored on country_profiles (a country can span several); we
    // seed a sensible default per country so companies.timezone is populated on
    // provisioning. This is a default the platform admin can override afterwards.
    private static readonly Dictionary<string, string> DefaultTimezone = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SA"] = "Asia/Riyadh",
        ["CA"] = "America/Toronto",
    };

    private const string SelectColumns =
        @"country_code, country_name, default_currency, default_locale, text_direction,
          calendar_system, invoicing_scheme, tax_id_label, default_tax_rate,
          data_residency_note, auto_enabled_features";

    public async Task<List<CountryProfile>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            $"SELECT {SelectColumns} FROM country_profiles ORDER BY country_name", ct: ct);
        return rows.Select(MapRow).ToList();
    }

    public async Task<CountryProfile?> GetAsync(string countryCode, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            $"SELECT {SelectColumns} FROM country_profiles WHERE country_code = @code",
            c => c.Parameters.AddWithValue("@code", Normalize(countryCode)), ct);
        return row is null ? null : MapRow(row);
    }

    // Insert or update a profile — the CRUD path that lets future countries be added
    // without a code deploy.
    public async Task<CountryProfile> UpsertAsync(CountryProfile profile, CancellationToken ct = default)
    {
        var featuresJson = JsonSerializer.Serialize(profile.AutoEnabledFeatures ?? []);
        await db.ExecuteAsync("""
            INSERT INTO country_profiles
                (country_code, country_name, default_currency, default_locale, text_direction,
                 calendar_system, invoicing_scheme, tax_id_label, default_tax_rate,
                 data_residency_note, auto_enabled_features, updated_at)
            VALUES
                (@code, @name, @currency, @locale, @dir, @cal, @inv, @taxLabel, @taxRate,
                 @residency, CAST(@features AS JSONB), NOW())
            ON CONFLICT (country_code) DO UPDATE SET
                country_name = EXCLUDED.country_name,
                default_currency = EXCLUDED.default_currency,
                default_locale = EXCLUDED.default_locale,
                text_direction = EXCLUDED.text_direction,
                calendar_system = EXCLUDED.calendar_system,
                invoicing_scheme = EXCLUDED.invoicing_scheme,
                tax_id_label = EXCLUDED.tax_id_label,
                default_tax_rate = EXCLUDED.default_tax_rate,
                data_residency_note = EXCLUDED.data_residency_note,
                auto_enabled_features = EXCLUDED.auto_enabled_features,
                updated_at = NOW()
            """,
            c =>
            {
                c.Parameters.AddWithValue("@code", Normalize(profile.CountryCode));
                c.Parameters.AddWithValue("@name", profile.CountryName);
                c.Parameters.AddWithValue("@currency", profile.DefaultCurrency);
                c.Parameters.AddWithValue("@locale", profile.DefaultLocale);
                c.Parameters.AddWithValue("@dir", profile.TextDirection);
                c.Parameters.AddWithValue("@cal", profile.CalendarSystem);
                c.Parameters.AddWithValue("@inv", profile.InvoicingScheme);
                c.Parameters.AddWithValue("@taxLabel", profile.TaxIdLabel);
                c.Parameters.AddWithValue("@taxRate", (object?)profile.DefaultTaxRate ?? DBNull.Value);
                c.Parameters.AddWithValue("@residency", (object?)profile.DataResidencyNote ?? DBNull.Value);
                c.Parameters.AddWithValue("@features", featuresJson);
            }, ct);

        return (await GetAsync(profile.CountryCode, ct))!;
    }

    public async Task<bool> DeleteAsync(string countryCode, CancellationToken ct = default)
    {
        var affected = await db.ExecuteAsync(
            "DELETE FROM country_profiles WHERE country_code = @code",
            c => c.Parameters.AddWithValue("@code", Normalize(countryCode)), ct);
        return affected > 0;
    }

    // Apply a country profile's defaults onto a freshly-created tenant. Returns the
    // resolved cascade so the caller can audit exactly what was applied, or null if
    // the country_code has no profile.
    public async Task<CascadeResult?> ApplyToTenantAsync(long companyId, string countryCode, string actor, CancellationToken ct = default)
    {
        var profile = await GetAsync(countryCode, ct);
        if (profile is null) return null;

        var normalized = Normalize(countryCode);
        var timezone = DefaultTimezone.TryGetValue(normalized, out var tz) ? tz : null;

        // companies: country + currency always; timezone only when we have a default
        // for this country (COALESCE keeps the existing timezone otherwise).
        await db.ExecuteAsync(
            @"UPDATE companies
                 SET country = @country,
                     currency = @currency,
                     timezone = COALESCE(@tz, timezone)
               WHERE id = @cid",
            c =>
            {
                c.Parameters.AddWithValue("@country", normalized);
                c.Parameters.AddWithValue("@currency", profile.DefaultCurrency);
                c.Parameters.AddWithValue("@tz", (object?)timezone ?? DBNull.Value);
                c.Parameters.AddWithValue("@cid", companyId);
            }, ct);

        // Mirror the currency onto the subscription's billing currency (only if the
        // subscription exists yet; tenant creation inserts it before cascading).
        await db.ExecuteAsync(
            "UPDATE tenant_subscriptions SET billing_currency = @currency, updated_at = NOW() WHERE company_id = @cid",
            c =>
            {
                c.Parameters.AddWithValue("@currency", profile.DefaultCurrency);
                c.Parameters.AddWithValue("@cid", companyId);
            }, ct);

        // Auto-enable each feature key as a country default — never overriding an
        // explicit override the operator may already have set.
        foreach (var feature in profile.AutoEnabledFeatures.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            await db.ExecuteAsync(
                @"INSERT INTO tenant_entitlements (company_id, module_key, enabled, source, updated_by)
                  VALUES (@cid, @mk, true, 'country', @by)
                  ON CONFLICT (company_id, module_key) DO UPDATE
                    SET enabled = CASE WHEN tenant_entitlements.source = 'override'
                                       THEN tenant_entitlements.enabled ELSE true END,
                        source  = CASE WHEN tenant_entitlements.source = 'override'
                                       THEN tenant_entitlements.source ELSE 'country' END,
                        updated_at = NOW()",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@mk", feature.Trim());
                    c.Parameters.AddWithValue("@by", actor);
                }, ct);
        }

        return new CascadeResult(normalized, profile.DefaultCurrency,
            timezone ?? "", profile.AutoEnabledFeatures);
    }

    private static string Normalize(string countryCode) =>
        (countryCode ?? "").Trim().ToUpperInvariant();

    private static CountryProfile MapRow(Dictionary<string, object?> row)
    {
        List<string> features = [];
        var raw = row["autoEnabledFeatures"]?.ToString();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try { features = JsonSerializer.Deserialize<List<string>>(raw) ?? []; }
            catch { features = []; }
        }

        decimal? taxRate = row["defaultTaxRate"] is null || row["defaultTaxRate"] is DBNull
            ? null
            : Convert.ToDecimal(row["defaultTaxRate"]);

        return new CountryProfile(
            row["countryCode"]?.ToString() ?? "",
            row["countryName"]?.ToString() ?? "",
            row["defaultCurrency"]?.ToString() ?? "",
            row["defaultLocale"]?.ToString() ?? "",
            row["textDirection"]?.ToString() ?? "ltr",
            row["calendarSystem"]?.ToString() ?? "gregorian",
            row["invoicingScheme"]?.ToString() ?? "standard",
            row["taxIdLabel"]?.ToString() ?? "Tax ID",
            taxRate,
            row["dataResidencyNote"]?.ToString(),
            features);
    }
}
