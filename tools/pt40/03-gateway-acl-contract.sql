-- ─────────────────────────────────────────────────────────────────────────────
-- PT40-Q go-live, step 03: bring telemetry_gateways up to the ACL/RLS contract
--
-- WHY THIS EXISTS
--   Script 01 created telemetry_gateways with only the system-scope ACL it needed to
--   reopen ingest. That worked -- gps-ingest went 503 -> 401 -- but it left the table
--   short of what FleetProductionReadinessService expects of a tenant-scoped table, so
--   the readiness counters moved:
--       missing_tables            1 -> 0   (the win)
--       rls_violations            0 -> 1   (this table has 1 policy; the contract wants 2)
--       grant_violations          8 -> 9   (missing opstrax_app column grants)
--       tenant_grant_violations  19 -> 21
--
--   This script closes exactly that gap and nothing else. The remaining 8 grant /
--   19 tenant-grant / 13 route-column violations pre-date any of this work and belong
--   to unapplied stage76 + stage80 -- schedule those separately, not during demo week.
--
--   Contract, read straight out of FleetProductionReadinessService:
--     * RLS: relrowsecurity AND relforcerowsecurity, a tenant column, and EXACTLY two
--       policies -- 'tenant_ticket_app' (opstrax_app, ALL, qual references the tenant
--       column via opstrax_security.current_tenant_id(), with_check = qual) and
--       'system_control_plane' (opstrax_system, ALL, qual = true). 01 created the
--       second; this adds the first.
--     * Grants: telemetry_gateways is deliberately EXCLUDED from the table-level
--       SELECT requirement and checked by COLUMN instead --
--           opstrax_app MUST have    SELECT on gateway_id
--           opstrax_app MUST have    UPDATE on status
--           opstrax_app MUST NOT have SELECT or UPDATE on secret_encrypted
--       i.e. the app may list and deactivate a gateway, but can never read or rewrite
--       its HMAC material. No table-level grant is issued: the expectation triple for
--       this table is (false,false,false).
--
-- RUN:  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -f tools/pt40/03-gateway-acl-contract.sql
--
-- Idempotent and additive. Safe to re-run.
-- ─────────────────────────────────────────────────────────────────────────────

-- Fail fast rather than queue behind a lock holder. telemetry_gateways is new and
-- effectively unused, so this should never wait -- but a queued exclusive request is
-- what wedged script 04's first run, so every DDL script here sets it now.
SET lock_timeout = '3s';

BEGIN;

-- Column-scoped grants only. secret_encrypted is never granted to opstrax_app, so the
-- envelope stays readable exclusively in system scope.
GRANT SELECT (gateway_id) ON telemetry_gateways TO opstrax_app;
GRANT UPDATE (status)     ON telemetry_gateways TO opstrax_app;

-- The tenant policy, matching the expression already in use on device_installations.
-- The scalar subquery form is required: the contract asserts the qual contains both
-- the tenant column and 'SELECT', and that with_check is identical to qual.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies
                 WHERE schemaname='public' AND tablename='telemetry_gateways'
                   AND policyname='tenant_ticket_app') THEN
    CREATE POLICY tenant_ticket_app ON telemetry_gateways FOR ALL
      TO opstrax_app
      USING      (company_id = (SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id = (SELECT opstrax_security.current_tenant_id()));
  END IF;
END $$;

COMMIT;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== policies on telemetry_gateways (expect exactly 2: tenant_ticket_app + system_control_plane) =='
SELECT policyname, cmd, roles::text, permissive
FROM pg_policies
WHERE schemaname='public' AND tablename='telemetry_gateways'
ORDER BY policyname;

\echo ''
\echo '== column grants (expect t / t / f / f) =='
SELECT has_column_privilege('opstrax_app','telemetry_gateways','gateway_id','SELECT')      AS app_reads_gateway_id,
       has_column_privilege('opstrax_app','telemetry_gateways','status','UPDATE')          AS app_updates_status,
       has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','SELECT') AS app_reads_secret_MUST_BE_FALSE,
       has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','UPDATE') AS app_writes_secret_MUST_BE_FALSE;
