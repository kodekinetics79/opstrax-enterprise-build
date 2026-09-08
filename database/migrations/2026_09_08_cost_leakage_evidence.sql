-- Cost leakage evidence integrity.
-- Legacy demo rows remain in storage for explicit demo environments, but operational
-- reads select only runtime detector records (RLK-* with data_origin=runtime_detector).

ALTER TABLE public.cost_leakage_items
    ADD COLUMN IF NOT EXISTS currency VARCHAR(10) NULL,
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS amount_evidence_status VARCHAR(40) NULL;

UPDATE public.cost_leakage_items
   SET data_origin = 'runtime_detector',
       amount_evidence_status = CASE WHEN estimated_loss > 0 THEN 'Recorded' ELSE 'Unavailable' END
 WHERE leakage_number LIKE 'RLK-%'
   AND data_origin IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_cost_leakage_currency'
           AND conrelid = 'public.cost_leakage_items'::regclass
    ) THEN
        ALTER TABLE public.cost_leakage_items
            ADD CONSTRAINT ck_cost_leakage_currency
            CHECK (currency IS NULL OR currency ~ '^[A-Z]{3}$') NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_cost_leakage_nonnegative_amounts'
           AND conrelid = 'public.cost_leakage_items'::regclass
    ) THEN
        ALTER TABLE public.cost_leakage_items
            ADD CONSTRAINT ck_cost_leakage_nonnegative_amounts
            CHECK (estimated_loss >= 0 AND projected_monthly_loss >= 0) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_cost_leakage_action_nonnegative_savings'
           AND conrelid = 'public.cost_leakage_actions'::regclass
    ) THEN
        ALTER TABLE public.cost_leakage_actions
            ADD CONSTRAINT ck_cost_leakage_action_nonnegative_savings
            CHECK (estimated_savings >= 0) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_cost_leakage_runtime_queue
    ON public.cost_leakage_items(company_id, status, created_at DESC)
    WHERE leakage_number LIKE 'RLK-%' AND deleted_at IS NULL;
