-- Stage 125 — exact-device spare-pool planning and reservation history.
--
-- Pool membership and reservation are operator-recorded software facts. They do
-- not prove physical possession, condition, compatibility, installation, or certification.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage125_users_company_id_id
  ON users(company_id,id);

CREATE TABLE IF NOT EXISTS device_spare_pool_entries (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  device_serial_snapshot VARCHAR(120) NOT NULL,
  pool_name VARCHAR(120) NOT NULL,
  entry_reason VARCHAR(500) NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  idempotency_key UUID NOT NULL,
  inventory_assurance_status VARCHAR(40) NOT NULL DEFAULT 'OperatorRecordedUnverified',
  physical_possession_claim BOOLEAN NOT NULL DEFAULT FALSE,
  condition_verified_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  added_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT uq_stage125_pool_entry_company_id UNIQUE(company_id,id),
  CONSTRAINT uq_stage125_pool_entry_device UNIQUE(company_id,device_id),
  CONSTRAINT uq_stage125_pool_entry_idempotency UNIQUE(company_id,idempotency_key),
  CONSTRAINT fk_stage125_pool_entry_device FOREIGN KEY(company_id,device_id)
    REFERENCES eld_devices(company_id,id),
  CONSTRAINT fk_stage125_pool_entry_actor FOREIGN KEY(company_id,added_by)
    REFERENCES users(company_id,id),
  CONSTRAINT ck_stage125_pool_entry_serial CHECK (BTRIM(device_serial_snapshot)<>''),
  CONSTRAINT ck_stage125_pool_name CHECK (LENGTH(BTRIM(pool_name)) BETWEEN 3 AND 120),
  CONSTRAINT ck_stage125_pool_entry_reason CHECK (LENGTH(BTRIM(entry_reason)) BETWEEN 5 AND 500),
  CONSTRAINT ck_stage125_pool_entry_source CHECK (LENGTH(BTRIM(source_reference)) BETWEEN 3 AND 240),
  CONSTRAINT ck_stage125_pool_assurance CHECK (inventory_assurance_status='OperatorRecordedUnverified'),
  CONSTRAINT ck_stage125_pool_no_possession CHECK (physical_possession_claim=FALSE),
  CONSTRAINT ck_stage125_pool_no_condition CHECK (condition_verified_claim=FALSE),
  CONSTRAINT ck_stage125_pool_no_certification CHECK (certification_claim=FALSE)
);

CREATE TABLE IF NOT EXISTS device_spare_pool_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  entry_id BIGINT NOT NULL,
  device_id BIGINT NOT NULL,
  action_type VARCHAR(24) NOT NULL,
  state_after VARCHAR(24) NOT NULL,
  rma_case_id BIGINT NULL,
  failed_device_id BIGINT NULL,
  action_reason VARCHAR(500) NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  effective_at TIMESTAMPTZ NOT NULL,
  idempotency_key UUID NOT NULL,
  event_status VARCHAR(32) NOT NULL DEFAULT 'OperatorRecorded',
  physical_possession_claim BOOLEAN NOT NULL DEFAULT FALSE,
  condition_verified_claim BOOLEAN NOT NULL DEFAULT FALSE,
  compatibility_claim BOOLEAN NOT NULL DEFAULT FALSE,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  recorded_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage125_pool_event_entry FOREIGN KEY(company_id,entry_id)
    REFERENCES device_spare_pool_entries(company_id,id),
  CONSTRAINT fk_stage125_pool_event_case FOREIGN KEY(company_id,rma_case_id,failed_device_id)
    REFERENCES device_rma_cases(company_id,id,device_id),
  CONSTRAINT fk_stage125_pool_event_actor FOREIGN KEY(company_id,recorded_by)
    REFERENCES users(company_id,id),
  CONSTRAINT uq_stage125_pool_event_idempotency UNIQUE(company_id,idempotency_key),
  CONSTRAINT ck_stage125_pool_event_pair CHECK ((action_type,state_after) IN (
    ('Added','Available'),('Reserved','Reserved'),('Released','Available'),('Removed','Removed'))),
  CONSTRAINT ck_stage125_pool_event_case_pair CHECK
    ((action_type IN ('Reserved','Released') AND rma_case_id IS NOT NULL AND failed_device_id IS NOT NULL) OR
     (action_type IN ('Added','Removed') AND rma_case_id IS NULL AND failed_device_id IS NULL)),
  CONSTRAINT ck_stage125_pool_event_reason CHECK (LENGTH(BTRIM(action_reason)) BETWEEN 5 AND 500),
  CONSTRAINT ck_stage125_pool_event_source CHECK (LENGTH(BTRIM(source_reference)) BETWEEN 3 AND 240),
  CONSTRAINT ck_stage125_pool_event_status CHECK (event_status='OperatorRecorded'),
  CONSTRAINT ck_stage125_pool_event_no_possession CHECK (physical_possession_claim=FALSE),
  CONSTRAINT ck_stage125_pool_event_no_condition CHECK (condition_verified_claim=FALSE),
  CONSTRAINT ck_stage125_pool_event_no_compatibility CHECK (compatibility_claim=FALSE),
  CONSTRAINT ck_stage125_pool_event_no_certification CHECK (certification_claim=FALSE)
);

CREATE INDEX IF NOT EXISTS ix_stage125_pool_entry_lookup
  ON device_spare_pool_entries(company_id,pool_name,device_serial_snapshot,id);
CREATE INDEX IF NOT EXISTS ix_stage125_pool_event_recent
  ON device_spare_pool_events(company_id,entry_id,effective_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage125_pool_case_reservation
  ON device_spare_pool_events(company_id,rma_case_id,effective_at DESC,id DESC)
  WHERE rma_case_id IS NOT NULL;

CREATE OR REPLACE FUNCTION stage125_guard_spare_pool_entry()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE
  device_row RECORD;
  actor_row RECORD;
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Spare-pool entries are immutable'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_entry_immutable';
  END IF;
  SELECT branch_id,device_serial,status,device_state INTO device_row
    FROM eld_devices WHERE company_id=NEW.company_id AND id=NEW.device_id AND deleted_at IS NULL;
  IF NOT FOUND OR device_row.branch_id IS DISTINCT FROM NEW.branch_id
     OR device_row.device_serial IS DISTINCT FROM NEW.device_serial_snapshot
     OR device_row.status IN ('Revoked','Retired') OR device_row.device_state IN ('Decommissioned','Retired') THEN
    RAISE EXCEPTION 'Spare-pool entry does not match an eligible exact device'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_exact_device';
  END IF;
  IF EXISTS (SELECT 1 FROM device_installations i WHERE i.company_id=NEW.company_id
               AND i.device_id=NEW.device_id AND i.effective_to IS NULL
               AND i.status IN ('Installed','Verified')) THEN
    RAISE EXCEPTION 'Installed device cannot enter the spare-pool plan'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_no_current_installation';
  END IF;
  SELECT branch_id,status INTO actor_row FROM users
   WHERE company_id=NEW.company_id AND id=NEW.added_by;
  IF NOT FOUND OR actor_row.status<>'Active'
     OR (NEW.branch_id IS NOT NULL AND actor_row.branch_id IS NOT NULL
         AND actor_row.branch_id IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'Spare-pool actor is unavailable or outside the device scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_actor_scope';
  END IF;
  RETURN NEW;
END
$fn$;

CREATE OR REPLACE FUNCTION stage125_guard_spare_pool_event()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE
  entry_row RECORD;
  actor_row RECORD;
  prior_row RECORD;
  case_row RECORD;
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'Spare-pool events are append-only'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_event_immutable';
  END IF;
  PERFORM pg_advisory_xact_lock(hashtextextended(
    'device-spare-pool:'||NEW.company_id::TEXT||':'||NEW.entry_id::TEXT,0));
  IF NEW.rma_case_id IS NOT NULL THEN
    PERFORM pg_advisory_xact_lock(hashtextextended(
      'device-spare-pool-case:'||NEW.company_id::TEXT||':'||NEW.rma_case_id::TEXT,0));
  END IF;

  SELECT branch_id,device_id INTO entry_row FROM device_spare_pool_entries
   WHERE company_id=NEW.company_id AND id=NEW.entry_id;
  IF NOT FOUND OR entry_row.branch_id IS DISTINCT FROM NEW.branch_id
     OR entry_row.device_id IS DISTINCT FROM NEW.device_id THEN
    RAISE EXCEPTION 'Spare-pool event does not match the exact entry scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_event_scope';
  END IF;
  SELECT branch_id,status INTO actor_row FROM users
   WHERE company_id=NEW.company_id AND id=NEW.recorded_by;
  IF NOT FOUND OR actor_row.status<>'Active'
     OR (NEW.branch_id IS NOT NULL AND actor_row.branch_id IS NOT NULL
         AND actor_row.branch_id IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'Spare-pool event actor is unavailable or outside the entry scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_event_actor_scope';
  END IF;

  SELECT id,action_type,state_after,rma_case_id,failed_device_id,effective_at INTO prior_row
    FROM device_spare_pool_events WHERE company_id=NEW.company_id AND entry_id=NEW.entry_id
   ORDER BY effective_at DESC,id DESC LIMIT 1;
  IF NOT FOUND THEN
    IF NEW.action_type<>'Added' OR NEW.state_after<>'Available' THEN
      RAISE EXCEPTION 'First spare-pool event must add the exact device'
        USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_first_event';
    END IF;
  ELSE
    IF NEW.effective_at<prior_row.effective_at THEN
      RAISE EXCEPTION 'Spare-pool events must be chronological'
        USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_chronology';
    END IF;
    IF (prior_row.state_after='Available' AND NEW.action_type NOT IN ('Reserved','Removed'))
       OR (prior_row.state_after='Reserved' AND NEW.action_type<>'Released')
       OR prior_row.state_after='Removed' THEN
      RAISE EXCEPTION 'Spare-pool state transition is invalid'
        USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_transition';
    END IF;
    IF NEW.action_type='Released' AND
       (NEW.rma_case_id IS DISTINCT FROM prior_row.rma_case_id OR
        NEW.failed_device_id IS DISTINCT FROM prior_row.failed_device_id) THEN
      RAISE EXCEPTION 'Spare release must close the current exact reservation'
        USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_release_reservation';
    END IF;
  END IF;

  IF NEW.action_type='Reserved' THEN
    SELECT branch_id,device_id,
      COALESCE((SELECT case_status_after FROM device_rma_events e
                 WHERE e.company_id=c.company_id AND e.case_id=c.id
                 ORDER BY sequence_number DESC LIMIT 1),'Open') current_status
      INTO case_row FROM device_rma_cases c
     WHERE c.company_id=NEW.company_id AND c.id=NEW.rma_case_id;
    IF NOT FOUND OR case_row.branch_id IS DISTINCT FROM NEW.branch_id
       OR case_row.device_id IS DISTINCT FROM NEW.failed_device_id
       OR case_row.device_id=NEW.device_id OR case_row.current_status='Resolved' THEN
      RAISE EXCEPTION 'Spare reservation does not match an open in-scope RMA case for another device'
        USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_reservation_case';
    END IF;
    IF EXISTS (
      SELECT 1 FROM (
        SELECT DISTINCT ON (e.entry_id) e.entry_id,e.state_after,e.rma_case_id
          FROM device_spare_pool_events e WHERE e.company_id=NEW.company_id
         ORDER BY e.entry_id,e.effective_at DESC,e.id DESC
      ) latest WHERE latest.state_after='Reserved' AND latest.rma_case_id=NEW.rma_case_id
    ) THEN
      RAISE EXCEPTION 'RMA case already has an active spare reservation'
        USING ERRCODE='23514',CONSTRAINT='ck_stage125_pool_unique_active_case';
    END IF;
  END IF;
  RETURN NEW;
END
$fn$;

CREATE OR REPLACE FUNCTION stage125_guard_device_pool_terminal_transition()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF (NEW.status IN ('Revoked','Retired') OR NEW.device_state IN ('Decommissioned','Retired'))
     AND NOT (OLD.status IN ('Revoked','Retired') OR OLD.device_state IN ('Decommissioned','Retired'))
     AND EXISTS (
       SELECT 1 FROM device_spare_pool_entries entry
       JOIN LATERAL (
         SELECT event.state_after FROM device_spare_pool_events event
          WHERE event.company_id=entry.company_id AND event.entry_id=entry.id
          ORDER BY event.effective_at DESC,event.id DESC LIMIT 1
       ) latest ON TRUE
       WHERE entry.company_id=NEW.company_id AND entry.device_id=NEW.id
         AND latest.state_after<>'Removed'
     ) THEN
    RAISE EXCEPTION 'Remove the device from the spare-pool plan before terminal lifecycle transition'
      USING ERRCODE='23514',CONSTRAINT='ck_stage125_device_not_in_pool';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage125_guard_spare_pool_entry ON device_spare_pool_entries;
CREATE TRIGGER trg_stage125_guard_spare_pool_entry
BEFORE INSERT OR UPDATE OR DELETE ON device_spare_pool_entries
FOR EACH ROW EXECUTE FUNCTION stage125_guard_spare_pool_entry();
DROP TRIGGER IF EXISTS trg_stage125_guard_spare_pool_event ON device_spare_pool_events;
CREATE TRIGGER trg_stage125_guard_spare_pool_event
BEFORE INSERT OR UPDATE OR DELETE ON device_spare_pool_events
FOR EACH ROW EXECUTE FUNCTION stage125_guard_spare_pool_event();
DROP TRIGGER IF EXISTS trg_stage125_guard_device_pool_terminal ON eld_devices;
CREATE TRIGGER trg_stage125_guard_device_pool_terminal
BEFORE UPDATE OF status,device_state ON eld_devices
FOR EACH ROW EXECUTE FUNCTION stage125_guard_device_pool_terminal_transition();

ALTER TABLE device_spare_pool_entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_spare_pool_entries FORCE ROW LEVEL SECURITY;
ALTER TABLE device_spare_pool_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_spare_pool_events FORCE ROW LEVEL SECURITY;

DO $stage125_security$
DECLARE target TEXT;
BEGIN
  FOREACH target IN ARRAY ARRAY['device_spare_pool_entries','device_spare_pool_events'] LOOP
    EXECUTE format('DROP POLICY IF EXISTS tenant_ticket_app ON %I',target);
    EXECUTE format('DROP POLICY IF EXISTS system_control_plane ON %I',target);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
       AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
      EXECUTE format('CREATE POLICY tenant_ticket_app ON %I FOR SELECT TO opstrax_app USING (company_id=(SELECT opstrax_security.current_tenant_id()))',target);
      EXECUTE format('REVOKE ALL ON TABLE %I FROM opstrax_app',target);
      EXECUTE format('GRANT SELECT ON TABLE %I TO opstrax_app',target);
      EXECUTE format('REVOKE ALL ON SEQUENCE %I FROM opstrax_app',target||'_id_seq');
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
      EXECUTE format('CREATE POLICY system_control_plane ON %I FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE)',target);
      EXECUTE format('REVOKE ALL ON TABLE %I FROM opstrax_system',target);
      EXECUTE format('GRANT SELECT,INSERT ON TABLE %I TO opstrax_system',target);
      EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %I TO opstrax_system',target||'_id_seq');
    END IF;
    EXECUTE format('REVOKE ALL ON TABLE %I FROM PUBLIC',target);
    EXECUTE format('REVOKE ALL ON SEQUENCE %I FROM PUBLIC',target||'_id_seq');
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT EXECUTE ON FUNCTION stage125_guard_spare_pool_entry() TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage125_guard_spare_pool_event() TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage125_guard_device_pool_terminal_transition() TO opstrax_system;
  END IF;
END
$stage125_security$;

REVOKE ALL ON FUNCTION stage125_guard_spare_pool_entry() FROM PUBLIC;
REVOKE ALL ON FUNCTION stage125_guard_spare_pool_event() FROM PUBLIC;
REVOKE ALL ON FUNCTION stage125_guard_device_pool_terminal_transition() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage125_device_spare_pool',
        'Exact-device spare-pool planning and RMA reservation without physical or certification claims')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
