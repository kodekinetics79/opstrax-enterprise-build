using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// PLATFORM BILLING ITEMIZATION + INDIRECT TAX (OpsTrax → tenant)
//
// Until now `platform_invoices` held a single amount_cents with an unstructured
// JSONB blob. That is not an invoice — it is a number. It cannot be audited, it
// cannot carry VAT/GST, and it cannot express "this tenant gets Dispatch free,
// Telematics at a flat fee, and Cold Chain per event".
//
// This service adds the missing sub-ledger:
//
//   platform_invoice_lines       — one row per billable thing, with its own
//                                  taxable base, tax code, rate and tax amount.
//   platform_invoice_sequences   — gap-free sequential numbering per (scope,year),
//                                  allocated at ISSUE time, never at draft time.
//   tenant_billing_plan_items    — per-tenant, per-feature commercial terms:
//                                  free / included / flat / per_seat / per_unit /
//                                  tiered / one_time, with floors and caps.
//   platform_tax_registrations   — where OpsTrax itself is registered to collect.
//   platform_tax_rules           — the determination decision table (priority
//                                  ordered), so tax is config, never hardcoded.
//
// Accounting invariants enforced downstream in PlatformBillingService:
//   • Tax is computed and rounded PER LINE; the header tax total is the SUM of
//     line tax. Never rate × header subtotal (that is the classic audit finding).
//   • An issued document is immutable. Corrections are credit notes, not edits.
//   • Numbering is allocated once, at issue, and is gap-free within its scope.
//   • Minor units respect the currency (JPY=0, KWD/BHD/OMR=3, most others=2).
//
// Every statement is additive and idempotent. The protected-environment mirror
// is database/migrations/2026_08_18_stage81_platform_billing_itemization.sql.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PlatformBillingSchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        // ── Itemized invoice lines ───────────────────────────────────────────
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_invoice_lines (
                id                 BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                invoice_id         BIGINT        NOT NULL REFERENCES platform_invoices(id) ON DELETE CASCADE,
                line_no            INT           NOT NULL DEFAULT 1,
                source             VARCHAR(24)   NOT NULL DEFAULT 'manual',
                feature_key        VARCHAR(120)  NULL,
                meter_key          VARCHAR(80)   NULL,
                description        VARCHAR(400)  NOT NULL,
                charge_model       VARCHAR(24)   NOT NULL DEFAULT 'flat',
                quantity           NUMERIC(18,4) NOT NULL DEFAULT 1,
                unit               VARCHAR(40)   NULL,
                unit_price_cents   BIGINT        NOT NULL DEFAULT 0,
                gross_amount_cents BIGINT        NOT NULL DEFAULT 0,
                discount_cents     BIGINT        NOT NULL DEFAULT 0,
                net_amount_cents   BIGINT        NOT NULL DEFAULT 0,
                tax_code           VARCHAR(40)   NULL,
                tax_category       VARCHAR(8)    NULL,
                tax_treatment      VARCHAR(24)   NOT NULL DEFAULT 'standard',
                tax_rate           NUMERIC(9,6)  NOT NULL DEFAULT 0,
                tax_amount_cents   BIGINT        NOT NULL DEFAULT 0,
                exemption_reason   VARCHAR(240)  NULL,
                total_cents        BIGINT        NOT NULL DEFAULT 0,
                period_start       DATE          NULL,
                period_end         DATE          NULL,
                created_at         TIMESTAMPTZ   NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_platform_invoice_lines_invoice ON platform_invoice_lines (invoice_id, line_no)");

        // ── Gap-free document numbering ──────────────────────────────────────
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_invoice_sequences (
                scope       VARCHAR(60) NOT NULL,
                period_year INT         NOT NULL,
                next_value  BIGINT      NOT NULL DEFAULT 1,
                updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (scope, period_year)
            )
            """);

        // ── Header columns the itemized model needs ──────────────────────────
        // amount_cents is retained as the grand total so existing collections
        // KPIs keep working; total_cents is its explicit twin.
        await db.ExecuteAsync("""
            ALTER TABLE platform_invoices
                ADD COLUMN IF NOT EXISTS document_type     VARCHAR(20)   NOT NULL DEFAULT 'invoice',
                ADD COLUMN IF NOT EXISTS subtotal_cents    BIGINT        NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS discount_cents    BIGINT        NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS tax_total_cents   BIGINT        NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS total_cents       BIGINT        NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS tax_country       VARCHAR(2)    NULL,
                ADD COLUMN IF NOT EXISTS tax_regime        VARCHAR(24)   NULL,
                ADD COLUMN IF NOT EXISTS tax_treatment     VARCHAR(24)   NULL,
                ADD COLUMN IF NOT EXISTS tax_label         VARCHAR(40)   NULL,
                ADD COLUMN IF NOT EXISTS place_of_supply   VARCHAR(120)  NULL,
                ADD COLUMN IF NOT EXISTS seller_legal_name VARCHAR(220)  NULL,
                ADD COLUMN IF NOT EXISTS seller_tax_no     VARCHAR(80)   NULL,
                ADD COLUMN IF NOT EXISTS buyer_legal_name  VARCHAR(220)  NULL,
                ADD COLUMN IF NOT EXISTS buyer_tax_no      VARCHAR(80)   NULL,
                ADD COLUMN IF NOT EXISTS buyer_country     VARCHAR(2)    NULL,
                ADD COLUMN IF NOT EXISTS period_start      DATE          NULL,
                ADD COLUMN IF NOT EXISTS period_end        DATE          NULL,
                ADD COLUMN IF NOT EXISTS payment_terms_days INT          NOT NULL DEFAULT 15,
                ADD COLUMN IF NOT EXISTS issued_by         VARCHAR(220)  NULL,
                ADD COLUMN IF NOT EXISTS voided_at         TIMESTAMPTZ   NULL,
                ADD COLUMN IF NOT EXISTS void_reason       VARCHAR(400)  NULL,
                ADD COLUMN IF NOT EXISTS credit_note_of    BIGINT        NULL,
                ADD COLUMN IF NOT EXISTS invoicing_scheme  VARCHAR(40)   NULL,
                ADD COLUMN IF NOT EXISTS minor_units       INT           NOT NULL DEFAULT 2,
                ADD COLUMN IF NOT EXISTS updated_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW()
            """);
        // Legacy single-amount rows predate itemization: mirror them into the
        // new totals so the ledger reads consistently from day one.
        await db.ExecuteAsync("UPDATE platform_invoices SET total_cents = amount_cents, subtotal_cents = amount_cents WHERE total_cents = 0 AND amount_cents <> 0");

        // ── Per-tenant, per-feature commercial terms ─────────────────────────
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS tenant_billing_plan_items (
                id                BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id        BIGINT        NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                feature_key       VARCHAR(120)  NOT NULL,
                feature_label     VARCHAR(200)  NULL,
                charge_model      VARCHAR(24)   NOT NULL DEFAULT 'included',
                meter_key         VARCHAR(80)   NULL,
                unit_price_cents  BIGINT        NOT NULL DEFAULT 0,
                included_quantity NUMERIC(18,4) NOT NULL DEFAULT 0,
                flat_price_cents  BIGINT        NOT NULL DEFAULT 0,
                minimum_cents     BIGINT        NULL,
                cap_cents         BIGINT        NULL,
                tiers_json        JSONB         NULL,
                currency          VARCHAR(8)    NULL,
                billing_interval  VARCHAR(20)   NOT NULL DEFAULT 'monthly',
                tax_code          VARCHAR(40)   NULL,
                effective_from    DATE          NOT NULL DEFAULT CURRENT_DATE,
                effective_to      DATE          NULL,
                active            BOOLEAN       NOT NULL DEFAULT true,
                note              VARCHAR(400)  NULL,
                updated_by        VARCHAR(220)  NULL,
                created_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                updated_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, feature_key)
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_tenant_billing_plan_company ON tenant_billing_plan_items (company_id, active)");

        // ── Seller-side (OpsTrax) indirect-tax registrations ─────────────────
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_tax_registrations (
                id                  BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                country_code        VARCHAR(2)   NOT NULL UNIQUE,
                regime              VARCHAR(24)  NOT NULL DEFAULT 'vat',
                legal_name          VARCHAR(220) NULL,
                tax_registration_no VARCHAR(80)  NULL,
                standard_rate       NUMERIC(9,6) NULL,
                tax_label           VARCHAR(40)  NULL,
                invoicing_scheme    VARCHAR(40)  NULL,
                registered          BOOLEAN      NOT NULL DEFAULT false,
                effective_from      DATE         NOT NULL DEFAULT CURRENT_DATE,
                effective_to        DATE         NULL,
                note                VARCHAR(400) NULL,
                updated_by          VARCHAR(220) NULL,
                created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        // ── Tax determination decision table ─────────────────────────────────
        // Lowest `priority` wins. NULL match_* means "any". This is what makes
        // tax config rather than code: an accountant can add a rule without a
        // deploy, and every issued invoice records which rule produced its tax.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_tax_rules (
                id                BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                rule_key          VARCHAR(80)  NOT NULL UNIQUE,
                description       VARCHAR(300) NULL,
                match_country     VARCHAR(2)   NULL,
                match_registered  BOOLEAN      NULL,
                match_has_tax_id  BOOLEAN      NULL,
                match_feature_key VARCHAR(120) NULL,
                treatment         VARCHAR(24)  NOT NULL DEFAULT 'standard',
                tax_code          VARCHAR(40)  NOT NULL DEFAULT 'STD',
                tax_category      VARCHAR(8)   NOT NULL DEFAULT 'S',
                rate              NUMERIC(9,6) NULL,
                reason_text       VARCHAR(300) NULL,
                priority          INT          NOT NULL DEFAULT 100,
                active            BOOLEAN      NOT NULL DEFAULT true,
                created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_platform_tax_rules_priority ON platform_tax_rules (active, priority)");

        await SeedTaxRegistrationsAsync();
        await SeedTaxRulesAsync();
    }

    // Seller registrations are seeded from the country reference catalog so the
    // rate an operator sees always matches the localization profile the tenant was
    // activated under. `registered=false` is the honest default: OpsTrax has not
    // claimed a registration number anywhere until Finance enters one.
    private async Task SeedTaxRegistrationsAsync()
    {
        await db.ExecuteAsync("""
            INSERT INTO platform_tax_registrations
                (country_code, regime, standard_rate, tax_label, invoicing_scheme, registered)
            SELECT cp.country_code,
                   CASE
                       WHEN cp.default_tax_rate IS NULL THEN 'none'
                       WHEN cp.country_code IN ('CA','AU','NZ','SG','IN') THEN 'gst'
                       ELSE 'vat'
                   END,
                   cp.default_tax_rate,
                   CASE
                       WHEN cp.default_tax_rate IS NULL THEN 'Tax'
                       WHEN cp.country_code IN ('CA','AU','NZ','SG','IN') THEN 'GST'
                       ELSE 'VAT'
                   END,
                   cp.invoicing_scheme,
                   false
            FROM country_profiles cp
            ON CONFLICT (country_code) DO UPDATE SET
                standard_rate    = COALESCE(platform_tax_registrations.standard_rate, EXCLUDED.standard_rate),
                invoicing_scheme = COALESCE(platform_tax_registrations.invoicing_scheme, EXCLUDED.invoicing_scheme),
                updated_at       = NOW()
            """);
    }

    // The default rule ladder, ordered the way a VAT determination actually runs.
    // Operators may add higher-priority country rules; these are the safety net.
    private static readonly (string Key, string Desc, string? Country, bool? Registered, bool? HasTaxId,
        string Treatment, string Code, string Category, decimal? Rate, string? Reason, int Priority)[] RuleSeed =
    {
        ("domestic_registered", "Seller registered in the buyer's country — domestic supply at the standard rate",
            null, true, null, "standard", "STD", "S", null, null, 100),
        ("reverse_charge_b2b", "Seller not registered locally and the buyer supplied a valid business tax ID — customer self-accounts",
            null, false, true, "reverse_charge", "AE", "AE", 0m,
            "Reverse charge — the customer accounts for the tax due on this supply.", 200),
        ("no_regime", "Buyer's country operates no indirect tax on this supply — outside the scope of VAT/GST",
            null, null, null, "out_of_scope", "O", "O", 0m,
            "Outside the scope of VAT/GST in the place of supply.", 900),
    };

    private async Task SeedTaxRulesAsync()
    {
        foreach (var r in RuleSeed)
        {
            await db.ExecuteAsync("""
                INSERT INTO platform_tax_rules
                    (rule_key, description, match_country, match_registered, match_has_tax_id,
                     treatment, tax_code, tax_category, rate, reason_text, priority)
                VALUES (@k,@d,@c,@reg,@tid,@t,@code,@cat,@rate,@reason,@p)
                ON CONFLICT (rule_key) DO NOTHING
                """,
                c =>
                {
                    c.Parameters.AddWithValue("@k", r.Key);
                    c.Parameters.AddWithValue("@d", r.Desc);
                    c.Parameters.AddWithValue("@c", (object?)r.Country ?? DBNull.Value);
                    c.Parameters.AddWithValue("@reg", (object?)r.Registered ?? DBNull.Value);
                    c.Parameters.AddWithValue("@tid", (object?)r.HasTaxId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@t", r.Treatment);
                    c.Parameters.AddWithValue("@code", r.Code);
                    c.Parameters.AddWithValue("@cat", r.Category);
                    c.Parameters.AddWithValue("@rate", (object?)r.Rate ?? DBNull.Value);
                    c.Parameters.AddWithValue("@reason", (object?)r.Reason ?? DBNull.Value);
                    c.Parameters.AddWithValue("@p", r.Priority);
                });
        }
    }
}
