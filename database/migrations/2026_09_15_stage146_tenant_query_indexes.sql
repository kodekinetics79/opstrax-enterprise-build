-- Stage 146 — tenant-leading indexes for high-frequency operational reads
--
-- Stage88 disabled runtime schema DDL in protected environments. These indexes
-- therefore belong in the owner migration chain even when matching indexes are
-- also declared by pre-Stage88 schema services for local/development databases.

BEGIN;

DO $preflight$
BEGIN
  IF to_regclass('public.expenses') IS NULL
     OR to_regclass('public.documents') IS NULL
     OR to_regclass('public.vehicle_documents') IS NULL
     OR to_regclass('public.driver_documents') IS NULL THEN
    RAISE EXCEPTION 'Stage146 requires expenses and document registry tables';
  END IF;
END
$preflight$;

CREATE INDEX IF NOT EXISTS ix_expenses_company_approval_date
  ON public.expenses(company_id, approval_status, expense_date DESC)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_documents_company_status_expiry
  ON public.documents(company_id, status, expires_at)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_vehicle_documents_company_vehicle_expiry
  ON public.vehicle_documents(company_id, vehicle_id, expiry_date);

CREATE INDEX IF NOT EXISTS ix_driver_documents_company_driver_expiry
  ON public.driver_documents(company_id, driver_id, expiry_date);

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_15_stage146_tenant_query_indexes','Tenant-leading indexes for expense and document reads')
ON CONFLICT(version) DO NOTHING;

COMMIT;
