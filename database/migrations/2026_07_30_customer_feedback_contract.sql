-- Reconcile every historical customer_feedback shape with CustomerPortalService.
-- Additive and idempotent: legacy analytics columns remain available to old reports.

BEGIN;

ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS customer_id BIGINT NULL;
ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS comment TEXT NULL;
ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS feedback_type VARCHAR(80) NOT NULL DEFAULT 'general';
ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS subject VARCHAR(200) NULL;
ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS status VARCHAR(30) NOT NULL DEFAULT 'open';
ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS ix_customer_feedback_company_customer
  ON customer_feedback (company_id, customer_id) WHERE customer_id IS NOT NULL;

COMMIT;
