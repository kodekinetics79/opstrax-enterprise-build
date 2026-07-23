-- Stage 33 — Durable cross-instance replay defense for gps-ingest (TEL-P1-REPLAY-005)
--
-- POST /api/telemetry/gps-ingest authenticates the trusted-gateway forward with
-- HMAC-SHA256 over "<timestamp>.<rawPayload>" under Telemetry:GatewaySecret. That
-- signature is a per-message identity: the SAME signed request replayed produces the
-- SAME signature. Until now replay was blocked only by a process-local in-memory cache
-- that reset on restart and was not shared across instances. This table makes the check
-- durable and atomic (UNIQUE + INSERT ... ON CONFLICT DO NOTHING), so a captured valid
-- packet cannot be re-accepted after a restart, on another instance, on retry, or under
-- concurrent submission.
--
-- Scoping: (gateway_id, signature). One global gateway secret exists today, so gateway_id
-- is a constant ('default'); the composite key lets a real per-gateway identity slot in
-- later and treats the same signature from a different gateway as distinct.
--
-- NOT tenant-scoped and NO RLS: this is an infrastructure ledger written before/independent
-- of tenant context, exactly like telemetry_nonces and telemetry_replay_seen. device_id /
-- company_id are recorded only for audit/forensic scoping (nullable).
--
-- MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app role and skips
-- startup schema init under RLS). Idempotent; safe to re-run. Also created idempotently by
-- TelemetrySchemaService.EnsureAsync in owner-capable environments.

BEGIN;

CREATE TABLE IF NOT EXISTS gps_gateway_replay (
    id          BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    gateway_id  VARCHAR(120) NOT NULL DEFAULT 'default',  -- future: per-gateway credential id
    signature   VARCHAR(256) NOT NULL,                    -- hex HMAC = per-message identity (nonce)
    signed_at   TIMESTAMPTZ  NOT NULL,                    -- X-Gateway-Timestamp of the request
    device_id   BIGINT       NULL,                        -- resolved device (audit scoping)
    company_id  BIGINT       NULL,                        -- resolved tenant (audit scoping)
    received_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()        -- server receive time (pruning key)
);

-- Atomic uniqueness: this is the replay defense. Concurrent duplicates -> exactly one INSERT wins.
ALTER TABLE gps_gateway_replay DROP CONSTRAINT IF EXISTS uq_gps_gateway_replay;
ALTER TABLE gps_gateway_replay
    ADD CONSTRAINT uq_gps_gateway_replay UNIQUE (gateway_id, signature);

-- Pruning key (retention sweep by received_at).
CREATE INDEX IF NOT EXISTS idx_ggr_received ON gps_gateway_replay (received_at);

-- Least-privilege grants to the restricted app role. Append + read + prune; no UPDATE
-- (a reservation row is immutable — only a retention DELETE removes it).
DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, DELETE ON gps_gateway_replay TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE gps_gateway_replay_id_seq TO opstrax_app;
    END IF;
END
$grant$;

INSERT INTO schema_migrations (version, description)
VALUES ('stage33_gps_gateway_replay',
        'gps_gateway_replay (gateway_id, signature) UNIQUE — durable cross-instance gps-ingest replay defense')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- RETENTION (bounded; applied by TelemetryBackgroundService on a timer)
--   24 h is far beyond the 300 s gateway freshness window, so pruning can never
--   reopen the replay window — a >300 s-old signed message is already rejected by
--   the freshness gate before it reaches the guard:
--     DELETE FROM gps_gateway_replay WHERE received_at < NOW() - INTERVAL '24 hours';
-- ============================================================================

-- ============================================================================
-- ROLLBACK (manual; run as DB OWNER — NOT auto-applied)
--   BEGIN;
--     DROP TABLE IF EXISTS gps_gateway_replay;  -- drops its unique constraint, index, sequence
--     DELETE FROM schema_migrations WHERE version = 'stage33_gps_gateway_replay';
--   COMMIT;
-- ============================================================================
