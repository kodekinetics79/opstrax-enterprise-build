-- Telematics 007 — Reboot-safe replay epochs + cross-epoch content replay defence
-- ============================================================================
-- PURPOSE
--   Two additive changes to the durable replay store from telematics/005, both
--   required by the GT06 device edge:
--
--   1. `pending_epoch_base` on telemetry_replay_device_state.
--      A GT06 tracker restarts its 16-bit information serial at 1 every time it
--      powers up. Migration 005's unwrap compares a candidate serial against the
--      device's high-water mark on a circle and treats the FARTHER half as
--      behind — correct for a wrap, wrong for a reboot. A vehicle that ignition-
--      cycles at serial 10000 comes back at serial 1, which is 9999 steps
--      "behind", so every frame it sends until its counter climbs past 10000 is
--      classified OutOfOrder. That is most of a shift of degraded telemetry.
--
--      PostgresReplayGuard.BeginSessionEpochAsync stamps the next counter
--      generation here when a device completes an AUTHENTICATED login. The next
--      frame is then unwrapped as `pending_epoch_base + raw_serial` and the
--      column is cleared. The base is applied directly rather than nudging the
--      high-water mark, because the nearer-half rule still maps a high raw
--      serial backwards; a directly applied base is forward for every serial in
--      [0, modulus).
--
--      Nothing is deleted or reset. high_water_unwrapped only ever increases and
--      telemetry_replay_seen keeps every row it had. The retention contract at
--      the foot of 005 is unchanged and still forbids blanket pruning.
--
--   2. An index on (device_id, content_hash).
--      An epoch boundary gives the same bytes a new unwrapped serial, so the
--      UNIQUE (device_id,unwrapped_serial,content_hash) key from 005 would NOT
--      recognise a frame captured before a power cycle and replayed after one.
--      The guard therefore looks a candidate's digest up per DEVICE, across all
--      epochs, before inserting. This index makes that lookup an index probe
--      rather than a scan of the device's history.
--
--      This is what keeps the epoch mechanism from being a replay hole: the
--      epoch decides ORDER, the content digest decides SEEN, and only the second
--      one is a security boundary.
--
-- SAFETY / REVERSIBILITY
--   Purely additive. Idempotent + re-runnable. MUST be applied by the DB OWNER.
--   Explicit -- ROLLBACK section at the foot.
-- ============================================================================

BEGIN;

-- ── 1. Pending counter generation, stamped at authenticated login ───────────
ALTER TABLE telemetry_replay_device_state
    ADD COLUMN IF NOT EXISTS pending_epoch_base BIGINT NULL;

-- The unwrapped serial below which everything belongs to a PREVIOUS login epoch.
-- Raised only when an authenticated login declares a possible counter reset; a natural
-- counter wrap does not move it, because a wrap is real forward progress and identical
-- bytes after one are a new occurrence rather than a replay.
ALTER TABLE telemetry_replay_device_state
    ADD COLUMN IF NOT EXISTS epoch_floor BIGINT NOT NULL DEFAULT 0;

COMMENT ON COLUMN telemetry_replay_device_state.epoch_floor IS
    'Unwrapped-serial floor of the current login epoch. A content hash already recorded below '
    'this value is a replay from a previous epoch, whatever serial it now claims. 0 = no login '
    'epoch boundary has been crossed yet.';

COMMENT ON COLUMN telemetry_replay_device_state.pending_epoch_base IS
    'Set at authenticated login when the device may legitimately restart its protocol counter. '
    'The next frame unwraps as pending_epoch_base + raw_serial and clears this column. '
    'NULL means no epoch change is outstanding.';

-- ── 2. Cross-epoch replay lookup ────────────────────────────────────────────
-- Not UNIQUE: the same digest may legitimately be recorded once per device, and the
-- guard consults this index BEFORE inserting rather than relying on a constraint.
CREATE INDEX IF NOT EXISTS idx_telemetry_replay_seen_device_content
    ON telemetry_replay_seen (device_id, content_hash);

-- ── 3. Ledger ───────────────────────────────────────────────────────────────
INSERT INTO schema_migrations (version, description)
VALUES ('telematics_007_replay_session_epoch',
        'Reboot-safe login epochs plus cross-epoch content-hash replay defence')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- ROLLBACK  (manual; run as DB OWNER — NOT auto-applied)
-- ----------------------------------------------------------------------------
-- Reverting reinstates the reboot defect: a device that power-cycles will have
-- its post-reboot frames classified OutOfOrder until its counter climbs past the
-- pre-reboot high-water mark.
-- BEGIN;
--   DROP INDEX IF EXISTS idx_telemetry_replay_seen_device_content;
--   ALTER TABLE telemetry_replay_device_state DROP COLUMN IF EXISTS epoch_floor;
--   ALTER TABLE telemetry_replay_device_state DROP COLUMN IF EXISTS pending_epoch_base;
--   DELETE FROM schema_migrations WHERE version = 'telematics_007_replay_session_epoch';
-- COMMIT;
-- ============================================================================
