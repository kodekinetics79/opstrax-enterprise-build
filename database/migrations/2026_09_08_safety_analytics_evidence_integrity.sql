-- Safety analytics provenance. Existing and unspecified rows remain retained
-- but unqualified; authenticated workflows must opt in explicitly.

ALTER TABLE public.safety_events
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

ALTER TABLE public.coaching_tasks
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_safety_events_evidence'
                   AND conrelid='public.safety_events'::regclass) THEN
        ALTER TABLE public.safety_events ADD CONSTRAINT ck_safety_events_evidence CHECK (
            data_origin IN ('user_workflow','runtime_detection','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('recorded_by_authenticated_actor','derived_from_qualified_source','provider_verified','unverified','demo_seed')
        ) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_coaching_tasks_evidence'
                   AND conrelid='public.coaching_tasks'::regclass) THEN
        ALTER TABLE public.coaching_tasks ADD CONSTRAINT ck_coaching_tasks_evidence CHECK (
            data_origin IN ('user_workflow','runtime_detection','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('recorded_by_authenticated_actor','derived_from_qualified_source','provider_verified','unverified','demo_seed')
        ) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_safety_events_company_evidence_time
    ON public.safety_events(company_id,data_origin,verification_status,occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_coaching_tasks_company_evidence_status
    ON public.coaching_tasks(company_id,data_origin,verification_status,status,due_at);
