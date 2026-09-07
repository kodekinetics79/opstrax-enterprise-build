-- Stage 116 — encrypted SIM/eSIM assignment history for DeviceOps.
--
-- A profile row is operator-recorded inventory. It never changes eld_devices.last_seen_at,
-- device_state, telemetry status, compatibility tier, or certification status. ICCID,
-- MSISDN, and APN values are encrypted before storage and are never granted to the app role.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE TABLE IF NOT EXISTS device_connectivity_profiles (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  profile_kind VARCHAR(20) NOT NULL,
  carrier_name VARCHAR(120) NOT NULL,
  iccid_encrypted TEXT NOT NULL,
  iccid_bidx VARCHAR(64) NOT NULL,
  iccid_last4 VARCHAR(4) NOT NULL,
  msisdn_encrypted TEXT NULL,
  msisdn_bidx VARCHAR(64) NULL,
  msisdn_last4 VARCHAR(4) NULL,
  apn_encrypted TEXT NULL,
  apn_bidx VARCHAR(64) NULL,
  apn_configured BOOLEAN NOT NULL DEFAULT FALSE,
  assignment_status VARCHAR(20) NOT NULL DEFAULT 'Assigned',
  effective_from TIMESTAMPTZ NOT NULL,
  effective_to TIMESTAMPTZ NULL,
  source_reference VARCHAR(240) NOT NULL,
  change_reason VARCHAR(500) NOT NULL,
  end_reason VARCHAR(500) NULL,
  idempotency_key UUID NOT NULL,
  created_by BIGINT NULL,
  ended_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  CONSTRAINT fk_stage116_connectivity_device
    FOREIGN KEY (company_id,device_id) REFERENCES eld_devices(company_id,id),
  CONSTRAINT ck_stage116_profile_kind CHECK (profile_kind IN ('PhysicalSIM','eSIM')),
  CONSTRAINT ck_stage116_carrier CHECK (BTRIM(carrier_name) <> ''),
  CONSTRAINT ck_stage116_iccid_encrypted CHECK (iccid_encrypted LIKE 'enc:%'),
  CONSTRAINT ck_stage116_iccid_bidx CHECK (iccid_bidx ~ '^[0-9a-f]{64}$'),
  CONSTRAINT ck_stage116_iccid_last4 CHECK (iccid_last4 ~ '^[0-9]{4}$'),
  CONSTRAINT ck_stage116_msisdn_encrypted CHECK (msisdn_encrypted IS NULL OR msisdn_encrypted LIKE 'enc:%'),
  CONSTRAINT ck_stage116_msisdn_bidx CHECK ((msisdn_encrypted IS NULL)=(msisdn_bidx IS NULL) AND (msisdn_bidx IS NULL OR msisdn_bidx ~ '^[0-9a-f]{64}$')),
  CONSTRAINT ck_stage116_msisdn_last4 CHECK ((msisdn_encrypted IS NULL)=(msisdn_last4 IS NULL) AND (msisdn_last4 IS NULL OR msisdn_last4 ~ '^[0-9]{4}$')),
  CONSTRAINT ck_stage116_apn_encrypted CHECK (apn_encrypted IS NULL OR apn_encrypted LIKE 'enc:%'),
  CONSTRAINT ck_stage116_apn_bidx CHECK (
    (apn_encrypted IS NULL)=(apn_bidx IS NULL)
    AND apn_configured=(apn_encrypted IS NOT NULL)
    AND (apn_bidx IS NULL OR apn_bidx ~ '^[0-9a-f]{64}$')),
  CONSTRAINT ck_stage116_assignment_lifecycle CHECK (
    (assignment_status='Assigned' AND effective_to IS NULL AND end_reason IS NULL AND ended_by IS NULL)
    OR
    (assignment_status='Ended' AND effective_to IS NOT NULL AND effective_to>effective_from AND end_reason IS NOT NULL AND BTRIM(end_reason)<>'' )
  ),
  CONSTRAINT ck_stage116_source CHECK (BTRIM(source_reference) <> ''),
  CONSTRAINT ck_stage116_change_reason CHECK (BTRIM(change_reason) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage116_connectivity_current_device
  ON device_connectivity_profiles(company_id,device_id)
  WHERE effective_to IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage116_connectivity_current_iccid
  ON device_connectivity_profiles(iccid_bidx)
  WHERE effective_to IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage116_connectivity_idempotency
  ON device_connectivity_profiles(company_id,device_id,idempotency_key);
CREATE INDEX IF NOT EXISTS ix_stage116_connectivity_history
  ON device_connectivity_profiles(company_id,device_id,effective_from DESC,id DESC);

CREATE OR REPLACE FUNCTION stage116_protect_device_connectivity_profile()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF OLD.effective_to IS NOT NULL THEN
    RAISE EXCEPTION 'Ended connectivity profiles are immutable'
      USING ERRCODE='23514', CONSTRAINT='ck_device_connectivity_profile_immutable';
  END IF;
  IF NEW.company_id IS DISTINCT FROM OLD.company_id
     OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
     OR NEW.device_id IS DISTINCT FROM OLD.device_id
     OR NEW.profile_kind IS DISTINCT FROM OLD.profile_kind
     OR NEW.carrier_name IS DISTINCT FROM OLD.carrier_name
     OR NEW.iccid_encrypted IS DISTINCT FROM OLD.iccid_encrypted
     OR NEW.iccid_bidx IS DISTINCT FROM OLD.iccid_bidx
     OR NEW.iccid_last4 IS DISTINCT FROM OLD.iccid_last4
     OR NEW.msisdn_encrypted IS DISTINCT FROM OLD.msisdn_encrypted
     OR NEW.msisdn_bidx IS DISTINCT FROM OLD.msisdn_bidx
     OR NEW.msisdn_last4 IS DISTINCT FROM OLD.msisdn_last4
     OR NEW.apn_encrypted IS DISTINCT FROM OLD.apn_encrypted
     OR NEW.apn_bidx IS DISTINCT FROM OLD.apn_bidx
     OR NEW.apn_configured IS DISTINCT FROM OLD.apn_configured
     OR NEW.effective_from IS DISTINCT FROM OLD.effective_from
     OR NEW.source_reference IS DISTINCT FROM OLD.source_reference
     OR NEW.change_reason IS DISTINCT FROM OLD.change_reason
     OR NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key
     OR NEW.created_by IS DISTINCT FROM OLD.created_by
     OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
    RAISE EXCEPTION 'Connectivity profile identity and protected inventory are immutable'
      USING ERRCODE='23514', CONSTRAINT='ck_device_connectivity_profile_immutable';
  END IF;
  IF NEW.assignment_status<>'Ended' OR NEW.effective_to IS NULL
     OR NEW.effective_to<=OLD.effective_from OR BTRIM(COALESCE(NEW.end_reason,''))='' THEN
    RAISE EXCEPTION 'A current connectivity profile can only transition once to Ended'
      USING ERRCODE='23514', CONSTRAINT='ck_device_connectivity_profile_end_transition';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage116_protect_device_connectivity_profile ON device_connectivity_profiles;
CREATE TRIGGER trg_stage116_protect_device_connectivity_profile
BEFORE UPDATE ON device_connectivity_profiles
FOR EACH ROW EXECUTE FUNCTION stage116_protect_device_connectivity_profile();

ALTER TABLE device_connectivity_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_connectivity_profiles FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_ticket_app ON device_connectivity_profiles;
DROP POLICY IF EXISTS system_control_plane ON device_connectivity_profiles;

DO $stage116_security$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON device_connectivity_profiles
      FOR SELECT TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE device_connectivity_profiles FROM opstrax_app;
    GRANT SELECT (
      id,company_id,branch_id,device_id,profile_kind,carrier_name,iccid_last4,
      msisdn_last4,apn_configured,assignment_status,effective_from,effective_to,source_reference,
      change_reason,end_reason,created_at,updated_at
    ) ON device_connectivity_profiles TO opstrax_app;
    REVOKE ALL ON SEQUENCE device_connectivity_profiles_id_seq FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON device_connectivity_profiles
      FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    GRANT SELECT,INSERT,UPDATE ON device_connectivity_profiles TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_connectivity_profiles_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage116_protect_device_connectivity_profile() TO opstrax_system;
  END IF;
END
$stage116_security$;

REVOKE ALL ON TABLE device_connectivity_profiles FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_connectivity_profiles_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage116_protect_device_connectivity_profile() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage116_device_connectivity_profiles',
        'Encrypted SIM/eSIM assignment history with masked tenant reads')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
