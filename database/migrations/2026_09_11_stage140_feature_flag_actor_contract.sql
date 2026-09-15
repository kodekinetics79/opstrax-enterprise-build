-- Stage 140 — feature-flag actor-column contract.
--
-- The administration endpoints read and write feature_flags.updated_by. Earlier
-- protected-environment owners created the table without that column, so the
-- first administrative list/create/update failed with PostgreSQL 42703. Keep the
-- repair additive and preserve the existing tenant RLS policies.

BEGIN;

ALTER TABLE feature_flags
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR(220) NULL;

DO $grant_runtime$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE feature_flags TO opstrax_app;
    END IF;
END
$grant_runtime$;

COMMIT;
