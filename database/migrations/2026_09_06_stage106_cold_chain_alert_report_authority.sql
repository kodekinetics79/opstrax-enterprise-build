-- Stage 106 — cold-chain alert and report evidence authority
--
-- A breach alert or compliance report must retain the authority of the readings
-- that produced it. Existing alerts and reports pre-date this boundary and remain
-- stored as LegacyUnverified evidence; they are not current operating claims.

BEGIN;

ALTER TABLE fleet_tms_temperature_alerts
  ADD COLUMN IF NOT EXISTS measurement_authority VARCHAR(40) NOT NULL DEFAULT 'LegacyUnverified';

UPDATE fleet_tms_temperature_alerts alert
SET measurement_authority = reading.measurement_authority
FROM fleet_tms_temperature_readings reading
WHERE alert.company_id = reading.company_id
  AND alert.reading_id = reading.id
  AND alert.measurement_authority = 'LegacyUnverified'
  AND reading.measurement_authority <> 'LegacyUnverified';

ALTER TABLE fleet_tms_temperature_alerts
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_alert_authority,
  ADD CONSTRAINT ck_ftms_temperature_alert_authority
    CHECK (measurement_authority IN ('DeviceReported','GatewayReported','OperatorObserved','ImportedHistorical','LegacyUnverified'));

ALTER TABLE fleet_tms_cold_chain_reports
  ADD COLUMN IF NOT EXISTS evidence_authority VARCHAR(40) NOT NULL DEFAULT 'LegacyUnverified';

ALTER TABLE fleet_tms_cold_chain_reports
  DROP CONSTRAINT IF EXISTS ck_ftms_cold_chain_report_authority,
  ADD CONSTRAINT ck_ftms_cold_chain_report_authority
    CHECK (evidence_authority IN ('AuthenticatedDevice','LegacyUnverified'));

CREATE INDEX IF NOT EXISTS idx_ftms_talert_company_authority_status
  ON fleet_tms_temperature_alerts (company_id, measurement_authority, status, triggered_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_ftms_ccreport_company_authority_time
  ON fleet_tms_cold_chain_reports (company_id, evidence_authority, generated_at_utc DESC);

COMMIT;
