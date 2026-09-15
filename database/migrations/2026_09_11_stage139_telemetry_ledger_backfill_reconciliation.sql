-- Stage 139 — forward-only telemetry contract reconciliation
--
-- Stage23 introduced the migration ledger by backfilling historical versions. On a
-- newly bootstrapped database, Stage51 must create the telemetry relations before
-- the older Stage12A enrichment can run. Stage23 therefore recorded Stage12A before
-- its DDL had executed. Replaying ledgered history is unsafe on a live database, so
-- this migration supplies the same additive contract under a new immutable version.

BEGIN;

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

CREATE INDEX IF NOT EXISTS idx_lvp_status
    ON latest_vehicle_positions(company_id, telemetry_status, risk_level);
CREATE INDEX IF NOT EXISTS idx_tlsa_company_updated
    ON telemetry_live_asset_states(company_id, updated_at);
CREATE INDEX IF NOT EXISTS idx_tlsa_company_risk
    ON telemetry_live_asset_states(company_id, risk_level, open_alert_count);

DO $stage139_verify$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('location_events','source_channel'),
            ('location_events','correlation_id'),
            ('location_events','causation_id'),
            ('location_events','client_generated_id'),
            ('location_events','idempotency_key'),
            ('telemetry_alerts','correlation_id'),
            ('telemetry_alerts','causation_id'),
            ('telemetry_alerts','source_channel'),
            ('telemetry_alerts','client_generated_id'),
            ('telemetry_alerts','ai_recommendation_id'),
            ('latest_vehicle_positions','source_event_id'),
            ('latest_vehicle_positions','correlation_id'),
            ('latest_vehicle_positions','causation_id'),
            ('latest_vehicle_positions','source_channel'),
            ('latest_vehicle_positions','telemetry_status'),
            ('latest_vehicle_positions','risk_level'),
            ('latest_vehicle_positions','alert_count'),
            ('latest_vehicle_positions','open_alert_count'),
            ('latest_vehicle_positions','next_action'),
            ('latest_vehicle_positions','summary_json'),
            ('latest_vehicle_positions','updated_at')
        ) AS required(table_name,column_name)
        WHERE NOT EXISTS (
            SELECT 1 FROM information_schema.columns c
            WHERE c.table_schema='public'
              AND c.table_name=required.table_name
              AND c.column_name=required.column_name
        )
    ) OR to_regclass('public.telemetry_live_asset_states') IS NULL THEN
        RAISE EXCEPTION 'Stage139 telemetry live-state contract is incomplete';
    END IF;
END
$stage139_verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_11_stage139_telemetry_ledger_backfill_reconciliation',
        'Forward-only reconciliation for Stage12A telemetry DDL backfilled by the Stage23 ledger')
ON CONFLICT(version) DO NOTHING;

COMMIT;
