-- 2026-07-14 — feature_flags table (owner migration)
--
-- PURPOSE
--   The REAL feature-flag system (FeatureFlagService, the Program.cs route
--   kill-switch, and the UI's GET /api/feature-flags/evaluate) shipped without a
--   table owner. On any database where a boot-time schema service could not run
--   the DDL (production connects as the restricted, non-DDL opstrax_app role under
--   RLS enforcement), the first flag read failed with
--       relation "feature_flags" does not exist
--   which also broke tenant provisioning (TenantCreate -> SeedDefaultsAsync).
--
--   The runtime owner is FeatureFlagSchemaService.EnsureAsync (CREATE IF NOT EXISTS,
--   self-healing on every boot). This migration is the owner-role counterpart for
--   environments where boot DDL is disabled. Idempotent and additive.
--
-- TENANCY / RLS
--   feature_flags is tenant-scoped (company_id BIGINT), so it is enrolled into
--   Row-Level Security by the Stage 19/22 tenant_isolation reconciliation (and, at
--   runtime, by RlsReconciliationSchemaService). The enrollment block below makes
--   this migration self-sufficient when applied standalone.

BEGIN;

CREATE TABLE IF NOT EXISTS feature_flags (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id   BIGINT       NOT NULL,
    flag_key     VARCHAR(120) NOT NULL,
    name         VARCHAR(200) NOT NULL,
    description  TEXT         NULL,
    enabled      BOOLEAN      NOT NULL DEFAULT TRUE,
    rollout_pct  INT          NOT NULL DEFAULT 100,
    environment  VARCHAR(40)  NOT NULL DEFAULT 'production',
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ  NULL,
    CONSTRAINT uq_feature_flags_company_key UNIQUE (company_id, flag_key)
);

CREATE INDEX IF NOT EXISTS idx_feature_flags_company ON feature_flags (company_id, flag_key);

-- Enroll into RLS (tenant_isolation + platform_admin_bypass) + FORCE, matching Stage 19/22.
ALTER TABLE feature_flags ENABLE ROW LEVEL SECURITY;
ALTER TABLE feature_flags FORCE ROW LEVEL SECURITY;

DO $ff$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'feature_flags' AND policyname = 'tenant_isolation'
    ) THEN
        CREATE POLICY tenant_isolation ON public.feature_flags
        FOR ALL
        USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
        WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'feature_flags' AND policyname = 'platform_admin_bypass'
    ) THEN
        CREATE POLICY platform_admin_bypass ON public.feature_flags
        FOR ALL
        USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
        WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON public.feature_flags TO opstrax_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
    END IF;
END
$ff$;

COMMIT;
