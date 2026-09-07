-- Disposable Camera Stage112 test fixture only. Production receives these columns
-- from the governed Stage51/95 migration chain before Stage112.
ALTER TABLE integrations
  ADD COLUMN IF NOT EXISTS integration_key VARCHAR(100) NULL,
  ADD COLUMN IF NOT EXISTS config_json JSONB NULL,
  ADD COLUMN IF NOT EXISTS operation_generation BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS operation_lease_token UUID NULL,
  ADD COLUMN IF NOT EXISTS operation_lease_expires_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
