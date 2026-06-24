using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch7SchemaService(Database db)
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
            @"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name=@table AND column_name=@column",
            c => { c.Parameters.AddWithValue("@table", table); c.Parameters.AddWithValue("@column", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        // Enrich audit_logs with severity + module context
        new("audit_logs", "severity",     "VARCHAR(40) NOT NULL DEFAULT 'Info'"),
        new("audit_logs", "module_key",   "VARCHAR(100) NULL"),
        new("audit_logs", "action_type",  "VARCHAR(80) NOT NULL DEFAULT 'update'"),
        // Enrich ai_recommendations with new fields (safe — uses IGNORE on insert)
        new("ai_recommendations", "description",  "TEXT NULL"),
        new("ai_recommendations", "priority",     "VARCHAR(40) NOT NULL DEFAULT 'Medium'"),
        new("ai_recommendations", "action_label", "VARCHAR(120) NULL"),
        new("ai_recommendations", "action_type",  "VARCHAR(80) NULL"),
        // Existing base schema had a smaller SLA table; Batch 7 needs the richer KPI/SLA center shape.
        new("sla_records", "tenant_id",           "BIGINT NOT NULL DEFAULT 1"),
        new("sla_records", "sla_number",          "VARCHAR(80) NULL"),
        new("sla_records", "job_id",              "BIGINT NULL"),
        new("sla_records", "route_id",            "BIGINT NULL"),
        new("sla_records", "sla_type",            "VARCHAR(80) NOT NULL DEFAULT 'On-Time Delivery'"),
        new("sla_records", "unit",                "VARCHAR(40) NOT NULL DEFAULT '%'"),
        new("sla_records", "breach_reason",       "TEXT NULL"),
        new("sla_records", "risk_score",          "DECIMAL(6,2) NOT NULL DEFAULT 20"),
        new("sla_records", "owner_role",          "VARCHAR(80) NULL"),
        new("sla_records", "recommended_action",  "TEXT NULL"),
        new("sla_records", "measured_at",         "TIMESTAMPTZ NULL"),
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS report_catalog (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NULL,
            report_key VARCHAR(100) NOT NULL UNIQUE,
            report_name VARCHAR(220) NOT NULL,
            report_category VARCHAR(100) NOT NULL,
            description TEXT NULL,
            default_filters_json JSONB NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS report_runs (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            report_key VARCHAR(100) NOT NULL,
            report_name VARCHAR(220) NOT NULL,
            filters_json JSONB NULL,
            run_by_user_id BIGINT NULL,
            run_by_name VARCHAR(160) NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Completed',
            row_count INT NULL,
            result_summary_json JSONB NULL,
            started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            completed_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",

        @"CREATE TABLE IF NOT EXISTS scheduled_reports (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            report_key VARCHAR(100) NOT NULL,
            report_name VARCHAR(220) NOT NULL,
            schedule_name VARCHAR(200) NOT NULL,
            frequency VARCHAR(40) NOT NULL DEFAULT 'Weekly',
            recipients_json JSONB NULL,
            filters_json JSONB NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Active',
            next_run_at TIMESTAMPTZ NULL,
            last_run_at TIMESTAMPTZ NULL,
            created_by_user_id BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS report_exports (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            report_run_id BIGINT NULL,
            report_key VARCHAR(100) NOT NULL,
            export_type VARCHAR(40) NOT NULL DEFAULT 'CSV',
            status VARCHAR(40) NOT NULL DEFAULT 'Pending',
            export_url VARCHAR(500) NULL,
            requested_by_user_id BIGINT NULL,
            requested_by_name VARCHAR(160) NULL,
            requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            completed_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS kpi_metrics (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            kpi_code VARCHAR(80) NOT NULL,
            kpi_name VARCHAR(220) NOT NULL,
            category VARCHAR(80) NOT NULL,
            target_value DECIMAL(12,4) NULL,
            actual_value DECIMAL(12,4) NOT NULL DEFAULT 0,
            unit VARCHAR(40) NOT NULL DEFAULT '%',
            trend VARCHAR(20) NOT NULL DEFAULT 'stable',
            status VARCHAR(40) NOT NULL DEFAULT 'On Target',
            owner_role VARCHAR(80) NULL,
            recommendation TEXT NULL,
            last_calculated_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS kpi_targets (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            kpi_code VARCHAR(80) NOT NULL,
            target_value DECIMAL(12,4) NOT NULL,
            unit VARCHAR(40) NOT NULL DEFAULT '%',
            effective_date DATE NOT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS sla_records (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            sla_number VARCHAR(80) NOT NULL UNIQUE,
            customer_id BIGINT NULL,
            job_id BIGINT NULL,
            route_id BIGINT NULL,
            sla_type VARCHAR(80) NOT NULL DEFAULT 'On-Time Delivery',
            target_value DECIMAL(10,2) NOT NULL DEFAULT 100,
            actual_value DECIMAL(10,2) NULL,
            unit VARCHAR(40) NOT NULL DEFAULT '%',
            status VARCHAR(40) NOT NULL DEFAULT 'Met',
            breach_reason VARCHAR(200) NULL,
            risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
            owner_role VARCHAR(80) NULL,
            recommended_action TEXT NULL,
            measured_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        @"CREATE TABLE IF NOT EXISTS sla_breaches (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            sla_record_id BIGINT NOT NULL,
            breach_type VARCHAR(80) NOT NULL DEFAULT 'Delivery Delay',
            severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
            description TEXT NULL,
            root_cause_placeholder VARCHAR(200) NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Open',
            detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            resolved_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",

        @"CREATE TABLE IF NOT EXISTS executive_snapshots (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            snapshot_date DATE NOT NULL,
            operations_health_score DECIMAL(5,2) NOT NULL DEFAULT 80,
            cost_health_score DECIMAL(5,2) NOT NULL DEFAULT 75,
            safety_health_score DECIMAL(5,2) NOT NULL DEFAULT 85,
            compliance_health_score DECIMAL(5,2) NOT NULL DEFAULT 88,
            customer_sla_score DECIMAL(5,2) NOT NULL DEFAULT 82,
            fleet_readiness_score DECIMAL(5,2) NOT NULL DEFAULT 79,
            dispatch_readiness_score DECIMAL(5,2) NOT NULL DEFAULT 86,
            top_risks_json JSONB NULL,
            top_savings_json JSONB NULL,
            ai_brief TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",

        @"CREATE TABLE IF NOT EXISTS audit_export_requests (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NOT NULL DEFAULT 1,
            requested_by_user_id BIGINT NULL,
            requested_by_name VARCHAR(160) NULL,
            date_from DATE NULL,
            date_to DATE NULL,
            filters_json JSONB NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Pending',
            export_url VARCHAR(500) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            completed_at TIMESTAMPTZ NULL
        )",

        // Workforce shift scheduling — one row per driver per week
        @"CREATE TABLE IF NOT EXISTS workforce_schedules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            driver_id BIGINT NOT NULL,
            week_start DATE NOT NULL,
            monday VARCHAR(40) NOT NULL DEFAULT 'Off',
            tuesday VARCHAR(40) NOT NULL DEFAULT 'Off',
            wednesday VARCHAR(40) NOT NULL DEFAULT 'Off',
            thursday VARCHAR(40) NOT NULL DEFAULT 'Off',
            friday VARCHAR(40) NOT NULL DEFAULT 'Off',
            saturday VARCHAR(40) NOT NULL DEFAULT 'Off',
            sunday VARCHAR(40) NOT NULL DEFAULT 'Off',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (driver_id, week_start)
        )",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_report_runs_key ON report_runs(report_key)",
        "CREATE INDEX IF NOT EXISTS idx_report_runs_tenant ON report_runs(tenant_id)",
        "CREATE INDEX IF NOT EXISTS idx_scheduled_reports_key ON scheduled_reports(report_key)",
        "CREATE INDEX IF NOT EXISTS idx_report_exports_key ON report_exports(report_key)",
        "CREATE INDEX IF NOT EXISTS idx_kpi_metrics_code ON kpi_metrics(kpi_code)",
        "CREATE INDEX IF NOT EXISTS idx_kpi_metrics_category ON kpi_metrics(category)",
        "CREATE INDEX IF NOT EXISTS idx_kpi_targets_code ON kpi_targets(kpi_code)",
        "CREATE INDEX IF NOT EXISTS idx_sla_records_customer ON sla_records(customer_id)",
        "CREATE INDEX IF NOT EXISTS idx_sla_records_job ON sla_records(job_id)",
        "CREATE INDEX IF NOT EXISTS idx_sla_records_status ON sla_records(status)",
        "CREATE INDEX IF NOT EXISTS idx_sla_records_risk ON sla_records(risk_score)",
        "CREATE INDEX IF NOT EXISTS idx_sla_breaches_record ON sla_breaches(sla_record_id)",
        "CREATE INDEX IF NOT EXISTS idx_executive_snapshots_date ON executive_snapshots(snapshot_date)",
        "CREATE INDEX IF NOT EXISTS idx_audit_logs_module ON audit_logs(module_key)",
        "CREATE INDEX IF NOT EXISTS idx_audit_logs_severity ON audit_logs(severity)",
        "CREATE INDEX IF NOT EXISTS idx_audit_logs_action ON audit_logs(action_name)",
    ];

    private static readonly string[] Seeds =
    [
        // ── REPORT CATALOG (30 records) ──────────────────────────────────────────────
        @"INSERT INTO report_catalog (report_key,report_name,report_category,description,status) VALUES
          ('fleet-utilization',      'Fleet Utilization Report',           'Fleet Operations', 'Vehicle utilization, idle time, and uptime metrics across the fleet.',                          'Active'),
          ('vehicle-readiness',      'Vehicle Readiness Report',           'Fleet Operations', 'Fleet readiness by status, last inspection, maintenance backlog, and ELD health.',              'Active'),
          ('driver-utilization',     'Driver Utilization Report',          'Fleet Operations', 'Driver hours, assignments, utilization %, and availability by period.',                         'Active'),
          ('jobs-completed',         'Jobs Completed Report',              'Jobs & Orders',    'Summary of completed jobs by customer, driver, vehicle, and route.',                            'Active'),
          ('delayed-jobs',           'Delayed Jobs Report',                'Jobs & Orders',    'Jobs with delays, ETA variance, delay reasons, and SLA impact.',                                'Active'),
          ('sla-at-risk',            'SLA At-Risk Report',                 'Jobs & Orders',    'Jobs and customers currently at risk of SLA breach with risk scores.',                          'Active'),
          ('dispatch-plan',          'Dispatch Plan Report',               'Dispatch',         'Dispatch assignments, readiness, AI recommendations, and coverage gaps.',                       'Active'),
          ('route-efficiency',       'Route Efficiency Report',            'Route Planning',   'Route performance, stop efficiency, mileage vs plan, and cost per mile.',                       'Active'),
          ('customer-eta',           'Customer ETA Report',                'Customer ETA',     'ETA accuracy, communication history, and customer feedback by job.',                            'Active'),
          ('maintenance-due',        'Maintenance Due Report',             'Maintenance',      'Upcoming and overdue maintenance items with priority and cost estimates.',                       'Active'),
          ('work-order-report',      'Work Order Report',                  'Work Orders',      'Work order status, labor, parts cost, and completion time by vehicle.',                         'Active'),
          ('dvir-compliance',        'DVIR Compliance Report',             'DVIR/Inspections', 'Pre/post-trip inspection compliance rate, defects, and critical findings.',                     'Active'),
          ('safety-events',          'Safety Events Report',               'Safety',           'Safety event register by severity, type, driver, vehicle, and date range.',                    'Active'),
          ('driver-coaching',        'Driver Coaching Report',             'Safety',           'Coaching task completion, acknowledgement rate, and safety score impact.',                      'Active'),
          ('dashcam-review',         'Dashcam Event Review Report',        'Dashcam/Incidents','AI dashcam events, false positive rate, review status, and coaching linkage.',                  'Active'),
          ('incident-register',      'Incident Register Report',           'Dashcam/Incidents','Full incident register with severity, insurance status, and legal review flag.',                'Active'),
          ('fuel-spend',             'Fuel Spend Report',                  'Fuel & Idling',    'Fuel transaction totals by vehicle, driver, fuel type, and region with anomaly flags.',         'Active'),
          ('idle-cost',              'Idle Cost Report',                   'Fuel & Idling',    'Idle event costs, duration by vehicle, driver, and location.',                                  'Active'),
          ('expense-register',       'Expense Register Report',            'Expenses',         'Operating expense register with category, approval status, and risk scores.',                   'Active'),
          ('contract-renewal',       'Contract Renewal Report',            'Contracts/Rates',  'Contracts expiring within 30/60/90 days with renewal risk and action queue.',                   'Active'),
          ('carrier-performance',    'Carrier Performance Report',         'Carriers',         'Carrier on-time %, safety score, compliance status, and contract health.',                      'Active'),
          ('cost-leakage-report',    'Cost Leakage Report',                'Cost & Margin',    'Leakage by category, estimated loss, and recoverable savings opportunities.',                   'Active'),
          ('margin-risk',            'Margin Risk Report',                 'Cost & Margin',    'Jobs and routes with low or negative margin with root-cause breakdown.',                        'Active'),
          ('country-compliance',     'Country Compliance Report',          'Compliance',       'Compliance status by country profile, violation counts, and audit readiness.',                  'Active'),
          ('hos-warning',            'HOS Warning Report',                 'HOS/ELD',          'Drivers approaching or exceeding HOS limits by country ruleset.',                               'Active'),
          ('expiring-documents',     'Expiring Documents Report',          'Documents',        'Driver and vehicle documents expiring within 30/60/90 days by category.',                       'Active'),
          ('audit-log-report',       'Audit Log Report',                   'Audit Logs',       'System audit trail filtered by module, user, action, severity, and date.',                     'Active'),
          ('executive-summary',      'Executive Operations Summary',       'Executive',        'Board-ready executive summary of fleet health, cost, safety, and SLA performance.',            'Active'),
          ('kpi-scorecard',          'KPI Scorecard Report',               'SLA/KPI',          'Full KPI scorecard with targets, actuals, trends, and drift detection.',                       'Active'),
          ('sla-scorecard',          'SLA Scorecard Report',               'SLA/KPI',          'Customer SLA scorecard with breach history, risk scores, and recommended actions.',            'Active')
          ON CONFLICT DO NOTHING",

        // ── REPORT RUN HISTORY (20 records) ────────────────────────────────────────
        @"INSERT INTO report_runs (id,tenant_id,report_key,report_name,run_by_name,status,row_count,started_at,completed_at) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'fleet-utilization','Fleet Utilization Report','admin','Completed',18,'2026-05-20 08:00:00','2026-05-20 08:00:45'),
          (2,1,'delayed-jobs','Delayed Jobs Report','admin','Completed',7,'2026-05-20 08:15:00','2026-05-20 08:15:12'),
          (3,1,'safety-events','Safety Events Report','admin','Completed',42,'2026-05-20 09:00:00','2026-05-20 09:00:38'),
          (4,1,'fuel-spend','Fuel Spend Report','admin','Completed',156,'2026-05-21 07:30:00','2026-05-21 07:31:02'),
          (5,1,'maintenance-due','Maintenance Due Report','admin','Completed',9,'2026-05-21 08:00:00','2026-05-21 08:00:22'),
          (6,1,'sla-at-risk','SLA At-Risk Report','admin','Completed',6,'2026-05-21 08:30:00','2026-05-21 08:30:18'),
          (7,1,'executive-summary','Executive Operations Summary','admin','Completed',1,'2026-05-21 09:00:00','2026-05-21 09:00:55'),
          (8,1,'country-compliance','Country Compliance Report','admin','Completed',10,'2026-05-22 08:00:00','2026-05-22 08:00:31'),
          (9,1,'audit-log-report','Audit Log Report','admin','Completed',248,'2026-05-22 09:00:00','2026-05-22 09:01:44'),
          (10,1,'driver-utilization','Driver Utilization Report','admin','Completed',20,'2026-05-22 10:00:00','2026-05-22 10:00:28'),
          (11,1,'cost-leakage-report','Cost Leakage Report','admin','Completed',12,'2026-05-22 11:00:00','2026-05-22 11:00:41'),
          (12,1,'hos-warning','HOS Warning Report','admin','Completed',4,'2026-05-23 07:00:00','2026-05-23 07:00:14'),
          (13,1,'expiring-documents','Expiring Documents Report','admin','Completed',8,'2026-05-23 08:00:00','2026-05-23 08:00:19'),
          (14,1,'dvir-compliance','DVIR Compliance Report','admin','Completed',55,'2026-05-23 09:00:00','2026-05-23 09:00:33'),
          (15,1,'kpi-scorecard','KPI Scorecard Report','admin','Completed',30,'2026-05-23 10:00:00','2026-05-23 10:00:47'),
          (16,1,'carrier-performance','Carrier Performance Report','admin','Completed',15,'2026-05-23 11:00:00','2026-05-23 11:00:22'),
          (17,1,'margin-risk','Margin Risk Report','admin','Completed',11,'2026-05-23 12:00:00','2026-05-23 12:00:36'),
          (18,1,'fleet-utilization','Fleet Utilization Report','admin','Completed',18,'2026-05-24 07:30:00','2026-05-24 07:30:41'),
          (19,1,'sla-scorecard','SLA Scorecard Report','admin','Completed',30,'2026-05-24 08:00:00','2026-05-24 08:00:52'),
          (20,1,'delayed-jobs','Delayed Jobs Report','admin','Running',NULL,'2026-05-24 08:15:00',NULL)
          ON CONFLICT DO NOTHING",

        // ── SCHEDULED REPORTS (8 records) ──────────────────────────────────────────
        @"INSERT INTO scheduled_reports (id,tenant_id,report_key,report_name,schedule_name,frequency,status,next_run_at) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'executive-summary','Executive Operations Summary','Daily Executive Brief','Daily','Active','2026-05-25 06:00:00'),
          (2,1,'fleet-utilization','Fleet Utilization Report','Weekly Fleet Report','Weekly','Active','2026-05-27 06:00:00'),
          (3,1,'sla-at-risk','SLA At-Risk Report','Daily SLA Watch','Daily','Active','2026-05-25 07:00:00'),
          (4,1,'safety-events','Safety Events Report','Weekly Safety Review','Weekly','Active','2026-05-27 08:00:00'),
          (5,1,'fuel-spend','Fuel Spend Report','Monthly Fuel Report','Monthly','Active','2026-06-01 06:00:00'),
          (6,1,'audit-log-report','Audit Log Report','Weekly Audit Export','Weekly','Active','2026-05-27 09:00:00'),
          (7,1,'hos-warning','HOS Warning Report','Daily HOS Alert','Daily','Active','2026-05-25 05:30:00'),
          (8,1,'country-compliance','Country Compliance Report','Monthly Compliance Summary','Monthly','Paused','2026-06-01 08:00:00')
          ON CONFLICT DO NOTHING",

        // ── REPORT EXPORTS (10 records) ─────────────────────────────────────────────
        @"INSERT INTO report_exports (id,tenant_id,report_run_id,report_key,export_type,status,requested_by_name,requested_at,completed_at) OVERRIDING SYSTEM VALUE VALUES
          (1,1,1,'fleet-utilization','CSV','Completed','admin','2026-05-20 08:05:00','2026-05-20 08:05:08'),
          (2,1,3,'safety-events','PDF','Completed','admin','2026-05-20 09:05:00','2026-05-20 09:05:15'),
          (3,1,4,'fuel-spend','XLSX','Completed','admin','2026-05-21 07:35:00','2026-05-21 07:35:22'),
          (4,1,7,'executive-summary','Executive PDF','Completed','admin','2026-05-21 09:10:00','2026-05-21 09:10:44'),
          (5,1,9,'audit-log-report','CSV','Completed','admin','2026-05-22 09:10:00','2026-05-22 09:10:31'),
          (6,1,11,'cost-leakage-report','XLSX','Completed','admin','2026-05-22 11:10:00','2026-05-22 11:10:19'),
          (7,1,14,'dvir-compliance','PDF','Completed','admin','2026-05-23 09:10:00','2026-05-23 09:10:27'),
          (8,1,15,'kpi-scorecard','XLSX','Completed','admin','2026-05-23 10:10:00','2026-05-23 10:10:33'),
          (9,1,19,'sla-scorecard','PDF','Pending','admin','2026-05-24 08:10:00',NULL),
          (10,1,NULL,'executive-summary','Executive PDF','Pending','admin','2026-05-24 08:30:00',NULL)
          ON CONFLICT DO NOTHING",

        // ── KPI METRICS (30 records) ────────────────────────────────────────────────
        @"INSERT INTO kpi_metrics (id,tenant_id,kpi_code,kpi_name,category,target_value,actual_value,unit,trend,status,owner_role,recommendation) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'OTD','On-Time Delivery','Dispatch',95,87.4,'%','down','At Risk','Fleet Manager','Investigate 6 high-delay jobs and review dispatch scheduling gaps.'),
          (2,1,1,'SLA-COMP','SLA Compliance','Customer Service',98,91.2,'%','down','At Risk','Company Admin','Review 4 SLA breach records and escalate customer communications.'),
          (3,1,'ETA-ACC','Average ETA Accuracy','Dispatch',90,84.6,'%','stable','Watch','Fleet Manager','Improve route planning for 3 high-variance lanes.'),
          (4,1,'JOBS-COMP','Jobs Completed','Operations',200,187,'count','up','On Target','Fleet Manager','On track. 187 of 200 target jobs completed this period.'),
          (5,1,'DELAYED-JOBS','Delayed Jobs','Operations',5,9,'count','up','Breached','Fleet Manager','9 delayed jobs — 4 are repeat lanes. Dispatch re-optimization recommended.'),
          (6,1,'DISPATCH-READ','Dispatch Readiness','Dispatch',95,92.1,'%','stable','On Target','Fleet Manager','Dispatch coverage is healthy. Monitor 2 understaffed time windows.'),
          (7,1,'VEH-UTIL','Vehicle Utilization','Fleet',80,74.3,'%','down','Watch','Fleet Manager','6 vehicles under 70% utilization. Review assignment efficiency.'),
          (8,1,'DRV-UTIL','Driver Utilization','Fleet',85,81.7,'%','stable','On Target','Fleet Manager','Driver utilization within target. Driver 4 has HOS restriction.'),
          (9,1,'SAFETY-SCORE','Fleet Safety Score','Safety',90,82.4,'%','down','Watch','Safety Manager','3 high-severity safety events this period. Coaching queue has 5 open tasks.'),
          (10,1,'MAINT-COMP','Maintenance Compliance','Maintenance',95,88.9,'%','stable','Watch','Fleet Manager','4 vehicles overdue for preventive maintenance. Schedule within 72 hours.'),
          (11,1,'DVIR-COMP','DVIR Compliance','Maintenance',98,94.7,'%','up','Watch','Fleet Manager','DVIR compliance improved. 3 reports still pending mechanic review.'),
          (12,1,'FUEL-EFF','Fuel Efficiency','Finance',8.5,7.9,'mpg','down','Watch','Finance Manager','Fleet average MPG below target. Idling and route inefficiency are primary drivers.'),
          (13,1,'IDLE-COST','Idle Cost','Finance',500,847.50,'USD/week','up','Breached','Finance Manager','Idle cost exceeds target by $347.50/week. Top 3 vehicles account for 68%.'),
          (14,1,'CPM','Cost per Mile','Finance',2.20,2.48,'USD','up','At Risk','Finance Manager','CPM 12.7% above target. Fuel and maintenance costs are primary drivers.'),
          (15,1,'GROSS-MARGIN','Gross Margin','Finance',22,17.4,'%','down','At Risk','Finance Manager','Margin below target. 4 routes operating below 10% margin.'),
          (16,1,'CX-SCORE','Customer Experience Score','Customer Service',90,85.2,'%','stable','Watch','Company Admin','Customer satisfaction declined in 2 accounts. Review ETA communication quality.'),
          (17,1,'FLEET-READY','Fleet Readiness','Fleet',90,78.6,'%','down','Watch','Fleet Manager','4 vehicles offline. ELD malfunction on TRK-104 requires immediate resolution.'),
          (18,1,'HOS-COMPLY','HOS Compliance','Compliance',100,86.7,'%','down','At Risk','Compliance Manager','3 active HOS violations. Driver 4 has repeat pattern requiring review.'),
          (19,1,'ELD-HEALTH','ELD Device Health','Compliance',100,90,'%','down','Watch','Compliance Manager','1 ELD malfunction, 1 diagnostic. FMCSA notification required for malfunction.'),
          (20,1,'DOC-VALID','Document Validity','Compliance',100,94.2,'%','stable','Watch','Compliance Manager','8 documents expiring within 30 days. CDL and medical certs at highest risk.'),
          (21,1,'MAINT-COST','Maintenance Cost per Vehicle','Maintenance',800,1124,'USD/month','up','Breached','Fleet Manager','Maintenance cost exceeds budget. VAN-112 accounts for 34% of total spend.'),
          (22,1,'WORK-ORDER-COMP','Work Order Completion','Maintenance',95,91.4,'%','up','Watch','Fleet Manager','Work order completion improving. 3 high-priority orders still pending parts.'),
          (23,1,'CARRIER-PERF','Carrier On-Time Performance','Dispatch',92,88.1,'%','stable','Watch','Fleet Manager','3 carriers below performance threshold. Review contract terms.'),
          (24,1,'INCIDENT-RATE','Incident Rate','Safety',2,4,'per 1M miles','up','Breached','Safety Manager','Incident rate doubled this period. Review dashcam events and coaching assignments.'),
          (25,1,'PROOF-COMP','Proof of Delivery Completion','Operations',98,95.7,'%','stable','On Target','Fleet Manager','POD completion near target. 2 open proof items require driver follow-up.'),
          (26,1,'CUSTOMER-SLA-MET','Customer SLA Met','Customer Service',95,88.4,'%','down','At Risk','Company Admin','SLA met below 90% for 2 customers. Priority escalation recommended.'),
          (27,1,'COST-LEAKAGE','Recoverable Cost Leakage','Finance',0,14280,'USD/month','stable','Breached','Finance Manager','$14,280 in recoverable leakage identified. Top categories: idling and unauthorized expenses.'),
          (28,1,'AUDIT-COVERAGE','Audit Coverage Score','Compliance',95,91.3,'%','stable','Watch','Compliance Manager','Audit coverage near target. 23 recent actions pending audit log enrichment.'),
          (29,1,'DISPATCH-CYCLE','Dispatch Cycle Time','Dispatch',30,38,'minutes','up','At Risk','Fleet Manager','Average dispatch cycle 27% above target. AI matching adoption needs improvement.'),
          (30,1,'ROUTE-EFF','Route Efficiency','Operations',88,83.2,'%','stable','Watch','Fleet Manager','Route efficiency below target on 4 lanes. Re-optimization would save ~$1,200/week.')
          ON CONFLICT DO NOTHING",

        // ── KPI TARGETS (20 records) ────────────────────────────────────────────────
        @"INSERT INTO kpi_targets (id,tenant_id,kpi_code,target_value,unit,effective_date,status) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'OTD',95,'%','2026-01-01','Active'),
          (2,1,1,'SLA-COMP',98,'%','2026-01-01','Active'),
          (3,1,'ETA-ACC',90,'%','2026-01-01','Active'),
          (4,1,'JOBS-COMP',200,'count','2026-01-01','Active'),
          (5,1,'DELAYED-JOBS',5,'count','2026-01-01','Active'),
          (6,1,'DISPATCH-READ',95,'%','2026-01-01','Active'),
          (7,1,'VEH-UTIL',80,'%','2026-01-01','Active'),
          (8,1,'DRV-UTIL',85,'%','2026-01-01','Active'),
          (9,1,'SAFETY-SCORE',90,'%','2026-01-01','Active'),
          (10,1,'MAINT-COMP',95,'%','2026-01-01','Active'),
          (11,1,'DVIR-COMP',98,'%','2026-01-01','Active'),
          (12,1,'FUEL-EFF',8.5,'mpg','2026-01-01','Active'),
          (13,1,'IDLE-COST',500,'USD/week','2026-01-01','Active'),
          (14,1,'CPM',2.20,'USD','2026-01-01','Active'),
          (15,1,'GROSS-MARGIN',22,'%','2026-01-01','Active'),
          (16,1,'CX-SCORE',90,'%','2026-01-01','Active'),
          (17,1,'FLEET-READY',90,'%','2026-01-01','Active'),
          (18,1,'HOS-COMPLY',100,'%','2026-01-01','Active'),
          (19,1,'INCIDENT-RATE',2,'per 1M miles','2026-01-01','Active'),
          (20,1,'COST-LEAKAGE',0,'USD/month','2026-01-01','Active')
          ON CONFLICT DO NOTHING",

        // ── SLA RECORDS (30 records) ────────────────────────────────────────────────
        @"INSERT INTO sla_records (id,company_id,tenant_id,sla_number,customer_id,job_id,sla_type,metric_name,target_value,actual_value,unit,status,breach_reason,risk_score,owner_role,recommended_action,measured_at) OVERRIDING SYSTEM VALUE VALUES
          (1,1,1,'SLA-001',1,1,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (2,1,1,'SLA-002',2,2,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (3,1,1,'SLA-003',3,3,'On-Time Delivery','On-Time Delivery',100,72,'%','Breached','Dispatch delay',85,'Fleet Manager','Review dispatch assignment for this lane. Schedule driver availability review.',NOW()),
          (4,1,1,'SLA-004',4,4,'ETA Accuracy','ETA Accuracy',90,88,'%','Met',NULL,12,'Fleet Manager','Within acceptable range.',NOW()),
          (5,1,1,'SLA-005',5,5,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (6,1,1,'SLA-006',1,6,'Proof of Delivery','Proof of Delivery',98,95,'%','At Risk','Missing POD document',42,'Fleet Manager','Follow up with driver on 2 incomplete POD records.',NOW()),
          (7,1,1,'SLA-007',2,7,'On-Time Delivery','On-Time Delivery',100,65,'%','Breached','Vehicle breakdown',92,'Fleet Manager','Escalate to customer service. Arrange replacement vehicle.',NOW()),
          (8,1,1,'SLA-008',3,8,'ETA Accuracy','ETA Accuracy',90,91,'%','Met',NULL,8,'Fleet Manager','ETA performance excellent on this lane.',NOW()),
          (9,1,1,'SLA-009',4,9,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (10,1,1,'SLA-010',5,10,'On-Time Delivery','On-Time Delivery',100,78,'%','Breached','Traffic/route delay',78,'Fleet Manager','Review alternate route options. Notify customer proactively.',NOW()),
          (11,1,1,'SLA-011',1,NULL,'Response Time','Response Time',4,3.8,'hours','Met',NULL,8,'Fleet Manager','Response time within SLA.',NOW()),
          (12,1,1,'SLA-012',2,NULL,'Response Time','Response Time',4,5.2,'hours','Breached','Driver availability',67,'Fleet Manager','Add backup driver coverage for this customer time window.',NOW()),
          (13,1,1,'SLA-013',3,NULL,'Communication Frequency','Communication Frequency',24,28,'hours','At Risk','ETA update delayed',38,'Fleet Manager','Automate ETA update triggers for this customer lane.',NOW()),
          (14,1,1,'SLA-014',4,11,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (15,1,1,'SLA-015',5,12,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (16,1,1,'SLA-016',1,13,'ETA Accuracy','ETA Accuracy',90,82,'%','At Risk','Route inefficiency',44,'Fleet Manager','Optimize route for this lane.',NOW()),
          (17,1,1,'SLA-017',2,14,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (18,1,1,'SLA-018',3,15,'On-Time Delivery','On-Time Delivery',100,92,'%','At Risk','Partial delivery',35,'Fleet Manager','Review partial delivery protocol with driver.',NOW()),
          (19,1,1,'SLA-019',4,16,'Proof of Delivery','Proof of Delivery',98,98,'%','Met',NULL,5,'Fleet Manager','POD compliance excellent.',NOW()),
          (20,1,1,'SLA-020',5,17,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (21,1,1,'SLA-021',1,18,'On-Time Delivery','On-Time Delivery',100,55,'%','Breached','Customer delay',88,'Fleet Manager','Document customer delay reason. Adjust SLA terms if recurring.',NOW()),
          (22,1,1,'SLA-022',2,19,'ETA Accuracy','ETA Accuracy',90,94,'%','Met',NULL,5,'Fleet Manager','ETA accuracy above target.',NOW()),
          (23,1,1,'SLA-023',3,20,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (24,1,1,'SLA-024',4,NULL,'Response Time','Response Time',4,4.1,'hours','At Risk','Staff shortage',28,'Fleet Manager','Schedule coverage review for peak hours.',NOW()),
          (25,1,1,'SLA-025',5,NULL,'Communication Frequency','Communication Frequency',24,24,'hours','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (26,1,1,'SLA-026',1,NULL,'On-Time Delivery','On-Time Delivery',100,68,'%','Breached','Unknown',80,'Fleet Manager','Investigate root cause. Missing proof record compounds issue.',NOW()),
          (27,1,1,'SLA-027',2,NULL,'Response Time','Response Time',4,3.5,'hours','Met',NULL,8,'Fleet Manager','Response time within SLA.',NOW()),
          (28,1,1,'SLA-028',3,NULL,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW()),
          (29,1,1,'SLA-029',4,NULL,'ETA Accuracy','ETA Accuracy',90,86,'%','At Risk','Traffic delay',32,'Fleet Manager','Add real-time traffic integration to ETA calculation.',NOW()),
          (30,1,1,'SLA-030',5,NULL,'On-Time Delivery','On-Time Delivery',100,100,'%','Met',NULL,5,'Fleet Manager','No action required.',NOW())
          ON CONFLICT DO NOTHING",

        // ── SLA BREACHES (10 records) ───────────────────────────────────────────────
        @"INSERT INTO sla_breaches (id,tenant_id,sla_record_id,breach_type,severity,description,root_cause_placeholder,status,detected_at) OVERRIDING SYSTEM VALUE VALUES
          (1,1,3,'Delivery Delay','High','Job-3 delivered 4 hours late — dispatch scheduling gap identified.','Dispatch delay — driver not assigned until 2 hours after window opened.','Open','2026-05-20 14:00:00'),
          (2,1,7,'Delivery Delay','Critical','Job-7 not delivered — vehicle breakdown mid-route.','Vehicle breakdown — brake failure on TRK-104. Replacement dispatched too late.','Escalated','2026-05-21 16:30:00'),
          (3,1,10,'Route Delay','Medium','Job-10 delayed by 22% of ETA window due to traffic.','Traffic/route delay — I-95 incident caused 90-minute delay.','Acknowledged','2026-05-22 11:00:00'),
          (4,1,12,'Response Time Breach','Medium','Customer-2 ticket response exceeded 4-hour SLA by 1.2 hours.','Driver availability — backup driver not on-call during evening shift.','Open','2026-05-22 20:00:00'),
          (5,1,21,'Delivery Delay','High','Job-18 not delivered — customer loading dock closed early.','Customer delay — receiving hours not communicated to dispatch.','Open','2026-05-23 15:00:00'),
          (6,1,26,'Delivery Delay','High','Unresolved breach — root cause under investigation.','Unknown — GPS data shows vehicle parked for 3 hours. Driver report pending.','Under Investigation','2026-05-23 18:00:00'),
          (7,1,6,'POD Missing','Medium','Customer-1 missing proof of delivery for 2 stops.','Missing POD — driver did not capture photo at delivery point.','Open','2026-05-24 08:00:00'),
          (8,1,13,'Communication Gap','Low','ETA update to Customer-3 delayed by 4 hours.','ETA update delayed — system notification trigger not configured for this lane.','Acknowledged','2026-05-23 12:00:00'),
          (9,1,16,'ETA Inaccuracy','Medium','Customer-1 received ETA 82% accurate — below 90% SLA target.','Route inefficiency — planned route had 2 unscheduled stops added.','Open','2026-05-22 17:00:00'),
          (10,1,18,'Partial Delivery','Low','Customer-3 received partial delivery — 1 item short shipped.','Partial delivery — warehouse picked incorrect pallet. Returns in progress.','Open','2026-05-23 10:00:00')
          ON CONFLICT DO NOTHING",

        // ── EXECUTIVE SNAPSHOTS (10 records — last 10 days) ────────────────────────
        @"INSERT INTO executive_snapshots (id,tenant_id,snapshot_date,operations_health_score,cost_health_score,safety_health_score,compliance_health_score,customer_sla_score,fleet_readiness_score,dispatch_readiness_score,ai_brief) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'2026-05-15',83,78,87,90,86,81,88,'Operations stable. 2 SLA risks in dispatch. Fuel cost trending up 6%.'),
          (2,1,'2026-05-16',82,77,86,90,85,80,87,'On-time delivery dipped to 88%. Driver 2 HOS warning flagged. Maintenance backlog growing.'),
          (3,1,'2026-05-17',80,76,85,89,84,79,86,'Fleet readiness at 79% — 3 vehicles in maintenance. Safety events require coaching review.'),
          (4,1,'2026-05-18',81,75,84,88,83,80,85,'Cost per mile increased to $2.48. Idle cost up. Margin risk on 4 routes identified.'),
          (5,1,'2026-05-19',82,74,83,88,82,80,84,'SLA compliance at 91.2% — below 98% target. 5 SLA breach records opened this week.'),
          (6,1,'2026-05-20',80,73,82,87,81,79,84,'Incident rate doubled. Dashcam events increased. Coaching queue has 5 open tasks.'),
          (7,1,'2026-05-21',79,73,81,87,80,78,83,'Critical: Driver 4 repeat HOS violation. ELD malfunction on TRK-104 unresolved.'),
          (8,1,'2026-05-22',80,74,82,88,81,78,84,'DVIR compliance improving. Cost leakage estimated at $14,280 this period.'),
          (9,1,'2026-05-23',81,75,83,88,82,79,85,'On-time delivery stabilizing at 87.4%. AI recommendations queue has 12 open items.'),
          (10,1,'2026-05-24',82,75,84,89,83,79,86,'Operations in WATCH status. On-time delivery improved 1%. Idle cost and 6 SLA risks require action. ELD malfunction on TRK-104 remains critical.')
          ON CONFLICT DO NOTHING",

        // ── AUDIT LOGS BULK SEED (100+ records across all modules) ──────────────────
        @"INSERT INTO audit_logs (id,company_id,actor_name,action_name,entity_name,entity_id,severity,module_key,action_type,details_json) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'admin','user.login','User',1,'Info','auth','login','{""source"":""api""}'),
          (2,1,'admin','vehicle.created','Vehicle',1,'Info','vehicles','create','{""source"":""api""}'),
          (3,1,'admin','vehicle.updated','Vehicle',2,'Info','vehicles','update','{""source"":""api""}'),
          (4,1,'admin','driver.created','Driver',1,'Info','drivers','create','{""source"":""api""}'),
          (5,1,'admin','job.created','Job',1,'Info','jobs','create','{""source"":""api""}'),
          (6,1,'admin','job.assigned','Job',2,'Info','jobs','update','{""source"":""api""}'),
          (7,1,'admin','job.status_changed','Job',3,'Info','jobs','update','{""source"":""api""}'),
          (8,1,'admin','dispatch.assigned','Dispatch',1,'Info','dispatch','create','{""source"":""api""}'),
          (9,1,'admin','route.created','Route',1,'Info','route-planning','create','{""source"":""api""}'),
          (10,1,'admin','route.optimized','Route',2,'Info','route-planning','update','{""source"":""api""}'),
          (11,1,'admin','maintenance.created','Maintenance',1,'Info','maintenance','create','{""source"":""api""}'),
          (12,1,'admin','maintenance.scheduled','Maintenance',2,'Info','maintenance','update','{""source"":""api""}'),
          (13,1,'admin','maintenance.overdue','Maintenance',3,'Warning','maintenance','flag','{""source"":""api""}'),
          (14,1,'admin','workorder.created','WorkOrder',1,'Info','work-orders','create','{""source"":""api""}'),
          (15,1,'admin','workorder.completed','WorkOrder',2,'Info','work-orders','update','{""source"":""api""}'),
          (16,1,'admin','dvir.submitted','DvirReport',1,'Info','dvir-inspections','create','{""source"":""api""}'),
          (17,1,'admin','dvir.critical_defect','DvirReport',2,'Critical','dvir-inspections','flag','{""source"":""api""}'),
          (18,1,'admin','document.created','Document',1,'Info','documents','create','{""source"":""api""}'),
          (19,1,'admin','document.expiring','Document',2,'Warning','documents','flag','{""source"":""api""}'),
          (20,1,'admin','document.expired','Document',3,'Critical','documents','flag','{""source"":""api""}'),
          (21,1,'admin','safety.event.created','SafetyEvent',1,'Warning','safety','create','{""source"":""api""}'),
          (22,1,'admin','safety.event.reviewed','SafetyEvent',2,'Info','safety','update','{""source"":""api""}'),
          (23,1,'admin','coaching.created','CoachingTask',1,'Info','coaching','create','{""source"":""api""}'),
          (24,1,'admin','coaching.completed','CoachingTask',2,'Info','coaching','update','{""source"":""api""}'),
          (25,1,'admin','dashcam.event.created','DashcamEvent',1,'Warning','dashcam','create','{""source"":""api""}'),
          (26,1,'admin','dashcam.false_positive','DashcamEvent',2,'Info','dashcam','update','{""source"":""api""}'),
          (27,1,'admin','incident.created','Incident',1,'High','incidents','create','{""source"":""api""}'),
          (28,1,'admin','incident.status_changed','Incident',2,'Info','incidents','update','{""source"":""api""}'),
          (29,1,'admin','evidence.package_created','EvidencePackage',1,'Info','evidence-packages','create','{""source"":""api""}'),
          (30,1,'admin','evidence.package_locked','EvidencePackage',2,'Info','evidence-packages','update','{""source"":""api""}'),
          (31,1,'admin','fuel.transaction_created','FuelTransaction',1,'Info','fuel-idling','create','{""source"":""api""}'),
          (32,1,'admin','fuel.anomaly_detected','FuelTransaction',2,'Warning','fuel-idling','flag','{""source"":""api""}'),
          (33,1,'admin','expense.created','Expense',1,'Info','expenses','create','{""source"":""api""}'),
          (34,1,'admin','expense.approved','Expense',2,'Info','expenses','update','{""source"":""api""}'),
          (35,1,'admin','expense.rejected','Expense',3,'Warning','expenses','update','{""source"":""api""}'),
          (36,1,'admin','contract.created','Contract',1,'Info','contracts-rates','create','{""source"":""api""}'),
          (37,1,'admin','contract.expiring','Contract',2,'Warning','contracts-rates','flag','{""source"":""api""}'),
          (38,1,'admin','carrier.created','Carrier',1,'Info','carrier-management','create','{""source"":""api""}'),
          (39,1,'admin','carrier.compliance_risk','Carrier',2,'Warning','carrier-management','flag','{""source"":""api""}'),
          (40,1,'admin','cost.leakage_detected','CostLeakage',1,'Warning','cost-leakage','flag','{""source"":""api""}'),
          (41,1,'admin','cost.action_created','CostLeakage',2,'Info','cost-leakage','create','{""source"":""api""}'),
          (42,1,'admin','compliance.violation_detected','ComplianceViolation',1,'Critical','compliance','flag','{""source"":""api""}'),
          (43,1,'admin','compliance.violation_acknowledged','ComplianceViolation',2,'Info','compliance','update','{""source"":""api""}'),
          (44,1,'admin','compliance.violation_resolved','ComplianceViolation',3,'Info','compliance','update','{""source"":""api""}'),
          (45,1,'admin','compliance.audit_package_created','AuditPackage',1,'Info','compliance','create','{""source"":""api""}'),
          (46,1,'admin','hos.log_certified','HosLog',1,'Info','hos-eld','update','{""source"":""api""}'),
          (47,1,'admin','eld.malfunction','EldDevice',4,'Critical','hos-eld','flag','{""source"":""api""}'),
          (48,1,'admin','eld.malfunction.resolved','EldDevice',8,'Info','hos-eld','update','{""source"":""api""}'),
          (49,1,'admin','localization.settings_updated','LocaleSettings',1,'Info','settings','update','{""source"":""api""}'),
          (50,1,'admin','user.login','User',1,'Info','auth','login','{""source"":""api""}'),
          (51,1,'admin','vehicle.status.changed','Vehicle',3,'Info','vehicles','update','{""source"":""api""}'),
          (52,1,'admin','vehicle.deleted','Vehicle',4,'Warning','vehicles','delete','{""source"":""api""}'),
          (53,1,'admin','driver.updated','Driver',2,'Info','drivers','update','{""source"":""api""}'),
          (54,1,'admin','driver.status.changed','Driver',3,'Info','drivers','update','{""source"":""api""}'),
          (55,1,'admin','job.deleted','Job',5,'Warning','jobs','delete','{""source"":""api""}'),
          (56,1,'admin','route.deleted','Route',3,'Warning','route-planning','delete','{""source"":""api""}'),
          (57,1,'admin','dispatch.auto_suggest','Dispatch',1,'Info','dispatch','create','{""source"":""api""}'),
          (58,1,'admin','eta.sent','Job',6,'Info','customer-portal','create','{""source"":""api""}'),
          (59,1,'admin','proof.completed','Job',7,'Info','customer-portal','update','{""source"":""api""}'),
          (60,1,'admin','customer.created','Customer',1,'Info','customers','create','{""source"":""api""}'),
          (61,1,'admin','asset.created','Asset',1,'Info','assets','create','{""source"":""api""}'),
          (62,1,'admin','maintenance.deferred','Maintenance',4,'Warning','maintenance','update','{""source"":""api""}'),
          (63,1,'admin','workorder.status_changed','WorkOrder',3,'Info','work-orders','update','{""source"":""api""}'),
          (64,1,'admin','workorder.cost_approved','WorkOrder',4,'Info','work-orders','update','{""source"":""api""}'),
          (65,1,'admin','dvir.mechanic_reviewed','DvirReport',3,'Info','dvir-inspections','update','{""source"":""api""}'),
          (66,1,'admin','dvir.driver_signed','DvirReport',4,'Info','dvir-inspections','update','{""source"":""api""}'),
          (67,1,'admin','document.renewed','Document',4,'Info','documents','update','{""source"":""api""}'),
          (68,1,'admin','safety.coaching_created','SafetyEvent',3,'Info','safety','create','{""source"":""api""}'),
          (69,1,'admin','incident.insurance_report','Incident',3,'Info','incidents','create','{""source"":""api""}'),
          (70,1,'admin','fuel.transaction_updated','FuelTransaction',3,'Info','fuel-idling','update','{""source"":""api""}'),
          (71,1,'admin','fuel.anomaly_reviewed','FuelTransaction',4,'Info','fuel-idling','update','{""source"":""api""}'),
          (72,1,'admin','contract.activated','Contract',3,'Info','contracts-rates','update','{""source"":""api""}'),
          (73,1,'admin','contract.expired','Contract',4,'Warning','contracts-rates','flag','{""source"":""api""}'),
          (74,1,'admin','carrier.status_changed','Carrier',3,'Info','carrier-management','update','{""source"":""api""}'),
          (75,1,'admin','cost.margin.recalculated','CostMargin',1,'Info','predictive-margin','update','{""source"":""api""}'),
          (76,1,'admin','hos.warning','HosClock',2,'Warning','hos-eld','flag','{""source"":""api""}'),
          (77,1,'admin','hos.violation','HosClock',4,'Critical','hos-eld','flag','{""source"":""api""}'),
          (78,1,'admin','cross_border.risk_detected','ComplianceViolation',5,'Warning','compliance','flag','{""source"":""api""}'),
          (79,1,'admin','report.run_created','ReportRun',1,'Info','reports','create','{""source"":""api""}'),
          (80,1,'admin','report.export_requested','ReportExport',1,'Info','reports','create','{""source"":""api""}'),
          (81,1,'admin','scheduled_report.created','ScheduledReport',1,'Info','reports','create','{""source"":""api""}'),
          (82,1,'admin','kpi.recalculated','KpiMetric',1,'Info','sla-kpi','update','{""source"":""api""}'),
          (83,1,'admin','sla.breach_detected','SlaRecord',3,'Critical','sla-kpi','flag','{""source"":""api""}'),
          (84,1,'admin','sla.breach_resolved','SlaBreach',1,'Info','sla-kpi','update','{""source"":""api""}'),
          (85,1,'admin','audit.export_requested','AuditExport',1,'Info','audit-logs','create','{""source"":""api""}'),
          (86,1,'admin','executive.snapshot_created','ExecutiveSnapshot',1,'Info','executive','create','{""source"":""api""}'),
          (87,1,'admin','user.login','User',2,'Info','auth','login','{""source"":""api""}'),
          (88,1,'admin','vehicle.updated','Vehicle',5,'Info','vehicles','update','{""source"":""api""}'),
          (89,1,'admin','driver.updated','Driver',4,'Info','drivers','update','{""source"":""api""}'),
          (90,1,'admin','job.created','Job',21,'Info','jobs','create','{""source"":""api""}'),
          (91,1,'admin','job.assigned','Job',22,'Info','jobs','update','{""source"":""api""}'),
          (92,1,'admin','safety.event.created','SafetyEvent',3,'Warning','safety','create','{""source"":""api""}'),
          (93,1,'admin','dashcam.event.created','DashcamEvent',3,'Warning','dashcam','create','{""source"":""api""}'),
          (94,1,'admin','fuel.transaction_created','FuelTransaction',5,'Info','fuel-idling','create','{""source"":""api""}'),
          (95,1,'admin','expense.created','Expense',4,'Info','expenses','create','{""source"":""api""}'),
          (96,1,'admin','compliance.violation_detected','ComplianceViolation',4,'High','compliance','flag','{""source"":""api""}'),
          (97,1,'admin','dvir.critical_defect','DvirReport',5,'Critical','dvir-inspections','flag','{""source"":""api""}'),
          (98,1,'admin','workorder.created','WorkOrder',5,'Info','work-orders','create','{""source"":""api""}'),
          (99,1,'admin','report.run_created','ReportRun',2,'Info','reports','create','{""source"":""api""}'),
          (100,1,'admin','executive.snapshot_created','ExecutiveSnapshot',10,'Info','executive','create','{""source"":""api""}')
          ON CONFLICT DO NOTHING",

        // ── AUDIT EXPORT REQUESTS (8 records) ──────────────────────────────────────
        @"INSERT INTO audit_export_requests (id,tenant_id,requested_by_name,date_from,date_to,status,created_at,completed_at) OVERRIDING SYSTEM VALUE VALUES
          (1,1,'admin','2026-05-01','2026-05-15','Completed','2026-05-16 09:00:00','2026-05-16 09:00:45'),
          (2,1,'admin','2026-04-01','2026-04-30','Completed','2026-05-01 08:00:00','2026-05-01 08:01:12'),
          (3,1,'admin','2026-05-01','2026-05-24','Completed','2026-05-24 08:00:00','2026-05-24 08:01:33'),
          (4,1,'admin','2026-05-20','2026-05-24','Pending','2026-05-24 08:30:00',NULL),
          (5,1,'admin','2026-03-01','2026-03-31','Completed','2026-04-01 07:30:00','2026-04-01 07:31:08'),
          (6,1,'admin','2026-02-01','2026-02-28','Completed','2026-03-01 08:00:00','2026-03-01 08:00:52'),
          (7,1,'admin','2026-01-01','2026-01-31','Completed','2026-02-01 09:00:00','2026-02-01 09:01:18'),
          (8,1,'admin','2026-05-24','2026-05-24','Pending','2026-05-24 09:00:00',NULL)
          ON CONFLICT DO NOTHING",

        // ── AI RECOMMENDATIONS for Batch 7 modules ─────────────────────────────────
        @"INSERT INTO ai_recommendations (module_key,title,description,priority,score,action_label,action_type) VALUES
          ('reports-analytics','Run SLA At-Risk Report — 6 Customers Flagged','6 customers have SLA risk scores above 50. Run the SLA At-Risk Report to identify breach root causes.','High',91,'Run Report','report_action'),
          ('reports-analytics','Cost Leakage Report — $14,280 Recoverable','Cost leakage analysis shows $14,280 in recoverable savings this period. Export the Cost Leakage Report for Finance review.','High',88,'Run Report','report_action'),
          ('reports-analytics','Schedule Daily HOS Warning Report','Drivers 2 and 4 have recurring HOS issues. Enable the Daily HOS Warning scheduled report to catch violations earlier.','Medium',76,'Schedule Report','schedule_action'),
          ('reports-analytics','Executive Summary Report — Board Ready','Monthly executive summary is due. Run and export the Executive Operations Summary for board distribution.','Medium',72,'Run Report','report_action'),
          ('sla-kpi','On-Time Delivery Drifting — 7.6% Below Target','OTD has dropped from 94% to 87.4% over 5 days. Investigate dispatch scheduling and vehicle availability.','Critical',96,'Review KPI','kpi_action'),
          ('sla-kpi','Idle Cost Breaching Weekly Target — Review Top 3 Vehicles','Idle cost is $347.50/week over target. TRK-101, VAN-106, and BOX-107 account for 68% of idle time.','High',89,'Review Idle Cost','kpi_action'),
          ('sla-kpi','4 SLA Breaches Open — Customer Communications Required','SLA records SLA-003, SLA-007, SLA-010, and SLA-021 are in breach status. Customer notifications are overdue.','High',87,'View SLA Breaches','sla_action'),
          ('sla-kpi','Gross Margin Below Target — 4 Routes Operating Below 10%','Gross margin at 17.4% vs 22% target. Identify low-margin routes and renegotiate customer rates.','High',84,'Margin Analysis','kpi_action'),
          ('audit-logs','97 Audit Events This Period — Coverage Score 91%','Audit coverage is near target. 23 recent actions pending enrichment. Review critical and warning severity events.','Medium',74,'View Audit Logs','audit_action'),
          ('audit-logs','2 Critical Audit Events Require Review','Audit events: ELD malfunction (eld.malfunction) and DVIR critical defect (dvir.critical_defect) are pending review.','High',85,'Review Critical Events','audit_action'),
          ('audit-logs','Compliance Audit Export Ready','AUD-2026-US-001 audit package is ready. Request an audit log export covering the same date range for the compliance review.','Medium',71,'Export Audit Logs','audit_action'),
          ('executive','Operations in WATCH Status — 3 KPIs Require Action','OTD, Idle Cost, and Gross Margin are below target. Recommend executive review and resource reallocation.','Critical',97,'View Executive Summary','executive_action'),
          ('executive','ELD Malfunction on TRK-104 — FMCSA Compliance Risk','Unresolved ELD malfunction creates FMCSA compliance exposure. Immediate resolution required.','Critical',95,'View Compliance','executive_action'),
          ('executive','Fleet Readiness Below 80% — 4 Vehicles Offline','Fleet readiness at 78.6% vs 90% target. 4 vehicles in maintenance. Dispatch coverage may be impacted.','High',86,'View Fleet Status','executive_action'),
          ('executive','Cost Savings Opportunity — $1,200/week Route Optimization','Route efficiency analysis identifies $1,200/week savings from 4-lane optimization.','Medium',78,'View Route Analysis','executive_action')
          ON CONFLICT DO NOTHING",
    ];
}
