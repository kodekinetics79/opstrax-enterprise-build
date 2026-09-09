ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS device_state TEXT NOT NULL DEFAULT 'Provisioned';
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS device_category TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;

CREATE TABLE IF NOT EXISTS device_installations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Provisioned',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
