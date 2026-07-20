-- 007_feature_flags.sql — a REAL feature-flag system.
--
-- Owner-applied (run as the DB owner, not the restricted opstrax_app role): under the
-- restricted-role + RLS deployment Program.cs skips schema init, so this must be applied
-- out-of-band. Idempotent — safe to re-run.
--
-- Replaces the removed fake Feature Flags screen, whose toggle changed no behaviour.
-- This one is consumed for real:
--   • server side  — Program.cs route kill-switch + FeatureFlagService.IsEnabledAsync()
--                    callable from ANY code path
--   • client side  — useFlag()/useFlags() gate real UI
--
-- Semantics:
--   enabled=false  -> hard OFF for everyone (kill switch wins over rollout)
--   rollout_pct    -> deterministic per-(flag,user) bucketing, so a 20% rollout is a
--                     STABLE 20% of users, not a coin flip on every request.

CREATE TABLE IF NOT EXISTS feature_flags (
    id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id   BIGINT       NOT NULL REFERENCES companies(id),
    flag_key     VARCHAR(120) NOT NULL,
    name         VARCHAR(160) NOT NULL,
    description  VARCHAR(400) NULL,
    enabled      BOOLEAN      NOT NULL DEFAULT false,
    rollout_pct  INT          NOT NULL DEFAULT 100,
    environment  VARCHAR(20)  NOT NULL DEFAULT 'production',
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by   VARCHAR(220) NULL,
    CONSTRAINT uq_feature_flags_company_key UNIQUE (company_id, flag_key),
    CONSTRAINT chk_feature_flags_rollout CHECK (rollout_pct BETWEEN 0 AND 100)
);

CREATE INDEX IF NOT EXISTS idx_feature_flags_company_key ON feature_flags (company_id, flag_key);

-- RLS: mirror the policy shape the runtime actually sets (app.current_tenant_id for
-- tenant scope, app.platform_admin='on' for the platform-admin bypass).
ALTER TABLE feature_flags ENABLE ROW LEVEL SECURITY;
ALTER TABLE feature_flags FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON feature_flags;
CREATE POLICY tenant_isolation ON feature_flags
    USING      (company_id = (NULLIF(current_setting('app.current_tenant_id', true), ''))::bigint)
    WITH CHECK (company_id = (NULLIF(current_setting('app.current_tenant_id', true), ''))::bigint);

DROP POLICY IF EXISTS platform_admin_bypass ON feature_flags;
CREATE POLICY platform_admin_bypass ON feature_flags
    USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
    WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');

-- Grant the restricted runtime role (only if it exists in this environment).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON feature_flags TO opstrax_app';
    END IF;
END $$;

-- First real flag: an AI kill switch. Seeded ENABLED so behaviour is unchanged, but an
-- operator can now genuinely stop every AI call tenant-wide during an incident (cost
-- spike, bad output, provider outage) without a deploy.
INSERT INTO feature_flags (company_id, flag_key, name, description, enabled, rollout_pct, environment)
SELECT c.id,
       'ai_copilot',
       'AI Copilot & Recommendations',
       'Kill switch for all AI features (/api/ai/*). Turn off to immediately stop AI calls tenant-wide.',
       true, 100, 'production'
FROM companies c
ON CONFLICT (company_id, flag_key) DO NOTHING;
