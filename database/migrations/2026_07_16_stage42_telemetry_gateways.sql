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
BEGIN
    EXECUTE 'ALTER TABLE public.telemetry_gateways ENABLE ROW LEVEL SECURITY';
    EXECUTE 'ALTER TABLE public.telemetry_gateways FORCE ROW LEVEL SECURITY';
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='telemetry_gateways' AND policyname='tenant_isolation') THEN
        EXECUTE $p$
            CREATE POLICY tenant_isolation ON public.telemetry_gateways FOR ALL
            USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
        $p$;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='telemetry_gateways' AND policyname='platform_admin_bypass') THEN
        EXECUTE $p$
            CREATE POLICY platform_admin_bypass ON public.telemetry_gateways FOR ALL
            USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
        $p$;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON telemetry_gateways TO opstrax_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
    END IF;
END
$rls$;
