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
//   4. reconcile country-derived entitlements and the compatible market pack
//   5. write the locked regulatory fields to tenant_locale_settings.
// User language and date-display preferences remain selectable. Regulatory
// country, currency, timezone, measurement units and market pack do not.
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
        ["US"] = "America/New_York",
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
        => await db.RunInSystemTransactionAsync(
            () => ApplyToTenantCoreAsync(companyId, countryCode, actor, ct), ct);

    private async Task<CascadeResult?> ApplyToTenantCoreAsync(long companyId, string countryCode, string actor, CancellationToken ct)
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

        // Remove stale country-derived features from a previous operating market.
        // Explicit, unrelated commercial overrides remain untouched.
        await db.ExecuteAsync(
            @"UPDATE tenant_entitlements
                 SET enabled=false, source='country', updated_by=@by, updated_at=NOW()
               WHERE company_id=@cid AND source='country'
                 AND NOT (module_key = ANY(@features))",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@by", actor);
                c.Parameters.AddWithValue("@features", profile.AutoEnabledFeatures.ToArray());
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

        var (lockedTimezone, distanceUnit, volumeUnit) = TenantMarketPolicyService.DefaultsFor(normalized);
        var localeTable = await db.QuerySingleAsync("SELECT to_regclass('public.tenant_locale_settings')::text table_name", ct: ct);
        if (localeTable?.GetValueOrDefault("tableName") is not null)
        {
            await db.ExecuteAsync(
                @"INSERT INTO tenant_locale_settings
                    (tenant_id,default_language,default_country,timezone,date_format,currency,distance_unit,volume_unit)
                  VALUES (@cid,@lang,@country,@tz,@date,@currency,@distance,@volume)
                  ON CONFLICT (tenant_id) DO UPDATE SET
                    default_country=EXCLUDED.default_country, timezone=EXCLUDED.timezone,
                    currency=EXCLUDED.currency, distance_unit=EXCLUDED.distance_unit,
                    volume_unit=EXCLUDED.volume_unit, updated_at=NOW()",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@lang", profile.DefaultLocale);
                    c.Parameters.AddWithValue("@country", normalized);
                    c.Parameters.AddWithValue("@tz", lockedTimezone);
                    c.Parameters.AddWithValue("@date", normalized == "US" ? "MM/DD/YYYY" : "DD/MM/YYYY");
                    c.Parameters.AddWithValue("@currency", profile.DefaultCurrency);
                    c.Parameters.AddWithValue("@distance", distanceUnit);
                    c.Parameters.AddWithValue("@volume", volumeUnit);
                }, ct);
        }

        // Existing country-bearing operational records move with the tenant in
        // the same transaction, so no stale regional label survives reassignment.
        await db.ExecuteAsync(
            "UPDATE branches SET country_code=@country, timezone=@tz, updated_at=NOW() WHERE company_id=@cid AND deleted_at IS NULL",
            c => { c.Parameters.AddWithValue("@country", normalized); c.Parameters.AddWithValue("@tz", lockedTimezone); c.Parameters.AddWithValue("@cid", companyId); }, ct);
        await db.ExecuteAsync(
            "UPDATE dvir_templates SET country_code=@country WHERE company_id=@cid",
            c => { c.Parameters.AddWithValue("@country", normalized); c.Parameters.AddWithValue("@cid", companyId); }, ct);
        await db.ExecuteAsync(
            "UPDATE dvir_reports SET country_code=@country WHERE company_id=@cid",
            c => { c.Parameters.AddWithValue("@country", normalized); c.Parameters.AddWithValue("@cid", companyId); }, ct);

        var packCode = TenantMarketPolicyService.PackForCountry(normalized);
        var packTable = await db.QuerySingleAsync("SELECT to_regclass('public.tenant_market_packs')::text table_name", ct: ct);
        if (packTable?.GetValueOrDefault("tableName") is not null)
        {
            await db.ExecuteAsync(
                @"UPDATE tenant_market_packs
                     SET status='disabled', updated_at=NOW(), enabled_by=@by
                   WHERE company_id=@cid AND status='active'
                     AND (@pack::text IS NULL OR pack_code<>@pack)",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@pack", (object?)packCode ?? DBNull.Value); c.Parameters.AddWithValue("@by", actor); }, ct);
            if (packCode is not null)
            {
                await db.ExecuteAsync(
                    @"INSERT INTO tenant_market_packs (company_id,pack_code,status,enabled_by,enabled_at,updated_at)
                      VALUES (@cid,@pack,'active',@by,NOW(),NOW())
                      ON CONFLICT (company_id,pack_code) DO UPDATE SET
                        status='active', enabled_by=EXCLUDED.enabled_by, updated_at=NOW()",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@pack", packCode); c.Parameters.AddWithValue("@by", actor); }, ct);
            }

            foreach (var knownPack in new[] { MarketPackSchemaService.Packs.CanadaNa, MarketPackSchemaService.Packs.SaudiGcc })
            {
                var moduleKey = MarketPackSchemaService.ModuleKeyForPack(knownPack);
                await db.ExecuteAsync(
                    @"INSERT INTO tenant_entitlements (company_id,module_key,enabled,source,updated_by,updated_at)
                      VALUES (@cid,@module,@enabled,'country_market',@by,NOW())
                      ON CONFLICT (company_id,module_key) DO UPDATE SET
                        enabled=EXCLUDED.enabled, source='country_market', updated_by=EXCLUDED.updated_by, updated_at=NOW()",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@module", moduleKey);
                        c.Parameters.AddWithValue("@enabled", knownPack == packCode);
                        c.Parameters.AddWithValue("@by", actor);
                    }, ct);
            }
        }

        return new CascadeResult(normalized, profile.DefaultCurrency,
            lockedTimezone, profile.AutoEnabledFeatures);
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
