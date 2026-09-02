-- Stage 96 — connector sync-specific freshness truth.
--
-- operation_last_attempt_at is the scheduler fairness clock for every provider
-- operation, including handshakes.  These columns deliberately separate data-sync
-- attempts and terminal results so the customer UI cannot present a handshake as
-- proof that the telemetry polling worker is healthy.
BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE integrations
  ADD COLUMN IF NOT EXISTS sync_last_attempt_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS sync_last_completed_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS sync_last_ok BOOLEAN NULL,
  ADD COLUMN IF NOT EXISTS provider_last_event_at TIMESTAMPTZ NULL;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_02_stage96_connector_sync_freshness', 'Sync-specific connector attempt and terminal-result clocks')
ON CONFLICT (version) DO NOTHING;
COMMIT;
