-- ─────────────────────────────────────────────────────────────────────────────
-- PT40-Q go-live: surgical production repair + device commissioning prep
--
-- WHY NOT THE FULL PREDEPLOY RUNNER
--   27 migrations are unapplied, including GL/tax/billing/MFA changes unrelated to
--   telematics, plus a terminal security cutover that rewrites RLS policies. Direct
--   inspection (not the ledger -- the ledger is unreliable here: gps_gateway_replay
--   exists while its migration is unrecorded) shows the telemetry path needs only
--   what is below. Everything here is idempotent and additive: no policy is
--   reopened, no privilege is revoked, no existing row is rewritten.
--
-- STATE THIS REPAIRS (verified against production by direct inspection):
--   1. telemetry_gateways             -- MISSING. Root cause of the 503 on gps-ingest:
--                                        GpsGatewayProjectionTopologyReadyAsync requires
--                                        telemetry_gateways.secret_encrypted. 11 of the
--                                        12 required columns already exist; only this
--                                        one table is absent.
--   2. device_installation_quarantine -- MISSING. The ingest device lookup evaluates an
--                                        EXISTS against it; absent, the query throws.
--   3. device_installations.effective_from/effective_to -- MISSING. Deployed code
--                                        (commit 42f5890 == main) resolves identity with
--                                        effective dating; production carries the older
--                                        stage66 shape, so resolution errors on an
--                                        undefined column.
--   4. Device 1011 (PT40-Q, IMEI 862464068456321) has revoked_at set and status
--      'CredentialRotationRequired' -- ingest rejects it 403.
--   5. Zero device_installations rows exist, so identity resolution returns 422.
--
-- RUN:  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -f tools/pt40/01-schema-and-device.sql
--
-- AFTER THIS, ONE STEP REMAINS THAT SQL CANNOT DO -- see 02-credentials-and-gateway.md.
-- The first valid fix promotes the device provisioning -> Active, and both
-- ck_eld_devices_active_credentials and ck_stage66_eld_active_credentials then require
-- a real api_key_hash + envelope-encrypted hmac secret. Those must be minted through the
-- API (it holds DATA_ENCRYPTION_KEY); writing them by hand here would either fail the
-- constraint or forge an envelope the app cannot decrypt.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- ── 1. telemetry_gateways (stage42) ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS telemetry_gateways (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    gateway_id VARCHAR(120) NOT NULL,
    company_id BIGINT NOT NULL,
    gateway_name VARCHAR(220) NULL,
    secret_encrypted TEXT NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    last_seen_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL,
    UNIQUE (gateway_id)
);
CREATE INDEX IF NOT EXISTS idx_telemetry_gateways_company
  ON telemetry_gateways (company_id, status);

ALTER TABLE telemetry_gateways ENABLE ROW LEVEL SECURITY;
ALTER TABLE telemetry_gateways FORCE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE telemetry_gateways FROM PUBLIC;

-- stage42 skips its own grants/policies when stage58 is live (it is), which would
-- leave the table RLS-forced with no way in. Mirror the ACL of the comparable
-- system-only table gps_gateway_replay exactly: opstrax_system arwd + one FOR ALL
-- policy. Deliberately NO opstrax_app grant -- secret-bearing columns stay
-- system-only, and every code path touching this table runs in system scope.
GRANT SELECT, INSERT, UPDATE, DELETE ON telemetry_gateways TO opstrax_system;
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies
                 WHERE schemaname='public' AND tablename='telemetry_gateways'
                   AND policyname='system_control_plane') THEN
    CREATE POLICY system_control_plane ON telemetry_gateways FOR ALL
      TO opstrax_system USING (true) WITH CHECK (true);
  END IF;
END $$;

-- ── 2. device_installation_quarantine (stage80 subset) ─────────────────────
CREATE TABLE IF NOT EXISTS device_installation_quarantine (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NULL,
  device_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  installation_id BIGINT NULL,
  reason_code VARCHAR(80) NOT NULL,
  evidence_json JSONB NOT NULL,
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  resolved_at TIMESTAMPTZ NULL,
  resolved_by BIGINT NULL,
  resolution_notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_device_installation_quarantine_evidence
  ON device_installation_quarantine
  (reason_code, COALESCE(device_id,0), COALESCE(vehicle_id,0), COALESCE(installation_id,0));

ALTER TABLE device_installation_quarantine ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_installation_quarantine FORCE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE device_installation_quarantine FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON device_installation_quarantine TO opstrax_system;
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies
                 WHERE schemaname='public' AND tablename='device_installation_quarantine'
                   AND policyname='system_control_plane') THEN
    CREATE POLICY system_control_plane ON device_installation_quarantine FOR ALL
      TO opstrax_system USING (true) WITH CHECK (true);
  END IF;
END $$;

-- ── 3. Effective-dated installations (stage80 subset) ──────────────────────
-- Only the two columns the deployed resolution query filters on. effective_from is
-- left NULLABLE on purpose: stage80 sets it NOT NULL only after its own backfill and
-- quarantine pass, and forcing it here would pre-empt that migration.
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS effective_from TIMESTAMPTZ NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS effective_to   TIMESTAMPTZ NULL;
UPDATE device_installations
   SET effective_from = COALESCE(effective_from, installed_at, created_at),
       effective_to   = COALESCE(effective_to, removed_at)
 WHERE effective_from IS NULL
    OR (effective_to IS NULL AND removed_at IS NOT NULL);

-- ── 4. Un-revoke the PT40 so ingest stops rejecting it 403 ─────────────────
-- 'Provisioning' (not 'Active'): the credential CHECK constraints bite only at
-- Active, and the first signed fix performs that promotion itself -- which is
-- exactly why the credentials in step 02 must exist BEFORE the device connects.
UPDATE eld_devices
   SET status       = 'Provisioning',
       device_state = 'Installed',
       revoked_at   = NULL,
       updated_at   = NOW()
 WHERE id = 1011
   AND imei = '862464068456321'
   AND device_serial = '4C4000067803';

-- ── 5. Exactly one current installation (device 1011 -> vehicle 1024) ──────
-- Identity resolution demands EXACTLY one; the guard keeps this re-runnable.
INSERT INTO device_installations
  (company_id, device_id, vehicle_id, status, vin_verified,
   effective_from, installed_at, created_at, updated_at)
SELECT 8, 1011, 1024, 'Installed', FALSE, NOW(), NOW(), NOW(), NOW()
WHERE NOT EXISTS (
  SELECT 1 FROM device_installations
   WHERE company_id=8 AND device_id=1011 AND effective_to IS NULL
     AND status IN ('Installed','Verified')
);

COMMIT;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== required ingest columns (expect still_missing = 0) =='
WITH required(t,c) AS (VALUES
 ('telemetry_gateways','secret_encrypted'),('gps_gateway_replay','signature'),
 ('eld_devices','last_heartbeat_at'),('location_events','observed_at'),
 ('location_events','normalized_at'),('latest_vehicle_positions','source_event_id'),
 ('latest_vehicle_positions','source_channel'),('telemetry_alerts','source_event_id'),
 ('telemetry_alerts','source_channel'),('telemetry_rules','threshold_value'),
 ('geofences','branch_id'),('geofences','polygon_json'))
SELECT count(*) FILTER (WHERE col.column_name IS NULL) AS still_missing,
       count(*)                                        AS required_total
FROM required r
LEFT JOIN information_schema.columns col
  ON col.table_schema='public' AND col.table_name=r.t AND col.column_name=r.c;

\echo ''
\echo '== PT40 device + its single current installation =='
SELECT d.id, d.imei, d.status, d.device_state, d.revoked_at,
       (d.hmac_secret_encrypted IS NOT NULL) AS has_enc_secret,
       (d.api_key_hash IS NOT NULL)          AS has_api_key,
       (SELECT count(*) FROM device_installations i
         WHERE i.company_id=8 AND i.device_id=d.id AND i.effective_to IS NULL
           AND i.status IN ('Installed','Verified')) AS current_installations
FROM eld_devices d WHERE d.id=1011;
