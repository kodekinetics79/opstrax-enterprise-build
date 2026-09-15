-- Payment ledger integrity for the customer-facing finance workflow.
-- NOT VALID preserves deployability if a historical environment contains an invalid
-- legacy row; PostgreSQL still enforces the constraint for every new record.
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname='ck_invoice_payments_positive_amount'
      AND conrelid='invoice_payments'::regclass
  ) THEN
    ALTER TABLE invoice_payments
      ADD CONSTRAINT ck_invoice_payments_positive_amount
      CHECK (amount > 0) NOT VALID;
  END IF;
END $$;
