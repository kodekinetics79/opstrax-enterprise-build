ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS device_state VARCHAR(40) NOT NULL DEFAULT 'Provisioned';
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS retired_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS row_version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS api_key_hash VARCHAR(64) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_secret VARCHAR(128) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_secret_encrypted TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS api_key_previous_hash VARCHAR(64) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS api_key_previous_valid_until TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_previous_secret_encrypted TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_previous_valid_until TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS credential_revoked_reason VARCHAR(200) NULL;

CREATE TABLE IF NOT EXISTS device_installations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Provisioned',
  effective_from TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  effective_to TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS device_connectivity_profiles (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  assignment_status VARCHAR(20) NOT NULL DEFAULT 'Assigned',
  effective_from TIMESTAMPTZ NOT NULL,
  effective_to TIMESTAMPTZ NULL,
  end_reason VARCHAR(500) NULL,
  ended_by BIGINT NULL,
  updated_at TIMESTAMPTZ NULL
);

DO $roles$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    CREATE ROLE opstrax_app NOLOGIN;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE ROLE opstrax_system NOLOGIN;
  END IF;
END
$roles$;

CREATE SCHEMA IF NOT EXISTS opstrax_security;
CREATE OR REPLACE FUNCTION opstrax_security.current_tenant_id()
RETURNS BIGINT LANGUAGE sql STABLE AS $fn$
  SELECT NULLIF(current_setting('app.current_tenant_id',true),'')::BIGINT
$fn$;
