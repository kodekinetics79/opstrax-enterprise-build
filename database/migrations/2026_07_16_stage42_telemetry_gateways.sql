-- Stage 42 — Per-gateway telematics credentials (H3)
-- Replaces the single shared fleet-wide gps-ingest secret (a cross-tenant skeleton key) with per-gateway
-- credentials: each trusted forwarding gateway has its OWN envelope-encrypted HMAC secret and is bound to
-- exactly one authorized tenant. A device resolved outside the gateway's company_id is rejected at ingest.
-- Dual-run: forwarders that don't send X-Gateway-Id keep using the legacy shared secret during migration.
-- DECOMMISSION (required for H3 to be fully effective): the legacy Telemetry:GatewaySecret path has NO
-- tenant-scope enforcement, so an attacker holding that secret can still cross tenants by simply omitting
-- the header. After all forwarders are migrated to per-gateway credentials, REMOVE Telemetry:GatewaySecret
-- from prod config (the legacy branch then fails closed on the < 32-char guard). Track this as a hard cutover.
--
-- Owner migration for restricted-role prod. IF NOT EXISTS / idempotent. RLS-enrolled (defense in depth;
-- the ingest lookup reads it in system scope pre-tenant-context).

BEGIN;

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

CREATE INDEX IF NOT EXISTS idx_telemetry_gateways_company ON telemetry_gateways (company_id, status);

DO $rls$
DECLARE
    stage58_live BOOLEAN := FALSE;
BEGIN
    -- This historical migration was missing from the predeploy chain. When repairing an
    -- already-secured database, create only the missing schema here: Stage58 is reapplied
    -- terminally by the runner and must remain the sole policy/grant authority. In particular,
    -- never reopen the former platform-admin GUC policy or broad app table grant mid-upgrade.
    IF to_regclass('public.schema_migrations') IS NOT NULL THEN
        EXECUTE $q$
            SELECT EXISTS (
                SELECT 1 FROM schema_migrations
                WHERE version='2026_07_31_stage58_nonforgeable_tenant_ticket'
            )
        $q$ INTO stage58_live;
    END IF;

    EXECUTE 'ALTER TABLE public.telemetry_gateways ENABLE ROW LEVEL SECURITY';
    EXECUTE 'ALTER TABLE public.telemetry_gateways FORCE ROW LEVEL SECURITY';
    REVOKE ALL ON TABLE public.telemetry_gateways FROM PUBLIC;
    REVOKE ALL ON SEQUENCE public.telemetry_gateways_id_seq FROM PUBLIC;

    IF NOT stage58_live AND NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='telemetry_gateways' AND policyname='tenant_isolation') THEN
        EXECUTE $p$
            CREATE POLICY tenant_isolation ON public.telemetry_gateways FOR ALL
            USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
        $p$;
    END IF;
    IF NOT stage58_live AND NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='telemetry_gateways' AND policyname='platform_admin_bypass') THEN
        EXECUTE $p$
            CREATE POLICY platform_admin_bypass ON public.telemetry_gateways FOR ALL
            USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
        $p$;
    END IF;
    IF NOT stage58_live AND EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON telemetry_gateways TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE telemetry_gateways_id_seq TO opstrax_app;
    END IF;
END
$rls$;

COMMIT;
