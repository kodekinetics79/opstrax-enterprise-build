-- Stage 49 — durable one-time MFA login challenge consumption.
-- Apply as the database owner before deploying the restricted runtime role.
BEGIN;

CREATE TABLE IF NOT EXISTS mfa_login_challenge_consumptions (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    challenge_hash VARCHAR(64) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    consumed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_mfa_challenge_hash_sha256
        CHECK (challenge_hash ~ '^[0-9a-f]{64}$')
);

DO $constraint$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid='public.mfa_login_challenge_consumptions'::regclass
          AND conname='ck_mfa_challenge_hash_sha256'
    ) THEN
        ALTER TABLE mfa_login_challenge_consumptions
            ADD CONSTRAINT ck_mfa_challenge_hash_sha256
            CHECK (challenge_hash ~ '^[0-9a-f]{64}$');
    END IF;
END
$constraint$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_mfa_challenge_consumptions_hash
    ON mfa_login_challenge_consumptions (challenge_hash);

CREATE INDEX IF NOT EXISTS idx_mfa_challenge_consumptions_expiry
    ON mfa_login_challenge_consumptions (expires_at);
CREATE INDEX IF NOT EXISTS idx_mfa_challenge_consumptions_tenant_user
    ON mfa_login_challenge_consumptions (company_id, user_id);

ALTER TABLE mfa_login_challenge_consumptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE mfa_login_challenge_consumptions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON mfa_login_challenge_consumptions;
CREATE POLICY tenant_isolation ON mfa_login_challenge_consumptions FOR ALL
    USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
    WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);

DROP POLICY IF EXISTS platform_admin_bypass ON mfa_login_challenge_consumptions;
CREATE POLICY platform_admin_bypass ON mfa_login_challenge_consumptions FOR ALL
    USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
    WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');

DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, DELETE ON mfa_login_challenge_consumptions TO opstrax_app;
        REVOKE UPDATE ON mfa_login_challenge_consumptions FROM opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE mfa_login_challenge_consumptions_id_seq TO opstrax_app;
    END IF;
END
$grant$;

COMMIT;
