using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class TelemetrySchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var col in Columns) await EnsureColumnAsync(col.Table, col.Name, col.Definition, ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
        foreach (var sql in CredentialHardening) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Seeds) await db.ExecuteAsync(sql, ct: ct);
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name=@table AND column_name=@column",
            c => { c.Parameters.AddWithValue("@table", table); c.Parameters.AddWithValue("@column", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        // eld_devices security + lifecycle columns
        new("eld_devices", "company_id",   "BIGINT NOT NULL DEFAULT 1"),
        // IMEI is the hardware GPS-tracker identifier (GT06/Concox/PT40-class) the trusted
        // gateway resolves a device by. An identifier, never a credential. Also created by
        // migration 2026_07_11_stage32_device_imei.sql for restricted-role prod that skips
        // this ensure; kept here so owner-capable envs self-heal and provisioning can write it.
        new("eld_devices", "imei",         "VARCHAR(32) NULL"),
        new("eld_devices", "api_key_hash", "VARCHAR(64) NULL"),
        new("eld_devices", "hmac_secret",  "VARCHAR(128) NULL"),
        new("eld_devices", "last_seen_at", "TIMESTAMPTZ NULL"),
        new("eld_devices", "revoked_at",   "TIMESTAMPTZ NULL"),
        new("eld_devices", "updated_at",   "TIMESTAMPTZ NULL"),
        new("eld_devices", "deleted_at",   "TIMESTAMPTZ NULL"),
        // location_events telemetry enrichment
        // accuracy_meters is in the Batch1 CREATE, but a location_events table created by an
        // older path (pre-column) won't get it via CREATE IF NOT EXISTS — backfill idempotently
        // so the trip-breadcrumbs replay query doesn't 42703 on such DBs.
        new("location_events", "accuracy_meters", "DECIMAL(8,2) NULL"),
        new("location_events", "device_id",   "BIGINT NULL"),
        new("location_events", "received_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("location_events", "source",      "VARCHAR(40) NOT NULL DEFAULT 'device'"),
        new("location_events", "nonce",       "VARCHAR(128) NULL"),
        new("location_events", "source_channel", "VARCHAR(40) NULL"),
        new("location_events", "correlation_id", "VARCHAR(120) NULL"),
        new("location_events", "causation_id", "VARCHAR(120) NULL"),
        new("location_events", "client_generated_id", "VARCHAR(120) NULL"),
        new("location_events", "idempotency_key", "VARCHAR(120) NULL"),
        new("telemetry_alerts", "correlation_id", "VARCHAR(120) NULL"),
        new("telemetry_alerts", "causation_id", "VARCHAR(120) NULL"),
        new("telemetry_alerts", "source_channel", "VARCHAR(40) NULL"),
        new("telemetry_alerts", "client_generated_id", "VARCHAR(120) NULL"),
        new("telemetry_alerts", "ai_recommendation_id", "BIGINT NULL"),
        new("latest_vehicle_positions", "source_event_id", "BIGINT NULL"),
        new("latest_vehicle_positions", "correlation_id", "VARCHAR(120) NULL"),
        new("latest_vehicle_positions", "causation_id", "VARCHAR(120) NULL"),
        new("latest_vehicle_positions", "source_channel", "VARCHAR(40) NULL"),
        new("latest_vehicle_positions", "telemetry_status", "VARCHAR(40) NULL"),
        new("latest_vehicle_positions", "risk_level", "VARCHAR(40) NULL"),
        new("latest_vehicle_positions", "alert_count", "INT NOT NULL DEFAULT 0"),
        new("latest_vehicle_positions", "open_alert_count", "INT NOT NULL DEFAULT 0"),
        new("latest_vehicle_positions", "next_action", "VARCHAR(160) NULL"),
        new("latest_vehicle_positions", "summary_json", "JSONB NULL"),
        new("latest_vehicle_positions", "updated_at", "TIMESTAMPTZ NULL"),
        // Provenance & trust metadata — EXACTLY mirrors migration
        // database/migrations/telematics/001_latest_position_provenance.sql so
        // owner-capable environments auto-create them here and production (which
        // runs as a restricted role and SKIPS this startup init) gets them from
        // migration 001. correlation_id is intentionally NOT re-declared (already
        // present above). Every read/write guards on
        // TelemetryProvenance.ColumnsAvailableAsync, so an environment where these
        // are still absent never 42703s ("column ... does not exist").
        new("latest_vehicle_positions", "source",              "TEXT NULL"),
        new("latest_vehicle_positions", "provider",            "TEXT NULL"),
        new("latest_vehicle_positions", "protocol",            "TEXT NULL"),
        new("latest_vehicle_positions", "adapter_version",     "TEXT NULL"),
        new("latest_vehicle_positions", "device_fix_time",     "TIMESTAMPTZ NULL"),
        new("latest_vehicle_positions", "gateway_received_at", "TIMESTAMPTZ NULL"),
        new("latest_vehicle_positions", "normalized_at",       "TIMESTAMPTZ NULL"),
        new("latest_vehicle_positions", "confidence",          "NUMERIC(4,3) NULL"),
        new("latest_vehicle_positions", "trust_score",         "NUMERIC(4,3) NULL"),
        new("latest_vehicle_positions", "quality_flags",       "JSONB NULL"),
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS latest_vehicle_positions (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            device_id BIGINT NULL,
            driver_id BIGINT NULL,
            lat DECIMAL(10,7) NOT NULL,
            lng DECIMAL(10,7) NOT NULL,
            speed_mph DECIMAL(6,2) NOT NULL DEFAULT 0,
            heading SMALLINT NOT NULL DEFAULT 0,
            accuracy_meters DECIMAL(8,2) NULL,
            engine_status VARCHAR(40) NULL,
            fuel_level DECIMAL(6,2) NULL,
            odometer_miles DECIMAL(12,2) NULL,
            battery_voltage DECIMAL(6,2) NULL,
            event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            event_count BIGINT NOT NULL DEFAULT 1,
            UNIQUE (company_id, vehicle_id)
        )",

        @"CREATE TABLE IF NOT EXISTS telemetry_alerts (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NULL,
            device_id BIGINT NULL,
            driver_id BIGINT NULL,
            alert_type VARCHAR(60) NOT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Warning',
            message TEXT NOT NULL,
            source_event_id BIGINT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Open',
            acknowledged_at TIMESTAMPTZ NULL,
            acknowledged_by VARCHAR(120) NULL,
            resolved_at TIMESTAMPTZ NULL,
            resolved_by VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        // Durable nonce store: prevents replay within the retention window.
        // UNIQUE enforces one-use-per-device-nonce at DB level.
        // Rows older than 24 h are pruned by TelemetryBackgroundService.
        @"CREATE TABLE IF NOT EXISTS telemetry_nonces (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            device_id BIGINT NOT NULL,
            nonce VARCHAR(128) NOT NULL,
            used_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (device_id, nonce)
        )",

        // Durable, cross-instance replay defense for the trusted-gateway path
        // (POST /api/telemetry/gps-ingest). The HMAC signature is the per-message identity;
        // UNIQUE(gateway_id, signature) makes 'already accepted?' atomic and shared across
        // instances/restarts. Not tenant-scoped, no RLS (infra ledger written before ownership
        // matters, like telemetry_nonces). device_id/company_id are recorded for audit scoping.
        // Rows older than the retention window are pruned by TelemetryBackgroundService.
        // Mirrored by migration 2026_07_14_stage33_gps_gateway_replay.sql for restricted prod.
        @"CREATE TABLE IF NOT EXISTS gps_gateway_replay (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            gateway_id VARCHAR(120) NOT NULL DEFAULT 'default',
            signature VARCHAR(256) NOT NULL,
            signed_at TIMESTAMPTZ NOT NULL,
            device_id BIGINT NULL,
            company_id BIGINT NULL,
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (gateway_id, signature)
        )",

        // Per-tenant, per-rule configurable thresholds. Defaults seeded below.
        @"CREATE TABLE IF NOT EXISTS telemetry_rules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            rule_type VARCHAR(60) NOT NULL,
            threshold_value DECIMAL(12,4) NOT NULL DEFAULT 65,
            severity VARCHAR(40) NOT NULL DEFAULT 'High',
            enabled BOOLEAN NOT NULL DEFAULT true,
            notes TEXT NULL,
            created_by BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (company_id, rule_type)
        )",

        @"CREATE TABLE IF NOT EXISTS telemetry_live_asset_states (
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
        )",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_ta_company_status ON telemetry_alerts(company_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_ta_vehicle ON telemetry_alerts(vehicle_id, company_id)",
        "CREATE INDEX IF NOT EXISTS idx_ta_type ON telemetry_alerts(company_id, alert_type, vehicle_id)",
        "CREATE INDEX IF NOT EXISTS idx_le_device ON location_events(device_id, event_time)",
        "CREATE INDEX IF NOT EXISTS idx_le_received ON location_events(company_id, received_at)",
        "CREATE INDEX IF NOT EXISTS idx_eld_apikey ON eld_devices(api_key_hash)",
        "CREATE INDEX IF NOT EXISTS idx_eld_company ON eld_devices(company_id, status)",
        // Globally-unique IMEI lookup (partial: many devices legitimately have no IMEI).
        // Matches ux_eld_devices_imei from migration stage32 so both provisioning paths agree.
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_eld_devices_imei ON eld_devices(imei) WHERE imei IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_lvp_tenant ON latest_vehicle_positions(company_id, received_at)",
        "CREATE INDEX IF NOT EXISTS idx_lvp_status ON latest_vehicle_positions(company_id, telemetry_status, risk_level)",
        "CREATE INDEX IF NOT EXISTS idx_tn_device_used ON telemetry_nonces(device_id, used_at)",
        "CREATE INDEX IF NOT EXISTS idx_ggr_received ON gps_gateway_replay(received_at)",
        "CREATE INDEX IF NOT EXISTS idx_tr_company ON telemetry_rules(company_id, rule_type, enabled)",
        "CREATE INDEX IF NOT EXISTS idx_tlsa_company_updated ON telemetry_live_asset_states(company_id, updated_at)",
        "CREATE INDEX IF NOT EXISTS idx_tlsa_company_risk ON telemetry_live_asset_states(company_id, risk_level, open_alert_count)",
    ];

    private static readonly string[] Seeds =
    [
        // last_seen_at: spread across last 12 minutes for demo staleness variety
        "UPDATE eld_devices SET last_seen_at = NOW() - (id % 12) * INTERVAL '1 minute' WHERE last_seen_at IS NULL",
        // Seed default speeding rule for every company that has devices
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'speeding', 65, 'High', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",
        // Seed default stale-device rule (900 seconds = 15 minutes)
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'stale_device', 900, 'Warning', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",
    ];

    private static readonly string[] CredentialHardening =
    [
        // Never manufacture credentials during schema startup. Legacy or incomplete
        // devices are quarantined until an operator explicitly rotates credentials.
        @"UPDATE eld_devices
          SET api_key_hash = NULL,
              hmac_secret = NULL,
              status = 'CredentialRotationRequired',
              revoked_at = COALESCE(revoked_at, NOW()),
              updated_at = NOW()
          WHERE deleted_at IS NULL
            AND (
                api_key_hash IS NULL
                OR btrim(api_key_hash) = ''
                OR api_key_hash !~ '^[0-9a-fA-F]{64}$'
                OR hmac_secret IS NULL
                OR btrim(hmac_secret) = ''
                OR length(hmac_secret) < 32
                OR api_key_hash = encode(sha256(('opstrax-' || 'dev-' || device_serial)::bytea), 'hex')
                OR hmac_secret = ('opstrax-' || 'hmac-dev-' || device_serial)
            )",
        @"DO $$
          BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'ck_eld_devices_active_credentials'
                  AND conrelid = 'eld_devices'::regclass
            ) THEN
                ALTER TABLE eld_devices
                ADD CONSTRAINT ck_eld_devices_active_credentials
                CHECK (
                    status <> 'Active'
                    OR (
                        api_key_hash IS NOT NULL
                        AND api_key_hash ~ '^[0-9a-fA-F]{64}$'
                        AND hmac_secret IS NOT NULL
                        AND length(btrim(hmac_secret)) >= 32
                        AND api_key_hash <> encode(sha256(('opstrax-' || 'dev-' || device_serial)::bytea), 'hex')
                        AND hmac_secret <> ('opstrax-' || 'hmac-dev-' || device_serial)
                    )
                );
            END IF;
          END
          $$",
    ];
}
