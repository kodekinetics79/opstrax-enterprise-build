-- Stage 5D P0-B1A3 dispatcher runtime hardening
-- Local-only additive migration for the foundation dispatcher loop.

BEGIN;

ALTER TABLE outbox_messages
    ADD COLUMN IF NOT EXISTS claimed_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS claimed_by VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS last_error TEXT NULL,
    ADD COLUMN IF NOT EXISTS dead_letter_reason TEXT NULL;

ALTER TABLE inbox_messages
    ADD COLUMN IF NOT EXISTS claimed_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS claimed_by VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS last_error TEXT NULL,
    ADD COLUMN IF NOT EXISTS dead_letter_reason TEXT NULL;

CREATE INDEX IF NOT EXISTS idx_outbox_tenant_locked_until ON outbox_messages (tenant_id, locked_until);
CREATE INDEX IF NOT EXISTS idx_outbox_tenant_retry_pending ON outbox_messages (tenant_id, status, next_attempt_at);
CREATE INDEX IF NOT EXISTS idx_inbox_tenant_locked_until ON inbox_messages (tenant_id, locked_until);
CREATE UNIQUE INDEX IF NOT EXISTS uq_inbox_tenant_idempotency_key
    ON inbox_messages (tenant_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

COMMIT;
