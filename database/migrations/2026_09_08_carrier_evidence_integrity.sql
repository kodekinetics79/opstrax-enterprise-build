-- Carrier registry, compliance-document, and performance provenance.
-- Historical generated fixtures remain available only to explicit demo contexts.
-- Unclassified business rows remain visible as unverified records and are never
-- promoted to authority-verified compliance or job-derived performance evidence.

ALTER TABLE public.carriers
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS compliance_evidence_status VARCHAR(80) NULL;

ALTER TABLE public.carrier_documents
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS verified_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS verified_by BIGINT NULL;

ALTER TABLE public.carrier_performance
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS calculation_status VARCHAR(80) NULL;

UPDATE public.carriers
   SET data_origin=CASE
       WHEN company_id=1 AND (carrier_number LIKE 'CAR-B5-%'
         OR (mc_number ~ '^MC-9000[1-6]$' AND name IN
             ('Blue Ridge Carrier','Capital Freight','Dulles Express','Potomac Partner','NOVA Regional','Arlington Courier'))
         )
         THEN 'demo_seed'
       ELSE COALESCE(data_origin, 'legacy_unverified') END,
       compliance_evidence_status=CASE
       WHEN company_id=1 AND (carrier_number LIKE 'CAR-B5-%'
         OR (mc_number ~ '^MC-9000[1-6]$' AND name IN
             ('Blue Ridge Carrier','Capital Freight','Dulles Express','Potomac Partner','NOVA Regional','Arlington Courier'))
         )
         THEN 'demo_seed'
       ELSE COALESCE(compliance_evidence_status, 'unverified') END
 WHERE data_origin IS NULL OR compliance_evidence_status IS NULL
    OR (company_id=1 AND (carrier_number LIKE 'CAR-B5-%'
        OR (mc_number ~ '^MC-9000[1-6]$' AND name IN
            ('Blue Ridge Carrier','Capital Freight','Dulles Express','Potomac Partner','NOVA Regional','Arlington Courier'))));

UPDATE public.carrier_documents cd
   SET data_origin=CASE
       WHEN (cd.company_id=1 AND cd.document_number ~ '^DOC-CAR-00(0[1-9]|1[0-5])$')
         OR EXISTS (SELECT 1 FROM public.carriers ca WHERE ca.id=cd.carrier_id
                    AND ca.company_id=cd.company_id AND ca.data_origin='demo_seed') THEN 'demo_seed'
       ELSE COALESCE(cd.data_origin, 'legacy_unverified') END,
       verification_status=CASE
       WHEN (cd.company_id=1 AND cd.document_number ~ '^DOC-CAR-00(0[1-9]|1[0-5])$')
         OR EXISTS (SELECT 1 FROM public.carriers ca WHERE ca.id=cd.carrier_id
                    AND ca.company_id=cd.company_id AND ca.data_origin='demo_seed') THEN 'demo_seed'
       ELSE COALESCE(cd.verification_status, 'unverified') END
 WHERE cd.data_origin IS NULL OR cd.verification_status IS NULL
    OR (cd.company_id=1 AND cd.document_number ~ '^DOC-CAR-00(0[1-9]|1[0-5])$')
    OR EXISTS (SELECT 1 FROM public.carriers ca WHERE ca.id=cd.carrier_id
               AND ca.company_id=cd.company_id AND ca.data_origin='demo_seed');

UPDATE public.carrier_performance cp
   SET data_origin=CASE
       WHEN (cp.company_id=1 AND cp.expense_total BETWEEN 2801 AND 2810
             AND cp.jobs_handled=12+(cp.expense_total-2800)::INT)
         OR EXISTS (SELECT 1 FROM public.carriers ca WHERE ca.id=cp.carrier_id
                    AND ca.company_id=cp.company_id AND ca.data_origin='demo_seed') THEN 'demo_seed'
       ELSE COALESCE(cp.data_origin, 'legacy_unverified') END,
       calculation_status=CASE
       WHEN (cp.company_id=1 AND cp.expense_total BETWEEN 2801 AND 2810
             AND cp.jobs_handled=12+(cp.expense_total-2800)::INT)
         OR EXISTS (SELECT 1 FROM public.carriers ca WHERE ca.id=cp.carrier_id
                    AND ca.company_id=cp.company_id AND ca.data_origin='demo_seed') THEN 'demo_seed'
       ELSE COALESCE(cp.calculation_status, 'unverified') END
 WHERE cp.data_origin IS NULL OR cp.calculation_status IS NULL
    OR (cp.company_id=1 AND cp.expense_total BETWEEN 2801 AND 2810
        AND cp.jobs_handled=12+(cp.expense_total-2800)::INT)
    OR EXISTS (SELECT 1 FROM public.carriers ca WHERE ca.id=cp.carrier_id
               AND ca.company_id=cp.company_id AND ca.data_origin='demo_seed');

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_carriers_evidence_status'
                   AND conrelid='public.carriers'::regclass) THEN
        ALTER TABLE public.carriers ADD CONSTRAINT ck_carriers_evidence_status CHECK (
            data_origin IN ('manual_entry','provider_import','legacy_unverified','demo_seed')
            AND compliance_evidence_status IN ('unverified','document_recorded','authority_verified','provider_verified','demo_seed')
        ) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_carrier_documents_evidence_status'
                   AND conrelid='public.carrier_documents'::regclass) THEN
        ALTER TABLE public.carrier_documents ADD CONSTRAINT ck_carrier_documents_evidence_status CHECK (
            data_origin IN ('manual_entry','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('unverified','authority_verified','provider_verified','demo_seed')
        ) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_carrier_performance_evidence_status'
                   AND conrelid='public.carrier_performance'::regclass) THEN
        ALTER TABLE public.carrier_performance ADD CONSTRAINT ck_carrier_performance_evidence_status CHECK (
            data_origin IN ('job_derived','provider_import','legacy_unverified','demo_seed')
            AND calculation_status IN ('calculated_from_jobs','provider_reported','unverified','demo_seed')
        ) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_carriers_company_origin
    ON public.carriers(company_id, data_origin, status);
CREATE INDEX IF NOT EXISTS idx_carrier_documents_company_evidence
    ON public.carrier_documents(company_id, carrier_id, data_origin, verification_status, expiry_date);
CREATE INDEX IF NOT EXISTS idx_carrier_performance_company_evidence
    ON public.carrier_performance(company_id, carrier_id, data_origin, calculation_status, period_start DESC);
