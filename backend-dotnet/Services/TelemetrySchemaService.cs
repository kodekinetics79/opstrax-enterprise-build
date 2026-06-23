using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class TelemetrySchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var col in Columns) await EnsureColumnAsync(col.Table, col.Name, col.Definition, ct);
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
        foreach (var sql in Seeds) await db.ExecuteAsync(sql, ct: ct);
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND column_name=@column",
            c => { c.Parameters.AddWithValue("@table", table); c.Parameters.AddWithValue("@column", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        // eld_devices security + lifecycle columns
        new("eld_devices", "company_id",   "BIGINT NOT NULL DEFAULT 1"),
        new("eld_devices", "api_key_hash", "VARCHAR(64) NULL COMMENT 'SHA-256 of raw device API key'"),
        new("eld_devices", "hmac_secret",  "VARCHAR(128) NULL COMMENT 'Device HMAC-SHA256 signing secret (plaintext, DB-level access controlled)'"),
        new("eld_devices", "last_seen_at", "TIMESTAMP NULL"),
        new("eld_devices", "revoked_at",   "TIMESTAMP NULL"),
        new("eld_devices", "updated_at",   "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("eld_devices", "deleted_at",   "TIMESTAMP NULL"),
        // location_events telemetry enrichment
        new("location_events", "device_id",   "BIGINT NULL"),
        new("location_events", "received_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP"),
        new("location_events", "source",      "VARCHAR(40) NOT NULL DEFAULT 'device'"),
        new("location_events", "nonce",       "VARCHAR(128) NULL"),
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS latest_vehicle_positions (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
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
            event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            received_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            event_count BIGINT NOT NULL DEFAULT 1,
            UNIQUE KEY uq_lvp_tenant_vehicle (company_id, vehicle_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS telemetry_alerts (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NULL,
            device_id BIGINT NULL,
            driver_id BIGINT NULL,
            alert_type VARCHAR(60) NOT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Warning',
            message TEXT NOT NULL,
            source_event_id BIGINT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Open',
            acknowledged_at TIMESTAMP NULL,
            acknowledged_by VARCHAR(120) NULL,
            resolved_at TIMESTAMP NULL,
            resolved_by VARCHAR(120) NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        // Durable nonce store: prevents replay within the retention window.
        // UNIQUE KEY enforces one-use-per-device-nonce at DB level.
        // Rows older than 24 h are pruned by TelemetryBackgroundService.
        @"CREATE TABLE IF NOT EXISTS telemetry_nonces (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            device_id BIGINT NOT NULL,
            nonce VARCHAR(128) NOT NULL,
            used_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY uq_device_nonce (device_id, nonce)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        // Per-tenant, per-rule configurable thresholds. Defaults seeded below.
        @"CREATE TABLE IF NOT EXISTS telemetry_rules (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            rule_type VARCHAR(60) NOT NULL,
            threshold_value DECIMAL(12,4) NOT NULL DEFAULT 65,
            severity VARCHAR(40) NOT NULL DEFAULT 'High',
            enabled TINYINT(1) NOT NULL DEFAULT 1,
            notes TEXT NULL,
            created_by BIGINT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_company_rule (company_id, rule_type)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX idx_ta_company_status ON telemetry_alerts(company_id, status)",
        "CREATE INDEX idx_ta_vehicle ON telemetry_alerts(vehicle_id, company_id)",
        "CREATE INDEX idx_ta_type ON telemetry_alerts(company_id, alert_type, vehicle_id)",
        "CREATE INDEX idx_le_device ON location_events(device_id, event_time)",
        "CREATE INDEX idx_le_received ON location_events(company_id, received_at)",
        "CREATE INDEX idx_eld_apikey ON eld_devices(api_key_hash)",
        "CREATE INDEX idx_eld_company ON eld_devices(company_id, status)",
        "CREATE INDEX idx_lvp_tenant ON latest_vehicle_positions(company_id, received_at)",
        "CREATE INDEX idx_tn_device_used ON telemetry_nonces(device_id, used_at)",
        "CREATE INDEX idx_tr_company ON telemetry_rules(company_id, rule_type, enabled)",
    ];

    private static readonly string[] Seeds =
    [
        // api_key_hash: deterministic for dev; real devices use provision endpoint
        "UPDATE eld_devices SET api_key_hash = SHA2(CONCAT('opstrax-dev-', device_serial), 256) WHERE api_key_hash IS NULL",
        // hmac_secret: deterministic for dev; real devices receive a random secret on provision
        "UPDATE eld_devices SET hmac_secret = CONCAT('opstrax-hmac-dev-', device_serial) WHERE hmac_secret IS NULL",
        // last_seen_at: spread across last 12 minutes for demo staleness variety
        "UPDATE eld_devices SET last_seen_at = DATE_SUB(NOW(), INTERVAL (id % 12) MINUTE) WHERE last_seen_at IS NULL",
        // Seed default speeding rule for every company that has devices
        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'speeding', 65, 'High', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",
        // Seed default stale-device rule (900 seconds = 15 minutes)
        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'stale_device', 900, 'Warning', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",
    ];
}
