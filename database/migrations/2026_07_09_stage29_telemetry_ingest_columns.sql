-- Stage 29 — Telemetry ingest: columns the ingest/provision handlers require
--
-- Driving a real device ping through POST /api/telemetry/ingest and
-- POST /api/telemetry/devices/provision surfaced columns the handlers write but that
-- were never created on some databases (the app runs as the restricted opstrax_app
-- role and SKIPS startup schema init under RLS — see stage28's note). Without these,
-- native device ingest 500s on every ping.
--
-- MUST be applied by the DB OWNER, not the app role. Idempotent; safe to re-run.

-- Device provisioning writes an optional notes field.
ALTER TABLE eld_devices        ADD COLUMN IF NOT EXISTS notes           TEXT NULL;

-- Ingest writes battery voltage to both the breadcrumb log and the live snapshot.
ALTER TABLE location_events     ADD COLUMN IF NOT EXISTS battery_voltage DECIMAL(6,2) NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS battery_voltage DECIMAL(6,2) NULL;

-- The AI-foundation insight written alongside a telemetry-derived safety_event.
ALTER TABLE safety_events       ADD COLUMN IF NOT EXISTS system_insight  TEXT NULL;
