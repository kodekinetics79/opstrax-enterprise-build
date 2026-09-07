ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS device_state TEXT NOT NULL DEFAULT 'Provisioned';
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_eld_devices_company_id_id ON eld_devices(company_id,id);

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
