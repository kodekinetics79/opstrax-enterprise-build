BEGIN;
CREATE TABLE IF NOT EXISTS password_reset_tokens (
  user_id BIGINT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
  company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  token_hash VARCHAR(64) NOT NULL UNIQUE,
  expires_at TIMESTAMPTZ NOT NULL,
  consumed_at TIMESTAMPTZ NULL,
  request_ip_hash VARCHAR(16) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_password_reset_expiry ON password_reset_tokens (expires_at) WHERE consumed_at IS NULL;
ALTER TABLE password_reset_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE password_reset_tokens FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_password_reset_tokens ON password_reset_tokens;
CREATE POLICY tenant_isolation_password_reset_tokens ON password_reset_tokens
  USING (
    current_setting('app.platform_admin_bypass', true) = 'true'
    OR company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint
  )
  WITH CHECK (
    current_setting('app.platform_admin_bypass', true) = 'true'
    OR company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint
  );
COMMIT;
