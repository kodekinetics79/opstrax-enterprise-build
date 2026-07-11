-- Stage 32 — Device IMEI for hardware GPS-tracker ingest
--
-- Hardware trackers (GT06/Concox/Jimi PT40-class) identify themselves by IMEI, which
-- they include in every location report forwarded to the server. The IMEI-keyed ingest
-- (POST /api/telemetry/gps-ingest) resolves the eld_devices row by imei (or device_serial)
-- and lands the fix on the live map — these devices cannot compute OpsTrax's HMAC, so the
-- IMEI on a provisioned device is their identity. Add the column + a tenant-scoped lookup index.
--
-- MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app role and
-- skips startup schema init under RLS). Idempotent; safe to re-run.

ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS imei VARCHAR(32) NULL;
CREATE INDEX IF NOT EXISTS idx_eld_devices_imei ON eld_devices(company_id, imei) WHERE imei IS NOT NULL;
