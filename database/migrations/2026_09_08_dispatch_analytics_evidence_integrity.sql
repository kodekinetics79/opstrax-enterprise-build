-- Dispatch analytics provenance. Existing and unspecified rows remain retained
-- but unqualified; authenticated workflow writes must opt in explicitly.

ALTER TABLE public.dispatch_assignments
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

ALTER TABLE public.dispatch_exceptions
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_dispatch_assignments_evidence'
                   AND conrelid='public.dispatch_assignments'::regclass) THEN
        ALTER TABLE public.dispatch_assignments ADD CONSTRAINT ck_dispatch_assignments_evidence CHECK (
            data_origin IN ('user_workflow','runtime_workflow','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('recorded_by_authenticated_actor','derived_from_qualified_workflow','provider_verified','unverified','demo_seed')
        ) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_dispatch_exceptions_evidence'
                   AND conrelid='public.dispatch_exceptions'::regclass) THEN
        ALTER TABLE public.dispatch_exceptions ADD CONSTRAINT ck_dispatch_exceptions_evidence CHECK (
            data_origin IN ('user_workflow','runtime_workflow','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('recorded_by_authenticated_actor','derived_from_qualified_workflow','provider_verified','unverified','demo_seed')
        ) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_dispatch_assignments_company_evidence_status
    ON public.dispatch_assignments(company_id,data_origin,verification_status,assignment_status,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_dispatch_exceptions_company_evidence_status
    ON public.dispatch_exceptions(company_id,data_origin,verification_status,status,created_at DESC);
