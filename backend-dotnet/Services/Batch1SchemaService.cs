using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch1SchemaService(Database db)
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

        foreach (var sql in SeedStatements)
        {
            await db.ExecuteAsync(sql, ct: ct);
        }
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM information_schema.columns
              WHERE table_schema = DATABASE() AND table_name=@table AND column_name=@column",
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
        new("vehicles", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 10"),
        new("vehicles", "device_status", "VARCHAR(60) NOT NULL DEFAULT 'Online'"),
        new("vehicles", "camera_status", "VARCHAR(60) NOT NULL DEFAULT 'Online'"),
        new("vehicles", "deleted_at", "TIMESTAMP NULL"),
        new("drivers", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 10"),
        new("drivers", "compliance_score", "DECIMAL(6,2) NOT NULL DEFAULT 95"),
        new("drivers", "deleted_at", "TIMESTAMP NULL"),
        new("customers", "phone", "VARCHAR(50) NULL"),
        new("customers", "billing_address", "VARCHAR(300) NULL"),
        new("customers", "shipping_address", "VARCHAR(300) NULL"),
        new("customers", "sla_health_score", "DECIMAL(6,2) NOT NULL DEFAULT 95"),
        new("customers", "delivery_experience_score", "DECIMAL(6,2) NOT NULL DEFAULT 95"),
        new("customers", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 10"),
        new("customers", "deleted_at", "TIMESTAMP NULL"),
        new("assets", "assigned_driver_id", "BIGINT NULL"),
        new("assets", "customer_id", "BIGINT NULL"),
        new("assets", "current_zone", "VARCHAR(160) NULL"),
        new("assets", "geofence_status", "VARCHAR(80) NOT NULL DEFAULT 'Inside authorized zone'"),
        new("assets", "utilization_score", "DECIMAL(6,2) NOT NULL DEFAULT 80"),
        new("assets", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 10"),
        new("assets", "deleted_at", "TIMESTAMP NULL"),
        new("users", "password_hash", "VARCHAR(255) NULL")
    ];

    private static readonly string[] TableStatements =
    [
        @"CREATE TABLE IF NOT EXISTS vehicle_documents (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            document_type VARCHAR(120) NOT NULL,
            document_name VARCHAR(220) NOT NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            expiry_date DATE NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS driver_documents (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NOT NULL,
            document_type VARCHAR(120) NOT NULL,
            document_name VARCHAR(220) NOT NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            expiry_date DATE NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS customer_contacts (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            customer_id BIGINT NOT NULL,
            full_name VARCHAR(160) NOT NULL,
            title VARCHAR(120) NULL,
            email VARCHAR(220) NULL,
            phone VARCHAR(50) NULL,
            is_primary BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS customer_addresses (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            customer_id BIGINT NOT NULL,
            address_type VARCHAR(60) NOT NULL,
            address_line VARCHAR(300) NOT NULL,
            city VARCHAR(120) NULL,
            state VARCHAR(80) NULL,
            postal_code VARCHAR(30) NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS asset_documents (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            asset_id BIGINT NOT NULL,
            document_type VARCHAR(120) NOT NULL,
            document_name VARCHAR(220) NOT NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            expiry_date DATE NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS vehicle_assignments (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            driver_id BIGINT NULL,
            assignment_type VARCHAR(80) NOT NULL DEFAULT 'Primary Driver',
            status VARCHAR(50) NOT NULL DEFAULT 'Active',
            assigned_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS driver_certifications (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NOT NULL,
            certification_type VARCHAR(120) NOT NULL,
            certification_number VARCHAR(120) NULL,
            status VARCHAR(50) NOT NULL DEFAULT 'Valid',
            expiry_date DATE NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS entity_timeline_events (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            entity_type VARCHAR(80) NOT NULL,
            entity_id BIGINT NOT NULL,
            event_type VARCHAR(120) NOT NULL,
            title VARCHAR(220) NOT NULL,
            body TEXT NULL,
            severity VARCHAR(50) NOT NULL DEFAULT 'Info',
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)"
    ];

    private static readonly string[] SeedStatements =
    [
        "UPDATE vehicles SET risk_score=GREATEST(4, 100-readiness_score) WHERE risk_score=10",
        "UPDATE vehicles SET year=2015, odometer_miles=268400, readiness_score=68, risk_score=78, status='Maintenance' WHERE vehicle_code='BOX-106' AND (year IS NULL OR year > 2018)",
        "UPDATE vehicles SET year=2017, odometer_miles=214800, readiness_score=74, risk_score=61, status='Delayed' WHERE vehicle_code='TRK-108' AND (year IS NULL OR year > 2018)",
        "UPDATE vehicles SET year=2018, odometer_miles=182250, readiness_score=77, risk_score=52 WHERE vehicle_code='REEFER-111' AND (year IS NULL OR year > 2019)",
        "UPDATE drivers SET risk_score=GREATEST(3, 100-readiness_score), compliance_score=82 + (id % 17) WHERE compliance_score=95",
        "UPDATE assets SET current_zone=COALESCE(current_zone,current_location), geofence_status=COALESCE(geofence_status,'Inside authorized zone'), utilization_score=IF(utilization_score=80, 70 + (id % 25), utilization_score), risk_score=IF(risk_score=10, IF(id % 5 = 0, 72, 18 + (id % 22)), risk_score)",
        @"INSERT INTO customers (company_id, customer_code, name, contact_name, email, phone, billing_address, shipping_address, status, sla_tier, sla_health_score, delivery_experience_score, risk_score)
          SELECT 1,'CUS-009','Manassas Advanced Manufacturing','Elena Ward','elena@mamfg.example','+1 703 555 4109','9100 Balls Ford Rd, Manassas, VA','9100 Balls Ford Rd, Manassas, VA','Active','Gold',94,92,18
          WHERE NOT EXISTS (SELECT 1 FROM customers WHERE customer_code='CUS-009')",
        @"INSERT INTO customers (company_id, customer_code, name, contact_name, email, phone, billing_address, shipping_address, status, sla_tier, sla_health_score, delivery_experience_score, risk_score)
          SELECT 1,'CUS-010','Potomac Government Services','Victor James','victor@potomacgov.example','+1 202 555 4110','1200 Pennsylvania Ave NW, Washington DC','650 N Glebe Rd, Arlington, VA','At Risk','Platinum',82,79,42
          WHERE NOT EXISTS (SELECT 1 FROM customers WHERE customer_code='CUS-010')",
        @"INSERT INTO vehicle_documents (company_id, vehicle_id, document_type, document_name, status, expiry_date)
          SELECT 1, v.id, 'Registration', CONCAT(v.vehicle_code, ' registration'), IF(v.id % 6 = 0, 'Expiring Soon', 'Active'), DATE_ADD(CURDATE(), INTERVAL v.id*9 DAY)
          FROM vehicles v WHERE NOT EXISTS (SELECT 1 FROM vehicle_documents d WHERE d.vehicle_id=v.id)",
        @"INSERT INTO driver_documents (company_id, driver_id, document_type, document_name, status, expiry_date)
          SELECT 1, d.id, 'License', CONCAT(d.driver_code, ' license file'), IF(d.id % 6 = 0, 'Expiring Soon', 'Active'), DATE_ADD(CURDATE(), INTERVAL d.id*11 DAY)
          FROM drivers d WHERE NOT EXISTS (SELECT 1 FROM driver_documents x WHERE x.driver_id=d.id)",
        @"INSERT INTO driver_certifications (company_id, driver_id, certification_type, certification_number, status, expiry_date)
          SELECT 1, d.id, 'CDL', CONCAT('CERT-', LPAD(d.id,5,'0')), IF(d.id % 7 = 0, 'Review', 'Valid'), DATE_ADD(CURDATE(), INTERVAL d.id*14 DAY)
          FROM drivers d WHERE NOT EXISTS (SELECT 1 FROM driver_certifications x WHERE x.driver_id=d.id)",
        @"INSERT INTO customer_contacts (company_id, customer_id, full_name, title, email, phone, is_primary)
          SELECT 1, c.id, COALESCE(c.contact_name, CONCAT(c.name, ' Contact')), 'Operations Lead', COALESCE(c.email, CONCAT('customer', c.id, '@opstrax.example')), COALESCE(c.phone, '+1 703 555 6100'), TRUE
          FROM customers c WHERE NOT EXISTS (SELECT 1 FROM customer_contacts x WHERE x.customer_id=c.id)",
        @"INSERT INTO customer_addresses (company_id, customer_id, address_type, address_line, city, state, postal_code)
          SELECT 1, c.id, 'Billing', COALESCE(c.billing_address, 'Northern Virginia Service Address'), 'Fairfax', 'VA', '22030'
          FROM customers c WHERE NOT EXISTS (SELECT 1 FROM customer_addresses x WHERE x.customer_id=c.id)",
        @"INSERT INTO asset_documents (company_id, asset_id, document_type, document_name, status, expiry_date)
          SELECT 1, a.id, 'Inspection', CONCAT(a.asset_code, ' inspection file'), IF(a.id % 5 = 0, 'Review', 'Active'), DATE_ADD(CURDATE(), INTERVAL a.id*13 DAY)
          FROM assets a WHERE NOT EXISTS (SELECT 1 FROM asset_documents x WHERE x.asset_id=a.id)",
        @"INSERT INTO vehicle_assignments (company_id, vehicle_id, driver_id, assignment_type, status)
          SELECT 1, v.id, v.assigned_driver_id, 'Primary Driver', 'Active'
          FROM vehicles v WHERE v.assigned_driver_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM vehicle_assignments x WHERE x.vehicle_id=v.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT 1, 'Vehicle', v.id, 'batch1.ready', CONCAT(v.vehicle_code, ' Batch 1 profile ready'), 'Vehicles detail evidence backfilled by OpsTrax.', 'Info'
          FROM vehicles v WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Vehicle' AND x.entity_id=v.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT 1, 'Driver', d.id, 'batch1.ready', CONCAT(d.driver_code, ' Batch 1 profile ready'), 'Drivers detail evidence backfilled by OpsTrax.', 'Info'
          FROM drivers d WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Driver' AND x.entity_id=d.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT 1, 'Customer', c.id, 'batch1.ready', CONCAT(c.customer_code, ' Batch 1 profile ready'), 'Customer detail evidence backfilled by OpsTrax.', 'Info'
          FROM customers c WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Customer' AND x.entity_id=c.id)",
        @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
          SELECT 1, 'Asset', a.id, 'batch1.ready', CONCAT(a.asset_code, ' Batch 1 profile ready'), 'Asset detail evidence backfilled by OpsTrax.', 'Info'
          FROM assets a WHERE NOT EXISTS (SELECT 1 FROM entity_timeline_events x WHERE x.entity_type='Asset' AND x.entity_id=a.id)",
        @"INSERT INTO ai_recommendations (company_id, module_key, title, body, score, status)
          SELECT 1, m.module_key, CONCAT('Batch 1 ', m.title, ' recommended action'), m.body, 94, 'Recommended'
          FROM (
            SELECT 'vehicles' module_key, 'vehicle' title, 'Review risk heat, data completeness, device status and driver assignment before dispatch.' body
            UNION ALL SELECT 'drivers','driver','Review readiness, HOS, certification and coaching signals before assignment.'
            UNION ALL SELECT 'customers','customer','Send proactive updates for SLA-sensitive customers and watch at-risk accounts.'
            UNION ALL SELECT 'assets','asset','Review geofence status, utilization and assignment before route release.'
          ) m
          WHERE NOT EXISTS (SELECT 1 FROM ai_recommendations r WHERE r.module_key=m.module_key AND r.title LIKE 'Batch 1%')"
    ];
}
