-- Stage 19 — Row-Level Security (defense-in-depth tenant isolation)
--
-- PURPOSE
--   Adds Postgres Row-Level Security (RLS) policies to every tenant-owned table
--   as a database-native backstop to the application-layer `company_id` /
--   `tenant_id` predicates. Today tenant isolation depends entirely on each
--   handler remembering its WHERE clause; RLS makes the database refuse to
--   return another tenant's rows even if a predicate is ever dropped.
--
-- DESIGN
--   * Tenant context is carried in the session GUC `app.current_tenant_id`.
--     The request pipeline is expected to `SET` it to the authenticated
--     company_id on every request (see the remediation report for the
--     connection-scoping prerequisite — this is NOT yet wired, see NOTE below).
--   * Each tenant table gets a permissive `tenant_isolation` policy that only
--     exposes rows whose tenant column equals the session GUC.
--   * Legitimate cross-tenant platform-admin / system work is handled by a
--     SEPARATE, explicit `platform_admin_bypass` policy gated on its own GUC
--     `app.platform_admin = 'on'` — never a blanket role bypass. Permissive
--     policies are OR-combined, so a row is reachable iff (tenant matches) OR
--     (platform-admin context is explicitly set).
--
-- SAFETY / REVERSIBILITY
--   * ADDITIVE and idempotent. Re-runnable. Touches no data, drops nothing.
--   * NOTE — enforcement is intentionally DORMANT until infrastructure is
--     hardened. RLS is bypassed for superusers and for BYPASSRLS roles, and
--     (without FORCE) for a table's owner. The current local/Render/Neon app
--     role owns these tables, so these policies do not change behaviour yet.
--     To ACTIVATE enforcement (owner action — see report):
--       1. Run the app as a dedicated NON-superuser, NON-BYPASSRLS role that
--          is not the table owner (or add `... FORCE ROW LEVEL SECURITY`).
--       2. Wire per-request `SET app.current_tenant_id = <company_id>` into a
--          request-scoped DB connection/transaction (the current per-query
--          connection model cannot do this safely — see report).
--   * ROLLBACK (manual):
--       DO $$ DECLARE t text; BEGIN
--         FOR t IN SELECT tablename FROM pg_tables WHERE schemaname='public' AND rowsecurity LOOP
--           EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON public.%I', t);
--           EXECUTE format('DROP POLICY IF EXISTS platform_admin_bypass ON public.%I', t);
--           EXECUTE format('ALTER TABLE public.%I DISABLE ROW LEVEL SECURITY', t);
--         END LOOP; END $$;

BEGIN;

DO $rls$
DECLARE
    rec        RECORD;
    tenant_col text;
    -- Global / platform-control tables that are intentionally cross-tenant or
    -- are written by system contexts; excluded so enforcement (once activated)
    -- never strands the control plane. Tenant operational tables are unaffected.
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

        -- Prefer company_id when a table carries both columns.
        tenant_col := CASE WHEN rec.has_company THEN 'company_id' ELSE 'tenant_id' END;

        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', rec.table_name);

        -- Tenant isolation: rows are visible/insertable only for the session tenant.
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

        -- Separate, explicit platform-admin / system bypass — NOT a role bypass.
        -- Only active when app.platform_admin is explicitly set to 'on'.
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

COMMIT;
