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
        foreach (var col in DiagnosticColumns) await EnsureColumnAsync(col.Table, col.Name, col.Definition, ct);
        foreach (var sql in DiagnosticHardening) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
        foreach (var sql in Seeds) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name=@t AND column_name=@c",
            c => { c.Parameters.AddWithValue("@t", table); c.Parameters.AddWithValue("@c", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColDef(string Table, string Name, string Definition);

    private static readonly ColDef[] Columns =
    [
        // Vehicles — fleet availability + inspection tracking
        new("vehicles", "out_of_service",      "BOOLEAN NOT NULL DEFAULT false"),
        new("vehicles", "availability_status", "VARCHAR(60) NOT NULL DEFAULT 'available'"),
        new("vehicles", "last_inspection_at",  "TIMESTAMPTZ NULL"),
        new("vehicles", "engine_hours",        "DECIMAL(12,2) NULL"),

        // maintenance_items — odometer + engine-hours readings recorded at service completion. These are
        // the baselines the PM evaluator reads to arm the next mileage / engine-hours interval.
        new("maintenance_items", "odometer_miles", "DECIMAL(12,2) NULL"),
        new("maintenance_items", "engine_hours",   "DECIMAL(12,2) NULL"),

        // DVIR reports — trip binding + odometer + review workflow
        new("dvir_reports", "trip_id",         "BIGINT NULL"),
        new("dvir_reports", "odometer_miles",  "DECIMAL(12,2) NULL"),
        new("dvir_reports", "engine_hours",    "DECIMAL(12,2) NULL"),
        new("dvir_reports", "reviewed_by",     "BIGINT NULL"),
        new("dvir_reports", "reviewed_at",     "TIMESTAMPTZ NULL"),
        new("dvir_reports", "signature_hash",  "VARCHAR(64) NULL"),
        new("dvir_reports", "signature_attestation_text", "TEXT NULL"),
        new("dvir_reports", "signed_at",       "TIMESTAMPTZ NULL"),
        new("dvir_reports", "signed_by",       "BIGINT NULL"),

        // DVIR defects — unified defect model
        new("dvir_defects", "vehicle_id",      "BIGINT NULL"),
        new("dvir_defects", "driver_id",       "BIGINT NULL"),
        new("dvir_defects", "fault_code_id",   "BIGINT NULL"),
        new("dvir_defects", "source",          "VARCHAR(40) NOT NULL DEFAULT 'dvir'"),
        new("dvir_defects", "out_of_service",  "BOOLEAN NOT NULL DEFAULT false"),
        new("dvir_defects", "resolved_at",     "TIMESTAMPTZ NULL"),
        new("dvir_defects", "resolved_by",     "BIGINT NULL"),

        // Work orders — cost tracking + lifecycle timestamps
        new("work_orders", "defect_id",        "BIGINT NULL"),
        new("work_orders", "actual_cost",      "DECIMAL(12,2) NULL"),
        new("work_orders", "completed_at",     "TIMESTAMPTZ NULL"),
        new("work_orders", "assigned_at",      "TIMESTAMPTZ NULL"),
        new("work_orders", "company_id",       "BIGINT NOT NULL DEFAULT 1"),

    ];

    // Applied after Tables so this works both for a clean install and an older fault_codes table.
    private static readonly ColDef[] DiagnosticColumns =
    [
        new("fault_codes", "branch_id",         "BIGINT NULL"),
        new("fault_codes", "source_event_id",   "VARCHAR(120) NULL"),
        new("fault_codes", "controller",        "VARCHAR(120) NULL"),
        new("fault_codes", "source_address",    "SMALLINT NULL"),
        new("fault_codes", "bus",               "VARCHAR(40) NULL"),
        new("fault_codes", "protocol",          "VARCHAR(40) NULL"),
        new("fault_codes", "spn",               "INT NULL"),
        new("fault_codes", "fmi",               "SMALLINT NULL"),
        new("fault_codes", "lamp_status",       "JSONB NULL"),
        new("fault_codes", "raw_evidence",      "JSONB NULL"),
        new("fault_codes", "observed_at",       "TIMESTAMPTZ NULL"),
        new("fault_codes", "received_at",       "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("fault_codes", "cleared_at",        "TIMESTAMPTZ NULL"),
        new("fault_codes", "clear_source",      "VARCHAR(80) NULL"),
        new("fault_codes", "updated_at",        "TIMESTAMPTZ NULL"),
        new("fault_codes", "canonical_identity", "VARCHAR(240) NULL"),
        new("fault_codes", "last_observed_at", "TIMESTAMPTZ NULL"),
        new("fault_codes", "last_source_event_id", "VARCHAR(120) NULL"),
        new("fault_codes", "diagnostic_hold_id", "BIGINT NULL"),
        new("fault_occurrences", "dtc_ordinal", "INT NOT NULL DEFAULT 0"),
        new("fault_occurrences", "canonical_dtc", "VARCHAR(240) NULL"),
        new("fault_occurrences", "payload_fingerprint", "VARCHAR(64) NULL"),
        new("diagnostic_holds", "device_id", "VARCHAR(120) NULL"),
        new("diagnostic_holds", "canonical_dtc", "VARCHAR(240) NULL"),
        new("diagnostic_holds", "severity", "VARCHAR(40) NULL"),
        new("diagnostic_holds", "source_event_id", "VARCHAR(160) NULL"),
        new("diagnostic_holds", "first_observed_at", "TIMESTAMPTZ NULL"),
        new("diagnostic_holds", "last_observed_at", "TIMESTAMPTZ NULL"),
        new("diagnostic_holds", "acknowledged_at", "TIMESTAMPTZ NULL"),
        new("diagnostic_holds", "acknowledged_by", "BIGINT NULL"),
        new("diagnostic_holds", "resolved_by", "BIGINT NULL"),
        new("diagnostic_holds", "resolution_note", "TEXT NULL"),
    ];

    private static readonly string[] Tables =
    [
        // Per-inspection checklist results — one row per item per DVIR report.
        // Templates define the items; this table captures driver responses.
        @"CREATE TABLE IF NOT EXISTS dvir_inspection_results (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            dvir_report_id BIGINT NOT NULL,
            item_category VARCHAR(120) NOT NULL,
            item_name VARCHAR(220) NOT NULL,
            result VARCHAR(40) NOT NULL DEFAULT 'pass',
            severity VARCHAR(40) NOT NULL DEFAULT 'minor',
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            checklist_item_id BIGINT NULL
        )",

        // Fault codes ingested from OBD/J1939/OEM device telemetry.
        // Populated by POST /api/maintenance/fault-codes/ingest (device-auth required).
        // One mutable projection row per tenant/device/protocol/code; immutable history
        // lives in fault_occurrences.
        @"CREATE TABLE IF NOT EXISTS fault_codes (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            device_id VARCHAR(120) NOT NULL,
            vehicle_id BIGINT NULL,
            branch_id BIGINT NULL,
            source_event_id VARCHAR(120) NULL,
            code_type VARCHAR(40) NOT NULL DEFAULT 'OBD',
            protocol VARCHAR(40) NULL,
            code VARCHAR(40) NOT NULL,
            description TEXT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Warning',
            controller VARCHAR(120) NULL,
            source_address SMALLINT NULL,
            bus VARCHAR(40) NULL,
            spn INT NULL,
            fmi SMALLINT NULL,
            lamp_status JSONB NULL,
            raw_evidence JSONB NULL,
            observed_at TIMESTAMPTZ NULL,
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            cleared_at TIMESTAMPTZ NULL,
            clear_source VARCHAR(80) NULL,
            occurrence_count INT NOT NULL DEFAULT 1,
            status VARCHAR(40) NOT NULL DEFAULT 'active',
            defect_id BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        // Immutable, idempotent diagnostic observations. source_event_id is supplied by
        // the device and is covered by the request HMAC, so retries can use fresh transport
        // nonces without duplicating a physical observation.
        @"CREATE TABLE IF NOT EXISTS fault_occurrences (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            device_id VARCHAR(120) NOT NULL,
            vehicle_id BIGINT NULL,
            source_event_id VARCHAR(120) NOT NULL,
            observed_at TIMESTAMPTZ NOT NULL,
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            controller VARCHAR(120) NULL,
            source_address SMALLINT NULL,
            bus VARCHAR(40) NULL,
            protocol VARCHAR(40) NOT NULL,
            code VARCHAR(40) NOT NULL,
            spn INT NULL,
            fmi SMALLINT NULL,
            occurrence_count INT NOT NULL DEFAULT 1,
            lamp_status JSONB NULL,
            raw_evidence JSONB NULL,
            payload_fingerprint VARCHAR(64) NULL,
            UNIQUE (company_id, device_id, source_event_id)
        )",

        @"CREATE TABLE IF NOT EXISTS diagnostic_holds (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            vehicle_id BIGINT NOT NULL,
            device_id VARCHAR(120) NOT NULL,
            fault_code_id BIGINT NULL,
            canonical_dtc VARCHAR(240) NOT NULL,
            severity VARCHAR(40) NOT NULL,
            status VARCHAR(30) NOT NULL DEFAULT 'active',
            out_of_service BOOLEAN NOT NULL DEFAULT true,
            source VARCHAR(40) NOT NULL DEFAULT 'machine_diagnostic',
            source_event_id VARCHAR(160) NOT NULL,
            reason TEXT NOT NULL,
            first_observed_at TIMESTAMPTZ NOT NULL,
            last_observed_at TIMESTAMPTZ NOT NULL,
            acknowledged_at TIMESTAMPTZ NULL,
            acknowledged_by BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            resolved_at TIMESTAMPTZ NULL,
            resolved_by BIGINT NULL,
            resolution_note TEXT NULL,
            resolution_evidence_type VARCHAR(40) NULL,
            resolution_evidence_reference VARCHAR(500) NULL,
            verified_at TIMESTAMPTZ NULL,
            updated_at TIMESTAMPTZ NULL
        )",
        "ALTER TABLE diagnostic_holds ADD COLUMN IF NOT EXISTS resolution_evidence_type VARCHAR(40) NULL",
        "ALTER TABLE diagnostic_holds ADD COLUMN IF NOT EXISTS resolution_evidence_reference VARCHAR(500) NULL",
        "ALTER TABLE diagnostic_holds ADD COLUMN IF NOT EXISTS verified_at TIMESTAMPTZ NULL",

        // Tenant-configurable preventive maintenance rules.
        // Trigger types: mileage, engine_hours, days, vehicle_class.
        // Background service generates maintenance_items when thresholds hit.
        @"CREATE TABLE IF NOT EXISTS maintenance_pm_rules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
            enabled BOOLEAN NOT NULL DEFAULT true,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (company_id, service_type, trigger_type, vehicle_class)
        )",
    ];

    private static readonly string[] DiagnosticHardening =
    [
        "UPDATE fault_codes SET canonical_identity=protocol || ':UNKNOWN:' || UPPER(code) WHERE canonical_identity IS NULL",
        "UPDATE fault_codes SET last_observed_at=COALESCE(observed_at,last_seen_at,created_at), last_source_event_id=COALESCE(source_event_id,'legacy:' || id::text) WHERE last_observed_at IS NULL OR last_source_event_id IS NULL",
        "UPDATE fault_occurrences SET canonical_dtc=protocol || ':UNKNOWN:' || UPPER(code) WHERE canonical_dtc IS NULL",
        "ALTER TABLE fault_occurrences DROP CONSTRAINT IF EXISTS fault_occurrences_company_id_device_id_source_event_id_key",
        "ALTER TABLE fault_codes DROP CONSTRAINT IF EXISTS fault_codes_device_id_code_status_key",
        "DROP INDEX IF EXISTS uq_fault_occurrences_event_dtc",
        "DROP INDEX IF EXISTS uq_fault_codes_derived_state",
        "DROP INDEX IF EXISTS uq_fault_codes_projection",
        "DROP INDEX IF EXISTS uq_fault_codes_company_device_source_event",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_fault_occurrences_source_dtc ON fault_occurrences(company_id,device_id,source_event_id,dtc_ordinal,canonical_dtc)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_fault_codes_canonical_projection ON fault_codes(company_id,device_id,protocol,canonical_identity)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_diagnostic_holds_active_fault ON diagnostic_holds(company_id,vehicle_id,canonical_dtc) WHERE status IN ('active','acknowledged')",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_dir_company_vehicle ON dvir_inspection_results(company_id, dvir_report_id)",
        "CREATE INDEX IF NOT EXISTS idx_fc_company_vehicle ON fault_codes(company_id, vehicle_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_fc_device ON fault_codes(device_id, code)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_fault_codes_canonical_projection ON fault_codes(company_id,device_id,protocol,canonical_identity)",
        "CREATE INDEX IF NOT EXISTS idx_fc_company_branch_state ON fault_codes(company_id, branch_id, status, last_seen_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_fo_company_vehicle_time ON fault_occurrences(company_id, vehicle_id, observed_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_dvir_defects_active_fault ON dvir_defects(company_id,fault_code_id) WHERE fault_code_id IS NOT NULL AND status NOT IN ('resolved','rejected')",
        "CREATE INDEX IF NOT EXISTS idx_pm_rules_company ON maintenance_pm_rules(company_id, enabled)",
        "CREATE INDEX IF NOT EXISTS idx_dvir_defects_vehicle ON dvir_defects(company_id, vehicle_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_wo_company_status ON work_orders(company_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_vehicles_oos ON vehicles(company_id, out_of_service)",
    ];

    // Seed default PM rules per company that doesn't have them yet.
    private static readonly string[] Seeds =
    [
        @"INSERT INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_miles, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Engine Oil Change', 'Oil Change', 'mileage', 5000, 10, 'High', 80
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='Oil Change')
          ON CONFLICT DO NOTHING",

        @"INSERT INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_miles, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Tire Rotation', 'Tire Rotation', 'mileage', 10000, 10, 'Medium', 60
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='Tire Rotation')
          ON CONFLICT DO NOTHING",

        @"INSERT INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_miles, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Brake Inspection', 'Brake Inspection', 'mileage', 20000, 10, 'Critical', 250
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='Brake Inspection')
          ON CONFLICT DO NOTHING",

        @"INSERT INTO maintenance_pm_rules
            (company_id, rule_name, service_type, trigger_type, interval_days, warning_threshold_pct, priority, estimated_cost)
          SELECT id, 'Annual DOT Inspection', 'DOT Inspection', 'days', 365, 14, 'Critical', 400
          FROM companies c
          WHERE NOT EXISTS (SELECT 1 FROM maintenance_pm_rules r WHERE r.company_id=c.id AND r.service_type='DOT Inspection')
          ON CONFLICT DO NOTHING",
    ];
}
