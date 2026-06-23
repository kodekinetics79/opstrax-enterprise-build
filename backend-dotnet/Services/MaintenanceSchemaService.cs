using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Creates new maintenance tables and adds missing columns to existing tables.
// Safe to run repeatedly — all CREATE TABLE uses IF NOT EXISTS, ALTER TABLE
// checks information_schema before adding columns.
public sealed class MaintenanceSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var col in Columns) await EnsureColumnAsync(col.Table, col.Name, col.Definition, ct);
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
        foreach (var sql in Seeds) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@t AND column_name=@c",
            c => { c.Parameters.AddWithValue("@t", table); c.Parameters.AddWithValue("@c", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColDef(string Table, string Name, string Definition);

    private static readonly ColDef[] Columns =
    [
        // Vehicles — fleet availability + inspection tracking
        new("vehicles", "out_of_service",      "TINYINT(1) NOT NULL DEFAULT 0"),
        new("vehicles", "availability_status", "VARCHAR(60) NOT NULL DEFAULT 'available'"),
        new("vehicles", "last_inspection_at",  "TIMESTAMP NULL"),
        new("vehicles", "engine_hours",        "DECIMAL(12,2) NULL"),

        // DVIR reports — trip binding + odometer + review workflow
        new("dvir_reports", "trip_id",         "BIGINT NULL"),
        new("dvir_reports", "odometer_miles",  "DECIMAL(12,2) NULL"),
        new("dvir_reports", "engine_hours",    "DECIMAL(12,2) NULL"),
        new("dvir_reports", "reviewed_by",     "BIGINT NULL"),
        new("dvir_reports", "reviewed_at",     "DATETIME NULL"),
        new("dvir_reports", "signature_hash",  "VARCHAR(64) NULL"),

        // DVIR defects — unified defect model
        new("dvir_defects", "vehicle_id",      "BIGINT NULL"),
        new("dvir_defects", "driver_id",       "BIGINT NULL"),
        new("dvir_defects", "fault_code_id",   "BIGINT NULL"),
        new("dvir_defects", "source",          "VARCHAR(40) NOT NULL DEFAULT 'dvir'"),
        new("dvir_defects", "out_of_service",  "TINYINT(1) NOT NULL DEFAULT 0"),
        new("dvir_defects", "resolved_at",     "TIMESTAMP NULL"),
        new("dvir_defects", "resolved_by",     "BIGINT NULL"),

        // Work orders — cost tracking + lifecycle timestamps
        new("work_orders", "defect_id",        "BIGINT NULL"),
        new("work_orders", "actual_cost",      "DECIMAL(12,2) NULL"),
        new("work_orders", "completed_at",     "TIMESTAMP NULL"),
        new("work_orders", "assigned_at",      "TIMESTAMP NULL"),
        new("work_orders", "company_id",       "BIGINT NOT NULL DEFAULT 1"),
    ];

    private static readonly string[] Tables =
    [
        // Per-inspection checklist results — one row per item per DVIR report.
        // Templates define the items; this table captures driver responses.
        @"CREATE TABLE IF NOT EXISTS dvir_inspection_results (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            dvir_report_id BIGINT NOT NULL,
            item_category VARCHAR(120) NOT NULL,
            item_name VARCHAR(220) NOT NULL,
            result VARCHAR(40) NOT NULL DEFAULT 'pass',
            severity VARCHAR(40) NOT NULL DEFAULT 'minor',
            notes TEXT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        // Fault codes ingested from OBD/J1939/OEM device telemetry.
        // Populated by POST /api/maintenance/fault-codes/ingest (device-auth required).
        // UNIQUE KEY prevents duplicate active codes per device.
        @"CREATE TABLE IF NOT EXISTS fault_codes (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            device_id VARCHAR(120) NOT NULL,
            vehicle_id BIGINT NULL,
            code_type VARCHAR(40) NOT NULL DEFAULT 'OBD',
            code VARCHAR(40) NOT NULL,
            description TEXT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Warning',
            first_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            occurrence_count INT NOT NULL DEFAULT 1,
            status VARCHAR(40) NOT NULL DEFAULT 'active',
            defect_id BIGINT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY uq_fc_device_code (device_id, code, status)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        // Tenant-configurable preventive maintenance rules.
        // Trigger types: mileage, engine_hours, days, vehicle_class.
        // Background service generates maintenance_items when thresholds hit.
        @"CREATE TABLE IF NOT EXISTS maintenance_pm_rules (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            rule_name VARCHAR(180) NOT NULL,
            service_type VARCHAR(120) NOT NULL,
            vehicle_class VARCHAR(80) NULL,
            trigger_type VARCHAR(40) NOT NULL DEFAULT 'mileage',
            interval_miles INT NULL,
            interval_engine_hours INT NULL,
            interval_days INT NULL,
            warning_threshold_pct INT NOT NULL DEFAULT 10,
            overdue_threshold_pct INT NOT NULL DEFAULT 0,
            priority VARCHAR(40) NOT NULL DEFAULT 'Medium',
            estimated_cost DECIMAL(12,2) NULL,
            enabled TINYINT(1) NOT NULL DEFAULT 1,
            notes TEXT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_pm_rule (company_id, service_type, trigger_type, vehicle_class)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX idx_dir_company_vehicle ON dvir_inspection_results(company_id, dvir_report_id)",
        "CREATE INDEX idx_fc_company_vehicle ON fault_codes(company_id, vehicle_id, status)",
        "CREATE INDEX idx_fc_device ON fault_codes(device_id, code)",
        "CREATE INDEX idx_pm_rules_company ON maintenance_pm_rules(company_id, enabled)",
        "CREATE INDEX idx_dvir_defects_vehicle ON dvir_defects(company_id, vehicle_id, status)",
        "CREATE INDEX idx_wo_company_status ON work_orders(company_id, status)",
        "CREATE INDEX idx_vehicles_oos ON vehicles(company_id, out_of_service)",
    ];

    // Seed default PM rules per company that doesn't have them yet.
    private static readonly string[] Seeds =
    [
        @"INSERT IGNORE INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_miles, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Engine Oil Change', 'Oil Change', 'mileage', 5000, 10, 'High', 80
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='Oil Change')",

        @"INSERT IGNORE INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_miles, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Tire Rotation', 'Tire Rotation', 'mileage', 10000, 10, 'Medium', 60
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='Tire Rotation')",

        @"INSERT IGNORE INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_miles, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Brake Inspection', 'Brake Inspection', 'mileage', 20000, 10, 'Critical', 250
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='Brake Inspection')",

        @"INSERT IGNORE INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_days, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Annual DOT Inspection', 'DOT Inspection', 'days', 365, 14, 'Critical', 400
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='DOT Inspection')",
    ];
}
