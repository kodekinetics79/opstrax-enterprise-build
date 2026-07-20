-- Stage 5 P0-B1A foundation migration
-- Local-only migration artifact for the controlled source slice.
-- This file mirrors the new backend-dotnet foundation schema and can be applied
-- during a local PostgreSQL migration runbook pass.

BEGIN;

CREATE TABLE IF NOT EXISTS authorization_decision_logs (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    actor_type VARCHAR(40) NOT NULL,
    actor_id VARCHAR(120) NULL,
    permission_key VARCHAR(120) NOT NULL,
    resource_type VARCHAR(80) NOT NULL,
    resource_id VARCHAR(120) NULL,
    decision VARCHAR(32) NOT NULL,
    reason TEXT NOT NULL,
    correlation_id VARCHAR(120) NULL,
    request_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS approval_policies (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NULL,
    action_key VARCHAR(120) NOT NULL UNIQUE,
    risk_level VARCHAR(40) NOT NULL,
    requires_approval BOOLEAN NOT NULL DEFAULT TRUE,
    approver_role VARCHAR(80) NULL,
    notes TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS approval_requests (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    requested_by_actor_type VARCHAR(40) NOT NULL,
    requested_by_actor_id VARCHAR(120) NULL,
    action_key VARCHAR(120) NOT NULL,
    resource_type VARCHAR(80) NOT NULL,
    resource_id VARCHAR(120) NULL,
    payload_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    risk_level VARCHAR(40) NOT NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'pending',
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    correlation_id VARCHAR(120) NULL
);

CREATE TABLE IF NOT EXISTS approval_decisions (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    approval_request_id BIGINT NOT NULL,
    tenant_id BIGINT NOT NULL,
    approver_user_id VARCHAR(120) NOT NULL,
    approver_actor_type VARCHAR(40) NULL,
    decision VARCHAR(40) NOT NULL,
    notes TEXT NULL,
    decided_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    correlation_id VARCHAR(120) NULL
);

CREATE TABLE IF NOT EXISTS domain_events (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    event_type VARCHAR(120) NOT NULL,
    aggregate_type VARCHAR(80) NOT NULL,
    aggregate_id VARCHAR(120) NOT NULL,
    payload_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    idempotency_key VARCHAR(160) NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at TIMESTAMPTZ NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS outbox_messages (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    event_type VARCHAR(120) NOT NULL,
    aggregate_type VARCHAR(80) NOT NULL,
    aggregate_id VARCHAR(120) NOT NULL,
    payload_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    idempotency_key VARCHAR(160) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status VARCHAR(40) NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMPTZ NULL,
    processed_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS inbox_messages (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    event_type VARCHAR(120) NOT NULL,
    source VARCHAR(80) NOT NULL,
    external_id VARCHAR(160) NOT NULL,
    payload_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status VARCHAR(40) NOT NULL DEFAULT 'received',
    idempotency_key VARCHAR(160) NULL,
    payload_hash VARCHAR(128) NULL,
    retry_count INT NOT NULL DEFAULT 0,
    processed_at TIMESTAMPTZ NULL,
    UNIQUE (tenant_id, source, external_id)
);

CREATE TABLE IF NOT EXISTS event_processing_logs (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    event_type VARCHAR(120) NOT NULL,
    processor VARCHAR(120) NOT NULL,
    status VARCHAR(40) NOT NULL,
    message TEXT NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    retry_count INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS idempotency_keys (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    operation VARCHAR(120) NOT NULL,
    idempotency_key VARCHAR(160) NOT NULL,
    request_hash VARCHAR(128) NOT NULL,
    response_hash VARCHAR(128) NULL,
    response_reference VARCHAR(200) NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'reserved',
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (tenant_id, operation, idempotency_key)
);

CREATE TABLE IF NOT EXISTS ai_reasoning_runs (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    trigger_type VARCHAR(120) NOT NULL,
    input_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    prompt_template VARCHAR(200) NOT NULL,
    expected_schema_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    status VARCHAR(40) NOT NULL DEFAULT 'started',
    confidence_score NUMERIC(6,3) NULL,
    output_json JSONB NULL,
    error_json JSONB NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS ai_recommendations (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    recommendation_type VARCHAR(120) NOT NULL,
    title VARCHAR(220) NOT NULL,
    summary TEXT NOT NULL,
    confidence_score NUMERIC(6,3) NOT NULL DEFAULT 0,
    urgency_score NUMERIC(6,3) NOT NULL DEFAULT 0,
    impact_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    proposed_action_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    risk_level VARCHAR(40) NOT NULL DEFAULT 'Medium',
    status VARCHAR(40) NOT NULL DEFAULT 'draft',
    source_event_id VARCHAR(120) NULL,
    actor_type VARCHAR(40) NULL,
    actor_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL
);

CREATE TABLE IF NOT EXISTS ai_recommendation_reasons (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    recommendation_id BIGINT NOT NULL,
    reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_recommendation_impacts (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    recommendation_id BIGINT NOT NULL,
    impact_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_action_requests (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    recommendation_id BIGINT NOT NULL,
    action_key VARCHAR(120) NOT NULL,
    resource_type VARCHAR(80) NOT NULL,
    resource_id VARCHAR(120) NULL,
    payload_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    risk_level VARCHAR(40) NOT NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'pending',
    requested_by_actor_type VARCHAR(40) NULL,
    requested_by_actor_id VARCHAR(120) NULL,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL
);

CREATE TABLE IF NOT EXISTS ai_action_outcomes (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id BIGINT NOT NULL,
    action_request_id BIGINT NOT NULL,
    status VARCHAR(40) NOT NULL,
    outcome_json JSONB NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL
);

CREATE INDEX IF NOT EXISTS idx_auth_decision_tenant_created ON authorization_decision_logs (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_approval_requests_tenant_status ON approval_requests (tenant_id, status, requested_at DESC);
CREATE INDEX IF NOT EXISTS idx_approval_decisions_tenant ON approval_decisions (tenant_id, decided_at DESC);
CREATE INDEX IF NOT EXISTS idx_domain_events_tenant_status ON domain_events (tenant_id, status, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_outbox_tenant_status ON outbox_messages (tenant_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_outbox_tenant_next_attempt ON outbox_messages (tenant_id, next_attempt_at);
CREATE INDEX IF NOT EXISTS idx_inbox_tenant_status ON inbox_messages (tenant_id, status, received_at DESC);
CREATE INDEX IF NOT EXISTS idx_inbox_tenant_source_external ON inbox_messages (tenant_id, source, external_id);
CREATE INDEX IF NOT EXISTS idx_idempotency_tenant_operation ON idempotency_keys (tenant_id, operation, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_runs_tenant_status ON ai_reasoning_runs (tenant_id, status, started_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_reco_tenant_status ON ai_recommendations (tenant_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_action_requests_tenant_status ON ai_action_requests (tenant_id, status, requested_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_action_outcomes_tenant_recorded ON ai_action_outcomes (tenant_id, recorded_at DESC);

COMMIT;
