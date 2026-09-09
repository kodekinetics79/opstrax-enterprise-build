-- Stage 120 — provider-authenticated connectivity observations reconciled to
-- one exact current SIM/eSIM profile. These rows preserve provider-reported
-- software facts; they never prove RF attachment, telemetry delivery or device
-- certification.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage120_connectivity_profile_owner
  ON device_connectivity_profiles(company_id,id,device_id);

CREATE TABLE IF NOT EXISTS device_connectivity_observations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  connectivity_profile_id BIGINT NOT NULL,
  profile_iccid_bidx_snapshot VARCHAR(64) NOT NULL,
  profile_iccid_last4 VARCHAR(4) NOT NULL,
  source_provider VARCHAR(80) NOT NULL,
  source_account_bidx VARCHAR(64) NOT NULL,
  source_observation_bidx VARCHAR(64) NOT NULL,
  payload_sha256 VARCHAR(64) NOT NULL,
  source_authentication_status VARCHAR(24) NOT NULL DEFAULT 'Authenticated',
  subscription_status VARCHAR(24) NOT NULL,
  network_registration_status VARCHAR(24) NOT NULL,
  data_session_status VARCHAR(24) NOT NULL,
  usage_bytes BIGINT NULL,
  roaming BOOLEAN NULL,
  observed_at TIMESTAMPTZ NOT NULL,
  received_at TIMESTAMPTZ NOT NULL,
  reconciliation_status VARCHAR(32) NOT NULL DEFAULT 'ExactCurrentProfile',
  provider_verified_claim BOOLEAN NOT NULL DEFAULT FALSE,
  physical_connectivity_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage120_observation_device
    FOREIGN KEY(company_id,device_id) REFERENCES eld_devices(company_id,id),
  CONSTRAINT fk_stage120_observation_profile
    FOREIGN KEY(company_id,connectivity_profile_id,device_id)
    REFERENCES device_connectivity_profiles(company_id,id,device_id),
  CONSTRAINT ck_stage120_iccid_bidx CHECK (profile_iccid_bidx_snapshot ~ '^[0-9a-f]{64}$'),
  CONSTRAINT ck_stage120_iccid_last4 CHECK (profile_iccid_last4 ~ '^[0-9]{4}$'),
  CONSTRAINT ck_stage120_source_provider CHECK (source_provider ~ '^[a-z0-9][a-z0-9._-]{0,79}$'),
  CONSTRAINT ck_stage120_source_hashes CHECK (
    source_account_bidx ~ '^[0-9a-f]{64}$'
    AND source_observation_bidx ~ '^[0-9a-f]{64}$'
    AND payload_sha256 ~ '^[0-9a-f]{64}$'),
  CONSTRAINT ck_stage120_source_auth CHECK (source_authentication_status='Authenticated'),
  CONSTRAINT ck_stage120_subscription CHECK (subscription_status IN ('Unknown','Active','Suspended','Deactivated')),
  CONSTRAINT ck_stage120_network CHECK (network_registration_status IN ('Unknown','Registered','Roaming','Denied','Detached')),
  CONSTRAINT ck_stage120_session CHECK (data_session_status IN ('Unknown','Attached','Detached','Blocked')),
  CONSTRAINT ck_stage120_usage CHECK (usage_bytes IS NULL OR usage_bytes>=0),
  CONSTRAINT ck_stage120_time CHECK (observed_at<=received_at+INTERVAL '5 minutes'),
  CONSTRAINT ck_stage120_reconciliation CHECK (reconciliation_status='ExactCurrentProfile'),
  CONSTRAINT ck_stage120_no_provider_claim CHECK (provider_verified_claim=FALSE),
  CONSTRAINT ck_stage120_no_physical_claim CHECK (physical_connectivity_claim=FALSE),
  CONSTRAINT ck_stage120_no_certification_claim CHECK (certification_claim=FALSE),
  UNIQUE(company_id,source_provider,source_account_bidx,source_observation_bidx)
);

CREATE INDEX IF NOT EXISTS ix_stage120_observations_device_recent
  ON device_connectivity_observations(company_id,device_id,observed_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage120_observations_profile_recent
  ON device_connectivity_observations(company_id,connectivity_profile_id,observed_at DESC,id DESC);

CREATE OR REPLACE FUNCTION stage120_guard_connectivity_observation()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Connectivity observations are append-only'
      USING ERRCODE='23514', CONSTRAINT='ck_stage120_observation_immutable';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM device_connectivity_profiles p
    JOIN eld_devices e ON e.company_id=p.company_id AND e.id=p.device_id
    WHERE p.company_id=NEW.company_id
      AND p.id=NEW.connectivity_profile_id
      AND p.device_id=NEW.device_id
      AND p.branch_id IS NOT DISTINCT FROM NEW.branch_id
      AND e.branch_id IS NOT DISTINCT FROM NEW.branch_id
      AND e.deleted_at IS NULL
      AND p.iccid_bidx=NEW.profile_iccid_bidx_snapshot
      AND p.iccid_last4=NEW.profile_iccid_last4
      AND p.assignment_status='Assigned'
      AND p.effective_to IS NULL
  ) THEN
    RAISE EXCEPTION 'Connectivity observation does not match the exact current profile'
      USING ERRCODE='23514', CONSTRAINT='ck_stage120_exact_current_profile';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage120_guard_connectivity_observation ON device_connectivity_observations;
CREATE TRIGGER trg_stage120_guard_connectivity_observation
BEFORE INSERT OR UPDATE OR DELETE ON device_connectivity_observations
FOR EACH ROW EXECUTE FUNCTION stage120_guard_connectivity_observation();

ALTER TABLE device_connectivity_observations ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_connectivity_observations FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_ticket_app ON device_connectivity_observations;
DROP POLICY IF EXISTS system_control_plane ON device_connectivity_observations;

DO $stage120_security$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON device_connectivity_observations
      FOR SELECT TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE device_connectivity_observations FROM opstrax_app;
    GRANT SELECT (
      id,company_id,branch_id,device_id,connectivity_profile_id,profile_iccid_last4,
      source_provider,source_authentication_status,subscription_status,
      network_registration_status,data_session_status,usage_bytes,roaming,
      observed_at,received_at,reconciliation_status,provider_verified_claim,
      physical_connectivity_claim,certification_claim,created_at
    ) ON device_connectivity_observations TO opstrax_app;
    REVOKE ALL ON SEQUENCE device_connectivity_observations_id_seq FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON device_connectivity_observations
      FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    REVOKE ALL ON TABLE device_connectivity_observations FROM opstrax_system;
    GRANT SELECT,INSERT ON device_connectivity_observations TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_connectivity_observations_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage120_guard_connectivity_observation() TO opstrax_system;
  END IF;
END
$stage120_security$;

REVOKE ALL ON TABLE device_connectivity_observations FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_connectivity_observations_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage120_guard_connectivity_observation() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage120_device_connectivity_observations',
        'Authenticated provider observations reconciled to exact current SIM/eSIM profiles')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
