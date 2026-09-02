-- Stage 95 — connector operation generation and lease barrier.
--
-- Provider calls run outside the request transaction.  A generation-bound lease
-- makes disconnect/configure an execution barrier: stale handshakes and syncs can
-- neither resurrect connector state nor write provider telemetry after invalidation.
BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE integrations
  ADD COLUMN IF NOT EXISTS operation_generation BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS operation_lease_token UUID NULL,
  ADD COLUMN IF NOT EXISTS operation_lease_expires_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS operation_last_attempt_at TIMESTAMPTZ NULL;

ALTER TABLE integrations
  DROP CONSTRAINT IF EXISTS ck_integrations_operation_lease_pair;
ALTER TABLE integrations
  ADD CONSTRAINT ck_integrations_operation_lease_pair
  CHECK ((operation_lease_token IS NULL) = (operation_lease_expires_at IS NULL));

CREATE INDEX IF NOT EXISTS ix_integrations_connector_operation_lease
  ON integrations (status, operation_lease_expires_at)
  WHERE integration_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_integrations_connector_attempt_fairness
  ON integrations (status, operation_last_attempt_at, id)
  WHERE integration_key IS NOT NULL;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_02_stage95_connector_operation_lease', 'Generation-bound connector operation lease and disconnect barrier')
ON CONFLICT (version) DO NOTHING;
COMMIT;
