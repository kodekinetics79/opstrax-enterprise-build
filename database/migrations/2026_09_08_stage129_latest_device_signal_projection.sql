-- Stage 129 — tenant-readable latest canonical device-signal projection.
--
-- The append-only canonical telemetry store remains system-only. This bounded
-- projection exposes the latest value and its complete truth labels to the
-- customer API without granting access to raw packets or the historical store.
-- A projected observation is operational evidence only and never certifies a
-- hardware, firmware, provider or regulatory candidate.

BEGIN;
SET LOCAL lock_timeout = '10s';

CREATE UNIQUE INDEX IF NOT EXISTS uq_stage129_eld_devices_owner
  ON eld_devices(company_id,id);

CREATE TABLE IF NOT EXISTS latest_device_signals (
  company_id BIGINT NOT NULL,
  device_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  signal_path VARCHAR(240) NOT NULL,
  value_json JSONB NULL,
  unit VARCHAR(32) NOT NULL DEFAULT '',
  availability VARCHAR(32) NOT NULL,
  source VARCHAR(32) NOT NULL,
  transport VARCHAR(32) NOT NULL,
  protocol VARCHAR(80) NOT NULL,
  adapter_name VARCHAR(120) NOT NULL,
  adapter_version VARCHAR(40) NOT NULL,
  trust_score NUMERIC(4,3) NOT NULL,
  confidence NUMERIC(4,3) NOT NULL,
  quality_flags JSONB NOT NULL DEFAULT '{}'::JSONB,
  evidence_headers JSONB NOT NULL DEFAULT '{}'::JSONB,
  event_id UUID NOT NULL,
  correlation_id UUID NOT NULL,
  observed_at TIMESTAMPTZ NOT NULL,
  gateway_received_at TIMESTAMPTZ NOT NULL,
  normalized_at TIMESTAMPTZ NOT NULL,
  certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY(company_id,device_id,signal_path),
  CONSTRAINT fk_stage129_signal_device FOREIGN KEY(company_id,device_id)
    REFERENCES eld_devices(company_id,id),
  CONSTRAINT ck_stage129_signal_path CHECK (signal_path ~ '^[A-Za-z][A-Za-z0-9_.]{2,239}$'),
  CONSTRAINT ck_stage129_signal_availability CHECK (
    availability IN ('Available','Stale','ParameterSpecific','Error','NotAvailable')),
  CONSTRAINT ck_stage129_signal_value_shape CHECK (
    (availability IN ('Available','Stale') AND value_json IS NOT NULL)
    OR (availability IN ('ParameterSpecific','Error','NotAvailable') AND value_json IS NULL)),
  CONSTRAINT ck_stage129_signal_source CHECK (
    source IN ('DirectDevice','VendorCloud','MobileApp','Simulator','Seed','Import','Manual')),
  CONSTRAINT ck_stage129_signal_transport CHECK (
    transport IN ('Tcp','Udp','Http','Mqtt','WebSocket','VendorWebhook','VendorPoll','Can')),
  CONSTRAINT ck_stage129_signal_protocol CHECK (LENGTH(BTRIM(protocol)) BETWEEN 1 AND 80),
  CONSTRAINT ck_stage129_signal_adapter CHECK (
    LENGTH(BTRIM(adapter_name)) BETWEEN 1 AND 120
    AND LENGTH(BTRIM(adapter_version)) BETWEEN 1 AND 40),
  CONSTRAINT ck_stage129_signal_scores CHECK (
    trust_score BETWEEN 0 AND 1 AND confidence BETWEEN 0 AND 1),
  CONSTRAINT ck_stage129_signal_bounded_json CHECK (
    pg_column_size(value_json)<=4096
    AND pg_column_size(quality_flags)<=4096
    AND pg_column_size(evidence_headers)<=16384),
  CONSTRAINT ck_stage129_signal_times CHECK (
    observed_at<=gateway_received_at+INTERVAL '5 minutes'
    AND gateway_received_at<=normalized_at+INTERVAL '5 minutes'),
  CONSTRAINT ck_stage129_signal_no_certification CHECK (certification_claim=FALSE)
);

-- Repair every truth constraint when a prior interrupted deployment or an
-- owner-capable development runtime created the table before this migration.
ALTER TABLE latest_device_signals
  DROP CONSTRAINT IF EXISTS fk_stage129_signal_device,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_path,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_availability,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_value_shape,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_source,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_transport,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_protocol,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_adapter,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_scores,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_bounded_json,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_times,
  DROP CONSTRAINT IF EXISTS ck_stage129_signal_no_certification;
ALTER TABLE latest_device_signals
  ADD CONSTRAINT fk_stage129_signal_device FOREIGN KEY(company_id,device_id)
    REFERENCES eld_devices(company_id,id),
  ADD CONSTRAINT ck_stage129_signal_path CHECK (signal_path ~ '^[A-Za-z][A-Za-z0-9_.]{2,239}$'),
  ADD CONSTRAINT ck_stage129_signal_availability CHECK (
    availability IN ('Available','Stale','ParameterSpecific','Error','NotAvailable')),
  ADD CONSTRAINT ck_stage129_signal_value_shape CHECK (
    (availability IN ('Available','Stale') AND value_json IS NOT NULL)
    OR (availability IN ('ParameterSpecific','Error','NotAvailable') AND value_json IS NULL)),
  ADD CONSTRAINT ck_stage129_signal_source CHECK (
    source IN ('DirectDevice','VendorCloud','MobileApp','Simulator','Seed','Import','Manual')),
  ADD CONSTRAINT ck_stage129_signal_transport CHECK (
    transport IN ('Tcp','Udp','Http','Mqtt','WebSocket','VendorWebhook','VendorPoll','Can')),
  ADD CONSTRAINT ck_stage129_signal_protocol CHECK (LENGTH(BTRIM(protocol)) BETWEEN 1 AND 80),
  ADD CONSTRAINT ck_stage129_signal_adapter CHECK (
    LENGTH(BTRIM(adapter_name)) BETWEEN 1 AND 120
    AND LENGTH(BTRIM(adapter_version)) BETWEEN 1 AND 40),
  ADD CONSTRAINT ck_stage129_signal_scores CHECK (
    trust_score BETWEEN 0 AND 1 AND confidence BETWEEN 0 AND 1),
  ADD CONSTRAINT ck_stage129_signal_bounded_json CHECK (
    pg_column_size(value_json)<=4096
    AND pg_column_size(quality_flags)<=4096
    AND pg_column_size(evidence_headers)<=16384),
  ADD CONSTRAINT ck_stage129_signal_times CHECK (
    observed_at<=gateway_received_at+INTERVAL '5 minutes'
    AND gateway_received_at<=normalized_at+INTERVAL '5 minutes'),
  ADD CONSTRAINT ck_stage129_signal_no_certification CHECK (certification_claim=FALSE);

CREATE INDEX IF NOT EXISTS ix_stage129_signal_device_observed
  ON latest_device_signals(company_id,device_id,observed_at DESC,signal_path);
CREATE INDEX IF NOT EXISTS ix_stage129_signal_vehicle_observed
  ON latest_device_signals(company_id,vehicle_id,observed_at DESC)
  WHERE vehicle_id IS NOT NULL;

-- Backfill the newest historical value for each device/path when a canonical
-- signal event predates this projection. Old canonical payloads did not carry
-- Availability; a non-null value is conservatively labeled Available and a
-- null value NotAvailable. No certification claim is introduced.
INSERT INTO latest_device_signals(
  company_id,device_id,vehicle_id,signal_path,value_json,unit,availability,
  source,transport,protocol,adapter_name,adapter_version,trust_score,confidence,
  quality_flags,evidence_headers,event_id,correlation_id,observed_at,
  gateway_received_at,normalized_at,certification_claim,updated_at)
SELECT DISTINCT ON (event.company_id,event.device_id,signal.key)
  event.company_id,event.device_id,event.vehicle_id,signal.key,
  CASE WHEN signal.value->'Value'='null'::JSONB THEN NULL ELSE signal.value->'Value' END,
  COALESCE(signal.value->>'Unit',''),
  CASE signal.value->>'Availability'
    WHEN '0' THEN 'Available' WHEN 'Available' THEN 'Available'
    WHEN '1' THEN 'Stale' WHEN 'Stale' THEN 'Stale'
    WHEN '2' THEN 'ParameterSpecific' WHEN 'ParameterSpecific' THEN 'ParameterSpecific'
    WHEN '3' THEN 'Error' WHEN 'Error' THEN 'Error'
    WHEN '4' THEN 'NotAvailable' WHEN 'NotAvailable' THEN 'NotAvailable'
    ELSE CASE WHEN signal.value->'Value' IS NULL OR signal.value->'Value'='null'::JSONB
              THEN 'NotAvailable' ELSE 'Available' END END,
  CASE signal.value->>'Source'
    WHEN '0' THEN 'DirectDevice' WHEN 'DirectDevice' THEN 'DirectDevice'
    WHEN '1' THEN 'VendorCloud' WHEN 'VendorCloud' THEN 'VendorCloud'
    WHEN '2' THEN 'MobileApp' WHEN 'MobileApp' THEN 'MobileApp'
    WHEN '3' THEN 'Simulator' WHEN 'Simulator' THEN 'Simulator'
    WHEN '4' THEN 'Seed' WHEN 'Seed' THEN 'Seed'
    WHEN '5' THEN 'Import' WHEN 'Import' THEN 'Import'
    WHEN '6' THEN 'Manual' WHEN 'Manual' THEN 'Manual'
    ELSE event.source END,
  CASE event.payload->'Event'->>'Transport'
    WHEN '0' THEN 'Tcp' WHEN '1' THEN 'Udp' WHEN '2' THEN 'Http'
    WHEN '3' THEN 'Mqtt' WHEN '4' THEN 'WebSocket'
    WHEN '5' THEN 'VendorWebhook' WHEN '6' THEN 'VendorPoll'
    WHEN '7' THEN 'Can' END,
  event.protocol,
  event.provider,
  event.adapter_version,
  event.trust_score,
  LEAST(1,GREATEST(0,COALESCE(signal.value->>'Confidence',event.confidence::TEXT)::NUMERIC)),
  CASE WHEN pg_column_size(COALESCE(event.quality_flags,'{}'::JSONB))<=4096
       THEN COALESCE(event.quality_flags,'{}'::JSONB) ELSE '{}'::JSONB END,
  CASE WHEN pg_column_size(safe_headers.value)<=16384 THEN safe_headers.value ELSE '{}'::JSONB END,
  (event.payload->>'_envelopeEventId')::UUID,
  event.correlation_id,
  COALESCE(event.device_fix_time,event.event_time),
  COALESCE(event.gateway_received_at,event.ingested_at,event.event_time),
  COALESCE((event.payload->'Event'->>'NormalizedAtUtc')::TIMESTAMPTZ,event.ingested_at,event.event_time),
  FALSE,NOW()
FROM canonical_telemetry_events event
CROSS JOIN LATERAL jsonb_each(CASE
  WHEN jsonb_typeof(event.payload->'Event'->'Signals')='object'
    THEN event.payload->'Event'->'Signals'
  ELSE '{}'::JSONB END) signal
CROSS JOIN LATERAL (SELECT jsonb_strip_nulls(jsonb_build_object(
  'j1939.pgn',event.payload->'Headers'->'j1939.pgn',
  'j1939.spns',event.payload->'Headers'->'j1939.spns',
  'j1939.source_address',event.payload->'Headers'->'j1939.source_address',
  'j1939.destination_address',event.payload->'Headers'->'j1939.destination_address',
  'j1939.adapter_type',event.payload->'Headers'->'j1939.adapter_type',
  'j1939.channel',event.payload->'Headers'->'j1939.channel',
  'j1939.capture_references',event.payload->'Headers'->'j1939.capture_references')) value) safe_headers
WHERE event.event_type='vehicle.signal'
  AND event.device_id IS NOT NULL
  AND event.correlation_id IS NOT NULL
  AND event.payload->>'_envelopeEventId' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$'
  AND event.source IN ('DirectDevice','VendorCloud','MobileApp','Simulator','Seed','Import','Manual')
  AND event.payload->'Event'->>'Transport' IN ('0','1','2','3','4','5','6','7')
  AND LENGTH(BTRIM(event.protocol)) BETWEEN 1 AND 80
  AND LENGTH(BTRIM(event.provider)) BETWEEN 1 AND 120
  AND LENGTH(BTRIM(event.adapter_version)) BETWEEN 1 AND 40
  AND event.trust_score BETWEEN 0 AND 1
  AND COALESCE(signal.value->>'Confidence',event.confidence::TEXT) ~ '^(0(\.[0-9]+)?|1(\.0+)?)$'
  AND COALESCE(pg_column_size(signal.value->'Value'),0)<=4096
  AND LENGTH(COALESCE(signal.value->>'Unit',''))<=32
  AND COALESCE(event.device_fix_time,event.event_time)
        <=COALESCE(event.gateway_received_at,event.ingested_at,event.event_time)+INTERVAL '5 minutes'
  AND COALESCE(event.gateway_received_at,event.ingested_at,event.event_time)
        <=COALESCE((event.payload->'Event'->>'NormalizedAtUtc')::TIMESTAMPTZ,event.ingested_at,event.event_time)
          +INTERVAL '5 minutes'
  AND signal.key ~ '^[A-Za-z][A-Za-z0-9_.]{2,239}$'
ORDER BY event.company_id,event.device_id,signal.key,event.event_time DESC,event.ingested_at DESC,event.id DESC
ON CONFLICT(company_id,device_id,signal_path) DO NOTHING;

ALTER TABLE latest_device_signals ENABLE ROW LEVEL SECURITY;
ALTER TABLE latest_device_signals FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_ticket_app ON latest_device_signals;
DROP POLICY IF EXISTS system_control_plane ON latest_device_signals;

DO $stage129_security$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON latest_device_signals
      FOR SELECT TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE latest_device_signals FROM opstrax_app;
    GRANT SELECT ON latest_device_signals TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY system_control_plane ON latest_device_signals
      FOR ALL TO opstrax_system USING (TRUE) WITH CHECK (TRUE);
    REVOKE ALL ON TABLE latest_device_signals FROM opstrax_system;
    GRANT SELECT,INSERT,UPDATE ON latest_device_signals TO opstrax_system;
  END IF;
END
$stage129_security$;

REVOKE ALL ON TABLE latest_device_signals FROM PUBLIC;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_08_stage129_latest_device_signal_projection',
        'Tenant-readable latest canonical signal projection with explicit availability and no certification claim')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
