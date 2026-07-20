-- Stage 30 — Reverse-geocoded address cache on live vehicle positions
--
-- Lets the live map label each vehicle with a street address ("106 W 1st St, Los
-- Angeles") instead of raw lat/lng. Addresses are reverse-geocoded server-side via the
-- tenant's Google Maps connector key and CACHED here, refreshed only when a vehicle
-- moves >~55 m (geocoded_lat/lng track where the cached address was resolved) so we
-- don't hit Google — or its billing — on every position tick.
--
-- MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app role and
-- skips startup schema init under RLS). Idempotent; safe to re-run.

ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS address      TEXT NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS geocoded_at  TIMESTAMPTZ NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS geocoded_lat DECIMAL(10,7) NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS geocoded_lng DECIMAL(10,7) NULL;
