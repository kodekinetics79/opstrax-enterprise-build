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
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
            event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            reviewed_by BIGINT NULL,
            reviewed_at TIMESTAMPTZ NULL,
            resolved_by BIGINT NULL,
            resolved_at TIMESTAMPTZ NULL,
            notes TEXT NULL,
            evidence_hash VARCHAR(64) NULL,
            meta_json JSONB NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (source_telemetry_alert_id)
        )",

        // Coaching tasks linked to safety events — full lifecycle tracking.
        // status: pending → scheduled → completed / overdue / dismissed
        @"CREATE TABLE IF NOT EXISTS safety_coaching_tasks (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
            driver_acknowledged_at TIMESTAMPTZ NULL,
            completed_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        // Cached driver safety scores — computed by SafetyBackgroundService.
        // score_30d is primary; breakdown_json holds explainable per-type counts.
        @"CREATE TABLE IF NOT EXISTS driver_safety_scores (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NOT NULL,
            score_7d DECIMAL(5,2) NOT NULL DEFAULT 100,
            score_30d DECIMAL(5,2) NOT NULL DEFAULT 100,
            score_90d DECIMAL(5,2) NOT NULL DEFAULT 100,
            events_7d INT NOT NULL DEFAULT 0,
            events_30d INT NOT NULL DEFAULT 0,
            events_90d INT NOT NULL DEFAULT 0,
            breakdown_json JSONB NULL,
            computed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (company_id, driver_id)
        )",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_se_company_status ON safety_events(company_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_se_driver ON safety_events(driver_id, company_id, event_time)",
        "CREATE INDEX IF NOT EXISTS idx_se_vehicle ON safety_events(vehicle_id, company_id)",
        "CREATE INDEX IF NOT EXISTS idx_se_type ON safety_events(company_id, event_type, status)",
        "CREATE INDEX IF NOT EXISTS idx_sct_company ON safety_coaching_tasks(company_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_sct_event ON safety_coaching_tasks(safety_event_id)",
        "CREATE INDEX IF NOT EXISTS idx_sct_driver ON safety_coaching_tasks(driver_id, company_id)",
        "CREATE INDEX IF NOT EXISTS idx_dss_company ON driver_safety_scores(company_id, score_30d)",
    ];

    // Seed default safety score weights into telemetry_rules using ON CONFLICT DO NOTHING.
    // These are per-tenant so seeded for every company that has devices.
    private static readonly string[] Seeds =
    [
        // Score weight rules — threshold_value = points deducted per event
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_speeding', 15, 'High', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",

        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_repeated_speeding', 25, 'Critical', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",

        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_geofence_breach', 10, 'High', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",

        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_weight_stale_device', 5, 'Medium', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",

        // Threshold: score below this value triggers automatic coaching_required
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_coaching_required_score', 70, 'High', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",

        // How many speeding events in 24h = 'repeated_speeding' event type
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'safety_repeated_speeding_threshold', 3, 'Critical', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",
    ];
}
