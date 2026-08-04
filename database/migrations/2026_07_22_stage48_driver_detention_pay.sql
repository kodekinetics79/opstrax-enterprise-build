-- Stage 48 — Detention -> driver pay policy (the differentiator: we collect detention AND pay drivers).
-- One active policy per tenant; fail-closed (no enabled policy => drivers paid no detention). Detention
-- pay lines are DERIVED during settlement generation (delete-and-recompute safe). Additive + idempotent.
CREATE TABLE IF NOT EXISTS driver_detention_pay_policy (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT FALSE,
    trigger_state VARCHAR(20) NOT NULL DEFAULT 'collected',
    share_type VARCHAR(20) NOT NULL DEFAULT 'percent',
    share_value DECIMAL(12,2) NOT NULL DEFAULT 0,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (company_id)
);

ALTER TABLE driver_detention_pay_policy ENABLE ROW LEVEL SECURITY;
ALTER TABLE driver_detention_pay_policy FORCE ROW LEVEL SECURITY;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename='driver_detention_pay_policy' AND policyname='tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON driver_detention_pay_policy FOR ALL
            USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename='driver_detention_pay_policy' AND policyname='platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON driver_detention_pay_policy FOR ALL
            USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON driver_detention_pay_policy TO opstrax_app;
    END IF;
END $$;
