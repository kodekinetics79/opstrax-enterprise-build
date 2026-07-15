using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// RlsReconciliationSchemaService (SEC-2) — the runtime "enroll-on-create" pass.
//
// Stage 19 enrolled every THEN-existing tenant table into Row-Level Security and
// Stage 20 added FORCE + the restricted opstrax_app role. Stage 22 re-ran the
// enrollment as a one-off migration. But those are all point-in-time: the app
// boot creates many tenant tables LATER (each *SchemaService.EnsureAsync), and a
// tenant table created after the last reconciliation runs with NO RLS — its only
// isolation is the hand-written WHERE company_id= predicate. A single dropped
// clause = cross-tenant leak (exactly what RlsTenantIsolationPostgresTests guards).
//
// This service ports the canonical, idempotent Stage-22 reconciliation into the
// boot chain and is wired to run LAST (after every table-creating schema step),
// so ANY tenant-scoped table that exists at the end of boot is enrolled +
// FORCE'd + granted to opstrax_app. It is the permanent fix for the coverage gap
// the Stage-22 migration could only close for tables that existed at migration time.
//
// Idempotent and additive: enables RLS, creates the tenant_isolation +
// platform_admin_bypass policies only if absent, re-applies FORCE, drops nothing.
// Skips the same control-plane / intentionally cross-tenant tables as Stage 19/22.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class RlsReconciliationSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        await db.ExecuteAsync(ReconcileSql, ct: ct);
    }

    private const string ReconcileSql = """
        DO $rls$
        DECLARE
            rec        RECORD;
            tenant_col text;
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

        DO $grant$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO opstrax_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;

                -- Append-only platform audit trail: the app role may INSERT/SELECT but never UPDATE/DELETE,
                -- so a compromised app connection cannot rewrite or erase the control-plane audit history.
                -- Runs AFTER the blanket grant above (this reconciler is the last boot step), so it wins.
                -- The app only ever INSERTs platform_audit_log rows; sequence repair uses setval (no DML).
                -- (Scoped to platform_audit_log; tenant audit_logs is left alone since retention purges it.)
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='platform_audit_log') THEN
                    REVOKE UPDATE, DELETE ON platform_audit_log FROM opstrax_app;
                END IF;
            END IF;
        END
        $grant$;
        """;
}
