-- Stage 52 — Fleet master identity uniqueness under concurrent writes
-- Owner-only, additive/idempotent, and fail-fast under psql ON_ERROR_STOP=1.
--
-- Codes remain reserved after soft deletion, matching the legacy table UNIQUE
-- constraints and API restore/archive contract. VIN and licence identities are
-- unique only across active rows so a genuinely retired identity can be reused.
-- This pre-deploy migration intentionally fails before creating any index when
-- historical duplicates exist; it never guesses which customer record to retain.

BEGIN;

ALTER TABLE drivers ADD COLUMN IF NOT EXISTS license_number_bidx VARCHAR(64) NULL;

DO $fleet_identity_preflight$
DECLARE
  duplicate_groups BIGINT;
  encrypted_without_blind_index BIGINT;
BEGIN
  -- A ciphertext cannot be compared with a newly supplied plaintext licence and
  -- PostgreSQL cannot derive the HMAC without the application key.  Silently
  -- indexing it as "legacy plaintext" would therefore allow the same real licence
  -- to be registered again.  Quarantine this ambiguous transition state and require
  -- a key-aware application backfill before Stage 52 can be ledgered.
  SELECT COUNT(*) INTO encrypted_without_blind_index
  FROM drivers
  WHERE deleted_at IS NULL
    AND license_number LIKE 'enc:%'
    AND NULLIF(BTRIM(license_number_bidx),'') IS NULL;
  IF encrypted_without_blind_index > 0 THEN
    RAISE EXCEPTION 'Stage 52 blocked: % active encrypted driver licence row(s) lack license_number_bidx; run a key-aware blind-index backfill and retry', encrypted_without_blind_index;
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, LOWER(BTRIM(vehicle_code))
    FROM vehicles
    GROUP BY company_id, LOWER(BTRIM(vehicle_code))
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    RAISE EXCEPTION 'Stage 52 blocked: % tenant/vehicle-code duplicate group(s) require reconciliation', duplicate_groups;
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, LOWER(BTRIM(driver_code))
    FROM drivers
    GROUP BY company_id, LOWER(BTRIM(driver_code))
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    RAISE EXCEPTION 'Stage 52 blocked: % tenant/driver-code duplicate group(s) require reconciliation', duplicate_groups;
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, LOWER(BTRIM(vin))
    FROM vehicles
    WHERE deleted_at IS NULL AND NULLIF(BTRIM(vin),'') IS NOT NULL
    GROUP BY company_id, LOWER(BTRIM(vin))
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    RAISE EXCEPTION 'Stage 52 blocked: % active tenant/VIN duplicate group(s) require reconciliation', duplicate_groups;
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, LOWER(BTRIM(license_number))
    FROM drivers
    WHERE deleted_at IS NULL
      AND NULLIF(BTRIM(license_number),'') IS NOT NULL
      AND NULLIF(BTRIM(license_number_bidx),'') IS NULL
    GROUP BY company_id, LOWER(BTRIM(license_number))
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    RAISE EXCEPTION 'Stage 52 blocked: % active tenant/plaintext-licence duplicate group(s) require reconciliation', duplicate_groups;
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, license_number_bidx
    FROM drivers
    WHERE deleted_at IS NULL AND NULLIF(BTRIM(license_number_bidx),'') IS NOT NULL
    GROUP BY company_id, license_number_bidx
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    RAISE EXCEPTION 'Stage 52 blocked: % active tenant/licence blind-index duplicate group(s) require reconciliation', duplicate_groups;
  END IF;
END
$fleet_identity_preflight$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicles_identity_code_normalized
  ON vehicles (company_id, LOWER(BTRIM(vehicle_code)));
CREATE UNIQUE INDEX IF NOT EXISTS uq_drivers_identity_code_normalized
  ON drivers (company_id, LOWER(BTRIM(driver_code)));
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicles_active_vin_normalized
  ON vehicles (company_id, LOWER(BTRIM(vin)))
  WHERE deleted_at IS NULL AND NULLIF(BTRIM(vin),'') IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_drivers_active_license_plaintext_normalized
  ON drivers (company_id, LOWER(BTRIM(license_number)))
  WHERE deleted_at IS NULL
    AND NULLIF(BTRIM(license_number),'') IS NOT NULL
    AND NULLIF(BTRIM(license_number_bidx),'') IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_drivers_active_license_bidx
  ON drivers (company_id, license_number_bidx)
  WHERE deleted_at IS NULL AND NULLIF(BTRIM(license_number_bidx),'') IS NOT NULL;

COMMIT;
