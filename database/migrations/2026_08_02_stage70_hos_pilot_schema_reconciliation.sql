-- Stage 70 — HOS pilot legacy-schema reconciliation
--
-- The original base schema predates the pilot write/read contract. CREATE TABLE
-- IF NOT EXISTS in runtime schema services cannot add columns to those existing
-- tables, so restricted production must receive them through an owner migration.
-- All columns are nullable and additive; no historical HOS meaning is invented.

BEGIN;

ALTER TABLE hos_logs
  ADD COLUMN IF NOT EXISTS notes TEXT NULL;

ALTER TABLE hos_clocks
  ADD COLUMN IF NOT EXISTS break_needed_at TIMESTAMPTZ NULL;
ALTER TABLE hos_clocks
  ADD COLUMN IF NOT EXISTS reset_at TIMESTAMPTZ NULL;
ALTER TABLE hos_clocks
  ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

INSERT INTO schema_migrations(version,description)
VALUES (
  '2026_08_02_stage70_hos_pilot_schema_reconciliation',
  'Additive HOS pilot columns for legacy and restricted-production schemas'
)
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
