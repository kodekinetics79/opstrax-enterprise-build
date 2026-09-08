-- Stage 124 — append-only RMA support ownership and escalation control.
--
-- These records establish software accountability for a support case. They do
-- not prove that a response occurred, that physical work happened, or that a
-- vendor accepted a warranty claim.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage124_users_company_id
  ON users(company_id,id);

CREATE TABLE IF NOT EXISTS device_rma_support_actions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  case_id BIGINT NOT NULL,
  device_id BIGINT NOT NULL,
  action_type VARCHAR(32) NOT NULL,
  owner_user_id BIGINT NOT NULL,
  owner_name_snapshot VARCHAR(200) NOT NULL,
  support_queue VARCHAR(120) NOT NULL,
  escalation_severity VARCHAR(2) NULL,
  action_reason VARCHAR(500) NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  effective_at TIMESTAMPTZ NOT NULL,
  idempotency_key UUID NOT NULL,
  support_action_status VARCHAR(32) NOT NULL DEFAULT 'OperatorRecorded',
  support_response_claim BOOLEAN NOT NULL DEFAULT FALSE,
  physical_outcome_claim BOOLEAN NOT NULL DEFAULT FALSE,
  warranty_acceptance_claim BOOLEAN NOT NULL DEFAULT FALSE,
  recorded_by BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage124_support_case FOREIGN KEY(company_id,case_id,device_id)
    REFERENCES device_rma_cases(company_id,id,device_id),
  CONSTRAINT fk_stage124_support_owner FOREIGN KEY(company_id,owner_user_id)
    REFERENCES users(company_id,id),
  CONSTRAINT fk_stage124_support_actor FOREIGN KEY(company_id,recorded_by)
    REFERENCES users(company_id,id),
  CONSTRAINT ck_stage124_action_type CHECK
    (action_type IN ('OwnershipClaimed','OwnershipReassigned','Escalated')),
  CONSTRAINT ck_stage124_owner_name CHECK (BTRIM(owner_name_snapshot)<>''),
  CONSTRAINT ck_stage124_support_queue CHECK (LENGTH(BTRIM(support_queue)) BETWEEN 3 AND 120),
  CONSTRAINT ck_stage124_escalation_pair CHECK
    ((action_type='Escalated' AND escalation_severity IN ('P0','P1','P2','P3')) OR
     (action_type IN ('OwnershipClaimed','OwnershipReassigned') AND escalation_severity IS NULL)),
  CONSTRAINT ck_stage124_action_reason CHECK (LENGTH(BTRIM(action_reason)) BETWEEN 5 AND 500),
  CONSTRAINT ck_stage124_source_reference CHECK (LENGTH(BTRIM(source_reference)) BETWEEN 3 AND 240),
  CONSTRAINT ck_stage124_operator_recorded CHECK (support_action_status='OperatorRecorded'),
  CONSTRAINT ck_stage124_no_response_claim CHECK (support_response_claim=FALSE),
  CONSTRAINT ck_stage124_no_physical_claim CHECK (physical_outcome_claim=FALSE),
  CONSTRAINT ck_stage124_no_warranty_claim CHECK (warranty_acceptance_claim=FALSE),
  UNIQUE(company_id,idempotency_key)
);

CREATE INDEX IF NOT EXISTS ix_stage124_support_case_recent
  ON device_rma_support_actions(company_id,case_id,effective_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage124_support_owner_queue
  ON device_rma_support_actions(company_id,owner_user_id,support_queue,effective_at DESC,id DESC);

CREATE OR REPLACE FUNCTION stage124_guard_rma_support_action()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
DECLARE
  case_row RECORD;
  owner_row RECORD;
  actor_row RECORD;
  prior_row RECORD;
  current_case_status TEXT;
BEGIN
  IF TG_OP<>'INSERT' THEN
    RAISE EXCEPTION 'RMA support actions are append-only'
      USING ERRCODE='23514',CONSTRAINT='ck_stage124_support_action_immutable';
  END IF;

  SELECT branch_id,device_id INTO case_row
    FROM device_rma_cases
   WHERE company_id=NEW.company_id AND id=NEW.case_id;
  IF NOT FOUND OR case_row.branch_id IS DISTINCT FROM NEW.branch_id
     OR case_row.device_id IS DISTINCT FROM NEW.device_id THEN
    RAISE EXCEPTION 'RMA support action does not match the exact case scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage124_exact_case_scope';
  END IF;

  SELECT COALESCE((SELECT case_status_after FROM device_rma_events
                    WHERE company_id=NEW.company_id AND case_id=NEW.case_id
                    ORDER BY sequence_number DESC LIMIT 1),'Open')
    INTO current_case_status;
  IF current_case_status='Resolved' THEN
    RAISE EXCEPTION 'Resolved RMA case cannot accept support actions'
      USING ERRCODE='23514',CONSTRAINT='ck_stage124_case_open';
  END IF;

  SELECT full_name,branch_id,status INTO owner_row
    FROM users WHERE company_id=NEW.company_id AND id=NEW.owner_user_id;
  IF NOT FOUND OR owner_row.status<>'Active'
     OR owner_row.full_name IS DISTINCT FROM NEW.owner_name_snapshot
     OR (NEW.branch_id IS NOT NULL AND owner_row.branch_id IS NOT NULL
         AND owner_row.branch_id IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'RMA support owner is unavailable or outside the case scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage124_owner_scope';
  END IF;

  SELECT branch_id,status INTO actor_row
    FROM users WHERE company_id=NEW.company_id AND id=NEW.recorded_by;
  IF NOT FOUND OR actor_row.status<>'Active'
     OR (NEW.branch_id IS NOT NULL AND actor_row.branch_id IS NOT NULL
         AND actor_row.branch_id IS DISTINCT FROM NEW.branch_id) THEN
    RAISE EXCEPTION 'RMA support action actor is unavailable or outside the case scope'
      USING ERRCODE='23514',CONSTRAINT='ck_stage124_actor_scope';
  END IF;

  SELECT action_type,owner_user_id,effective_at INTO prior_row
    FROM device_rma_support_actions
   WHERE company_id=NEW.company_id AND case_id=NEW.case_id
   ORDER BY effective_at DESC,id DESC LIMIT 1;

  IF NOT FOUND THEN
    IF NEW.action_type<>'OwnershipClaimed' OR NEW.owner_user_id<>NEW.recorded_by THEN
      RAISE EXCEPTION 'First RMA support action must claim ownership for the actor'
        USING ERRCODE='23514',CONSTRAINT='ck_stage124_first_owner';
    END IF;
  ELSE
    IF NEW.effective_at<prior_row.effective_at THEN
      RAISE EXCEPTION 'RMA support actions must be chronological'
        USING ERRCODE='23514',CONSTRAINT='ck_stage124_support_chronology';
    END IF;
    IF NEW.action_type='OwnershipClaimed'
       OR (NEW.action_type='OwnershipReassigned' AND
           (NEW.owner_user_id=prior_row.owner_user_id OR NEW.owner_user_id<>NEW.recorded_by)) THEN
      RAISE EXCEPTION 'RMA ownership transition is invalid'
        USING ERRCODE='23514',CONSTRAINT='ck_stage124_owner_transition';
    END IF;
    IF NEW.action_type='Escalated' AND NEW.owner_user_id<>prior_row.owner_user_id THEN
      RAISE EXCEPTION 'RMA escalation must preserve the current owner'
        USING ERRCODE='23514',CONSTRAINT='ck_stage124_escalation_owner';
    END IF;
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage124_guard_rma_support_action ON device_rma_support_actions;
CREATE TRIGGER trg_stage124_guard_rma_support_action
BEFORE INSERT OR UPDATE OR DELETE ON device_rma_support_actions
FOR EACH ROW EXECUTE FUNCTION stage124_guard_rma_support_action();

ALTER TABLE device_rma_support_actions ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_rma_support_actions FORCE ROW LEVEL SECURITY;

DO $stage124_security$
BEGIN
  DROP POLICY IF EXISTS tenant_ticket_app ON device_rma_support_actions;
  DROP POLICY IF EXISTS system_control_plane ON device_rma_support_actions;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON device_rma_support_actions
      FOR SELECT TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE device_rma_support_actions FROM opstrax_app;
    GRANT SELECT ON TABLE device_rma_support_actions TO opstrax_app;
    REVOKE ALL ON SEQUENCE device_rma_support_actions_id_seq FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON device_rma_support_actions
      FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    REVOKE ALL ON TABLE device_rma_support_actions FROM opstrax_system;
    GRANT SELECT,INSERT ON TABLE device_rma_support_actions TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_rma_support_actions_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage124_guard_rma_support_action() TO opstrax_system;
  END IF;
END
$stage124_security$;

REVOKE ALL ON TABLE device_rma_support_actions FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_rma_support_actions_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage124_guard_rma_support_action() FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage124_rma_support_ownership',
        'Append-only RMA ownership and escalation actions without support or physical outcome claims')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
