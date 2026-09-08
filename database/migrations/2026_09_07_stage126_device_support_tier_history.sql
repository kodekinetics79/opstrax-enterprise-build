-- Stage 126 — exact-device support-tier routing history.
--
-- These are operator-recorded service-routing targets. They do not prove a
-- commercial entitlement, provider support, hardware supportability, or certification.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage126_users_company_id_id
  ON users(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage126_eld_devices_company_id_id
  ON eld_devices(company_id,id);

CREATE TABLE IF NOT EXISTS device_support_tier_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  device_serial_snapshot VARCHAR(120) NOT NULL,
  action_type VARCHAR(24) NOT NULL,
  state_after VARCHAR(24) NOT NULL,
  tier_code VARCHAR(32) NOT NULL,
  coverage_window VARCHAR(32) NOT NULL,
  routing_response_target_minutes INT NOT NULL,
  escalation_policy_reference VARCHAR(240) NOT NULL,
  commercial_reference VARCHAR(240) NOT NULL,
  action_reason VARCHAR(500) NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  effective_at TIMESTAMPTZ NOT NULL,
  idempotency_key UUID NOT NULL,
  record_status VARCHAR(40) NOT NULL DEFAULT 'OperatorRecordedUnverified',
  commercial_entitlement_verified_claim BOOLEAN NOT NULL DEFAULT FALSE,
  provider_support_claim BOOLEAN NOT NULL DEFAULT FALSE,
  hardware_supportability_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  recorded_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage126_support_device FOREIGN KEY(company_id,device_id)
    REFERENCES eld_devices(company_id,id),
  CONSTRAINT fk_stage126_support_actor FOREIGN KEY(company_id,recorded_by)
    REFERENCES users(company_id,id),
  CONSTRAINT uq_stage126_support_idempotency UNIQUE(company_id,idempotency_key),
  CONSTRAINT ck_stage126_support_serial CHECK (BTRIM(device_serial_snapshot)<>''),
  CONSTRAINT ck_stage126_support_action CHECK (action_type IN ('Assigned','Changed','Ended')),
  CONSTRAINT ck_stage126_support_state CHECK (
    (action_type IN ('Assigned','Changed') AND state_after='Assigned') OR
    (action_type='Ended' AND state_after='NotAssigned')),
  CONSTRAINT ck_stage126_support_tier CHECK (tier_code IN ('Standard','Priority','CriticalOps','Custom')),
  CONSTRAINT ck_stage126_support_coverage CHECK (coverage_window IN ('BusinessHours','ExtendedHours','AlwaysOn','Custom')),
  CONSTRAINT ck_stage126_support_target CHECK (routing_response_target_minutes BETWEEN 15 AND 10080),
  CONSTRAINT ck_stage126_support_escalation CHECK (LENGTH(BTRIM(escalation_policy_reference)) BETWEEN 3 AND 240),
  CONSTRAINT ck_stage126_support_commercial CHECK (LENGTH(BTRIM(commercial_reference)) BETWEEN 3 AND 240),
  CONSTRAINT ck_stage126_support_reason CHECK (LENGTH(BTRIM(action_reason)) BETWEEN 5 AND 500),
  CONSTRAINT ck_stage126_support_source CHECK (LENGTH(BTRIM(source_reference)) BETWEEN 3 AND 240),
  CONSTRAINT ck_stage126_support_status CHECK (record_status='OperatorRecordedUnverified'),
  CONSTRAINT ck_stage126_support_no_entitlement CHECK (commercial_entitlement_verified_claim=FALSE),
  CONSTRAINT ck_stage126_support_no_provider CHECK (provider_support_claim=FALSE),
  CONSTRAINT ck_stage126_support_no_hardware CHECK (hardware_supportability_claim=FALSE),
  CONSTRAINT ck_stage126_support_no_certification CHECK (certification_claim=FALSE)
);

CREATE INDEX IF NOT EXISTS ix_stage126_support_device_recent
  ON device_support_tier_events(company_id,device_id,effective_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage126_support_tier_recent
  ON device_support_tier_events(company_id,tier_code,state_after,effective_at DESC,id DESC);

CREATE OR REPLACE FUNCTION stage126_guard_device_support_tier_event()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE
  device_row RECORD;
  actor_row RECORD;
  prior_row RECORD;
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Device support-tier events are append-only'
      USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_immutable';
  END IF;
  PERFORM pg_advisory_xact_lock(hashtextextended(
    'device-support-tier:'||NEW.company_id::TEXT||':'||NEW.device_id::TEXT,0));

  SELECT branch_id,device_serial,status,device_state INTO device_row
    FROM eld_devices WHERE company_id=NEW.company_id AND id=NEW.device_id AND deleted_at IS NULL;
  IF NOT FOUND OR device_row.branch_id IS DISTINCT FROM NEW.branch_id
     OR device_row.device_serial IS DISTINCT FROM NEW.device_serial_snapshot THEN
    RAISE EXCEPTION 'Support-tier event does not match the exact device scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_exact_device';
  END IF;
  IF NEW.action_type IN ('Assigned','Changed')
     AND (device_row.status IN ('Revoked','Retired') OR device_row.device_state IN ('Decommissioned','Retired')) THEN
    RAISE EXCEPTION 'Terminal device cannot receive an active support-tier plan'
      USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_device_terminal';
  END IF;
  SELECT branch_id,status INTO actor_row FROM users
   WHERE company_id=NEW.company_id AND id=NEW.recorded_by;
  IF NOT FOUND OR actor_row.status<>'Active'
     OR (NEW.branch_id IS NOT NULL AND actor_row.branch_id IS NOT NULL
         AND actor_row.branch_id IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'Support-tier actor is unavailable or outside the device scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_actor_scope';
  END IF;

  SELECT action_type,state_after,tier_code,coverage_window,routing_response_target_minutes,
         escalation_policy_reference,commercial_reference,effective_at INTO prior_row
    FROM device_support_tier_events
   WHERE company_id=NEW.company_id AND device_id=NEW.device_id
   ORDER BY effective_at DESC,id DESC LIMIT 1;
  IF NOT FOUND THEN
    IF NEW.action_type<>'Assigned' OR NEW.state_after<>'Assigned' THEN
      RAISE EXCEPTION 'First device support-tier event must assign a tier'
        USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_first_event';
    END IF;
  ELSE
    IF NEW.effective_at<prior_row.effective_at THEN
      RAISE EXCEPTION 'Device support-tier events must be chronological'
        USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_chronology';
    END IF;
    IF (prior_row.state_after='Assigned' AND NEW.action_type NOT IN ('Changed','Ended'))
       OR (prior_row.state_after='NotAssigned' AND NEW.action_type<>'Assigned') THEN
      RAISE EXCEPTION 'Device support-tier state transition is invalid'
        USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_transition';
    END IF;
    IF NEW.action_type='Ended' AND (
         NEW.tier_code IS DISTINCT FROM prior_row.tier_code OR
         NEW.coverage_window IS DISTINCT FROM prior_row.coverage_window OR
         NEW.routing_response_target_minutes IS DISTINCT FROM prior_row.routing_response_target_minutes OR
         NEW.escalation_policy_reference IS DISTINCT FROM prior_row.escalation_policy_reference OR
         NEW.commercial_reference IS DISTINCT FROM prior_row.commercial_reference) THEN
      RAISE EXCEPTION 'Ending support coverage must preserve the current exact routing plan'
        USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_end_current';
    END IF;
    IF NEW.action_type='Changed' AND
       ROW(NEW.tier_code,NEW.coverage_window,NEW.routing_response_target_minutes,
           NEW.escalation_policy_reference,NEW.commercial_reference)
       IS NOT DISTINCT FROM
       ROW(prior_row.tier_code,prior_row.coverage_window,prior_row.routing_response_target_minutes,
           prior_row.escalation_policy_reference,prior_row.commercial_reference) THEN
      RAISE EXCEPTION 'Support-tier change must alter the routing plan'
        USING ERRCODE='23514',CONSTRAINT='ck_stage126_support_material_change';
    END IF;
  END IF;
  RETURN NEW;
END
$fn$;

CREATE OR REPLACE FUNCTION stage126_guard_device_support_terminal_transition()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF (NEW.status IN ('Revoked','Retired') OR NEW.device_state IN ('Decommissioned','Retired'))
     AND NOT (OLD.status IN ('Revoked','Retired') OR OLD.device_state IN ('Decommissioned','Retired'))
     AND COALESCE((
       SELECT state_after FROM device_support_tier_events
        WHERE company_id=NEW.company_id AND device_id=NEW.id
        ORDER BY effective_at DESC,id DESC LIMIT 1
     ),'NotAssigned')='Assigned' THEN
    RAISE EXCEPTION 'End the active support-tier plan before terminal lifecycle transition'
      USING ERRCODE='23514',CONSTRAINT='ck_stage126_device_support_tier_active';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage126_guard_device_support_tier_event ON device_support_tier_events;
CREATE TRIGGER trg_stage126_guard_device_support_tier_event
BEFORE INSERT OR UPDATE OR DELETE ON device_support_tier_events
FOR EACH ROW EXECUTE FUNCTION stage126_guard_device_support_tier_event();
DROP TRIGGER IF EXISTS trg_stage126_guard_device_support_terminal ON eld_devices;
CREATE TRIGGER trg_stage126_guard_device_support_terminal
BEFORE UPDATE OF status,device_state ON eld_devices
FOR EACH ROW EXECUTE FUNCTION stage126_guard_device_support_terminal_transition();

ALTER TABLE device_support_tier_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_support_tier_events FORCE ROW LEVEL SECURITY;

DO $stage126_security$
BEGIN
  DROP POLICY IF EXISTS tenant_ticket_app ON device_support_tier_events;
  DROP POLICY IF EXISTS system_control_plane ON device_support_tier_events;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON device_support_tier_events FOR SELECT TO opstrax_app
      USING(company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE device_support_tier_events FROM opstrax_app;
    REVOKE ALL ON SEQUENCE device_support_tier_events_id_seq FROM opstrax_app;
    GRANT SELECT ON TABLE device_support_tier_events TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON device_support_tier_events FOR ALL TO opstrax_system
      USING(TRUE) WITH CHECK(TRUE);
    REVOKE ALL ON TABLE device_support_tier_events FROM opstrax_system;
    GRANT SELECT,INSERT ON TABLE device_support_tier_events TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_support_tier_events_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage126_guard_device_support_tier_event() TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage126_guard_device_support_terminal_transition() TO opstrax_system;
  END IF;
  REVOKE ALL ON TABLE device_support_tier_events FROM PUBLIC;
  REVOKE ALL ON SEQUENCE device_support_tier_events_id_seq FROM PUBLIC;
END
$stage126_security$;

REVOKE ALL ON FUNCTION stage126_guard_device_support_tier_event() FROM PUBLIC;
REVOKE ALL ON FUNCTION stage126_guard_device_support_terminal_transition() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage126_device_support_tier_history',
        'Exact-device support-tier routing history without entitlement provider hardware or certification claims')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
