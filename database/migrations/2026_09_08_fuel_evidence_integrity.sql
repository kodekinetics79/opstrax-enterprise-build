-- Fuel and idling evidence integrity.
-- Known Batch 5 demo rows remain available only to explicitly enabled demo environments.
-- Operational endpoints exclude them and expose the origin of all other records.

ALTER TABLE public.fuel_transactions
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL;

ALTER TABLE public.idling_events
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS cost_evidence_status VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;

ALTER TABLE public.fuel_anomalies
    ADD COLUMN IF NOT EXISTS currency VARCHAR(10) NULL,
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS amount_evidence_status VARCHAR(40) NULL;

UPDATE public.fuel_transactions
   SET data_origin = CASE
       WHEN transaction_number LIKE 'FT-B5-%' THEN 'demo_seed'
       ELSE COALESCE(data_origin, 'legacy_unverified') END
 WHERE data_origin IS NULL OR transaction_number LIKE 'FT-B5-%';

UPDATE public.idling_events
   SET data_origin = CASE
       WHEN event_number ~ '^IDLE-10[0-9]{2}$' THEN 'demo_seed'
       ELSE COALESCE(data_origin, 'legacy_unverified') END,
       cost_evidence_status = COALESCE(cost_evidence_status,
           CASE WHEN estimated_cost > 0 THEN 'Recorded estimate' ELSE 'Unavailable' END)
 WHERE data_origin IS NULL OR cost_evidence_status IS NULL
    OR event_number ~ '^IDLE-10[0-9]{2}$';

UPDATE public.fuel_anomalies
   SET data_origin = CASE
       WHEN description LIKE 'AI fuel advisor detected anomaly:%' THEN 'demo_seed'
       ELSE COALESCE(data_origin, 'legacy_unverified') END,
       amount_evidence_status = COALESCE(amount_evidence_status,
           CASE WHEN estimated_loss > 0 THEN 'Recorded estimate' ELSE 'Unavailable' END)
 WHERE data_origin IS NULL OR amount_evidence_status IS NULL
    OR description LIKE 'AI fuel advisor detected anomaly:%';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname='ck_fuel_transactions_recorded_amounts'
           AND conrelid='public.fuel_transactions'::regclass
    ) THEN
        ALTER TABLE public.fuel_transactions
            ADD CONSTRAINT ck_fuel_transactions_recorded_amounts
            CHECK (quantity > 0 AND unit_price >= 0 AND total_cost >= 0) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname='ck_idling_recorded_amounts'
           AND conrelid='public.idling_events'::regclass
    ) THEN
        ALTER TABLE public.idling_events
            ADD CONSTRAINT ck_idling_recorded_amounts
            CHECK (duration_minutes >= 0 AND estimated_fuel_burn >= 0 AND estimated_cost >= 0) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname='ck_fuel_anomaly_recorded_loss'
           AND conrelid='public.fuel_anomalies'::regclass
    ) THEN
        ALTER TABLE public.fuel_anomalies
            ADD CONSTRAINT ck_fuel_anomaly_recorded_loss
            CHECK (estimated_loss >= 0) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_fuel_transactions_operational
    ON public.fuel_transactions(company_id, fuel_date DESC, id DESC)
    WHERE deleted_at IS NULL AND data_origin <> 'demo_seed';

CREATE INDEX IF NOT EXISTS ix_idling_events_operational
    ON public.idling_events(company_id, started_at DESC, id DESC)
    WHERE deleted_at IS NULL AND data_origin <> 'demo_seed';

CREATE INDEX IF NOT EXISTS ix_fuel_anomalies_operational
    ON public.fuel_anomalies(company_id, status, created_at DESC)
    WHERE data_origin <> 'demo_seed';
