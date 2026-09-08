-- Stage 98: permit explicit unknown GPS speed/heading without inventing zero.
-- Deploy NULL-compatible readers before enabling the new Samsara writer.
-- Retain legacy defaults for existing producers; the new writer explicitly binds
-- NULL. This does not reinterpret historical zeros or change other producers.
-- Rollback retains this additive schema: never backfill unknowns with zero or
-- reapply NOT NULL once real unknown measurements have been written.
BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE location_events
  ALTER COLUMN speed_mph DROP NOT NULL,
  ALTER COLUMN heading DROP NOT NULL;
ALTER TABLE latest_vehicle_positions
  ALTER COLUMN speed_mph DROP NOT NULL,
  ALTER COLUMN heading DROP NOT NULL;
ALTER TABLE telemetry_live_asset_states
  ALTER COLUMN speed_mph DROP NOT NULL,
  ALTER COLUMN heading DROP NOT NULL;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_02_stage98_optional_gps_measurements', 'Explicit unknown GPS speed and heading in history and projections')
ON CONFLICT (version) DO NOTHING;
COMMIT;
