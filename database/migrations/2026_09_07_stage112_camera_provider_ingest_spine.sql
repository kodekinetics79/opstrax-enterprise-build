-- Stage 112 — provider-neutral camera event intake and media-reference spine
--
-- This is an engineering intake boundary, not provider certification. It records
-- an authenticated adapter's canonical payload fingerprint and opaque provider
-- media identifiers while every event remains on EXTERNAL HOLD. No URL, media
-- bytes, AI conclusion, or provider-authoritative customer claim is created here.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE TABLE IF NOT EXISTS camera_provider_event_inbox (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  branch_id BIGINT NULL,
  provider_key VARCHAR(80) NOT NULL,
  provider_account_ref VARCHAR(160) NOT NULL,
  provider_event_id VARCHAR(180) NOT NULL,
  payload_schema_version VARCHAR(40) NOT NULL,
  payload_sha256 VARCHAR(64) NOT NULL,
  conflicting_payload_sha256 VARCHAR(64) NULL,
  event_type VARCHAR(120) NOT NULL,
  occurred_at_utc TIMESTAMPTZ NOT NULL,
  provider_received_at_utc TIMESTAMPTZ NOT NULL,
  first_received_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_received_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  vehicle_external_id VARCHAR(180) NULL,
  driver_external_id VARCHAR(180) NULL,
  trip_external_id VARCHAR(180) NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  trip_id BIGINT NULL,
  reconciliation_status VARCHAR(24) NOT NULL DEFAULT 'Pending',
  processing_status VARCHAR(24) NOT NULL DEFAULT 'PendingVerification',
  provider_verification_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
  quarantine_reason VARCHAR(160) NULL,
  seen_count INTEGER NOT NULL DEFAULT 1,
  created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT uq_camera_provider_inbox_company_id UNIQUE (company_id,id),
  CONSTRAINT uq_camera_provider_inbox_event UNIQUE (company_id,provider_key,provider_account_ref,provider_event_id),
  CONSTRAINT ck_camera_provider_inbox_provider_key CHECK (provider_key ~ '^[a-z0-9][a-z0-9._-]{0,79}$'),
  CONSTRAINT ck_camera_provider_inbox_payload_hash CHECK (payload_sha256 ~ '^[0-9a-f]{64}$'),
  CONSTRAINT ck_camera_provider_inbox_conflict_hash CHECK (
    conflicting_payload_sha256 IS NULL OR conflicting_payload_sha256 ~ '^[0-9a-f]{64}$'
  ),
  CONSTRAINT ck_camera_provider_inbox_time_order CHECK (occurred_at_utc <= provider_received_at_utc + INTERVAL '5 minutes'),
  CONSTRAINT ck_camera_provider_inbox_receipt_order CHECK (last_received_at_utc >= first_received_at_utc),
  CONSTRAINT ck_camera_provider_inbox_reconciliation CHECK (reconciliation_status IN ('Pending','Matched','Quarantined')),
  CONSTRAINT ck_camera_provider_inbox_processing CHECK (processing_status IN ('PendingVerification','Quarantined')),
  CONSTRAINT ck_camera_provider_inbox_external_hold CHECK (provider_verification_status='ExternalHold'),
  CONSTRAINT ck_camera_provider_inbox_quarantine CHECK (
    (processing_status='Quarantined' AND quarantine_reason IS NOT NULL)
    OR (processing_status='PendingVerification' AND quarantine_reason IS NULL)
  ),
  CONSTRAINT ck_camera_provider_inbox_seen_count CHECK (seen_count > 0)
);

CREATE TABLE IF NOT EXISTS camera_provider_media_references (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  provider_event_inbox_id BIGINT NOT NULL,
  camera_role VARCHAR(24) NOT NULL,
  media_kind VARCHAR(16) NOT NULL,
  content_type VARCHAR(80) NOT NULL,
  provider_media_id VARCHAR(240) NOT NULL,
  captured_at_utc TIMESTAMPTZ NULL,
  duration_milliseconds INTEGER NULL,
  provider_expires_at_utc TIMESTAMPTZ NULL,
  retrieval_status VARCHAR(24) NOT NULL DEFAULT 'ProviderPending',
  access_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
  recording_mode VARCHAR(48) NULL,
  retention_class VARCHAR(80) NOT NULL,
  privacy_policy_version VARCHAR(80) NOT NULL,
  legal_hold BOOLEAN NOT NULL DEFAULT FALSE,
  deletion_requested_at_utc TIMESTAMPTZ NULL,
  deleted_at_utc TIMESTAMPTZ NULL,
  first_seen_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_camera_provider_media_event FOREIGN KEY (company_id,provider_event_inbox_id)
    REFERENCES camera_provider_event_inbox(company_id,id) ON DELETE CASCADE,
  CONSTRAINT uq_camera_provider_media_reference UNIQUE (company_id,provider_event_inbox_id,camera_role,provider_media_id),
  CONSTRAINT ck_camera_provider_media_role CHECK (camera_role IN ('RoadFacing','DriverFacing')),
  CONSTRAINT ck_camera_provider_media_kind CHECK (media_kind IN ('Video','Image')),
  CONSTRAINT ck_camera_provider_media_type CHECK (content_type IN ('video/mp4','image/jpeg')),
  CONSTRAINT ck_camera_provider_media_duration CHECK (duration_milliseconds IS NULL OR duration_milliseconds BETWEEN 0 AND 3600000),
  CONSTRAINT ck_camera_provider_media_retrieval CHECK (retrieval_status IN ('ProviderPending','Expired','Revoked','Unavailable','Error')),
  CONSTRAINT ck_camera_provider_media_external_hold CHECK (access_status='ExternalHold'),
  CONSTRAINT ck_camera_provider_media_seen_order CHECK (last_seen_at_utc >= first_seen_at_utc),
  CONSTRAINT ck_camera_provider_media_deletion_order CHECK (
    deleted_at_utc IS NULL OR (deletion_requested_at_utc IS NOT NULL AND deleted_at_utc >= deletion_requested_at_utc)
  )
);

ALTER TABLE camera_provider_media_references
  ADD COLUMN IF NOT EXISTS deletion_requested_at_utc TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS deleted_at_utc TIMESTAMPTZ NULL,
  DROP CONSTRAINT IF EXISTS ck_camera_provider_media_deletion_order,
  ADD CONSTRAINT ck_camera_provider_media_deletion_order CHECK (
    deleted_at_utc IS NULL OR (deletion_requested_at_utc IS NOT NULL AND deleted_at_utc >= deletion_requested_at_utc)
  );

-- Provider event identifiers are commonly unique only inside one provider account.
-- Keep the account in the idempotency identity so two tenant connections cannot
-- collide merely because their provider reused the same event number.
ALTER TABLE camera_provider_event_inbox
  DROP CONSTRAINT IF EXISTS uq_camera_provider_inbox_event,
  ADD CONSTRAINT uq_camera_provider_inbox_event
    UNIQUE (company_id,provider_key,provider_account_ref,provider_event_id);

COMMENT ON TABLE camera_provider_event_inbox IS
  'Provider camera intake ledger. Rows are engineering evidence on ExternalHold, never certification evidence by themselves.';
COMMENT ON COLUMN camera_provider_event_inbox.payload_sha256 IS
  'SHA-256 calculated by OpsTrax over the exact authenticated provider payload; raw payload and secrets are not stored.';
COMMENT ON COLUMN camera_provider_event_inbox.provider_verification_status IS
  'Locked to ExternalHold until a later provider-specific certification migration establishes a stronger state.';
COMMENT ON COLUMN camera_provider_media_references.provider_media_id IS
  'Opaque provider identifier only. URLs, credentials, signed query strings and media bytes are prohibited by the service boundary.';

CREATE OR REPLACE FUNCTION stage112_protect_camera_provider_event_identity()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF NEW.company_id IS DISTINCT FROM OLD.company_id
     OR NEW.provider_key IS DISTINCT FROM OLD.provider_key
     OR NEW.provider_account_ref IS DISTINCT FROM OLD.provider_account_ref
     OR NEW.provider_event_id IS DISTINCT FROM OLD.provider_event_id
     OR NEW.payload_schema_version IS DISTINCT FROM OLD.payload_schema_version
     OR NEW.payload_sha256 IS DISTINCT FROM OLD.payload_sha256
     OR NEW.event_type IS DISTINCT FROM OLD.event_type
     OR NEW.occurred_at_utc IS DISTINCT FROM OLD.occurred_at_utc
     OR NEW.provider_received_at_utc IS DISTINCT FROM OLD.provider_received_at_utc
     OR NEW.vehicle_external_id IS DISTINCT FROM OLD.vehicle_external_id
     OR NEW.driver_external_id IS DISTINCT FROM OLD.driver_external_id
     OR NEW.trip_external_id IS DISTINCT FROM OLD.trip_external_id
     OR NEW.first_received_at_utc IS DISTINCT FROM OLD.first_received_at_utc
     OR NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc
     OR NEW.seen_count < OLD.seen_count THEN
    RAISE EXCEPTION 'Stage112 camera provider event identity is immutable'
      USING ERRCODE='23514', CONSTRAINT='ck_camera_provider_event_identity_immutable';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage112_protect_camera_provider_event_identity ON camera_provider_event_inbox;
CREATE TRIGGER trg_stage112_protect_camera_provider_event_identity
BEFORE UPDATE ON camera_provider_event_inbox
FOR EACH ROW EXECUTE FUNCTION stage112_protect_camera_provider_event_identity();

CREATE OR REPLACE FUNCTION stage112_protect_camera_provider_media_identity()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF NEW.company_id IS DISTINCT FROM OLD.company_id
     OR NEW.provider_event_inbox_id IS DISTINCT FROM OLD.provider_event_inbox_id
     OR NEW.camera_role IS DISTINCT FROM OLD.camera_role
     OR NEW.media_kind IS DISTINCT FROM OLD.media_kind
     OR NEW.content_type IS DISTINCT FROM OLD.content_type
     OR NEW.provider_media_id IS DISTINCT FROM OLD.provider_media_id
     OR NEW.captured_at_utc IS DISTINCT FROM OLD.captured_at_utc
     OR NEW.duration_milliseconds IS DISTINCT FROM OLD.duration_milliseconds
     OR NEW.recording_mode IS DISTINCT FROM OLD.recording_mode
     OR NEW.retention_class IS DISTINCT FROM OLD.retention_class
     OR NEW.privacy_policy_version IS DISTINCT FROM OLD.privacy_policy_version
     OR NEW.first_seen_at_utc IS DISTINCT FROM OLD.first_seen_at_utc
     OR NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc THEN
    RAISE EXCEPTION 'Stage112 camera provider media identity is immutable'
      USING ERRCODE='23514', CONSTRAINT='ck_camera_provider_media_identity_immutable';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage112_protect_camera_provider_media_identity ON camera_provider_media_references;
CREATE TRIGGER trg_stage112_protect_camera_provider_media_identity
BEFORE UPDATE ON camera_provider_media_references
FOR EACH ROW EXECUTE FUNCTION stage112_protect_camera_provider_media_identity();

REVOKE ALL ON FUNCTION stage112_protect_camera_provider_event_identity() FROM PUBLIC;
REVOKE ALL ON FUNCTION stage112_protect_camera_provider_media_identity() FROM PUBLIC;

CREATE INDEX IF NOT EXISTS ix_camera_provider_inbox_work
  ON camera_provider_event_inbox(processing_status,last_received_at_utc,id);
CREATE INDEX IF NOT EXISTS ix_camera_provider_inbox_scope_time
  ON camera_provider_event_inbox(company_id,branch_id,occurred_at_utc DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_camera_provider_inbox_reconciliation
  ON camera_provider_event_inbox(company_id,reconciliation_status,last_received_at_utc,id);
CREATE INDEX IF NOT EXISTS ix_camera_provider_media_expiry
  ON camera_provider_media_references(retrieval_status,provider_expires_at_utc,id)
  WHERE retrieval_status IN ('ProviderPending','Expired');

ALTER TABLE camera_provider_event_inbox ENABLE ROW LEVEL SECURITY;
ALTER TABLE camera_provider_event_inbox FORCE ROW LEVEL SECURITY;
ALTER TABLE camera_provider_media_references ENABLE ROW LEVEL SECURITY;
ALTER TABLE camera_provider_media_references FORCE ROW LEVEL SECURITY;

REVOKE ALL ON TABLE camera_provider_event_inbox,camera_provider_media_references FROM PUBLIC;
REVOKE ALL ON SEQUENCE camera_provider_event_inbox_id_seq,camera_provider_media_references_id_seq FROM PUBLIC;

DO $stage112_runtime_security$
BEGIN
  -- Provider intake is a background/control-plane operation. The tenant-facing
  -- application identity receives no direct access to raw provider identities.
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    REVOKE ALL ON TABLE camera_provider_event_inbox,camera_provider_media_references FROM opstrax_app;
    REVOKE ALL ON SEQUENCE camera_provider_event_inbox_id_seq,camera_provider_media_references_id_seq FROM opstrax_app;
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    DROP POLICY IF EXISTS system_control_plane ON camera_provider_event_inbox;
    CREATE POLICY system_control_plane ON camera_provider_event_inbox
      AS PERMISSIVE FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    DROP POLICY IF EXISTS system_control_plane ON camera_provider_media_references;
    CREATE POLICY system_control_plane ON camera_provider_media_references
      AS PERMISSIVE FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);

    REVOKE ALL ON TABLE camera_provider_event_inbox,camera_provider_media_references FROM opstrax_system;
    GRANT SELECT,INSERT,UPDATE ON TABLE camera_provider_event_inbox,camera_provider_media_references TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE camera_provider_event_inbox_id_seq,camera_provider_media_references_id_seq TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage112_protect_camera_provider_event_identity() TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage112_protect_camera_provider_media_identity() TO opstrax_system;
  END IF;
END
$stage112_runtime_security$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage112_camera_provider_ingest_spine',
        'Provider-neutral camera event intake, replay quarantine and opaque media references on ExternalHold')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
