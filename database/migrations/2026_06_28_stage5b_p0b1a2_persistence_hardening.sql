-- Stage 5B P0-B1A2 persistence hardening
-- Additive PostgreSQL migration for durable foundation records.
-- This file is local-only and intentionally non-destructive.

BEGIN;

ALTER TABLE approval_requests
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL;

ALTER TABLE approval_decisions
    ADD COLUMN IF NOT EXISTS tenant_id BIGINT NULL,
    ADD COLUMN IF NOT EXISTS approver_actor_type VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL;

ALTER TABLE domain_events
    ADD COLUMN IF NOT EXISTS retry_count INT NOT NULL DEFAULT 0;

ALTER TABLE outbox_messages
    ADD COLUMN IF NOT EXISTS retry_count INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS next_attempt_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS processed_at TIMESTAMPTZ NULL;

ALTER TABLE inbox_messages
    ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL,
    ADD COLUMN IF NOT EXISTS payload_hash VARCHAR(128) NULL,
    ADD COLUMN IF NOT EXISTS retry_count INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS processed_at TIMESTAMPTZ NULL;

ALTER TABLE event_processing_logs
    ADD COLUMN IF NOT EXISTS retry_count INT NOT NULL DEFAULT 0;

ALTER TABLE ai_recommendations
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL;

ALTER TABLE ai_action_requests
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL;

ALTER TABLE ai_action_outcomes
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL;

CREATE INDEX IF NOT EXISTS idx_approval_decisions_tenant ON approval_decisions (tenant_id, decided_at DESC);
CREATE INDEX IF NOT EXISTS idx_outbox_tenant_next_attempt ON outbox_messages (tenant_id, next_attempt_at);
CREATE INDEX IF NOT EXISTS idx_inbox_tenant_source_external ON inbox_messages (tenant_id, source, external_id);
CREATE INDEX IF NOT EXISTS idx_ai_action_outcomes_tenant_recorded ON ai_action_outcomes (tenant_id, recorded_at DESC);

COMMIT;
