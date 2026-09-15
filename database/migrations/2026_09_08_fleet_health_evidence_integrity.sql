-- Fleet-health snapshots are customer-visible derived measurements. Existing
-- snapshots predate source qualification and remain retained but unverified.

ALTER TABLE public.fleet_health_snapshots
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_fleet_health_snapshot_evidence'
                   AND conrelid='public.fleet_health_snapshots'::regclass) THEN
        ALTER TABLE public.fleet_health_snapshots ADD CONSTRAINT ck_fleet_health_snapshot_evidence CHECK (
            (data_origin='runtime_computed' AND verification_status='calculated_from_qualified_sources')
            OR (data_origin='legacy_unverified' AND verification_status='unverified')
            OR (data_origin='demo_seed' AND verification_status='demo_seed')) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_fhs_company_evidence_date
    ON public.fleet_health_snapshots(company_id,data_origin,verification_status,snapshot_date DESC);
