-- Stage 68 — Explicit tenant commercial access policy
--
-- Existing tenants retain legacy allow-on-absence behavior. Application-created
-- tenants explicitly select package_allowlist, where a missing entitlement denies.
-- This migration is additive/idempotent and is applied by the database owner before
-- the restricted Production runtime starts.
BEGIN;

ALTER TABLE companies
  ADD COLUMN IF NOT EXISTS entitlement_policy_mode VARCHAR(32) DEFAULT 'legacy_allow';

-- Repair a partially deployed nullable column without changing an explicit tenant
-- decision already present.
ALTER TABLE companies
  ALTER COLUMN entitlement_policy_mode SET DEFAULT 'legacy_allow';
UPDATE companies
SET entitlement_policy_mode='legacy_allow'
WHERE entitlement_policy_mode IS NULL;
ALTER TABLE companies
  ALTER COLUMN entitlement_policy_mode SET NOT NULL;

DO $stage68_policy_constraint$
BEGIN
  IF EXISTS (
    SELECT 1 FROM companies
    WHERE entitlement_policy_mode NOT IN ('legacy_allow','package_allowlist')
  ) THEN
    RAISE EXCEPTION 'Stage68 blocked: unknown entitlement policy mode requires reconciliation';
  END IF;
  -- Reconcile a partial deployment that created a same-named but weakened
  -- constraint. Catalog name alone is not proof of the commercial boundary.
  ALTER TABLE companies DROP CONSTRAINT IF EXISTS ck_companies_entitlement_policy_mode;
  ALTER TABLE companies
    ADD CONSTRAINT ck_companies_entitlement_policy_mode
    CHECK (entitlement_policy_mode IN ('legacy_allow','package_allowlist'));
END
$stage68_policy_constraint$;

COMMENT ON COLUMN companies.entitlement_policy_mode IS
  'Commercial module policy: legacy_allow permits missing entitlement rows; package_allowlist denies them.';

-- Version provenance for reconciliatory demo/pilot fixtures. Production never
-- exposes the seed endpoint, but materializing this contract out-of-band keeps
-- restricted staging/pilot runtimes DDL-free and lets the terminal RLS migration
-- apply the same tenant boundary as every other company-owned table.
CREATE TABLE IF NOT EXISTS demo_fixture_versions (
  company_id      BIGINT      NOT NULL,
  fixture_key     VARCHAR(80) NOT NULL,
  fixture_version INT         NOT NULL,
  applied_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY(company_id,fixture_key)
);

DO $stage68_fixture_contract$
BEGIN
  IF EXISTS (SELECT 1 FROM demo_fixture_versions WHERE fixture_version<=0) THEN
    RAISE EXCEPTION 'Stage68 blocked: demo fixture version must be positive';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='public.demo_fixture_versions'::regclass
      AND conname='ck_demo_fixture_version_positive'
  ) THEN
    ALTER TABLE demo_fixture_versions
      ADD CONSTRAINT ck_demo_fixture_version_positive CHECK(fixture_version>0);
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='public.demo_fixture_versions'::regclass
      AND conname='fk_demo_fixture_versions_company'
  ) THEN
    ALTER TABLE demo_fixture_versions
      ADD CONSTRAINT fk_demo_fixture_versions_company
      FOREIGN KEY(company_id) REFERENCES companies(id) ON DELETE CASCADE NOT VALID;
  END IF;
  ALTER TABLE demo_fixture_versions VALIDATE CONSTRAINT fk_demo_fixture_versions_company;
END
$stage68_fixture_contract$;

ALTER TABLE demo_fixture_versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE demo_fixture_versions FORCE ROW LEVEL SECURITY;

-- Before a first Stage58 cutover the runtime roles/functions may not exist yet;
-- Stage58 will install the policies and grants. On an already secured database,
-- make this migration independently safe before the terminal repair is replayed.
DO $stage68_fixture_rls$
DECLARE pol record;
BEGIN
  FOR pol IN SELECT policyname FROM pg_policies
    WHERE schemaname='public' AND tablename='demo_fixture_versions'
  LOOP
    EXECUTE format('DROP POLICY %I ON public.demo_fixture_versions',pol.policyname);
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON demo_fixture_versions
      FOR ALL TO opstrax_app
      USING(company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK(company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON demo_fixture_versions FROM opstrax_app;
    GRANT SELECT ON demo_fixture_versions TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON demo_fixture_versions
      FOR ALL TO opstrax_system USING(true) WITH CHECK(true);
    REVOKE ALL ON demo_fixture_versions FROM opstrax_system;
    GRANT SELECT,INSERT,UPDATE,DELETE ON demo_fixture_versions TO opstrax_system;
  END IF;
END
$stage68_fixture_rls$;

INSERT INTO schema_migrations(version,description)
VALUES (
  '2026_08_02_stage68_entitlement_policy_mode',
  'Explicit commercial access policy and versioned pilot fixture provenance'
)
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
