-- Stage 39 — Billing profiles + consolidation (ADR-008 Billing layer)
-- Tenant-configurable AR billing on top of the revenue chain: billing_profiles drive how a customer's
-- delivered-job charges consolidate into invoice_drafts; billing_consolidation_runs is the idempotency
-- anchor. Additive: a tenant with no billing_profiles behaves exactly like today (virtual LegacyDefault).
--
-- Owner migration for restricted-role prod (skips ensure + boot reconciler): creates the tables, adds the
-- additive columns, and RLS-enrolls the new tables (mirrors stage37/38). Fully IF NOT EXISTS / idempotent.

CREATE TABLE IF NOT EXISTS billing_profiles (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    profile_code VARCHAR(80) NOT NULL,
    profile_name VARCHAR(220) NOT NULL,
    scope_type VARCHAR(20) NOT NULL DEFAULT 'tenant',
    scope_id BIGINT NULL,
    cycle VARCHAR(20) NOT NULL DEFAULT 'immediate',
    consolidation VARCHAR(20) NOT NULL DEFAULT 'per_load',
    numbering_scheme VARCHAR(40) NOT NULL DEFAULT 'legacy_job',
    number_prefix VARCHAR(40) NULL,
    payment_terms_days INT NOT NULL DEFAULT 30,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    require_vat BOOLEAN NOT NULL DEFAULT FALSE,
    memo_default TEXT NULL,
    dunning_offsets_json JSONB NULL,
    version INT NOT NULL DEFAULT 1,
    status VARCHAR(40) NOT NULL DEFAULT 'active',
    effective_date DATE NOT NULL,
    expiry_date DATE NULL,
    config_set_id BIGINT NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS billing_consolidation_runs (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    billing_profile_id BIGINT NOT NULL,
    billing_profile_version INT NOT NULL,
    customer_id BIGINT NOT NULL,
    group_key VARCHAR(200) NOT NULL,
    period_start DATE NULL,
    period_end DATE NULL,
    invoice_draft_id UUID NULL,
    allocated_invoice_no VARCHAR(80) NULL,
    resolved_config_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    status VARCHAR(40) NOT NULL DEFAULT 'draft',
    charge_count INT NOT NULL DEFAULT 0,
    subtotal DECIMAL(18,2) NOT NULL DEFAULT 0,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    source VARCHAR(40) NOT NULL DEFAULT 'system',
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS billing_profile_id BIGINT NULL;
ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS payment_terms_days INT NULL;
ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS document_type VARCHAR(20) NOT NULL DEFAULT 'invoice';
ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS adjusts_invoice_id UUID NULL;
ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS document_type VARCHAR(20) NOT NULL DEFAULT 'invoice';
ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS adjusts_invoice_id UUID NULL;
ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS payment_terms_days INT NULL;
ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS billing_status VARCHAR(20) NOT NULL DEFAULT 'unbilled';

CREATE UNIQUE INDEX IF NOT EXISTS uq_billing_profiles_code_ver ON billing_profiles (company_id, profile_code, effective_date, version);
CREATE INDEX IF NOT EXISTS idx_billing_profiles_lookup ON billing_profiles (company_id, scope_type, scope_id, status, effective_date DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_billing_runs_group ON billing_consolidation_runs (company_id, billing_profile_id, customer_id, group_key) WHERE source='system';
CREATE INDEX IF NOT EXISTS idx_billing_runs_customer ON billing_consolidation_runs (company_id, customer_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_job_charges_billing_status ON job_charges (company_id, billing_status, job_id);

DO $rls$
DECLARE
    t text;
    tbls text[] := ARRAY['billing_profiles','billing_consolidation_runs'];
BEGIN
    FOREACH t IN ARRAY tbls LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t);
        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=t AND policyname='tenant_isolation') THEN
            EXECUTE format($p$
                CREATE POLICY tenant_isolation ON public.%I FOR ALL
                USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
                WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            $p$, t);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=t AND policyname='platform_admin_bypass') THEN
            EXECUTE format($p$
                CREATE POLICY platform_admin_bypass ON public.%I FOR ALL
                USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
                WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            $p$, t);
        END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON billing_profiles, billing_consolidation_runs TO opstrax_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
    END IF;
END
$rls$;
