-- Stage 66 — Telematics customer-pilot security, trust, lifecycle and diagnostics contract
-- Additive/idempotent. Apply as database owner before deploying the Stage66 application.
BEGIN;

-- Device credentials are envelope encrypted by the application. Plaintext credentials are
-- deliberately revoked at cutover: silently retaining them would preserve an at-rest secret leak.
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS api_key_hash VARCHAR(64) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_secret VARCHAR(128) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_secret_encrypted TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_previous_secret_encrypted TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_key_version INT NOT NULL DEFAULT 1;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_rotated_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_previous_valid_until TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS credential_revoked_reason TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS device_state TEXT NOT NULL DEFAULT 'Provisioned';
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS health_status VARCHAR(40) NOT NULL DEFAULT 'unknown';
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS health_reason VARCHAR(120) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS recommended_action TEXT NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS first_connected_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS last_heartbeat_at TIMESTAMPTZ NULL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS observed_at TIMESTAMPTZ NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS normalized_at TIMESTAMPTZ NULL;
UPDATE location_events SET observed_at=event_time WHERE observed_at IS NULL;
UPDATE location_events SET normalized_at=COALESCE(received_at,event_time,NOW()) WHERE normalized_at IS NULL;

UPDATE eld_devices
SET status='CredentialRotationRequired',
    device_state='Quarantined',
    credential_revoked_reason=COALESCE(credential_revoked_reason,'stage66_plaintext_secret_cutover'),
    revoked_at=COALESCE(revoked_at,NOW()),
    hmac_secret=NULL,
    updated_at=NOW()
WHERE hmac_secret IS NOT NULL AND hmac_secret_encrypted IS NULL;

ALTER TABLE eld_devices DROP CONSTRAINT IF EXISTS ck_eld_devices_active_credentials;
ALTER TABLE eld_devices DROP CONSTRAINT IF EXISTS ck_stage66_eld_active_credentials;
ALTER TABLE eld_devices ADD CONSTRAINT ck_stage66_eld_active_credentials CHECK (
  LOWER(status) <> 'active' OR (
    api_key_hash ~ '^[0-9a-fA-F]{64}$'
    AND hmac_secret_encrypted IS NOT NULL
    AND length(btrim(hmac_secret_encrypted)) >= 24
    AND hmac_key_version > 0
    AND revoked_at IS NULL
  )
) NOT VALID;
ALTER TABLE eld_devices DROP CONSTRAINT IF EXISTS ck_eld_devices_device_state;
ALTER TABLE eld_devices ADD CONSTRAINT ck_eld_devices_device_state CHECK (device_state IN (
  'Provisioned','Registered','Enrolled','Installed','Verified','Activated','Online','Idle','Offline',
  'Degraded','Maintenance','Suspended','Quarantined','Lost','Faulty','Decommissioning',
  'Decommissioned','Retired'
)) NOT VALID;
ALTER TABLE eld_devices DROP CONSTRAINT IF EXISTS ck_stage66_eld_health_status;
ALTER TABLE eld_devices ADD CONSTRAINT ck_stage66_eld_health_status
  CHECK (health_status IN ('unknown','never_connected','healthy','degraded','offline','faulty','maintenance')) NOT VALID;

-- Complete installation/RMA lifecycle with immutable transition and evidence records.
CREATE TABLE IF NOT EXISTS device_state_transitions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  device_serial VARCHAR(120) NULL,
  from_state VARCHAR(40) NULL,
  to_state VARCHAR(40) NOT NULL,
  reason_code VARCHAR(80) NULL,
  reason TEXT NULL,
  actor_user_id BIGINT NULL,
  actor VARCHAR(160) NULL,
  correlation_id VARCHAR(120) NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE device_state_transitions ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE device_state_transitions ADD COLUMN IF NOT EXISTS reason_code VARCHAR(80) NULL;
ALTER TABLE device_state_transitions ADD COLUMN IF NOT EXISTS actor_user_id BIGINT NULL;
ALTER TABLE device_state_transitions ADD COLUMN IF NOT EXISTS occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE TABLE IF NOT EXISTS device_installations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  installer_user_id BIGINT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Provisioned',
  cable_type VARCHAR(80) NULL,
  compatibility_result VARCHAR(40) NULL,
  vin_verified BOOLEAN NOT NULL DEFAULT FALSE,
  activation_challenge_hash VARCHAR(64) NULL,
  activation_verified_at TIMESTAMPTZ NULL,
  failure_reason TEXT NULL,
  replaced_installation_id BIGINT NULL,
  installed_at TIMESTAMPTZ NULL,
  removed_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
CREATE TABLE IF NOT EXISTS device_installation_evidence (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  installation_id BIGINT NOT NULL,
  evidence_type VARCHAR(60) NOT NULL,
  object_key TEXT NOT NULL,
  sha256 VARCHAR(64) NOT NULL,
  captured_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  captured_by BIGINT NULL
);

-- Explainable health is per channel; no inferred signal masquerades as an observed reading.
CREATE TABLE IF NOT EXISTS device_channel_health (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  channel VARCHAR(40) NOT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'unknown',
  observed_value JSONB NULL,
  source VARCHAR(40) NOT NULL DEFAULT 'device',
  reason_code VARCHAR(80) NULL,
  recommended_action TEXT NULL,
  observed_at TIMESTAMPTZ NULL,
  received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  normalized_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(company_id,device_id,channel)
);
ALTER TABLE device_channel_health DROP CONSTRAINT IF EXISTS ck_stage66_health_channel;
ALTER TABLE device_channel_health ADD CONSTRAINT ck_stage66_health_channel CHECK (channel IN (
  'heartbeat','power','backup_battery','cellular','gnss','ecm','storage','clock','firmware','configuration','sim','cable'
)) NOT VALID;
ALTER TABLE device_channel_health DROP CONSTRAINT IF EXISTS ck_stage66_health_state;
ALTER TABLE device_channel_health ADD CONSTRAINT ck_stage66_health_state
  CHECK (status IN ('unknown','healthy','degraded','offline','faulty','not_supported')) NOT VALID;

-- Provider-capability-aware desired/reported twin and durable remote-command ledger.
CREATE TABLE IF NOT EXISTS telematics_device_commands (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id BIGINT NOT NULL,
  command_type VARCHAR(60) NOT NULL,
  desired_payload JSONB NOT NULL DEFAULT '{}'::jsonb,
  reported_payload JSONB NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'queued',
  idempotency_key VARCHAR(120) NOT NULL,
  correlation_id VARCHAR(120) NULL,
  attempt_count INT NOT NULL DEFAULT 0,
  max_attempts INT NOT NULL DEFAULT 3,
  scheduled_for TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  dispatched_at TIMESTAMPTZ NULL,
  acknowledged_at TIMESTAMPTZ NULL,
  applied_at TIMESTAMPTZ NULL,
  expires_at TIMESTAMPTZ NULL,
  last_error TEXT NULL,
  requested_by BIGINT NULL,
  approved_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  UNIQUE(company_id,idempotency_key)
);
ALTER TABLE telematics_device_commands DROP CONSTRAINT IF EXISTS ck_stage66_command_status;
ALTER TABLE telematics_device_commands ADD CONSTRAINT ck_stage66_command_status CHECK (status IN (
  'queued','approved','dispatched','acknowledged','applied','failed','expired','cancelled','dead_letter'
)) NOT VALID;
ALTER TABLE telematics_device_commands DROP CONSTRAINT IF EXISTS ck_stage66_command_attempts;
ALTER TABLE telematics_device_commands ADD CONSTRAINT ck_stage66_command_attempts
  CHECK (attempt_count>=0 AND max_attempts BETWEEN 1 AND 20 AND attempt_count<=max_attempts) NOT VALID;

-- Precise-location privacy policy is explicit and auditable, not implied by generic map access.
CREATE TABLE IF NOT EXISTS telemetry_privacy_policies (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  policy_name VARCHAR(160) NOT NULL,
  off_duty_mode VARCHAR(30) NOT NULL DEFAULT 'mask',
  precise_location_retention_days INT NOT NULL DEFAULT 90,
  customer_share_ttl_minutes INT NOT NULL DEFAULT 120,
  enabled BOOLEAN NOT NULL DEFAULT TRUE,
  created_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  UNIQUE(company_id,branch_id,policy_name)
);
ALTER TABLE telemetry_privacy_policies DROP CONSTRAINT IF EXISTS ck_stage66_privacy_mode;
ALTER TABLE telemetry_privacy_policies ADD CONSTRAINT ck_stage66_privacy_mode
  CHECK (off_duty_mode IN ('hide','mask','show')) NOT VALID;
ALTER TABLE telemetry_privacy_policies DROP CONSTRAINT IF EXISTS ck_stage66_privacy_retention;
ALTER TABLE telemetry_privacy_policies ADD CONSTRAINT ck_stage66_privacy_retention
  CHECK (precise_location_retention_days BETWEEN 1 AND 730 AND customer_share_ttl_minutes BETWEEN 5 AND 10080) NOT VALID;

-- Gateway store-and-forward is a system-only durable ledger. It is intentionally not
-- exposed to the tenant application role; tenant identity travels inside the signed envelope.
CREATE TABLE IF NOT EXISTS telemetry_store_forward (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  event_id UUID NOT NULL UNIQUE,
  topic VARCHAR(160) NOT NULL,
  partition_key VARCHAR(200) NOT NULL,
  envelope_json JSONB NOT NULL,
  enqueued_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  claimed_at TIMESTAMPTZ NULL,
  claim_token UUID NULL,
  attempts INT NOT NULL DEFAULT 0,
  last_error TEXT NULL
);
ALTER TABLE telemetry_store_forward DROP CONSTRAINT IF EXISTS ck_stage66_store_forward_attempts;
ALTER TABLE telemetry_store_forward ADD CONSTRAINT ck_stage66_store_forward_attempts
  CHECK (attempts BETWEEN 0 AND 100) NOT VALID;
CREATE INDEX IF NOT EXISTS idx_telemetry_store_forward_pending
  ON telemetry_store_forward(enqueued_at,id) WHERE claim_token IS NULL;
CREATE INDEX IF NOT EXISTS idx_telemetry_store_forward_claimed
  ON telemetry_store_forward(claimed_at) WHERE claim_token IS NOT NULL;

-- Pre-request, one-time SSE capability consumption. Only the SHA-256 nonce digest is
-- persisted. The app may INSERT (atomic uniqueness) but cannot enumerate capabilities.
CREATE TABLE IF NOT EXISTS telemetry_stream_ticket_nonces (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  nonce_hash VARCHAR(64) NOT NULL UNIQUE,
  audit_company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  user_id BIGINT NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  issued_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  consumed_at TIMESTAMPTZ NULL
);

-- Reconcile the pre-Stage66 runtime-created ledger. Its nonce_hash primary key is
-- retained, while the additive surrogate id receives an owned sequence so both
-- legacy and fresh installations have the same insert/grant contract.
CREATE SEQUENCE IF NOT EXISTS telemetry_stream_ticket_nonces_id_seq;
DO $stage66_stream_nonce_identity$
DECLARE
  identity_kind "char";
  highest_id BIGINT;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='telemetry_stream_ticket_nonces'
      AND column_name='id'
  ) THEN
    ALTER TABLE telemetry_stream_ticket_nonces ADD COLUMN id BIGINT NULL;
  END IF;

  SELECT a.attidentity INTO identity_kind
  FROM pg_attribute a
  WHERE a.attrelid='public.telemetry_stream_ticket_nonces'::regclass
    AND a.attname='id' AND NOT a.attisdropped;

  -- A fresh Stage66 table already owns its GENERATED ALWAYS identity sequence.
  -- Legacy/plain-bigint variants need the equivalent owned-default contract.
  IF COALESCE(identity_kind,'')='' THEN
    ALTER SEQUENCE telemetry_stream_ticket_nonces_id_seq
      OWNED BY telemetry_stream_ticket_nonces.id;
    ALTER TABLE telemetry_stream_ticket_nonces ALTER COLUMN id
      SET DEFAULT nextval('telemetry_stream_ticket_nonces_id_seq'::regclass);

    SELECT max(id) INTO highest_id FROM telemetry_stream_ticket_nonces;
    PERFORM setval(
      'telemetry_stream_ticket_nonces_id_seq'::regclass,
      GREATEST(COALESCE(highest_id,0),1),
      highest_id IS NOT NULL
    );
    UPDATE telemetry_stream_ticket_nonces
      SET id=nextval('telemetry_stream_ticket_nonces_id_seq'::regclass)
      WHERE id IS NULL;
    SELECT max(id) INTO highest_id FROM telemetry_stream_ticket_nonces;
    PERFORM setval(
      'telemetry_stream_ticket_nonces_id_seq'::regclass,
      GREATEST(COALESCE(highest_id,0),1),
      highest_id IS NOT NULL
    );
  END IF;
END
$stage66_stream_nonce_identity$;
ALTER TABLE telemetry_stream_ticket_nonces ALTER COLUMN id SET NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_telemetry_stream_ticket_nonces_id
  ON telemetry_stream_ticket_nonces(id);
ALTER TABLE telemetry_stream_ticket_nonces ADD COLUMN IF NOT EXISTS issued_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE telemetry_stream_ticket_nonces ALTER COLUMN consumed_at DROP NOT NULL;
ALTER TABLE telemetry_stream_ticket_nonces DROP CONSTRAINT IF EXISTS ck_stage66_stream_nonce_hash;
ALTER TABLE telemetry_stream_ticket_nonces ADD CONSTRAINT ck_stage66_stream_nonce_hash
  CHECK (nonce_hash ~ '^[0-9a-f]{64}$') NOT VALID;
CREATE INDEX IF NOT EXISTS idx_telemetry_stream_ticket_expiry
  ON telemetry_stream_ticket_nonces(expires_at);

CREATE TABLE IF NOT EXISTS telemetry_gateway_rejections (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  event_id UUID NOT NULL UNIQUE,
  correlation_id UUID NULL,
  claimed_identifier_masked TEXT NULL,
  reason TEXT NOT NULL,
  protocol VARCHAR(80) NULL,
  message_type VARCHAR(80) NULL,
  received_at TIMESTAMPTZ NOT NULL,
  raw_frame_bytes INT NOT NULL DEFAULT 0,
  remote_endpoint TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE telemetry_gateway_rejections DROP CONSTRAINT IF EXISTS ck_stage66_rejection_frame_size;
ALTER TABLE telemetry_gateway_rejections ADD CONSTRAINT ck_stage66_rejection_frame_size
  CHECK (raw_frame_bytes BETWEEN 0 AND 10485760) NOT VALID;
CREATE INDEX IF NOT EXISTS idx_telemetry_gateway_rejections_received
  ON telemetry_gateway_rejections(received_at DESC);

-- Diagnostics evidence and lifecycle. The legacy code column remains for compatibility.
CREATE TABLE IF NOT EXISTS fault_codes (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  device_id VARCHAR(120) NOT NULL,
  vehicle_id BIGINT NULL,
  code_type VARCHAR(40) NOT NULL DEFAULT 'OBD',
  code VARCHAR(40) NOT NULL,
  description TEXT NULL,
  severity VARCHAR(40) NOT NULL DEFAULT 'Warning',
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  occurrence_count INT NOT NULL DEFAULT 1,
  status VARCHAR(40) NOT NULL DEFAULT 'active',
  defect_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(device_id,code,status)
);
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS source_event_id VARCHAR(160) NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS controller VARCHAR(120) NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS source_address SMALLINT NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS bus VARCHAR(40) NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS protocol VARCHAR(40) NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS spn INT NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS fmi SMALLINT NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS lamp_status JSONB NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS raw_evidence JSONB NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS observed_at TIMESTAMPTZ NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS received_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS cleared_at TIMESTAMPTZ NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS clear_source VARCHAR(80) NULL;
ALTER TABLE fault_codes ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS vehicle_id BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS driver_id BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS fault_code_id BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS source VARCHAR(40) NOT NULL DEFAULT 'dvir';
UPDATE fault_codes SET protocol=UPPER(COALESCE(NULLIF(protocol,''),NULLIF(code_type,''),'OBD')) WHERE protocol IS NULL OR protocol='';
DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM fault_codes
    GROUP BY company_id,device_id,protocol,code HAVING COUNT(*)>1
  ) THEN
    RAISE EXCEPTION 'Stage66 blocked: duplicate derived fault states require reconciliation against fault_occurrences';
  END IF;
END $$;
ALTER TABLE fault_codes ALTER COLUMN protocol SET NOT NULL;
ALTER TABLE fault_codes DROP CONSTRAINT IF EXISTS fault_codes_device_id_code_status_key;
DROP INDEX IF EXISTS uq_fault_codes_derived_state;
CREATE UNIQUE INDEX IF NOT EXISTS uq_fault_codes_projection
  ON fault_codes(company_id,device_id,protocol,code);

CREATE TABLE IF NOT EXISTS fault_occurrences (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  device_id VARCHAR(120) NOT NULL,
  vehicle_id BIGINT NULL,
  source_event_id VARCHAR(120) NOT NULL,
  observed_at TIMESTAMPTZ NOT NULL,
  received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  controller VARCHAR(120) NULL,
  source_address SMALLINT NULL,
  bus VARCHAR(40) NULL,
  protocol VARCHAR(40) NOT NULL,
  code VARCHAR(40) NOT NULL,
  spn INT NULL,
  fmi SMALLINT NULL,
  occurrence_count INT NOT NULL DEFAULT 1,
  lamp_status JSONB NULL,
  raw_evidence JSONB NULL,
  UNIQUE(company_id,device_id,source_event_id)
);

UPDATE fault_codes f SET branch_id=v.branch_id
FROM vehicles v WHERE f.company_id=v.company_id AND f.vehicle_id=v.id AND f.branch_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_fault_codes_company_device_source_event
  ON fault_codes(company_id,device_id,source_event_id) WHERE source_event_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_location_events_company_idempotency
  ON location_events(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_geofences_company_branch_active
  ON geofences(company_id,branch_id,status);
CREATE INDEX IF NOT EXISTS idx_fault_codes_company_branch_status_recent
  ON fault_codes(company_id,branch_id,status,last_seen_at DESC);
CREATE INDEX IF NOT EXISTS idx_fault_occurrences_company_vehicle_time
  ON fault_occurrences(company_id,vehicle_id,observed_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_dvir_defects_active_fault
  ON dvir_defects(company_id,fault_code_id)
  WHERE fault_code_id IS NOT NULL AND status NOT IN ('resolved','rejected');
CREATE INDEX IF NOT EXISTS idx_device_state_transitions_company_device_recent
  ON device_state_transitions(company_id,device_id,occurred_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS idx_device_installations_company_branch_status
  ON device_installations(company_id,branch_id,status,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_device_channel_health_company_status
  ON device_channel_health(company_id,branch_id,status,received_at DESC);
CREATE INDEX IF NOT EXISTS idx_telematics_commands_dispatch
  ON telematics_device_commands(status,scheduled_for) WHERE status IN ('queued','approved','failed');

-- Canonical role-targeted FORCE-RLS policies. The system identity is reserved for workers.
DO $stage66_rls$
DECLARE t TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY[
    'eld_devices','fault_codes','fault_occurrences','device_state_transitions','device_installations',
    'device_installation_evidence','device_channel_health','telematics_device_commands',
    'telemetry_privacy_policies'
  ] LOOP
    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',t);
    EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',t);
    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I',t);
    EXECUTE format('DROP POLICY IF EXISTS platform_admin_bypass ON %I',t);
    EXECUTE format('DROP POLICY IF EXISTS tenant_ticket_app ON %I',t);
    EXECUTE format('DROP POLICY IF EXISTS system_control_plane ON %I',t);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
       AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
      EXECUTE format('CREATE POLICY tenant_ticket_app ON %I FOR ALL TO opstrax_app USING (company_id=(SELECT opstrax_security.current_tenant_id())) WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()))',t);
      EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON %I TO opstrax_app',t);
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
      EXECUTE format('CREATE POLICY system_control_plane ON %I FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE)',t);
      EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON %I TO opstrax_system',t);
    END IF;
  END LOOP;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    GRANT USAGE,SELECT ON SEQUENCE device_state_transitions_id_seq,device_installations_id_seq,
      device_installation_evidence_id_seq,device_channel_health_id_seq,
      telematics_device_commands_id_seq,telemetry_privacy_policies_id_seq,
      fault_occurrences_id_seq TO opstrax_app;
    REVOKE ALL ON telemetry_stream_ticket_nonces FROM opstrax_app;
    GRANT INSERT ON telemetry_stream_ticket_nonces TO opstrax_app;
    REVOKE ALL ON SEQUENCE telemetry_stream_ticket_nonces_id_seq FROM opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE telemetry_stream_ticket_nonces_id_seq TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT USAGE,SELECT ON SEQUENCE device_state_transitions_id_seq,device_installations_id_seq,
      device_installation_evidence_id_seq,device_channel_health_id_seq,
      telematics_device_commands_id_seq,telemetry_privacy_policies_id_seq,
      fault_occurrences_id_seq TO opstrax_system;
    GRANT SELECT,INSERT,UPDATE,DELETE ON telemetry_store_forward TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE telemetry_store_forward_id_seq TO opstrax_system;
    REVOKE ALL ON telemetry_stream_ticket_nonces FROM opstrax_system;
    GRANT SELECT,INSERT,UPDATE,DELETE ON telemetry_stream_ticket_nonces TO opstrax_system;
    REVOKE ALL ON SEQUENCE telemetry_stream_ticket_nonces_id_seq FROM opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE telemetry_stream_ticket_nonces_id_seq TO opstrax_system;
    GRANT SELECT,INSERT,DELETE ON telemetry_gateway_rejections TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE telemetry_gateway_rejections_id_seq TO opstrax_system;
  END IF;

  -- Infrastructure ledgers do not inherit ambient PUBLIC access. The app role
  -- can atomically issue but cannot enumerate or consume stream capabilities.
  REVOKE ALL ON telemetry_stream_ticket_nonces FROM PUBLIC;
  REVOKE ALL ON SEQUENCE telemetry_stream_ticket_nonces_id_seq FROM PUBLIC;
END
$stage66_rls$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_02_stage66_telematics_pilot','Telematics trust, lifecycle, health, command, privacy and diagnostics pilot contract')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
