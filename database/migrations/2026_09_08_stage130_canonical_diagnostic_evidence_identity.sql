-- Stage 130 — indexed identity proof for customer-visible canonical diagnostics.
--
-- A fault row is classified as canonical CAN evidence only when its exact company,
-- registry device, vehicle, observation time, envelope event id and DTC identity
-- all match the immutable canonical telemetry event. Raw evidence supplied through
-- any other ingest route cannot create that classification.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE INDEX IF NOT EXISTS idx_stage130_canonical_diagnostic_identity
  ON canonical_telemetry_events (
    company_id,
    device_id,
    event_time,
    (payload->>'_envelopeEventId'))
  WHERE event_type='diagnostic.event'
    AND source='DirectDevice'
    AND UPPER(protocol)='J1939';

COMMENT ON INDEX idx_stage130_canonical_diagnostic_identity IS
  'Supports exact immutable-event proof before a tenant fault row is labeled canonical CAN evidence.';

COMMIT;
