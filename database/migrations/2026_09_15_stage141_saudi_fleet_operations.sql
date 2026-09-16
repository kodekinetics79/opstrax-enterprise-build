-- Saudi fleet operations: one canonical roster for owned, rented, and partner units.
-- External-system states are recorded evidence and default to Not configured.
BEGIN;

ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS ownership_model VARCHAR(30) NOT NULL DEFAULT 'Owned';
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS fleet_provider_name VARCHAR(160) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS rental_contract_number VARCHAR(100) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS rental_end_date DATE NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS body_type VARCHAR(80) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS capacity_kg NUMERIC(14,2) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS capacity_cbm NUMERIC(14,2) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS tracking_provider VARCHAR(80) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS external_fleet_id VARCHAR(140) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS tamm_status VARCHAR(40) NOT NULL DEFAULT 'Not configured';
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS tamm_last_verified_at TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS idx_vehicles_company_ownership
  ON vehicles (company_id, ownership_model) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_vehicles_company_body_capacity
  ON vehicles (company_id, body_type, capacity_kg, capacity_cbm) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_vehicles_company_external_fleet
  ON vehicles (company_id, tracking_provider, external_fleet_id) WHERE deleted_at IS NULL;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_15_stage141_saudi_fleet_operations','Saudi owned, rented and partner fleet operating fields')
ON CONFLICT(version) DO NOTHING;

COMMIT;
