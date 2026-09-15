-- Executive snapshot provenance. Historical Batch 7 scorecards remain retained
-- as demo data but cannot appear in tenant executive summaries.

ALTER TABLE public.executive_snapshots
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NULL;

UPDATE public.executive_snapshots
   SET data_origin=CASE WHEN tenant_id=1 AND id BETWEEN 1 AND 10
          AND snapshot_date BETWEEN DATE '2026-05-15' AND DATE '2026-05-24'
        THEN 'demo_seed' ELSE COALESCE(data_origin,'legacy_unverified') END,
       verification_status=CASE WHEN tenant_id=1 AND id BETWEEN 1 AND 10
          AND snapshot_date BETWEEN DATE '2026-05-15' AND DATE '2026-05-24'
        THEN 'demo_seed' ELSE COALESCE(verification_status,'unverified') END
 WHERE data_origin IS NULL OR verification_status IS NULL
    OR (tenant_id=1 AND id BETWEEN 1 AND 10
        AND snapshot_date BETWEEN DATE '2026-05-15' AND DATE '2026-05-24');

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_executive_snapshots_evidence'
                   AND conrelid='public.executive_snapshots'::regclass) THEN
        ALTER TABLE public.executive_snapshots ADD CONSTRAINT ck_executive_snapshots_evidence CHECK (
            data_origin IN ('runtime_computed','manual_entry','provider_import','legacy_unverified','demo_seed')
            AND verification_status IN ('calculated_from_qualified_sources','manual_verified','provider_verified','unverified','demo_seed')
        ) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_executive_snapshots_tenant_evidence
    ON public.executive_snapshots(tenant_id,data_origin,verification_status,snapshot_date DESC);
