-- Stage 102 — Canada/KSA HOS policy + shadow calculation evidence
--
-- This stage creates configuration/evidence structures required to run the new
-- deterministic calculator without overwriting the legacy/demo `hos_clocks`.
-- Shadow mode is intentional: regulatory SMEs and real provider/device evidence
-- must accept the calculation boundary before any shadow result becomes a
-- production dispatch/ELD compliance authority.
--
-- Dependency: Stage101 Canada/KSA regulatory baseline.

BEGIN;

DO $preflight$
BEGIN
  IF to_regclass('public.drivers') IS NULL
     OR to_regclass('public.companies') IS NULL
     OR to_regclass('public.hos_logs') IS NULL
     OR to_regclass('public.compliance_profiles') IS NULL THEN
    RAISE EXCEPTION 'Stage102 requires drivers, companies, hos_logs and compliance_profiles';
  END IF;
END
$preflight$;

-- ---------------------------------------------------------------------------
-- Driver policy assignment
-- ---------------------------------------------------------------------------
-- Canada defines a driver's "day" as the carrier-designated 24-hour period.
-- Saudi weekly calculations/extension counts also require an explicit carrier
-- week anchor. These must never be guessed from the API server timezone.
CREATE TABLE IF NOT EXISTS driver_hos_policy_assignments (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  branch_id BIGINT NULL,
  driver_id BIGINT NOT NULL REFERENCES drivers(id) ON DELETE CASCADE,
  jurisdiction_code VARCHAR(8) NOT NULL,
  rule_profile_code VARCHAR(96) NOT NULL,
  cycle_type VARCHAR(40) NOT NULL,
  timezone VARCHAR(80) NOT NULL,
  day_start_local TIME NOT NULL,
  week_start_iso SMALLINT NULL,
  effective_from TIMESTAMPTZ NOT NULL,
  effective_to TIMESTAMPTZ NULL,
  source VARCHAR(40) NOT NULL DEFAULT 'manual',
  source_reference VARCHAR(160) NULL,
  review_status VARCHAR(24) NOT NULL DEFAULT 'Draft',
  reviewed_by BIGINT NULL,
  reviewed_at TIMESTAMPTZ NULL,
  notes TEXT NULL,
  row_version BIGINT NOT NULL DEFAULT 1,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT ck_hos_policy_jurisdiction CHECK (jurisdiction_code IN ('CA','SA')),
  CONSTRAINT ck_hos_policy_profile CHECK (
    (jurisdiction_code='CA' AND rule_profile_code IN (
      'CA-S60-SOR-2005-313-2026-06-21',
      'CA-N60-SOR-2005-313-2026-06-21'
    )) OR
    (jurisdiction_code='SA' AND rule_profile_code='SA-TGA-GOODS-2026-09-03')
  ),
  CONSTRAINT ck_hos_policy_cycle CHECK (
    (jurisdiction_code='CA' AND cycle_type IN ('Cycle 1','Cycle 2')) OR
    (jurisdiction_code='SA' AND cycle_type='TGA')
  ),
  CONSTRAINT ck_hos_policy_week_anchor CHECK (
    (jurisdiction_code='CA' AND week_start_iso IS NULL) OR
    (jurisdiction_code='SA' AND week_start_iso BETWEEN 1 AND 7)
  ),
  CONSTRAINT ck_hos_policy_effective_range CHECK (effective_to IS NULL OR effective_to > effective_from),
  CONSTRAINT ck_hos_policy_review_status CHECK (review_status IN ('Draft','Reviewed','Approved','Suspended','Expired'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_driver_hos_policy_current
  ON driver_hos_policy_assignments(company_id,driver_id)
  WHERE effective_to IS NULL;
CREATE INDEX IF NOT EXISTS ix_driver_hos_policy_effective
  ON driver_hos_policy_assignments(company_id,driver_id,effective_from DESC,effective_to);
CREATE INDEX IF NOT EXISTS ix_driver_hos_policy_branch
  ON driver_hos_policy_assignments(company_id,branch_id,driver_id);

-- ---------------------------------------------------------------------------
-- Evidence-backed exception authorizations
-- ---------------------------------------------------------------------------
-- The calculator never auto-grants a Saudi 10-hour extension or Canadian
-- deferral. These records carry the explicit authorization/evidence input.
CREATE TABLE IF NOT EXISTS hos_exception_authorizations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  branch_id BIGINT NULL,
  driver_id BIGINT NOT NULL REFERENCES drivers(id) ON DELETE CASCADE,
  jurisdiction_code VARCHAR(8) NOT NULL,
  exception_type VARCHAR(64) NOT NULL,
  local_date DATE NOT NULL,
  authorized BOOLEAN NOT NULL DEFAULT FALSE,
  reason TEXT NULL,
  evidence_reference VARCHAR(240) NULL,
  approved_by BIGINT NULL,
  approved_at TIMESTAMPTZ NULL,
  revoked_at TIMESTAMPTZ NULL,
  source VARCHAR(40) NOT NULL DEFAULT 'manual',
  row_version BIGINT NOT NULL DEFAULT 1,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT ck_hos_exception_jurisdiction CHECK (jurisdiction_code IN ('CA','SA')),
  CONSTRAINT ck_hos_exception_type CHECK (exception_type IN (
    'CA_DAILY_OFF_DUTY_DEFERRAL',
    'CA_ADVERSE_DRIVING',
    'CA_PERSONAL_CONVEYANCE_VALIDATED',
    'SA_DAILY_10H_EXTENSION'
  )),
  CONSTRAINT ck_hos_exception_evidence CHECK (
    authorized=FALSE OR (approved_at IS NOT NULL AND evidence_reference IS NOT NULL AND length(trim(evidence_reference))>0)
  )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_hos_exception_active
  ON hos_exception_authorizations(company_id,driver_id,local_date,exception_type)
  WHERE revoked_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_hos_exception_driver_date
  ON hos_exception_authorizations(company_id,driver_id,local_date DESC);

-- ---------------------------------------------------------------------------
-- Regulated source provenance on HOS records
-- ---------------------------------------------------------------------------
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS source_provider VARCHAR(80) NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS source_device_identifier VARCHAR(120) NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS source_received_at TIMESTAMPTZ NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS source_payload_sha256 CHAR(64) NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS source_sequence VARCHAR(120) NULL;
ALTER TABLE hos_logs ADD COLUMN IF NOT EXISTS provenance_verified BOOLEAN NOT NULL DEFAULT FALSE;

DO $hos_log_constraints$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_hos_log_source_sha256') THEN
    ALTER TABLE hos_logs ADD CONSTRAINT ck_hos_log_source_sha256
      CHECK (source_payload_sha256 IS NULL OR source_payload_sha256 ~ '^[0-9a-fA-F]{64}$');
  END IF;
END
$hos_log_constraints$;

CREATE INDEX IF NOT EXISTS ix_hos_logs_source_lineage
  ON hos_logs(company_id,driver_id,source_provider,source_event_id,source_received_at DESC)
  WHERE deleted_at IS NULL;

-- ---------------------------------------------------------------------------
-- Append-only shadow clock snapshots
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS hos_shadow_clock_snapshots (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  branch_id BIGINT NULL,
  driver_id BIGINT NOT NULL REFERENCES drivers(id) ON DELETE CASCADE,
  policy_assignment_id BIGINT NOT NULL REFERENCES driver_hos_policy_assignments(id),
  rule_profile_code VARCHAR(96) NOT NULL,
  algorithm_version VARCHAR(64) NOT NULL,
  calculated_at TIMESTAMPTZ NOT NULL,
  coverage_start TIMESTAMPTZ NOT NULL,
  day_start TIMESTAMPTZ NOT NULL,
  week_start TIMESTAMPTZ NULL,
  drive_remaining_minutes INTEGER NOT NULL,
  shift_remaining_minutes INTEGER NULL,
  cycle_remaining_minutes INTEGER NOT NULL,
  break_remaining_minutes INTEGER NULL,
  data_complete BOOLEAN NOT NULL,
  review_required BOOLEAN NOT NULL,
  can_drive BOOLEAN NOT NULL,
  status VARCHAR(24) NOT NULL,
  violations JSONB NOT NULL DEFAULT '[]'::jsonb,
  driving_blocks JSONB NOT NULL DEFAULT '[]'::jsonb,
  review_flags JSONB NOT NULL DEFAULT '[]'::jsonb,
  metrics JSONB NOT NULL DEFAULT '{}'::jsonb,
  source_event_count INTEGER NOT NULL,
  source_max_event_time TIMESTAMPTZ NULL,
  source_watermark VARCHAR(160) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT ck_hos_shadow_status CHECK (status IN ('OK','Warning','Blocked','Violation','Unverified')),
  CONSTRAINT ck_hos_shadow_minutes CHECK (
    drive_remaining_minutes>=0 AND cycle_remaining_minutes>=0
    AND (shift_remaining_minutes IS NULL OR shift_remaining_minutes>=0)
    AND (break_remaining_minutes IS NULL OR break_remaining_minutes>=0)
  ),
  CONSTRAINT ck_hos_shadow_truth CHECK (can_drive=FALSE OR (data_complete=TRUE AND review_required=FALSE AND status IN ('OK','Warning')))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_hos_shadow_idempotent
  ON hos_shadow_clock_snapshots(company_id,driver_id,policy_assignment_id,algorithm_version,source_watermark);
CREATE INDEX IF NOT EXISTS ix_hos_shadow_latest
  ON hos_shadow_clock_snapshots(company_id,driver_id,calculated_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_hos_shadow_blocked
  ON hos_shadow_clock_snapshots(company_id,status,calculated_at DESC)
  WHERE status IN ('Blocked','Violation','Unverified');

-- ---------------------------------------------------------------------------
-- Tenant isolation / runtime grants
-- ---------------------------------------------------------------------------
-- Stage102 runs after the established RLS cutover, so these newly-created
-- company-scoped tables must enroll themselves immediately. Relying on an older
-- one-time reconciliation pass would leave a protected-environment gap.
ALTER TABLE driver_hos_policy_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE driver_hos_policy_assignments FORCE ROW LEVEL SECURITY;
ALTER TABLE hos_exception_authorizations ENABLE ROW LEVEL SECURITY;
ALTER TABLE hos_exception_authorizations FORCE ROW LEVEL SECURITY;
ALTER TABLE hos_shadow_clock_snapshots ENABLE ROW LEVEL SECURITY;
ALTER TABLE hos_shadow_clock_snapshots FORCE ROW LEVEL SECURITY;

REVOKE ALL ON TABLE driver_hos_policy_assignments FROM PUBLIC;
REVOKE ALL ON TABLE hos_exception_authorizations FROM PUBLIC;
REVOKE ALL ON TABLE hos_shadow_clock_snapshots FROM PUBLIC;

DO $runtime_rls$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    DROP POLICY IF EXISTS tenant_ticket_app ON driver_hos_policy_assignments;
    CREATE POLICY tenant_ticket_app ON driver_hos_policy_assignments
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));

    DROP POLICY IF EXISTS tenant_ticket_app ON hos_exception_authorizations;
    CREATE POLICY tenant_ticket_app ON hos_exception_authorizations
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));

    DROP POLICY IF EXISTS tenant_ticket_app ON hos_shadow_clock_snapshots;
    CREATE POLICY tenant_ticket_app ON hos_shadow_clock_snapshots
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));

    REVOKE ALL ON TABLE driver_hos_policy_assignments FROM opstrax_app;
    REVOKE ALL ON TABLE hos_exception_authorizations FROM opstrax_app;
    REVOKE ALL ON TABLE hos_shadow_clock_snapshots FROM opstrax_app;
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE driver_hos_policy_assignments TO opstrax_app;
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE hos_exception_authorizations TO opstrax_app;
    -- Shadow evidence is append-only. The DB trigger also rejects UPDATE/DELETE,
    -- but withholding those privileges reduces the runtime attack surface.
    GRANT SELECT,INSERT ON TABLE hos_shadow_clock_snapshots TO opstrax_app;

    GRANT USAGE,SELECT ON SEQUENCE driver_hos_policy_assignments_id_seq TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE hos_exception_authorizations_id_seq TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE hos_shadow_clock_snapshots_id_seq TO opstrax_app;
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    DROP POLICY IF EXISTS system_control_plane ON driver_hos_policy_assignments;
    CREATE POLICY system_control_plane ON driver_hos_policy_assignments
      AS PERMISSIVE FOR ALL TO opstrax_system USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS system_control_plane ON hos_exception_authorizations;
    CREATE POLICY system_control_plane ON hos_exception_authorizations
      AS PERMISSIVE FOR ALL TO opstrax_system USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS system_control_plane ON hos_shadow_clock_snapshots;
    CREATE POLICY system_control_plane ON hos_shadow_clock_snapshots
      AS PERMISSIVE FOR ALL TO opstrax_system USING (true) WITH CHECK (true);

    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE driver_hos_policy_assignments TO opstrax_system;
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE hos_exception_authorizations TO opstrax_system;
    GRANT SELECT,INSERT ON TABLE hos_shadow_clock_snapshots TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE driver_hos_policy_assignments_id_seq TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE hos_exception_authorizations_id_seq TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE hos_shadow_clock_snapshots_id_seq TO opstrax_system;
  END IF;
END
$runtime_rls$;

-- ---------------------------------------------------------------------------
-- Append-only protection. Calculations are evidence snapshots, not mutable state.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION prevent_hos_shadow_snapshot_mutation()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'hos_shadow_clock_snapshots are append-only evidence';
END;
$$;

DROP TRIGGER IF EXISTS trg_hos_shadow_no_update ON hos_shadow_clock_snapshots;
CREATE TRIGGER trg_hos_shadow_no_update
  BEFORE UPDATE OR DELETE ON hos_shadow_clock_snapshots
  FOR EACH ROW EXECUTE FUNCTION prevent_hos_shadow_snapshot_mutation();

-- ---------------------------------------------------------------------------
-- Postconditions
-- ---------------------------------------------------------------------------
DO $postcondition$
DECLARE
  isolated_count INTEGER;
BEGIN
  IF to_regclass('public.driver_hos_policy_assignments') IS NULL
     OR to_regclass('public.hos_exception_authorizations') IS NULL
     OR to_regclass('public.hos_shadow_clock_snapshots') IS NULL THEN
    RAISE EXCEPTION 'Stage102 failed: required HOS policy/shadow tables missing';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname='uq_driver_hos_policy_current') THEN
    RAISE EXCEPTION 'Stage102 failed: current-policy uniqueness missing';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_hos_shadow_no_update' AND NOT tgisinternal) THEN
    RAISE EXCEPTION 'Stage102 failed: shadow snapshot append-only trigger missing';
  END IF;

  SELECT COUNT(*) INTO isolated_count
  FROM pg_class c
  JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='public'
    AND c.relname IN ('driver_hos_policy_assignments','hos_exception_authorizations','hos_shadow_clock_snapshots')
    AND c.relrowsecurity AND c.relforcerowsecurity;
  IF isolated_count <> 3 THEN
    RAISE EXCEPTION 'Stage102 failed: all three HOS evidence tables must FORCE RLS';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL
     AND (SELECT COUNT(*) FROM pg_policies
          WHERE schemaname='public'
            AND tablename IN ('driver_hos_policy_assignments','hos_exception_authorizations','hos_shadow_clock_snapshots')
            AND policyname='tenant_ticket_app') <> 3 THEN
    RAISE EXCEPTION 'Stage102 failed: application tenant-ticket policies incomplete';
  END IF;
END
$postcondition$;

COMMIT;
