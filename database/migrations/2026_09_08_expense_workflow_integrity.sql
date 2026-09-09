-- Expense workflow integrity guardrails.
-- NOT VALID preserves inspectable historical rows while enforcing these rules for
-- every new or changed record. Legacy EXP-B5 rows remain visibly classified as
-- demo data by the API and UI until a tenant explicitly removes them.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_expenses_positive_amount'
           AND conrelid = 'public.expenses'::regclass
    ) THEN
        ALTER TABLE public.expenses
            ADD CONSTRAINT ck_expenses_positive_amount CHECK (amount > 0) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_expenses_currency_code'
           AND conrelid = 'public.expenses'::regclass
    ) THEN
        ALTER TABLE public.expenses
            ADD CONSTRAINT ck_expenses_currency_code
            CHECK (currency ~ '^[A-Z]{3}$') NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_expenses_approval_status'
           AND conrelid = 'public.expenses'::regclass
    ) THEN
        ALTER TABLE public.expenses
            ADD CONSTRAINT ck_expenses_approval_status
            CHECK (approval_status IN ('Pending', 'Approved', 'Rejected')) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_expenses_receipt_status'
           AND conrelid = 'public.expenses'::regclass
    ) THEN
        ALTER TABLE public.expenses
            ADD CONSTRAINT ck_expenses_receipt_status
            CHECK (receipt_status IN ('Missing', 'Uploaded')) NOT VALID;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_expenses_company_expense_number
    ON public.expenses(company_id, expense_number)
    WHERE expense_number ~ '^EXP-[0-9]{17}-[0-9A-F]{9}$' AND deleted_at IS NULL;
