-- Stage 94 — Samsara provider commercial truth.
--
-- Earlier demo fixtures asserted Connected/Real-time and a provider account without
-- a real tenant credential, provider handshake, or data sync. Reset only those exact
-- known fixtures. Any customer-managed connector, successful handshake, credential,
-- cursor, or provider-derived evidence falls outside these predicates and is retained.
BEGIN;
SET LOCAL lock_timeout = '10s';

UPDATE integrations
SET category = 'Telematics & ELD',
    description = 'Samsara connector readiness for GPS position, engine-state, and odometer sync. A tenant-authorized provider account and successful live handshake are required before use.',
    status = 'Disconnected',
    sync_label = 'Never',
    last_sync_at = NULL,
    related_systems_json = '["vehicles"]'::jsonb,
    connected_to_json = '["GPS","Engine state","Odometer"]'::jsonb,
    config_json = '{}'::jsonb,
    last_tested_at = NULL,
    last_test_ok = NULL,
    last_test_message = NULL,
    updated_at = NOW()
WHERE integration_key = 'samsara'
  AND provider_name = 'Samsara'
  AND status = 'Connected'
  AND config_json = '{"providerAccountId":"sam-1001"}'::jsonb
  AND last_sync_at = TIMESTAMPTZ '2026-06-24T14:14:00Z'
  AND last_test_ok IS NULL;

UPDATE integrations
SET status = 'Disconnected',
    sync_label = 'Never',
    last_sync_at = NULL,
    updated_at = NOW()
WHERE integration_key IS NULL
  AND provider_name = 'Samsara Import Adapter'
  AND category = 'Telematics'
  AND status = 'Connected'
  AND (config_json IS NULL OR config_json = '{}'::jsonb)
  AND last_sync_at IS NULL
  AND last_test_ok IS NULL;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_02_stage94_samsara_provider_truth', 'Reset only exact fabricated Samsara provider fixtures to unverified')
ON CONFLICT (version) DO NOTHING;
COMMIT;
