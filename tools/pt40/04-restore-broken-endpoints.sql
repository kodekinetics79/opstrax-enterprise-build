-- ─────────────────────────────────────────────────────────────────────────────
-- Restore the three API endpoints currently returning HTTP 500 in production
--
-- SYMPTOM
--   "Unable to load fleet data -- The vehicles service did not respond" on Fleet
--   Overview, and an empty Live Map. Verified directly against production with a
--   valid token (this is tenant-independent -- it breaks for every tenant):
--       GET /api/vehicles                  -> 500
--       GET /api/telemetry/positions       -> 500
--       GET /api/customers                 -> 500
--       GET /api/telemetry/live-map-summary -> 200   (reads a different table)
--
-- CAUSE
--   Deployed code is commit 42f5890 (== main) but production's schema predates it.
--   Each of those handlers SELECTs columns that do not exist, so Postgres raises
--   undefined_column and the handler 500s. Nothing is wrong with the data or the
--   code -- the database is simply behind.
--
-- SCOPE
--   Additive only. Every column below is NULLABLE with no default, no backfill, no
--   constraint, no index, no trigger. Adding a nullable column takes only a brief
--   catalog lock in Postgres 11+ -- it does not rewrite these tables (location_events
--   alone holds ~325k rows, and it is not touched here anyway).
--
--   These are lifted verbatim from the unapplied migrations that own them, so a later
--   full run of stage80 / stage30 / the customer-health contract is a no-op on them
--   (every statement there is ADD COLUMN IF NOT EXISTS).
--
--   Deliberately NOT included: stage80's triggers, CHECK constraints, FKs, uniqueness
--   indexes and quarantine backfill. Those change behaviour on live data and belong in
--   a planned migration window, not a demo-eve repair.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/04-restore-broken-endpoints.sql
--       Re-run until the verification block reports still_missing = 0.
--
-- WHY THERE IS NO ENCLOSING TRANSACTION, AND WHY lock_timeout IS SET
--   The first attempt at this script wrapped everything in BEGIN/COMMIT with no lock
--   timeout, and it HUNG. ALTER TABLE needs an ACCESS EXCLUSIVE lock; the API had two
--   sessions sitting 'idle in transaction' holding conflicting locks on vehicles, so
--   the ALTER queued -- and a QUEUED exclusive request blocks every new reader that
--   arrives behind it. That turns a harmless column add into a self-inflicted outage
--   on the busiest table in the app.
--
--   Two changes prevent that:
--     1. lock_timeout -- if the lock is not free within 3s, the statement fails
--        immediately instead of queueing and blocking readers.
--     2. No enclosing transaction -- each ALTER commits on its own, so one blocked
--        statement neither rolls back the ones that succeeded nor holds their locks
--        while waiting. With ADD COLUMN IF NOT EXISTS, re-running simply skips the
--        columns already added.
--
--   So: run it, and if any statement reports "canceling statement due to lock timeout",
--   just run it again. Each pass picks up whatever is still missing. It converges.
--
-- Idempotent. Safe to re-run, and safe to re-run repeatedly.
-- ─────────────────────────────────────────────────────────────────────────────

-- Fail fast rather than queue behind a long-lived lock holder.
SET lock_timeout = '3s';

-- ── vehicles (stage80) -- breaks GET /api/vehicles, i.e. Fleet Overview ────
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS plate_jurisdiction   VARCHAR(80)  NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS vehicle_class        VARCHAR(80)  NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS vin_exception_type   VARCHAR(80)  NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS alternate_identifier VARCHAR(120) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS updated_at           TIMESTAMPTZ  NULL;

-- ── latest_vehicle_positions -- breaks GET /api/telemetry/positions, the Live Map ──
-- address + geocoded_* are the reverse-geocode cache (stage30); installation/assignment/
-- trip are the lineage columns (stage80). The positions handler selects all of them.
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS address         TEXT          NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS geocoded_at     TIMESTAMPTZ   NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS geocoded_lat    DECIMAL(10,7) NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS geocoded_lng    DECIMAL(10,7) NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS installation_id BIGINT        NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS assignment_id   BIGINT        NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS trip_id         BIGINT        NULL;

-- ── customers -- breaks GET /api/customers ─────────────────────────────────
ALTER TABLE customers ADD COLUMN IF NOT EXISTS health_state       VARCHAR(32) NULL;
ALTER TABLE customers ADD COLUMN IF NOT EXISTS health_computed_at TIMESTAMPTZ NULL;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== expect still_missing = 0 =='
WITH required(t,c) AS (VALUES
  ('vehicles','plate_jurisdiction'),('vehicles','vehicle_class'),
  ('vehicles','vin_exception_type'),('vehicles','alternate_identifier'),
  ('vehicles','updated_at'),
  ('latest_vehicle_positions','address'),('latest_vehicle_positions','geocoded_at'),
  ('latest_vehicle_positions','geocoded_lat'),('latest_vehicle_positions','geocoded_lng'),
  ('latest_vehicle_positions','installation_id'),('latest_vehicle_positions','assignment_id'),
  ('latest_vehicle_positions','trip_id'),
  ('customers','health_state'),('customers','health_computed_at'))
SELECT count(*) FILTER (WHERE col.column_name IS NULL) AS still_missing,
       count(*)                                        AS expected_total
FROM required r
LEFT JOIN information_schema.columns col
  ON col.table_schema='public' AND col.table_name=r.t AND col.column_name=r.c;
