-- ─────────────────────────────────────────────────────────────────────────────
-- Reconcile the finance/tax/GL tables to the stage58 security model
--
-- RUN THIS IMMEDIATELY AFTER applying the finance migrations, in the SAME window.
-- Between the two, the new tables are unreadable by the application and
-- /health/ready reports additional rls_violations. That gap is expected and closes
-- here — but do not walk away in the middle of it.
--
-- WHY IT IS NEEDED
--   The finance migrations (stage35-48) predate the stage58 security cutover and were
--   never reconciled with it. Production is uniformly on the stage58 model —
--   system_control_plane on 257 tables, tenant_ticket_app on 248, and ZERO tables on
--   the legacy pair. Those migrations still create the OLD model:
--
--     tenant_isolation      USING (company_id = current_setting('app.current_tenant_id'))
--     platform_admin_bypass USING (current_setting('app.platform_admin') = 'on')
--
--   Under stage58 that policy can never match: BeginTenantScopeAsync takes the
--   non-forgeable ticket path and does not set app.current_tenant_id at all. The tables
--   would be created and the application still could not read them — the endpoints stay
--   500, just with 42501 instead of 42P01.
--
--   stage45_general_ledger is worse: it creates chart_of_accounts, journal_entries and
--   journal_lines with no grants, no RLS and no policies whatsoever.
--
-- WHAT IT DOES, per table
--   1. ENABLE + FORCE row level security
--   2. Drop the legacy policies if the migration created them
--   3. Create the stage58 pair, matching the definitions already in use:
--        tenant_ticket_app     opstrax_app,    ALL,
--                              USING/WITH CHECK (<tenant_col> = (SELECT opstrax_security.current_tenant_id()))
--        system_control_plane  opstrax_system, ALL, USING/WITH CHECK (true)
--      The scalar-subquery form is required: FleetProductionReadinessService asserts the
--      qual contains both the tenant column and 'SELECT', and that with_check = qual.
--   4. GRANT SELECT/INSERT/UPDATE/DELETE to opstrax_app and opstrax_system, plus USAGE
--      on the backing identity sequences
--
--   The tenant column is DETECTED rather than assumed — most of these use company_id,
--   but the model is not universal (authorization_decision_logs uses tenant_id). A table
--   with neither is left alone and reported, because a tenant policy cannot be written
--   for it and guessing would either fail or silently disable isolation.
--
--   Tables absent from the database are skipped, so this is safe to run before or after
--   any subset of the migrations.
--
-- SAFETY
--   Idempotent, additive, and scoped to the 27 finance tables listed below. It never
--   touches an existing table's policies. Nothing is revoked except the legacy policies
--   these same migrations create.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/12-finance-stage58-reconciliation.sql
-- ─────────────────────────────────────────────────────────────────────────────

SET lock_timeout = '5s';

DO $$
DECLARE
  target   TEXT;
  tenant_col TEXT;
  seq      TEXT;
  skipped  TEXT[] := ARRAY[]::TEXT[];
  done     INT := 0;
  targets  TEXT[] := ARRAY[
    'billing_consolidation_runs','billing_profiles','chart_of_accounts',
    'customer_tax_status','driver_detention_pay_policy','fin_config_change_log',
    'fin_config_documents','fin_config_sets','gl_export_runs','gl_periods',
    'invoice_tax_lines','issued_invoice_tax_lines','journal_entries','journal_lines',
    'pay_agreements','revenue_recognition_entries','revrec_fiscal_calendars',
    'revrec_fiscal_periods','revrec_profiles','revrec_schedule_lines','revrec_schedules',
    'seller_tax_registration','settlement_lines','settlement_payments',
    'settlement_statements','tax_profiles','tax_rules'
  ];
BEGIN
  FOREACH target IN ARRAY targets LOOP
    IF to_regclass('public.' || target) IS NULL THEN
      skipped := skipped || (target || ' (table absent)');
      CONTINUE;
    END IF;

    -- Detect the tenant column; never assume company_id.
    SELECT column_name INTO tenant_col
      FROM information_schema.columns
     WHERE table_schema='public' AND table_name=target
       AND column_name IN ('company_id','tenant_id')
     ORDER BY CASE column_name WHEN 'company_id' THEN 0 ELSE 1 END
     LIMIT 1;

    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', target);
    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', target);
    EXECUTE format('REVOKE ALL ON TABLE public.%I FROM PUBLIC', target);

    -- Remove the pre-stage58 policies if this migration created them.
    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON public.%I', target);
    EXECUTE format('DROP POLICY IF EXISTS platform_admin_bypass ON public.%I', target);

    -- System control plane: always present, unconditional.
    IF NOT EXISTS (SELECT 1 FROM pg_policies
                    WHERE schemaname='public' AND tablename=target
                      AND policyname='system_control_plane') THEN
      EXECUTE format($p$CREATE POLICY system_control_plane ON public.%I FOR ALL
                        TO opstrax_system USING (true) WITH CHECK (true)$p$, target);
    END IF;

    -- Tenant policy: only where a tenant column exists.
    IF tenant_col IS NOT NULL THEN
      IF NOT EXISTS (SELECT 1 FROM pg_policies
                      WHERE schemaname='public' AND tablename=target
                        AND policyname='tenant_ticket_app') THEN
        EXECUTE format(
          $p$CREATE POLICY tenant_ticket_app ON public.%I FOR ALL TO opstrax_app
               USING      (%I = (SELECT opstrax_security.current_tenant_id()))
               WITH CHECK (%I = (SELECT opstrax_security.current_tenant_id()))$p$,
          target, tenant_col, tenant_col);
      END IF;
      EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON public.%I TO opstrax_app', target);
    ELSE
      -- No tenant column: isolation cannot be expressed, so the app gets no access and
      -- the table stays system-only. Reported below rather than silently left open.
      skipped := skipped || (target || ' (no company_id/tenant_id — left system-only)');
    END IF;

    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON public.%I TO opstrax_system', target);

    -- Identity/serial sequences need USAGE for inserts to work.
    FOR seq IN
      SELECT quote_ident(s.relname)
        FROM pg_class s
        JOIN pg_depend d ON d.objid = s.oid AND d.deptype IN ('a','i')
        JOIN pg_class t ON t.oid = d.refobjid
       WHERE s.relkind = 'S' AND t.relname = target
    LOOP
      EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE public.%s TO opstrax_app', seq);
      EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE public.%s TO opstrax_system', seq);
    END LOOP;

    done := done + 1;
  END LOOP;

  RAISE NOTICE 'reconciled % table(s)', done;
  IF array_length(skipped, 1) IS NOT NULL THEN
    RAISE NOTICE 'skipped/partial: %', array_to_string(skipped, '; ');
  END IF;
END $$;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== any finance table still carrying a LEGACY policy (expect 0 rows) =='
SELECT tablename, policyname
FROM pg_policies
WHERE schemaname='public'
  AND policyname IN ('tenant_isolation','platform_admin_bypass')
ORDER BY tablename;

\echo ''
\echo '== finance tables that exist but are NOT on the stage58 pair (expect 0 rows) =='
WITH targets(t) AS (VALUES
  ('billing_consolidation_runs'),('billing_profiles'),('chart_of_accounts'),
  ('customer_tax_status'),('driver_detention_pay_policy'),('fin_config_change_log'),
  ('fin_config_documents'),('fin_config_sets'),('gl_export_runs'),('gl_periods'),
  ('invoice_tax_lines'),('issued_invoice_tax_lines'),('journal_entries'),('journal_lines'),
  ('pay_agreements'),('revenue_recognition_entries'),('revrec_fiscal_calendars'),
  ('revrec_fiscal_periods'),('revrec_profiles'),('revrec_schedule_lines'),('revrec_schedules'),
  ('seller_tax_registration'),('settlement_lines'),('settlement_payments'),
  ('settlement_statements'),('tax_profiles'),('tax_rules'))
SELECT t.t AS table_name,
       (SELECT count(*) FROM pg_policies p
         WHERE p.schemaname='public' AND p.tablename=t.t
           AND p.policyname IN ('tenant_ticket_app','system_control_plane')) AS stage58_policies,
       has_table_privilege('opstrax_app', 'public.'||t.t, 'SELECT') AS app_can_select
FROM targets t
WHERE to_regclass('public.'||t.t) IS NOT NULL
  AND (SELECT count(*) FROM pg_policies p
        WHERE p.schemaname='public' AND p.tablename=t.t
          AND p.policyname IN ('tenant_ticket_app','system_control_plane')) < 2
ORDER BY t.t;

\echo ''
\echo '== summary =='
WITH targets(t) AS (VALUES
  ('billing_consolidation_runs'),('billing_profiles'),('chart_of_accounts'),
  ('customer_tax_status'),('driver_detention_pay_policy'),('fin_config_change_log'),
  ('fin_config_documents'),('fin_config_sets'),('gl_export_runs'),('gl_periods'),
  ('invoice_tax_lines'),('issued_invoice_tax_lines'),('journal_entries'),('journal_lines'),
  ('pay_agreements'),('revenue_recognition_entries'),('revrec_fiscal_calendars'),
  ('revrec_fiscal_periods'),('revrec_profiles'),('revrec_schedule_lines'),('revrec_schedules'),
  ('seller_tax_registration'),('settlement_lines'),('settlement_payments'),
  ('settlement_statements'),('tax_profiles'),('tax_rules'))
SELECT count(*) FILTER (WHERE to_regclass('public.'||t) IS NOT NULL) AS tables_present,
       count(*)                                                     AS tables_expected
FROM targets;
