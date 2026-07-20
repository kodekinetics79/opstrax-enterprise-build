-- Stage 21 — Customer Portal foundation (additive)
--
-- PURPOSE
--   Adds the customer-principal binding and feedback lifecycle needed for a real,
--   authenticated customer portal, WITHOUT a parallel auth subsystem:
--     * A portal customer-user is an existing `users` row bound to a `customer_id`
--       (NULL for internal staff). Portal endpoints resolve this customer_id from the
--       authenticated user and scope every query by (company_id, customer_id) — a
--       customer-within-tenant isolation boundary that is stricter than tenant RBAC.
--     * `customer_feedback` gains a status lifecycle (open/under_review/resolved/closed)
--       + an optional subject, reusing the existing table (no new feedback table).
--
-- SAFETY: additive, idempotent, no data touched.

BEGIN;

ALTER TABLE users ADD COLUMN IF NOT EXISTS customer_id BIGINT NULL;
CREATE INDEX IF NOT EXISTS ix_users_company_customer ON users(company_id, customer_id) WHERE customer_id IS NOT NULL;

ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS status VARCHAR(30) NOT NULL DEFAULT 'open';
ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS subject VARCHAR(200) NULL;
ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

COMMIT;
