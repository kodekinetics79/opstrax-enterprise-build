-- Stage 117 — firmware campaign planning with immutable exact-device snapshots.
--
-- This control plane deliberately cannot dispatch OTA commands. Every campaign and
-- target remains ExternalHold, provider capability remains Unverified, and the
-- remote-upgrade claim is fixed FALSE until a later provider/device evidence gate.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_eld_devices_company_id_id
  ON eld_devices(company_id,id);

CREATE TABLE IF NOT EXISTS device_firmware_campaigns (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  campaign_name VARCHAR(160) NOT NULL,
  target_firmware_version VARCHAR(120) NOT NULL,
  rollback_firmware_version VARCHAR(120) NULL,
  rollout_strategy VARCHAR(20) NOT NULL,
  batch_size INT NOT NULL,
  scheduled_for TIMESTAMPTZ NOT NULL,
  maintenance_window_minutes INT NOT NULL,
  execution_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
  provider_capability_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
  remote_upgrade_claim BOOLEAN NOT NULL DEFAULT FALSE,
  external_hold_reason VARCHAR(500) NOT NULL,
  source_reference VARCHAR(240) NOT NULL,
  change_reason VARCHAR(500) NOT NULL,
  idempotency_key UUID NOT NULL,
  created_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT uq_stage117_campaign_company_id_id UNIQUE(company_id,id),
  CONSTRAINT uq_stage117_campaign_company_id_target UNIQUE(company_id,id,target_firmware_version),
  CONSTRAINT uq_stage117_campaign_idempotency UNIQUE(company_id,idempotency_key),
  CONSTRAINT ck_stage117_campaign_name CHECK (BTRIM(campaign_name)<>''),
  CONSTRAINT ck_stage117_target_version CHECK (target_firmware_version ~ '^[A-Za-z0-9][A-Za-z0-9._+\-]{0,119}$'),
  CONSTRAINT ck_stage117_rollback_version CHECK (
    rollback_firmware_version IS NULL OR (
      rollback_firmware_version ~ '^[A-Za-z0-9][A-Za-z0-9._+\-]{0,119}$'
      AND LOWER(rollback_firmware_version)<>LOWER(target_firmware_version))),
  CONSTRAINT ck_stage117_rollout_strategy CHECK (rollout_strategy IN ('Manual','Canary','Staged')),
  CONSTRAINT ck_stage117_batch_size CHECK (batch_size BETWEEN 1 AND 100),
  CONSTRAINT ck_stage117_window CHECK (maintenance_window_minutes BETWEEN 15 AND 720),
  CONSTRAINT ck_stage117_execution_hold CHECK (execution_status='ExternalHold'),
  CONSTRAINT ck_stage117_capability_unverified CHECK (provider_capability_status='Unverified'),
  CONSTRAINT ck_stage117_no_remote_claim CHECK (remote_upgrade_claim=FALSE),
  CONSTRAINT ck_stage117_hold_reason CHECK (BTRIM(external_hold_reason)<>''),
  CONSTRAINT ck_stage117_source CHECK (BTRIM(source_reference)<>''),
  CONSTRAINT ck_stage117_reason CHECK (BTRIM(change_reason)<>'')
);

CREATE TABLE IF NOT EXISTS device_firmware_campaign_targets (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  campaign_id BIGINT NOT NULL,
  device_id BIGINT NOT NULL,
  device_serial VARCHAR(120) NOT NULL,
  manufacturer VARCHAR(120) NULL,
  device_model VARCHAR(160) NULL,
  hardware_revision VARCHAR(120) NULL,
  reported_firmware_version VARCHAR(120) NULL,
  target_firmware_version VARCHAR(120) NOT NULL,
  planning_status VARCHAR(32) NOT NULL,
  planning_reason VARCHAR(500) NOT NULL,
  rollout_batch INT NOT NULL,
  delivery_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
  provider_capability_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
  remote_upgrade_claim BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_stage117_target_campaign
    FOREIGN KEY(company_id,campaign_id,target_firmware_version)
    REFERENCES device_firmware_campaigns(company_id,id,target_firmware_version),
  CONSTRAINT fk_stage117_target_device
    FOREIGN KEY(company_id,device_id) REFERENCES eld_devices(company_id,id),
  CONSTRAINT uq_stage117_target_campaign_device UNIQUE(company_id,campaign_id,device_id),
  CONSTRAINT ck_stage117_target_serial CHECK (BTRIM(device_serial)<>''),
  CONSTRAINT ck_stage117_target_version CHECK (target_firmware_version ~ '^[A-Za-z0-9][A-Za-z0-9._+\-]{0,119}$'),
  CONSTRAINT ck_stage117_target_planning CHECK (planning_status IN ('ReadyForExternalEvidence','BlockedIdentity','AlreadyCurrent')),
  CONSTRAINT ck_stage117_target_reason CHECK (BTRIM(planning_reason)<>''),
  CONSTRAINT ck_stage117_target_batch CHECK (rollout_batch>=1),
  CONSTRAINT ck_stage117_target_delivery_hold CHECK (delivery_status='ExternalHold'),
  CONSTRAINT ck_stage117_target_capability_unverified CHECK (provider_capability_status='Unverified'),
  CONSTRAINT ck_stage117_target_no_remote_claim CHECK (remote_upgrade_claim=FALSE)
);

CREATE INDEX IF NOT EXISTS ix_stage117_campaigns_company_schedule
  ON device_firmware_campaigns(company_id,scheduled_for DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage117_targets_device_recent
  ON device_firmware_campaign_targets(company_id,device_id,created_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_stage117_targets_campaign_batch
  ON device_firmware_campaign_targets(company_id,campaign_id,rollout_batch,id);

CREATE OR REPLACE FUNCTION stage117_protect_firmware_campaign_history()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  RAISE EXCEPTION 'Firmware campaign planning history is immutable'
    USING ERRCODE='23514', CONSTRAINT='ck_stage117_firmware_history_immutable';
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage117_protect_firmware_campaign ON device_firmware_campaigns;
CREATE TRIGGER trg_stage117_protect_firmware_campaign
BEFORE UPDATE OR DELETE ON device_firmware_campaigns
FOR EACH ROW EXECUTE FUNCTION stage117_protect_firmware_campaign_history();

DROP TRIGGER IF EXISTS trg_stage117_protect_firmware_target ON device_firmware_campaign_targets;
CREATE TRIGGER trg_stage117_protect_firmware_target
BEFORE UPDATE OR DELETE ON device_firmware_campaign_targets
FOR EACH ROW EXECUTE FUNCTION stage117_protect_firmware_campaign_history();

ALTER TABLE device_firmware_campaigns ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_firmware_campaigns FORCE ROW LEVEL SECURITY;
ALTER TABLE device_firmware_campaign_targets ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_firmware_campaign_targets FORCE ROW LEVEL SECURITY;

DO $stage117_security$
DECLARE t TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['device_firmware_campaigns','device_firmware_campaign_targets'] LOOP
    EXECUTE format('DROP POLICY IF EXISTS tenant_ticket_app ON %I',t);
    EXECUTE format('DROP POLICY IF EXISTS system_control_plane ON %I',t);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
       AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
      EXECUTE format(
        'CREATE POLICY tenant_ticket_app ON %I FOR SELECT TO opstrax_app USING (company_id=(SELECT opstrax_security.current_tenant_id()))',t);
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
    REVOKE ALL ON SEQUENCE device_firmware_campaigns_id_seq,device_firmware_campaign_targets_id_seq FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT USAGE,SELECT ON SEQUENCE device_firmware_campaigns_id_seq,device_firmware_campaign_targets_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage117_protect_firmware_campaign_history() TO opstrax_system;
  END IF;
END
$stage117_security$;

REVOKE ALL ON SEQUENCE device_firmware_campaigns_id_seq,device_firmware_campaign_targets_id_seq FROM PUBLIC;
REVOKE ALL ON FUNCTION stage117_protect_firmware_campaign_history() FROM PUBLIC;

COMMENT ON TABLE device_firmware_campaigns IS
  'Immutable firmware rollout planning only. ExternalHold is mandatory; no OTA dispatch or success claim.';
COMMENT ON TABLE device_firmware_campaign_targets IS
  'Exact device identity snapshots for firmware planning. Delivery and provider capability remain unverified.';

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage117_device_firmware_campaign_planning',
        'Immutable firmware campaign planning fixed at ExternalHold with no remote-upgrade claim')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
