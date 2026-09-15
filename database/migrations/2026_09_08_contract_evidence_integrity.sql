-- Contract and legacy rate provenance.
-- Known generated fixtures are retained for explicit demo tenants but are excluded
-- from operational contract surfaces. Existing unclassified rows remain visible
-- with an unverified-origin label rather than being promoted to commercial evidence.

ALTER TABLE public.contracts
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

ALTER TABLE public.contract_rates
    ADD COLUMN IF NOT EXISTS currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

UPDATE public.contracts
   SET data_origin = CASE
       WHEN contract_code LIKE 'CON-B5-%'
         OR contract_number LIKE 'CON-B5-%'
         OR (contract_code ~ '^CTR-[0-9]{3}$' AND title LIKE '% Master Transport Agreement')
         THEN 'demo_seed'
       ELSE COALESCE(data_origin, 'legacy_unverified') END
 WHERE data_origin IS NULL
    OR contract_code LIKE 'CON-B5-%'
    OR contract_number LIKE 'CON-B5-%'
    OR (contract_code ~ '^CTR-[0-9]{3}$' AND title LIKE '% Master Transport Agreement');

UPDATE public.contract_rates cr
   SET data_origin = CASE
       WHEN EXISTS (
           SELECT 1 FROM public.contracts con
            WHERE con.id=cr.contract_id
              AND con.company_id=cr.company_id
              AND con.data_origin='demo_seed') THEN 'demo_seed'
       ELSE COALESCE(cr.data_origin, 'legacy_unverified') END
 WHERE cr.data_origin IS NULL
    OR EXISTS (
        SELECT 1 FROM public.contracts con
         WHERE con.id=cr.contract_id
           AND con.company_id=cr.company_id
           AND con.data_origin='demo_seed');

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname='ck_contracts_data_origin'
           AND conrelid='public.contracts'::regclass
    ) THEN
        ALTER TABLE public.contracts
            ADD CONSTRAINT ck_contracts_data_origin
            CHECK (data_origin IN ('manual_entry','provider_import','legacy_unverified','demo_seed')) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname='ck_contract_rates_evidence_fields'
           AND conrelid='public.contract_rates'::regclass
    ) THEN
        ALTER TABLE public.contract_rates
            ADD CONSTRAINT ck_contract_rates_evidence_fields
            CHECK (
                data_origin IN ('manual_entry','provider_import','legacy_unverified','demo_seed')
                AND currency ~ '^[A-Z]{3}$'
                AND base_rate >= 0
                AND (minimum_charge IS NULL OR minimum_charge >= 0)
                AND (fuel_surcharge_percent IS NULL OR fuel_surcharge_percent BETWEEN 0 AND 100)
            ) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname='ck_contracts_recorded_terms'
           AND conrelid='public.contracts'::regclass
    ) THEN
        ALTER TABLE public.contracts
            ADD CONSTRAINT ck_contracts_recorded_terms
            CHECK (
                currency ~ '^[A-Z]{3}$'
                AND base_rate >= 0
                AND (fuel_surcharge_percent IS NULL OR fuel_surcharge_percent BETWEEN 0 AND 100)
                AND (effective_date IS NULL OR expiry_date IS NULL OR expiry_date > effective_date)
            ) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_contracts_company_origin
    ON public.contracts(company_id, data_origin, status, expiry_date);

CREATE INDEX IF NOT EXISTS idx_contract_rates_company_origin
    ON public.contract_rates(company_id, contract_id, data_origin, status);
