-- Stage 99 — Wave 3 G3A HOS clock source truth
--
-- W3-A-TRUTH-001: pre-commercial hos_clocks rows historically carried convenient
-- non-null defaults (11h drive / 14h shift / 70h cycle) even when no accepted ELD
-- source existed. Persistence is not regulatory authority. Fail closed until an
-- authoritative provider/device/source boundary is explicitly persisted.

BEGIN;

ALTER TABLE hos_clocks
  ADD COLUMN IF NOT EXISTS clock_source VARCHAR(80) NULL,
  ADD COLUMN IF NOT EXISTS source_event_id VARCHAR(160) NULL,
  ADD COLUMN IF NOT EXISTS source_observed_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS source_authority VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified',
  ADD COLUMN IF NOT EXISTS source_quality VARCHAR(32) NULL;

ALTER TABLE hos_clocks
  ALTER COLUMN drive_time_remaining_minutes DROP NOT NULL,
  ALTER COLUMN drive_time_remaining_minutes DROP DEFAULT,
  ALTER COLUMN shift_time_remaining_minutes DROP NOT NULL,
  ALTER COLUMN shift_time_remaining_minutes DROP DEFAULT,
  ALTER COLUMN cycle_time_remaining_minutes DROP NOT NULL,
  ALTER COLUMN cycle_time_remaining_minutes DROP DEFAULT,
  ALTER COLUMN status SET DEFAULT 'Unavailable';

-- Existing rows have no persisted provenance capable of proving that their legal-time
-- values came from an accepted certified source. Do not preserve an unverifiable legal
-- clock merely because it is already stored. The rows remain for identity/history, but
-- the actionable values are cleared until a governed source writes a new snapshot.
UPDATE hos_clocks
SET drive_time_remaining_minutes = NULL,
    shift_time_remaining_minutes = NULL,
    cycle_time_remaining_minutes = NULL,
    break_needed_at = NULL,
    reset_at = NULL,
    status = 'Unavailable',
    hos_warning = 'Authoritative ELD/HOS source not connected',
    clock_source = NULL,
    source_event_id = NULL,
    source_observed_at = NULL,
    source_authority = 'LegacyUnverified',
    source_quality = NULL,
    updated_at = NOW();

DO $stage99$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid = 'hos_clocks'::regclass
      AND conname = 'ck_hos_clocks_source_authority'
  ) THEN
    ALTER TABLE hos_clocks
      ADD CONSTRAINT ck_hos_clocks_source_authority
      CHECK (
        (
          source_authority = 'Authoritative'
          AND clock_source IS NOT NULL
          AND BTRIM(clock_source) <> ''
          AND source_observed_at IS NOT NULL
          AND drive_time_remaining_minutes IS NOT NULL
          AND shift_time_remaining_minutes IS NOT NULL
          AND cycle_time_remaining_minutes IS NOT NULL
          AND drive_time_remaining_minutes >= 0
          AND shift_time_remaining_minutes >= 0
          AND cycle_time_remaining_minutes >= 0
          AND status IN ('OK','Warning','Violation')
        )
        OR
        (
          source_authority IN ('LegacyUnverified','ProviderPending')
          AND drive_time_remaining_minutes IS NULL
          AND shift_time_remaining_minutes IS NULL
          AND cycle_time_remaining_minutes IS NULL
          AND status = 'Unavailable'
        )
      ) NOT VALID;
  END IF;
END
$stage99$;

-- Validate after the fail-closed rewrite above. NOT VALID avoids table-locking surprises
-- before the rewrite, while VALIDATE makes the migration itself prove the invariant.
ALTER TABLE hos_clocks VALIDATE CONSTRAINT ck_hos_clocks_source_authority;

CREATE INDEX IF NOT EXISTS idx_hos_clocks_company_branch_authority
  ON hos_clocks(company_id, branch_id, source_authority, source_observed_at DESC);

COMMIT;
