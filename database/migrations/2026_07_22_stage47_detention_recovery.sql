-- Stage 47 — Detention Recovery (owner migration for restricted-role prod; mirrors
-- DetentionSchemaService exactly). Additive + idempotent. Includes RLS + grants so no separate step.
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS customer_id BIGINT NULL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS site_role VARCHAR(30) NULL;
CREATE INDEX IF NOT EXISTS idx_geofences_company_customer ON geofences (company_id, customer_id) WHERE customer_id IS NOT NULL;

ALTER TABLE jobs ADD COLUMN IF NOT EXISTS po_number VARCHAR(80) NULL;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS bol_number VARCHAR(80) NULL;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS rate_con_number VARCHAR(80) NULL;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS appointment_ref VARCHAR(80) NULL;

CREATE TABLE IF NOT EXISTS detention_rule_cards (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    scope_type VARCHAR(20) NOT NULL DEFAULT 'customer',
    scope_id BIGINT NULL,
    free_minutes INT NOT NULL DEFAULT 120,
    rate_per_hour DECIMAL(12,2) NOT NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    billing_increment_minutes INT NOT NULL DEFAULT 15,
    max_charge_amount DECIMAL(12,2) NULL,
    claim_window_days INT NOT NULL DEFAULT 30,
    notice_percent INT NOT NULL DEFAULT 75,
    grace_minutes INT NOT NULL DEFAULT 0,
    merge_gap_minutes INT NOT NULL DEFAULT 10,
    max_dwell_hours INT NOT NULL DEFAULT 24,
    version INT NOT NULL DEFAULT 1,
    effective_date DATE NOT NULL DEFAULT CURRENT_DATE,
    active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_detention_rule_cards ON detention_rule_cards (company_id, scope_type, scope_id, active);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_rule_card_active ON detention_rule_cards (company_id, scope_type, COALESCE(scope_id, 0)) WHERE active;

CREATE TABLE IF NOT EXISTS detention_dwells (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    geofence_id BIGINT NOT NULL, vehicle_id BIGINT NOT NULL, driver_id BIGINT NULL,
    customer_id BIGINT NULL, job_id BIGINT NULL, dispatch_assignment_id BIGINT NULL,
    stop_role VARCHAR(20) NULL,
    entry_event_id BIGINT NOT NULL, exit_event_id BIGINT NULL,
    entered_at TIMESTAMPTZ NOT NULL, exited_at TIMESTAMPTZ NULL,
    arrival_lower TIMESTAMPTZ NULL, arrival_upper TIMESTAMPTZ NULL,
    departure_lower TIMESTAMPTZ NULL, departure_upper TIMESTAMPTZ NULL,
    billed_from_at TIMESTAMPTZ NULL, billed_to_at TIMESTAMPTZ NULL,
    appointment_at TIMESTAMPTZ NULL,
    appointment_source VARCHAR(30) NULL,
    clock_start_at TIMESTAMPTZ NULL,
    clock_rule VARCHAR(60) NULL,
    dwell_minutes INT NULL, free_minutes_applied INT NULL, billable_minutes INT NULL,
    rule_card_id BIGINT NULL, rule_card_version INT NULL,
    quantity_hours DECIMAL(8,3) NULL, unit_rate DECIMAL(12,2) NULL, amount DECIMAL(12,2) NULL, currency VARCHAR(10) NULL,
    warning_notified_at TIMESTAMPTZ NULL,
    claim_deadline_at TIMESTAMPTZ NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'open',
    close_reason VARCHAR(40) NULL,
    truncated BOOLEAN NOT NULL DEFAULT FALSE,
    review_required BOOLEAN NOT NULL DEFAULT TRUE,
    reviewed_by_user_id BIGINT NULL, reviewed_at TIMESTAMPTZ NULL, review_note TEXT NULL,
    job_charge_id BIGINT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_dwell_entry ON detention_dwells (company_id, entry_event_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_dwell_open ON detention_dwells (company_id, geofence_id, vehicle_id) WHERE status='open';
CREATE INDEX IF NOT EXISTS idx_detention_dwells_company_status ON detention_dwells (company_id, status, entered_at DESC);

CREATE TABLE IF NOT EXISTS detention_dwell_events (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    dwell_id BIGINT NOT NULL,
    geofence_event_id BIGINT NOT NULL,
    event_type VARCHAR(10) NOT NULL,
    event_time TIMESTAMPTZ NOT NULL,
    consume_role VARCHAR(30) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_event_consumed ON detention_dwell_events (company_id, geofence_event_id);
CREATE INDEX IF NOT EXISTS idx_detention_dwell_events_dwell ON detention_dwell_events (company_id, dwell_id, event_time);

CREATE TABLE IF NOT EXISTS detention_notices (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL, dwell_id BIGINT NOT NULL,
    notice_type VARCHAR(30) NOT NULL,
    recipient_name VARCHAR(160) NULL, recipient_address VARCHAR(220) NULL,
    channel VARCHAR(20) NOT NULL DEFAULT 'email',
    body_snapshot TEXT NOT NULL,
    delivery_status VARCHAR(20) NOT NULL DEFAULT 'logged',
    sent_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_notice ON detention_notices (company_id, dwell_id, notice_type);

CREATE TABLE IF NOT EXISTS detention_evidence (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL, dwell_id BIGINT NOT NULL,
    schema_version INT NOT NULL DEFAULT 2,
    evidence_canonical TEXT NOT NULL,
    evidence_json JSONB NOT NULL,
    evidence_sha256 CHAR(64) NOT NULL,
    breadcrumb_count INT NOT NULL DEFAULT 0,
    breadcrumbs_included INT NOT NULL DEFAULT 0,
    full_trail_sha256 CHAR(64) NULL,
    share_token VARCHAR(64) NULL,
    share_expires_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_evidence_dwell ON detention_evidence (company_id, dwell_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_evidence_token ON detention_evidence (share_token) WHERE share_token IS NOT NULL;

CREATE OR REPLACE FUNCTION detention_evidence_immutable() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'detention_evidence is immutable';
    END IF;
    IF NEW.evidence_canonical IS DISTINCT FROM OLD.evidence_canonical
       OR NEW.evidence_json::text IS DISTINCT FROM OLD.evidence_json::text
       OR NEW.evidence_sha256 IS DISTINCT FROM OLD.evidence_sha256
       OR NEW.dwell_id IS DISTINCT FROM OLD.dwell_id
       OR NEW.company_id IS DISTINCT FROM OLD.company_id
       OR NEW.schema_version IS DISTINCT FROM OLD.schema_version
       OR NEW.breadcrumb_count IS DISTINCT FROM OLD.breadcrumb_count
       OR NEW.breadcrumbs_included IS DISTINCT FROM OLD.breadcrumbs_included
       OR NEW.full_trail_sha256 IS DISTINCT FROM OLD.full_trail_sha256
       OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
        RAISE EXCEPTION 'detention_evidence is immutable (only share fields may change)';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS trg_detention_evidence_immutable ON detention_evidence;
CREATE TRIGGER trg_detention_evidence_immutable BEFORE UPDATE OR DELETE ON detention_evidence FOR EACH ROW EXECUTE FUNCTION detention_evidence_immutable();

ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS detention_dwell_id BIGINT NULL;
ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS evidence_sha256 CHAR(64) NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_job_charges_detention ON job_charges (company_id, detention_dwell_id) WHERE detention_dwell_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_detention_priced ON outbox_messages (tenant_id, aggregate_id) WHERE event_type='detention.dwell.priced';
CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_detention_warning ON outbox_messages (tenant_id, aggregate_id) WHERE event_type='detention.dwell.warning';

-- RLS + restricted-role grants (mirror the reconciler's policy shape).
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['detention_rule_cards','detention_dwells','detention_dwell_events','detention_notices','detention_evidence'] LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t);
        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename=t AND policyname='tenant_isolation') THEN
            EXECUTE format($p$CREATE POLICY tenant_isolation ON public.%I FOR ALL
                USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
                WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)$p$, t);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename=t AND policyname='platform_admin_bypass') THEN
            EXECUTE format($p$CREATE POLICY platform_admin_bypass ON public.%I FOR ALL
                USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
                WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')$p$, t);
        END IF;
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t);
    END LOOP;
END $$;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON detention_rule_cards, detention_dwells, detention_dwell_events, detention_notices, detention_evidence TO opstrax_app;
    END IF;
END $$;
