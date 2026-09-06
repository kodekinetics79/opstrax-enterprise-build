-- Stage 99 — Wave 3 G3A HOS clock source truth
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

CREATE OR REPLACE FUNCTION stage99_enforce_hos_clock_source_truth()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF COALESCE(NEW.source_authority, 'LegacyUnverified') <> 'Authoritative' THEN
    NEW.source_authority := COALESCE(NULLIF(BTRIM(NEW.source_authority), ''), 'LegacyUnverified');
    IF NEW.source_authority NOT IN ('LegacyUnverified','ProviderPending') THEN
      NEW.source_authority := 'LegacyUnverified';
    END IF;
    NEW.drive_time_remaining_minutes := NULL;
    NEW.shift_time_remaining_minutes := NULL;
    NEW.cycle_time_remaining_minutes := NULL;
    NEW.break_needed_at := NULL;
    NEW.reset_at := NULL;
    NEW.status := 'Unavailable';
    NEW.hos_warning := 'Authoritative ELD/HOS source not connected';
    NEW.clock_source := NULL;
    NEW.source_event_id := NULL;
    NEW.source_observed_at := NULL;
    NEW.source_quality := NULL;
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage99_enforce_hos_clock_source_truth ON hos_clocks;
CREATE TRIGGER trg_stage99_enforce_hos_clock_source_truth
BEFORE INSERT OR UPDATE ON hos_clocks
FOR EACH ROW EXECUTE FUNCTION stage99_enforce_hos_clock_source_truth();

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
        (source_authority = 'Authoritative'
         AND clock_source IS NOT NULL AND BTRIM(clock_source) <> ''
         AND source_observed_at IS NOT NULL
         AND drive_time_remaining_minutes IS NOT NULL
         AND shift_time_remaining_minutes IS NOT NULL
         AND cycle_time_remaining_minutes IS NOT NULL
         AND drive_time_remaining_minutes >= 0
         AND shift_time_remaining_minutes >= 0
         AND cycle_time_remaining_minutes >= 0
         AND status IN ('OK','Warning','Violation'))
        OR
        (source_authority IN ('LegacyUnverified','ProviderPending')
         AND drive_time_remaining_minutes IS NULL
         AND shift_time_remaining_minutes IS NULL
         AND cycle_time_remaining_minutes IS NULL
         AND status = 'Unavailable')
      ) NOT VALID;
  END IF;
END
$stage99$;

ALTER TABLE hos_clocks VALIDATE CONSTRAINT ck_hos_clocks_source_authority;
CREATE INDEX IF NOT EXISTS idx_hos_clocks_company_branch_authority
  ON hos_clocks(company_id, branch_id, source_authority, source_observed_at DESC);
COMMIT;
