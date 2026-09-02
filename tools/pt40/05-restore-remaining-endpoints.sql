-- ─────────────────────────────────────────────────────────────────────────────
-- Round 2 of the endpoint repair: location_events + eld_devices
--
-- WHY A SECOND ROUND
--   Script 04 fixed GET /api/telemetry/positions (500 -> 200, the Live Map reads it).
--   Render's error log then named the next failures precisely rather than by guess:
--
--     GET /api/telemetry/breadcrumbs
--       42703: column "assignment_id" does not exist   POSITION: 168
--       -> breadcrumbs read location_events, and 04 only added the lineage columns to
--          latest_vehicle_positions. Same columns, different table. My omission.
--
--     GET /api/telemetry/devices, /api/vehicles, /api/customers -> still 500
--       -> eld_devices is missing the stage80 identity columns the device list selects.
--
-- SCOPE
--   Additive only, same rules as 04: no backfill, no constraint, no index, no trigger,
--   no enclosing transaction, and lock_timeout so a statement fails fast instead of
--   queueing an ACCESS EXCLUSIVE request in front of live readers.
--
--   location_events holds ~325k rows. Every column added to it here is NULLABLE with no
--   default, which in Postgres 11+ is a catalog-only change -- no table rewrite, no scan.
--
--   eld_devices.device_category is NOT NULL DEFAULT 'GPS' -- also catalog-only in PG11+,
--   where a non-volatile default is stored as the attribute's "missing" value rather than
--   written into all 1011 rows. It is copied verbatim from stage80 so a later full run of
--   that migration is a no-op.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/05-restore-remaining-endpoints.sql
--       Re-run until the verification block reports still_missing = 0. If a statement
--       reports "canceling statement due to lock timeout", that is the guard working --
--       just run it again.
--
-- Idempotent. Safe to re-run.
-- ─────────────────────────────────────────────────────────────────────────────

SET lock_timeout = '3s';

-- ── location_events (stage80 lineage) -- breaks GET /api/telemetry/breadcrumbs,
--    i.e. the Live Map's trail replay ────────────────────────────────────────
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS installation_id BIGINT         NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS assignment_id   BIGINT         NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS battery_voltage NUMERIC(10,3)  NULL;

-- ── eld_devices (stage80 identity) -- breaks GET /api/telemetry/devices ─────
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS device_category      VARCHAR(40)  NOT NULL DEFAULT 'GPS';
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS provider_external_id VARCHAR(160) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS manufacturer         VARCHAR(120) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS sim_iccid            VARCHAR(32)  NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS sim_imsi             VARCHAR(32)  NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS commissioned_at      TIMESTAMPTZ  NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS retired_at           TIMESTAMPTZ  NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS notes                TEXT         NULL;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== expect still_missing = 0 =='
WITH required(t,c) AS (VALUES
  ('location_events','installation_id'),('location_events','assignment_id'),
  ('location_events','battery_voltage'),
  ('eld_devices','device_category'),('eld_devices','provider_external_id'),
  ('eld_devices','manufacturer'),('eld_devices','sim_iccid'),('eld_devices','sim_imsi'),
  ('eld_devices','commissioned_at'),('eld_devices','retired_at'),('eld_devices','notes'))
SELECT count(*) FILTER (WHERE col.column_name IS NULL) AS still_missing,
       count(*)                                        AS expected_total
FROM required r
LEFT JOIN information_schema.columns col
  ON col.table_schema='public' AND col.table_name=r.t AND col.column_name=r.c;

\echo ''
\echo '== PT40 still correctly registered after the eld_devices change =='
SELECT id, imei, status, device_state, device_category
FROM eld_devices WHERE id = 1011;
