-- Stage 38 — Tax engine (indirect tax: VAT / GST / ZATCA-VAT), ADR-008 P3
-- Closes the hardcoded-zero-tax hole. A config-driven tax sub-ledger derived beside the invoice chain:
--   tax_profiles + tax_rules (decision table) + customer_tax_status + seller_tax_registration (config)
--   invoice_tax_lines (mutable draft breakdown) -> issued_invoice_tax_lines (immutable snapshot)
--
-- Owner migration for restricted-role prod (skips the ensure + boot reconciler): creates the tables,
-- adds the nullable pin columns, and RLS-enrolls the new tables (mirrors stage37). Fully IF NOT EXISTS
-- / additive / idempotent. Backward-compatible: absence of a published profile => tax_total=0 as today.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS tax_profiles (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    profile_code VARCHAR(80) NOT NULL,
    profile_name VARCHAR(220) NOT NULL,
    regime VARCHAR(24) NOT NULL DEFAULT 'vat',
    price_inclusive BOOLEAN NOT NULL DEFAULT FALSE,
    freight_taxable BOOLEAN NOT NULL DEFAULT TRUE,
    currency VARCHAR(10) NULL,
    rounding_strategy VARCHAR(24) NOT NULL DEFAULT 'half_up',
    effective_date DATE NOT NULL,
    expiry_date DATE NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'draft',
    author_user_id BIGINT NULL,
    published_by_user_id BIGINT NULL,
    published_at TIMESTAMPTZ NULL,
    approval_request_id VARCHAR(120) NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS tax_rules (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    tax_profile_id BIGINT NOT NULL REFERENCES tax_profiles(id) ON DELETE CASCADE,
    match_customer_id BIGINT NULL,
    match_charge_code VARCHAR(80) NULL,
    match_charge_type VARCHAR(40) NULL,
    match_jurisdiction VARCHAR(120) NULL,
    tax_code VARCHAR(40) NOT NULL,
    tax_category VARCHAR(8) NULL,
    exemption_reason_code VARCHAR(40) NULL,
    rate NUMERIC(9,6) NOT NULL DEFAULT 0,
    taxable BOOLEAN NOT NULL DEFAULT TRUE,
    priority INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS customer_tax_status (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    customer_id BIGINT NOT NULL,
    tax_exempt BOOLEAN NOT NULL DEFAULT FALSE,
    exemption_reason VARCHAR(120) NULL,
    exemption_reason_code VARCHAR(40) NULL,
    exemption_certificate VARCHAR(160) NULL,
    tax_registration_no VARCHAR(80) NULL,
    jurisdiction VARCHAR(120) NULL,
    effective_date DATE NOT NULL DEFAULT CURRENT_DATE,
    expiry_date DATE NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS seller_tax_registration (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    jurisdiction VARCHAR(120) NOT NULL,
    regime VARCHAR(24) NOT NULL,
    tax_registration_no VARCHAR(80) NOT NULL,
    legal_name VARCHAR(220) NULL,
    effective_date DATE NOT NULL DEFAULT CURRENT_DATE,
    expiry_date DATE NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS invoice_tax_lines (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id BIGINT NOT NULL,
    invoice_draft_id UUID NOT NULL REFERENCES invoice_drafts(id) ON DELETE CASCADE,
    invoice_draft_line_id UUID NOT NULL REFERENCES invoice_draft_lines(id) ON DELETE CASCADE,
    job_charge_id BIGINT NULL,
    regime VARCHAR(24) NOT NULL,
    tax_code VARCHAR(40) NOT NULL,
    tax_category VARCHAR(8) NULL,
    exemption_reason_code VARCHAR(40) NULL,
    jurisdiction VARCHAR(120) NULL,
    taxable_amount NUMERIC(18,2) NOT NULL DEFAULT 0,
    rate NUMERIC(9,6) NOT NULL DEFAULT 0,
    tax_amount NUMERIC(18,2) NOT NULL DEFAULT 0,
    price_inclusive BOOLEAN NOT NULL DEFAULT FALSE,
    exempt_reason VARCHAR(120) NULL,
    tax_profile_id BIGINT NULL,
    tax_point_date DATE NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS issued_invoice_tax_lines (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id BIGINT NOT NULL,
    issued_invoice_id UUID NOT NULL REFERENCES issued_invoices(id) ON DELETE CASCADE,
    issued_invoice_line_id UUID NULL,
    source_invoice_tax_line_id UUID NULL,
    job_charge_id BIGINT NULL,
    regime VARCHAR(24) NOT NULL,
    tax_code VARCHAR(40) NOT NULL,
    tax_category VARCHAR(8) NULL,
    exemption_reason_code VARCHAR(40) NULL,
    jurisdiction VARCHAR(120) NULL,
    taxable_amount NUMERIC(18,2) NOT NULL DEFAULT 0,
    rate NUMERIC(9,6) NOT NULL DEFAULT 0,
    tax_amount NUMERIC(18,2) NOT NULL DEFAULT 0,
    price_inclusive BOOLEAN NOT NULL DEFAULT FALSE,
    exempt_reason VARCHAR(120) NULL,
    tax_profile_id BIGINT NULL,
    tax_point_date DATE NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS tax_profile_id BIGINT NULL;
ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS tax_point_date DATE NULL;
ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS tax_profile_id BIGINT NULL;
ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS tax_point_date DATE NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_tax_profiles_company_code ON tax_profiles (company_id, profile_code);
CREATE INDEX IF NOT EXISTS idx_tax_profiles_lookup ON tax_profiles (company_id, status, effective_date DESC);
CREATE INDEX IF NOT EXISTS idx_tax_rules_profile ON tax_rules (company_id, tax_profile_id, priority DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_customer_tax_status ON customer_tax_status (company_id, customer_id, effective_date);
CREATE INDEX IF NOT EXISTS idx_customer_tax_status_lookup ON customer_tax_status (company_id, customer_id, effective_date DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_seller_tax_registration ON seller_tax_registration (company_id, jurisdiction, regime, effective_date);
CREATE INDEX IF NOT EXISTS idx_seller_tax_registration_lookup ON seller_tax_registration (company_id, jurisdiction, regime, effective_date DESC);
CREATE INDEX IF NOT EXISTS idx_invoice_tax_lines_draft ON invoice_tax_lines (company_id, invoice_draft_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_invoice_tax_lines_line ON invoice_tax_lines (company_id, invoice_draft_line_id, tax_code);
CREATE INDEX IF NOT EXISTS idx_issued_invoice_tax_lines_invoice ON issued_invoice_tax_lines (company_id, issued_invoice_id);

-- RLS enrollment (prod skips the boot reconciler). invoice_drafts/issued_invoices are already enrolled;
-- their new nullable columns need no re-enroll.
DO $rls$
DECLARE
    t text;
    tbls text[] := ARRAY['tax_profiles','tax_rules','customer_tax_status','seller_tax_registration','invoice_tax_lines','issued_invoice_tax_lines'];
BEGIN
    FOREACH t IN ARRAY tbls LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t);

        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=t AND policyname='tenant_isolation') THEN
            EXECUTE format($p$
                CREATE POLICY tenant_isolation ON public.%I
                FOR ALL
                USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
                WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            $p$, t);
        END IF;

        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=t AND policyname='platform_admin_bypass') THEN
            EXECUTE format($p$
                CREATE POLICY platform_admin_bypass ON public.%I
                FOR ALL
                USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
                WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            $p$, t);
        END IF;
    END LOOP;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON tax_profiles, tax_rules, customer_tax_status, seller_tax_registration, invoice_tax_lines, issued_invoice_tax_lines TO opstrax_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
    END IF;
END
$rls$;
