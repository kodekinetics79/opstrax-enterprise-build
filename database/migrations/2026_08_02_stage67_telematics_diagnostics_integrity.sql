-- Stage 67 — Canonical, order-safe telematics diagnostics and non-regulatory safety holds
-- Additive/idempotent. Apply as database owner after Stage66.
BEGIN;

ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS api_key_previous_hash VARCHAR(64) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS api_key_previous_valid_until TIMESTAMPTZ NULL;

DO $stage67_api_key_uniqueness$
BEGIN
  IF EXISTS (
    SELECT key_hash FROM (
      SELECT api_key_hash key_hash FROM eld_devices WHERE api_key_hash IS NOT NULL AND deleted_at IS NULL
      UNION ALL
      SELECT api_key_previous_hash FROM eld_devices WHERE api_key_previous_hash IS NOT NULL AND deleted_at IS NULL
    ) keys GROUP BY key_hash HAVING COUNT(*)>1
  ) THEN
    RAISE EXCEPTION 'Stage67 blocked: current/previous device API key hashes are ambiguous';
  END IF;
END
$stage67_api_key_uniqueness$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_eld_devices_api_key_hash
  ON eld_devices(api_key_hash) WHERE api_key_hash IS NOT NULL AND deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_eld_devices_api_key_previous_hash
  ON eld_devices(api_key_previous_hash) WHERE api_key_previous_hash IS NOT NULL AND deleted_at IS NULL;

ALTER TABLE fault_occurrences ADD COLUMN IF NOT EXISTS dtc_ordinal INT NOT NULL DEFAULT 0;
ALTER TABLE fault_occurrences ADD COLUMN IF NOT EXISTS canonical_dtc VARCHAR(240) NULL;
UPDATE fault_occurrences
SET canonical_dtc = CASE
  WHEN UPPER(protocol)='J1939' AND spn IS NOT NULL AND fmi IS NOT NULL THEN
    'J1939:' || COALESCE(NULLIF(UPPER(BTRIM(controller)),''),
      CASE WHEN source_address IS NOT NULL THEN 'SA:' || LPAD(source_address::TEXT,2,'0') ELSE 'UNKNOWN' END)
      || ':SPN:' || spn::TEXT || ':FMI:' || fmi::TEXT
  ELSE UPPER(protocol) || ':' || COALESCE(NULLIF(UPPER(BTRIM(controller)),''),'UNKNOWN') || ':' || UPPER(BTRIM(code))
END
WHERE canonical_dtc IS NULL OR BTRIM(canonical_dtc)='';
ALTER TABLE fault_occurrences ALTER COLUMN canonical_dtc SET NOT NULL;
ALTER TABLE fault_occurrences DROP CONSTRAINT IF EXISTS fault_occurrences_company_id_device_id_source_event_id_key;
DROP INDEX IF EXISTS uq_fault_occurrences_source_event;
CREATE UNIQUE INDEX IF NOT EXISTS uq_fault_occurrences_source_dtc
  ON fault_occurrences(company_id,device_id,source_event_id,dtc_ordinal,canonical_dtc);

ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS canonical_identity VARCHAR(240) NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS last_observed_at TIMESTAMPTZ NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS last_source_event_id VARCHAR(160) NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS diagnostic_hold_id BIGINT NULL;
UPDATE fault_codes
SET canonical_identity = CASE
  WHEN UPPER(protocol)='J1939' AND spn IS NOT NULL AND fmi IS NOT NULL THEN
    'J1939:' || COALESCE(NULLIF(UPPER(BTRIM(controller)),''),
      CASE WHEN source_address IS NOT NULL THEN 'SA:' || LPAD(source_address::TEXT,2,'0') ELSE 'UNKNOWN' END)
      || ':SPN:' || spn::TEXT || ':FMI:' || fmi::TEXT
  ELSE UPPER(protocol) || ':' || COALESCE(NULLIF(UPPER(BTRIM(controller)),''),'UNKNOWN') || ':' || UPPER(BTRIM(code))
END,
last_observed_at=COALESCE(last_observed_at,observed_at,last_seen_at,received_at,created_at),
last_source_event_id=COALESCE(last_source_event_id,source_event_id,'legacy:' || id::TEXT)
WHERE canonical_identity IS NULL OR BTRIM(canonical_identity)='' OR last_observed_at IS NULL OR last_source_event_id IS NULL;
ALTER TABLE fault_codes ALTER COLUMN canonical_identity SET NOT NULL;
ALTER TABLE fault_codes ALTER COLUMN last_observed_at SET NOT NULL;
ALTER TABLE fault_codes ALTER COLUMN last_source_event_id SET NOT NULL;
DROP INDEX IF EXISTS uq_fault_codes_projection;
DROP INDEX IF EXISTS uq_fault_codes_derived_state;
DROP INDEX IF EXISTS uq_fault_codes_company_device_source_event;
CREATE UNIQUE INDEX IF NOT EXISTS uq_fault_codes_canonical_projection
  ON fault_codes(company_id,device_id,protocol,canonical_identity);

CREATE TABLE IF NOT EXISTS diagnostic_holds (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  vehicle_id BIGINT NOT NULL,
  device_id VARCHAR(120) NOT NULL,
  fault_code_id BIGINT NULL,
  canonical_dtc VARCHAR(240) NOT NULL,
  severity VARCHAR(40) NOT NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'active',
  out_of_service BOOLEAN NOT NULL DEFAULT TRUE,
  source VARCHAR(40) NOT NULL DEFAULT 'machine_diagnostic',
  source_event_id VARCHAR(160) NOT NULL,
  reason TEXT NOT NULL,
  first_observed_at TIMESTAMPTZ NOT NULL,
  last_observed_at TIMESTAMPTZ NOT NULL,
  acknowledged_at TIMESTAMPTZ NULL,
  acknowledged_by BIGINT NULL,
  resolved_at TIMESTAMPTZ NULL,
  resolved_by BIGINT NULL,
  resolution_note TEXT NULL,
  resolution_evidence_type VARCHAR(40) NULL,
  resolution_evidence_reference VARCHAR(500) NULL,
  verified_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
ALTER TABLE diagnostic_holds ADD COLUMN IF NOT EXISTS resolution_evidence_type VARCHAR(40) NULL;
ALTER TABLE diagnostic_holds ADD COLUMN IF NOT EXISTS resolution_evidence_reference VARCHAR(500) NULL;
ALTER TABLE diagnostic_holds ADD COLUMN IF NOT EXISTS verified_at TIMESTAMPTZ NULL;
ALTER TABLE diagnostic_holds DROP CONSTRAINT IF EXISTS ck_stage67_diagnostic_hold_status;
ALTER TABLE diagnostic_holds ADD CONSTRAINT ck_stage67_diagnostic_hold_status
  CHECK (status IN ('active','acknowledged','resolved','superseded')) NOT VALID;
CREATE UNIQUE INDEX IF NOT EXISTS uq_diagnostic_holds_active_fault
  ON diagnostic_holds(company_id,vehicle_id,canonical_dtc)
  WHERE status IN ('active','acknowledged');
CREATE INDEX IF NOT EXISTS idx_diagnostic_holds_scope_status
  ON diagnostic_holds(company_id,branch_id,status,last_observed_at DESC);

ALTER TABLE diagnostic_holds ENABLE ROW LEVEL SECURITY;
ALTER TABLE diagnostic_holds FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON diagnostic_holds;
DROP POLICY IF EXISTS platform_admin_bypass ON diagnostic_holds;
DROP POLICY IF EXISTS tenant_ticket_app ON diagnostic_holds;
DROP POLICY IF EXISTS system_control_plane ON diagnostic_holds;
DO $stage67_rls$
DECLARE safe_device_columns TEXT;
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON diagnostic_holds FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    GRANT SELECT,INSERT,UPDATE,DELETE ON diagnostic_holds TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE diagnostic_holds_id_seq TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    -- Tenant-facing SQL must never enumerate credential material. Pre-authentication
    -- uses the system identity; normal application reads receive an explicit safe list.
    REVOKE SELECT ON eld_devices FROM opstrax_app;
    SELECT string_agg(quote_ident(column_name),',' ORDER BY ordinal_position)
      INTO safe_device_columns
      FROM information_schema.columns
      WHERE table_schema='public' AND table_name='eld_devices'
        AND column_name NOT IN (
          'api_key_hash','api_key_previous_hash','hmac_secret',
          'hmac_secret_encrypted','hmac_previous_secret_encrypted'
        );
    IF safe_device_columns IS NOT NULL THEN
      EXECUTE format('GRANT SELECT (%s) ON eld_devices TO opstrax_app',safe_device_columns);
    END IF;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON diagnostic_holds FOR ALL TO opstrax_system
      USING (TRUE) WITH CHECK (TRUE);
    GRANT SELECT,INSERT,UPDATE,DELETE ON diagnostic_holds TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE diagnostic_holds_id_seq TO opstrax_system;
  END IF;
END
$stage67_rls$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_02_stage67_telematics_diagnostics_integrity','Canonical order-safe diagnostics, credential rotation grace, and machine safety holds')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
