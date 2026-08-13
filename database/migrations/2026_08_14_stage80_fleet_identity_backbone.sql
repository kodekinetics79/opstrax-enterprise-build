-- Stage 80 — Authoritative vehicle/device installation and telemetry identity backbone
--
-- Owner-only, additive/idempotent and forward-recoverable.  Stage66 introduced an
-- installation work queue; this migration turns it into the effective-dated source of
-- truth.  Legacy mutable eld_devices.vehicle_id remains a compatibility projection only.
BEGIN;

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE IF NOT EXISTS device_installation_quarantine (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NULL,
  device_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  installation_id BIGINT NULL,
  reason_code VARCHAR(80) NOT NULL,
  evidence_json JSONB NOT NULL,
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  resolved_at TIMESTAMPTZ NULL,
  resolved_by BIGINT NULL,
  resolution_notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_device_installation_quarantine_evidence
  ON device_installation_quarantine
  (reason_code,COALESCE(device_id,0),COALESCE(vehicle_id,0),COALESCE(installation_id,0));

ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS plate_jurisdiction VARCHAR(80) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS vehicle_class VARCHAR(80) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS vin_exception_type VARCHAR(80) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS alternate_identifier VARCHAR(120) NULL;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

-- Existing fleets may contain legacy identifiers that cannot safely be guessed or
-- rewritten. Preserve those rows, but require every new vehicle and every identity
-- change to satisfy the governed VIN-or-approved-alternate contract.
CREATE OR REPLACE FUNCTION stage80_vin_is_valid(candidate TEXT)
RETURNS BOOLEAN LANGUAGE plpgsql IMMUTABLE STRICT AS $$
DECLARE
  normalized TEXT:=UPPER(BTRIM(candidate));
  weights INT[]:=ARRAY[8,7,6,5,4,3,2,10,0,9,8,7,6,5,4,3,2];
  value INT; total INT:=0; idx INT; expected TEXT;
BEGIN
  IF normalized !~ '^[A-HJ-NPR-Z0-9]{17}$' THEN RETURN FALSE; END IF;
  FOR idx IN 1..17 LOOP
    IF SUBSTRING(normalized,idx,1) ~ '^[0-9]$' THEN
      value:=SUBSTRING(normalized,idx,1)::INT;
    ELSE
      value:=CASE SUBSTRING(normalized,idx,1)
        WHEN 'A' THEN 1 WHEN 'B' THEN 2 WHEN 'C' THEN 3 WHEN 'D' THEN 4
        WHEN 'E' THEN 5 WHEN 'F' THEN 6 WHEN 'G' THEN 7 WHEN 'H' THEN 8
        WHEN 'J' THEN 1 WHEN 'K' THEN 2 WHEN 'L' THEN 3 WHEN 'M' THEN 4
        WHEN 'N' THEN 5 WHEN 'P' THEN 7 WHEN 'R' THEN 9
        WHEN 'S' THEN 2 WHEN 'T' THEN 3 WHEN 'U' THEN 4 WHEN 'V' THEN 5
        WHEN 'W' THEN 6 WHEN 'X' THEN 7 WHEN 'Y' THEN 8 WHEN 'Z' THEN 9
        ELSE NULL END;
      IF value IS NULL THEN RETURN FALSE; END IF;
    END IF;
    total:=total+(value*weights[idx]);
  END LOOP;
  expected:=CASE WHEN total % 11=10 THEN 'X' ELSE (total % 11)::TEXT END;
  RETURN SUBSTRING(normalized,9,1)=expected;
END $$;

CREATE OR REPLACE FUNCTION stage80_enforce_vehicle_identity()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
  normalized_vin TEXT:=NULLIF(UPPER(BTRIM(NEW.vin)),'');
  normalized_alternate TEXT:=NULLIF(UPPER(BTRIM(NEW.alternate_identifier)),'');
BEGIN
  IF TG_OP='UPDATE'
     AND NEW.vin IS NOT DISTINCT FROM OLD.vin
     AND NEW.vin_exception_type IS NOT DISTINCT FROM OLD.vin_exception_type
     AND NEW.alternate_identifier IS NOT DISTINCT FROM OLD.alternate_identifier THEN
    RETURN NEW;
  END IF;
  IF normalized_vin IS NOT NULL THEN
    IF NEW.vin_exception_type IS NOT NULL OR normalized_alternate IS NOT NULL
       OR NOT stage80_vin_is_valid(normalized_vin) THEN
      RAISE EXCEPTION 'vehicle identity requires one valid VIN or one approved alternate identifier'
        USING ERRCODE='23514';
    END IF;
    NEW.vin:=normalized_vin;
  ELSE
    IF NEW.vin_exception_type NOT IN
         ('manufacturer-serial-number','government-registration-number','legacy-fleet-identifier')
       OR normalized_alternate IS NULL
       OR LENGTH(normalized_alternate) NOT BETWEEN 4 AND 64
       OR normalized_alternate !~ '^[A-Z0-9][A-Z0-9._/-]*$'
       OR normalized_alternate ~ '^(UNKNOWN|NONE|N/?A|NA|TBD|NOT-APPLICABLE|NO-VIN|NOVIN)$'
       OR (LENGTH(normalized_alternate)=17 AND normalized_alternate ~ '^[A-Z0-9]{17}$') THEN
      RAISE EXCEPTION 'vehicle without VIN requires an approved exception and governed alternate identifier'
        USING ERRCODE='23514';
    END IF;
    NEW.vin:=NULL;
    NEW.alternate_identifier:=normalized_alternate;
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage80_enforce_vehicle_identity ON vehicles;
CREATE TRIGGER trg_stage80_enforce_vehicle_identity
BEFORE INSERT OR UPDATE OF vin,vin_exception_type,alternate_identifier ON vehicles
FOR EACH ROW EXECUTE FUNCTION stage80_enforce_vehicle_identity();
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage80_vehicles_active_alternate_identity
  ON vehicles(company_id,vin_exception_type,UPPER(BTRIM(alternate_identifier)))
  WHERE deleted_at IS NULL AND NULLIF(BTRIM(alternate_identifier),'') IS NOT NULL;

ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS device_category VARCHAR(40) NOT NULL DEFAULT 'GPS';
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS imei VARCHAR(32) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS provider_external_id VARCHAR(160) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS manufacturer VARCHAR(120) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS sim_iccid VARCHAR(32) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS sim_imsi VARCHAR(32) NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS commissioned_at TIMESTAMPTZ NULL;
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS retired_at TIMESTAMPTZ NULL;
-- The provision API persists operator-entered registration notes. Stage29 added
-- this column to some legacy databases, but it is not part of the protected
-- owner chain; Stage80 must therefore reconcile it for clean and upgraded sites.
ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS notes TEXT NULL;

-- Normalized identifier conflicts are evidence, not a winner-selection problem. Hold
-- every conflicting legacy record outside the active identity namespace, then enforce
-- normalized uniqueness for all non-quarantined inventory written after this migration.
INSERT INTO device_installation_quarantine
  (company_id,device_id,vehicle_id,installation_id,reason_code,evidence_json)
SELECT d.company_id,d.id,d.vehicle_id,NULL,'duplicate_normalized_device_serial',
       jsonb_build_object('deviceId',d.id,'serial',d.device_serial)
FROM eld_devices d
JOIN (
  SELECT UPPER(BTRIM(device_serial)) normalized
  FROM eld_devices WHERE deleted_at IS NULL
  GROUP BY UPPER(BTRIM(device_serial)) HAVING COUNT(*)>1
) conflict ON conflict.normalized=UPPER(BTRIM(d.device_serial))
WHERE d.deleted_at IS NULL
ON CONFLICT DO NOTHING;

INSERT INTO device_installation_quarantine
  (company_id,device_id,vehicle_id,installation_id,reason_code,evidence_json)
SELECT d.company_id,d.id,d.vehicle_id,NULL,'duplicate_normalized_imei',
       jsonb_build_object('deviceId',d.id,'imei',d.imei)
FROM eld_devices d
JOIN (
  SELECT REGEXP_REPLACE(imei,'[^0-9]','','g') normalized
  FROM eld_devices WHERE deleted_at IS NULL AND NULLIF(BTRIM(imei),'') IS NOT NULL
  GROUP BY REGEXP_REPLACE(imei,'[^0-9]','','g') HAVING COUNT(*)>1
) conflict ON conflict.normalized=REGEXP_REPLACE(d.imei,'[^0-9]','','g')
WHERE d.deleted_at IS NULL
ON CONFLICT DO NOTHING;

UPDATE eld_devices d SET device_state='Quarantined',updated_at=NOW()
WHERE d.deleted_at IS NULL AND EXISTS (
  SELECT 1 FROM device_installation_quarantine q
  WHERE q.device_id=d.id AND q.resolved_at IS NULL
    AND q.reason_code IN ('duplicate_normalized_device_serial','duplicate_normalized_imei')
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage80_eld_active_normalized_serial
  ON eld_devices(UPPER(BTRIM(device_serial)))
  WHERE deleted_at IS NULL AND device_state<>'Quarantined';
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage80_eld_active_normalized_imei
  ON eld_devices((REGEXP_REPLACE(imei,'[^0-9]','','g')))
  WHERE deleted_at IS NULL AND device_state<>'Quarantined' AND NULLIF(BTRIM(imei),'') IS NOT NULL;

ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS device_role VARCHAR(40) NOT NULL DEFAULT 'GPS';
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS is_primary BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS effective_from TIMESTAMPTZ NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS effective_to TIMESTAMPTZ NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS installed_by BIGINT NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS removed_by BIGINT NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS installation_location VARCHAR(160) NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS odometer_at_installation DECIMAL(12,2) NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS commissioning_method VARCHAR(80) NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS commissioning_result VARCHAR(40) NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS verification_reference TEXT NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS assignment_reason TEXT NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS removal_reason TEXT NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS source VARCHAR(40) NOT NULL DEFAULT 'operator';
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(120) NULL;
ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS row_version INT NOT NULL DEFAULT 1;

-- A reapply must be able to reconcile newly discovered legacy conflicts before
-- restoring the runtime immutability guards below.
DROP TRIGGER IF EXISTS trg_stage80_preserve_installation_history ON device_installations;
DROP TRIGGER IF EXISTS trg_stage80_preserve_installation_evidence ON device_installation_evidence;
DROP TRIGGER IF EXISTS trg_stage80_sync_device_vehicle_projection ON device_installations;

UPDATE device_installations
SET effective_from=COALESCE(effective_from,installed_at,created_at),
    effective_to=COALESCE(effective_to,removed_at),
    installed_by=COALESCE(installed_by,installer_user_id),
    updated_at=COALESCE(updated_at,NOW())
WHERE effective_from IS NULL OR (effective_to IS NULL AND removed_at IS NOT NULL)
   OR installed_by IS NULL OR updated_at IS NULL;
UPDATE device_installations SET status='Installed',updated_at=NOW()
 WHERE status='Active' AND effective_to IS NULL;
UPDATE device_installations SET status='Removed',updated_at=NOW()
 WHERE status='Active' AND effective_to IS NOT NULL;
ALTER TABLE device_installations ALTER COLUMN effective_from SET NOT NULL;

ALTER TABLE device_installations ADD COLUMN IF NOT EXISTS effective_period TSTZRANGE
  GENERATED ALWAYS AS (tstzrange(effective_from,effective_to,'[)')) STORED;

-- Preserve exact evidence before removing invalid rows from the active domain.  We do
-- not choose winners. Every conflicting legacy row is quarantined and must be resolved
-- through the governed API before it can become authoritative.
INSERT INTO device_installation_quarantine
  (company_id,device_id,vehicle_id,installation_id,reason_code,evidence_json)
SELECT i.company_id,i.device_id,i.vehicle_id,i.id,'tenant_or_reference_mismatch',to_jsonb(i)
FROM device_installations i
LEFT JOIN eld_devices d ON d.id=i.device_id
LEFT JOIN vehicles v ON v.id=i.vehicle_id
WHERE d.id IS NULL OR v.id IS NULL OR d.company_id IS DISTINCT FROM i.company_id
   OR v.company_id IS DISTINCT FROM i.company_id
ON CONFLICT DO NOTHING;

INSERT INTO device_installation_quarantine
  (company_id,device_id,vehicle_id,installation_id,reason_code,evidence_json)
SELECT DISTINCT i.company_id,i.device_id,i.vehicle_id,i.id,'overlapping_vehicle_primary_role',to_jsonb(i)
FROM device_installations i
JOIN device_installations other
  ON other.company_id=i.company_id AND other.vehicle_id=i.vehicle_id AND other.id<>i.id
 AND LOWER(other.device_role)=LOWER(i.device_role) AND other.is_primary AND i.is_primary
 AND tstzrange(other.effective_from,other.effective_to,'[)') && i.effective_period
WHERE i.status IN ('Installed','Verified','Removed','Active')
  AND other.status IN ('Installed','Verified','Removed','Active')
ON CONFLICT DO NOTHING;

INSERT INTO device_installation_quarantine
  (company_id,device_id,vehicle_id,installation_id,reason_code,evidence_json)
SELECT e.company_id,e.id,e.vehicle_id,NULL,'ambiguous_legacy_vehicle_role',
       jsonb_build_object('deviceId',e.id,'vehicleId',e.vehicle_id,'deviceCategory',e.device_category)
FROM eld_devices e
WHERE e.vehicle_id IS NOT NULL AND e.company_id IS NOT NULL AND e.deleted_at IS NULL
  AND EXISTS (
    SELECT 1 FROM eld_devices other
    WHERE other.id<>e.id AND other.company_id=e.company_id AND other.vehicle_id=e.vehicle_id
      AND other.deleted_at IS NULL
      AND LOWER(COALESCE(NULLIF(other.device_category,''),'GPS'))=
          LOWER(COALESCE(NULLIF(e.device_category,''),'GPS')))
ON CONFLICT DO NOTHING;

INSERT INTO device_installation_quarantine
  (company_id,device_id,vehicle_id,installation_id,reason_code,evidence_json)
SELECT DISTINCT i.company_id,i.device_id,i.vehicle_id,i.id,'overlapping_device_installation',to_jsonb(i)
FROM device_installations i
JOIN device_installations other
  ON other.company_id=i.company_id AND other.device_id=i.device_id AND other.id<>i.id
 AND tstzrange(other.effective_from,other.effective_to,'[)') && i.effective_period
WHERE i.status IN ('Installed','Verified','Removed','Active')
  AND other.status IN ('Installed','Verified','Removed','Active')
ON CONFLICT DO NOTHING;

UPDATE device_installations i
SET status='Quarantined',updated_at=NOW(),failure_reason=COALESCE(failure_reason,q.reason_code)
FROM device_installation_quarantine q
WHERE q.installation_id=i.id AND q.resolved_at IS NULL
  AND i.status IN ('Installed','Verified','Removed','Active');

-- Detect cross-column namespace ambiguity. IMEI/serial are lookup identifiers, never
-- credentials; an identifier must still resolve exactly one inventory row.
INSERT INTO device_installation_quarantine
  (company_id,device_id,vehicle_id,installation_id,reason_code,evidence_json)
SELECT d.company_id,d.id,d.vehicle_id,NULL,'ambiguous_device_identifier',
       jsonb_build_object('deviceId',d.id,'serial',d.device_serial,'imei',d.imei,'conflictsWith',other.id)
FROM eld_devices d JOIN eld_devices other ON other.id<>d.id
WHERE d.deleted_at IS NULL AND other.deleted_at IS NULL
  AND ((NULLIF(REGEXP_REPLACE(d.imei,'[^0-9]','','g'),'') IS NOT NULL
        AND REGEXP_REPLACE(d.imei,'[^0-9]','','g')=UPPER(BTRIM(other.device_serial)))
    OR (NULLIF(BTRIM(d.device_serial),'') IS NOT NULL
        AND UPPER(BTRIM(d.device_serial))=REGEXP_REPLACE(other.imei,'[^0-9]','','g')))
ON CONFLICT DO NOTHING;

-- Cross-column serial/IMEI collisions are an authorization ambiguity, not merely a
-- reporting concern.  Put every affected inventory row on a database-enforced hold;
-- neither native nor gateway ingest accepts a Quarantined device.
UPDATE eld_devices d SET device_state='Quarantined',updated_at=NOW()
WHERE d.deleted_at IS NULL AND EXISTS (
  SELECT 1 FROM device_installation_quarantine q
  WHERE q.device_id=d.id AND q.resolved_at IS NULL
    AND q.reason_code='ambiguous_device_identifier'
);

-- Keep the namespace safe after migration too. A newly registered or renamed
-- serial/IMEI that collides in either column is persisted only on quarantine so
-- operators retain the submitted evidence without making lookup ambiguous.
CREATE OR REPLACE FUNCTION stage80_hold_ambiguous_device_identifier()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE conflict RECORD;
BEGIN
  SELECT other.id,other.company_id,other.vehicle_id,other.device_serial,other.imei
    INTO conflict
  FROM eld_devices other
  WHERE other.id<>NEW.id AND other.deleted_at IS NULL
    AND (
      UPPER(BTRIM(other.device_serial))=UPPER(BTRIM(NEW.device_serial))
      OR (NULLIF(REGEXP_REPLACE(NEW.imei,'[^0-9]','','g'),'') IS NOT NULL
          AND REGEXP_REPLACE(other.imei,'[^0-9]','','g')=REGEXP_REPLACE(NEW.imei,'[^0-9]','','g'))
      OR (NULLIF(REGEXP_REPLACE(NEW.imei,'[^0-9]','','g'),'') IS NOT NULL
          AND UPPER(BTRIM(other.device_serial))=REGEXP_REPLACE(NEW.imei,'[^0-9]','','g'))
      OR (NULLIF(REGEXP_REPLACE(other.imei,'[^0-9]','','g'),'') IS NOT NULL
          AND REGEXP_REPLACE(other.imei,'[^0-9]','','g')=UPPER(BTRIM(NEW.device_serial)))
    )
  ORDER BY other.id LIMIT 1;
  IF FOUND THEN
    UPDATE eld_devices SET device_state='Quarantined',updated_at=NOW() WHERE id=conflict.id;
    INSERT INTO device_installation_quarantine
      (company_id,device_id,vehicle_id,reason_code,evidence_json)
    VALUES
      (conflict.company_id,conflict.id,conflict.vehicle_id,'ambiguous_device_identifier',
       jsonb_build_object('deviceId',conflict.id,'serial',conflict.device_serial,'imei',conflict.imei,'conflictsWith',NEW.id)),
      (NEW.company_id,NEW.id,NEW.vehicle_id,'ambiguous_device_identifier',
       jsonb_build_object('deviceId',NEW.id,'serial',NEW.device_serial,'imei',NEW.imei,'conflictsWith',conflict.id))
    ON CONFLICT DO NOTHING;
    NEW.device_state:='Quarantined';
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage80_hold_ambiguous_device_identifier ON eld_devices;
CREATE TRIGGER trg_stage80_hold_ambiguous_device_identifier
BEFORE INSERT OR UPDATE OF device_serial,imei ON eld_devices
FOR EACH ROW EXECUTE FUNCTION stage80_hold_ambiguous_device_identifier();

-- Backfill only unambiguous, tenant-coherent mutable pointers. The source and
-- commissioning result make their lower assurance explicit.
INSERT INTO device_installations
  (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,
   effective_from,installed_at,commissioning_method,commissioning_result,
   assignment_reason,source,correlation_id,idempotency_key)
SELECT e.company_id,COALESCE(e.branch_id,v.branch_id),e.id,e.vehicle_id,'Installed',
       COALESCE(NULLIF(e.device_category,''),'GPS'),TRUE,
       GREATEST(COALESCE(e.updated_at,e.created_at,NOW()),
                COALESCE((SELECT MAX(history.effective_to) FROM device_installations history
                          WHERE history.company_id=e.company_id AND history.device_id=e.id),'-infinity'::timestamptz)),
       GREATEST(COALESCE(e.updated_at,e.created_at,NOW()),
                COALESCE((SELECT MAX(history.effective_to) FROM device_installations history
                          WHERE history.company_id=e.company_id AND history.device_id=e.id),'-infinity'::timestamptz)),
       'legacy_backfill','unverified','Legacy mutable device/vehicle projection','legacy_backfill',
       'stage80-legacy-'||e.id::text,'stage80-legacy-'||e.id::text
FROM eld_devices e
JOIN vehicles v ON v.id=e.vehicle_id AND v.company_id=e.company_id AND v.deleted_at IS NULL
WHERE e.vehicle_id IS NOT NULL AND e.company_id IS NOT NULL AND e.deleted_at IS NULL
  AND NOT EXISTS (
    SELECT 1 FROM device_installations i
    WHERE i.company_id=e.company_id AND i.device_id=e.id
      AND i.effective_to IS NULL AND i.status IN ('Installed','Verified')
  )
  AND NOT EXISTS (SELECT 1 FROM device_installation_quarantine q
                  WHERE q.device_id=e.id AND q.resolved_at IS NULL)
ON CONFLICT DO NOTHING;

CREATE UNIQUE INDEX IF NOT EXISTS uq_eld_devices_company_id_id ON eld_devices(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicles_company_id_id ON vehicles(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_device_installations_company_id_id ON device_installations(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_device_installations_idempotency
  ON device_installations(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;

ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS fk_stage80_installation_device;
ALTER TABLE device_installations ADD CONSTRAINT fk_stage80_installation_device
  FOREIGN KEY (company_id,device_id) REFERENCES eld_devices(company_id,id) NOT VALID;
ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS fk_stage80_installation_vehicle;
ALTER TABLE device_installations ADD CONSTRAINT fk_stage80_installation_vehicle
  FOREIGN KEY (company_id,vehicle_id) REFERENCES vehicles(company_id,id) NOT VALID;
ALTER TABLE device_installation_evidence DROP CONSTRAINT IF EXISTS fk_stage80_evidence_installation;
ALTER TABLE device_installation_evidence ADD CONSTRAINT fk_stage80_evidence_installation
  FOREIGN KEY (company_id,installation_id) REFERENCES device_installations(company_id,id) NOT VALID;
ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS ck_stage80_installation_status;
ALTER TABLE device_installations ADD CONSTRAINT ck_stage80_installation_status CHECK
  (status IN ('Provisioned','Installed','Verified','Removed','Failed','Quarantined')) NOT VALID;
ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS ck_stage80_installation_time;
ALTER TABLE device_installations ADD CONSTRAINT ck_stage80_installation_time CHECK
  (effective_to IS NULL OR effective_to>effective_from) NOT VALID;
ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS ck_stage80_installation_role;
ALTER TABLE device_installations ADD CONSTRAINT ck_stage80_installation_role CHECK
  (device_role IN ('GPS','ELD','Dashcam','OBD-II','J1939/CAN','Temperature','Fuel','Tire','BLE Gateway','Other')) NOT VALID;

-- Installation verification is part of the governed device lifecycle.  Keep the
-- immutable transition ledger aligned with the inventory state machine extended
-- by Stage 66; otherwise an installation can update eld_devices but cannot record
-- the corresponding evidence row.
ALTER TABLE device_state_transitions DROP CONSTRAINT IF EXISTS ck_dst_states;
ALTER TABLE device_state_transitions ADD CONSTRAINT ck_dst_states CHECK (
  to_state IN (
    'Provisioned','Registered','Enrolled','Installed','Verified','Activated','Online','Idle','Offline',
    'Degraded','Maintenance','Suspended','Quarantined','Lost','Faulty',
    'Decommissioning','Decommissioned','Retired'
  ) AND (
    from_state IS NULL OR from_state IN (
      'Provisioned','Registered','Enrolled','Installed','Verified','Activated','Online','Idle','Offline',
      'Degraded','Maintenance','Suspended','Quarantined','Lost','Faulty',
      'Decommissioning','Decommissioned','Retired'
    )
  )
) NOT VALID;

CREATE OR REPLACE FUNCTION stage80_enforce_installation_scope()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE device_branch BIGINT; vehicle_branch BIGINT; normalized_role TEXT;
BEGIN
  SELECT role INTO normalized_role
  FROM unnest(ARRAY['GPS','ELD','Dashcam','OBD-II','J1939/CAN','Temperature','Fuel','Tire','BLE Gateway','Other']) role
  WHERE LOWER(role)=LOWER(BTRIM(NEW.device_role)) LIMIT 1;
  IF normalized_role IS NULL THEN
    RAISE EXCEPTION 'unsupported installation device role' USING ERRCODE='23514';
  END IF;
  NEW.device_role:=normalized_role;
  SELECT branch_id INTO device_branch FROM eld_devices
   WHERE id=NEW.device_id AND company_id=NEW.company_id AND deleted_at IS NULL;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'installation device is not an active member of the tenant' USING ERRCODE='23503';
  END IF;
  SELECT branch_id INTO vehicle_branch FROM vehicles
   WHERE id=NEW.vehicle_id AND company_id=NEW.company_id AND deleted_at IS NULL;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'installation vehicle is not an active member of the tenant' USING ERRCODE='23503';
  END IF;
  IF device_branch IS NOT NULL AND device_branch IS DISTINCT FROM vehicle_branch THEN
    RAISE EXCEPTION 'installation device and vehicle must belong to the same branch' USING ERRCODE='23514';
  END IF;
  IF NEW.branch_id IS DISTINCT FROM vehicle_branch THEN
    RAISE EXCEPTION 'installation branch must match the vehicle branch' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage80_enforce_installation_scope ON device_installations;
CREATE TRIGGER trg_stage80_enforce_installation_scope
BEFORE INSERT OR UPDATE OF company_id,branch_id,device_id,vehicle_id ON device_installations
FOR EACH ROW EXECUTE FUNCTION stage80_enforce_installation_scope();

-- Installation identity and closed history are evidence. Lifecycle operations may
-- commission an open row or close it once, but may never retarget/re-date it or
-- rewrite a previously closed period.
CREATE OR REPLACE FUNCTION stage80_preserve_installation_history()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP='DELETE' THEN
    IF session_user IN ('opstrax_app','opstrax_system') THEN
      RAISE EXCEPTION 'installation history cannot be deleted by a runtime identity' USING ERRCODE='55000';
    END IF;
    RETURN OLD;
  END IF;
  IF NEW.company_id IS DISTINCT FROM OLD.company_id
     OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
     OR NEW.device_id IS DISTINCT FROM OLD.device_id
     OR NEW.vehicle_id IS DISTINCT FROM OLD.vehicle_id
     OR NEW.device_role IS DISTINCT FROM OLD.device_role
     OR NEW.is_primary IS DISTINCT FROM OLD.is_primary
     OR NEW.effective_from IS DISTINCT FROM OLD.effective_from
     OR NEW.installed_at IS DISTINCT FROM OLD.installed_at
     OR NEW.installer_user_id IS DISTINCT FROM OLD.installer_user_id
     OR NEW.installed_by IS DISTINCT FROM OLD.installed_by
     OR NEW.source IS DISTINCT FROM OLD.source
     OR NEW.correlation_id IS DISTINCT FROM OLD.correlation_id
     OR NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key
     OR NEW.replaced_installation_id IS DISTINCT FROM OLD.replaced_installation_id
     OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
    RAISE EXCEPTION 'installation identity and effective start are immutable' USING ERRCODE='55000';
  END IF;
  IF OLD.effective_to IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
    RAISE EXCEPTION 'closed installation history is immutable' USING ERRCODE='55000';
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_stage80_preserve_installation_history ON device_installations;
CREATE TRIGGER trg_stage80_preserve_installation_history
BEFORE UPDATE OR DELETE ON device_installations
FOR EACH ROW EXECUTE FUNCTION stage80_preserve_installation_history();

CREATE OR REPLACE FUNCTION stage80_preserve_installation_evidence()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP='DELETE' AND session_user NOT IN ('opstrax_app','opstrax_system') THEN
    RETURN OLD;
  END IF;
  RAISE EXCEPTION 'device installation evidence is append-only' USING ERRCODE='55000';
END $$;
DROP TRIGGER IF EXISTS trg_stage80_preserve_installation_evidence ON device_installation_evidence;
CREATE TRIGGER trg_stage80_preserve_installation_evidence
BEFORE UPDATE OR DELETE ON device_installation_evidence
FOR EACH ROW EXECUTE FUNCTION stage80_preserve_installation_evidence();

ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS ex_stage80_device_installation_period;
ALTER TABLE device_installations ADD CONSTRAINT ex_stage80_device_installation_period
  EXCLUDE USING gist (company_id WITH =,device_id WITH =,effective_period WITH &&)
  WHERE (status IN ('Installed','Verified','Removed'));
ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS ex_stage80_vehicle_primary_role_period;
ALTER TABLE device_installations ADD CONSTRAINT ex_stage80_vehicle_primary_role_period
  EXCLUDE USING gist (company_id WITH =,vehicle_id WITH =,(LOWER(BTRIM(device_role))) WITH =,effective_period WITH &&)
  WHERE (status IN ('Installed','Verified','Removed') AND is_primary);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage80_vehicle_primary_role
  ON device_installations(company_id,vehicle_id,device_role)
  WHERE effective_to IS NULL AND status IN ('Installed','Verified') AND is_primary;
CREATE INDEX IF NOT EXISTS idx_stage80_installation_history
  ON device_installations(company_id,device_id,effective_from DESC,id DESC);
CREATE INDEX IF NOT EXISTS idx_stage80_vehicle_installations
  ON device_installations(company_id,vehicle_id,effective_from DESC,id DESC);

ALTER TABLE location_events ADD COLUMN IF NOT EXISTS installation_id BIGINT NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS assignment_id BIGINT NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS trip_id BIGINT NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS battery_voltage NUMERIC(10, 3) NULL;
-- TelemetryPositions always projects the reverse-geocode cache. Stage30 was a
-- legacy optional migration, so the protected owner chain must reconcile it.
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS address TEXT NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS installation_id BIGINT NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS assignment_id BIGINT NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS trip_id BIGINT NULL;
ALTER TABLE telemetry_alerts ADD COLUMN IF NOT EXISTS installation_id BIGINT NULL;
ALTER TABLE telemetry_alerts ADD COLUMN IF NOT EXISTS assignment_id BIGINT NULL;
ALTER TABLE telemetry_alerts ADD COLUMN IF NOT EXISTS trip_id BIGINT NULL;
ALTER TABLE canonical_telemetry_events ADD COLUMN IF NOT EXISTS installation_id BIGINT NULL;
ALTER TABLE canonical_telemetry_events ADD COLUMN IF NOT EXISTS assignment_id BIGINT NULL;
ALTER TABLE canonical_telemetry_events ADD COLUMN IF NOT EXISTS trip_id BIGINT NULL;

-- A driver may accept an assignment before reaching the vehicle, but departure is
-- governed by explicit vehicle confirmation and a current pre-trip DVIR. These columns
-- preserve that evidence on the canonical dispatch assignment rather than in device state.
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS assigned_by_user_id BIGINT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS vehicle_confirmed_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS vehicle_confirmed_by_driver_id BIGINT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS vehicle_confirmation_method VARCHAR(40) NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS vehicle_confirmation_reference VARCHAR(160) NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS pretrip_dvir_id BIGINT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS operational_started_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS supersedes_assignment_id BIGINT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS driver_change_reason VARCHAR(500) NULL;
ALTER TABLE assignment_confirmations ADD COLUMN IF NOT EXISTS dispatch_assignment_id BIGINT NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS trip_id BIGINT NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS template_id BIGINT NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS signature_hash VARCHAR(64) NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS odometer_miles DECIMAL(12,2) NULL;
ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS engine_hours DECIMAL(12,2) NULL;
CREATE TABLE IF NOT EXISTS dvir_inspection_results (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  dvir_report_id BIGINT NOT NULL,
  item_category VARCHAR(120) NOT NULL,
  item_name VARCHAR(220) NOT NULL,
  result VARCHAR(40) NOT NULL DEFAULT 'pass',
  severity VARCHAR(40) NOT NULL DEFAULT 'minor',
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE dvir_inspection_results ADD COLUMN IF NOT EXISTS checklist_item_id BIGINT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage80_dvir_templates_company_id_id
  ON dvir_templates(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage80_inspection_items_company_id_id
  ON inspection_checklist_items(company_id,id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stage80_dvir_reports_company_id_id
  ON dvir_reports(company_id,id);
ALTER TABLE dvir_reports DROP CONSTRAINT IF EXISTS fk_stage80_dvir_report_template_scope;
ALTER TABLE dvir_reports ADD CONSTRAINT fk_stage80_dvir_report_template_scope
  FOREIGN KEY(company_id,template_id) REFERENCES dvir_templates(company_id,id) NOT VALID;
ALTER TABLE dvir_reports VALIDATE CONSTRAINT fk_stage80_dvir_report_template_scope;
ALTER TABLE dvir_inspection_results DROP CONSTRAINT IF EXISTS fk_stage80_dvir_result_item_scope;
ALTER TABLE dvir_inspection_results ADD CONSTRAINT fk_stage80_dvir_result_item_scope
  FOREIGN KEY(company_id,checklist_item_id) REFERENCES inspection_checklist_items(company_id,id) NOT VALID;
ALTER TABLE dvir_inspection_results VALIDATE CONSTRAINT fk_stage80_dvir_result_item_scope;
ALTER TABLE dvir_inspection_results DROP CONSTRAINT IF EXISTS fk_stage80_dvir_result_report_scope;
ALTER TABLE dvir_inspection_results ADD CONSTRAINT fk_stage80_dvir_result_report_scope
  FOREIGN KEY(company_id,dvir_report_id) REFERENCES dvir_reports(company_id,id) NOT VALID;
ALTER TABLE dvir_inspection_results VALIDATE CONSTRAINT fk_stage80_dvir_result_report_scope;
ALTER TABLE dispatch_assignments DROP CONSTRAINT IF EXISTS ck_stage80_vehicle_confirmation_method;
ALTER TABLE dispatch_assignments ADD CONSTRAINT ck_stage80_vehicle_confirmation_method CHECK
  (vehicle_confirmation_method IS NULL OR vehicle_confirmation_method IN ('unit_suffix','vin_suffix','qr','nfc')) NOT VALID;
CREATE INDEX IF NOT EXISTS idx_stage80_dispatch_assignment_lineage
  ON dispatch_assignments(company_id,supersedes_assignment_id) WHERE supersedes_assignment_id IS NOT NULL;

-- A compatibility projection cannot be independently edited. This trigger derives it
-- from the authoritative open installation after every governed ledger mutation.
CREATE OR REPLACE FUNCTION stage80_sync_device_vehicle_projection()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER
SET search_path=pg_catalog,public AS $$
DECLARE target_device BIGINT; target_company BIGINT; projected_vehicle BIGINT;
BEGIN
  target_device:=COALESCE(NEW.device_id,OLD.device_id);
  target_company:=COALESCE(NEW.company_id,OLD.company_id);
  SELECT vehicle_id INTO projected_vehicle FROM device_installations
   WHERE company_id=target_company AND device_id=target_device
     AND effective_from<=NOW() AND effective_to IS NULL AND status IN ('Installed','Verified')
   ORDER BY effective_from DESC,id DESC LIMIT 1;
  UPDATE eld_devices SET vehicle_id=projected_vehicle,driver_id=NULL,updated_at=NOW()
   WHERE id=target_device AND company_id=target_company;
  RETURN COALESCE(NEW,OLD);
END $$;
REVOKE ALL ON FUNCTION stage80_sync_device_vehicle_projection() FROM PUBLIC;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    REVOKE ALL ON FUNCTION stage80_sync_device_vehicle_projection() FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    REVOKE ALL ON FUNCTION stage80_sync_device_vehicle_projection() FROM opstrax_system;
  END IF;
END $$;
DROP TRIGGER IF EXISTS trg_stage80_sync_device_vehicle_projection ON device_installations;
CREATE TRIGGER trg_stage80_sync_device_vehicle_projection
AFTER INSERT OR UPDATE OF vehicle_id,status,effective_from,effective_to ON device_installations
FOR EACH ROW EXECUTE FUNCTION stage80_sync_device_vehicle_projection();

WITH current_install AS (
  SELECT DISTINCT ON (i.company_id,i.device_id)
    i.company_id,i.device_id,i.vehicle_id
  FROM device_installations i
  WHERE i.effective_from<=NOW() AND i.effective_to IS NULL AND i.status IN ('Installed','Verified')
  ORDER BY i.company_id,i.device_id,i.effective_from DESC,i.id DESC
)
UPDATE eld_devices e
SET vehicle_id=current_install.vehicle_id,driver_id=NULL,updated_at=NOW()
FROM current_install
WHERE e.company_id=current_install.company_id AND e.id=current_install.device_id
  AND e.deleted_at IS NULL AND e.vehicle_id IS DISTINCT FROM current_install.vehicle_id;
UPDATE eld_devices e SET vehicle_id=NULL,driver_id=NULL,updated_at=NOW()
WHERE e.deleted_at IS NULL AND e.vehicle_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM device_installations i WHERE i.company_id=e.company_id AND i.device_id=e.id
      AND i.effective_from<=NOW() AND i.effective_to IS NULL AND i.status IN ('Installed','Verified')
  );

REVOKE ALL ON TABLE device_installation_quarantine FROM PUBLIC;
REVOKE ALL ON SEQUENCE device_installation_quarantine_id_seq FROM PUBLIC;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    REVOKE ALL ON TABLE device_installation_quarantine FROM opstrax_app;
    REVOKE ALL ON SEQUENCE device_installation_quarantine_id_seq FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    REVOKE ALL ON TABLE device_installation_quarantine FROM opstrax_system;
    REVOKE ALL ON SEQUENCE device_installation_quarantine_id_seq FROM opstrax_system;
    GRANT SELECT,INSERT,UPDATE ON TABLE device_installation_quarantine TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE device_installation_quarantine_id_seq TO opstrax_system;
  END IF;
END $$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_14_stage80_fleet_identity_backbone',
        'Authoritative effective-dated vehicle/device installation and telemetry identity backbone')
ON CONFLICT(version) DO NOTHING;

COMMIT;

-- Forward recovery: never drop installation/quarantine evidence. To roll back the
-- application, deploy the previous SHA; the compatibility trigger keeps legacy
-- eld_devices.vehicle_id readers coherent. Resolve quarantine rows explicitly, then
-- commission a replacement installation through the governed API.
