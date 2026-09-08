-- Stage 121 — installer appointment, checklist and artifact-reference workflow.
--
-- This is an operator evidence-recording boundary.  A scheduled appointment is
-- not proof that an installer attended.  A checklist result is an unverified
-- operator assertion.  An artifact reference proves only that a hash-addressed
-- reference was recorded.  None of these rows certify hardware or physical work.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage121_device_owner
  ON eld_devices(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage121_vehicle_owner
  ON vehicles(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage121_user_owner
  ON users(company_id,id);

CREATE TABLE IF NOT EXISTS device_installation_work_packages (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  assigned_installer_user_id BIGINT NOT NULL,
  work_order_reference VARCHAR(120) NOT NULL,
  appointment_start TIMESTAMPTZ NOT NULL,
  appointment_end TIMESTAMPTZ NOT NULL,
  service_location VARCHAR(160) NOT NULL,
  work_scope VARCHAR(1000) NOT NULL,
  idempotency_key VARCHAR(120) NOT NULL,
  physical_appointment_claim BOOLEAN NOT NULL DEFAULT FALSE,
  physical_work_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  created_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage121_work_device FOREIGN KEY(company_id,device_id)
    REFERENCES eld_devices(company_id,id),
  CONSTRAINT fk_stage121_work_vehicle FOREIGN KEY(company_id,vehicle_id)
    REFERENCES vehicles(company_id,id),
  CONSTRAINT fk_stage121_work_installer FOREIGN KEY(company_id,assigned_installer_user_id)
    REFERENCES users(company_id,id),
  CONSTRAINT fk_stage121_work_creator FOREIGN KEY(company_id,created_by)
    REFERENCES users(company_id,id),
  CONSTRAINT ck_stage121_work_order_reference CHECK (BTRIM(work_order_reference)<>''),
  CONSTRAINT ck_stage121_appointment_window CHECK (appointment_end>appointment_start),
  CONSTRAINT ck_stage121_service_location CHECK (BTRIM(service_location)<>''),
  CONSTRAINT ck_stage121_work_scope CHECK (CHAR_LENGTH(BTRIM(work_scope))>=5),
  CONSTRAINT ck_stage121_no_appointment_claim CHECK (physical_appointment_claim=FALSE),
  CONSTRAINT ck_stage121_no_work_claim CHECK (physical_work_claim=FALSE),
  CONSTRAINT ck_stage121_no_certification_claim CHECK (certification_claim=FALSE),
  UNIQUE(company_id,work_order_reference),
  UNIQUE(company_id,idempotency_key),
  UNIQUE(company_id,id,device_id)
);

CREATE TABLE IF NOT EXISTS device_installation_checklist_observations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  work_package_id BIGINT NOT NULL,
  checklist_item VARCHAR(40) NOT NULL,
  observed_result VARCHAR(24) NOT NULL,
  evidence_reference VARCHAR(240) NOT NULL,
  observation_notes VARCHAR(1000) NOT NULL,
  observed_at TIMESTAMPTZ NOT NULL,
  assurance_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
  physical_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  idempotency_key VARCHAR(120) NOT NULL,
  recorded_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage121_checklist_work FOREIGN KEY(company_id,work_package_id,device_id)
    REFERENCES device_installation_work_packages(company_id,id,device_id),
  CONSTRAINT fk_stage121_checklist_actor FOREIGN KEY(company_id,recorded_by)
    REFERENCES users(company_id,id),
  CONSTRAINT ck_stage121_checklist_item CHECK (checklist_item IN (
    'DeviceIdentity','VehicleIdentity','Mounting','PrimaryPower','Ground','Ignition',
    'GNSSAntenna','CellularAntenna','Harness','CANBus','CameraAlignment','SensorPlacement')),
  CONSTRAINT ck_stage121_checklist_result CHECK (observed_result IN ('Pass','Fail','NotObserved','NotApplicable')),
  CONSTRAINT ck_stage121_checklist_reference CHECK (CHAR_LENGTH(BTRIM(evidence_reference))>=3),
  CONSTRAINT ck_stage121_checklist_notes CHECK (CHAR_LENGTH(BTRIM(observation_notes))>=3),
  CONSTRAINT ck_stage121_checklist_time CHECK (observed_at<=created_at+INTERVAL '5 minutes'),
  CONSTRAINT ck_stage121_checklist_assurance CHECK (assurance_status='Unverified'),
  CONSTRAINT ck_stage121_checklist_no_physical_claim CHECK (physical_evidence_claim=FALSE),
  CONSTRAINT ck_stage121_checklist_no_certification_claim CHECK (certification_claim=FALSE),
  UNIQUE(company_id,work_package_id,idempotency_key)
);

CREATE TABLE IF NOT EXISTS device_installation_artifact_references (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  work_package_id BIGINT NOT NULL,
  artifact_type VARCHAR(40) NOT NULL,
  object_key TEXT NOT NULL,
  sha256 VARCHAR(64) NOT NULL,
  captured_at TIMESTAMPTZ NOT NULL,
  content_verification_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
  physical_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  idempotency_key VARCHAR(120) NOT NULL,
  recorded_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage121_artifact_work FOREIGN KEY(company_id,work_package_id,device_id)
    REFERENCES device_installation_work_packages(company_id,id,device_id),
  CONSTRAINT fk_stage121_artifact_actor FOREIGN KEY(company_id,recorded_by)
    REFERENCES users(company_id,id),
  CONSTRAINT ck_stage121_artifact_type CHECK (artifact_type IN (
    'InstallationPhoto','SerialLabel','WiringPhoto','PowerReading','TechnicianChecklist',
    'CommissioningReport','RemovalPhoto','OtherDocument')),
  CONSTRAINT ck_stage121_artifact_object_key CHECK (
    BTRIM(object_key)<>'' AND LEFT(object_key,1)<>'/' AND object_key !~ '\\\\'
    AND POSITION('..' IN LOWER(object_key))=0 AND POSITION('%2e' IN LOWER(object_key))=0
    AND object_key !~ '^[a-zA-Z][a-zA-Z0-9+.-]*:'),
  CONSTRAINT ck_stage121_artifact_sha CHECK (sha256 ~ '^[0-9a-f]{64}$'),
  CONSTRAINT ck_stage121_artifact_time CHECK (captured_at<=created_at+INTERVAL '5 minutes'),
  CONSTRAINT ck_stage121_artifact_assurance CHECK (content_verification_status='Unverified'),
  CONSTRAINT ck_stage121_artifact_no_physical_claim CHECK (physical_evidence_claim=FALSE),
  CONSTRAINT ck_stage121_artifact_no_certification_claim CHECK (certification_claim=FALSE),
  UNIQUE(company_id,work_package_id,idempotency_key),
  UNIQUE(company_id,work_package_id,artifact_type,sha256)
);

CREATE INDEX IF NOT EXISTS ix_stage121_work_device_schedule
  ON device_installation_work_packages(company_id,device_id,appointment_start DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage121_checklist_work_latest
  ON device_installation_checklist_observations(company_id,work_package_id,checklist_item,observed_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage121_artifact_work_recent
  ON device_installation_artifact_references(company_id,work_package_id,captured_at DESC,id DESC);

CREATE OR REPLACE FUNCTION stage121_guard_work_package()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE device_branch BIGINT; vehicle_branch BIGINT; installer_branch BIGINT;
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Installation work packages are append-only'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_work_package_immutable';
  END IF;
  SELECT branch_id INTO device_branch FROM eld_devices
   WHERE company_id=NEW.company_id AND id=NEW.device_id AND deleted_at IS NULL;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'Work-package device is outside the active tenant inventory'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_work_device_scope';
  END IF;
  SELECT branch_id INTO vehicle_branch FROM vehicles
   WHERE company_id=NEW.company_id AND id=NEW.vehicle_id AND deleted_at IS NULL;
  IF NOT FOUND OR device_branch IS DISTINCT FROM vehicle_branch
     OR NEW.branch_id IS DISTINCT FROM vehicle_branch THEN
    RAISE EXCEPTION 'Work-package device and vehicle must match the exact branch'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_work_branch_scope';
  END IF;
  SELECT branch_id INTO installer_branch FROM users
   WHERE company_id=NEW.company_id AND id=NEW.assigned_installer_user_id AND status='Active';
  IF NOT FOUND OR (installer_branch IS NOT NULL AND installer_branch IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'Assigned installer is outside the active tenant/branch scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_installer_scope';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM users WHERE company_id=NEW.company_id AND id=NEW.created_by AND status='Active') THEN
    RAISE EXCEPTION 'Work-package creator is outside the active tenant scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_creator_scope';
  END IF;
  RETURN NEW;
END
$fn$;

CREATE OR REPLACE FUNCTION stage121_guard_work_evidence()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE package_installer BIGINT; recorder_branch BIGINT;
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Installation work-package evidence is append-only'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_work_evidence_immutable';
  END IF;
  SELECT w.assigned_installer_user_id INTO package_installer
    FROM device_installation_work_packages w
    JOIN eld_devices d ON d.company_id=w.company_id AND d.id=w.device_id
    WHERE w.company_id=NEW.company_id AND w.id=NEW.work_package_id
      AND w.device_id=NEW.device_id AND w.branch_id IS NOT DISTINCT FROM NEW.branch_id
      AND d.branch_id IS NOT DISTINCT FROM NEW.branch_id AND d.deleted_at IS NULL
  ;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'Work-package evidence does not match the exact device and branch'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_work_evidence_scope';
  END IF;
  SELECT branch_id INTO recorder_branch FROM users
   WHERE company_id=NEW.company_id AND id=NEW.recorded_by AND status='Active';
  IF NOT FOUND OR NEW.recorded_by IS DISTINCT FROM package_installer
     OR (recorder_branch IS NOT NULL AND recorder_branch IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'Work-package evidence must be recorded by the active assigned installer'
      USING ERRCODE='23514',CONSTRAINT='ck_stage121_work_evidence_recorder_scope';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage121_guard_work_package ON device_installation_work_packages;
CREATE TRIGGER trg_stage121_guard_work_package
BEFORE INSERT OR UPDATE OR DELETE ON device_installation_work_packages
FOR EACH ROW EXECUTE FUNCTION stage121_guard_work_package();
DROP TRIGGER IF EXISTS trg_stage121_guard_checklist ON device_installation_checklist_observations;
CREATE TRIGGER trg_stage121_guard_checklist
BEFORE INSERT OR UPDATE OR DELETE ON device_installation_checklist_observations
FOR EACH ROW EXECUTE FUNCTION stage121_guard_work_evidence();
DROP TRIGGER IF EXISTS trg_stage121_guard_artifact ON device_installation_artifact_references;
CREATE TRIGGER trg_stage121_guard_artifact
BEFORE INSERT OR UPDATE OR DELETE ON device_installation_artifact_references
FOR EACH ROW EXECUTE FUNCTION stage121_guard_work_evidence();

ALTER TABLE device_installation_work_packages ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_installation_work_packages FORCE ROW LEVEL SECURITY;
ALTER TABLE device_installation_checklist_observations ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_installation_checklist_observations FORCE ROW LEVEL SECURITY;
ALTER TABLE device_installation_artifact_references ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_installation_artifact_references FORCE ROW LEVEL SECURITY;

DO $stage121_security$
DECLARE table_name TEXT;
BEGIN
  FOREACH table_name IN ARRAY ARRAY[
    'device_installation_work_packages',
    'device_installation_checklist_observations',
    'device_installation_artifact_references'
  ] LOOP
    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I',table_name);
    EXECUTE format('DROP POLICY IF EXISTS platform_admin_bypass ON %I',table_name);
    EXECUTE format('DROP POLICY IF EXISTS tenant_ticket_app ON %I',table_name);
    EXECUTE format('DROP POLICY IF EXISTS system_control_plane ON %I',table_name);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
      EXECUTE format('REVOKE ALL ON TABLE %I FROM opstrax_app',table_name);
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
      EXECUTE format('REVOKE ALL ON TABLE %I FROM opstrax_system',table_name);
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
       AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
      EXECUTE format(
        'CREATE POLICY tenant_ticket_app ON %I FOR ALL TO opstrax_app USING (company_id=(SELECT opstrax_security.current_tenant_id())) WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()))',
        table_name);
      EXECUTE format('GRANT SELECT,INSERT ON TABLE %I TO opstrax_app',table_name);
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
      EXECUTE format('CREATE POLICY system_control_plane ON %I FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE)',table_name);
      EXECUTE format('GRANT SELECT,INSERT ON TABLE %I TO opstrax_system',table_name);
    END IF;
    EXECUTE format('REVOKE ALL ON TABLE %I FROM PUBLIC',table_name);
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    GRANT USAGE,SELECT ON SEQUENCE device_installation_work_packages_id_seq TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE device_installation_checklist_observations_id_seq TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE device_installation_artifact_references_id_seq TO opstrax_app;
    GRANT EXECUTE ON FUNCTION stage121_guard_work_package() TO opstrax_app;
    GRANT EXECUTE ON FUNCTION stage121_guard_work_evidence() TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT USAGE,SELECT ON SEQUENCE device_installation_work_packages_id_seq TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_installation_checklist_observations_id_seq TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_installation_artifact_references_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage121_guard_work_package() TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage121_guard_work_evidence() TO opstrax_system;
  END IF;
END
$stage121_security$;

REVOKE ALL ON SEQUENCE device_installation_work_packages_id_seq FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_installation_checklist_observations_id_seq FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_installation_artifact_references_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage121_guard_work_package() FROM PUBLIC;
REVOKE ALL ON FUNCTION stage121_guard_work_evidence() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage121_device_installation_work_packages',
        'Append-only installer work packages, checklist observations and artifact references without physical claims')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
