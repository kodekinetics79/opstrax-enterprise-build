-- Stage 114 — account-bound, effective-dated camera asset reconciliation.
--
-- Provider camera events may name only a provider asset. Preserve the exact
-- OpsTrax device and installation used to attribute that event, while keeping
-- every provider event and media reference on ExternalHold.
BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE camera_provider_event_inbox
  ADD COLUMN IF NOT EXISTS device_id BIGINT NULL,
  ADD COLUMN IF NOT EXISTS device_installation_id BIGINT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage114_installation_device_identity
  ON device_installations(company_id,id,device_id);
CREATE INDEX IF NOT EXISTS ix_stage114_device_installation_period
  ON device_installations(company_id,device_id,effective_from DESC,id DESC)
  WHERE status IN ('Installed','Verified','Removed');

ALTER TABLE camera_provider_event_inbox
  DROP CONSTRAINT IF EXISTS ck_stage114_camera_device_installation_pair,
  ADD CONSTRAINT ck_stage114_camera_device_installation_pair
    CHECK ((device_id IS NULL) = (device_installation_id IS NULL)) NOT VALID;
ALTER TABLE camera_provider_event_inbox
  VALIDATE CONSTRAINT ck_stage114_camera_device_installation_pair;

ALTER TABLE camera_provider_event_inbox
  DROP CONSTRAINT IF EXISTS fk_stage114_camera_device_installation,
  ADD CONSTRAINT fk_stage114_camera_device_installation
    FOREIGN KEY (company_id,device_installation_id,device_id)
    REFERENCES device_installations(company_id,id,device_id) NOT VALID;
ALTER TABLE camera_provider_event_inbox
  VALIDATE CONSTRAINT fk_stage114_camera_device_installation;

CREATE INDEX IF NOT EXISTS ix_stage114_camera_device_installation
  ON camera_provider_event_inbox(company_id,device_id,device_installation_id,occurred_at_utc DESC,id DESC)
  WHERE device_id IS NOT NULL;

COMMENT ON COLUMN camera_provider_event_inbox.device_id IS
  'Exact account-bound provider device used for event-time reconciliation; null while no single trusted mapping exists.';
COMMENT ON COLUMN camera_provider_event_inbox.device_installation_id IS
  'Exact effective-dated installation used for event-time reconciliation; populated only with device_id.';

CREATE OR REPLACE FUNCTION stage114_protect_camera_device_mapping()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF OLD.device_id IS NOT NULL
     AND (NEW.device_id IS DISTINCT FROM OLD.device_id
          OR NEW.device_installation_id IS DISTINCT FROM OLD.device_installation_id) THEN
    RAISE EXCEPTION 'Stage114 camera device mapping is immutable after reconciliation'
      USING ERRCODE='23514', CONSTRAINT='ck_camera_provider_device_mapping_immutable';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage114_protect_camera_device_mapping ON camera_provider_event_inbox;
CREATE TRIGGER trg_stage114_protect_camera_device_mapping
BEFORE UPDATE ON camera_provider_event_inbox
FOR EACH ROW EXECUTE FUNCTION stage114_protect_camera_device_mapping();

REVOKE ALL ON FUNCTION stage114_protect_camera_device_mapping() FROM PUBLIC;
DO $stage114_runtime_security$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT EXECUTE ON FUNCTION stage114_protect_camera_device_mapping() TO opstrax_system;
  END IF;
END
$stage114_runtime_security$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_07_stage114_camera_asset_reconciliation',
        'Account-bound provider asset reconciliation to exact event-time device installation')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
