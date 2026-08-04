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
// Idempotent and fail-closed: after Stage 58 is present it enables RLS, removes every
// existing policy, recreates the signed-ticket app policy + explicit system policy,
// and re-applies FORCE. It never recreates the forgeable legacy GUC policies. PostgreSQL
// OR-combines permissive policies, so leaving even one rogue USING(true) policy is
// an isolation bypass.
// Privileges are intentionally migration-owned: a boot-time blanket GRANT would
// undo the least-privilege contracts for reference, evidence, and audit tables.
// The companies control-plane table is special-cased as tenant-scoped on its id;
// reviewed bootstrap/platform flows use the canonical system bypass.
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
            policy_rec RECORD;
            tenant_col text;
            skip_tables text[] := ARRAY[
                'platform_admin_users', 'platform_sessions', 'platform_audit_log',
                'platform_packages', 'platform_invoices', 'platform_impersonation_sessions', 'schema_migrations',
                -- Pre-tenant infrastructure replay ledger. Its nullable company_id is
                -- audit metadata; gateway signature is the authorization boundary.
                'gps_gateway_replay',
                -- Nullable global templates/catalog rows require Stage-58's split
                -- SELECT versus mutation policies and must not be generically replaced.
                'roles', 'report_catalog'
            ];
        BEGIN
            -- Owner-capable development boot may precede the mandatory terminal
            -- migration. In that state do not recreate legacy GUC-trusting policy;
            -- Stage 58/readiness owns the fail-closed cutover.
            IF to_regprocedure('opstrax_security.current_tenant_id()') IS NULL
               OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
                RETURN;
            END IF;
            FOR rec IN
                SELECT c.table_name,
                       bool_or(c.column_name = 'company_id') AS has_company,
                       bool_or(c.column_name = 'tenant_id')  AS has_tenant
                FROM information_schema.columns c
                JOIN information_schema.tables t
                  ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                WHERE c.table_schema = 'public'
                  AND t.table_type = 'BASE TABLE'
                  AND ((c.column_name IN ('company_id', 'tenant_id') AND c.data_type = 'bigint')
                    OR (c.table_name = 'companies' AND c.column_name = 'id' AND c.data_type = 'bigint'))
                GROUP BY c.table_name
            LOOP
                CONTINUE WHEN rec.table_name = ANY(skip_tables);

                tenant_col := CASE WHEN rec.table_name = 'companies' THEN 'id'
                                   WHEN rec.has_company THEN 'company_id' ELSE 'tenant_id' END;

                EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', rec.table_name);

                FOR policy_rec IN
                    SELECT policyname FROM pg_policies
                    WHERE schemaname = 'public' AND tablename = rec.table_name
                LOOP
                    EXECUTE format('DROP POLICY %I ON public.%I', policy_rec.policyname, rec.table_name);
                END LOOP;

                EXECUTE format($p$
                    CREATE POLICY tenant_ticket_app ON public.%I
                    FOR ALL
                    TO opstrax_app
                    USING (%I = (SELECT opstrax_security.current_tenant_id()))
                    WITH CHECK (%I = (SELECT opstrax_security.current_tenant_id()))
                $p$, rec.table_name, tenant_col, tenant_col);

                EXECUTE format($p$
                    CREATE POLICY system_control_plane ON public.%I
                    FOR ALL
                    TO opstrax_system
                    USING (true)
                    WITH CHECK (true)
                $p$, rec.table_name);
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

        """;
}
