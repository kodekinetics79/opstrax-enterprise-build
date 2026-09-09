-- Stage 134 — reconcile the original OPX-DEMO ELD fixture
--
-- Stage 133 removed provider meaning from the newer Meridian, ACME and
-- time-series demo fixtures. The original checked-in seed used a different,
-- deterministic serial/model vocabulary in the OPX-DEMO tenant and therefore
-- survived that narrowly scoped cleanup. Reconcile only that exact legacy
-- fixture shape. Customer-created ELD rows in every other tenant are untouched.

BEGIN;

UPDATE eld_devices d
SET device_serial = CASE
        WHEN d.device_serial LIKE 'DEMO-%' THEN d.device_serial
        ELSE 'DEMO-' || d.device_serial
    END,
    device_model = 'Synthetic demo ELD',
    provider = 'Synthetic fixture — no provider account',
    status = 'Diagnostic',
    device_state = 'Quarantined',
    health_status = 'never_connected',
    health_reason = 'Synthetic fixture — authoritative provider source not connected',
    recommended_action = 'Connect and verify a real provider account and exact physical device',
    first_connected_at = NULL,
    last_heartbeat_at = NULL,
    last_seen_at = NULL,
    last_sync_at = NULL,
    firmware_version = 'demo-fixture',
    provider_sync_status = 'Unverified',
    provider_account_ref = NULL,
    provider_external_id = NULL,
    manufacturer = NULL,
    hardware_revision = NULL,
    api_key_hash = NULL,
    api_key_previous_hash = NULL,
    api_key_previous_valid_until = NULL,
    hmac_secret = NULL,
    hmac_secret_encrypted = NULL,
    hmac_previous_secret_encrypted = NULL,
    hmac_previous_valid_until = NULL,
    notes = 'Synthetic demo fixture; no provider account or physical-device evidence.',
    updated_at = NOW()
WHERE d.company_id = (SELECT id FROM companies WHERE company_code='OPX-DEMO')
  AND d.device_serial ~ '^(DEMO-)?ELD-[0-9]{3}-(TRK|VAN|BOX)[0-9]{3}$'
  AND d.device_model IN (
      'KeepTruckin M300','Samsara VG34','Omnitracs IVG','Synthetic demo ELD'
  )
  AND d.provider IN (
      'Motive','Samsara','Omnitracs','Synthetic fixture — no provider account'
  );

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_09_stage134_legacy_demo_eld_reconciliation',
        'Reconcile original OPX-DEMO ELD fixtures to explicit synthetic unverified records')
ON CONFLICT(version) DO NOTHING;

COMMIT;
