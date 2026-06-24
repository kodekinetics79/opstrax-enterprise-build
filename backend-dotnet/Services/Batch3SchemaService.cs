using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch3SchemaService(Database db)
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

        foreach (var sql in IndexStatements)
        {
            try { await db.ExecuteAsync(sql, ct: ct); } catch { }
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
        new("maintenance_items", "maintenance_schedule_id", "BIGINT NULL"),
        new("maintenance_items", "asset_id", "BIGINT NULL"),
        new("maintenance_items", "service_type", "VARCHAR(120) NULL"),
        new("maintenance_items", "description", "TEXT NULL"),
        new("maintenance_items", "priority", "VARCHAR(40) NOT NULL DEFAULT 'Medium'"),
        new("maintenance_items", "due_odometer", "INT NULL"),
        new("maintenance_items", "due_engine_hours", "INT NULL"),
        new("maintenance_items", "estimated_cost", "DECIMAL(12,2) NULL"),
        new("maintenance_items", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 40"),
        new("maintenance_items", "recommended_action", "VARCHAR(240) NULL"),
        new("maintenance_items", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("maintenance_items", "updated_at", "TIMESTAMPTZ NULL"),
        new("maintenance_items", "deleted_at", "TIMESTAMPTZ NULL"),

        new("work_orders", "work_order_number", "VARCHAR(80) NULL"),
        new("work_orders", "asset_id", "BIGINT NULL"),
        new("work_orders", "maintenance_item_id", "BIGINT NULL"),
        new("work_orders", "dvir_report_id", "BIGINT NULL"),
        new("work_orders", "issue_type", "VARCHAR(120) NULL"),
        new("work_orders", "description", "TEXT NULL"),
        new("work_orders", "assigned_to_user_id", "BIGINT NULL"),
        new("work_orders", "vendor_name", "VARCHAR(160) NULL"),
        new("work_orders", "created_date", "TIMESTAMPTZ NULL"),
        new("work_orders", "started_at", "TIMESTAMPTZ NULL"),
        new("work_orders", "completed_at", "TIMESTAMPTZ NULL"),
        new("work_orders", "approved_cost", "DECIMAL(12,2) NULL"),
        new("work_orders", "downtime_hours", "DECIMAL(8,2) NOT NULL DEFAULT 0"),
        new("work_orders", "cost_approval_status", "VARCHAR(80) NOT NULL DEFAULT 'Pending'"),
        new("work_orders", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 35"),
        new("work_orders", "recommended_action", "VARCHAR(240) NULL"),
        new("work_orders", "notes", "TEXT NULL"),
        new("work_orders", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("work_orders", "updated_at", "TIMESTAMPTZ NULL"),
        new("work_orders", "deleted_at", "TIMESTAMPTZ NULL"),

        new("documents", "document_number", "VARCHAR(80) NULL"),
        new("documents", "entity_type", "VARCHAR(80) NULL"),
        new("documents", "entity_id", "BIGINT NULL"),
        new("documents", "category", "VARCHAR(120) NULL"),
        new("documents", "country_code", "VARCHAR(12) NULL"),
        new("documents", "issuing_authority", "VARCHAR(160) NULL"),
        new("documents", "issued_at", "DATE NULL"),
        new("documents", "expires_at", "DATE NULL"),
        new("documents", "renewal_status", "VARCHAR(80) NOT NULL DEFAULT 'Current'"),
        new("documents", "file_url", "VARCHAR(400) NULL"),
        new("documents", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 20"),
        new("documents", "recommended_action", "VARCHAR(240) NULL"),
        new("documents", "notes", "TEXT NULL"),
        new("documents", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("documents", "updated_at", "TIMESTAMPTZ NULL"),
        new("documents", "deleted_at", "TIMESTAMPTZ NULL"),
        new("notifications", "severity", "VARCHAR(50) NOT NULL DEFAULT 'Info'"),
        new("notifications", "module_key", "VARCHAR(100) NULL")
    ];

    private static readonly string[] TableStatements =
    [
        @"CREATE TABLE IF NOT EXISTS maintenance_schedules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            vehicle_id BIGINT NULL,
            asset_id BIGINT NULL,
            service_type VARCHAR(120) NOT NULL,
            trigger_type VARCHAR(80) NOT NULL DEFAULT 'Date',
            interval_miles INT NULL,
            interval_engine_hours INT NULL,
            interval_days INT NULL,
            last_service_date DATE NULL,
            last_service_odometer INT NULL,
            next_due_date DATE NULL,
            next_due_odometer INT NULL,
            next_due_engine_hours INT NULL,
            priority VARCHAR(40) NOT NULL DEFAULT 'Medium',
            status VARCHAR(60) NOT NULL DEFAULT 'Active',
            estimated_cost DECIMAL(12,2) NULL,
            vendor_id BIGINT NULL,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            deleted_at TIMESTAMPTZ NULL)",
        @"CREATE TABLE IF NOT EXISTS work_order_labor (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            work_order_id BIGINT NOT NULL,
            technician_name VARCHAR(160) NOT NULL,
            labor_hours DECIMAL(8,2) NOT NULL DEFAULT 0,
            labor_rate DECIMAL(10,2) NOT NULL DEFAULT 0,
            total_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS work_order_parts (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            work_order_id BIGINT NOT NULL,
            part_name VARCHAR(180) NOT NULL,
            part_number VARCHAR(100) NULL,
            quantity DECIMAL(8,2) NOT NULL DEFAULT 1,
            unit_cost DECIMAL(10,2) NOT NULL DEFAULT 0,
            total_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            status VARCHAR(80) NOT NULL DEFAULT 'Needed',
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS work_order_status_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            work_order_id BIGINT NOT NULL,
            previous_status VARCHAR(80) NULL,
            new_status VARCHAR(80) NOT NULL,
            event_title VARCHAR(180) NOT NULL,
            event_description TEXT NULL,
            occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_by_user_id BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS dvir_reports (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            report_number VARCHAR(80) NOT NULL,
            driver_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            country_code VARCHAR(12) NULL,
            inspection_type VARCHAR(80) NOT NULL,
            inspection_status VARCHAR(80) NOT NULL DEFAULT 'Submitted',
            defects_found INT NOT NULL DEFAULT 0,
            safe_to_operate BOOLEAN NOT NULL DEFAULT TRUE,
            driver_signature_status VARCHAR(80) NOT NULL DEFAULT 'Pending',
            mechanic_review_status VARCHAR(80) NOT NULL DEFAULT 'Pending',
            repair_certification_status VARCHAR(80) NOT NULL DEFAULT 'Pending',
            submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            mechanic_reviewed_at TIMESTAMPTZ NULL,
            repair_certified_at TIMESTAMPTZ NULL,
            risk_score DECIMAL(6,2) NOT NULL DEFAULT 30,
            recommended_action VARCHAR(240) NULL,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            deleted_at TIMESTAMPTZ NULL)",
        @"CREATE TABLE IF NOT EXISTS dvir_defects (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            dvir_report_id BIGINT NOT NULL,
            defect_category VARCHAR(120) NOT NULL,
            defect_description TEXT NOT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Minor',
            status VARCHAR(80) NOT NULL DEFAULT 'Open',
            linked_work_order_id BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL)",
        @"CREATE TABLE IF NOT EXISTS dvir_templates (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            template_name VARCHAR(180) NOT NULL,
            country_code VARCHAR(12) NULL,
            vehicle_type VARCHAR(80) NULL,
            inspection_type VARCHAR(80) NOT NULL,
            status VARCHAR(80) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL)",
        @"CREATE TABLE IF NOT EXISTS inspection_checklist_items (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            template_id BIGINT NOT NULL,
            item_label VARCHAR(220) NOT NULL,
            item_category VARCHAR(120) NOT NULL,
            required BOOLEAN NOT NULL DEFAULT TRUE,
            sort_order INT NOT NULL DEFAULT 1,
            status VARCHAR(80) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS document_timeline_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL DEFAULT 1,
            document_id BIGINT NOT NULL,
            event_title VARCHAR(180) NOT NULL,
            event_description TEXT NULL,
            occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())"
    ];

    private static readonly string[] IndexStatements =
    [
        "CREATE INDEX IF NOT EXISTS ix_b3_maintenance_due ON maintenance_items(company_id, status, priority, due_date)",
        "CREATE INDEX IF NOT EXISTS ix_b3_maintenance_schedule_due ON maintenance_schedules(company_id, status, next_due_date)",
        "CREATE INDEX IF NOT EXISTS ix_b3_work_orders_status ON work_orders(company_id, status, priority, due_date)",
        "CREATE INDEX IF NOT EXISTS ix_b3_work_orders_number ON work_orders(company_id, work_order_number)",
        "CREATE INDEX IF NOT EXISTS ix_b3_dvir_status ON dvir_reports(company_id, inspection_status, safe_to_operate)",
        "CREATE INDEX IF NOT EXISTS ix_b3_dvir_number ON dvir_reports(company_id, report_number)",
        "CREATE INDEX IF NOT EXISTS ix_b3_documents_expiry ON documents(company_id, status, expires_at)",
        "CREATE INDEX IF NOT EXISTS ix_b3_documents_number ON documents(company_id, document_number)"
    ];

    private static readonly string[] SeedStatements =
    [
        @"UPDATE maintenance_items SET service_type=COALESCE(service_type, category, title), description=COALESCE(description, title), priority=COALESCE(priority, risk_level, 'Medium'), risk_score=CASE WHEN risk_score=40 THEN CASE WHEN risk_level IN ('Critical','High') THEN 78 ELSE 25 + (id % 35) END ELSE risk_score END, recommended_action=COALESCE(recommended_action, CASE WHEN status IN ('Overdue','Open') OR due_date < CURRENT_DATE THEN 'Create work order and reserve maintenance bay' ELSE 'Monitor next service trigger' END), estimated_cost=COALESCE(estimated_cost, 250 + id*35)",
        @"UPDATE work_orders SET work_order_number=COALESCE(work_order_number, work_order_code), issue_type=COALESCE(issue_type, title), created_date=COALESCE(created_date, created_at), vendor_name=COALESCE(vendor_name, (ARRAY['NOVA Fleet Care','Dulles Diesel','Potomac Tire & Brake','Capital Mobile Service'])[(id % 4)+1]), approved_cost=COALESCE(approved_cost, CASE WHEN status='Completed' THEN estimated_cost ELSE NULL END), risk_score=CASE WHEN risk_score=35 THEN CASE WHEN priority IN ('Critical','High') THEN 82 ELSE 25 + (id % 40) END ELSE risk_score END, recommended_action=COALESCE(recommended_action, CASE WHEN status='Waiting Parts' THEN 'Escalate parts ETA and vendor SLA' ELSE 'Review downtime and cost approval' END)",
        @"UPDATE documents SET document_number=COALESCE(document_number, 'DOC-' || LPAD(id::TEXT,5,'0')), entity_type=COALESCE(entity_type, 'vehicle'), entity_id=COALESCE(entity_id, ((id - 1) % 20) + 1), category=COALESCE(category, document_type), country_code=COALESCE(country_code, 'US'), issuing_authority=COALESCE(issuing_authority, 'Virginia DMV'), issued_at=COALESCE(issued_at, CURRENT_DATE - 260 * INTERVAL '1 day'), expires_at=COALESCE(expires_at, CURRENT_DATE + (id % 90) * INTERVAL '1 day'), renewal_status=CASE WHEN status IN ('Expired','Expiring') THEN 'Renewal Required' ELSE renewal_status END, risk_score=CASE WHEN risk_score=20 THEN CASE WHEN status IN ('Expired','Expiring') OR expires_at <= CURRENT_DATE + 30 * INTERVAL '1 day' THEN 82 ELSE 20 + (id % 30) END ELSE risk_score END, recommended_action=COALESCE(recommended_action, CASE WHEN status IN ('Expired','Expiring') THEN 'Start renewal workflow and add to audit package' ELSE 'Keep in active document vault' END)",
        @"INSERT INTO maintenance_schedules (company_id, vehicle_id, asset_id, service_type, trigger_type, interval_miles, interval_engine_hours, interval_days, last_service_date, last_service_odometer, next_due_date, next_due_odometer, next_due_engine_hours, priority, status, estimated_cost, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
          SELECT 1,
                 CASE WHEN n % 5 = 0 THEN NULL ELSE ((n - 1) % 20) + 1 END,
                 CASE WHEN n % 5 = 0 THEN ((n - 1) % 12) + 1 ELSE NULL END,
                 (ARRAY['PM-A Service','Brake Inspection','Oil Change','Reefer Calibration','Tire Rotation','DOT Annual'])[(n % 6)+1],
                 (ARRAY['Mileage','Engine Hours','Date'])[(n % 3)+1],
                 5000 + n*250, 300 + n*12, 30 + (n % 45),
                 CURRENT_DATE - (20+n) * INTERVAL '1 day', 42000+n*1300,
                 CURRENT_DATE + (n-8) * INTERVAL '1 day', 47000+n*1400, 500+n*20,
                 (ARRAY['Low','Medium','High','Critical'])[(n % 4)+1],
                 CASE WHEN n % 6 = 0 THEN 'Deferred' ELSE 'Active' END,
                 250+n*45, 'Batch 3 preventive maintenance schedule.'
          FROM seq WHERE (SELECT COUNT(*) FROM maintenance_schedules WHERE deleted_at IS NULL) < 20",
        @"INSERT INTO maintenance_items (company_id, vehicle_id, asset_id, maintenance_schedule_id, title, category, service_type, description, priority, status, due_date, due_odometer, due_engine_hours, estimated_cost, risk_level, risk_score, recommended_action)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 25)
          SELECT 1,
                 CASE WHEN n % 6 = 0 THEN NULL ELSE ((n - 1) % 20) + 1 END,
                 CASE WHEN n % 6 = 0 THEN ((n - 1) % 12) + 1 ELSE NULL END,
                 ((n - 1) % 20) + 1,
                 'B3 maintenance item ' || n,
                 (ARRAY['PM','Brake','Oil','Reefer','Tire','Inspection'])[(n % 6)+1],
                 (ARRAY['PM-A Service','Brake Inspection','Oil Change','Reefer Calibration','Tire Rotation','DOT Annual'])[(n % 6)+1],
                 'Seeded Batch 3 maintenance risk and due trigger.',
                 (ARRAY['Low','Medium','High','Critical'])[(n % 4)+1],
                 (ARRAY['Open','Scheduled','In Progress','Overdue','Deferred'])[(n % 5)+1],
                 CURRENT_DATE + (n-12) * INTERVAL '1 day', 52000+n*900, 600+n*14,
                 300+n*38,
                 (ARRAY['Low','Medium','High','Critical'])[(n % 4)+1],
                 CASE WHEN n % 5 = 0 THEN 88 ELSE 32+(n%45) END,
                 CASE WHEN n % 5 = 0 THEN 'Create critical work order' ELSE 'Schedule before next dispatch window' END
          FROM seq WHERE (SELECT COUNT(*) FROM maintenance_items WHERE deleted_at IS NULL) < 25",
        @"INSERT INTO work_orders (company_id, vehicle_id, asset_id, maintenance_item_id, work_order_code, work_order_number, issue_type, title, description, priority, status, assigned_to_user_id, vendor_name, created_date, due_date, estimated_cost, approved_cost, downtime_hours, cost_approval_status, risk_score, recommended_action, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
          SELECT 1,
                 CASE WHEN n % 7 = 0 THEN NULL ELSE ((n - 1) % 20) + 1 END,
                 CASE WHEN n % 7 = 0 THEN ((n - 1) % 12) + 1 ELSE NULL END,
                 ((n - 1) % 25) + 1,
                 'WO-B3-' || (3000+n), 'WO-B3-' || (3000+n),
                 (ARRAY['Brakes','Tires','Engine','Electrical','DVIR Defect','Preventive'])[(n % 6)+1],
                 'Batch 3 work order ' || n, 'Seeded repair execution work order.',
                 (ARRAY['Low','Medium','High','Critical'])[(n % 4)+1],
                 (ARRAY['Draft','Open','Assigned','In Progress','Waiting Parts','Waiting Approval','Completed','Cancelled'])[(n % 8)+1],
                 ((n - 1) % 5) + 1,
                 (ARRAY['NOVA Fleet Care','Dulles Diesel','Potomac Tire & Brake','Capital Mobile Service'])[(n % 4)+1],
                 NOW() - n * INTERVAL '1 day', NOW() + (n-6) * INTERVAL '1 day',
                 450+n*80, CASE WHEN n % 3 = 0 THEN 420+n*65 ELSE NULL END, n*1.5,
                 CASE WHEN n % 4 = 0 THEN 'Approved' ELSE 'Pending' END,
                 CASE WHEN n % 5 = 0 THEN 91 ELSE 35+(n%50) END,
                 CASE WHEN n % 5 = 0 THEN 'Approve cost and move to critical repair lane' ELSE 'Monitor vendor SLA and parts availability' END,
                 'Batch 3 work order seed.'
          FROM seq WHERE (SELECT COUNT(*) FROM work_orders WHERE deleted_at IS NULL) < 20",
        @"INSERT INTO work_order_labor (company_id, work_order_id, technician_name, labor_hours, labor_rate, total_cost, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
          SELECT 1, ((n - 1) % 20) + 1,
                 (ARRAY['A. Patel','J. Morgan','S. Rivera','K. Brooks','M. Chen'])[(n%5)+1],
                 1 + (n % 5), 95, (1 + (n % 5))*95, 'Batch 3 labor line.'
          FROM seq WHERE (SELECT COUNT(*) FROM work_order_labor) < 20",
        @"INSERT INTO work_order_parts (company_id, work_order_id, part_name, part_number, quantity, unit_cost, total_cost, status, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
          SELECT 1, ((n - 1) % 20) + 1,
                 (ARRAY['Brake pad set','Steer tire','Oil filter','ABS sensor','Reefer belt','LED marker'])[(n%6)+1],
                 'PART-' || (7000+n), 1+(n%3), 65+n*8, (1+(n%3))*(65+n*8),
                 CASE WHEN n%5 = 0 THEN 'Delayed' ELSE 'Reserved' END, 'Batch 3 parts line.'
          FROM seq WHERE (SELECT COUNT(*) FROM work_order_parts) < 20",
        @"INSERT INTO work_order_status_events (company_id, work_order_id, previous_status, new_status, event_title, event_description)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
          SELECT 1, ((n - 1) % 20) + 1, 'Open',
                 (ARRAY['Assigned','In Progress','Waiting Parts','Waiting Approval','Completed'])[(n%5)+1],
                 'Work order event ' || n, 'Seeded status event for Batch 3.'
          FROM seq WHERE (SELECT COUNT(*) FROM work_order_status_events) < 20",
        @"INSERT INTO dvir_templates (company_id, template_name, country_code, vehicle_type, inspection_type, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 5)
          SELECT 1, 'US DVIR Template ' || n, 'US',
                 (ARRAY['Truck','Van','Box Truck','Reefer'])[(n%4)+1],
                 (ARRAY['Pre-Trip','Post-Trip'])[(n%2)+1], 'Active'
          FROM seq WHERE (SELECT COUNT(*) FROM dvir_templates) < 5",
        @"INSERT INTO inspection_checklist_items (company_id, template_id, item_label, item_category, required, sort_order)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 50)
          SELECT 1, ((n - 1) % 5) + 1,
                 (ARRAY['Brakes','Lights','Tires','Steering','Mirrors','Horn','Wipers','Coupling','Emergency kit','Leaks'])[(n%10)+1] || ' check',
                 (ARRAY['Safety','Exterior','Cab','Powertrain','Compliance'])[(n%5)+1], TRUE, n
          FROM seq WHERE (SELECT COUNT(*) FROM inspection_checklist_items) < 50",
        @"INSERT INTO dvir_reports (company_id, report_number, driver_id, vehicle_id, country_code, inspection_type, inspection_status, defects_found, safe_to_operate, driver_signature_status, mechanic_review_status, repair_certification_status, submitted_at, risk_score, recommended_action, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 25)
          SELECT 1, 'DVIR-B3-' || (4000+n), ((n - 1) % 20) + 1, ((n - 1) % 20) + 1, 'US',
                 (ARRAY['Pre-Trip','Post-Trip'])[(n%2)+1],
                 (ARRAY['Submitted','Mechanic Review','Repair Required','Certified','Signed'])[(n%5)+1],
                 CASE WHEN n%4 = 0 THEN 2 WHEN n%3 = 0 THEN 1 ELSE 0 END,
                 n%4 <> 0,
                 CASE WHEN n%5 = 0 THEN 'Missing' ELSE 'Signed' END,
                 CASE WHEN n%4 = 0 THEN 'Pending' ELSE 'Reviewed' END,
                 CASE WHEN n%4 = 0 THEN 'Pending' ELSE 'Certified' END,
                 NOW() - n * INTERVAL '1 hour',
                 CASE WHEN n%4 = 0 THEN 88 ELSE 22+(n%40) END,
                 CASE WHEN n%4 = 0 THEN 'Block dispatch and create work order' ELSE 'Archive inspection and monitor repeats' END,
                 'Batch 3 DVIR report.'
          FROM seq WHERE (SELECT COUNT(*) FROM dvir_reports WHERE deleted_at IS NULL) < 25",
        @"INSERT INTO dvir_defects (company_id, dvir_report_id, defect_category, defect_description, severity, status, linked_work_order_id)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 30)
          SELECT 1, ((n - 1) % 25) + 1,
                 (ARRAY['Brakes','Tires','Lights','Steering','Coupling','Leaks'])[(n%6)+1],
                 'Seeded DVIR defect ' || n,
                 (ARRAY['Minor','Major','Critical','Major'])[(n%4)+1],
                 CASE WHEN n%3 = 0 THEN 'Resolved' ELSE 'Open' END,
                 CASE WHEN n%5 = 0 THEN ((n - 1) % 20) + 1 ELSE NULL END
          FROM seq WHERE (SELECT COUNT(*) FROM dvir_defects) < 30",
        @"INSERT INTO documents (company_id, title, document_number, entity_type, entity_id, document_type, category, country_code, issuing_authority, issued_at, expires_at, status, renewal_status, file_url, risk_score, recommended_action, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 40)
          SELECT 1, 'Batch 3 document ' || n, 'DOC-B3-' || (5000+n),
                 (ARRAY['vehicle','driver','company','asset','customer','carrier','contract','work order','inspection'])[(n%9)+1],
                 ((n - 1) % 20) + 1,
                 (ARRAY['Registration','Insurance','Annual Inspection','Driver License','Medical Card','Permit','DVIR Report','Contract'])[(n%8)+1],
                 (ARRAY['Registration','Insurance','Annual Inspection','Driver License','Medical Card','Permit','DVIR Report','Contract'])[(n%8)+1],
                 CASE WHEN n%6 = 0 THEN 'CA' ELSE 'US' END,
                 (ARRAY['Virginia DMV','FMCSA','Insurance Carrier','OpsTrax Vault'])[(n%4)+1],
                 CURRENT_DATE - (180+n) * INTERVAL '1 day', CURRENT_DATE + (n-18) * INTERVAL '1 day',
                 CASE WHEN n < 18 THEN 'Expiring' WHEN n%5 = 0 THEN 'Expired' ELSE 'Active' END,
                 CASE WHEN n < 18 THEN 'Renewal Required' ELSE 'Current' END,
                 '/placeholder/document.pdf',
                 CASE WHEN n < 18 THEN 85 ELSE 20+(n%35) END,
                 CASE WHEN n < 18 THEN 'Start renewal and add to audit package' ELSE 'Keep active in vault' END,
                 'Batch 3 document vault record.'
          FROM seq WHERE (SELECT COUNT(*) FROM documents WHERE deleted_at IS NULL) < 40",
        @"INSERT INTO document_timeline_events (company_id, document_id, event_title, event_description)
          SELECT 1, d.id, 'Document vault seeded', 'Batch 3 document entered audit-ready vault.' FROM documents d
          WHERE NOT EXISTS (SELECT 1 FROM document_timeline_events e WHERE e.document_id=d.id) LIMIT 40",
        @"INSERT INTO ai_recommendations (company_id, module_key, title, body, score, status)
          SELECT 1, x.module_key, x.title, x.body, x.score, 'Recommended'
          FROM (
            SELECT 'maintenance' module_key, 'Predictive maintenance warning' title, 'TRK-117 pattern suggests repeat brake risk. Create a critical work order before next dispatch.' body, 95 score
            UNION ALL SELECT 'work-orders','Cost approval intelligence','Waiting approval repairs are creating downtime exposure. Approve or reject cost within the next service window.',92
            UNION ALL SELECT 'dvir','Unsafe vehicle lockout recommendation','Critical DVIR defects should block dispatch until mechanic review and repair certification are complete.',96
            UNION ALL SELECT 'dvir-inspections','Repeat defect intelligence','Repeated lights/brake defects indicate a preventive maintenance schedule should be advanced.',90
            UNION ALL SELECT 'documents','Document expiry risk','Medical card and vehicle insurance documents entering 30-day renewal window should be queued for renewal.',94
          ) x WHERE NOT EXISTS (SELECT 1 FROM ai_recommendations ar WHERE ar.module_key=x.module_key AND ar.title=x.title)",
        @"INSERT INTO notifications (company_id, event_type, title, message, severity, module_key, status)
          SELECT 1, 'system.alert', x.title, x.body, x.severity, x.module_key, 'unread'
          FROM (
            SELECT 'Maintenance due' title, 'Service due this week for multiple NOVA/DC fleet units.' body, 'Warning' severity, 'maintenance' module_key
            UNION ALL SELECT 'Critical DVIR defect','Unsafe-to-operate DVIR needs mechanic review.','Critical','dvir-inspections'
            UNION ALL SELECT 'Document expiring','Compliance document renewal queue has high-risk records.','Warning','documents'
          ) x WHERE NOT EXISTS (SELECT 1 FROM notifications n WHERE n.title=x.title AND n.module_key=x.module_key)"
    ];
}
