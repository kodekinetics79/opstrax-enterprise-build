-- Stage 22 — RLS coverage reconciliation (close the "added after Stage 19" gap)
--
-- PURPOSE
--   Stage 19 enrolled every then-existing tenant table into Row-Level Security and
--   Stage 20 added FORCE + the restricted `opstrax_app` role. Both are point-in-time:
--   any tenant table CREATED AFTER those ran (via a later *SchemaService.EnsureAsync)
--   is NOT enrolled, so its only isolation is the hand-written `WHERE company_id=`
--   predicate — a single dropped clause = cross-tenant leak.
--
--   Observed gap (2026-07-01): these tenant tables carried company_id with NO RLS —
--       alert_rules, hos_records, module_records, zatca_invoices
--   (hos_records = HOS compliance, zatca_invoices = tax invoices — exactly the data
--   that must never leak in the Canada / Saudi pilots).
--
-- DESIGN
--   Re-runs the SAME enrollment logic as Stage 19 (tenant_isolation +
--   platform_admin_bypass policies) and the SAME FORCE pass as Stage 20, over ALL
--   current tenant tables. This is therefore the canonical, idempotent RLS
--   reconciliation pass: run it any time schema changes to re-close the gap.
--   It is a stopgap for the deeper fix (enroll-on-create in the schema-service layer),
--   which SEC-2 addresses.
--
-- SAFETY / REVERSIBILITY
--   * ADDITIVE and idempotent. Re-runnable. Touches no data, drops nothing.
--   * Excludes the same control-plane / intentionally cross-tenant tables as Stage 19.
--     platform_invoices stays EXCLUDED (platform-admin billing, not tenant-scoped).
--   * ROLLBACK (manual): same as Stage 19/20 rollback blocks.

BEGIN;

DO $rls$
DECLARE
    rec        RECORD;
    tenant_col text;
    -- Same exclusion set as Stage 19: control-plane / intentionally cross-tenant.
    skip_tables text[] := ARRAY[
        'platform_admin_users', 'platform_sessions', 'platform_audit_log',
        'platform_packages', 'platform_invoices', 'companies', 'schema_migrations'
    ];
BEGIN
    FOR rec IN
        SELECT c.table_name,
               bool_or(c.column_name = 'company_id') AS has_company,
               bool_or(c.column_name = 'tenant_id')  AS has_tenant
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.table_schema = 'public'
          AND t.table_type = 'BASE TABLE'
          AND c.column_name IN ('company_id', 'tenant_id')
          AND c.data_type = 'bigint'
        GROUP BY c.table_name
    LOOP
        CONTINUE WHEN rec.table_name = ANY(skip_tables);

        tenant_col := CASE WHEN rec.has_company THEN 'company_id' ELSE 'tenant_id' END;

        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', rec.table_name);

        IF NOT EXISTS (
            SELECT 1 FROM pg_policies
            WHERE schemaname = 'public' AND tablename = rec.table_name
              AND policyname = 'tenant_isolation'
        ) THEN
            EXECUTE format($p$
                CREATE POLICY tenant_isolation ON public.%I
                FOR ALL
                USING (%I = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
                WITH CHECK (%I = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            $p$, rec.table_name, tenant_col, tenant_col);
        END IF;

        IF NOT EXISTS (
            SELECT 1 FROM pg_policies
            WHERE schemaname = 'public' AND tablename = rec.table_name
              AND policyname = 'platform_admin_bypass'
        ) THEN
            EXECUTE format($p$
                CREATE POLICY platform_admin_bypass ON public.%I
                FOR ALL
                USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
                WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            $p$, rec.table_name);
        END IF;
    END LOOP;
END
$rls$;

-- Re-apply FORCE over every RLS-enabled tenant table (Stage 20 parity for new tables).
DO $force$
DECLARE t text;
BEGIN
    FOR t IN
        SELECT tablename FROM pg_tables
        WHERE schemaname = 'public' AND rowsecurity = true
    LOOP
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t);
    END LOOP;
END
$force$;

-- Ensure the restricted role can DML the newly-enrolled tables too.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO opstrax_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;

COMMIT;
