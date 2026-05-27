using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch4SchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var column in Columns) await EnsureColumnAsync(column.Table, column.Name, column.Definition, ct);
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
        new("safety_events", "event_number", "VARCHAR(80) NULL"),
        new("safety_events", "job_id", "BIGINT NULL"),
        new("safety_events", "route_id", "BIGINT NULL"),
        new("safety_events", "location_description", "VARCHAR(220) NULL"),
        new("safety_events", "latitude", "DECIMAL(10,7) NULL"),
        new("safety_events", "longitude", "DECIMAL(10,7) NULL"),
        new("safety_events", "speed", "DECIMAL(8,2) NULL"),
        new("safety_events", "posted_speed_limit", "DECIMAL(8,2) NULL"),
        new("safety_events", "occurred_at", "DATETIME NULL"),
        new("safety_events", "coaching_status", "VARCHAR(80) NOT NULL DEFAULT 'Not Created'"),
        new("safety_events", "incident_status", "VARCHAR(80) NOT NULL DEFAULT 'None'"),
        new("safety_events", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 35"),
        new("safety_events", "ai_summary", "TEXT NULL"),
        new("safety_events", "recommended_action", "VARCHAR(260) NULL"),
        new("safety_events", "created_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP"),
        new("safety_events", "updated_at", "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("safety_events", "deleted_at", "TIMESTAMP NULL"),

        new("dashcam_events", "event_number", "VARCHAR(80) NULL"),
        new("dashcam_events", "event_type", "VARCHAR(120) NULL"),
        new("dashcam_events", "driver_id", "BIGINT NULL"),
        new("dashcam_events", "vehicle_id", "BIGINT NULL"),
        new("dashcam_events", "job_id", "BIGINT NULL"),
        new("dashcam_events", "route_id", "BIGINT NULL"),
        new("dashcam_events", "location_description", "VARCHAR(220) NULL"),
        new("dashcam_events", "latitude", "DECIMAL(10,7) NULL"),
        new("dashcam_events", "longitude", "DECIMAL(10,7) NULL"),
        new("dashcam_events", "road_facing_clip_url", "VARCHAR(400) NULL"),
        new("dashcam_events", "driver_facing_clip_url", "VARCHAR(400) NULL"),
        new("dashcam_events", "thumbnail_url", "VARCHAR(400) NULL"),
        new("dashcam_events", "video_provider", "VARCHAR(120) NOT NULL DEFAULT 'OpsTrax Placeholder'"),
        new("dashcam_events", "ai_summary", "TEXT NULL"),
        new("dashcam_events", "ai_confidence", "DECIMAL(6,2) NOT NULL DEFAULT 84"),
        new("dashcam_events", "review_status", "VARCHAR(80) NOT NULL DEFAULT 'Pending Review'"),
        new("dashcam_events", "false_positive", "BOOLEAN NOT NULL DEFAULT FALSE"),
        new("dashcam_events", "evidence_status", "VARCHAR(80) NOT NULL DEFAULT 'Not Packaged'"),
        new("dashcam_events", "recommended_action", "VARCHAR(260) NULL"),
        new("dashcam_events", "occurred_at", "DATETIME NULL"),
        new("dashcam_events", "created_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP"),
        new("dashcam_events", "updated_at", "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("dashcam_events", "deleted_at", "TIMESTAMP NULL")
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS coaching_tasks (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, task_number VARCHAR(80) NOT NULL,
            driver_id BIGINT NOT NULL, safety_event_id BIGINT NULL, dashcam_event_id BIGINT NULL, assigned_to_user_id BIGINT NULL,
            coaching_type VARCHAR(120) NOT NULL, priority VARCHAR(50) NOT NULL DEFAULT 'Medium', status VARCHAR(80) NOT NULL DEFAULT 'Draft',
            title VARCHAR(220) NOT NULL, description TEXT NULL, ai_script TEXT NULL, driver_acknowledged BOOLEAN NOT NULL DEFAULT FALSE,
            acknowledged_at DATETIME NULL, completed_at DATETIME NULL, before_safety_score DECIMAL(6,2) NULL, after_safety_score DECIMAL(6,2) NULL,
            effectiveness_score DECIMAL(6,2) NULL, due_at DATETIME NULL, created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP, deleted_at TIMESTAMP NULL)",
        @"CREATE TABLE IF NOT EXISTS coaching_notes (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, coaching_task_id BIGINT NOT NULL,
            note_type VARCHAR(80) NOT NULL DEFAULT 'Manager Note', note_text TEXT NOT NULL, created_by_user_id BIGINT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS incidents (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, incident_number VARCHAR(80) NOT NULL,
            safety_event_id BIGINT NULL, dashcam_event_id BIGINT NULL, driver_id BIGINT NULL, vehicle_id BIGINT NULL, job_id BIGINT NULL, route_id BIGINT NULL,
            incident_type VARCHAR(120) NOT NULL, severity VARCHAR(50) NOT NULL, status VARCHAR(80) NOT NULL DEFAULT 'New',
            location_description VARCHAR(220) NULL, latitude DECIMAL(10,7) NULL, longitude DECIMAL(10,7) NULL, occurred_at DATETIME NULL,
            driver_statement TEXT NULL, witness_statement TEXT NULL, customer_statement TEXT NULL, ai_summary TEXT NULL, recommended_action VARCHAR(260) NULL,
            insurance_report_status VARCHAR(80) NOT NULL DEFAULT 'Not Created', created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP, deleted_at TIMESTAMP NULL)",
        @"CREATE TABLE IF NOT EXISTS incident_evidence (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, incident_id BIGINT NOT NULL,
            evidence_type VARCHAR(120) NOT NULL, evidence_title VARCHAR(220) NOT NULL, evidence_url VARCHAR(400) NULL, evidence_json JSON NULL,
            source_entity_type VARCHAR(100) NULL, source_entity_id BIGINT NULL, created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS evidence_packages (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, package_number VARCHAR(80) NOT NULL,
            incident_id BIGINT NULL, safety_event_id BIGINT NULL, dashcam_event_id BIGINT NULL, driver_id BIGINT NULL, vehicle_id BIGINT NULL, job_id BIGINT NULL,
            package_type VARCHAR(120) NOT NULL DEFAULT 'Insurance Evidence', status VARCHAR(80) NOT NULL DEFAULT 'Draft', locked BOOLEAN NOT NULL DEFAULT FALSE,
            export_url VARCHAR(400) NULL, summary TEXT NULL, created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP, deleted_at TIMESTAMP NULL)",
        @"CREATE TABLE IF NOT EXISTS evidence_package_items (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, evidence_package_id BIGINT NOT NULL,
            item_type VARCHAR(120) NOT NULL, item_title VARCHAR(220) NOT NULL, item_url VARCHAR(400) NULL, item_json JSON NULL,
            source_entity_type VARCHAR(100) NULL, source_entity_id BIGINT NULL, created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS insurance_reports (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, report_number VARCHAR(80) NOT NULL,
            incident_id BIGINT NOT NULL, evidence_package_id BIGINT NULL, status VARCHAR(80) NOT NULL DEFAULT 'Draft',
            report_summary TEXT NULL, export_url VARCHAR(400) NULL, created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS driver_safety_scorecards (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, driver_id BIGINT NOT NULL,
            safety_score DECIMAL(6,2) NOT NULL DEFAULT 90, harsh_braking_count INT NOT NULL DEFAULT 0, harsh_acceleration_count INT NOT NULL DEFAULT 0,
            speeding_count INT NOT NULL DEFAULT 0, dashcam_event_count INT NOT NULL DEFAULT 0, coaching_open_count INT NOT NULL DEFAULT 0,
            coaching_completed_count INT NOT NULL DEFAULT 0, incident_count INT NOT NULL DEFAULT 0, risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
            calculated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS vehicle_safety_scorecards (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, vehicle_id BIGINT NOT NULL,
            safety_score DECIMAL(6,2) NOT NULL DEFAULT 90, safety_event_count INT NOT NULL DEFAULT 0, dashcam_event_count INT NOT NULL DEFAULT 0,
            incident_count INT NOT NULL DEFAULT 0, route_deviation_count INT NOT NULL DEFAULT 0, risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
            calculated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS safety_trends (
            id BIGINT PRIMARY KEY AUTO_INCREMENT, company_id BIGINT NOT NULL DEFAULT 1, trend_date DATE NOT NULL,
            harsh_braking_count INT NOT NULL DEFAULT 0, harsh_acceleration_count INT NOT NULL DEFAULT 0, speeding_count INT NOT NULL DEFAULT 0,
            dashcam_event_count INT NOT NULL DEFAULT 0, incident_count INT NOT NULL DEFAULT 0, fleet_safety_score DECIMAL(6,2) NOT NULL DEFAULT 90)"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX ix_b4_safety_events ON safety_events(company_id, severity, review_status, occurred_at)",
        "CREATE INDEX ix_b4_dashcam_events ON dashcam_events(company_id, severity, review_status, occurred_at)",
        "CREATE INDEX ix_b4_coaching_tasks ON coaching_tasks(company_id, driver_id, status, priority)",
        "CREATE INDEX ix_b4_incidents ON incidents(company_id, status, severity, incident_number)",
        "CREATE INDEX ix_b4_evidence_packages ON evidence_packages(company_id, status, package_number)",
        "CREATE INDEX ix_b4_insurance_reports ON insurance_reports(company_id, report_number, status)"
    ];

    private static readonly string[] Seeds =
    [
        "UPDATE safety_events SET event_number=COALESCE(event_number, CONCAT('SAFE-', LPAD(id,5,'0'))), occurred_at=COALESCE(occurred_at,event_time), location_description=COALESCE(location_description, ELT((id%7)+1,'Manassas Yard','Woodbridge I-95','Alexandria Medical Zone','Dulles Toll Road','Fairfax Delivery Zone','Arlington Urban Core','Washington DC Service Zone')), coaching_status=IF(review_status='Coaching Assigned','Created',coaching_status), incident_status=IF(severity='Critical','Open',incident_status), risk_score=IF(risk_score=35, IF(severity='Critical',90, IF(severity='High',72, 22+(id%30))), risk_score), ai_summary=COALESCE(ai_summary,'OpsTrax AI detected a safety signal requiring review.'), recommended_action=COALESCE(recommended_action, IF(severity IN ('High','Critical'),'Review and create coaching task','Review event evidence'))",
        "UPDATE dashcam_events SET event_number=COALESCE(event_number, CONCAT('VID-', LPAD(id,5,'0'))), event_type=COALESCE(event_type,title), occurred_at=COALESCE(occurred_at,event_time), driver_id=COALESCE(driver_id, ((id-1)%20)+1), vehicle_id=COALESCE(vehicle_id, ((id-1)%20)+1), location_description=COALESCE(location_description,'Northern Virginia corridor'), thumbnail_url=COALESCE(thumbnail_url,'/placeholder/dashcam-thumb.jpg'), road_facing_clip_url=COALESCE(road_facing_clip_url,'/placeholder/road-facing.mp4'), driver_facing_clip_url=COALESCE(driver_facing_clip_url,'/placeholder/driver-facing.mp4'), ai_summary=COALESCE(ai_summary,'AI dashcam placeholder summary with driver behavior, road context and exoneration signals.'), review_status=COALESCE(review_status,'Pending Review'), recommended_action=COALESCE(recommended_action,'Review video and determine coaching/evidence path')",
        @"INSERT INTO safety_events (company_id, event_number, event_type, severity, driver_id, vehicle_id, job_id, route_id, location_description, latitude, longitude, speed, posted_speed_limit, occurred_at, review_status, coaching_status, incident_status, risk_score, ai_summary, recommended_action)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40)
          SELECT 1, CONCAT('SAFE-B4-',1000+n), ELT((n%7)+1,'Harsh Braking','Harsh Acceleration','Speeding','Route Deviation','Following Distance','Distracted Driving Placeholder','Near Miss'), ELT((n%4)+1,'Low','Medium','High','Critical'), ((n-1)%20)+1, ((n-1)%20)+1, ((n-1)%50)+1, ((n-1)%12)+1, ELT((n%7)+1,'Manassas Yard','Woodbridge I-95','Alexandria Medical Zone','Dulles Toll Road','Fairfax Delivery Zone','Arlington Urban Core','Washington DC Service Zone'), 38.7+n*.003, -77.5+n*.004, 35+n, 45, DATE_SUB(NOW(), INTERVAL n HOUR), ELT((n%4)+1,'New','In Review','Reviewed','Escalated'), IF(n%4=0,'Needed','Not Created'), IF(n%6=0,'Open','None'), IF(n%4=0,88,25+(n%45)), 'AI safety advisor detected preventable risk pattern.', IF(n%4=0,'Create coaching task and incident review','Review safety evidence')
          FROM seq WHERE (SELECT COUNT(*) FROM safety_events WHERE deleted_at IS NULL) < 40",
        @"INSERT INTO dashcam_events (company_id, event_number, safety_event_id, event_type, title, severity, driver_id, vehicle_id, job_id, route_id, location_description, thumbnail_url, road_facing_clip_url, driver_facing_clip_url, video_provider, ai_summary, ai_confidence, review_status, coaching_status, evidence_status, recommended_action, occurred_at)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<25)
          SELECT 1, CONCAT('VID-B4-',2000+n), ((n-1)%40)+1, ELT((n%6)+1,'Near Miss','Distracted Driving Placeholder','Tailgating Placeholder','Speeding Video','Collision/Near Miss','Driver Exoneration Review'), CONCAT('AI dashcam event ',n), ELT((n%4)+1,'Low','Medium','High','Critical'), ((n-1)%20)+1, ((n-1)%20)+1, ((n-1)%50)+1, ((n-1)%12)+1, ELT((n%7)+1,'Manassas Yard','Woodbridge I-95','Alexandria Medical Zone','Dulles Toll Road','Fairfax Delivery Zone','Arlington Urban Core','Washington DC Service Zone'), '/placeholder/dashcam-thumb.jpg','/placeholder/road-facing.mp4','/placeholder/driver-facing.mp4','OpsTrax Placeholder', 'AI incident summary: vehicle context, video metadata, speed and route evidence are ready for review.', 78+(n%20), ELT((n%4)+1,'Pending Review','Reviewed','False Positive Review','Escalated'), IF(n%3=0,'Created','Needed'), IF(n%4=0,'Packaged','Not Packaged'), IF(n%5=0,'Potential driver exoneration: below speed limit with external cut-in risk','Review video and build evidence package'), DATE_SUB(NOW(), INTERVAL n HOUR)
          FROM seq WHERE (SELECT COUNT(*) FROM dashcam_events WHERE deleted_at IS NULL) < 25",
        @"INSERT INTO coaching_tasks (company_id, task_number, driver_id, safety_event_id, dashcam_event_id, assigned_to_user_id, coaching_type, priority, status, title, description, ai_script, driver_acknowledged, acknowledged_at, completed_at, before_safety_score, after_safety_score, effectiveness_score, due_at)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<20)
          SELECT 1, CONCAT('COACH-B4-',3000+n), ((n-1)%20)+1, ((n-1)%40)+1, IF(n%2=0,((n-1)%25)+1,NULL), 1, ELT((n%5)+1,'Following Distance','Speed Management','Braking Pattern','Distracted Driving','Route Discipline'), ELT((n%4)+1,'Low','Medium','High','Critical'), ELT((n%7)+1,'Draft','Assigned','In Progress','Driver Acknowledged','Completed','Escalated','Cancelled'), CONCAT('Driver coaching task ',n), 'Seeded coaching workflow from safety/video event.', 'Review following distance and braking patterns. Focus on maintaining safe distance in high-traffic zones.', n%4=0, IF(n%4=0,NOW(),NULL), IF(n%5=0,NOW(),NULL), 78+n%10, 82+n%12, 65+n%25, DATE_ADD(NOW(), INTERVAL n DAY)
          FROM seq WHERE (SELECT COUNT(*) FROM coaching_tasks WHERE deleted_at IS NULL) < 20",
        @"INSERT INTO coaching_notes (company_id, coaching_task_id, note_type, note_text, created_by_user_id)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<30)
          SELECT 1, ((n-1)%20)+1, ELT((n%3)+1,'Manager Note','Driver Response','AI Script'), CONCAT('Batch 4 coaching note ',n), 1 FROM seq WHERE (SELECT COUNT(*) FROM coaching_notes) < 30",
        @"INSERT INTO incidents (company_id, incident_number, safety_event_id, dashcam_event_id, driver_id, vehicle_id, job_id, route_id, incident_type, severity, status, location_description, occurred_at, driver_statement, witness_statement, customer_statement, ai_summary, recommended_action, insurance_report_status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<15)
          SELECT 1, CONCAT('INC-B4-',4000+n), ((n-1)%40)+1, ((n-1)%25)+1, ((n-1)%20)+1, ((n-1)%20)+1, ((n-1)%50)+1, ((n-1)%12)+1, ELT((n%5)+1,'Collision','Near Miss','Cargo Damage','Roadside Event','Customer Property'), ELT((n%4)+1,'Low','Medium','High','Critical'), ELT((n%7)+1,'New','Under Review','Awaiting Driver Statement','Evidence Collected','Insurance Report Ready','Closed','Reopened'), ELT((n%7)+1,'Manassas Yard','Woodbridge I-95','Alexandria','Dulles','Fairfax','Arlington','Washington DC'), DATE_SUB(NOW(), INTERVAL n DAY), 'Driver statement placeholder pending.', 'Witness statement placeholder pending.', 'Customer statement placeholder pending.', 'AI incident report builder summarized location, involved units, evidence and next step.', 'Collect evidence package and legal review placeholder', IF(n%3=0,'Ready','Not Created')
          FROM seq WHERE (SELECT COUNT(*) FROM incidents WHERE deleted_at IS NULL) < 15",
        @"INSERT INTO incident_evidence (company_id, incident_id, evidence_type, evidence_title, evidence_url, evidence_json, source_entity_type, source_entity_id)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<30)
          SELECT 1, ((n-1)%15)+1, ELT((n%7)+1,'Video','GPS Trail','Speed Data','Driver Statement','DVIR Reference','Maintenance Context','Photo Placeholder'), CONCAT('Incident evidence ',n), '/placeholder/evidence.dat', JSON_OBJECT('source','OpsTrax seeded evidence','chainOfCustody','intact'), ELT((n%5)+1,'dashcam','safety','dvir','maintenance','document'), n FROM seq WHERE (SELECT COUNT(*) FROM incident_evidence) < 30",
        @"INSERT INTO evidence_packages (company_id, package_number, incident_id, safety_event_id, dashcam_event_id, driver_id, vehicle_id, job_id, package_type, status, locked, summary)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<10)
          SELECT 1, CONCAT('EVD-B4-',5000+n), ((n-1)%15)+1, ((n-1)%40)+1, ((n-1)%25)+1, ((n-1)%20)+1, ((n-1)%20)+1, ((n-1)%50)+1, 'Insurance Evidence', IF(n%3=0,'Export Ready','Draft'), n%4=0, 'Evidence package bundle: video, GPS, speed, route, DVIR, maintenance and statements.'
          FROM seq WHERE (SELECT COUNT(*) FROM evidence_packages WHERE deleted_at IS NULL) < 10",
        @"INSERT INTO evidence_package_items (company_id, evidence_package_id, item_type, item_title, item_url, item_json, source_entity_type, source_entity_id)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40)
          SELECT 1, ((n-1)%10)+1, ELT((n%7)+1,'Road Video','Driver Video','GPS Trail','Speed Snapshot','DVIR','Maintenance','Photo'), CONCAT('Evidence package item ',n), '/placeholder/package-item.dat', JSON_OBJECT('readyForExport', true), ELT((n%5)+1,'dashcam','location','dvir','maintenance','document'), n FROM seq WHERE (SELECT COUNT(*) FROM evidence_package_items) < 40",
        @"INSERT INTO insurance_reports (company_id, report_number, incident_id, evidence_package_id, status, report_summary, export_url)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<8)
          SELECT 1, CONCAT('INS-B4-',6000+n), ((n-1)%15)+1, ((n-1)%10)+1, IF(n%3=0,'Ready','Draft'), 'Insurance/legal report placeholder generated from incident and evidence package.', '/placeholder/insurance-report.pdf' FROM seq WHERE (SELECT COUNT(*) FROM insurance_reports) < 8",
        @"INSERT INTO driver_safety_scorecards (company_id, driver_id, safety_score, harsh_braking_count, harsh_acceleration_count, speeding_count, dashcam_event_count, coaching_open_count, coaching_completed_count, incident_count, risk_score)
          SELECT 1, d.id, GREATEST(55, 98-(d.id%12)*3), d.id%5, d.id%4, d.id%6, d.id%3, d.id%4, d.id%2, d.id%3, 20+(d.id%10)*6 FROM drivers d WHERE NOT EXISTS (SELECT 1 FROM driver_safety_scorecards s WHERE s.driver_id=d.id)",
        @"INSERT INTO vehicle_safety_scorecards (company_id, vehicle_id, safety_score, safety_event_count, dashcam_event_count, incident_count, route_deviation_count, risk_score)
          SELECT 1, v.id, GREATEST(58, 97-(v.id%10)*3), v.id%6, v.id%4, v.id%3, v.id%2, 18+(v.id%8)*7 FROM vehicles v WHERE NOT EXISTS (SELECT 1 FROM vehicle_safety_scorecards s WHERE s.vehicle_id=v.id)",
        @"INSERT INTO safety_trends (company_id, trend_date, harsh_braking_count, harsh_acceleration_count, speeding_count, dashcam_event_count, incident_count, fleet_safety_score)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<14)
          SELECT 1, DATE_SUB(CURDATE(), INTERVAL n DAY), n%7, n%5, n%8, n%4, n%3, 88-(n%6) FROM seq WHERE (SELECT COUNT(*) FROM safety_trends) < 14",
        @"INSERT INTO ai_recommendations (company_id,module_key,title,body,score,status)
          SELECT 1, x.module_key, x.title, x.body, x.score, 'Recommended' FROM (
            SELECT 'safety' module_key,'AI Safety Advisor' title,'High-risk drivers should receive coaching before next dense urban route.' body,95 score
            UNION ALL SELECT 'dashcam','Driver exoneration support','Video metadata suggests potential external cut-in risk; preserve evidence package.'
            ,93 UNION ALL SELECT 'coaching','AI coaching script generator','Use following-distance script and compare before/after safety score.',91
            UNION ALL SELECT 'incidents','AI incident report builder','Bundle video, GPS, speed, job, DVIR and maintenance context for legal review.',94
            UNION ALL SELECT 'evidence-packages','Chain-of-custody advisor','Lock export-ready packages before sharing with insurance or legal stakeholders.',92
          ) x WHERE NOT EXISTS (SELECT 1 FROM ai_recommendations ar WHERE ar.module_key=x.module_key AND ar.title=x.title)",
        @"INSERT INTO notifications (company_id,title,body,severity,module_key,status)
          SELECT 1,x.title,x.body,x.severity,x.module_key,'Unread' FROM (
            SELECT 'Safety event review' title,'Critical safety events are waiting for review.' body,'Critical' severity,'safety' module_key
            UNION ALL SELECT 'Dashcam evidence pending','Video incidents need evidence package decisions.','Warning','dashcam'
            UNION ALL SELECT 'Incident report ready','Insurance/legal report placeholders are ready for review.','Warning','incidents'
          ) x WHERE NOT EXISTS (SELECT 1 FROM notifications n WHERE n.title=x.title AND n.module_key=x.module_key)"
    ];
}
