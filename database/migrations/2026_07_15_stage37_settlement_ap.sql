-- Stage 37 — Settlement / carrier-&-driver-pay (AP), ADR-007 §C
-- The AP mirror of the AR chain (invoice_drafts → _lines → issued_invoices → payments):
--   pay_agreements (AP analogue of rate_cards)
--   settlement_statements → settlement_lines → settlement_payments
--
-- Owner migration for restricted-role prod, which skips SettlementSchemaService.EnsureAsync AND the
-- boot-final RlsReconciliationSchemaService — so this file both creates the tables and enrolls them
-- into RLS (tenant_isolation + platform_admin_bypass + FORCE + grant to opstrax_app), mirroring the
-- stage22 reconcile pattern. All IF NOT EXISTS / additive so it is re-runnable and drift-safe.

CREATE TABLE IF NOT EXISTS pay_agreements (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    agreement_code VARCHAR(80) NOT NULL,
    agreement_name VARCHAR(220) NOT NULL,
    payee_type VARCHAR(20) NOT NULL DEFAULT 'driver',
    payee_id BIGINT NULL,
    basis VARCHAR(20) NOT NULL DEFAULT 'per_mile',
    rate DECIMAL(12,4) NOT NULL DEFAULT 0,
    min_pay DECIMAL(12,2) NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    effective_date DATE NOT NULL,
    expiry_date DATE NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'active',
    notes TEXT NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS settlement_statements (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    statement_no VARCHAR(80) NOT NULL,
    payee_type VARCHAR(20) NOT NULL DEFAULT 'driver',
    payee_id BIGINT NOT NULL,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'draft',
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    subtotal DECIMAL(18,2) NOT NULL DEFAULT 0,
    total DECIMAL(18,2) NOT NULL DEFAULT 0,
    amount_paid DECIMAL(18,2) NOT NULL DEFAULT 0,
    source VARCHAR(40) NOT NULL DEFAULT 'system',
    pay_agreement_id BIGINT NULL,
    approved_by_user_id BIGINT NULL,
    approved_at TIMESTAMPTZ NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS settlement_lines (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    statement_id BIGINT NOT NULL REFERENCES settlement_statements(id) ON DELETE CASCADE,
    job_id BIGINT NULL,
    line_no INT NOT NULL,
    pay_code VARCHAR(80) NOT NULL DEFAULT 'linehaul',
    description TEXT NOT NULL,
    basis VARCHAR(20) NOT NULL,
    basis_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    quantity DECIMAL(18,3) NOT NULL DEFAULT 1,
    unit_rate DECIMAL(18,4) NOT NULL DEFAULT 0,
    amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    pay_agreement_id BIGINT NULL,
    source VARCHAR(20) NOT NULL DEFAULT 'settlement',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS settlement_payments (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    statement_id BIGINT NOT NULL REFERENCES settlement_statements(id) ON DELETE CASCADE,
    amount DECIMAL(18,2) NOT NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    method VARCHAR(40) NULL,
    reference VARCHAR(120) NULL,
    idempotency_key VARCHAR(160) NULL,
    paid_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id BIGINT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_pay_agreements_company_code ON pay_agreements (company_id, agreement_code);
CREATE INDEX IF NOT EXISTS idx_pay_agreements_lookup ON pay_agreements (company_id, payee_type, payee_id, status, effective_date DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_statements_company_no ON settlement_statements (company_id, statement_no);
CREATE INDEX IF NOT EXISTS idx_settlement_statements_payee ON settlement_statements (company_id, payee_type, payee_id, status, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_statements_period ON settlement_statements (company_id, payee_type, payee_id, period_start, period_end) WHERE source = 'system';

CREATE INDEX IF NOT EXISTS idx_settlement_lines_company_statement ON settlement_lines (company_id, statement_id, line_no);
CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_lines_job ON settlement_lines (company_id, statement_id, job_id) WHERE job_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_settlement_payments_company_statement ON settlement_payments (company_id, statement_id, paid_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_payments_idem ON settlement_payments (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

-- RLS enrollment (prod skips the boot reconciler). tenant_isolation scopes every row to the
-- session tenant; platform_admin_bypass lets the control plane cross tenants; FORCE applies it to
-- the table owner too. Idempotent: policies created only if absent.
DO $rls$
DECLARE
    t text;
    tbls text[] := ARRAY['pay_agreements','settlement_statements','settlement_lines','settlement_payments'];
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
        GRANT SELECT, INSERT, UPDATE, DELETE ON pay_agreements, settlement_statements, settlement_lines, settlement_payments TO opstrax_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
    END IF;
END
$rls$;
