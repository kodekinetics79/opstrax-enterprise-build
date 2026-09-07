-- Stage 115 — exact-tuple DeviceOps compatibility candidate registry.
--
-- This is a pre-certification engineering registry. It lets the customer-facing
-- product show whether a registered device matches a frozen software/hardware
-- candidate, but it deliberately cannot record Pilot, Certified Compatible, or
-- Production Supported. Those tiers require physical evidence and independent
-- acceptance in a later certification-candidate gate.

BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE eld_devices
  ADD COLUMN IF NOT EXISTS manufacturer VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS hardware_revision VARCHAR(120) NULL;

ALTER TABLE eld_devices
  DROP CONSTRAINT IF EXISTS ck_stage115_device_manufacturer,
  ADD CONSTRAINT ck_stage115_device_manufacturer CHECK (
    manufacturer IS NULL OR (BTRIM(manufacturer) <> '' AND LENGTH(manufacturer) <= 120)
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage115_device_hardware_revision,
  ADD CONSTRAINT ck_stage115_device_hardware_revision CHECK (
    hardware_revision IS NULL OR (BTRIM(hardware_revision) <> '' AND LENGTH(hardware_revision) <= 120)
  ) NOT VALID;
ALTER TABLE eld_devices VALIDATE CONSTRAINT ck_stage115_device_manufacturer;
ALTER TABLE eld_devices VALIDATE CONSTRAINT ck_stage115_device_hardware_revision;

CREATE TABLE IF NOT EXISTS device_compatibility_candidates (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  manufacturer VARCHAR(120) NOT NULL,
  device_model VARCHAR(160) NOT NULL,
  hardware_revision VARCHAR(120) NOT NULL,
  firmware_version VARCHAR(120) NOT NULL,
  software_candidate_sha VARCHAR(40) NOT NULL,
  engineering_status VARCHAR(24) NOT NULL DEFAULT 'Candidate',
  certification_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
  external_hold_reason VARCHAR(500) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  CONSTRAINT ck_stage115_candidate_manufacturer CHECK (BTRIM(manufacturer) <> ''),
  CONSTRAINT ck_stage115_candidate_model CHECK (BTRIM(device_model) <> ''),
  CONSTRAINT ck_stage115_candidate_hardware_revision CHECK (BTRIM(hardware_revision) <> ''),
  CONSTRAINT ck_stage115_candidate_firmware CHECK (BTRIM(firmware_version) <> ''),
  CONSTRAINT ck_stage115_candidate_sha CHECK (software_candidate_sha ~ '^[0-9a-f]{40}$'),
  CONSTRAINT ck_stage115_candidate_engineering_status CHECK (engineering_status IN ('Candidate','Deferred','Rejected')),
  CONSTRAINT ck_stage115_candidate_external_hold CHECK (certification_status='ExternalHold'),
  CONSTRAINT ck_stage115_candidate_hold_reason CHECK (BTRIM(external_hold_reason) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage115_device_compatibility_tuple_candidate
  ON device_compatibility_candidates (
    UPPER(BTRIM(manufacturer)),
    UPPER(BTRIM(device_model)),
    UPPER(BTRIM(hardware_revision)),
    UPPER(BTRIM(firmware_version)),
    software_candidate_sha
  );
CREATE INDEX IF NOT EXISTS ix_stage115_device_compatibility_lookup
  ON device_compatibility_candidates (
    UPPER(BTRIM(manufacturer)),
    UPPER(BTRIM(device_model)),
    UPPER(BTRIM(hardware_revision)),
    UPPER(BTRIM(firmware_version)),
    created_at DESC,
    id DESC
  );

COMMENT ON TABLE device_compatibility_candidates IS
  'Exact hardware/firmware and software-SHA engineering candidates. Every row remains ExternalHold and is not certification evidence.';
COMMENT ON COLUMN device_compatibility_candidates.certification_status IS
  'Locked to ExternalHold. A later frozen certification gate must introduce evidence-bound promotion.';

CREATE OR REPLACE FUNCTION stage115_protect_device_compatibility_candidate()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF NEW.manufacturer IS DISTINCT FROM OLD.manufacturer
     OR NEW.device_model IS DISTINCT FROM OLD.device_model
     OR NEW.hardware_revision IS DISTINCT FROM OLD.hardware_revision
     OR NEW.firmware_version IS DISTINCT FROM OLD.firmware_version
     OR NEW.software_candidate_sha IS DISTINCT FROM OLD.software_candidate_sha
     OR NEW.certification_status IS DISTINCT FROM OLD.certification_status
     OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
    RAISE EXCEPTION 'Stage115 compatibility candidate identity and certification hold are immutable'
      USING ERRCODE='23514', CONSTRAINT='ck_device_compatibility_candidate_immutable';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage115_protect_device_compatibility_candidate ON device_compatibility_candidates;
CREATE TRIGGER trg_stage115_protect_device_compatibility_candidate
BEFORE UPDATE ON device_compatibility_candidates
FOR EACH ROW EXECUTE FUNCTION stage115_protect_device_compatibility_candidate();

REVOKE ALL ON TABLE device_compatibility_candidates FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_compatibility_candidates_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage115_protect_device_compatibility_candidate() FROM PUBLIC;

DO $stage115_runtime_security$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    GRANT SELECT (manufacturer,hardware_revision) ON eld_devices TO opstrax_app;
    GRANT SELECT ON device_compatibility_candidates TO opstrax_app;
    REVOKE INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER ON device_compatibility_candidates FROM opstrax_app;
    REVOKE ALL ON SEQUENCE device_compatibility_candidates_id_seq FROM opstrax_app;
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT SELECT,INSERT,UPDATE,DELETE ON device_compatibility_candidates TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_compatibility_candidates_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage115_protect_device_compatibility_candidate() TO opstrax_system;
  END IF;
END
$stage115_runtime_security$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage115_device_compatibility_candidate_registry',
        'Exact-tuple read-only DeviceOps candidate registry locked to ExternalHold')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
