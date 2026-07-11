-- Stage 32 — Device IMEI for hardware GPS-tracker ingest
--
-- Hardware trackers (GT06/Concox/Jimi PT40-class) identify themselves by IMEI, which
-- they include in every location report forwarded to the server. The trusted-gateway ingest
-- (POST /api/telemetry/gps-ingest) authenticates the forwarder with HMAC, then resolves the
-- provisioned eld_devices row by IMEI (or device_serial). IMEI is an identifier, never an
-- authentication credential. Add the column + a globally unique lookup index.
--
-- MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app role and
-- skips startup schema init under RLS). Idempotent; safe to re-run.

BEGIN;

ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS imei VARCHAR(32) NULL;

-- Fail before changing either index when legacy data is ambiguous. Device ownership
-- must be resolved by an operator; a migration must never pick a winning tenant.
DO $$
DECLARE
  duplicate_imeis TEXT;
BEGIN
  SELECT string_agg(quote_literal(imei), ', ' ORDER BY imei)
    INTO duplicate_imeis
  FROM (
    SELECT imei
    FROM eld_devices
    WHERE imei IS NOT NULL
    GROUP BY imei
    HAVING COUNT(*) > 1
    ORDER BY imei
    LIMIT 20
  ) duplicates;

  IF duplicate_imeis IS NOT NULL THEN
    RAISE EXCEPTION 'Cannot enforce global IMEI uniqueness; duplicate values require owner review: %', duplicate_imeis;
  END IF;
END $$;

DROP INDEX IF EXISTS idx_eld_devices_imei;
CREATE UNIQUE INDEX IF NOT EXISTS ux_eld_devices_imei ON eld_devices(imei) WHERE imei IS NOT NULL;

COMMIT;
