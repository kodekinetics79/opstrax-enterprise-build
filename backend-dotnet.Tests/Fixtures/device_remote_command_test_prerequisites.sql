ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMPTZ NULL;

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
  UNIQUE(company_id,idempotency_key),
  CONSTRAINT ck_stage66_command_status CHECK (status IN (
    'queued','approved','dispatched','acknowledged','applied','failed','expired','cancelled','dead_letter')),
  CONSTRAINT ck_stage66_command_attempts CHECK (
    attempt_count>=0 AND max_attempts BETWEEN 1 AND 20 AND attempt_count<=max_attempts)
);
