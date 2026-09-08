-- Stage 122 — explicit work-package to installation traceability.
--
-- A link records that the assigned installer associated one governed work
-- package with one persisted installation after completing the software
-- checklist/reference boundary. It does not verify artifact content, prove
-- physical work, or certify the device or installation.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage122_work_package_owner
  ON device_installation_work_packages(company_id,id,device_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage122_installation_owner
  ON device_installations(company_id,id,device_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage122_user_owner
  ON users(company_id,id);

CREATE TABLE IF NOT EXISTS device_installation_work_package_links (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  work_package_id BIGINT NOT NULL,
  installation_id BIGINT NOT NULL,
  link_assurance_status VARCHAR(32) NOT NULL DEFAULT 'RecordedUnverified',
  physical_work_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  idempotency_key VARCHAR(120) NOT NULL,
  linked_by BIGINT NOT NULL,
  linked_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage122_link_work FOREIGN KEY(company_id,work_package_id,device_id)
    REFERENCES device_installation_work_packages(company_id,id,device_id),
  CONSTRAINT fk_stage122_link_installation FOREIGN KEY(company_id,installation_id,device_id)
    REFERENCES device_installations(company_id,id,device_id),
  CONSTRAINT fk_stage122_link_actor FOREIGN KEY(company_id,linked_by)
    REFERENCES users(company_id,id),
  CONSTRAINT ck_stage122_link_assurance CHECK (link_assurance_status='RecordedUnverified'),
  CONSTRAINT ck_stage122_link_no_physical_claim CHECK (physical_work_claim=FALSE),
  CONSTRAINT ck_stage122_link_no_certification_claim CHECK (certification_claim=FALSE),
  CONSTRAINT ck_stage122_link_idempotency CHECK (BTRIM(idempotency_key)<>''),
  UNIQUE(company_id,work_package_id),
  UNIQUE(company_id,installation_id),
  UNIQUE(company_id,idempotency_key)
);

CREATE INDEX IF NOT EXISTS ix_stage122_links_device_recent
  ON device_installation_work_package_links(company_id,device_id,linked_at DESC,id DESC);

CREATE OR REPLACE FUNCTION stage122_guard_installation_work_link()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE
  work_row RECORD;
  installation_row RECORD;
  actor_branch BIGINT;
  required_items TEXT[];
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Installation work-package links are append-only'
      USING ERRCODE='23514',CONSTRAINT='ck_stage122_work_link_immutable';
  END IF;

  SELECT w.branch_id,w.device_id,w.vehicle_id,w.assigned_installer_user_id,d.device_category
    INTO work_row
    FROM device_installation_work_packages w
    JOIN eld_devices d ON d.company_id=w.company_id AND d.id=w.device_id AND d.deleted_at IS NULL
   WHERE w.company_id=NEW.company_id AND w.id=NEW.work_package_id AND w.device_id=NEW.device_id;
  IF NOT FOUND OR work_row.branch_id IS DISTINCT FROM NEW.branch_id THEN
    RAISE EXCEPTION 'Work package is outside the exact device and branch scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage122_work_link_scope';
  END IF;

  SELECT i.branch_id,i.device_id,i.vehicle_id,i.status
    INTO installation_row
    FROM device_installations i
   WHERE i.company_id=NEW.company_id AND i.id=NEW.installation_id AND i.device_id=NEW.device_id;
  IF NOT FOUND OR installation_row.branch_id IS DISTINCT FROM NEW.branch_id
     OR installation_row.vehicle_id IS DISTINCT FROM work_row.vehicle_id
     OR installation_row.status NOT IN ('Installed','Verified','Failed','Removed') THEN
    RAISE EXCEPTION 'Installation does not match the work-package device, vehicle, branch, or lifecycle'
      USING ERRCODE='23514',CONSTRAINT='ck_stage122_installation_link_scope';
  END IF;

  SELECT branch_id INTO actor_branch FROM users
   WHERE company_id=NEW.company_id AND id=NEW.linked_by AND status='Active';
  IF NOT FOUND OR NEW.linked_by IS DISTINCT FROM work_row.assigned_installer_user_id
     OR (actor_branch IS NOT NULL AND actor_branch IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'Only the active assigned installer can link this work package'
      USING ERRCODE='23514',CONSTRAINT='ck_stage122_installation_link_actor';
  END IF;

  required_items:=ARRAY['DeviceIdentity','VehicleIdentity','Mounting','PrimaryPower','Ground','Ignition','Harness'];
  IF COALESCE(work_row.device_category,'') ~* '(gps|eld|telematics)' THEN
    required_items:=required_items||ARRAY['GNSSAntenna','CellularAntenna'];
  END IF;
  IF COALESCE(work_row.device_category,'') ~* '(j1939|can|obd)' THEN
    required_items:=required_items||ARRAY['CANBus'];
  END IF;
  IF COALESCE(work_row.device_category,'') ~* '(camera|dashcam|video)' THEN
    required_items:=required_items||ARRAY['CameraAlignment'];
  END IF;
  IF COALESCE(work_row.device_category,'') ~* '(temperature|fuel|tire|sensor)' THEN
    required_items:=required_items||ARRAY['SensorPlacement'];
  END IF;

  IF EXISTS (
    SELECT 1 FROM unnest(required_items) required(item)
     WHERE COALESCE((
       SELECT o.observed_result
         FROM device_installation_checklist_observations o
        WHERE o.company_id=NEW.company_id AND o.work_package_id=NEW.work_package_id
          AND o.checklist_item=required.item
        ORDER BY o.observed_at DESC,o.id DESC LIMIT 1
     ),'') NOT IN ('Pass','NotApplicable')
  ) THEN
    RAISE EXCEPTION 'Every required checklist item needs a latest Pass or NotApplicable observation'
      USING ERRCODE='23514',CONSTRAINT='ck_stage122_installation_link_checklist';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM device_installation_artifact_references a
     WHERE a.company_id=NEW.company_id AND a.work_package_id=NEW.work_package_id
  ) THEN
    RAISE EXCEPTION 'At least one artifact reference is required before linking the installation'
      USING ERRCODE='23514',CONSTRAINT='ck_stage122_installation_link_artifact';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage122_guard_installation_work_link ON device_installation_work_package_links;
CREATE TRIGGER trg_stage122_guard_installation_work_link
BEFORE INSERT OR UPDATE OR DELETE ON device_installation_work_package_links
FOR EACH ROW EXECUTE FUNCTION stage122_guard_installation_work_link();

ALTER TABLE device_installation_work_package_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_installation_work_package_links FORCE ROW LEVEL SECURITY;

DO $stage122_security$
BEGIN
  DROP POLICY IF EXISTS tenant_isolation ON device_installation_work_package_links;
  DROP POLICY IF EXISTS platform_admin_bypass ON device_installation_work_package_links;
  DROP POLICY IF EXISTS tenant_ticket_app ON device_installation_work_package_links;
  DROP POLICY IF EXISTS system_control_plane ON device_installation_work_package_links;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    REVOKE ALL ON TABLE device_installation_work_package_links FROM opstrax_app;
    IF to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
      CREATE POLICY tenant_ticket_app ON device_installation_work_package_links
        FOR ALL TO opstrax_app
        USING (company_id=(SELECT opstrax_security.current_tenant_id()))
        WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
      GRANT SELECT,INSERT ON TABLE device_installation_work_package_links TO opstrax_app;
    END IF;
    GRANT USAGE,SELECT ON SEQUENCE device_installation_work_package_links_id_seq TO opstrax_app;
    GRANT EXECUTE ON FUNCTION stage122_guard_installation_work_link() TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    REVOKE ALL ON TABLE device_installation_work_package_links FROM opstrax_system;
    CREATE POLICY system_control_plane ON device_installation_work_package_links
      FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    GRANT SELECT,INSERT ON TABLE device_installation_work_package_links TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_installation_work_package_links_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage122_guard_installation_work_link() TO opstrax_system;
  END IF;
END
$stage122_security$;

REVOKE ALL ON TABLE device_installation_work_package_links FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_installation_work_package_links_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage122_guard_installation_work_link() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage122_installation_work_package_links',
        'Append-only explicit installation links gated by complete operator-recorded work packages')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
