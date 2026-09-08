-- Maintenance analytics provenance. Existing and unspecified rows remain
-- retained but unqualified; authenticated and source-derived workflows opt in.

ALTER TABLE public.maintenance_items
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';
ALTER TABLE public.work_orders
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';
ALTER TABLE public.dvir_reports
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';
ALTER TABLE public.dvir_defects
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_maintenance_items_evidence' AND conrelid='public.maintenance_items'::regclass) THEN
        ALTER TABLE public.maintenance_items ADD CONSTRAINT ck_maintenance_items_evidence CHECK (
            data_origin IN ('user_workflow','runtime_pm','legacy_unverified','demo_seed')
            AND verification_status IN ('recorded_by_authenticated_actor','derived_from_qualified_source','unverified','demo_seed')) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_work_orders_evidence' AND conrelid='public.work_orders'::regclass) THEN
        ALTER TABLE public.work_orders ADD CONSTRAINT ck_work_orders_evidence CHECK (
            data_origin IN ('user_workflow','workflow_derived','legacy_unverified','demo_seed')
            AND verification_status IN ('recorded_by_authenticated_actor','derived_from_qualified_source','unverified','demo_seed')) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_dvir_reports_evidence' AND conrelid='public.dvir_reports'::regclass) THEN
        ALTER TABLE public.dvir_reports ADD CONSTRAINT ck_dvir_reports_evidence CHECK (
            data_origin IN ('user_workflow','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('recorded_by_authenticated_actor','provider_verified','unverified','demo_seed')) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_dvir_defects_evidence' AND conrelid='public.dvir_defects'::regclass) THEN
        ALTER TABLE public.dvir_defects ADD CONSTRAINT ck_dvir_defects_evidence CHECK (
            data_origin IN ('dvir_workflow','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('derived_from_qualified_source','provider_verified','unverified','demo_seed')) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_maintenance_items_company_evidence_due ON public.maintenance_items(company_id,data_origin,verification_status,status,due_date);
CREATE INDEX IF NOT EXISTS idx_work_orders_company_evidence_status ON public.work_orders(company_id,data_origin,verification_status,status);
CREATE INDEX IF NOT EXISTS idx_dvir_reports_company_evidence_time ON public.dvir_reports(company_id,data_origin,verification_status,submitted_at DESC);
CREATE INDEX IF NOT EXISTS idx_dvir_defects_company_evidence_status ON public.dvir_defects(company_id,data_origin,verification_status,status,severity);
