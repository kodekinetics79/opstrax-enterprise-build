-- Stage 113 — provider-account-bound Samsara asset identity.
--
-- A Samsara asset ID is unique only inside its provider organization. Legacy
-- discovery used samsara-{assetId}, so changing an integration token to another
-- organization could reuse an earlier OpsTrax device and its installation. Keep
-- the provider-issued organization reference on the verified connector and bind
-- every newly discovered device to the (provider, organization, asset) tuple.
BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE integrations
  ADD COLUMN IF NOT EXISTS provider_account_ref VARCHAR(160) NULL,
  ADD COLUMN IF NOT EXISTS provider_account_verified_at TIMESTAMPTZ NULL;

UPDATE integrations
SET provider_account_ref=NULL,provider_account_verified_at=NULL
WHERE (provider_account_ref IS NULL) <> (provider_account_verified_at IS NULL);

ALTER TABLE integrations DROP CONSTRAINT IF EXISTS ck_stage113_provider_account_verification_pair;
ALTER TABLE integrations ADD CONSTRAINT ck_stage113_provider_account_verification_pair
  CHECK ((provider_account_ref IS NULL) = (provider_account_verified_at IS NULL));

ALTER TABLE eld_devices
  ADD COLUMN IF NOT EXISTS provider_account_ref VARCHAR(160) NULL,
  ADD COLUMN IF NOT EXISTS provider_external_id VARCHAR(160) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage113_provider_asset_identity
  ON eld_devices(company_id,LOWER(BTRIM(provider)),provider_account_ref,provider_external_id)
  WHERE deleted_at IS NULL
    AND NULLIF(BTRIM(provider),'') IS NOT NULL
    AND NULLIF(BTRIM(provider_account_ref),'') IS NOT NULL
    AND NULLIF(BTRIM(provider_external_id),'') IS NOT NULL;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_07_stage113_samsara_account_identity',
        'Provider-account-bound Samsara connector and discovered asset identity')
ON CONFLICT (version) DO NOTHING;
COMMIT;
