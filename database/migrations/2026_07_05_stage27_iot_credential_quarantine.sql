-- Stage 27 - Fail-closed IoT credential quarantine
--
-- Runtime schema initialization must never create device credentials. This
-- migration revokes legacy devices whose credentials are missing, malformed,
-- too short, or match the former serial-derived development credentials.
-- Operators must rotate affected devices through an approved provisioning flow.

BEGIN;

-- These columns historically came from runtime schema initialization. Declare
-- them here so migration-only production deployments receive the same contract.
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS api_key_hash VARCHAR(64) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS hmac_secret VARCHAR(128) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;

UPDATE eld_devices
SET api_key_hash = NULL,
    hmac_secret = NULL,
    status = 'CredentialRotationRequired',
    revoked_at = COALESCE(revoked_at, NOW()),
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND (
      api_key_hash IS NULL
      OR btrim(api_key_hash) = ''
      OR api_key_hash !~ '^[0-9a-fA-F]{64}$'
      OR hmac_secret IS NULL
      OR btrim(hmac_secret) = ''
      OR length(hmac_secret) < 32
      OR api_key_hash = encode(sha256(('opstrax-' || 'dev-' || device_serial)::bytea), 'hex')
      OR hmac_secret = ('opstrax-' || 'hmac-dev-' || device_serial)
  );

ALTER TABLE eld_devices
DROP CONSTRAINT IF EXISTS ck_eld_devices_active_credentials;

ALTER TABLE eld_devices
ADD CONSTRAINT ck_eld_devices_active_credentials
CHECK (
    status <> 'Active'
    OR (
        api_key_hash IS NOT NULL
        AND api_key_hash ~ '^[0-9a-fA-F]{64}$'
        AND hmac_secret IS NOT NULL
        AND length(btrim(hmac_secret)) >= 32
        AND api_key_hash <> encode(sha256(('opstrax-' || 'dev-' || device_serial)::bytea), 'hex')
        AND hmac_secret <> ('opstrax-' || 'hmac-dev-' || device_serial)
    )
);

INSERT INTO schema_migrations (version, description)
VALUES (
    '2026_07_05_stage27_iot_credential_quarantine',
    'Fail-closed IoT credential quarantine and active-device invariant'
)
ON CONFLICT (version) DO NOTHING;

COMMIT;
