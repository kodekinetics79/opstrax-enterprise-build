-- Explicit document lifecycle origin; do not infer or rewrite historical workflow.
BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE documents
  ADD COLUMN IF NOT EXISTS lifecycle_mode VARCHAR(20) NOT NULL DEFAULT 'legacy_unknown',
  ADD COLUMN IF NOT EXISTS lifecycle_assessed_on DATE NULL;
ALTER TABLE documents ALTER COLUMN risk_score DROP NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint
                 WHERE conrelid='documents'::regclass AND conname='ck_documents_lifecycle_mode') THEN
    ALTER TABLE documents ADD CONSTRAINT ck_documents_lifecycle_mode
      CHECK (lifecycle_mode IN ('automatic', 'manual', 'legacy_unknown'));
  END IF;
END $$;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_08_31_stage93_document_lifecycle_provenance', 'Explicit document lifecycle origin and nullable unknown risk')
ON CONFLICT (version) DO NOTHING;
COMMIT;
