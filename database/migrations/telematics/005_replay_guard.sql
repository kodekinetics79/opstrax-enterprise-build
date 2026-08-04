-- Telematics 005 — Durable replay + sequence defense store
-- ============================================================================
-- PURPOSE
--   Back PostgresReplayGuard (telematics/src/Opstrax.Telematics.Gateway/
--   Security/Replay/PostgresReplayGuard.cs) with a durable, SHARED replay/dedup
--   store. The gps-ingest path today defends replay with a PROCESS-LOCAL,
--   non-durable in-memory cache (docs/telematics/security/threat-model.md §1.2
--   and row D2): it forgets its window on restart, is not shared across gateway
--   instances, and can balloon memory under a distinct-nonce flood. This table
--   gives the same atomic guarantee the STRONG path already gets from
--   telemetry_nonces: a UNIQUE constraint means only the first insert of a
--   (device_id, serial, content_hash) triple can win; concurrent/later attempts
--   are rejected via 23505 -> DuplicateReplay, durably and fleet-wide.
--
-- GROUNDING / SCOPE
--   This is an INFRASTRUCTURE/security table keyed by device identity + protocol
--   serial + content hash. It is deliberately NOT tenant-scoped: `device_id` is a
--   free-form identifier (the resolved device id where available, otherwise an
--   untrusted claim such as IMEI) and the gateway writes it under a system scope
--   before ownership is resolved — mirroring how eld_devices and telemetry_nonces
--   are global. It therefore carries NO company_id and is NOT enrolled in RLS.
--   `serial` is the protocol frame serial (GT06's 16-bit info serial, widened to
--   BIGINT). `content_hash` is an opaque digest (e.g. SHA-256 hex) of the frame.
--
-- SEQUENCE SEMANTICS
--   The guard reads MAX(serial) per device as the monotonic high-water mark; a new
--   row whose serial is strictly below that mark is classified OutOfOrder. The raw
--   GT06 counter wraps at 65536 — feed a monotonic ingest sequence (or unwrap the
--   counter) when wrap tolerance is required; the in-memory guard offers a wrap mode.
--
-- SAFETY / REVERSIBILITY
--   ADDITIVE + idempotent + re-runnable. MUST be applied by the DB OWNER.
--   Explicit -- ROLLBACK section at the foot. No existing object is altered.
-- ============================================================================

BEGIN;

-- ── 1. The durable seen-set ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS telemetry_replay_seen (
    id              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    device_id       TEXT         NOT NULL,          -- resolved device id, else untrusted claim (IMEI)
    serial          BIGINT       NOT NULL,          -- protocol frame serial / sequence number
    content_hash    TEXT         NOT NULL,          -- opaque digest of the frame (e.g. sha256 hex)
    device_fix_time TIMESTAMPTZ  NULL,              -- device-stamped fix time, when known (audit/context)
    seen_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW()  -- server receive time (pruning key)
);

-- The replay guarantee: the FIRST insert of a triple wins; every other attempt
-- hits 23505. Named so the guard's ON CONFLICT target is explicit and stable.
ALTER TABLE telemetry_replay_seen DROP CONSTRAINT IF EXISTS uq_telemetry_replay_seen_triple;
ALTER TABLE telemetry_replay_seen
    ADD CONSTRAINT uq_telemetry_replay_seen_triple UNIQUE (device_id, serial, content_hash);

-- High-water lookup: MAX(serial) per device for the out-of-order check.
CREATE INDEX IF NOT EXISTS idx_telemetry_replay_seen_device_serial
    ON telemetry_replay_seen (device_id, serial DESC);

-- Retention / pruning sweep support (drop rows older than the freshness window).
CREATE INDEX IF NOT EXISTS idx_telemetry_replay_seen_seen_at
    ON telemetry_replay_seen (seen_at);

-- ── 2. Least-privilege grants to the restricted app role where it exists ─────
-- Append + read + prune. No UPDATE: a seen row is immutable; only a retention
-- sweep (DELETE by seen_at) removes it.
DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, DELETE ON telemetry_replay_seen TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE telemetry_replay_seen_id_seq TO opstrax_app;
    END IF;
END
$grant$;

-- ── 3. Ledger ───────────────────────────────────────────────────────────────
INSERT INTO schema_migrations (version, description)
VALUES ('telematics_005_replay_guard',
        'telemetry_replay_seen (device_id,serial,content_hash) UNIQUE — durable replay/sequence store')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- RETENTION (operational note — run on a timer, e.g. TelemetryBackgroundService)
--   A bounded window is enough: once a serial is below the device high-water mark
--   its replays are caught as OutOfOrder even without a dedup row. Prune well
--   beyond the ingest freshness window (gps-ingest = ±300 s) to keep the table
--   small without reopening a replay gap:
--     DELETE FROM telemetry_replay_seen WHERE seen_at < NOW() - INTERVAL '24 hours';
-- ============================================================================

-- ============================================================================
-- ROLLBACK  (manual; run as DB OWNER — NOT auto-applied)
-- ----------------------------------------------------------------------------
-- BEGIN;
--   DROP TABLE IF EXISTS telemetry_replay_seen;   -- drops its unique constraint, indexes, sequence
--   DELETE FROM schema_migrations WHERE version = 'telematics_005_replay_guard';
-- COMMIT;
-- ============================================================================
