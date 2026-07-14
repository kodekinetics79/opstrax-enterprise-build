-- Telematics 001 — Provenance & trust metadata on latest_vehicle_positions
-- ============================================================================
-- PURPOSE
--   The live-map snapshot (latest_vehicle_positions) currently records WHERE a
--   vehicle is but not WHERE THAT FIX CAME FROM or HOW MUCH WE TRUST IT. As soon
--   as ingest fans out across multiple sources (native ELD ping, HMAC gateway
--   GT06/Concox forwarder, partner API pull, simulator) the map needs to show
--   and reason about provenance: which adapter normalized the fix, the on-device
--   fix time vs. the gateway receipt time vs. our normalization time, and a
--   confidence / trust score so stale or spoofed fixes can be de-weighted.
--
--   This migration adds that provenance + trust column set and backfills existing
--   rows as provenance 'legacy' (pre-provenance ingest).
--
-- GROUNDING
--   latest_vehicle_positions is defined by TelemetrySchemaService and extended by
--   database/migrations/2026_06_28_stage12a_telemetry_live_state.sql (added
--   correlation_id VARCHAR(120), source_channel, telemetry_status, ...) and
--   stage29 (battery_voltage) / stage30 (address cache). It carries company_id
--   BIGINT NOT NULL, so it is already RLS-enabled + FORCE'd by stage19/stage20 —
--   this migration adds no policies.
--
-- CORRELATION_ID NOTE (schema drift, handled explicitly below)
--   stage12a already added correlation_id as VARCHAR(120). The provenance contract
--   wants a UUID. A bare `ADD COLUMN IF NOT EXISTS correlation_id UUID` would be a
--   SILENT NO-OP against the existing varchar column (misleading). So correlation_id
--   is handled in a guarded DO block: added as UUID only where absent; where the
--   stage12a varchar already exists it is LEFT INTACT (a lossy in-place varchar->uuid
--   cast could fail on non-UUID history and is not cleanly reversible) and a NOTICE
--   documents the drift for the owner.
--
-- SAFETY / REVERSIBILITY
--   ADDITIVE + idempotent + re-runnable. No column is dropped; no row is deleted.
--   MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app
--   role and skips startup schema init under RLS — see stage28/stage29). An
--   explicit -- ROLLBACK section is at the foot of this file.
-- ============================================================================

BEGIN;

-- 1. Provenance + trust columns (non-conflicting names, one idempotent ALTER).
ALTER TABLE latest_vehicle_positions
    ADD COLUMN IF NOT EXISTS source              TEXT          NULL,  -- logical provenance: 'legacy'|'native_eld'|'gateway'|'partner_api'|'simulator'
    ADD COLUMN IF NOT EXISTS provider            TEXT          NULL,  -- upstream provider / OEM (e.g. 'Concox','Samsara','internal')
    ADD COLUMN IF NOT EXISTS protocol            TEXT          NULL,  -- wire protocol (e.g. 'GT06','JT808','rest_json','mqtt')
    ADD COLUMN IF NOT EXISTS adapter_version     TEXT          NULL,  -- normalizer/adapter build that produced this fix
    ADD COLUMN IF NOT EXISTS device_fix_time     TIMESTAMPTZ   NULL,  -- fix timestamp asserted BY THE DEVICE
    ADD COLUMN IF NOT EXISTS gateway_received_at TIMESTAMPTZ   NULL,  -- when the trusted gateway/forwarder received it
    ADD COLUMN IF NOT EXISTS normalized_at       TIMESTAMPTZ   NULL,  -- when our adapter normalized it into the canonical shape
    ADD COLUMN IF NOT EXISTS confidence          NUMERIC(4,3)  NULL,  -- 0.000..1.000 positional confidence (GPS fix quality / HDOP-derived)
    ADD COLUMN IF NOT EXISTS trust_score         NUMERIC(4,3)  NULL,  -- 0.000..1.000 source trust (auth strength, staleness, anti-spoof)
    ADD COLUMN IF NOT EXISTS quality_flags       JSONB         NULL;  -- structured flags: {"stale":true,"jumped":false,"low_hdop":true,...}

-- Bound the two scores to a sane 0..1 range (idempotent: drop-then-add).
ALTER TABLE latest_vehicle_positions DROP CONSTRAINT IF EXISTS ck_lvp_confidence_range;
ALTER TABLE latest_vehicle_positions
    ADD CONSTRAINT ck_lvp_confidence_range
    CHECK (confidence IS NULL OR (confidence >= 0 AND confidence <= 1));

ALTER TABLE latest_vehicle_positions DROP CONSTRAINT IF EXISTS ck_lvp_trust_score_range;
ALTER TABLE latest_vehicle_positions
    ADD CONSTRAINT ck_lvp_trust_score_range
    CHECK (trust_score IS NULL OR (trust_score >= 0 AND trust_score <= 1));

-- 2. correlation_id: add as UUID only if absent; document the stage12a varchar drift.
DO $corr$
DECLARE
    existing_type text;
BEGIN
    SELECT data_type INTO existing_type
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name   = 'latest_vehicle_positions'
      AND column_name  = 'correlation_id';

    IF existing_type IS NULL THEN
        EXECUTE 'ALTER TABLE latest_vehicle_positions ADD COLUMN correlation_id UUID NULL';
    ELSIF existing_type <> 'uuid' THEN
        RAISE NOTICE 'latest_vehicle_positions.correlation_id already exists as % (stage12a); left intact. Convert out-of-band once history is confirmed UUID-shaped: ALTER TABLE latest_vehicle_positions ALTER COLUMN correlation_id TYPE uuid USING correlation_id::uuid;', existing_type;
    END IF;
END
$corr$;

-- 3. Backfill existing rows as pre-provenance 'legacy'. Idempotent via WHERE guard;
--    also seeds the provenance timestamps from the columns we already have so the
--    map has a coherent (device_fix_time <= gateway_received_at <= normalized_at)
--    ordering for legacy fixes instead of NULLs.
UPDATE latest_vehicle_positions
SET source              = 'legacy',
    device_fix_time     = COALESCE(device_fix_time, event_time),
    gateway_received_at = COALESCE(gateway_received_at, received_at),
    normalized_at       = COALESCE(normalized_at, received_at)
WHERE source IS NULL;

-- 4. Provenance lookup index — correlate a live snapshot back to its ingest trail
--    / canonical event (telematics 003 also indexes correlation_id). Partial: only
--    rows that carry a correlation id, tenant-leading so it composes with RLS.
CREATE INDEX IF NOT EXISTS idx_lvp_company_correlation
    ON latest_vehicle_positions (company_id, correlation_id)
    WHERE correlation_id IS NOT NULL;

-- 5. Record in the schema_migrations ledger (stage23).
INSERT INTO schema_migrations (version, description)
VALUES ('telematics_001_latest_position_provenance',
        'Provenance + trust metadata on latest_vehicle_positions; backfill legacy')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- ROLLBACK  (manual; run as DB OWNER — NOT auto-applied)
-- ----------------------------------------------------------------------------
-- BEGIN;
--   DROP INDEX IF EXISTS idx_lvp_company_correlation;
--   ALTER TABLE latest_vehicle_positions DROP CONSTRAINT IF EXISTS ck_lvp_confidence_range;
--   ALTER TABLE latest_vehicle_positions DROP CONSTRAINT IF EXISTS ck_lvp_trust_score_range;
--   ALTER TABLE latest_vehicle_positions
--       DROP COLUMN IF EXISTS source,
--       DROP COLUMN IF EXISTS provider,
--       DROP COLUMN IF EXISTS protocol,
--       DROP COLUMN IF EXISTS adapter_version,
--       DROP COLUMN IF EXISTS device_fix_time,
--       DROP COLUMN IF EXISTS gateway_received_at,
--       DROP COLUMN IF EXISTS normalized_at,
--       DROP COLUMN IF EXISTS confidence,
--       DROP COLUMN IF EXISTS trust_score,
--       DROP COLUMN IF EXISTS quality_flags;
--   -- NOTE: correlation_id is intentionally NOT dropped here. If this migration
--   -- created it (column was absent before), drop it too:
--   --   ALTER TABLE latest_vehicle_positions DROP COLUMN IF EXISTS correlation_id;
--   -- If it pre-existed from stage12a, leave it — stage12a owns it.
--   DELETE FROM schema_migrations WHERE version = 'telematics_001_latest_position_provenance';
-- COMMIT;
-- ============================================================================
