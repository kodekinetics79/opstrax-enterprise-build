-- Stage 12A: Telemetry / IoT / Live Map completion
-- Additive only. Mirrors the local schema service so the telemetry-native
-- live-state projection and correlation metadata are durable and reviewable.

ALTER TABLE IF EXISTS location_events
    ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(120) NULL;

ALTER TABLE IF EXISTS telemetry_alerts
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS ai_recommendation_id BIGINT NULL;

ALTER TABLE IF EXISTS latest_vehicle_positions
    ADD COLUMN IF NOT EXISTS source_event_id BIGINT NULL,
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS telemetry_status VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS risk_level VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS alert_count INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS open_alert_count INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS next_action VARCHAR(160) NULL,
    ADD COLUMN IF NOT EXISTS summary_json JSONB NULL,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

CREATE TABLE IF NOT EXISTS telemetry_live_asset_states (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    vehicle_id BIGINT NOT NULL,
    device_id BIGINT NULL,
    driver_id BIGINT NULL,
    vehicle_code VARCHAR(60) NULL,
    device_serial VARCHAR(120) NULL,
    driver_name VARCHAR(160) NULL,
    lat DECIMAL(10,7) NOT NULL,
    lng DECIMAL(10,7) NOT NULL,
    speed_mph DECIMAL(6,2) NOT NULL DEFAULT 0,
    heading SMALLINT NOT NULL DEFAULT 0,
    engine_status VARCHAR(40) NULL,
    telemetry_status VARCHAR(40) NOT NULL DEFAULT 'healthy',
    risk_level VARCHAR(40) NOT NULL DEFAULT 'low',
    alert_count INT NOT NULL DEFAULT 0,
    open_alert_count INT NOT NULL DEFAULT 0,
    stale_seconds BIGINT NOT NULL DEFAULT 0,
    last_event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    source_event_id BIGINT NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    source_channel VARCHAR(40) NULL,
    next_action VARCHAR(160) NULL,
    summary_json JSONB NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, vehicle_id)
);

CREATE INDEX IF NOT EXISTS idx_lvp_status ON latest_vehicle_positions(company_id, telemetry_status, risk_level);
CREATE INDEX IF NOT EXISTS idx_tlsa_company_updated ON telemetry_live_asset_states(company_id, updated_at);
CREATE INDEX IF NOT EXISTS idx_tlsa_company_risk ON telemetry_live_asset_states(company_id, risk_level, open_alert_count);

-- Rollback guidance:
-- DROP TABLE telemetry_live_asset_states;
-- ALTER TABLE location_events DROP COLUMN IF EXISTS source_channel, DROP COLUMN IF EXISTS correlation_id,
--   DROP COLUMN IF EXISTS causation_id, DROP COLUMN IF EXISTS client_generated_id, DROP COLUMN IF EXISTS idempotency_key;
-- ALTER TABLE telemetry_alerts DROP COLUMN IF EXISTS correlation_id, DROP COLUMN IF EXISTS causation_id,
--   DROP COLUMN IF EXISTS source_channel, DROP COLUMN IF EXISTS client_generated_id, DROP COLUMN IF EXISTS ai_recommendation_id;
-- ALTER TABLE latest_vehicle_positions DROP COLUMN IF EXISTS source_event_id, DROP COLUMN IF EXISTS correlation_id,
--   DROP COLUMN IF EXISTS causation_id, DROP COLUMN IF EXISTS source_channel, DROP COLUMN IF EXISTS telemetry_status,
--   DROP COLUMN IF EXISTS risk_level, DROP COLUMN IF EXISTS alert_count, DROP COLUMN IF EXISTS open_alert_count,
--   DROP COLUMN IF EXISTS next_action, DROP COLUMN IF EXISTS summary_json, DROP COLUMN IF EXISTS updated_at;
