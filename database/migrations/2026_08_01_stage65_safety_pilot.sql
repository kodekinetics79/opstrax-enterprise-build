BEGIN;

-- Safety pilot integrity contract. Additive and re-runnable: preserve legacy rows,
-- derive branch ownership only from authoritative linked fleet records, and fail
-- rather than silently choosing between duplicate business identities.

ALTER TABLE incidents ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE incidents ADD COLUMN IF NOT EXISTS row_version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE incidents ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL;
ALTER TABLE incidents ADD COLUMN IF NOT EXISTS idempotency_request_hash VARCHAR(64) NULL;
ALTER TABLE incident_evidence ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE incident_evidence ADD COLUMN IF NOT EXISTS content_hash VARCHAR(64) NULL;
ALTER TABLE insurance_reports ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;

ALTER TABLE coaching_tasks ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE coaching_tasks ADD COLUMN IF NOT EXISTS row_version BIGINT NOT NULL DEFAULT 0;
ALTER TABLE coaching_tasks ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL;
ALTER TABLE coaching_tasks ADD COLUMN IF NOT EXISTS idempotency_request_hash VARCHAR(64) NULL;
ALTER TABLE coaching_tasks ADD COLUMN IF NOT EXISTS effectiveness_formula_version VARCHAR(40) NULL;
ALTER TABLE coaching_tasks ADD COLUMN IF NOT EXISTS effectiveness_evaluated_at TIMESTAMPTZ NULL;
ALTER TABLE coaching_tasks ADD COLUMN IF NOT EXISTS effectiveness_observation_json JSONB NULL;
ALTER TABLE coaching_notes ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE driver_safety_scorecards ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE vehicle_safety_scorecards ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE safety_trends ADD COLUMN IF NOT EXISTS trend_date DATE NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS row_version BIGINT NOT NULL DEFAULT 0;

ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(100) NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS idempotency_request_hash VARCHAR(64) NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS row_version INT NOT NULL DEFAULT 1;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS driver_repair_acknowledged_at TIMESTAMPTZ NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS driver_repair_acknowledged_by BIGINT NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS signature_attestation_text TEXT NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS signed_at TIMESTAMPTZ NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS signed_by BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS out_of_service BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMPTZ NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS reviewed_by BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS repair_certified_at TIMESTAMPTZ NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS repair_certified_by BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS driver_acknowledged_at TIMESTAMPTZ NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS driver_acknowledged_by BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS row_version INT NOT NULL DEFAULT 1;

ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS vehicle_id BIGINT NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS country_code VARCHAR(10) NOT NULL DEFAULT 'US';
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS profile_id BIGINT NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS start_time TIMESTAMPTZ NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS end_time TIMESTAMPTZ NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS duration_minutes INT NOT NULL DEFAULT 0;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS location VARCHAR(200) NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS is_certified BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS source VARCHAR(40) NOT NULL DEFAULT 'manual';
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS source_event_id VARCHAR(120) NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS certified_by BIGINT NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS certified_at TIMESTAMPTZ NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
ALTER TABLE hos_clocks ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE IF EXISTS hos_records ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE compliance_violations ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS company_id BIGINT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS malfunction_code VARCHAR(80) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS malfunction_description TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS malfunction_resolved_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS malfunction_resolved_by BIGINT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS resolution_evidence TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS row_version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS provider_sync_status VARCHAR(40) NULL;

CREATE TABLE IF NOT EXISTS hos_certifications (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  driver_id BIGINT NOT NULL,
  log_date DATE NOT NULL,
  attestation_text TEXT NOT NULL,
  attestation_hash VARCHAR(64) NOT NULL,
  certified_by BIGINT NOT NULL,
  certified_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  source_revision VARCHAR(64) NOT NULL,
  source_snapshot JSONB NOT NULL DEFAULT '[]'::jsonb,
  UNIQUE(company_id,driver_id,log_date,source_revision)
);

ALTER TABLE hos_certifications ADD COLUMN IF NOT EXISTS source_snapshot JSONB NOT NULL DEFAULT '[]'::jsonb;

CREATE TABLE IF NOT EXISTS eld_malfunction_history (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  eld_device_id BIGINT NOT NULL,
  event_type VARCHAR(40) NOT NULL,
  from_status VARCHAR(40) NOT NULL,
  to_status VARCHAR(40) NOT NULL,
  malfunction_code VARCHAR(80) NULL,
  malfunction_description TEXT NULL,
  evidence TEXT NULL,
  actor_user_id BIGINT NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Reject conflicting driver/vehicle ownership instead of guessing a branch.
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM dvir_reports r
    JOIN drivers d ON d.company_id=r.company_id AND d.id=r.driver_id
    JOIN vehicles v ON v.company_id=r.company_id AND v.id=r.vehicle_id
    WHERE d.branch_id IS NOT NULL AND v.branch_id IS NOT NULL AND d.branch_id<>v.branch_id
  ) THEN
    RAISE EXCEPTION 'Stage 65 blocked: a DVIR links a driver and vehicle from different branches';
  END IF;
END $$;

UPDATE incidents i SET branch_id=COALESCE(
  (SELECT d.branch_id FROM drivers d WHERE d.company_id=i.company_id AND d.id=i.driver_id),
  (SELECT v.branch_id FROM vehicles v WHERE v.company_id=i.company_id AND v.id=i.vehicle_id),
  (SELECT j.branch_id FROM jobs j WHERE j.company_id=i.company_id AND j.id=i.job_id),
  (SELECT r.branch_id FROM routes r WHERE r.company_id=i.company_id AND r.id=i.route_id)
) WHERE i.branch_id IS NULL;
UPDATE incident_evidence e SET branch_id=i.branch_id FROM incidents i
 WHERE e.company_id=i.company_id AND e.incident_id=i.id AND e.branch_id IS NULL;
UPDATE insurance_reports r SET branch_id=i.branch_id FROM incidents i
 WHERE r.company_id=i.company_id AND r.incident_id=i.id AND r.branch_id IS NULL;

UPDATE coaching_tasks t SET branch_id=d.branch_id FROM drivers d
 WHERE t.company_id=d.company_id AND t.driver_id=d.id AND t.branch_id IS NULL;
UPDATE coaching_notes n SET branch_id=t.branch_id FROM coaching_tasks t
 WHERE n.company_id=t.company_id AND n.coaching_task_id=t.id AND n.branch_id IS NULL;
UPDATE driver_safety_scorecards s SET branch_id=d.branch_id FROM drivers d
 WHERE s.company_id=d.company_id AND s.driver_id=d.id AND s.branch_id IS NULL;
UPDATE vehicle_safety_scorecards s SET branch_id=v.branch_id FROM vehicles v
 WHERE s.company_id=v.company_id AND s.vehicle_id=v.id AND s.branch_id IS NULL;
UPDATE safety_events s SET branch_id=d.branch_id FROM drivers d
 WHERE s.company_id=d.company_id AND s.driver_id=d.id AND s.branch_id IS NULL;
UPDATE safety_events s SET branch_id=v.branch_id FROM vehicles v
 WHERE s.company_id=v.company_id AND s.vehicle_id=v.id AND s.branch_id IS NULL;

UPDATE dvir_reports r SET branch_id=COALESCE(d.branch_id,v.branch_id)
 FROM drivers d, vehicles v
 WHERE r.company_id=d.company_id AND r.driver_id=d.id
   AND r.company_id=v.company_id AND r.vehicle_id=v.id AND r.branch_id IS NULL;
UPDATE dvir_defects x SET branch_id=r.branch_id FROM dvir_reports r
 WHERE x.company_id=r.company_id AND x.dvir_report_id=r.id AND x.branch_id IS NULL;

UPDATE hos_logs h SET branch_id=d.branch_id FROM drivers d
 WHERE h.company_id=d.company_id AND h.driver_id=d.id AND h.branch_id IS NULL;
UPDATE hos_clocks h SET branch_id=d.branch_id FROM drivers d
 WHERE h.company_id=d.company_id AND h.driver_id=d.id AND h.branch_id IS NULL;
DO $$ BEGIN
  IF to_regclass('public.hos_records') IS NOT NULL THEN
    EXECUTE 'UPDATE hos_records h SET branch_id=d.branch_id FROM drivers d
             WHERE h.company_id=d.company_id AND h.driver_id=d.id AND h.branch_id IS NULL';
  END IF;
END $$;
UPDATE compliance_violations x SET branch_id=d.branch_id FROM drivers d
 WHERE x.company_id=d.company_id AND x.driver_id=d.id AND x.branch_id IS NULL;
UPDATE compliance_violations x SET branch_id=v.branch_id FROM vehicles v
 WHERE x.company_id=v.company_id AND x.vehicle_id=v.id AND x.branch_id IS NULL;
UPDATE eld_devices e SET company_id=d.company_id,branch_id=COALESCE(e.branch_id,d.branch_id) FROM drivers d
 WHERE e.driver_id=d.id AND (e.company_id IS NULL OR e.branch_id IS NULL);
UPDATE eld_devices e SET company_id=v.company_id,branch_id=COALESCE(e.branch_id,v.branch_id) FROM vehicles v
 WHERE e.vehicle_id=v.id AND (e.company_id IS NULL OR e.branch_id IS NULL);

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM incidents WHERE idempotency_key IS NOT NULL GROUP BY company_id,idempotency_key HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate incident idempotency keys require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM incidents WHERE deleted_at IS NULL GROUP BY company_id,incident_number HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate active incident numbers require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM insurance_reports GROUP BY company_id,incident_id HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate incident insurance reports require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM coaching_tasks GROUP BY company_id,task_number HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate coaching task numbers require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM coaching_tasks WHERE idempotency_key IS NOT NULL GROUP BY company_id,idempotency_key HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate coaching idempotency keys require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM driver_safety_scorecards GROUP BY company_id,driver_id HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate driver scorecards require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM vehicle_safety_scorecards GROUP BY company_id,vehicle_id HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate vehicle scorecards require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM safety_trends GROUP BY company_id,trend_date HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate safety trend dates require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM dvir_reports WHERE deleted_at IS NULL GROUP BY company_id,report_number HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate active DVIR report numbers require reconciliation';
  END IF;
  IF EXISTS (SELECT 1 FROM hos_logs WHERE source_event_id IS NOT NULL GROUP BY company_id,source,source_event_id HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 65 blocked: duplicate HOS source events require reconciliation';
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_incidents_company_idempotency
  ON incidents(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_incidents_company_number_active
  ON incidents(company_id,incident_number) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_incidents_company_branch_status
  ON incidents(company_id,branch_id,status,occurred_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_incident_evidence_company_branch_incident
  ON incident_evidence(company_id,branch_id,incident_id);
CREATE INDEX IF NOT EXISTS idx_insurance_reports_company_branch_incident
  ON insurance_reports(company_id,branch_id,incident_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_insurance_reports_company_incident
  ON insurance_reports(company_id,incident_id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_coaching_tasks_company_number ON coaching_tasks(company_id,task_number);
CREATE UNIQUE INDEX IF NOT EXISTS uq_coaching_tasks_company_idempotency
  ON coaching_tasks(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_coaching_tasks_active_safety_event
  ON coaching_tasks(company_id,safety_event_id)
  WHERE safety_event_id IS NOT NULL AND deleted_at IS NULL AND LOWER(status) NOT IN ('completed','cancelled','dismissed');
CREATE UNIQUE INDEX IF NOT EXISTS uq_coaching_tasks_active_dashcam_event
  ON coaching_tasks(company_id,dashcam_event_id)
  WHERE dashcam_event_id IS NOT NULL AND deleted_at IS NULL AND LOWER(status) NOT IN ('completed','cancelled','dismissed');
CREATE INDEX IF NOT EXISTS idx_coaching_tasks_company_branch_status
  ON coaching_tasks(company_id,branch_id,status,due_at) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_driver_safety_scorecards_company_driver
  ON driver_safety_scorecards(company_id,driver_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_safety_scorecards_company_vehicle
  ON vehicle_safety_scorecards(company_id,vehicle_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_safety_trends_company_date
  ON safety_trends(company_id,trend_date);

CREATE UNIQUE INDEX IF NOT EXISTS uq_dvir_reports_company_idempotency
  ON dvir_reports(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_dvir_reports_company_number_active
  ON dvir_reports(company_id,report_number) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_dvir_reports_company_branch_status
  ON dvir_reports(company_id,branch_id,inspection_status,submitted_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_dvir_defects_company_branch_report
  ON dvir_defects(company_id,branch_id,dvir_report_id);
CREATE INDEX IF NOT EXISTS idx_dvir_defects_company_branch_status_oos
  ON dvir_defects(company_id,branch_id,status,out_of_service);

CREATE UNIQUE INDEX IF NOT EXISTS uq_hos_logs_company_source_event
  ON hos_logs(company_id,source,source_event_id) WHERE source_event_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_hos_logs_company_branch_driver_time
  ON hos_logs(company_id,branch_id,driver_id,start_time DESC) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_hos_certifications_company_branch_driver_date
  ON hos_certifications(company_id,branch_id,driver_id,log_date DESC);
CREATE INDEX IF NOT EXISTS idx_hos_clocks_company_branch_status
  ON hos_clocks(company_id,branch_id,status,drive_time_remaining_minutes);
DO $$ BEGIN
  IF to_regclass('public.hos_records') IS NOT NULL THEN
    EXECUTE 'CREATE INDEX IF NOT EXISTS idx_hos_records_company_branch_driver_shift
             ON hos_records(company_id,branch_id,driver_id,shift_date DESC)';
  END IF;
END $$;
CREATE INDEX IF NOT EXISTS idx_compliance_violations_company_branch_status
  ON compliance_violations(company_id,branch_id,status,detected_at DESC);
CREATE INDEX IF NOT EXISTS idx_eld_devices_company_branch_status
  ON eld_devices(company_id,branch_id,status) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_eld_malfunction_history_company_device
  ON eld_malfunction_history(company_id,eld_device_id,occurred_at DESC,id DESC);

-- A driver's certification is an append-only snapshot. Corrections remain possible,
-- but changing any material segment field invalidates that segment's certified flag
-- so the daily UI/API requires a new certification over the corrected snapshot.
CREATE OR REPLACE FUNCTION stage65_hos_day_lock_key(
  p_company_id BIGINT, p_driver_id BIGINT, p_log_date DATE, p_branch_id BIGINT)
RETURNS BIGINT LANGUAGE SQL STABLE AS $$
  SELECT hashtextextended(
    format('hos-cert:%s:%s:%s:%s', p_company_id, p_driver_id,
           (p_log_date - DATE '2000-01-01'),
           COALESCE(p_branch_id::TEXT, '<null>')), 0)
$$;

CREATE OR REPLACE FUNCTION stage65_lock_hos_day_on_insert()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  PERFORM pg_advisory_xact_lock(stage65_hos_day_lock_key(
    NEW.company_id, NEW.driver_id, NEW.log_date, NEW.branch_id));
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage65_hos_insert_day_lock ON hos_logs;
CREATE TRIGGER trg_stage65_hos_insert_day_lock
BEFORE INSERT ON hos_logs FOR EACH ROW
WHEN (NEW.deleted_at IS NULL)
EXECUTE FUNCTION stage65_lock_hos_day_on_insert();

CREATE OR REPLACE FUNCTION stage65_lock_hos_days_on_identity_change()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
  old_key BIGINT := stage65_hos_day_lock_key(OLD.company_id,OLD.driver_id,OLD.log_date,OLD.branch_id);
  new_key BIGINT := stage65_hos_day_lock_key(NEW.company_id,NEW.driver_id,NEW.log_date,NEW.branch_id);
BEGIN
  IF (OLD.company_id,OLD.driver_id,OLD.log_date,OLD.branch_id)
       IS NOT DISTINCT FROM
     (NEW.company_id,NEW.driver_id,NEW.log_date,NEW.branch_id)
     AND OLD.deleted_at IS NOT DISTINCT FROM NEW.deleted_at THEN
    RETURN NEW;
  END IF;
  IF NOT pg_try_advisory_xact_lock(LEAST(old_key,new_key)) THEN
    RAISE EXCEPTION 'HOS day is being certified; retry the segment change'
      USING ERRCODE='40001';
  END IF;
  IF old_key<>new_key AND NOT pg_try_advisory_xact_lock(GREATEST(old_key,new_key)) THEN
    RAISE EXCEPTION 'HOS day is being certified; retry the segment change'
      USING ERRCODE='40001';
  END IF;
  NEW.is_certified := FALSE;
  NEW.certified_at := NULL;
  NEW.certified_by := NULL;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage65_hos_identity_day_lock ON hos_logs;
CREATE TRIGGER trg_stage65_hos_identity_day_lock
BEFORE UPDATE OF company_id,driver_id,log_date,branch_id,deleted_at ON hos_logs
FOR EACH ROW EXECUTE FUNCTION stage65_lock_hos_days_on_identity_change();

CREATE OR REPLACE FUNCTION stage65_invalidate_hos_days_on_identity_change()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF (OLD.company_id,OLD.driver_id,OLD.log_date,OLD.branch_id)
       IS DISTINCT FROM
     (NEW.company_id,NEW.driver_id,NEW.log_date,NEW.branch_id)
     OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at THEN
    UPDATE hos_logs SET is_certified=FALSE,certified_at=NULL,certified_by=NULL
     WHERE id<>NEW.id AND deleted_at IS NULL AND is_certified=TRUE AND (
       (company_id=OLD.company_id AND driver_id=OLD.driver_id AND log_date=OLD.log_date
        AND branch_id IS NOT DISTINCT FROM OLD.branch_id)
       OR
       (company_id=NEW.company_id AND driver_id=NEW.driver_id AND log_date=NEW.log_date
        AND branch_id IS NOT DISTINCT FROM NEW.branch_id));
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage65_hos_identity_change_invalidates_days ON hos_logs;
CREATE TRIGGER trg_stage65_hos_identity_change_invalidates_days
AFTER UPDATE OF company_id,driver_id,log_date,branch_id,deleted_at ON hos_logs
FOR EACH ROW EXECUTE FUNCTION stage65_invalidate_hos_days_on_identity_change();

CREATE OR REPLACE FUNCTION stage65_invalidate_hos_certification_on_material_change()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF OLD.is_certified AND (
       OLD.vehicle_id IS DISTINCT FROM NEW.vehicle_id
    OR OLD.country_code IS DISTINCT FROM NEW.country_code
    OR OLD.profile_id IS DISTINCT FROM NEW.profile_id
    OR OLD.status IS DISTINCT FROM NEW.status
    OR OLD.start_time IS DISTINCT FROM NEW.start_time
    OR OLD.end_time IS DISTINCT FROM NEW.end_time
    OR OLD.duration_minutes IS DISTINCT FROM NEW.duration_minutes
    OR OLD.location IS DISTINCT FROM NEW.location
    OR OLD.source IS DISTINCT FROM NEW.source
    OR OLD.source_event_id IS DISTINCT FROM NEW.source_event_id
    OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at
  ) THEN
    NEW.is_certified := FALSE;
    NEW.certified_at := NULL;
    NEW.certified_by := NULL;
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage65_hos_material_change_invalidates_certification ON hos_logs;
CREATE TRIGGER trg_stage65_hos_material_change_invalidates_certification
BEFORE UPDATE OF vehicle_id,country_code,profile_id,status,start_time,end_time,duration_minutes,
                 location,source,source_event_id,deleted_at
ON hos_logs FOR EACH ROW
EXECUTE FUNCTION stage65_invalidate_hos_certification_on_material_change();

CREATE OR REPLACE FUNCTION stage65_invalidate_hos_certified_day()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  UPDATE hos_logs SET is_certified=FALSE,certified_at=NULL,certified_by=NULL
   WHERE company_id=NEW.company_id AND driver_id=NEW.driver_id AND log_date=NEW.log_date
     AND branch_id IS NOT DISTINCT FROM NEW.branch_id
     AND deleted_at IS NULL AND id<>NEW.id AND is_certified=TRUE;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage65_hos_segment_change_invalidates_day ON hos_logs;
CREATE TRIGGER trg_stage65_hos_segment_change_invalidates_day
AFTER UPDATE ON hos_logs FOR EACH ROW
WHEN (OLD.is_certified=TRUE AND NEW.is_certified=FALSE)
EXECUTE FUNCTION stage65_invalidate_hos_certified_day();

CREATE OR REPLACE FUNCTION stage65_invalidate_hos_day_on_insert()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  UPDATE hos_logs SET is_certified=FALSE,certified_at=NULL,certified_by=NULL
   WHERE company_id=NEW.company_id AND driver_id=NEW.driver_id AND log_date=NEW.log_date
     AND branch_id IS NOT DISTINCT FROM NEW.branch_id
     AND deleted_at IS NULL AND id<>NEW.id AND is_certified=TRUE;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage65_hos_insert_invalidates_certified_day ON hos_logs;
CREATE TRIGGER trg_stage65_hos_insert_invalidates_certified_day
AFTER INSERT ON hos_logs FOR EACH ROW
WHEN (NEW.deleted_at IS NULL)
EXECUTE FUNCTION stage65_invalidate_hos_day_on_insert();

CREATE OR REPLACE FUNCTION stage65_prevent_certified_hos_log_delete()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF OLD.is_certified THEN RAISE EXCEPTION 'Certified HOS segments cannot be deleted; create a correction and recertify'; END IF;
  RETURN OLD;
END $$;
DROP TRIGGER IF EXISTS trg_stage65_prevent_certified_hos_log_delete ON hos_logs;
CREATE TRIGGER trg_stage65_prevent_certified_hos_log_delete
BEFORE DELETE ON hos_logs FOR EACH ROW EXECUTE FUNCTION stage65_prevent_certified_hos_log_delete();

CREATE OR REPLACE FUNCTION stage65_guard_hos_certification_snapshot()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'HOS certification snapshots are immutable';
END $$;
DROP TRIGGER IF EXISTS trg_stage65_hos_certification_snapshot_immutable ON hos_certifications;
CREATE TRIGGER trg_stage65_hos_certification_snapshot_immutable
BEFORE UPDATE OR DELETE ON hos_certifications FOR EACH ROW
EXECUTE FUNCTION stage65_guard_hos_certification_snapshot();

-- Stage58's migration sweep runs before Stage65 on a clean chain, so explicitly
-- enroll this later-created tenant history table in the signed-ticket RLS contract.
ALTER TABLE eld_malfunction_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE eld_malfunction_history FORCE ROW LEVEL SECURITY;
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    DROP POLICY IF EXISTS tenant_ticket_app ON eld_malfunction_history;
    CREATE POLICY tenant_ticket_app ON eld_malfunction_history FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    GRANT SELECT,INSERT,UPDATE,DELETE ON eld_malfunction_history TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE eld_malfunction_history_id_seq TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    DROP POLICY IF EXISTS system_control_plane ON eld_malfunction_history;
    CREATE POLICY system_control_plane ON eld_malfunction_history FOR ALL TO opstrax_system
      USING (TRUE) WITH CHECK (TRUE);
    GRANT SELECT,INSERT,UPDATE,DELETE ON eld_malfunction_history TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE eld_malfunction_history_id_seq TO opstrax_system;
  END IF;
END $$;

ALTER TABLE hos_logs DROP CONSTRAINT IF EXISTS ck_stage65_hos_log_times;
ALTER TABLE hos_logs ADD CONSTRAINT ck_stage65_hos_log_times
  CHECK (duration_minutes>=0 AND (end_time IS NULL OR start_time IS NULL OR end_time>=start_time)) NOT VALID;
ALTER TABLE hos_clocks DROP CONSTRAINT IF EXISTS ck_stage65_hos_clock_nonnegative;
ALTER TABLE hos_clocks ADD CONSTRAINT ck_stage65_hos_clock_nonnegative
  CHECK (drive_time_remaining_minutes>=0 AND shift_time_remaining_minutes>=0 AND cycle_time_remaining_minutes>=0) NOT VALID;
ALTER TABLE dvir_reports DROP CONSTRAINT IF EXISTS ck_stage65_dvir_defects_nonnegative;
ALTER TABLE dvir_reports ADD CONSTRAINT ck_stage65_dvir_defects_nonnegative CHECK (defects_found>=0) NOT VALID;
ALTER TABLE coaching_tasks DROP CONSTRAINT IF EXISTS ck_stage65_coaching_row_version;
ALTER TABLE coaching_tasks ADD CONSTRAINT ck_stage65_coaching_row_version CHECK (row_version>=0) NOT VALID;
ALTER TABLE safety_events DROP CONSTRAINT IF EXISTS ck_stage65_safety_event_row_version;
ALTER TABLE safety_events ADD CONSTRAINT ck_stage65_safety_event_row_version CHECK (row_version>=0) NOT VALID;
ALTER TABLE coaching_tasks DROP CONSTRAINT IF EXISTS ck_stage65_coaching_effectiveness;
ALTER TABLE coaching_tasks ADD CONSTRAINT ck_stage65_coaching_effectiveness CHECK (effectiveness_score IS NULL OR effectiveness_score BETWEEN 0 AND 100) NOT VALID;
ALTER TABLE coaching_tasks DROP CONSTRAINT IF EXISTS ck_stage65_coaching_status;
UPDATE coaching_tasks SET status=CASE LOWER(BTRIM(status))
  WHEN 'open' THEN 'Draft'
  WHEN 'in progress' THEN 'Assigned'
  WHEN 'dismissed' THEN 'Cancelled'
  ELSE status END
WHERE LOWER(BTRIM(status)) IN ('open','in progress','dismissed');
ALTER TABLE coaching_tasks ADD CONSTRAINT ck_stage65_coaching_status
  CHECK (status IN ('Draft','Assigned','Driver Acknowledged','Completed','Escalated','Cancelled')) NOT VALID;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_01_stage65_safety_pilot','Safety module branch isolation, concurrency, idempotency and integrity contract')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
