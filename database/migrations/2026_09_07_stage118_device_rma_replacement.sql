-- Stage 118 — append-only RMA, custody, warranty-posture and replacement planning.
-- Physical custody, warranty acceptance, swap completion and device readiness remain
-- unverified. The software records operator assertions and exact device snapshots only.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage118_eld_devices_company_id_id
  ON eld_devices(company_id,id);

CREATE TABLE IF NOT EXISTS device_rma_cases (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  device_serial VARCHAR(120) NOT NULL,
  manufacturer VARCHAR(120) NULL,
  device_model VARCHAR(160) NULL,
  hardware_revision VARCHAR(120) NULL,
  reported_firmware_version VARCHAR(120) NULL,
  severity VARCHAR(2) NOT NULL,
  failure_category VARCHAR(40) NOT NULL,
  failure_description VARCHAR(1000) NOT NULL,
  observed_at TIMESTAMPTZ NOT NULL,
  warranty_posture VARCHAR(32) NOT NULL,
  warranty_reference VARCHAR(240) NULL,
  warranty_evidence_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
  support_sla_reference VARCHAR(240) NOT NULL,
  response_due_at TIMESTAMPTZ NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  physical_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
  idempotency_key UUID NOT NULL,
  created_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT uq_stage118_case_company_id UNIQUE(company_id,id),
  CONSTRAINT uq_stage118_case_device_identity UNIQUE(company_id,id,device_id),
  CONSTRAINT uq_stage118_case_idempotency UNIQUE(company_id,idempotency_key),
  CONSTRAINT fk_stage118_case_device FOREIGN KEY(company_id,device_id) REFERENCES eld_devices(company_id,id),
  CONSTRAINT ck_stage118_case_serial CHECK (BTRIM(device_serial)<>''),
  CONSTRAINT ck_stage118_case_severity CHECK (severity IN ('P0','P1','P2','P3')),
  CONSTRAINT ck_stage118_failure_category CHECK (failure_category IN ('Power','Connectivity','GNSS','CAN','Camera','Firmware','PhysicalDamage','Intermittent','Other')),
  CONSTRAINT ck_stage118_failure_description CHECK (LENGTH(BTRIM(failure_description)) BETWEEN 10 AND 1000),
  CONSTRAINT ck_stage118_warranty_posture CHECK (warranty_posture IN ('Unknown','ClaimedInWarranty','ClaimedOutOfWarranty','NotApplicable')),
  CONSTRAINT ck_stage118_warranty_reference CHECK (warranty_posture NOT IN ('ClaimedInWarranty','ClaimedOutOfWarranty') OR warranty_reference IS NOT NULL),
  CONSTRAINT ck_stage118_warranty_unverified CHECK (warranty_evidence_status='Unverified'),
  CONSTRAINT ck_stage118_case_no_physical_claim CHECK (physical_evidence_claim=FALSE),
  CONSTRAINT ck_stage118_response_due CHECK (response_due_at>=observed_at)
);

CREATE TABLE IF NOT EXISTS device_rma_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  case_id BIGINT NOT NULL,
  device_id BIGINT NOT NULL,
  sequence_number INT NOT NULL,
  event_type VARCHAR(40) NOT NULL,
  case_status_after VARCHAR(32) NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL,
  custody_location VARCHAR(240) NULL,
  tracking_reference VARCHAR(240) NULL,
  evidence_reference VARCHAR(240) NOT NULL,
  evidence_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
  notes VARCHAR(1000) NOT NULL,
  physical_completion_claim BOOLEAN NOT NULL DEFAULT FALSE,
  idempotency_key UUID NOT NULL,
  recorded_by BIGINT NULL,
  recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage118_event_case FOREIGN KEY(company_id,case_id,device_id) REFERENCES device_rma_cases(company_id,id,device_id),
  CONSTRAINT uq_stage118_event_sequence UNIQUE(company_id,case_id,sequence_number),
  CONSTRAINT uq_stage118_event_idempotency UNIQUE(company_id,idempotency_key),
  CONSTRAINT ck_stage118_event_sequence CHECK (sequence_number>=1),
  CONSTRAINT ck_stage118_event_type CHECK (event_type IN ('CaseOpened','ReturnAuthorized','Shipped','Received','VendorDisposition','ReplacementLinked','CaseClosed')),
  CONSTRAINT ck_stage118_event_status CHECK (case_status_after IN ('Open','AwaitingReturn','InTransit','UnderReview','ReplacementPlanned','Resolved')),
  CONSTRAINT ck_stage118_event_pair CHECK ((event_type,case_status_after) IN (
    ('CaseOpened','Open'),('ReturnAuthorized','AwaitingReturn'),('Shipped','InTransit'),
    ('Received','UnderReview'),('VendorDisposition','UnderReview'),
    ('ReplacementLinked','ReplacementPlanned'),('CaseClosed','Resolved'))),
  CONSTRAINT ck_stage118_event_evidence_reference CHECK (BTRIM(evidence_reference)<>''),
  CONSTRAINT ck_stage118_event_notes CHECK (BTRIM(notes)<>''),
  CONSTRAINT ck_stage118_event_evidence_unverified CHECK (evidence_status='Unverified'),
  CONSTRAINT ck_stage118_event_no_physical_claim CHECK (physical_completion_claim=FALSE)
);

CREATE TABLE IF NOT EXISTS device_rma_replacements (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  case_id BIGINT NOT NULL,
  failed_device_id BIGINT NOT NULL,
  failed_device_serial VARCHAR(120) NOT NULL,
  replacement_device_id BIGINT NOT NULL,
  replacement_device_serial VARCHAR(120) NOT NULL,
  replacement_manufacturer VARCHAR(120) NULL,
  replacement_device_model VARCHAR(160) NULL,
  replacement_hardware_revision VARCHAR(120) NULL,
  replacement_firmware_version VARCHAR(120) NULL,
  replacement_status VARCHAR(24) NOT NULL DEFAULT 'Planned',
  physical_swap_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
  physical_swap_claim BOOLEAN NOT NULL DEFAULT FALSE,
  change_reason VARCHAR(500) NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  idempotency_key UUID NOT NULL,
  created_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage118_replacement_case FOREIGN KEY(company_id,case_id,failed_device_id) REFERENCES device_rma_cases(company_id,id,device_id),
  CONSTRAINT fk_stage118_replacement_device FOREIGN KEY(company_id,replacement_device_id) REFERENCES eld_devices(company_id,id),
  CONSTRAINT uq_stage118_replacement_case UNIQUE(company_id,case_id),
  CONSTRAINT uq_stage118_replacement_idempotency UNIQUE(company_id,idempotency_key),
  CONSTRAINT ck_stage118_replacement_distinct CHECK (failed_device_id<>replacement_device_id),
  CONSTRAINT ck_stage118_replacement_serials CHECK (BTRIM(failed_device_serial)<>'' AND BTRIM(replacement_device_serial)<>''),
  CONSTRAINT ck_stage118_replacement_status CHECK (replacement_status='Planned'),
  CONSTRAINT ck_stage118_swap_external_hold CHECK (physical_swap_status='ExternalHold'),
  CONSTRAINT ck_stage118_no_swap_claim CHECK (physical_swap_claim=FALSE),
  CONSTRAINT ck_stage118_replacement_reason CHECK (BTRIM(change_reason)<>''),
  CONSTRAINT ck_stage118_replacement_source CHECK (BTRIM(source_reference)<>'')
);

CREATE INDEX IF NOT EXISTS ix_stage118_cases_device_recent ON device_rma_cases(company_id,device_id,created_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage118_cases_severity_due ON device_rma_cases(company_id,severity,response_due_at,id);
CREATE INDEX IF NOT EXISTS ix_stage118_events_case_sequence ON device_rma_events(company_id,case_id,sequence_number);
CREATE INDEX IF NOT EXISTS ix_stage118_replacements_device ON device_rma_replacements(company_id,replacement_device_id,created_at DESC);

CREATE OR REPLACE FUNCTION stage118_protect_rma_history()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  RAISE EXCEPTION 'Device RMA history is immutable'
    USING ERRCODE='23514', CONSTRAINT='ck_stage118_rma_history_immutable';
END
$fn$;

DO $stage118_triggers$
DECLARE t TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['device_rma_cases','device_rma_events','device_rma_replacements'] LOOP
    EXECUTE format('DROP TRIGGER IF EXISTS trg_stage118_protect_history ON %I',t);
    EXECUTE format('CREATE TRIGGER trg_stage118_protect_history BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION stage118_protect_rma_history()',t);
  END LOOP;
END
$stage118_triggers$;

ALTER TABLE device_rma_cases ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_rma_cases FORCE ROW LEVEL SECURITY;
ALTER TABLE device_rma_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_rma_events FORCE ROW LEVEL SECURITY;
ALTER TABLE device_rma_replacements ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_rma_replacements FORCE ROW LEVEL SECURITY;

DO $stage118_security$
DECLARE t TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['device_rma_cases','device_rma_events','device_rma_replacements'] LOOP
    EXECUTE format('DROP POLICY IF EXISTS tenant_ticket_app ON %I',t);
    EXECUTE format('DROP POLICY IF EXISTS system_control_plane ON %I',t);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
       AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
      EXECUTE format('CREATE POLICY tenant_ticket_app ON %I FOR SELECT TO opstrax_app USING (company_id=(SELECT opstrax_security.current_tenant_id()))',t);
      EXECUTE format('REVOKE ALL ON TABLE %I FROM opstrax_app',t);
      EXECUTE format('GRANT SELECT ON TABLE %I TO opstrax_app',t);
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
      EXECUTE format('CREATE POLICY system_control_plane ON %I FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE)',t);
      EXECUTE format('GRANT SELECT,INSERT ON TABLE %I TO opstrax_system',t);
    END IF;
    EXECUTE format('REVOKE ALL ON TABLE %I FROM PUBLIC',t);
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    REVOKE ALL ON SEQUENCE device_rma_cases_id_seq,device_rma_events_id_seq,device_rma_replacements_id_seq FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT USAGE,SELECT ON SEQUENCE device_rma_cases_id_seq,device_rma_events_id_seq,device_rma_replacements_id_seq TO opstrax_system;
  END IF;
END
$stage118_security$;

REVOKE ALL ON SEQUENCE device_rma_cases_id_seq,device_rma_events_id_seq,device_rma_replacements_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage118_protect_rma_history() FROM PUBLIC;

COMMENT ON TABLE device_rma_cases IS 'Operator-recorded device support cases; warranty and physical evidence remain unverified.';
COMMENT ON TABLE device_rma_events IS 'Append-only custody assertions with evidence references; no physical completion claim.';
COMMENT ON TABLE device_rma_replacements IS 'Exact replacement-device plan fixed at ExternalHold until physical installation evidence exists.';

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage118_device_rma_replacement',
        'Append-only RMA custody and replacement planning with unverified physical and warranty evidence')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
