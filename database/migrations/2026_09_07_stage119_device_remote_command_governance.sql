-- Stage 119 — capability-negotiated remote-command governance.
--
-- A request may enter the legacy command ledger only when a current, exact-device
-- Verified capability record exists. That capability is operational evidence, not
-- compatibility certification. No provider adapter or physical outcome is inferred.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage119_eld_devices_company_id_id
  ON eld_devices(company_id,id);

CREATE TABLE IF NOT EXISTS device_command_capabilities (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  device_serial VARCHAR(120) NOT NULL,
  manufacturer VARCHAR(120) NOT NULL,
  device_model VARCHAR(160) NOT NULL,
  hardware_revision VARCHAR(120) NOT NULL,
  firmware_version VARCHAR(120) NOT NULL,
  provider VARCHAR(120) NOT NULL,
  command_type VARCHAR(60) NOT NULL,
  command_class VARCHAR(24) NOT NULL,
  capability_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
  evidence_source VARCHAR(40) NOT NULL,
  evidence_reference VARCHAR(240) NOT NULL,
  observed_at TIMESTAMPTZ NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  physical_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  recorded_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  CONSTRAINT uq_stage119_capability_company_id UNIQUE(company_id,id),
  CONSTRAINT fk_stage119_capability_device FOREIGN KEY(company_id,device_id) REFERENCES eld_devices(company_id,id),
  CONSTRAINT ck_stage119_capability_serial CHECK (BTRIM(device_serial)<>''),
  CONSTRAINT ck_stage119_capability_tuple CHECK (
    BTRIM(manufacturer)<>'' AND BTRIM(device_model)<>'' AND BTRIM(hardware_revision)<>'' AND BTRIM(firmware_version)<>'' AND BTRIM(provider)<>''),
  CONSTRAINT ck_stage119_capability_type CHECK (command_type IN ('RequestPosition','RequestDiagnostics','RestartDevice')),
  CONSTRAINT ck_stage119_capability_class CHECK (
    (command_type IN ('RequestPosition','RequestDiagnostics') AND command_class='Observation') OR
    (command_type='RestartDevice' AND command_class='Controlled')),
  CONSTRAINT ck_stage119_capability_status CHECK (capability_status IN ('Unverified','Verified','Rejected','Revoked')),
  CONSTRAINT ck_stage119_capability_source CHECK (evidence_source IN ('ProviderCapabilityResponse','DeviceProtocolHandshake','PhysicalBench')),
  CONSTRAINT ck_stage119_capability_reference CHECK (BTRIM(evidence_reference)<>''),
  CONSTRAINT ck_stage119_capability_window CHECK (expires_at>observed_at AND observed_at<=created_at+INTERVAL '5 minutes'),
  CONSTRAINT ck_stage119_capability_no_physical_claim CHECK (physical_evidence_claim=FALSE),
  CONSTRAINT ck_stage119_capability_no_certification_claim CHECK (certification_claim=FALSE)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage119_capability_evidence
  ON device_command_capabilities(company_id,device_id,command_type,evidence_reference);
CREATE INDEX IF NOT EXISTS ix_stage119_capability_lookup
  ON device_command_capabilities(company_id,device_id,command_type,capability_status,expires_at DESC,id DESC);

ALTER TABLE telematics_device_commands
  ADD COLUMN IF NOT EXISTS capability_id BIGINT NULL,
  ADD COLUMN IF NOT EXISTS device_serial_snapshot VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS manufacturer_snapshot VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS device_model_snapshot VARCHAR(160) NULL,
  ADD COLUMN IF NOT EXISTS hardware_revision_snapshot VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS firmware_version_snapshot VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS provider_snapshot VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS command_class VARCHAR(24) NULL,
  ADD COLUMN IF NOT EXISTS purpose VARCHAR(500) NULL,
  ADD COLUMN IF NOT EXISTS source_reference VARCHAR(240) NULL,
  ADD COLUMN IF NOT EXISTS safety_confirmation_hash VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS governance_status VARCHAR(24) NOT NULL DEFAULT 'LegacyUnverified',
  ADD COLUMN IF NOT EXISTS provider_delivery_claim BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS physical_outcome_claim BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE telematics_device_commands DROP CONSTRAINT IF EXISTS fk_stage119_command_capability;
ALTER TABLE telematics_device_commands ADD CONSTRAINT fk_stage119_command_capability
  FOREIGN KEY(company_id,capability_id) REFERENCES device_command_capabilities(company_id,id) NOT VALID;
ALTER TABLE telematics_device_commands DROP CONSTRAINT IF EXISTS ck_stage119_command_type;
ALTER TABLE telematics_device_commands ADD CONSTRAINT ck_stage119_command_type
  CHECK (command_type IN ('RequestPosition','RequestDiagnostics','RestartDevice')) NOT VALID;
ALTER TABLE telematics_device_commands DROP CONSTRAINT IF EXISTS ck_stage119_command_governance;
ALTER TABLE telematics_device_commands ADD CONSTRAINT ck_stage119_command_governance
  CHECK (governance_status IN ('LegacyUnverified','EvidenceVerified')) NOT VALID;
ALTER TABLE telematics_device_commands DROP CONSTRAINT IF EXISTS ck_stage119_command_claims;
ALTER TABLE telematics_device_commands ADD CONSTRAINT ck_stage119_command_claims
  CHECK (provider_delivery_claim=FALSE AND physical_outcome_claim=FALSE) NOT VALID;

CREATE INDEX IF NOT EXISTS ix_stage119_commands_device_recent
  ON telematics_device_commands(company_id,device_id,created_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage119_commands_capability
  ON telematics_device_commands(company_id,capability_id,created_at DESC,id DESC)
  WHERE capability_id IS NOT NULL;

CREATE OR REPLACE FUNCTION stage119_protect_device_command_capability()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF TG_OP='DELETE' THEN
    RAISE EXCEPTION 'Device command capability evidence is immutable'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_capability_immutable';
  END IF;
  IF NEW.company_id IS DISTINCT FROM OLD.company_id
     OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
     OR NEW.device_id IS DISTINCT FROM OLD.device_id
     OR NEW.device_serial IS DISTINCT FROM OLD.device_serial
     OR NEW.manufacturer IS DISTINCT FROM OLD.manufacturer
     OR NEW.device_model IS DISTINCT FROM OLD.device_model
     OR NEW.hardware_revision IS DISTINCT FROM OLD.hardware_revision
     OR NEW.firmware_version IS DISTINCT FROM OLD.firmware_version
     OR NEW.provider IS DISTINCT FROM OLD.provider
     OR NEW.command_type IS DISTINCT FROM OLD.command_type
     OR NEW.command_class IS DISTINCT FROM OLD.command_class
     OR NEW.evidence_source IS DISTINCT FROM OLD.evidence_source
     OR NEW.evidence_reference IS DISTINCT FROM OLD.evidence_reference
     OR NEW.observed_at IS DISTINCT FROM OLD.observed_at
     OR NEW.expires_at IS DISTINCT FROM OLD.expires_at
     OR NEW.physical_evidence_claim IS DISTINCT FROM OLD.physical_evidence_claim
     OR NEW.certification_claim IS DISTINCT FROM OLD.certification_claim
     OR NEW.recorded_by IS DISTINCT FROM OLD.recorded_by
     OR NEW.created_at IS DISTINCT FROM OLD.created_at
     OR NOT ((OLD.capability_status='Verified' AND NEW.capability_status='Revoked') OR
             (OLD.capability_status='Unverified' AND NEW.capability_status='Rejected')) THEN
    RAISE EXCEPTION 'Device command capability evidence is immutable; only rejection or revocation is allowed'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_capability_immutable';
  END IF;
  NEW.updated_at=NOW();
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage119_protect_capability ON device_command_capabilities;
CREATE TRIGGER trg_stage119_protect_capability
BEFORE UPDATE OR DELETE ON device_command_capabilities
FOR EACH ROW EXECUTE FUNCTION stage119_protect_device_command_capability();

CREATE OR REPLACE FUNCTION stage119_guard_device_command()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE capability RECORD; device RECORD; expected_attempts INT;
BEGIN
  IF TG_OP='INSERT' THEN
    IF NEW.capability_id IS NULL OR NEW.governance_status<>'EvidenceVerified' OR NEW.status<>'approved' THEN
      RAISE EXCEPTION 'Remote command requires a verified capability and approved admission state'
        USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_capability_required';
    END IF;
    SELECT * INTO capability FROM device_command_capabilities
     WHERE company_id=NEW.company_id AND id=NEW.capability_id FOR KEY SHARE;
    IF capability.id IS NULL OR capability.device_id<>NEW.device_id
       OR capability.branch_id IS DISTINCT FROM NEW.branch_id
       OR capability.command_type<>NEW.command_type OR capability.capability_status<>'Verified'
       OR capability.observed_at>NOW() OR capability.expires_at<=NOW() THEN
      RAISE EXCEPTION 'Remote command capability is absent, mismatched, unverified, or expired'
        USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_capability_invalid';
    END IF;
    SELECT * INTO device FROM eld_devices WHERE company_id=NEW.company_id AND id=NEW.device_id;
    IF device.id IS NULL OR device.branch_id IS DISTINCT FROM NEW.branch_id
       OR device.deleted_at IS NOT NULL OR device.revoked_at IS NOT NULL
       OR LOWER(COALESCE(device.status,'')) IN ('revoked','retired','decommissioned')
       OR LOWER(COALESCE(device.device_state,'')) IN ('revoked','retired','decommissioned')
       OR capability.device_serial IS DISTINCT FROM device.device_serial
       OR capability.manufacturer IS DISTINCT FROM device.manufacturer
       OR capability.device_model IS DISTINCT FROM device.device_model
       OR capability.hardware_revision IS DISTINCT FROM device.hardware_revision
       OR capability.firmware_version IS DISTINCT FROM device.firmware_version
       OR capability.provider IS DISTINCT FROM device.provider THEN
      RAISE EXCEPTION 'Remote command capability does not match the current exact device tuple'
        USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_tuple_mismatch';
    END IF;
    expected_attempts := 1;
    IF NEW.command_class<>capability.command_class OR NEW.max_attempts<>expected_attempts
       OR NEW.attempt_count<>0 OR NEW.dispatched_at IS NOT NULL OR NEW.acknowledged_at IS NOT NULL
       OR NEW.applied_at IS NOT NULL OR NEW.scheduled_for<NOW()-INTERVAL '1 minute'
       OR NEW.scheduled_for>NOW()+INTERVAL '1 minute'
       OR NEW.expires_at IS NULL OR NEW.expires_at<=NEW.scheduled_for
       OR NEW.expires_at>LEAST(capability.expires_at,NEW.scheduled_for+INTERVAL '10 minutes')
       OR NEW.safety_confirmation_hash !~ '^[0-9a-f]{64}$'
       OR BTRIM(COALESCE(NEW.purpose,''))='' OR BTRIM(COALESCE(NEW.source_reference,''))=''
       OR jsonb_typeof(NEW.desired_payload)<>'object' OR pg_column_size(NEW.desired_payload)>8192
       OR NEW.reported_payload IS NOT NULL OR NEW.last_error IS NOT NULL OR NEW.updated_at IS NOT NULL
       OR NEW.provider_delivery_claim OR NEW.physical_outcome_claim THEN
      RAISE EXCEPTION 'Remote command request violates the governed admission envelope'
        USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_envelope';
    END IF;
    IF NEW.device_serial_snapshot IS DISTINCT FROM capability.device_serial
       OR NEW.manufacturer_snapshot IS DISTINCT FROM capability.manufacturer
       OR NEW.device_model_snapshot IS DISTINCT FROM capability.device_model
       OR NEW.hardware_revision_snapshot IS DISTINCT FROM capability.hardware_revision
       OR NEW.firmware_version_snapshot IS DISTINCT FROM capability.firmware_version
       OR NEW.provider_snapshot IS DISTINCT FROM capability.provider THEN
      RAISE EXCEPTION 'Remote command snapshots do not match admitted capability evidence'
        USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_snapshot_mismatch';
    END IF;
    RETURN NEW;
  END IF;

  IF NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
     OR NEW.device_id IS DISTINCT FROM OLD.device_id OR NEW.command_type IS DISTINCT FROM OLD.command_type
     OR NEW.desired_payload IS DISTINCT FROM OLD.desired_payload OR NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key
     OR NEW.capability_id IS DISTINCT FROM OLD.capability_id OR NEW.device_serial_snapshot IS DISTINCT FROM OLD.device_serial_snapshot
     OR NEW.manufacturer_snapshot IS DISTINCT FROM OLD.manufacturer_snapshot OR NEW.device_model_snapshot IS DISTINCT FROM OLD.device_model_snapshot
     OR NEW.hardware_revision_snapshot IS DISTINCT FROM OLD.hardware_revision_snapshot OR NEW.firmware_version_snapshot IS DISTINCT FROM OLD.firmware_version_snapshot
     OR NEW.provider_snapshot IS DISTINCT FROM OLD.provider_snapshot OR NEW.command_class IS DISTINCT FROM OLD.command_class
     OR NEW.purpose IS DISTINCT FROM OLD.purpose OR NEW.source_reference IS DISTINCT FROM OLD.source_reference
     OR NEW.safety_confirmation_hash IS DISTINCT FROM OLD.safety_confirmation_hash OR NEW.governance_status IS DISTINCT FROM OLD.governance_status
     OR NEW.max_attempts IS DISTINCT FROM OLD.max_attempts OR NEW.scheduled_for IS DISTINCT FROM OLD.scheduled_for
     OR NEW.expires_at IS DISTINCT FROM OLD.expires_at OR NEW.requested_by IS DISTINCT FROM OLD.requested_by
     OR NEW.approved_by IS DISTINCT FROM OLD.approved_by OR NEW.created_at IS DISTINCT FROM OLD.created_at
     OR NEW.provider_delivery_claim OR NEW.physical_outcome_claim THEN
    RAISE EXCEPTION 'Remote command request identity and claims are immutable'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_immutable';
  END IF;
  IF NOT ((OLD.status='approved' AND NEW.status IN ('dispatched','cancelled','expired')) OR
          (OLD.status='dispatched' AND NEW.status IN ('acknowledged','failed','expired','dead_letter')) OR
          (OLD.status='acknowledged' AND NEW.status IN ('applied','failed'))) THEN
    RAISE EXCEPTION 'Remote command status transition is invalid'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_transition';
  END IF;
  IF NEW.attempt_count <> OLD.attempt_count + (CASE
       WHEN OLD.status='approved' AND NEW.status='dispatched' THEN 1 ELSE 0 END) THEN
    RAISE EXCEPTION 'Remote command attempt count does not match its status transition'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_attempt_transition';
  END IF;
  IF (OLD.dispatched_at IS NOT NULL AND NEW.dispatched_at IS DISTINCT FROM OLD.dispatched_at)
     OR (OLD.acknowledged_at IS NOT NULL AND NEW.acknowledged_at IS DISTINCT FROM OLD.acknowledged_at)
     OR (OLD.applied_at IS NOT NULL AND NEW.applied_at IS DISTINCT FROM OLD.applied_at) THEN
    RAISE EXCEPTION 'Remote command evidence timestamps cannot be rewritten'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_evidence_immutable';
  END IF;
  IF NEW.reported_payload IS DISTINCT FROM OLD.reported_payload AND
     (OLD.reported_payload IS NOT NULL OR NEW.status NOT IN ('acknowledged','failed')
      OR NEW.reported_payload IS NULL OR jsonb_typeof(NEW.reported_payload)<>'object'
      OR pg_column_size(NEW.reported_payload)>8192) THEN
    RAISE EXCEPTION 'Remote command reported payload is not bounded acknowledgement evidence'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_reported_payload';
  END IF;
  IF (NEW.last_error IS DISTINCT FROM OLD.last_error AND NEW.status NOT IN ('failed','dead_letter'))
     OR (NEW.status IN ('failed','dead_letter') AND BTRIM(COALESCE(NEW.last_error,''))='') THEN
    RAISE EXCEPTION 'Remote command errors may be recorded only with a failure state'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_error_evidence';
  END IF;
  IF NEW.status='dispatched' THEN
    SELECT * INTO capability FROM device_command_capabilities
     WHERE company_id=NEW.company_id AND id=NEW.capability_id FOR KEY SHARE;
    SELECT * INTO device FROM eld_devices
     WHERE company_id=NEW.company_id AND id=NEW.device_id;
    IF NEW.expires_at<=NOW() OR capability.id IS NULL
       OR capability.device_id<>NEW.device_id OR capability.branch_id IS DISTINCT FROM NEW.branch_id
       OR capability.command_type<>NEW.command_type OR capability.capability_status<>'Verified'
       OR capability.observed_at>NOW() OR capability.expires_at<=NOW()
       OR device.id IS NULL OR device.branch_id IS DISTINCT FROM NEW.branch_id
       OR device.deleted_at IS NOT NULL OR device.revoked_at IS NOT NULL
       OR LOWER(COALESCE(device.status,'')) IN ('revoked','retired','decommissioned')
       OR LOWER(COALESCE(device.device_state,'')) IN ('revoked','retired','decommissioned')
       OR capability.device_serial IS DISTINCT FROM device.device_serial
       OR capability.manufacturer IS DISTINCT FROM device.manufacturer
       OR capability.device_model IS DISTINCT FROM device.device_model
       OR capability.hardware_revision IS DISTINCT FROM device.hardware_revision
       OR capability.firmware_version IS DISTINCT FROM device.firmware_version
       OR capability.provider IS DISTINCT FROM device.provider THEN
      RAISE EXCEPTION 'Remote command capability is no longer valid for dispatch'
        USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_dispatch_capability';
    END IF;
  END IF;
  IF (NEW.status='dispatched' AND (NEW.dispatched_at IS NULL OR NEW.acknowledged_at IS NOT NULL OR NEW.applied_at IS NOT NULL))
     OR (NEW.status='acknowledged' AND (NEW.dispatched_at IS NULL OR NEW.acknowledged_at IS NULL OR NEW.applied_at IS NOT NULL))
     OR (NEW.status='applied' AND (NEW.dispatched_at IS NULL OR NEW.acknowledged_at IS NULL OR NEW.applied_at IS NULL))
     OR (NEW.status='approved' AND (NEW.dispatched_at IS NOT NULL OR NEW.acknowledged_at IS NOT NULL OR NEW.applied_at IS NOT NULL)) THEN
    RAISE EXCEPTION 'Remote command timestamps do not support the requested status'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_status_evidence';
  END IF;
  IF (NEW.acknowledged_at IS NOT NULL AND NEW.acknowledged_at<NEW.dispatched_at)
     OR (NEW.applied_at IS NOT NULL AND NEW.applied_at<NEW.acknowledged_at) THEN
    RAISE EXCEPTION 'Remote command evidence timestamps are out of order'
      USING ERRCODE='23514', CONSTRAINT='ck_stage119_command_timestamp_order';
  END IF;
  NEW.updated_at=NOW();
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage119_guard_device_command ON telematics_device_commands;
CREATE TRIGGER trg_stage119_guard_device_command
BEFORE INSERT OR UPDATE ON telematics_device_commands
FOR EACH ROW EXECUTE FUNCTION stage119_guard_device_command();

ALTER TABLE device_command_capabilities ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_command_capabilities FORCE ROW LEVEL SECURITY;
ALTER TABLE telematics_device_commands ENABLE ROW LEVEL SECURITY;
ALTER TABLE telematics_device_commands FORCE ROW LEVEL SECURITY;

DO $stage119_security$
BEGIN
  DROP POLICY IF EXISTS tenant_ticket_app ON device_command_capabilities;
  DROP POLICY IF EXISTS system_control_plane ON device_command_capabilities;
  DROP POLICY IF EXISTS tenant_ticket_app ON telematics_device_commands;
  DROP POLICY IF EXISTS system_control_plane ON telematics_device_commands;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON device_command_capabilities FOR SELECT TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()));
    CREATE POLICY tenant_ticket_app ON telematics_device_commands FOR SELECT TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON device_command_capabilities FROM opstrax_app;
    GRANT SELECT ON device_command_capabilities TO opstrax_app;
    REVOKE ALL ON SEQUENCE device_command_capabilities_id_seq FROM opstrax_app;
    REVOKE INSERT,UPDATE,DELETE ON telematics_device_commands FROM opstrax_app;
    GRANT SELECT ON telematics_device_commands TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON device_command_capabilities FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    CREATE POLICY system_control_plane ON telematics_device_commands FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    REVOKE ALL ON device_command_capabilities FROM opstrax_system;
    REVOKE ALL ON telematics_device_commands FROM opstrax_system;
    GRANT SELECT,INSERT,UPDATE ON device_command_capabilities TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_command_capabilities_id_seq TO opstrax_system;
    GRANT SELECT,INSERT,UPDATE ON telematics_device_commands TO opstrax_system;
  END IF;
END
$stage119_security$;

REVOKE ALL ON device_command_capabilities FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_command_capabilities_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage119_protect_device_command_capability() FROM PUBLIC;
REVOKE ALL ON FUNCTION stage119_guard_device_command() FROM PUBLIC;

COMMENT ON TABLE device_command_capabilities IS 'Exact-device operational command capability evidence; it is not compatibility certification.';
COMMENT ON COLUMN telematics_device_commands.provider_delivery_claim IS 'Fixed false until independently reconciled provider delivery evidence exists.';
COMMENT ON COLUMN telematics_device_commands.physical_outcome_claim IS 'Fixed false; command ledger state never proves a physical vehicle or device outcome.';

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage119_device_remote_command_governance',
        'Exact capability, payload, confirmation and status-transition governance for remote command requests')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
