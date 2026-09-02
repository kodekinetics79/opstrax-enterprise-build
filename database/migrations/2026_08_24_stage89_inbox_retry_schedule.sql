-- Phase 1 certification recovery: inbox retry scheduling parity.
-- Failure handling has always persisted next_attempt_at; make the owner migration
-- contract and claim query honor that durable backoff timestamp.

BEGIN;

ALTER TABLE inbox_messages
  ADD COLUMN IF NOT EXISTS next_attempt_at TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS idx_inbox_tenant_retry_pending
  ON inbox_messages (tenant_id, status, next_attempt_at);

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_24_stage89_inbox_retry_schedule', 'Inbox retry schedule and claim parity')
ON CONFLICT (version) DO NOTHING;

COMMIT;
