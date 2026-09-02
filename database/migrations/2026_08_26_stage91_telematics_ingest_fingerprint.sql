-- Stage 91 — immutable ingest fingerprints for safe idempotent retries.
--
-- Native telemetry client-generated IDs and diagnostic source-event IDs are
-- independently retryable. A reused identity is accepted only when its exact
-- authenticated payload fingerprint matches the first accepted observation.

BEGIN;

ALTER TABLE location_events
  ADD COLUMN IF NOT EXISTS ingest_fingerprint VARCHAR(64) NULL;

ALTER TABLE fault_occurrences
  ADD COLUMN IF NOT EXISTS payload_fingerprint VARCHAR(64) NULL;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM location_events
    WHERE ingest_fingerprint IS NOT NULL
      AND ingest_fingerprint !~ '^[0-9a-f]{64}$'
  ) THEN
    RAISE EXCEPTION 'Stage91 blocked: invalid location event ingest fingerprint';
  END IF;

  IF EXISTS (
    SELECT 1 FROM fault_occurrences
    WHERE payload_fingerprint IS NOT NULL
      AND payload_fingerprint !~ '^[0-9a-f]{64}$'
  ) THEN
    RAISE EXCEPTION 'Stage91 blocked: invalid diagnostic payload fingerprint';
  END IF;
END $$;

COMMENT ON COLUMN location_events.ingest_fingerprint IS
  'SHA-256 of the exact authenticated native-ingest request body; never a secret.';
COMMENT ON COLUMN fault_occurrences.payload_fingerprint IS
  'SHA-256 of the exact authenticated diagnostic request body; shared by every DTC in one source event.';

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_26_stage91_telematics_ingest_fingerprint',
        'Immutable native and diagnostic ingest payload fingerprints')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
