using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// COUNTRY PROFILES — platform-managed localization / market defaults.
//
// A country_profiles row is the single source of truth for the defaults a tenant
// should inherit when it is provisioned for a given country: currency, locale,
// text direction, calendar system, invoicing scheme, tax labelling and the set of
// feature keys that should be auto-enabled (auto_enabled_features).
//
// This is a STRUCTURE + CASCADE mechanism only. It records WHICH features a
// country turns on by default (e.g. zatca_invoicing for Saudi Arabia); it does NOT
// implement those features (ZATCA invoice generation, Hijri rendering, Arabic RTL).
// Those remain separate builds that now have a clean place to plug into.
//
// Additive & idempotent:
//   - creates country_profiles (no drops, no destructive ALTERs)
//   - adds companies.country / companies.currency via ADD COLUMN IF NOT EXISTS
//     (companies.timezone already exists) so the tenant-creation cascade can
//     persist the profile defaults onto the tenant company row
//   - seeds a baseline of major logistics markets (NA/EU/GCC/APAC/LATAM/Africa)
//     using ON CONFLICT upserts; any further country is added through the
//     platform CRUD endpoint, never a code deploy.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CountryProfileSchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS country_profiles (
                country_code       VARCHAR(2)   PRIMARY KEY,
                country_name       VARCHAR(120) NOT NULL,
                default_currency   VARCHAR(8)   NOT NULL,
                default_locale     VARCHAR(20)  NOT NULL,
                text_direction     VARCHAR(3)   NOT NULL DEFAULT 'ltr',
                calendar_system    VARCHAR(40)  NOT NULL DEFAULT 'gregorian',
                invoicing_scheme   VARCHAR(40)  NOT NULL DEFAULT 'standard',
                tax_id_label       VARCHAR(60)  NOT NULL DEFAULT 'Tax ID',
                default_tax_rate   NUMERIC(6,4) NULL,
                data_residency_note VARCHAR(400) NULL,
                auto_enabled_features JSONB     NOT NULL DEFAULT '[]'::jsonb,
                created_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                CONSTRAINT chk_country_text_direction CHECK (text_direction IN ('ltr','rtl'))
            )
            """);

        // Tenant company row carries the resolved defaults so runtime reads never
        // have to re-join country_profiles. timezone already exists on companies.
        await db.ExecuteAsync("ALTER TABLE companies ADD COLUMN IF NOT EXISTS country VARCHAR(2) NULL");
        await db.ExecuteAsync("ALTER TABLE companies ADD COLUMN IF NOT EXISTS currency VARCHAR(8) NULL");

        await SeedProfilesAsync();
    }

    // Seed a baseline of major logistics markets. Idempotent upsert: re-running keeps
    // these canonical seed defaults while never disturbing profiles an operator later
    // adds/edits through the CRUD endpoint. Operators can still add any country not
    // listed here (or adjust tax rates / auto-enabled features) via the platform API.
    private async Task SeedProfilesAsync()
    {
        var saFeatures = JsonSerializer.Serialize(new[] { "zatca_invoicing", "hijri_calendar_toggle", "arabic_rtl" });
        var arabicRtl = JsonSerializer.Serialize(new[] { "arabic_rtl" });
        var none = JsonSerializer.Serialize(Array.Empty<string>());
        const string euGdpr = "EU data residency (GDPR).";

        // code, name, currency, locale, direction, calendar, invoicing, taxLabel, taxRate, residency, featuresJson
        var seeds = new (string, string, string, string, string, string, string, string, decimal?, string?, string)[]
        {
            ("US", "United States",        "USD", "en-US", "ltr", "gregorian",            "standard",     "EIN / Tax ID",     null,    null,   none),
            ("CA", "Canada",               "CAD", "en-CA", "ltr", "gregorian",            "standard",     "GST/HST Number",   0.0500m, null,   none),
            ("GB", "United Kingdom",       "GBP", "en-GB", "ltr", "gregorian",            "standard",     "VAT Number",       0.2000m, null,   none),
            ("IE", "Ireland",              "EUR", "en-IE", "ltr", "gregorian",            "standard",     "VAT Number",       0.2300m, euGdpr, none),
            ("DE", "Germany",              "EUR", "de-DE", "ltr", "gregorian",            "standard",     "USt-IdNr.",        0.1900m, euGdpr, none),
            ("FR", "France",               "EUR", "fr-FR", "ltr", "gregorian",            "standard",     "TVA Number",       0.2000m, euGdpr, none),
            ("NL", "Netherlands",          "EUR", "nl-NL", "ltr", "gregorian",            "standard",     "BTW Number",       0.2100m, euGdpr, none),
            ("ES", "Spain",                "EUR", "es-ES", "ltr", "gregorian",            "standard",     "NIF / VAT",        0.2100m, euGdpr, none),
            ("IT", "Italy",                "EUR", "it-IT", "ltr", "gregorian",            "standard",     "Partita IVA",      0.2200m, euGdpr, none),
            ("AU", "Australia",            "AUD", "en-AU", "ltr", "gregorian",            "standard",     "ABN",              0.1000m, null,   none),
            ("NZ", "New Zealand",          "NZD", "en-NZ", "ltr", "gregorian",            "standard",     "GST Number",       0.1500m, null,   none),
            ("SG", "Singapore",            "SGD", "en-SG", "ltr", "gregorian",            "standard",     "GST Reg. No.",     0.0900m, null,   none),
            ("IN", "India",                "INR", "en-IN", "ltr", "gregorian",            "standard",     "GSTIN",            0.1800m, null,   none),
            ("JP", "Japan",                "JPY", "ja-JP", "ltr", "gregorian",            "standard",     "Corporate Number", 0.1000m, null,   none),
            ("BR", "Brazil",               "BRL", "pt-BR", "ltr", "gregorian",            "standard",     "CNPJ",             0.1700m, "Brazil data residency (LGPD).", none),
            ("MX", "Mexico",               "MXN", "es-MX", "ltr", "gregorian",            "standard",     "RFC",              0.1600m, null,   none),
            ("ZA", "South Africa",         "ZAR", "en-ZA", "ltr", "gregorian",            "standard",     "VAT Number",       0.1500m, null,   none),
            ("SA", "Saudi Arabia",         "SAR", "ar-SA", "rtl", "gregorian_hijri_dual", "zatca_phase2", "VAT Number",       0.1500m, "KSA data residency expected for regulated invoicing (ZATCA).", saFeatures),
            ("AE", "United Arab Emirates", "AED", "ar-AE", "rtl", "gregorian",            "standard",     "TRN",              0.0500m, null,   arabicRtl),
            ("QA", "Qatar",                "QAR", "ar-QA", "rtl", "gregorian",            "standard",     "Tax Card No.",     null,    null,   arabicRtl),
            ("KW", "Kuwait",               "KWD", "ar-KW", "rtl", "gregorian",            "standard",     "Tax No.",          null,    null,   arabicRtl),
            ("BH", "Bahrain",              "BHD", "ar-BH", "rtl", "gregorian",            "standard",     "VAT Number",       0.1000m, null,   arabicRtl),
            ("OM", "Oman",                 "OMR", "ar-OM", "rtl", "gregorian",            "standard",     "VAT Number",       0.0500m, null,   arabicRtl),
            ("EG", "Egypt",                "EGP", "ar-EG", "rtl", "gregorian",            "standard",     "Tax Reg. No.",     0.1400m, null,   arabicRtl),
        };

        foreach (var (code, name, currency, locale, direction, calendar, invoicing, taxLabel, taxRate, residency, featuresJson) in seeds)
            await UpsertSeedAsync(code, name, currency, locale, direction, calendar, invoicing, taxLabel, taxRate, residency, featuresJson);
    }

    private Task UpsertSeedAsync(string code, string name, string currency, string locale,
        string direction, string calendar, string invoicing, string taxLabel, decimal? taxRate,
        string? residency, string featuresJson)
    {
        return db.ExecuteAsync("""
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
                c.Parameters.AddWithValue("@code", code);
                c.Parameters.AddWithValue("@name", name);
                c.Parameters.AddWithValue("@currency", currency);
                c.Parameters.AddWithValue("@locale", locale);
                c.Parameters.AddWithValue("@dir", direction);
                c.Parameters.AddWithValue("@cal", calendar);
                c.Parameters.AddWithValue("@inv", invoicing);
                c.Parameters.AddWithValue("@taxLabel", taxLabel);
                c.Parameters.AddWithValue("@taxRate", (object?)taxRate ?? DBNull.Value);
                c.Parameters.AddWithValue("@residency", (object?)residency ?? DBNull.Value);
                c.Parameters.AddWithValue("@features", featuresJson);
            });
    }
}
