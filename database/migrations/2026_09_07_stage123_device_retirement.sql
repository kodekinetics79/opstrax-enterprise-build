-- Stage 123 — governed software retirement with immutable lifecycle receipt.
--
-- This records credential invalidation, lifecycle closure, and the operator's
-- physical disposition plan. The plan is not evidence that any physical action
-- occurred and cannot promote a device or installation to a certified state.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage123_eld_device_owner
  ON eld_devices(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage123_user_owner
  ON users(company_id,id);

CREATE TABLE IF NOT EXISTS device_retirement_records (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  device_serial_snapshot VARCHAR(120) NOT NULL,
  retirement_reason VARCHAR(500) NOT NULL,
  disposition_plan VARCHAR(32) NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  effective_at TIMESTAMPTZ NOT NULL,
  prior_status VARCHAR(40) NOT NULL,
  prior_device_state VARCHAR(40) NOT NULL,
  row_version_before BIGINT NOT NULL,
  row_version_after BIGINT NOT NULL,
  ended_connectivity_profile_id BIGINT NULL,
  credentials_revoked BOOLEAN NOT NULL DEFAULT TRUE,
  record_status VARCHAR(32) NOT NULL DEFAULT 'OperatorRecorded',
  physical_disposition_status VARCHAR(32) NOT NULL DEFAULT 'Unverified',
  physical_disposition_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  idempotency_key UUID NOT NULL,
  retired_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage123_retirement_device FOREIGN KEY(company_id,device_id)
    REFERENCES eld_devices(company_id,id),
  CONSTRAINT fk_stage123_retirement_actor FOREIGN KEY(company_id,retired_by)
    REFERENCES users(company_id,id),
  CONSTRAINT ck_stage123_retirement_reason CHECK (BTRIM(retirement_reason)<>''),
  CONSTRAINT ck_stage123_retirement_disposition CHECK
    (disposition_plan IN ('ReturnToVendor','Recycle','SecureStorage','Other')),
  CONSTRAINT ck_stage123_retirement_source CHECK (BTRIM(source_reference)<>''),
  CONSTRAINT ck_stage123_retirement_version CHECK
    (row_version_before>=1 AND row_version_after=row_version_before+1),
  CONSTRAINT ck_stage123_credentials_revoked CHECK (credentials_revoked=TRUE),
  CONSTRAINT ck_stage123_record_status CHECK (record_status='OperatorRecorded'),
  CONSTRAINT ck_stage123_physical_status CHECK (physical_disposition_status='Unverified'),
  CONSTRAINT ck_stage123_no_physical_claim CHECK (physical_disposition_claim=FALSE),
  CONSTRAINT ck_stage123_no_certification_claim CHECK (certification_claim=FALSE),
  UNIQUE(company_id,device_id),
  UNIQUE(company_id,idempotency_key)
);

CREATE INDEX IF NOT EXISTS ix_stage123_retirement_recent
  ON device_retirement_records(company_id,effective_at DESC,id DESC);

CREATE OR REPLACE FUNCTION stage123_guard_device_retirement()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE
  device_row RECORD;
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Device retirement records are append-only'
      USING ERRCODE='23514',CONSTRAINT='ck_stage123_retirement_immutable';
  END IF;

  SELECT branch_id,device_serial,status,device_state,retired_at,revoked_at,row_version,
         api_key_hash,hmac_secret,hmac_secret_encrypted,api_key_previous_hash,
         api_key_previous_valid_until,hmac_previous_secret_encrypted,hmac_previous_valid_until
    INTO device_row
    FROM eld_devices
   WHERE company_id=NEW.company_id AND id=NEW.device_id AND deleted_at IS NULL;

  IF NOT FOUND OR device_row.branch_id IS DISTINCT FROM NEW.branch_id
     OR device_row.device_serial IS DISTINCT FROM NEW.device_serial_snapshot
     OR device_row.status<>'Retired' OR device_row.device_state<>'Retired'
     OR device_row.retired_at IS DISTINCT FROM NEW.effective_at
     OR device_row.revoked_at IS NULL OR device_row.revoked_at>NEW.effective_at
     OR device_row.row_version IS DISTINCT FROM NEW.row_version_after THEN
    RAISE EXCEPTION 'Retirement receipt does not match the exact retired device revision'
      USING ERRCODE='23514',CONSTRAINT='ck_stage123_exact_retired_device';
  END IF;

  IF device_row.api_key_hash IS NOT NULL OR device_row.hmac_secret IS NOT NULL
     OR device_row.hmac_secret_encrypted IS NOT NULL OR device_row.api_key_previous_hash IS NOT NULL
     OR device_row.api_key_previous_valid_until IS NOT NULL
     OR device_row.hmac_previous_secret_encrypted IS NOT NULL
     OR device_row.hmac_previous_valid_until IS NOT NULL THEN
    RAISE EXCEPTION 'Retired device still has usable credential material'
      USING ERRCODE='23514',CONSTRAINT='ck_stage123_retired_credentials_cleared';
  END IF;

  IF EXISTS (
    SELECT 1 FROM device_installations i
     WHERE i.company_id=NEW.company_id AND i.device_id=NEW.device_id
       AND i.effective_to IS NULL AND i.status IN ('Installed','Verified')
  ) THEN
    RAISE EXCEPTION 'Installed device must be removed before retirement'
      USING ERRCODE='23514',CONSTRAINT='ck_stage123_no_current_installation';
  END IF;

  IF EXISTS (
    SELECT 1 FROM device_connectivity_profiles p
     WHERE p.company_id=NEW.company_id AND p.device_id=NEW.device_id
       AND p.effective_to IS NULL AND p.assignment_status='Assigned'
  ) THEN
    RAISE EXCEPTION 'Current connectivity profile must end before retirement'
      USING ERRCODE='23514',CONSTRAINT='ck_stage123_no_current_connectivity';
  END IF;

  IF NEW.ended_connectivity_profile_id IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM device_connectivity_profiles p
     WHERE p.company_id=NEW.company_id AND p.device_id=NEW.device_id
       AND p.id=NEW.ended_connectivity_profile_id AND p.assignment_status='Ended'
       AND p.effective_to=NEW.effective_at
  ) THEN
    RAISE EXCEPTION 'Ended connectivity profile does not match the retirement boundary'
      USING ERRCODE='23514',CONSTRAINT='ck_stage123_ended_connectivity_match';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage123_guard_device_retirement ON device_retirement_records;
CREATE TRIGGER trg_stage123_guard_device_retirement
BEFORE INSERT OR UPDATE OR DELETE ON device_retirement_records
FOR EACH ROW EXECUTE FUNCTION stage123_guard_device_retirement();

ALTER TABLE device_retirement_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_retirement_records FORCE ROW LEVEL SECURITY;

DO $stage123_security$
BEGIN
  DROP POLICY IF EXISTS tenant_ticket_app ON device_retirement_records;
  DROP POLICY IF EXISTS system_control_plane ON device_retirement_records;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON device_retirement_records
      FOR SELECT TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE device_retirement_records FROM opstrax_app;
    GRANT SELECT ON TABLE device_retirement_records TO opstrax_app;
    REVOKE ALL ON SEQUENCE device_retirement_records_id_seq FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON device_retirement_records
      FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    REVOKE ALL ON TABLE device_retirement_records FROM opstrax_system;
    GRANT SELECT,INSERT ON TABLE device_retirement_records TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_retirement_records_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage123_guard_device_retirement() TO opstrax_system;
  END IF;
END
$stage123_security$;

REVOKE ALL ON TABLE device_retirement_records FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_retirement_records_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage123_guard_device_retirement() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage123_device_retirement',
        'Governed software retirement with immutable unverified physical disposition receipt')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
