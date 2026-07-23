-- Telematics 002 — Device lifecycle state machine + transition audit
-- ============================================================================
-- PURPOSE
--   eld_devices today carries a coarse operational `status` ('Active',
--   'CredentialRotationRequired' from stage27, ...). That conflates two axes and
--   cannot answer "where is this device in its lifecycle?" — provisioned but not
--   yet enrolled, live, degraded, quarantined, being decommissioned, retired.
--   Fleet onboarding, credential rotation (stage27), and RMA/decommission flows
--   all need a first-class lifecycle state with a CONSTRAINED vocabulary and an
--   append-only, tenant-scoped audit of every transition (who / why / when).
--
-- GROUNDING
--   eld_devices (database/init/001_schema.sql:1329) is a GLOBAL registry keyed by
--   device_serial (UNIQUE) with NO company_id column, so it is deliberately NOT
--   under RLS (stage19 only enrolls tables with a company_id/tenant_id bigint).
--   The lifecycle *state* therefore lives on the global row; the *audit trail*
--   (device_state_transitions) is company_id-scoped and gets full RLS, matching
--   the stage33 table pattern. `device_state` is ORTHOGONAL to `status` and to
--   stage27's credential fields — it never relaxes the ck_eld_devices_active_credentials
--   invariant.
--
-- LIFECYCLE VOCABULARY (16 states)
--   Provisioned → Registered → Enrolled → Activated → Online ⇄ Idle ⇄ Offline
--   Degraded / Maintenance (recoverable) · Suspended / Quarantined (held)
--   Lost / Faulty (fault) · Decommissioning → Decommissioned → Retired (terminal)
--
-- SAFETY / REVERSIBILITY
--   ADDITIVE + idempotent + re-runnable. Column default 'Provisioned' so the
--   NOT NULL add is instant metadata-only on PG11+. MUST be applied by the DB
--   OWNER. Explicit -- ROLLBACK section at the foot.
-- ============================================================================

BEGIN;

-- ── 1. Lifecycle state on the global device registry ────────────────────────
ALTER TABLE eld_devices
    ADD COLUMN IF NOT EXISTS device_state TEXT NOT NULL DEFAULT 'Provisioned';

-- Constrain to the 16-state vocabulary (idempotent: drop-then-add, per stage27).
ALTER TABLE eld_devices DROP CONSTRAINT IF EXISTS ck_eld_devices_device_state;
ALTER TABLE eld_devices
    ADD CONSTRAINT ck_eld_devices_device_state CHECK (device_state IN (
        'Provisioned',      -- record created, credentials may not be issued yet
        'Registered',       -- identity (serial/IMEI) known to the platform
        'Enrolled',         -- bound to a tenant vehicle/driver
        'Activated',        -- credentials issued, cleared to transmit
        'Online',           -- transmitting fresh telemetry
        'Idle',             -- enrolled + active but no recent motion
        'Offline',          -- past the stale threshold, no heartbeat
        'Degraded',         -- transmitting but low quality / partial
        'Maintenance',      -- intentionally pulled for service/firmware
        'Suspended',        -- administratively paused (billing/policy)
        'Quarantined',      -- fail-closed credential quarantine (stage27)
        'Lost',             -- reported lost/stolen
        'Faulty',           -- hardware fault, pending RMA
        'Decommissioning',  -- teardown in progress
        'Decommissioned',   -- removed from service
        'Retired'           -- terminal, archived
    ));

CREATE INDEX IF NOT EXISTS idx_eld_devices_device_state
    ON eld_devices (device_state);

-- ── 2. Company-scoped, append-only transition audit ─────────────────────────
CREATE TABLE IF NOT EXISTS device_state_transitions (
    id             BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     BIGINT      NOT NULL,          -- tenant that owns this device enrollment
    device_id      BIGINT      NOT NULL,          -- FK-in-spirit -> eld_devices.id
    device_serial  VARCHAR(120) NULL,             -- denormalized for audit legibility if device row is later purged
    from_state     TEXT        NULL,              -- NULL for the very first transition
    to_state       TEXT        NOT NULL,
    reason         TEXT        NULL,              -- free-text / policy code, e.g. 'credential_rotation_required'
    actor          TEXT        NULL,              -- who/what caused it: user email, 'system', 'gateway', job name
    correlation_id UUID        NULL,              -- ties the transition to the ingest/provisioning event that triggered it
    at             TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Same constrained vocabulary; from_state may be NULL (first transition).
ALTER TABLE device_state_transitions DROP CONSTRAINT IF EXISTS ck_dst_states;
ALTER TABLE device_state_transitions
    ADD CONSTRAINT ck_dst_states CHECK (
        to_state IN (
            'Provisioned','Registered','Enrolled','Activated','Online','Idle','Offline',
            'Degraded','Maintenance','Suspended','Quarantined','Lost','Faulty',
            'Decommissioning','Decommissioned','Retired'
        )
        AND (from_state IS NULL OR from_state IN (
            'Provisioned','Registered','Enrolled','Activated','Online','Idle','Offline',
            'Degraded','Maintenance','Suspended','Quarantined','Lost','Faulty',
            'Decommissioning','Decommissioned','Retired'
        ))
    );

-- Newest-first history per device within a tenant (audit timeline query).
CREATE INDEX IF NOT EXISTS idx_dst_company_device_at
    ON device_state_transitions (company_id, device_id, at DESC);
-- Tenant-wide recent transitions (ops dashboard / anomaly feed).
CREATE INDEX IF NOT EXISTS idx_dst_company_at
    ON device_state_transitions (company_id, at DESC);

-- ── 3. RLS on the tenant-scoped audit table (stage19/stage20/stage33 pattern) ─
ALTER TABLE device_state_transitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE device_state_transitions FORCE  ROW LEVEL SECURITY;

DO $rls$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'device_state_transitions' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON device_state_transitions
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'device_state_transitions' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON device_state_transitions
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
END
$rls$;

-- Grant DML to the restricted app role where it exists (skip otherwise — stage33 note).
DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT ON device_state_transitions TO opstrax_app;  -- append-only: no UPDATE/DELETE
        GRANT USAGE, SELECT ON SEQUENCE device_state_transitions_id_seq TO opstrax_app;
    END IF;
END
$grant$;

-- ── 4. Ledger ───────────────────────────────────────────────────────────────
INSERT INTO schema_migrations (version, description)
VALUES ('telematics_002_device_lifecycle',
        'eld_devices.device_state (16-state CHECK) + device_state_transitions audit (RLS)')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- ROLLBACK  (manual; run as DB OWNER — NOT auto-applied)
-- ----------------------------------------------------------------------------
-- BEGIN;
--   DROP TABLE IF EXISTS device_state_transitions;   -- drops its policies + sequence
--   DROP INDEX IF EXISTS idx_eld_devices_device_state;
--   ALTER TABLE eld_devices DROP CONSTRAINT IF EXISTS ck_eld_devices_device_state;
--   ALTER TABLE eld_devices DROP COLUMN IF EXISTS device_state;
--   DELETE FROM schema_migrations WHERE version = 'telematics_002_device_lifecycle';
-- COMMIT;
-- ============================================================================
