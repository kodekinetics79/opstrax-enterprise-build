BEGIN;

ALTER TABLE public.driver_safety_scores
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

ALTER TABLE public.driver_safety_scores
    DROP CONSTRAINT IF EXISTS ck_driver_safety_scores_evidence;
ALTER TABLE public.driver_safety_scores
    ADD CONSTRAINT ck_driver_safety_scores_evidence CHECK (
        data_origin IN ('runtime_computed','legacy_unverified','demo_seed')
        AND verification_status IN ('calculated_from_qualified_sources','unverified','demo_seed')
    );

CREATE INDEX IF NOT EXISTS idx_driver_safety_scores_company_evidence
    ON public.driver_safety_scores(company_id,data_origin,verification_status,computed_at DESC);

COMMIT;
