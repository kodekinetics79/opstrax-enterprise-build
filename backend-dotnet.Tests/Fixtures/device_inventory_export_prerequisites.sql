-- Dedicated Stage 127 export fixture additions. The shared retirement fixture
-- creates the minimal governed device tables; the export also projects the
-- current installation role without requiring the full production seed.
ALTER TABLE eld_devices
  ADD COLUMN IF NOT EXISTS imei VARCHAR(32) NULL,
  ADD COLUMN IF NOT EXISTS device_category VARCHAR(40) NULL,
  ADD COLUMN IF NOT EXISTS manufacturer VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS hardware_revision VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS last_seen_at TIMESTAMPTZ NULL;

ALTER TABLE device_installations
  ADD COLUMN IF NOT EXISTS device_role VARCHAR(40) NULL,
  ADD COLUMN IF NOT EXISTS is_primary BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE dispatch_assignments
  ADD COLUMN IF NOT EXISTS assignment_status VARCHAR(40) NOT NULL DEFAULT 'assigned';
