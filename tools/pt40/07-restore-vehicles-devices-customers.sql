-- ─────────────────────────────────────────────────────────────────────────────
-- Round 3: the last three 500s — /api/vehicles, /api/telemetry/devices, /api/customers
--
-- These were diagnosed from Render's error log, not guessed. Two distinct causes:
--
--   1. 42703: column i.device_role does not exist      -> /api/vehicles, /api/telemetry/devices
--      Both join device_installations and select the stage80 column set. Round 1 added
--      only effective_from/effective_to to that table; the rest were still missing.
--
--   2. 23502: null value in column "sla_health_score"  -> /api/customers
--      violates not-null constraint
--      GET /api/customers calls CustomerHealthService.RefreshCompanyAsync, which
--      RECOMPUTES AND WRITES on read. PersistAsync binds DBNull when a customer has too
--      little history to score (CustomerHealthService.cs:227), but the legacy column is
--      NOT NULL DEFAULT 95.
--
--      Making these nullable is not a workaround -- it is the code's intent. The read
--      path explicitly branches on NULL:
--          CASE WHEN c.sla_health_score IS NULL
--               THEN 'Not enough delivery history to score'
--      (EndpointMappings.cs:4398-4405). A hard 95 default is precisely the kind of
--      fabricated score we have been removing: it presents "no data" as a healthy
--      customer. stage80:548-552 drops these constraints for exactly this reason, and
--      the statements below are copied from it verbatim.
--
-- SCOPE
--   Additive columns + three DROP NOT NULL. No data rewritten, no row deleted. Dropping
--   NOT NULL is a catalog-only change and cannot fail on existing data.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/07-restore-vehicles-devices-customers.sql
--       Re-run if any statement reports a lock timeout — it converges.
--
-- Idempotent. Safe to re-run.
-- ─────────────────────────────────────────────────────────────────────────────

SET lock_timeout = '3s';

-- ── device_installations: the remaining stage80 columns ────────────────────
-- device_role is the one the log named; the rest are added together so the next
-- request does not simply fail on the following column in the same SELECT list.
-- effective_period is deliberately OMITTED: it is a GENERATED STORED column, which
-- does rewrite the table, and nothing in the failing read paths selects it.
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS device_role              VARCHAR(40)   NOT NULL DEFAULT 'GPS';
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS is_primary               BOOLEAN       NOT NULL DEFAULT TRUE;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS installed_by             BIGINT        NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS removed_by               BIGINT        NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS installation_location    VARCHAR(160)  NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS odometer_at_installation DECIMAL(12,2) NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS commissioning_method     VARCHAR(80)   NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS commissioning_result     VARCHAR(40)   NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS verification_reference   TEXT          NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS assignment_reason        TEXT          NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS removal_reason           TEXT          NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS source                   VARCHAR(40)   NOT NULL DEFAULT 'operator';
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS correlation_id           VARCHAR(120)  NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS idempotency_key          VARCHAR(120)  NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS row_version              INT           NOT NULL DEFAULT 1;

-- ── customers: let an unscorable customer be honestly unscored ─────────────
ALTER TABLE customers ALTER COLUMN sla_health_score          DROP NOT NULL;
ALTER TABLE customers ALTER COLUMN delivery_experience_score DROP NOT NULL;
ALTER TABLE customers ALTER COLUMN risk_score                DROP NOT NULL;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== device_installations columns (expect still_missing = 0) =='
WITH required(c) AS (VALUES
  ('device_role'),('is_primary'),('installed_by'),('removed_by'),
  ('installation_location'),('odometer_at_installation'),('commissioning_method'),
  ('commissioning_result'),('verification_reference'),('assignment_reason'),
  ('removal_reason'),('source'),('correlation_id'),('idempotency_key'),('row_version'))
SELECT count(*) FILTER (WHERE col.column_name IS NULL) AS still_missing,
       count(*)                                        AS expected_total
FROM required r
LEFT JOIN information_schema.columns col
  ON col.table_schema='public' AND col.table_name='device_installations'
 AND col.column_name=r.c;

\echo ''
\echo '== customers health columns (expect all YES = nullable) =='
SELECT column_name, is_nullable
FROM information_schema.columns
WHERE table_schema='public' AND table_name='customers'
  AND column_name IN ('sla_health_score','delivery_experience_score','risk_score')
ORDER BY column_name;

\echo ''
\echo '== location_events lineage (from script 05 — re-run 05 if any are missing) =='
SELECT count(*) FILTER (WHERE col.column_name IS NULL) AS still_missing_from_05
FROM (VALUES ('installation_id'),('assignment_id'),('battery_voltage')) AS r(c)
LEFT JOIN information_schema.columns col
  ON col.table_schema='public' AND col.table_name='location_events' AND col.column_name=r.c;
