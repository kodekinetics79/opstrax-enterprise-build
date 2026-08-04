-- Stage 46 — GL period close (maker-checker lock) + ERP journal export audit.
-- gl_periods: open -> pending_close -> closed state machine with dual-control user ids, frozen totals
-- and a deterministic close checksum. gl_export_runs: append-only export audit. The trigger is the HARD
-- back-posting lock: any INSERT into journal_entries dated inside a closed period is rejected, blocking
-- every writer (AR/AP handlers, backfills), not just the service layer. Additive and idempotent.
CREATE TABLE IF NOT EXISTS gl_periods (
    id                     BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id             BIGINT        NOT NULL,
    period_code            VARCHAR(20)   NOT NULL,
    period_start           DATE          NOT NULL,
    period_end             DATE          NOT NULL,
    status                 VARCHAR(20)   NOT NULL DEFAULT 'open',
    requested_by_user_id   BIGINT        NULL,
    requested_at           TIMESTAMPTZ   NULL,
    closed_by_user_id      BIGINT        NULL,
    closed_at              TIMESTAMPTZ   NULL,
    close_checksum         VARCHAR(80)   NULL,
    total_debits           NUMERIC(18,2) NOT NULL DEFAULT 0,
    total_credits          NUMERIC(18,2) NOT NULL DEFAULT 0,
    entry_count            INT           NOT NULL DEFAULT 0,
    created_at             TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at             TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, period_code)
);

CREATE TABLE IF NOT EXISTS gl_export_runs (
    id                   BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id           BIGINT        NOT NULL,
    period_code          VARCHAR(20)   NOT NULL,
    format               VARCHAR(20)   NOT NULL,
    row_count            INT           NOT NULL DEFAULT 0,
    total_debits         NUMERIC(18,2) NOT NULL DEFAULT 0,
    total_credits        NUMERIC(18,2) NOT NULL DEFAULT 0,
    checksum             VARCHAR(80)   NULL,
    file_name            TEXT          NULL,
    exported_by_user_id  BIGINT        NULL,
    exported_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_gl_periods_lookup ON gl_periods (company_id, status, period_end);
CREATE INDEX IF NOT EXISTS ix_gl_export_runs ON gl_export_runs (company_id, period_code);

CREATE OR REPLACE FUNCTION gl_enforce_period_lock() RETURNS trigger AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM gl_periods p
               WHERE p.company_id = NEW.company_id AND p.status = 'closed'
                 AND NEW.entry_date BETWEEN p.period_start AND p.period_end) THEN
        RAISE EXCEPTION 'gl_period_closed: entry_date % is in a locked period for company %',
            NEW.entry_date, NEW.company_id USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_gl_period_lock ON journal_entries;
CREATE TRIGGER trg_gl_period_lock BEFORE INSERT ON journal_entries FOR EACH ROW EXECUTE FUNCTION gl_enforce_period_lock();

-- RLS + restricted-role grants (mirror stage40 pattern).
ALTER TABLE gl_periods ENABLE ROW LEVEL SECURITY;
ALTER TABLE gl_periods FORCE ROW LEVEL SECURITY;
ALTER TABLE gl_export_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE gl_export_runs FORCE ROW LEVEL SECURITY;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename='gl_periods' AND policyname='tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON gl_periods FOR ALL
            USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename='gl_periods' AND policyname='platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON gl_periods FOR ALL
            USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename='gl_export_runs' AND policyname='tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON gl_export_runs FOR ALL
            USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename='gl_export_runs' AND policyname='platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON gl_export_runs FOR ALL
            USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
END $$;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON gl_periods, gl_export_runs TO opstrax_app;
    END IF;
END $$;
