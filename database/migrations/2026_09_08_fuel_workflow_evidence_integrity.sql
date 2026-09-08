-- Fuel anomaly provenance and update-path integrity.
-- Existing detector-labelled rows remain unverified until regenerated from a
-- source-qualified transaction. The constraint applies to every new write.

ALTER TABLE public.fuel_anomalies
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

-- A missing assessment must remain missing. Historical default 20 was a
-- presentation placeholder, not recorded risk evidence.
ALTER TABLE public.idling_events
    ALTER COLUMN risk_score DROP DEFAULT,
    ALTER COLUMN risk_score DROP NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_fuel_anomaly_evidence'
                 AND conrelid='public.fuel_anomalies'::regclass) THEN
    ALTER TABLE public.fuel_anomalies ADD CONSTRAINT ck_fuel_anomaly_evidence CHECK (
      (data_origin='runtime_detector' AND verification_status='derived_from_qualified_transaction')
      OR (COALESCE(data_origin,'legacy_unverified')='legacy_unverified' AND verification_status='unverified')
      OR (data_origin='demo_seed' AND verification_status IN ('demo_seed','unverified'))) NOT VALID;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_fuel_anomalies_company_evidence
    ON public.fuel_anomalies(company_id,data_origin,verification_status,fuel_transaction_id,created_at DESC);
