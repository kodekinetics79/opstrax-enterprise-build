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
//   - seeds SA + CA using ON CONFLICT upserts so future countries are added
//     through the platform CRUD endpoint, never a code deploy.
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

    // Seed exactly two profiles for now (SA, CA). Idempotent upsert: re-running keeps
    // the canonical seed defaults for these two rows while never disturbing profiles
    // an operator later adds/edits through the CRUD endpoint.
    private async Task SeedProfilesAsync()
    {
        var saFeatures = JsonSerializer.Serialize(new[] { "zatca_invoicing", "hijri_calendar_toggle", "arabic_rtl" });
        var caFeatures = JsonSerializer.Serialize(Array.Empty<string>());

        await UpsertSeedAsync(
            code: "SA", name: "Saudi Arabia", currency: "SAR", locale: "ar-SA",
            direction: "rtl", calendar: "gregorian_hijri_dual", invoicing: "zatca_phase2",
            taxLabel: "VAT Number", taxRate: 0.1500m,
            residency: "KSA data residency expected for regulated invoicing (ZATCA).",
            featuresJson: saFeatures);

        await UpsertSeedAsync(
            code: "CA", name: "Canada", currency: "CAD", locale: "en-CA",
            direction: "ltr", calendar: "gregorian", invoicing: "standard",
            taxLabel: "GST/HST Number", taxRate: 0.0500m,
            residency: null,
            featuresJson: caFeatures);
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
