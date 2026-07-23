using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch1SchemaService(Database db, IConfiguration? configuration = null)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var column in Columns)
        {
            await EnsureColumnAsync(column.Table, column.Name, column.Definition, ct);
        }

        foreach (var sql in TableStatements)
        {
            await db.ExecuteAsync(sql, ct: ct);
        }

        // Demo/synthetic data only on explicit opt-in — these statements mutate
        // existing tenant rows cross-tenant (see DemoSeedGate).
        if (DemoSeedGate.IsExplicitlyEnabled(configuration))
        {
            foreach (var sql in SeedStatements)
            {
                await db.ExecuteAsync(sql, ct: ct);
            }
        }
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM information_schema.columns
              WHERE table_schema = current_schema() AND table_name=@table AND column_name=@column",
            c =>
            {
                c.Parameters.AddWithValue("@table", table);
                c.Parameters.AddWithValue("@column", column);
            }, ct);

        if (exists == 0)
        {
            await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
        }
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        new("vehicles", "risk_score",          "DECIMAL(6,2) NOT NULL DEFAULT 10"),
        new("vehicles", "device_status",       "VARCHAR(60) NOT NULL DEFAULT 'Online'"),
        new("vehicles", "camera_status",       "VARCHAR(60) NOT NULL DEFAULT 'Online'"),
        new("vehicles", "deleted_at",          "TIMESTAMPTZ NULL"),
        new("vehicles", "out_of_service",      "BOOLEAN NOT NULL DEFAULT FALSE"),
        new("vehicles", "availability_status", "VARCHAR(60) NULL"),
        new("drivers", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 10"),
        new("drivers", "compliance_score", "DECIMAL(6,2) NOT NULL DEFAULT 95"),
        new("drivers", "deleted_at", "TIMESTAMPTZ NULL"),
        // Blind index (HMAC) for license_number — lets the uniqueness check match on
        // encrypted values without decrypting. See PiiProtectionService.BlindIndex.
        new("drivers", "license_number_bidx", "VARCHAR(64) NULL"),
        new("customers", "phone", "VARCHAR(50) NULL"),
        new("customers", "billing_address", "VARCHAR(300) NULL"),
        new("customers", "shipping_address", "VARCHAR(300) NULL"),
        // Health scores are COMPUTED from delivery history (CustomerHealthService), never
        // defaulted. NULL is the honest value for a customer with no history — see the
        // NOT NULL/DEFAULT drops in TableStatements below.
        new("customers", "sla_health_score", "DECIMAL(6,2) NULL"),
        new("customers", "delivery_experience_score", "DECIMAL(6,2) NULL"),
        new("customers", "risk_score", "DECIMAL(6,2) NULL"),
        new("customers", "health_state", "VARCHAR(24) NULL"),
        new("customers", "health_computed_at", "TIMESTAMPTZ NULL"),
        new("customers", "deleted_at", "TIMESTAMPTZ NULL"),
        new("assets", "assigned_driver_id", "BIGINT NULL"),
        new("assets", "customer_id", "BIGINT NULL"),
        new("assets", "current_zone", "VARCHAR(160) NULL"),
        new("assets", "geofence_status", "VARCHAR(80) NOT NULL DEFAULT 'Inside authorized zone'"),
        new("assets", "utilization_score", "DECIMAL(6,2) NOT NULL DEFAULT 80"),
        new("assets", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 10"),
        new("assets", "deleted_at", "TIMESTAMPTZ NULL"),
        new("users", "password_hash", "VARCHAR(255) NULL"),
        new("ai_insights", "status", "VARCHAR(50) NOT NULL DEFAULT 'Open'"),
        new("ai_insights", "category", "VARCHAR(120) NOT NULL DEFAULT 'Operations'"),
        new("ai_insights", "alert_type", "VARCHAR(120) NULL"),
        new("ai_insights", "entity_type", "VARCHAR(60) NULL"),
        new("ai_insights", "entity_id", "BIGINT NULL"),
        new("ai_insights", "acknowledged_at", "TIMESTAMPTZ NULL"),
        new("ai_insights", "closed_at", "TIMESTAMPTZ NULL"),
        new("ai_insights", "acknowledged_by", "VARCHAR(160) NULL"),
        new("ai_insights", "recommended_action", "TEXT NULL"),
        new("ai_insights", "company_id", "BIGINT NOT NULL DEFAULT 1"),
        new("location_events", "engine_status", "VARCHAR(40) NULL DEFAULT 'Running'"),
        new("location_events", "fuel_level", "DECIMAL(6,2) NULL"),
        new("location_events", "odometer_miles", "DECIMAL(12,2) NULL"),
        // ai_recommendations drift: 001_schema.sql created the ORIGINAL (module_key/body)
        // shape, but the AI foundation layer (PostgresAiFoundationService.CreateRecommendation)
        // and the Batch/Stage seeds write the RICH shape. FoundationSchemaService owns the
        // rich CREATE but runs AFTER these batch seeds, so reconcile the columns HERE (first
        // schema step) so every later INSERT — batch seed, boot, and runtime — sees them.
        // All nullable / defaulted so the ALTER is safe on tables that already hold rows.
        new("ai_recommendations", "tenant_id", "BIGINT NULL"),
        new("ai_recommendations", "recommendation_type", "VARCHAR(120) NULL"),
        new("ai_recommendations", "summary", "TEXT NULL"),
        new("ai_recommendations", "confidence_score", "NUMERIC(6,3) NOT NULL DEFAULT 0"),
        new("ai_recommendations", "urgency_score", "NUMERIC(6,3) NOT NULL DEFAULT 0"),
        new("ai_recommendations", "impact_json", "JSONB NOT NULL DEFAULT '{}'::jsonb"),
        new("ai_recommendations", "reason_json", "JSONB NOT NULL DEFAULT '{}'::jsonb"),
        new("ai_recommendations", "proposed_action_json", "JSONB NOT NULL DEFAULT '{}'::jsonb"),
        new("ai_recommendations", "risk_level", "VARCHAR(40) NOT NULL DEFAULT 'Medium'"),
        new("ai_recommendations", "source_event_id", "VARCHAR(120) NULL"),
        new("ai_recommendations", "actor_type", "VARCHAR(40) NULL"),
        new("ai_recommendations", "actor_id", "VARCHAR(120) NULL"),
        new("ai_recommendations", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()")
    ];

    private static readonly string[] TableStatements =
    [
        // ai_recommendations legacy reconciliation: the ORIGINAL 001_schema shape made
        // module_key/body NOT NULL, but the rich AI foundation INSERT
        // (PostgresAiFoundationService.CreateRecommendation) does not populate them. Drop
        // the NOT NULL on the legacy columns IF they exist (a fresh, rich-shape DB created
        // by FoundationSchemaService has no such columns, so guard on existence).
        @"DO $reco$
          BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema=current_schema() AND table_name='ai_recommendations' AND column_name='module_key') THEN
                ALTER TABLE ai_recommendations ALTER COLUMN module_key DROP NOT NULL;
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema=current_schema() AND table_name='ai_recommendations' AND column_name='body') THEN
                ALTER TABLE ai_recommendations ALTER COLUMN body DROP NOT NULL;
            END IF;
          END
          $reco$;",
        @"CREATE TABLE IF NOT EXISTS vehicle_documents (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            document_type VARCHAR(120) NOT NULL,
            document_name VARCHAR(220) NOT NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            expiry_date DATE NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS driver_documents (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NOT NULL,
            document_type VARCHAR(120) NOT NULL,
            document_name VARCHAR(220) NOT NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            expiry_date DATE NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS customer_contacts (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            customer_id BIGINT NOT NULL,
            full_name VARCHAR(160) NOT NULL,
            title VARCHAR(120) NULL,
            email VARCHAR(220) NULL,
            phone VARCHAR(50) NULL,
            is_primary BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS customer_addresses (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            customer_id BIGINT NOT NULL,
            address_type VARCHAR(60) NOT NULL,
            address_line VARCHAR(300) NOT NULL,
            city VARCHAR(120) NULL,
            state VARCHAR(80) NULL,
            postal_code VARCHAR(30) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS asset_documents (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            asset_id BIGINT NOT NULL,
            document_type VARCHAR(120) NOT NULL,
            document_name VARCHAR(220) NOT NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            expiry_date DATE NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS vehicle_assignments (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            driver_id BIGINT NULL,
            assignment_type VARCHAR(80) NOT NULL DEFAULT 'Primary Driver',
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS driver_certifications (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NOT NULL,
            certification_type VARCHAR(120) NOT NULL,
            certification_number VARCHAR(120) NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Valid',
            expiry_date DATE NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS entity_timeline_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            entity_type VARCHAR(80) NOT NULL,
            entity_id BIGINT NOT NULL,
            event_type VARCHAR(120) NOT NULL,
            title VARCHAR(220) NOT NULL,
            body TEXT NULL,
            severity VARCHAR(50) NOT NULL DEFAULT 'Info',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS location_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            vehicle_id BIGINT NULL,
            vehicle_code VARCHAR(60) NULL,
            driver_id BIGINT NULL,
            driver_code VARCHAR(60) NULL,
            lat DECIMAL(10,7) NOT NULL,
            lng DECIMAL(10,7) NOT NULL,
            speed_mph DECIMAL(6,2) NOT NULL DEFAULT 0,
            heading SMALLINT NOT NULL DEFAULT 0,
            accuracy_meters DECIMAL(8,2) NULL,
            altitude_meters DECIMAL(8,2) NULL,
            event_type VARCHAR(60) NOT NULL DEFAULT 'ping',
            engine_status VARCHAR(40) NULL DEFAULT 'Running',
            fuel_level DECIMAL(6,2) NULL,
            odometer_miles DECIMAL(12,2) NULL,
            battery_voltage DECIMAL(6,2) NULL,
            dtc_codes JSONB NULL,
            geofence_id BIGINT NULL,
            device_id VARCHAR(120) NULL,
            event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",
        "CREATE INDEX IF NOT EXISTS idx_le_vehicle_time ON location_events (vehicle_id, event_time)",
        "CREATE INDEX IF NOT EXISTS idx_le_company_time ON location_events (company_id, event_time)",
        "CREATE INDEX IF NOT EXISTS idx_le_vehicle_code ON location_events (vehicle_code)",

        // Customer health: NULL must be representable. Older databases created these three
        // columns as NOT NULL DEFAULT 94/95/10, which is precisely how the fabricated
        // "SLA Health 94%" reached the UI for customers that had never had a single job.
        // CustomerHealthService writes a real score or NULL ("Not enough data") — so drop
        // the NOT NULL and the flattering default. Idempotent.
        "ALTER TABLE customers ALTER COLUMN sla_health_score DROP NOT NULL",
        "ALTER TABLE customers ALTER COLUMN sla_health_score DROP DEFAULT",
        "ALTER TABLE customers ALTER COLUMN delivery_experience_score DROP NOT NULL",
        "ALTER TABLE customers ALTER COLUMN delivery_experience_score DROP DEFAULT",
        "ALTER TABLE customers ALTER COLUMN risk_score DROP NOT NULL",
        "ALTER TABLE customers ALTER COLUMN risk_score DROP DEFAULT",
        "CREATE INDEX IF NOT EXISTS idx_customers_health_computed ON customers (company_id, health_computed_at)"
    ];

    private static readonly string[] SeedStatements =
    [
        "UPDATE vehicles SET risk_score=GREATEST(4, 100-readiness_score) WHERE risk_score=10",
        "UPDATE vehicles SET year=2015, odometer_miles=268400, readiness_score=68, risk_score=78, status='Maintenance' WHERE vehicle_code='BOX-106' AND (year IS NULL OR year > 2018)",
        "UPDATE vehicles SET year=2017, odometer_miles=214800, readiness_score=74, risk_score=61, status='Delayed' WHERE vehicle_code='TRK-108' AND (year IS NULL OR year > 2018)",
        "UPDATE vehicles SET year=2018, odometer_miles=182250, readiness_score=77, risk_score=52 WHERE vehicle_code='REEFER-111' AND (year IS NULL OR year > 2019)",
        "UPDATE drivers SET risk_score=GREATEST(3, 100-readiness_score), compliance_score=82 + (id % 17) WHERE compliance_score=95",
        "UPDATE assets SET current_zone=COALESCE(current_zone,current_location), geofence_status=COALESCE(geofence_status,'Inside authorized zone'), utilization_score=CASE WHEN utilization_score=80 THEN 70 + (id % 25) ELSE utilization_score END, risk_score=CASE WHEN risk_score=10 THEN CASE WHEN id % 5 = 0 THEN 72 ELSE 18 + (id % 22) END ELSE risk_score END",
        // REMOVED: the 'CUS-009' Manassas Advanced Manufacturing and 'CUS-010' Potomac
        // Government Services INSERTs. They wrote two invented customer accounts (with
        // invented 94/92/18 health scores) into REAL tenant company_id=1 on boot, guarded
        // only by a global, non-tenant-scoped WHERE NOT EXISTS on customer_code. A schema
        // service must never fabricate business rows for a real tenant. Demo data belongs
        // in DemoTenantSeeder (its own opt-in demo tenant).
        // Child-evidence rows derive company_id from their PARENT (never a hardcoded 1),
        // so a tenant's documents/contacts/addresses/timeline are stamped with that
        // tenant's company_id — not company 1. (Fixes the cross-tenant company_id
        // mismatch corruption found in seeded child rows.)
        @"INSERT INTO vehicle_documents (company_id, vehicle_id, document_type, document_name, status, expiry_date)
          SELECT v.company_id, v.id, 'Registration', v.vehicle_code || ' registration',
                 CASE WHEN v.id % 6 = 0 THEN 'Expiring Soon' ELSE 'Active' END,
                 CURRENT_DATE + v.id*9 * INTERVAL '1 day'
          FROM vehicles v WHERE NOT EXISTS (SELECT 1 FROM vehicle_documents d WHERE d.vehicle_id=v.id)",
        @"INSERT INTO driver_documents (company_id, driver_id, document_type, document_name, status, expiry_date)
          SELECT d.company_id, d.id, 'License', d.driver_code || ' license file',
                 CASE WHEN d.id % 6 = 0 THEN 'Expiring Soon' ELSE 'Active' END,
                 CURRENT_DATE + d.id*11 * INTERVAL '1 day'
          FROM drivers d WHERE NOT EXISTS (SELECT 1 FROM driver_documents x WHERE x.driver_id=d.id)",
        @"INSERT INTO driver_certifications (company_id, driver_id, certification_type, certification_number, status, expiry_date)
          SELECT d.company_id, d.id, 'CDL', 'CERT-' || LPAD(d.id::TEXT,5,'0'),
                 CASE WHEN d.id % 7 = 0 THEN 'Review' ELSE 'Valid' END,
                 CURRENT_DATE + d.id*14 * INTERVAL '1 day'
          FROM drivers d WHERE NOT EXISTS (SELECT 1 FROM driver_certifications x WHERE x.driver_id=d.id)",
        @"INSERT INTO customer_contacts (company_id, customer_id, full_name, title, email, phone, is_primary)
          SELECT c.company_id, c.id, COALESCE(c.contact_name, c.name || ' Contact'), 'Operations Lead',
                 COALESCE(c.email, 'customer' || c.id || '@opstrax.example'),
                 COALESCE(c.phone, '+1 703 555 6100'), TRUE
          FROM customers c WHERE NOT EXISTS (SELECT 1 FROM customer_contacts x WHERE x.customer_id=c.id)",
        @"INSERT INTO customer_addresses (company_id, customer_id, address_type, address_line, city, state, postal_code)
          SELECT c.company_id, c.id, 'Billing', COALESCE(c.billing_address, 'Northern Virginia Service Address'), 'Fairfax', 'VA', '22030'
          FROM customers c WHERE NOT EXISTS (SELECT 1 FROM customer_addresses x WHERE x.customer_id=c.id)",
        @"INSERT INTO asset_documents (company_id, asset_id, document_type, document_name, status, expiry_date)
          SELECT a.company_id, a.id, 'Inspection', a.asset_code || ' inspection file',
                 CASE WHEN a.id % 5 = 0 THEN 'Review' ELSE 'Active' END,
                 CURRENT_DATE + a.id*13 * INTERVAL '1 day'
          FROM assets a WHERE NOT EXISTS (SELECT 1 FROM asset_documents x WHERE x.asset_id=a.id)",
        @"INSERT INTO vehicle_assignments (company_id, vehicle_id, driver_id, assignment_type, status)
          SELECT v.company_id, v.id, v.assigned_driver_id, 'Primary Driver', 'Active'
          FROM vehicles v WHERE v.assigned_driver_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM vehicle_assignments x WHERE x.vehicle_id=v.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT v.company_id, 'Vehicle', v.id, 'batch1.ready', v.vehicle_code || ' Batch 1 profile ready', 'Vehicles detail evidence backfilled by OpsTrax.', 'Info'
          FROM vehicles v WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Vehicle' AND x.entity_id=v.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT d.company_id, 'Driver', d.id, 'batch1.ready', d.driver_code || ' Batch 1 profile ready', 'Drivers detail evidence backfilled by OpsTrax.', 'Info'
          FROM drivers d WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Driver' AND x.entity_id=d.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT c.company_id, 'Customer', c.id, 'batch1.ready', c.customer_code || ' Batch 1 profile ready', 'Customer detail evidence backfilled by OpsTrax.', 'Info'
          FROM customers c WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Customer' AND x.entity_id=c.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT a.company_id, 'Asset', a.id, 'batch1.ready', a.asset_code || ' Batch 1 profile ready', 'Asset detail evidence backfilled by OpsTrax.', 'Info'
          FROM assets a WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Asset' AND x.entity_id=a.id)",
        // REMOVED: the 'Batch 1 ... recommended action' ai_recommendations INSERT and the
        // four ai_insights INSERTs ('Brake inspection overdue', 'Driver coaching review',
        // 'Customer SLA at risk', 'Device heartbeat stale'). Every one of them hardcoded
        // company_id=1 and invented an operational alert / AI recommendation (with an
        // invented 94 confidence score) inside a REAL tenant. Insights and recommendations
        // must be produced by the agentic/analytics services from real signals, not written
        // into the tenant by a schema migration.

        @"INSERT INTO location_events (company_id, vehicle_id, vehicle_code, driver_id, lat, lng, speed_mph, heading, event_type, engine_status, fuel_level, odometer_miles, event_time)
          SELECT v.company_id, v.id, v.vehicle_code, v.assigned_driver_id,
                 38.8951 + (v.id * 0.0412 % 0.65),
                 -77.0364 - (v.id * 0.0387 % 0.58),
                 CASE WHEN v.status='Active' THEN 35 + (v.id * 7 % 40)
                      WHEN v.status='In Transit' THEN 45 + (v.id * 11 % 30)
                      ELSE 0 END,
                 (v.id * 37) % 360,
                 CASE WHEN v.status IN ('Active','In Transit') THEN 'ping' ELSE 'parked' END,
                 CASE WHEN v.status IN ('Active','In Transit') THEN 'Running' ELSE 'Off' END,
                 60 + (v.id * 13 % 35),
                 COALESCE(v.odometer_miles, 50000 + v.id * 3127),
                 NOW() - (v.id % 8) * INTERVAL '1 minute'
          FROM vehicles v
          WHERE NOT EXISTS (SELECT 1 FROM location_events le WHERE le.vehicle_id=v.id)"
    ];
}
