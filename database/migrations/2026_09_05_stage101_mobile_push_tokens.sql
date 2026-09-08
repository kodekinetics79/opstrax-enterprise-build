-- Stage 101 — tenant/user-scoped mobile push token lifecycle.
--
-- Stores provider tokens required for native Driver/Fleet/Customer notifications.
-- Tokens are never accepted for another user/tenant from request input: handlers bind
-- company_id and user_id exclusively from the authenticated server session.
BEGIN;

CREATE TABLE IF NOT EXISTS mobile_device_tokens (
    id                  BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id          BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    user_id             BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    product             VARCHAR(20) NOT NULL CHECK (product IN ('driver','fleet','customer')),
    platform            VARCHAR(20) NOT NULL CHECK (platform IN ('ios','android')),
    provider            VARCHAR(20) NOT NULL DEFAULT 'expo' CHECK (provider IN ('expo')),
    push_token          TEXT NOT NULL CHECK (char_length(push_token) BETWEEN 8 AND 4096),
    token_fingerprint   CHAR(64) NOT NULL CHECK (token_fingerprint ~ '^[0-9a-f]{64}$'),
    app_version         VARCHAR(40) NULL,
    device_os_version   VARCHAR(80) NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','revoked')),
    last_registered_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revoked_at          TIMESTAMPTZ NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NULL,
    CONSTRAINT ck_mobile_device_token_revocation
      CHECK ((status='active' AND revoked_at IS NULL) OR (status='revoked' AND revoked_at IS NOT NULL)),
    CONSTRAINT uq_mobile_device_token_company_fingerprint UNIQUE (company_id, token_fingerprint)
);

CREATE INDEX IF NOT EXISTS idx_mobile_device_tokens_user_active
  ON mobile_device_tokens(company_id,user_id,product,platform,last_registered_at DESC)
  WHERE status='active';
CREATE INDEX IF NOT EXISTS idx_mobile_device_tokens_fingerprint
  ON mobile_device_tokens(company_id,token_fingerprint);

ALTER TABLE mobile_device_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE mobile_device_tokens FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON mobile_device_tokens;
DROP POLICY IF EXISTS platform_admin_bypass ON mobile_device_tokens;
DROP POLICY IF EXISTS tenant_ticket_app ON mobile_device_tokens;
DROP POLICY IF EXISTS system_control_plane ON mobile_device_tokens;
DO $stage101_rls$
BEGIN
  IF to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON mobile_device_tokens FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    CREATE POLICY system_control_plane ON mobile_device_tokens FOR ALL TO opstrax_system
      USING (true) WITH CHECK (true);
  ELSE
    CREATE POLICY tenant_isolation ON mobile_device_tokens FOR ALL
      USING (company_id=NULLIF(current_setting('app.current_tenant_id',true),'')::BIGINT)
      WITH CHECK (company_id=NULLIF(current_setting('app.current_tenant_id',true),'')::BIGINT);
    CREATE POLICY platform_admin_bypass ON mobile_device_tokens FOR ALL
      USING (NULLIF(current_setting('app.platform_admin',true),'')='on')
      WITH CHECK (NULLIF(current_setting('app.platform_admin',true),'')='on');
  END IF;
END
$stage101_rls$;

REVOKE ALL ON TABLE mobile_device_tokens FROM PUBLIC;
REVOKE ALL ON SEQUENCE mobile_device_tokens_id_seq FROM PUBLIC;
GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE mobile_device_tokens TO opstrax_app;
GRANT USAGE,SELECT ON SEQUENCE mobile_device_tokens_id_seq TO opstrax_app;
DO $stage101_system_grants$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE mobile_device_tokens TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE mobile_device_tokens_id_seq TO opstrax_system;
  END IF;
END
$stage101_system_grants$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_05_stage101_mobile_push_tokens',
        'Tenant and authenticated-user scoped mobile push token registration and revocation')
ON CONFLICT (version) DO NOTHING;

COMMIT;
