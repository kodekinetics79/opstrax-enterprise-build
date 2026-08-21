-- Stage 82 — reconcile the live-device credential constraint with envelope encryption.
--
-- Some upgraded databases retained Stage 27's plaintext-HMAC constraint alongside
-- Stage 66's encrypted-secret constraint. Secure provisioning deliberately writes
-- hmac_secret=NULL and hmac_secret_encrypted=<ciphertext>, so the stale constraint
-- rejected every real device with SQLSTATE 23514 after credentials had been minted.
--
-- This migration is additive/idempotent at the release level and never manufactures,
-- decrypts, or rewrites credential material.
BEGIN;

ALTER TABLE public.eld_devices
  DROP CONSTRAINT IF EXISTS ck_eld_devices_active_credentials;

-- Keep one canonical contract name. Dropping the Stage 66 alias is safe inside this
-- transaction because the replacement CHECK is installed before commit.
ALTER TABLE public.eld_devices
  DROP CONSTRAINT IF EXISTS ck_stage66_eld_active_credentials;

ALTER TABLE public.eld_devices
  ADD CONSTRAINT ck_eld_devices_active_credentials CHECK (
    LOWER(status) <> 'active' OR (
      api_key_hash IS NOT NULL
      AND api_key_hash ~ '^[0-9a-fA-F]{64}$'
      AND hmac_secret IS NULL
      AND hmac_secret_encrypted IS NOT NULL
      AND length(btrim(hmac_secret_encrypted)) >= 24
      AND hmac_key_version > 0
      AND revoked_at IS NULL
    )
  ) NOT VALID;

-- Existing active rows already satisfy the Stage 66 encrypted-secret constraint on
-- supported databases. Validation turns schema drift into a deployment failure rather
-- than allowing readiness to claim success while provisioning remains broken.
ALTER TABLE public.eld_devices
  VALIDATE CONSTRAINT ck_eld_devices_active_credentials;

INSERT INTO public.schema_migrations(version, description)
VALUES (
  '2026_08_20_stage82_telematics_device_credential_constraint',
  'Reconcile active-device credentials with encrypted HMAC storage'
)
ON CONFLICT (version) DO NOTHING;

COMMIT;
