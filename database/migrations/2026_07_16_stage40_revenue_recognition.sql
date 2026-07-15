-- Stage 40 — Revenue recognition sub-ledger (ADR-008 rev-rec layer)
-- Append-only accounting sub-ledger derived BESIDE issued_invoices (never mutates them). Two-tier
-- immutability: entries 'pending' + recomputable while their fiscal period is 'open'; period close
-- freezes them to 'posted'. P0: accrual + on_invoice only; everything else fails closed.
--
-- Owner migration for restricted-role prod (skips ensure + boot reconciler): creates the tables +
-- indexes + the two outbox idempotency indexes, and RLS-enrolls the new tables. IF NOT EXISTS /
-- idempotent. Additive: no revrec_profile => no recognition (today's behavior exactly).

CREATE TABLE IF NOT EXISTS revrec_profiles (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    profile_code VARCHAR(80) NOT NULL,
    profile_name VARCHAR(220) NOT NULL,
    method VARCHAR(20) NOT NULL DEFAULT 'accrual',
    trigger VARCHAR(20) NOT NULL DEFAULT 'on_invoice',
    recognize_base VARCHAR(20) NOT NULL DEFAULT 'total',
    ratable_periods INT NULL,
    functional_currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    revenue_account_code VARCHAR(40) NULL,
    deferred_revenue_account_code VARCHAR(40) NULL,
    calendar_id BIGINT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'published',
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    effective_from DATE NOT NULL DEFAULT DATE '1900-01-01',
    effective_to DATE NULL,
    config_set_id BIGINT NULL,
    notes TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS revrec_fiscal_calendars (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    calendar_code VARCHAR(80) NOT NULL,
    calendar_name VARCHAR(220) NOT NULL,
    period_type VARCHAR(20) NOT NULL DEFAULT 'monthly',
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS revrec_fiscal_periods (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    calendar_id BIGINT NULL,
    period_code VARCHAR(20) NOT NULL,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'open',
    entry_count INT NOT NULL DEFAULT 0,
    recognized_total_functional DECIMAL(18,2) NOT NULL DEFAULT 0,
    close_checksum VARCHAR(80) NULL,
    closed_at TIMESTAMPTZ NULL,
    closed_by_user_id BIGINT NULL,
    correlation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS revenue_recognition_entries (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    issued_invoice_id UUID NOT NULL,
    issued_invoice_line_id UUID NULL,
    customer_id BIGINT NULL,
    job_id BIGINT NULL,
    profile_id BIGINT NULL,
    fiscal_period_id BIGINT NULL,
    schedule_id BIGINT NULL,
    entry_type VARCHAR(20) NOT NULL DEFAULT 'recognition',
    recognition_date DATE NOT NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    amount DECIMAL(18,2) NOT NULL,
    functional_currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    fx_rate DECIMAL(18,8) NOT NULL DEFAULT 1,
    fx_date DATE NULL,
    amount_functional DECIMAL(18,2) NOT NULL,
    revenue_account_code VARCHAR(40) NULL,
    deferred_revenue_account_code VARCHAR(40) NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    source VARCHAR(20) NOT NULL DEFAULT 'system',
    reverses_entry_id BIGINT NULL,
    memo TEXT NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id BIGINT NULL
);

CREATE TABLE IF NOT EXISTS revrec_schedules (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    issued_invoice_id UUID NOT NULL,
    issued_invoice_line_id UUID NULL,
    profile_id BIGINT NULL,
    method VARCHAR(20) NOT NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    total_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS revrec_schedule_lines (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    schedule_id BIGINT NOT NULL REFERENCES revrec_schedules(id) ON DELETE CASCADE,
    seq INT NOT NULL,
    scheduled_date DATE NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    milestone_code VARCHAR(80) NULL,
    recognized_entry_id BIGINT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_profiles_company_code ON revrec_profiles (company_id, profile_code);
CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_profiles_default ON revrec_profiles (company_id) WHERE is_default;
CREATE INDEX IF NOT EXISTS idx_revrec_profiles_lookup ON revrec_profiles (company_id, status, effective_from DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_calendars_code ON revrec_fiscal_calendars (company_id, calendar_code);
CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_periods_company_code ON revrec_fiscal_periods (company_id, period_code);
CREATE INDEX IF NOT EXISTS idx_revrec_periods_range ON revrec_fiscal_periods (company_id, period_start, period_end);
CREATE INDEX IF NOT EXISTS idx_revrec_periods_open ON revrec_fiscal_periods (company_id, status, period_end);
CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_entries_invoice_system ON revenue_recognition_entries (company_id, issued_invoice_id) WHERE source='system' AND entry_type='recognition' AND issued_invoice_line_id IS NULL AND schedule_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_reversal ON revenue_recognition_entries (company_id, reverses_entry_id) WHERE entry_type='reversal';
CREATE INDEX IF NOT EXISTS idx_revrec_entries_invoice ON revenue_recognition_entries (company_id, issued_invoice_id);
CREATE INDEX IF NOT EXISTS idx_revrec_entries_period ON revenue_recognition_entries (company_id, fiscal_period_id, status);
CREATE INDEX IF NOT EXISTS idx_revrec_entries_date ON revenue_recognition_entries (company_id, recognition_date);
CREATE INDEX IF NOT EXISTS idx_revrec_schedule_lines_sched ON revrec_schedule_lines (company_id, schedule_id, seq);

-- Outbox idempotency for the revenue.recognized derive-beside event (invoice.issued is already
-- enqueued once per issue by events.Publish; the recognition handler is idempotent).
CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_revenue_recognized ON outbox_messages (tenant_id, aggregate_id) WHERE event_type='revenue.recognized';

DO $rls$
DECLARE
    t text;
    tbls text[] := ARRAY['revrec_profiles','revrec_fiscal_calendars','revrec_fiscal_periods','revenue_recognition_entries','revrec_schedules','revrec_schedule_lines'];
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
        GRANT SELECT, INSERT, UPDATE, DELETE ON revrec_profiles, revrec_fiscal_calendars, revrec_fiscal_periods, revenue_recognition_entries, revrec_schedules, revrec_schedule_lines TO opstrax_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
    END IF;
END
$rls$;
