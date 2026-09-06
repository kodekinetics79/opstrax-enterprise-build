-- Stage 105 — cold-chain measurement authority and calibration metadata
--
-- Operator-entered observations must never advance a device heartbeat or become
-- indistinguishable from authenticated sensor/gateway telemetry. Existing readings
-- pre-date this authority boundary and are retained as LegacyUnverified evidence.

BEGIN;

ALTER TABLE fleet_tms_temperature_devices
  ADD COLUMN IF NOT EXISTS sensor_type VARCHAR(60) NOT NULL DEFAULT 'Temperature',
  ADD COLUMN IF NOT EXISTS measurement_unit VARCHAR(20) NOT NULL DEFAULT 'Celsius',
  ADD COLUMN IF NOT EXISTS calibration_status VARCHAR(30) NOT NULL DEFAULT 'NotReported',
  ADD COLUMN IF NOT EXISTS calibrated_at_utc TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS calibration_due_at_utc TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS calibration_reference VARCHAR(160) NULL,
  ADD COLUMN IF NOT EXISTS last_measurement_source VARCHAR(30) NULL,
  ADD COLUMN IF NOT EXISTS last_measurement_observed_at_utc TIMESTAMPTZ NULL;

ALTER TABLE fleet_tms_temperature_readings
  ADD COLUMN IF NOT EXISTS measurement_authority VARCHAR(40) NOT NULL DEFAULT 'LegacyUnverified',
  ADD COLUMN IF NOT EXISTS received_at_utc TIMESTAMPTZ NULL;

UPDATE fleet_tms_temperature_readings
SET received_at_utc = COALESCE(received_at_utc, created_at_utc, recorded_at_utc)
WHERE received_at_utc IS NULL;

ALTER TABLE fleet_tms_temperature_readings
  ALTER COLUMN received_at_utc SET DEFAULT NOW(),
  ALTER COLUMN received_at_utc SET NOT NULL;

ALTER TABLE fleet_tms_temperature_devices
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_device_sensor_type,
  ADD CONSTRAINT ck_ftms_temperature_device_sensor_type
    CHECK (sensor_type IN ('Temperature','Humidity','Door','Fuel','Tire','MultiSensor','Other')),
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_device_unit,
  ADD CONSTRAINT ck_ftms_temperature_device_unit
    CHECK (measurement_unit IN ('Celsius','Fahrenheit','Percent','Boolean','PSI','Liters','Other')),
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_device_calibration_status,
  ADD CONSTRAINT ck_ftms_temperature_device_calibration_status
    CHECK (calibration_status IN ('NotReported','Current','Due','Expired','NotRequired')),
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_device_calibration_dates,
  ADD CONSTRAINT ck_ftms_temperature_device_calibration_dates
    CHECK (calibrated_at_utc IS NULL OR calibration_due_at_utc IS NULL OR calibration_due_at_utc > calibrated_at_utc),
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_device_last_source,
  ADD CONSTRAINT ck_ftms_temperature_device_last_source
    CHECK (last_measurement_source IS NULL OR last_measurement_source IN ('Sensor','Gateway'));

ALTER TABLE fleet_tms_temperature_readings
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_reading_authority,
  ADD CONSTRAINT ck_ftms_temperature_reading_authority
    CHECK (measurement_authority IN ('DeviceReported','GatewayReported','OperatorObserved','ImportedHistorical','LegacyUnverified')),
  DROP CONSTRAINT IF EXISTS ck_ftms_temperature_reading_time_order,
  ADD CONSTRAINT ck_ftms_temperature_reading_time_order
    CHECK (recorded_at_utc <= received_at_utc + INTERVAL '5 minutes');

CREATE INDEX IF NOT EXISTS idx_ftms_tread_company_authority_time
  ON fleet_tms_temperature_readings (company_id, measurement_authority, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_ftms_tdev_company_calibration_due
  ON fleet_tms_temperature_devices (company_id, calibration_status, calibration_due_at_utc);

COMMIT;
