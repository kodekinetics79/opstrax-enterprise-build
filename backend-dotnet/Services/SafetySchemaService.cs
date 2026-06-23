using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class SafetySchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
        foreach (var sql in Seeds) await db.ExecuteAsync(sql, ct: ct);
    }

    private static readonly string[] Tables =
    [
        // Core safety events — linked to telemetry alerts and location events as evidence.
        // status: open → in_review → coaching_assigned → coached → resolved / dismissed
        @"CREATE TABLE IF NOT EXISTS safety_events (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NULL,
            vehicle_id BIGINT NULL,
            device_id BIGINT NULL,
            source_telemetry_alert_id BIGINT NULL,
            source_location_event_id BIGINT NULL,
            event_type VARCHAR(60) NOT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'High',
            score_impact DECIMAL(6,2) NOT NULL DEFAULT 15,
            status VARCHAR(40) NOT NULL DEFAULT 'open',
            event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            reviewed_by BIGINT NULL,
            reviewed_at TIMESTAMP NULL,
            resolved_by BIGINT NULL,
            resolved_at TIMESTAMP NULL,
            notes TEXT NULL,
            evidence_hash VARCHAR(64) NULL,
            meta_json JSON NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_se_telemetry_alert (source_telemetry_alert_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        // Coaching tasks linked to safety events — full lifecycle tracking.
        // status: pending → scheduled → completed / overdue / dismissed
        @"CREATE TABLE IF NOT EXISTS safety_coaching_tasks (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            safety_event_id BIGINT NULL,
            driver_id BIGINT NULL,
            assigned_to BIGINT NULL,
            assigned_by BIGINT NULL,
            due_date DATE NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'pending',
            coaching_type VARCHAR(60) NULL,
            notes TEXT NULL,
            outcome TEXT NULL,
            driver_acknowledged_at TIMESTAMP NULL,
            completed_at TIMESTAMP NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        // Cached driver safety scores — computed by SafetyBackgroundService.
        // score_30d is primary; breakdown_json holds explainable per-type counts.
        @"CREATE TABLE IF NOT EXISTS driver_safety_scores (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NOT NULL,
            score_7d DECIMAL(5,2) NOT NULL DEFAULT 100,
            score_30d DECIMAL(5,2) NOT NULL DEFAULT 100,
            score_90d DECIMAL(5,2) NOT NULL DEFAULT 100,
            events_7d INT NOT NULL DEFAULT 0,
            events_30d INT NOT NULL DEFAULT 0,
            events_90d INT NOT NULL DEFAULT 0,
            breakdown_json JSON NULL,
            computed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY uq_dss_company_driver (company_id, driver_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX idx_se_company_status ON safety_events(company_id, status)",
        "CREATE INDEX idx_se_driver ON safety_events(driver_id, company_id, event_time)",
        "CREATE INDEX idx_se_vehicle ON safety_events(vehicle_id, company_id)",
        "CREATE INDEX idx_se_type ON safety_events(company_id, event_type, status)",
        "CREATE INDEX idx_sct_company ON safety_coaching_tasks(company_id, status)",
        "CREATE INDEX idx_sct_event ON safety_coaching_tasks(safety_event_id)",
        "CREATE INDEX idx_sct_driver ON safety_coaching_tasks(driver_id, company_id)",
        "CREATE INDEX idx_dss_company ON driver_safety_scores(company_id, score_30d)",
    ];

    // Seed default safety score weights into telemetry_rules using INSERT IGNORE.
    // These are per-tenant so seeded for every company that has devices.
    private static readonly string[] Seeds =
    [
        // Score weight rules — threshold_value = points deducted per event
        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_speeding', 15, 'High', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",

        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_repeated_speeding', 25, 'Critical', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",

        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_geofence_breach', 10, 'High', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",

        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_stale_device', 5, 'Medium', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",

        // Threshold: score below this value triggers automatic coaching_required
        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_coaching_required_score', 70, 'High', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",

        // How many speeding events in 24h = 'repeated_speeding' event type
        @"INSERT IGNORE INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_repeated_speeding_threshold', 3, 'Critical', 1
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0",
    ];
}
