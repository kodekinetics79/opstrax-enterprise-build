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
--   telemetry_nonces: a per-device locked high-water unwrap plus UNIQUE
--   (device_id,unwrapped_serial,content_hash) means only the first occurrence wins;
--   concurrent retries receive its stored event_id durably and fleet-wide.
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
--   The guard updates telemetry_replay_device_state under a per-device advisory lock.
--   GT06's 16-bit raw counter is unwrapped into a durable monotonic generation; the
--   nearer half-range advances, the farther half and exact half fail closed behind.
--
-- SAFETY / REVERSIBILITY
--   Idempotent + re-runnable repair. MUST be applied by the DB OWNER.
--   Explicit -- ROLLBACK section at the foot.
-- ============================================================================

BEGIN;

-- ── 1. The durable seen-set ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS telemetry_replay_seen (
    id              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    device_id       TEXT         NOT NULL,          -- resolved device id, else untrusted claim (IMEI)
    serial          BIGINT       NOT NULL,          -- protocol frame serial / sequence number
    unwrapped_serial BIGINT      NOT NULL,          -- durable monotonic generation-aware serial
    content_hash    TEXT         NOT NULL,          -- opaque digest of the frame (e.g. sha256 hex)
    event_id        UUID         NOT NULL,          -- stable only for this unwrapped occurrence/retries
    device_fix_time TIMESTAMPTZ  NULL,              -- device-stamped fix time, when known (audit/context)
    seen_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW()  -- server receive time (audit/query key)
);

-- Repair installations created by the original raw-serial-only migration. Legacy rows are
-- conservatively generation zero; no unreliable chronology is inferred from them.
ALTER TABLE telemetry_replay_seen ADD COLUMN IF NOT EXISTS unwrapped_serial BIGINT NULL;
ALTER TABLE telemetry_replay_seen ADD COLUMN IF NOT EXISTS event_id UUID NULL;
UPDATE telemetry_replay_seen
   SET unwrapped_serial=serial
 WHERE unwrapped_serial IS NULL;
UPDATE telemetry_replay_seen
   SET event_id=md5('opstrax.replay.legacy.v1:'||id::text||':'||device_id||':'||serial::text||':'||content_hash)::uuid
 WHERE event_id IS NULL;
ALTER TABLE telemetry_replay_seen ALTER COLUMN unwrapped_serial SET NOT NULL;
ALTER TABLE telemetry_replay_seen ALTER COLUMN event_id SET NOT NULL;

CREATE TABLE IF NOT EXISTS telemetry_replay_device_state (
    device_id            TEXT        PRIMARY KEY,
    last_raw_serial      BIGINT      NOT NULL,
    high_water_unwrapped BIGINT      NOT NULL,
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
-- Do not infer high-water state from legacy rows. The former raw-key uniqueness could suppress
-- later-generation identical frames, so that history is not a trustworthy chronology. The guard
-- bootstraps each legacy device on its first post-upgrade frame into a fresh epoch above every
-- legacy unwrapped value while holding its advisory lock.

-- The replay guarantee: the first insert of an unwrapped occurrence wins; every retry
-- resolves the same stored event id through ON CONFLICT DO NOTHING.
ALTER TABLE telemetry_replay_seen DROP CONSTRAINT IF EXISTS uq_telemetry_replay_seen_triple;
ALTER TABLE telemetry_replay_seen
    DROP CONSTRAINT IF EXISTS uq_telemetry_replay_seen_unwrapped;
ALTER TABLE telemetry_replay_seen
    ADD CONSTRAINT uq_telemetry_replay_seen_unwrapped
    UNIQUE (device_id, unwrapped_serial, content_hash);

-- Operational generation lookup; authoritative high-water lives in the state table.
DROP INDEX IF EXISTS idx_telemetry_replay_seen_device_serial;
CREATE INDEX idx_telemetry_replay_seen_device_serial
    ON telemetry_replay_seen (device_id, unwrapped_serial DESC);

-- Historical receive-time lookup only. This index is not evidence that an age cutoff is safe;
-- the retention contract below deliberately forbids blanket pruning.
CREATE INDEX IF NOT EXISTS idx_telemetry_replay_seen_seen_at
    ON telemetry_replay_seen (seen_at);

-- ── 2. Least-privilege grants to the separately authenticated system role ────
-- Seen rows are immutable; only the high-water state needs UPDATE. Tenant app
-- identity never reads raw replay identities or opaque frame hashes.
DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        REVOKE ALL PRIVILEGES ON telemetry_replay_seen,telemetry_replay_device_state FROM opstrax_app;
        REVOKE ALL PRIVILEGES ON SEQUENCE telemetry_replay_seen_id_seq FROM opstrax_app;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_system') THEN
        GRANT SELECT,INSERT,DELETE ON telemetry_replay_seen TO opstrax_system;
        GRANT SELECT,INSERT,UPDATE ON telemetry_replay_device_state TO opstrax_system;
        GRANT USAGE,SELECT ON SEQUENCE telemetry_replay_seen_id_seq TO opstrax_system;
    END IF;
END
$grant$;

-- ── 3. Ledger ───────────────────────────────────────────────────────────────
INSERT INTO schema_migrations (version, description)
VALUES ('telematics_005_replay_guard',
        'Durable unwrapped replay generations with stable per-occurrence event identities')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- RETENTION SAFETY
--   No blanket age-based DELETE is safe for this ledger. Removing the occurrence at a
--   device's current high-water mark lets the same raw serial + content hash insert again with
--   a new event_id; removing legacy rows also weakens the fresh-epoch bootstrap for a device
--   that does not yet have durable state. There is intentionally no runtime prune worker.
--
--   Keep every row until a transactional, state-aware retention design exists that preserves
--   all current-high-water occurrence identities and every row for devices without a durable
--   state row. A simple `seen_at` cutoff does not satisfy that contract.
-- ============================================================================

-- ============================================================================
-- ROLLBACK  (manual; run as DB OWNER — NOT auto-applied)
-- ----------------------------------------------------------------------------
-- BEGIN;
--   DROP TABLE IF EXISTS telemetry_replay_seen;   -- drops its unique constraint, indexes, sequence
--   DELETE FROM schema_migrations WHERE version = 'telematics_005_replay_guard';
-- COMMIT;
-- ============================================================================
