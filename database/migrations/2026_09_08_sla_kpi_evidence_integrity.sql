-- KPI target and SLA measurement provenance.
-- Historical Batch 7 fixtures are retained only as demo evidence. Existing rows
-- without verifiable provenance remain legacy/unverified and are excluded from
-- customer KPI/SLA claims by the application query boundary.

ALTER TABLE public.kpi_metrics
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL;

ALTER TABLE public.kpi_targets
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NULL;

ALTER TABLE public.sla_records
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS measurement_evidence_status VARCHAR(80) NULL;

ALTER TABLE public.sla_breaches
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL;

UPDATE public.kpi_metrics
   SET data_origin=CASE WHEN tenant_id=1 AND id BETWEEN 1 AND 30
          AND kpi_code IN ('OTD','SLA-COMP','ETA-ACC','JOBS-COMP','DELAYED-JOBS','DISPATCH-READ','VEH-UTIL','DRV-UTIL','SAFETY-SCORE','MAINT-COMP','DVIR-COMP','FUEL-EFF','IDLE-COST','CPM','GROSS-MARGIN','CX-SCORE','FLEET-READY','HOS-COMPLY','ELD-HEALTH','DOC-VALID','MAINT-COST','WORK-ORDER-COMP','CARRIER-PERF','INCIDENT-RATE','PROOF-COMP','CUSTOMER-SLA-MET','COST-LEAKAGE','AUDIT-COVERAGE','DISPATCH-CYCLE','ROUTE-EFF')
        THEN 'demo_seed' ELSE COALESCE(data_origin,'legacy_unverified') END
 WHERE data_origin IS NULL OR (tenant_id=1 AND id BETWEEN 1 AND 30
       AND kpi_code IN ('OTD','SLA-COMP','ETA-ACC','JOBS-COMP','DELAYED-JOBS','DISPATCH-READ','VEH-UTIL','DRV-UTIL','SAFETY-SCORE','MAINT-COMP','DVIR-COMP','FUEL-EFF','IDLE-COST','CPM','GROSS-MARGIN','CX-SCORE','FLEET-READY','HOS-COMPLY','ELD-HEALTH','DOC-VALID','MAINT-COST','WORK-ORDER-COMP','CARRIER-PERF','INCIDENT-RATE','PROOF-COMP','CUSTOMER-SLA-MET','COST-LEAKAGE','AUDIT-COVERAGE','DISPATCH-CYCLE','ROUTE-EFF'));

UPDATE public.kpi_targets
   SET data_origin=CASE WHEN tenant_id=1 AND id BETWEEN 1 AND 20
          AND effective_date=DATE '2026-01-01'
          AND kpi_code IN ('OTD','SLA-COMP','ETA-ACC','JOBS-COMP','DELAYED-JOBS','DISPATCH-READ','VEH-UTIL','DRV-UTIL','SAFETY-SCORE','MAINT-COMP','DVIR-COMP','FUEL-EFF','IDLE-COST','CPM','GROSS-MARGIN','CX-SCORE','FLEET-READY','HOS-COMPLY','INCIDENT-RATE','COST-LEAKAGE')
        THEN 'demo_seed' ELSE COALESCE(data_origin,'legacy_unverified') END,
       verification_status=CASE WHEN tenant_id=1 AND id BETWEEN 1 AND 20
          AND effective_date=DATE '2026-01-01'
          AND kpi_code IN ('OTD','SLA-COMP','ETA-ACC','JOBS-COMP','DELAYED-JOBS','DISPATCH-READ','VEH-UTIL','DRV-UTIL','SAFETY-SCORE','MAINT-COMP','DVIR-COMP','FUEL-EFF','IDLE-COST','CPM','GROSS-MARGIN','CX-SCORE','FLEET-READY','HOS-COMPLY','INCIDENT-RATE','COST-LEAKAGE')
        THEN 'demo_seed' ELSE COALESCE(verification_status,'unverified') END
 WHERE data_origin IS NULL OR verification_status IS NULL
    OR (tenant_id=1 AND id BETWEEN 1 AND 20 AND effective_date=DATE '2026-01-01'
        AND kpi_code IN ('OTD','SLA-COMP','ETA-ACC','JOBS-COMP','DELAYED-JOBS','DISPATCH-READ','VEH-UTIL','DRV-UTIL','SAFETY-SCORE','MAINT-COMP','DVIR-COMP','FUEL-EFF','IDLE-COST','CPM','GROSS-MARGIN','CX-SCORE','FLEET-READY','HOS-COMPLY','INCIDENT-RATE','COST-LEAKAGE'));

UPDATE public.sla_records
   SET data_origin=CASE WHEN tenant_id=1 AND company_id=1 AND id BETWEEN 1 AND 30
          AND sla_number ~ '^SLA-0(0[1-9]|[12][0-9]|30)$'
        THEN 'demo_seed' ELSE COALESCE(data_origin,'legacy_unverified') END,
       measurement_evidence_status=CASE WHEN tenant_id=1 AND company_id=1 AND id BETWEEN 1 AND 30
          AND sla_number ~ '^SLA-0(0[1-9]|[12][0-9]|30)$'
        THEN 'demo_seed' ELSE COALESCE(measurement_evidence_status,'unverified') END
 WHERE data_origin IS NULL OR measurement_evidence_status IS NULL
    OR (tenant_id=1 AND company_id=1 AND id BETWEEN 1 AND 30
        AND sla_number ~ '^SLA-0(0[1-9]|[12][0-9]|30)$');

UPDATE public.sla_breaches sb
   SET data_origin=CASE WHEN EXISTS (
          SELECT 1 FROM public.sla_records sr
           WHERE sr.id=sb.sla_record_id AND sr.tenant_id=sb.tenant_id
             AND sr.data_origin='demo_seed')
        THEN 'demo_seed' ELSE COALESCE(sb.data_origin,'legacy_unverified') END
 WHERE sb.data_origin IS NULL OR EXISTS (
       SELECT 1 FROM public.sla_records sr
        WHERE sr.id=sb.sla_record_id AND sr.tenant_id=sb.tenant_id
          AND sr.data_origin='demo_seed');

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_kpi_metrics_data_origin'
                   AND conrelid='public.kpi_metrics'::regclass) THEN
        ALTER TABLE public.kpi_metrics ADD CONSTRAINT ck_kpi_metrics_data_origin CHECK (
            data_origin IN ('runtime_computed','manual_entry','provider_import','legacy_unverified','demo_seed')
        ) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_kpi_targets_evidence'
                   AND conrelid='public.kpi_targets'::regclass) THEN
        ALTER TABLE public.kpi_targets ADD CONSTRAINT ck_kpi_targets_evidence CHECK (
            data_origin IN ('manual_entry','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('verified','unverified','demo_seed')
        ) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_sla_records_evidence'
                   AND conrelid='public.sla_records'::regclass) THEN
        ALTER TABLE public.sla_records ADD CONSTRAINT ck_sla_records_evidence CHECK (
            data_origin IN ('job_derived','provider_import','manual_entry','legacy_unverified','demo_seed')
            AND measurement_evidence_status IN ('calculated_from_events','provider_verified','manual_verified','unverified','demo_seed')
        ) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_sla_breaches_data_origin'
                   AND conrelid='public.sla_breaches'::regclass) THEN
        ALTER TABLE public.sla_breaches ADD CONSTRAINT ck_sla_breaches_data_origin CHECK (
            data_origin IN ('derived_from_sla','provider_import','manual_entry','legacy_unverified','demo_seed')
        ) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_kpi_targets_tenant_evidence
    ON public.kpi_targets(tenant_id,data_origin,verification_status,effective_date DESC);
CREATE INDEX IF NOT EXISTS idx_sla_records_tenant_evidence
    ON public.sla_records(tenant_id,company_id,data_origin,measurement_evidence_status,measured_at DESC);
CREATE INDEX IF NOT EXISTS idx_sla_breaches_tenant_evidence
    ON public.sla_breaches(tenant_id,data_origin,status,detected_at DESC);
