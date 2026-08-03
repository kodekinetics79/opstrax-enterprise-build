-- Stage 71: coaching acknowledgement/completion evidence reconciliation.
-- Production skips runtime schema initialization, so the driver acknowledgement
-- field must be delivered by the owner migration chain rather than DriverSchemaService.
BEGIN;

ALTER TABLE coaching_tasks
  ADD COLUMN IF NOT EXISTS acknowledged_note TEXT NULL;

ALTER TABLE coaching_tasks
  DROP CONSTRAINT IF EXISTS ck_stage71_coaching_acknowledged_note_length;
ALTER TABLE coaching_tasks
  ADD CONSTRAINT ck_stage71_coaching_acknowledged_note_length
  CHECK (acknowledged_note IS NULL OR char_length(acknowledged_note) <= 2000) NOT VALID;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_02_stage71_coaching_evidence_reconciliation',
        'Persist coaching acknowledgement evidence in production-shaped deployments')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
