-- Stage 133 — remove provider/certification meaning from the canonical demo ELD
--
-- The safety-pilot fixture is useful for HOS and malfunction workflow demos, but
-- its legacy "Healthy" provider-sync value could be read as authentic provider
-- evidence. Scrub only the exact synthetic record shape inside an explicitly
-- named demo tenant. Real device/provider rows and driver qualification records
-- are deliberately untouched.

BEGIN;

UPDATE eld_devices d
SET device_model = 'Synthetic demo ELD',
    provider = 'Synthetic fixture — no provider account',
    last_sync_at = NULL,
    firmware_version = 'demo-fixture',
    provider_sync_status = 'Unverified'
WHERE d.device_serial LIKE 'MER-ELD-%'
  AND d.device_model IN ('Pilot ELD','Synthetic demo ELD')
  AND d.provider IN ('Synthetic Provider','Synthetic fixture — no provider account')
  AND EXISTS (
    SELECT 1
    FROM companies c
    WHERE c.id = d.company_id
      AND (LOWER(c.name) LIKE '%demo%' OR LOWER(c.company_code) LIKE '%demo%')
  );

-- The checked-in ACME scale harness predates the global demo-name banner. Its
-- stable company code and exact ELD serial/model vocabulary make the synthetic
-- rows distinguishable without touching customer-created records.
UPDATE companies
SET name = 'Acme Transport — Demo'
WHERE company_code = 'ACME-TRANSPORT'
  AND name = 'Acme Transport';

UPDATE eld_devices d
SET device_serial = REGEXP_REPLACE(d.device_serial, '^ACME-ELD-', 'ACME-DEMO-ELD-'),
    device_model = 'Synthetic demo ELD',
    provider = 'Synthetic fixture — no provider account',
    status = 'Diagnostic',
    last_heartbeat_at = NULL,
    last_sync_at = NULL,
    firmware_version = 'demo-fixture',
    provider_sync_status = 'Unverified',
    api_key_hash = NULL,
    api_key_previous_hash = NULL,
    api_key_previous_valid_until = NULL,
    hmac_secret = NULL,
    hmac_secret_encrypted = NULL,
    hmac_previous_secret_encrypted = NULL,
    hmac_previous_valid_until = NULL
WHERE d.company_id = (SELECT id FROM companies WHERE company_code='ACME-TRANSPORT')
  AND d.device_serial ~ '^ACME-ELD-[0-9]{4}$'
  AND d.device_model IN ('GO9','VG34','LBB-3')
  AND d.provider IN ('Geotab','Samsara','Motive');

-- The older time-series enrichment fixture minted provider-branded, active-looking
-- gateways and recent heartbeat/sync timestamps. Match its deterministic serial and
-- exact provider/model vocabulary only inside explicitly named demo tenants.
UPDATE eld_devices d
SET device_model = 'Synthetic demo gateway',
    provider = 'Synthetic fixture — no provider account',
    status = 'Diagnostic',
    last_heartbeat_at = NULL,
    last_sync_at = NULL,
    firmware_version = 'demo-fixture',
    provider_sync_status = 'Unverified',
    api_key_hash = NULL,
    api_key_previous_hash = NULL,
    api_key_previous_valid_until = NULL,
    hmac_secret = NULL,
    hmac_secret_encrypted = NULL,
    hmac_previous_secret_encrypted = NULL,
    hmac_previous_valid_until = NULL
WHERE d.device_serial ~ ('^ELD-' || d.company_id || '-[0-9]{3}$')
  AND d.device_model IN ('GO9','VG34','LBB-3','Synthetic demo gateway')
  AND d.provider IN ('Geotab','Samsara','Motive','Synthetic fixture — no provider account')
  AND EXISTS (
    SELECT 1
    FROM companies c
    WHERE c.id = d.company_id
      AND (LOWER(c.name) LIKE '%demo%' OR LOWER(c.company_code) LIKE '%demo%')
  );

COMMENT ON COLUMN hos_logs.is_certified IS
  'Driver attestation of a complete daily HOS record; never ELD product, provider, device, or regulatory certification.';

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_09_stage133_demo_eld_certification_truth',
        'Scrub false certification, provider, device and sync meaning from synthetic ELD fixtures')
ON CONFLICT(version) DO NOTHING;

COMMIT;
