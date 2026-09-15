-- Stage 128 — engineering-declared compatibility capability catalog.
--
-- A declaration describes what a frozen software candidate is designed to
-- support for one exact hardware/firmware tuple. It is not provider, physical,
-- or certification evidence. Certification references and dates remain empty
-- until a later evidence-bound certification-candidate migration exists.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE OR REPLACE FUNCTION stage128_valid_capability_list(values_to_check TEXT[], maximum_items INT)
RETURNS BOOLEAN
LANGUAGE SQL
IMMUTABLE
AS $fn$
  SELECT values_to_check IS NOT NULL
     AND cardinality(values_to_check) <= maximum_items
     AND NOT EXISTS (
       SELECT 1 FROM unnest(values_to_check) AS item(value)
        WHERE value IS NULL
           OR LENGTH(BTRIM(value)) NOT BETWEEN 1 AND 120
     )
     AND cardinality(values_to_check) = (
       SELECT COUNT(DISTINCT LOWER(BTRIM(value)))
         FROM unnest(values_to_check) AS item(value)
     );
$fn$;

ALTER TABLE device_compatibility_candidates
  ADD COLUMN IF NOT EXISTS capability_declaration_status VARCHAR(40) NOT NULL DEFAULT 'NotRecorded',
  ADD COLUMN IF NOT EXISTS protocol_names TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
  ADD COLUMN IF NOT EXISTS supported_fields TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
  ADD COLUMN IF NOT EXISTS supported_events TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
  ADD COLUMN IF NOT EXISTS supported_commands TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
  ADD COLUMN IF NOT EXISTS known_limitations VARCHAR(2000) NOT NULL
    DEFAULT 'Capability metadata has not been recorded for this candidate.',
  ADD COLUMN IF NOT EXISTS declaration_source_reference VARCHAR(240) NULL,
  ADD COLUMN IF NOT EXISTS declared_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS catalog_support_tier VARCHAR(32) NOT NULL DEFAULT 'Unverified',
  ADD COLUMN IF NOT EXISTS certification_reference VARCHAR(240) NULL,
  ADD COLUMN IF NOT EXISTS certification_date DATE NULL,
  ADD COLUMN IF NOT EXISTS physical_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS provider_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS certification_claim BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE device_compatibility_candidates
  DROP CONSTRAINT IF EXISTS ck_stage128_capability_status,
  ADD CONSTRAINT ck_stage128_capability_status CHECK (
    capability_declaration_status IN ('NotRecorded','EngineeringDeclaredUnverified')
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_protocol_list,
  ADD CONSTRAINT ck_stage128_protocol_list CHECK (
    stage128_valid_capability_list(protocol_names,16)
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_field_list,
  ADD CONSTRAINT ck_stage128_field_list CHECK (
    stage128_valid_capability_list(supported_fields,64)
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_event_list,
  ADD CONSTRAINT ck_stage128_event_list CHECK (
    stage128_valid_capability_list(supported_events,64)
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_command_list,
  ADD CONSTRAINT ck_stage128_command_list CHECK (
    stage128_valid_capability_list(supported_commands,32)
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_limitations,
  ADD CONSTRAINT ck_stage128_limitations CHECK (
    LENGTH(BTRIM(known_limitations)) BETWEEN 3 AND 2000
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_declaration_shape,
  ADD CONSTRAINT ck_stage128_declaration_shape CHECK (
    (
      capability_declaration_status='NotRecorded'
      AND cardinality(protocol_names)=0
      AND cardinality(supported_fields)=0
      AND cardinality(supported_events)=0
      AND cardinality(supported_commands)=0
      AND declaration_source_reference IS NULL
      AND declared_at IS NULL
    ) OR (
      capability_declaration_status='EngineeringDeclaredUnverified'
      AND cardinality(protocol_names)>0
      AND cardinality(supported_fields)>0
      AND cardinality(supported_events)>0
      AND LENGTH(BTRIM(declaration_source_reference)) BETWEEN 3 AND 240
      AND declared_at IS NOT NULL
    )
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_unverified_tier,
  ADD CONSTRAINT ck_stage128_unverified_tier CHECK (catalog_support_tier='Unverified') NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_no_certification_record,
  ADD CONSTRAINT ck_stage128_no_certification_record CHECK (
    certification_reference IS NULL AND certification_date IS NULL
  ) NOT VALID,
  DROP CONSTRAINT IF EXISTS ck_stage128_no_evidence_claims,
  ADD CONSTRAINT ck_stage128_no_evidence_claims CHECK (
    physical_evidence_claim=FALSE
    AND provider_evidence_claim=FALSE
    AND certification_claim=FALSE
  ) NOT VALID;

ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_capability_status;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_protocol_list;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_field_list;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_event_list;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_command_list;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_limitations;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_declaration_shape;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_unverified_tier;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_no_certification_record;
ALTER TABLE device_compatibility_candidates VALIDATE CONSTRAINT ck_stage128_no_evidence_claims;

CREATE OR REPLACE FUNCTION stage128_protect_capability_declaration()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF OLD.capability_declaration_status='EngineeringDeclaredUnverified'
     AND ROW(
       NEW.capability_declaration_status,NEW.protocol_names,NEW.supported_fields,
       NEW.supported_events,NEW.supported_commands,NEW.known_limitations,
       NEW.declaration_source_reference,NEW.declared_at
     ) IS DISTINCT FROM ROW(
       OLD.capability_declaration_status,OLD.protocol_names,OLD.supported_fields,
       OLD.supported_events,OLD.supported_commands,OLD.known_limitations,
       OLD.declaration_source_reference,OLD.declared_at
     ) THEN
    RAISE EXCEPTION 'Stage128 capability declaration is immutable once recorded'
      USING ERRCODE='23514',CONSTRAINT='ck_stage128_capability_declaration_immutable';
  END IF;
  IF OLD.capability_declaration_status='NotRecorded'
     AND NEW.capability_declaration_status NOT IN ('NotRecorded','EngineeringDeclaredUnverified') THEN
    RAISE EXCEPTION 'Stage128 capability declaration transition is invalid'
      USING ERRCODE='23514',CONSTRAINT='ck_stage128_capability_declaration_transition';
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage128_protect_capability_declaration ON device_compatibility_candidates;
CREATE TRIGGER trg_stage128_protect_capability_declaration
BEFORE UPDATE ON device_compatibility_candidates
FOR EACH ROW EXECUTE FUNCTION stage128_protect_capability_declaration();

COMMENT ON COLUMN device_compatibility_candidates.capability_declaration_status IS
  'Engineering declaration state only. It is never provider, physical, or certification evidence.';
COMMENT ON COLUMN device_compatibility_candidates.known_limitations IS
  'Known engineering limits or an explicit statement that capability metadata is not yet recorded.';
COMMENT ON COLUMN device_compatibility_candidates.catalog_support_tier IS
  'Locked to Unverified until a later evidence-bound certification candidate is accepted.';

REVOKE ALL ON FUNCTION stage128_valid_capability_list(TEXT[],INT) FROM PUBLIC;
REVOKE ALL ON FUNCTION stage128_protect_capability_declaration() FROM PUBLIC;

DO $stage128_runtime_security$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    GRANT SELECT ON device_compatibility_candidates TO opstrax_app;
    REVOKE INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER ON device_compatibility_candidates FROM opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    GRANT EXECUTE ON FUNCTION stage128_valid_capability_list(TEXT[],INT) TO opstrax_system;
    GRANT EXECUTE ON FUNCTION stage128_protect_capability_declaration() TO opstrax_system;
  END IF;
END
$stage128_runtime_security$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_08_stage128_device_compatibility_capability_catalog',
        'Immutable engineering-declared capability catalog locked to Unverified and ExternalHold')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
