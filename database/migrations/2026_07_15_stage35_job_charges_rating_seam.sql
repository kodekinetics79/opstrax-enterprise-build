-- Stage 35 — Rating-engine seam on job_charges (ADR-008 P0)
--
-- The configurable rating engine writes computed charges tagged source='rating', so re-rating a
-- job can delete-and-recompute ONLY its own rows without ever touching a hand-keyed ('manual')
-- charge or one already on an issued invoice. These columns + the partial unique index are that
-- idempotency foundation. Additive and backward-compatible: existing rows default to 'manual', so
-- nothing downstream changes until the rating engine actually runs.
--
-- Also created idempotently by BusinessSpineSchemaService.EnsureAsync in owner-capable envs; this
-- migration covers restricted-role production (which skips startup schema init). Idempotent.

BEGIN;

ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS source     VARCHAR(20) NOT NULL DEFAULT 'manual';
ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS rate_basis VARCHAR(40) NULL;
ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS rated_at   TIMESTAMPTZ NULL;

-- One rating-owned charge per (job, charge_type). Partial so hand-keyed charges are unconstrained.
CREATE UNIQUE INDEX IF NOT EXISTS uq_job_charges_rated
    ON job_charges (company_id, job_id, charge_type) WHERE source = 'rating';

INSERT INTO schema_migrations (version, description)
VALUES ('stage35_job_charges_rating_seam',
        'job_charges.source/rate_basis/rated_at + uq_job_charges_rated — rating-engine idempotency seam')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- ROLLBACK (manual; run as DB OWNER — NOT auto-applied)
--   BEGIN;
--     DROP INDEX IF EXISTS uq_job_charges_rated;
--     ALTER TABLE job_charges DROP COLUMN IF EXISTS source, DROP COLUMN IF EXISTS rate_basis, DROP COLUMN IF EXISTS rated_at;
--     DELETE FROM schema_migrations WHERE version = 'stage35_job_charges_rating_seam';
--   COMMIT;
-- ============================================================================
