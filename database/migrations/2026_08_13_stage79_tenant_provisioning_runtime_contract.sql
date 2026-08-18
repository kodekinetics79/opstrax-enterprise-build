-- Stage 79 — Protected-environment tenant provisioning runtime contract
--
-- Protected Staging and Production skip owner-capable runtime schema services.
-- TenantCreate nevertheless writes the extended company profile and subscription,
-- mints a tenant-admin password-reset token, and seeds the tenant feature flags.
-- Keep those writes owner-migrated so provisioning never depends on boot-time DDL.

BEGIN;

ALTER TABLE companies ADD COLUMN IF NOT EXISTS legal_name VARCHAR(220) NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS website VARCHAR(200) NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS fleet_size INT NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS tax_id VARCHAR(80) NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_name VARCHAR(160) NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_email VARCHAR(200) NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_phone VARCHAR(40) NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS billing_email VARCHAR(200) NULL;

ALTER TABLE tenant_subscriptions
  ADD COLUMN IF NOT EXISTS billing_cycle VARCHAR(20) NOT NULL DEFAULT 'monthly';

CREATE TABLE IF NOT EXISTS password_reset_tokens (
  user_id BIGINT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
  company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  token_hash VARCHAR(64) NOT NULL UNIQUE,
  expires_at TIMESTAMPTZ NOT NULL,
  consumed_at TIMESTAMPTZ NULL,
  request_ip_hash VARCHAR(16) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_password_reset_expiry
  ON password_reset_tokens (expires_at) WHERE consumed_at IS NULL;

CREATE TABLE IF NOT EXISTS feature_flags (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  flag_key VARCHAR(120) NOT NULL,
  name VARCHAR(200) NOT NULL,
  description TEXT NULL,
  enabled BOOLEAN NOT NULL DEFAULT TRUE,
  rollout_pct INT NOT NULL DEFAULT 100,
  environment VARCHAR(40) NOT NULL DEFAULT 'production',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  CONSTRAINT uq_feature_flags_company_key UNIQUE (company_id, flag_key)
);
CREATE INDEX IF NOT EXISTS idx_feature_flags_company
  ON feature_flags (company_id, flag_key);

-- Tenant login and invite ownership are case-insensitive. The same email may
-- legitimately belong to multiple tenants because company_code disambiguates
-- authentication; only duplicates inside one tenant make the lookup ambiguous.
DO $email_collision_preflight$
BEGIN
  IF EXISTS (
    SELECT 1
    FROM users
    GROUP BY company_id, lower(email)
    HAVING count(*) > 1
  ) THEN
    RAISE EXCEPTION 'Stage79 found a within-tenant case-insensitive email collision';
  END IF;
END
$email_collision_preflight$;

DO $drop_global_user_email_unique$
DECLARE constraint_name text;
BEGIN
  FOR constraint_name IN
    SELECT con.conname
    FROM pg_constraint con
    WHERE con.conrelid='users'::regclass
      AND con.contype='u'
      AND con.conkey=ARRAY[(SELECT attnum FROM pg_attribute
        WHERE attrelid='users'::regclass AND attname='email')]::smallint[]
  LOOP
    EXECUTE format('ALTER TABLE users DROP CONSTRAINT %I', constraint_name);
  END LOOP;
END
$drop_global_user_email_unique$;

DROP INDEX IF EXISTS users_email_key;
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_company_email_ci
  ON users (company_id, lower(email));

-- Stage58 is terminally reapplied by the supported owner runner after this
-- migration. Until then these new tenant tables remain owner-only: this migration
-- deliberately grants no runtime or PUBLIC privileges and creates no legacy-GUC
-- policy that could reopen the non-forgeable tenant boundary.
REVOKE ALL ON TABLE password_reset_tokens, feature_flags FROM PUBLIC;
REVOKE ALL ON SEQUENCE feature_flags_id_seq FROM PUBLIC;

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_13_stage79_tenant_provisioning_runtime_contract',
        'Protected-environment tenant provisioning runtime contract')
ON CONFLICT (version) DO NOTHING;

COMMIT;
