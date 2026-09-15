-- Stage 137 — reconcile legacy operational columns before truth cleanup.
--
-- The canonical 001 schema creates compact Jobs and POD tables. Production and
-- the complete local bootstrap also receive these additive columns from init/004,
-- but a migration-only environment does not. Stage135 uses the fields below to
-- identify exact legacy demo contradictions, so make that dependency explicit
-- without changing or deleting any customer data.

BEGIN;

ALTER TABLE jobs
  ADD COLUMN IF NOT EXISTS job_number VARCHAR(60),
  ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ;

ALTER TABLE proof_of_delivery
  ADD COLUMN IF NOT EXISTS received_by VARCHAR(160),
  ADD COLUMN IF NOT EXISTS proof_type VARCHAR(80) DEFAULT 'Digital Signature',
  ADD COLUMN IF NOT EXISTS signature_url TEXT,
  ADD COLUMN IF NOT EXISTS notes TEXT;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_11_stage137_legacy_operational_truth_contract',
        'Reconcile additive legacy Jobs and POD columns required by operational truth cleanup')
ON CONFLICT(version) DO NOTHING;

COMMIT;
