-- Stage 81 — Platform billing itemization + indirect tax (OpsTrax → tenant)
--
-- Protected Staging and Production skip runtime schema initialization, so the
-- owner mirror of PlatformBillingSchemaService lives here. Purely additive and
-- idempotent: CREATE TABLE / ADD COLUMN IF NOT EXISTS plus reference-data upserts.
-- No tenant, customer, user or credential rows are created.
--
-- What it closes: platform_invoices carried one amount_cents and an unstructured
-- JSONB blob. That cannot be audited, cannot carry VAT/GST, and cannot express
-- per-feature commercial terms. This adds the sub-ledger:
--
--   platform_invoice_lines      one row per billable thing, taxed on its own base
--   platform_invoice_sequences  gap-free numbering, allocated at ISSUE time
--   tenant_billing_plan_items   free / included / flat / per_seat / per_unit /
--                               tiered / one_time terms, per tenant per feature
--   platform_tax_registrations  where OpsTrax itself is registered to collect
--   platform_tax_rules          the determination decision table (config, not code)

BEGIN;

-- ── Itemized invoice lines ───────────────────────────────────────────────────
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
);
CREATE INDEX IF NOT EXISTS idx_platform_invoice_lines_invoice
    ON platform_invoice_lines (invoice_id, line_no);

-- ── Gap-free document numbering ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS platform_invoice_sequences (
    scope       VARCHAR(60) NOT NULL,
    period_year INT         NOT NULL,
    next_value  BIGINT      NOT NULL DEFAULT 1,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (scope, period_year)
);

-- ── Invoice header: totals, tax posture, document lifecycle ──────────────────
-- amount_cents is retained as the grand total so the existing collections KPIs
-- keep working; total_cents is its explicit, itemization-derived twin.
ALTER TABLE platform_invoices
    ADD COLUMN IF NOT EXISTS document_type      VARCHAR(20)  NOT NULL DEFAULT 'invoice',
    ADD COLUMN IF NOT EXISTS subtotal_cents     BIGINT       NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_cents     BIGINT       NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS tax_total_cents    BIGINT       NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_cents        BIGINT       NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS tax_country        VARCHAR(2)   NULL,
    ADD COLUMN IF NOT EXISTS tax_regime         VARCHAR(24)  NULL,
    ADD COLUMN IF NOT EXISTS tax_treatment      VARCHAR(24)  NULL,
    ADD COLUMN IF NOT EXISTS tax_label          VARCHAR(40)  NULL,
    ADD COLUMN IF NOT EXISTS place_of_supply    VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS seller_legal_name  VARCHAR(220) NULL,
    ADD COLUMN IF NOT EXISTS seller_tax_no      VARCHAR(80)  NULL,
    ADD COLUMN IF NOT EXISTS buyer_legal_name   VARCHAR(220) NULL,
    ADD COLUMN IF NOT EXISTS buyer_tax_no       VARCHAR(80)  NULL,
    ADD COLUMN IF NOT EXISTS buyer_country      VARCHAR(2)   NULL,
    ADD COLUMN IF NOT EXISTS period_start       DATE         NULL,
    ADD COLUMN IF NOT EXISTS period_end         DATE         NULL,
    ADD COLUMN IF NOT EXISTS payment_terms_days INT          NOT NULL DEFAULT 15,
    ADD COLUMN IF NOT EXISTS issued_by          VARCHAR(220) NULL,
    ADD COLUMN IF NOT EXISTS voided_at          TIMESTAMPTZ  NULL,
    ADD COLUMN IF NOT EXISTS void_reason        VARCHAR(400) NULL,
    ADD COLUMN IF NOT EXISTS credit_note_of     BIGINT       NULL,
    ADD COLUMN IF NOT EXISTS invoicing_scheme   VARCHAR(40)  NULL,
    ADD COLUMN IF NOT EXISTS minor_units        INT          NOT NULL DEFAULT 2,
    ADD COLUMN IF NOT EXISTS updated_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW();

-- Legacy single-amount rows predate itemization: mirror them into the new totals
-- so the ledger reads consistently from the first deploy.
UPDATE platform_invoices
   SET total_cents = amount_cents, subtotal_cents = amount_cents
 WHERE total_cents = 0 AND amount_cents <> 0;

-- ── Per-tenant, per-feature commercial terms ─────────────────────────────────
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
);
CREATE INDEX IF NOT EXISTS idx_tenant_billing_plan_company
    ON tenant_billing_plan_items (company_id, active);

-- ── Seller-side (OpsTrax) indirect-tax registrations ─────────────────────────
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
);

-- ── Tax determination decision table ─────────────────────────────────────────
-- Lowest priority wins; a NULL match_* column means "any".
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
);
CREATE INDEX IF NOT EXISTS idx_platform_tax_rules_priority
    ON platform_tax_rules (active, priority);

-- ── Reference data ───────────────────────────────────────────────────────────
-- Registrations are derived from the country catalog so the rate an operator sees
-- always matches the localization profile the tenant was activated under.
-- registered=false is the honest default: OpsTrax claims no registration number
-- anywhere until Finance enters one.
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
    updated_at       = NOW();

-- The default determination ladder, in the order a VAT decision actually runs.
INSERT INTO platform_tax_rules
    (rule_key, description, match_country, match_registered, match_has_tax_id,
     treatment, tax_code, tax_category, rate, reason_text, priority)
VALUES
    ('domestic_registered',
     'Seller registered in the buyer''s country — domestic supply at the standard rate',
     NULL, TRUE, NULL, 'standard', 'STD', 'S', NULL, NULL, 100),
    ('reverse_charge_b2b',
     'Seller not registered locally and the buyer supplied a valid business tax ID — customer self-accounts',
     NULL, FALSE, TRUE, 'reverse_charge', 'AE', 'AE', 0,
     'Reverse charge — the customer accounts for the tax due on this supply.', 200),
    ('no_regime',
     'Buyer''s country operates no indirect tax on this supply — outside the scope of VAT/GST',
     NULL, NULL, NULL, 'out_of_scope', 'O', 'O', 0,
     'Outside the scope of VAT/GST in the place of supply.', 900)
ON CONFLICT (rule_key) DO NOTHING;

-- ── RLS enrolment ────────────────────────────────────────────────────────────
-- platform_invoices is a platform control-plane artifact excluded from tenant RLS
-- (see stage 19/22/53); its line sub-ledger, numbering and tax config inherit that
-- classification. tenant_billing_plan_items is tenant-keyed but is written only by
-- Platform Admin, so it follows the same system-role policy as its parent.
DO $$
DECLARE
    tbl  TEXT;
    pol  RECORD;
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_system') THEN
        FOREACH tbl IN ARRAY ARRAY[
            'platform_invoice_lines','platform_invoice_sequences',
            'tenant_billing_plan_items','platform_tax_registrations','platform_tax_rules'
        ] LOOP
            IF to_regclass('public.' || tbl) IS NOT NULL THEN
                EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', tbl);
                EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', tbl);
                FOR pol IN SELECT policyname FROM pg_policies
                            WHERE schemaname='public' AND tablename=tbl LOOP
                    EXECUTE format('DROP POLICY %I ON public.%I', pol.policyname, tbl);
                END LOOP;
                EXECUTE format(
                    'CREATE POLICY system_control_plane ON public.%I FOR ALL TO opstrax_system USING(true) WITH CHECK(true)', tbl);
                EXECUTE format(
                    'GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.%I TO opstrax_system', tbl);
            END IF;
        END LOOP;
    END IF;
END $$;

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_18_stage81_platform_billing_itemization',
        'Platform billing itemization, per-feature commercial terms and indirect tax determination')
ON CONFLICT (version) DO NOTHING;

COMMIT;
