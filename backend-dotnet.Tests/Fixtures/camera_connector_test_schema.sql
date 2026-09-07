-- Disposable Camera Stage112 test fixture only. Production receives these columns
-- from the governed Stage51/95 migration chain before Stage112.
ALTER TABLE integrations
  ADD COLUMN IF NOT EXISTS integration_key VARCHAR(100) NULL,
  ADD COLUMN IF NOT EXISTS config_json JSONB NULL,
  ADD COLUMN IF NOT EXISTS operation_generation BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS operation_lease_token UUID NULL,
  ADD COLUMN IF NOT EXISTS operation_lease_expires_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS operation_last_attempt_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS last_tested_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS last_test_ok BOOLEAN NULL,
  ADD COLUMN IF NOT EXISTS last_test_message TEXT NULL,
  ADD COLUMN IF NOT EXISTS last_sync_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS sync_label VARCHAR(80) NULL,
  ADD COLUMN IF NOT EXISTS sync_last_attempt_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS sync_last_completed_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS sync_last_ok BOOLEAN NULL,
  ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

-- Stage113 requires the fleet identity columns that production receives from the
-- governed Stage65/66/80 chain. This disposable database intentionally installs
-- only the minimum shape needed by the camera/provider identity tests.
ALTER TABLE eld_devices
  ADD COLUMN IF NOT EXISTS company_id BIGINT NULL,
  ADD COLUMN IF NOT EXISTS last_seen_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS device_state TEXT NOT NULL DEFAULT 'Provisioned';
