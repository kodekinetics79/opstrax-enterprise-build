-- Stage 100 — Wave 3 G3B dashcam provider/media truth
--
-- W3-B-TRUTH-001: legacy dashcam rows were allowed to look provider-backed even
-- when no real camera/provider evidence existed. The old runtime schema supplied
-- placeholder provider and AI-confidence defaults and ordinary persisted clip URLs.
-- This migration makes unknown provider/media truth explicit and fail-closed.

BEGIN;

ALTER TABLE dashcam_events
  ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL,
  ADD COLUMN IF NOT EXISTS provider_event_id VARCHAR(180) NULL,
  ADD COLUMN IF NOT EXISTS provider_received_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS source_authority VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified',
  ADD COLUMN IF NOT EXISTS media_status VARCHAR(32) NOT NULL DEFAULT 'ProviderPending',
  ADD COLUMN IF NOT EXISTS road_facing_media_ref VARCHAR(300) NULL,
  ADD COLUMN IF NOT EXISTS driver_facing_media_ref VARCHAR(300) NULL,
  ADD COLUMN IF NOT EXISTS media_expires_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS recording_mode VARCHAR(48) NULL,
  ADD COLUMN IF NOT EXISTS retention_class VARCHAR(80) NULL,
  ADD COLUMN IF NOT EXISTS privacy_policy_version VARCHAR(80) NULL,
  ADD COLUMN IF NOT EXISTS provider_payload_hash VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS row_version BIGINT NOT NULL DEFAULT 0;

ALTER TABLE dashcam_events
  ALTER COLUMN video_provider DROP DEFAULT,
  ALTER COLUMN video_provider DROP NOT NULL,
  ALTER COLUMN ai_confidence DROP DEFAULT,
  ALTER COLUMN ai_confidence DROP NOT NULL;

-- No current row carries a G3B evidence package proving provider/event/media
-- authority. Preserve metadata for historical review, but remove fields that can
-- be mistaken for playable provider evidence or a measured model confidence.
UPDATE dashcam_events
SET road_facing_clip_url = NULL,
    driver_facing_clip_url = NULL,
    thumbnail_url = NULL,
    ai_confidence = NULL,
    video_provider = CASE
      WHEN LOWER(BTRIM(COALESCE(video_provider,''))) IN ('','opstrax placeholder','placeholder','demo') THEN NULL
      ELSE video_provider
    END,
    provider_event_id = NULL,
    provider_received_at = NULL,
    source_authority = 'LegacyUnverified',
    media_status = 'Unavailable',
    road_facing_media_ref = NULL,
    driver_facing_media_ref = NULL,
    media_expires_at = NULL,
    recording_mode = NULL,
    retention_class = NULL,
    privacy_policy_version = NULL,
    provider_payload_hash = NULL,
    row_version = row_version + 1,
    updated_at = NOW()
WHERE source_authority <> 'Authoritative';

-- Legacy runtime/demo writers predate provider authority and may still try to
-- write placeholder provider names, confidence values or direct clip URLs. Normalize
-- those writes at the database boundary so they cannot recreate customer-visible
-- provider/media claims after Stage 100.
CREATE OR REPLACE FUNCTION stage100_enforce_dashcam_provider_truth()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF COALESCE(NEW.source_authority, 'LegacyUnverified') <> 'Authoritative' THEN
    NEW.source_authority := COALESCE(NULLIF(BTRIM(NEW.source_authority), ''), 'LegacyUnverified');
    IF NEW.source_authority NOT IN ('LegacyUnverified','ProviderPending') THEN
      NEW.source_authority := 'LegacyUnverified';
    END IF;
    NEW.road_facing_clip_url := NULL;
    NEW.driver_facing_clip_url := NULL;
    NEW.thumbnail_url := NULL;
    NEW.ai_confidence := NULL;
    IF LOWER(BTRIM(COALESCE(NEW.video_provider,''))) IN ('','opstrax placeholder','placeholder','demo') THEN
      NEW.video_provider := NULL;
    END IF;
    NEW.provider_event_id := NULL;
    NEW.provider_received_at := NULL;
    NEW.media_status := CASE WHEN NEW.source_authority='ProviderPending' THEN 'ProviderPending' ELSE 'Unavailable' END;
    NEW.road_facing_media_ref := NULL;
    NEW.driver_facing_media_ref := NULL;
    NEW.media_expires_at := NULL;
    NEW.provider_payload_hash := NULL;
  END IF;
  NEW.row_version := COALESCE(NEW.row_version,0) + 1;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage100_enforce_dashcam_provider_truth ON dashcam_events;
CREATE TRIGGER trg_stage100_enforce_dashcam_provider_truth
BEFORE INSERT OR UPDATE ON dashcam_events
FOR EACH ROW EXECUTE FUNCTION stage100_enforce_dashcam_provider_truth();

-- Backfill branch only from already-owned fleet identity. A missing or conflicting
-- branch remains NULL and must fail closed in branch-scoped customer workflows.
UPDATE dashcam_events de
SET branch_id = COALESCE(
  (SELECT v.branch_id FROM vehicles v WHERE v.id=de.vehicle_id AND v.company_id=de.company_id),
  (SELECT d.branch_id FROM drivers d WHERE d.id=de.driver_id AND d.company_id=de.company_id)
)
WHERE de.branch_id IS NULL;

DO $stage100$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='dashcam_events'::regclass
      AND conname='ck_dashcam_source_authority'
  ) THEN
    ALTER TABLE dashcam_events
      ADD CONSTRAINT ck_dashcam_source_authority CHECK (
        (
          source_authority='Authoritative'
          AND video_provider IS NOT NULL AND BTRIM(video_provider)<>''
          AND provider_event_id IS NOT NULL AND BTRIM(provider_event_id)<>''
          AND provider_received_at IS NOT NULL
          AND provider_payload_hash IS NOT NULL
          AND provider_payload_hash ~ '^[0-9a-f]{64}$'
          AND media_status IN ('ProviderPending','Ready','Unavailable','Expired','Error')
        )
        OR
        (
          source_authority IN ('LegacyUnverified','ProviderPending')
          AND media_status IN ('ProviderPending','Unavailable','Error')
          AND road_facing_clip_url IS NULL
          AND driver_facing_clip_url IS NULL
          AND thumbnail_url IS NULL
          AND road_facing_media_ref IS NULL
          AND driver_facing_media_ref IS NULL
        )
      ) NOT VALID;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='dashcam_events'::regclass
      AND conname='ck_dashcam_ready_media_reference'
  ) THEN
    ALTER TABLE dashcam_events
      ADD CONSTRAINT ck_dashcam_ready_media_reference CHECK (
        media_status <> 'Ready'
        OR (
          source_authority='Authoritative'
          AND (road_facing_media_ref IS NOT NULL OR driver_facing_media_ref IS NOT NULL)
        )
      ) NOT VALID;
  END IF;
END
$stage100$;

ALTER TABLE dashcam_events VALIDATE CONSTRAINT ck_dashcam_source_authority;
ALTER TABLE dashcam_events VALIDATE CONSTRAINT ck_dashcam_ready_media_reference;

CREATE UNIQUE INDEX IF NOT EXISTS uq_dashcam_provider_event
  ON dashcam_events(company_id, LOWER(BTRIM(video_provider)), provider_event_id)
  WHERE deleted_at IS NULL
    AND source_authority='Authoritative'
    AND video_provider IS NOT NULL
    AND provider_event_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_dashcam_company_branch_occurred
  ON dashcam_events(company_id, branch_id, occurred_at DESC, id DESC)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_dashcam_media_status
  ON dashcam_events(company_id, media_status, provider_received_at DESC)
  WHERE deleted_at IS NULL;

COMMIT;
