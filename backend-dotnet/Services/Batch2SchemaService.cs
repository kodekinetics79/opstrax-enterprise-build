using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch2SchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in TableStatements)
        {
            await db.ExecuteAsync(sql, ct: ct);
        }

        foreach (var column in Columns)
        {
            await EnsureColumnAsync(column.Table, column.Name, column.Definition, ct);
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
        new("jobs", "job_number", "VARCHAR(60) NULL"),
        new("jobs", "contract_id", "BIGINT NULL"),
        new("jobs", "pickup_latitude", "DECIMAL(10,7) NULL"),
        new("jobs", "pickup_longitude", "DECIMAL(10,7) NULL"),
        new("jobs", "dropoff_latitude", "DECIMAL(10,7) NULL"),
        new("jobs", "dropoff_longitude", "DECIMAL(10,7) NULL"),
        new("jobs", "sla_window_start", "DATETIME NULL"),
        new("jobs", "sla_window_end", "DATETIME NULL"),
        new("jobs", "required_vehicle_type", "VARCHAR(80) NULL"),
        new("jobs", "required_driver_certification", "VARCHAR(120) NULL"),
        new("jobs", "route_id", "BIGINT NULL"),
        new("jobs", "eta", "DATETIME NULL"),
        new("jobs", "sla_status", "VARCHAR(60) NOT NULL DEFAULT 'On Track'"),
        new("jobs", "proof_status", "VARCHAR(60) NOT NULL DEFAULT 'Pending'"),
        new("jobs", "customer_update_status", "VARCHAR(60) NOT NULL DEFAULT 'Not Sent'"),
        new("jobs", "tracking_code", "VARCHAR(80) NULL"),
        new("jobs", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 20"),
        new("jobs", "revenue_estimate", "DECIMAL(12,2) NULL"),
        new("jobs", "cost_estimate", "DECIMAL(12,2) NULL"),
        new("jobs", "margin_estimate", "DECIMAL(12,2) NULL"),
        new("jobs", "notes", "TEXT NULL"),
        new("jobs", "updated_at", "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("jobs", "deleted_at", "TIMESTAMP NULL"),
        new("routes", "route_name", "VARCHAR(180) NULL"),
        new("routes", "region", "VARCHAR(120) NULL"),
        new("routes", "route_type", "VARCHAR(80) NOT NULL DEFAULT 'Delivery'"),
        new("routes", "planned_start", "DATETIME NULL"),
        new("routes", "planned_end", "DATETIME NULL"),
        new("routes", "total_stops", "INT NOT NULL DEFAULT 0"),
        new("routes", "estimated_distance", "DECIMAL(10,2) NOT NULL DEFAULT 0"),
        new("routes", "estimated_duration_minutes", "INT NOT NULL DEFAULT 0"),
        new("routes", "efficiency_score", "DECIMAL(6,2) NOT NULL DEFAULT 85"),
        new("routes", "sla_risk", "VARCHAR(60) NOT NULL DEFAULT 'Low'"),
        new("routes", "cost_estimate", "DECIMAL(12,2) NULL"),
        new("routes", "optimization_mode", "VARCHAR(80) NOT NULL DEFAULT 'Balanced'"),
        new("routes", "notes", "TEXT NULL"),
        new("routes", "updated_at", "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("routes", "deleted_at", "TIMESTAMP NULL"),
        new("route_stops", "company_id", "BIGINT NOT NULL DEFAULT 1"),
        new("route_stops", "customer_id", "BIGINT NULL"),
        new("route_stops", "stop_type", "VARCHAR(60) NOT NULL DEFAULT 'Delivery'"),
        new("route_stops", "latitude", "DECIMAL(10,7) NULL"),
        new("route_stops", "longitude", "DECIMAL(10,7) NULL"),
        new("route_stops", "time_window_start", "DATETIME NULL"),
        new("route_stops", "time_window_end", "DATETIME NULL"),
        new("route_stops", "proof_status", "VARCHAR(60) NOT NULL DEFAULT 'Pending'"),
        new("route_stops", "notes", "TEXT NULL"),
        new("route_stops", "created_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP"),
        new("route_stops", "updated_at", "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("customer_eta_links", "public_status", "VARCHAR(80) NOT NULL DEFAULT 'Active'"),
        new("dispatch_assignments", "assigned_by_user_id", "BIGINT NULL"),
        new("dispatch_assignments", "assignment_status", "VARCHAR(60) NULL"),
        new("dispatch_assignments", "match_reasons_json", "JSON NULL"),
        new("dispatch_assignments", "started_at", "DATETIME NULL"),
        new("dispatch_assignments", "arrived_at", "DATETIME NULL"),
        new("dispatch_assignments", "completed_at", "DATETIME NULL"),
        new("dispatch_assignments", "cancelled_at", "DATETIME NULL"),
        new("dispatch_assignments", "created_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP"),
        new("dispatch_assignments", "updated_at", "TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"),
        new("customer_communications", "message_type", "VARCHAR(80) NOT NULL DEFAULT 'ETA Update'"),
        new("customer_communications", "created_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP"),
        new("eta_updates", "customer_id", "BIGINT NULL"),
        new("eta_updates", "tracking_code", "VARCHAR(80) NULL"),
        new("eta_updates", "eta", "DATETIME NULL"),
        new("eta_updates", "confidence_level", "VARCHAR(40) NOT NULL DEFAULT 'Medium'"),
        new("eta_updates", "created_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP"),
        new("proof_of_delivery", "proof_type", "VARCHAR(80) NOT NULL DEFAULT 'Placeholder'"),
        new("proof_of_delivery", "photo_url", "VARCHAR(400) NULL"),
        new("proof_of_delivery", "signature_url", "VARCHAR(400) NULL"),
        new("proof_of_delivery", "received_by", "VARCHAR(160) NULL"),
        new("proof_of_delivery", "notes", "TEXT NULL"),
        new("proof_of_delivery", "created_at", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP")
    ];

    private static readonly string[] TableStatements =
    [
        @"CREATE TABLE IF NOT EXISTS job_status_events (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            job_id BIGINT NOT NULL,
            from_status VARCHAR(60) NULL,
            to_status VARCHAR(60) NOT NULL,
            notes TEXT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS route_paths (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            route_id BIGINT NOT NULL,
            path_json JSON NULL,
            distance_miles DECIMAL(10,2) NULL,
            duration_minutes INT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS route_recommendations (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            route_id BIGINT NULL,
            recommendation_type VARCHAR(80) NOT NULL DEFAULT 'Route',
            title VARCHAR(220) NOT NULL,
            body TEXT NOT NULL,
            score DECIMAL(6,2) NOT NULL DEFAULT 85,
            status VARCHAR(50) NOT NULL DEFAULT 'Recommended',
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS customer_eta_links (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            job_id BIGINT NOT NULL,
            customer_id BIGINT NULL,
            tracking_code VARCHAR(80) NOT NULL,
            public_status VARCHAR(80) NOT NULL DEFAULT 'Active',
            expires_at DATETIME NULL,
            last_viewed_at DATETIME NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
        @"CREATE TABLE IF NOT EXISTS customer_feedback (
            id BIGINT PRIMARY KEY AUTO_INCREMENT,
            company_id BIGINT NOT NULL,
            job_id BIGINT NOT NULL,
            tracking_code VARCHAR(80) NULL,
            rating INT NULL,
            sentiment VARCHAR(80) NULL,
            comments TEXT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)"
    ];

    private static readonly string[] SeedStatements =
    [
        "UPDATE jobs SET job_number=COALESCE(job_number, job_code), sla_window_start=COALESCE(sla_window_start, scheduled_start), sla_window_end=COALESCE(sla_window_end, sla_due_at, scheduled_end), eta=COALESCE(eta, scheduled_end), tracking_code=COALESCE(tracking_code, CONCAT('ETA-', job_code)), required_vehicle_type=COALESCE(required_vehicle_type, ELT((id % 4)+1,'Truck','Van','Box Truck','Reefer')), required_driver_certification=COALESCE(required_driver_certification, ELT((id % 4)+1,'CDL','Medical Card','Hazmat','Cold Chain Handling')), sla_status=IF(status IN ('Delayed','At Risk'), 'At Risk', sla_status), proof_status=IF(status IN ('Completed','Delivered'), 'Captured', proof_status), customer_update_status=IF(id % 3 = 0, 'Sent', customer_update_status), risk_score=IF(risk_score=20, IF(status IN ('Delayed','At Risk') OR priority='Critical', 72, 18 + (id % 35)), risk_score), revenue_estimate=COALESCE(revenue_estimate, 450 + id*22), cost_estimate=COALESCE(cost_estimate, 240 + id*15), margin_estimate=COALESCE(margin_estimate, revenue_estimate - cost_estimate), notes=COALESCE(notes, 'Seeded Northern Virginia/DC job record.')",
        "UPDATE routes SET route_name=COALESCE(route_name, name), region=COALESCE(region, ELT((id % 7)+1,'Manassas','Woodbridge','Alexandria','Dulles','Fairfax','Arlington','Washington DC')), planned_start=COALESCE(planned_start, created_at), planned_end=COALESCE(planned_end, DATE_ADD(created_at, INTERVAL 6 HOUR)), total_stops=(SELECT COUNT(*) FROM route_stops rs WHERE rs.route_id=routes.id), estimated_distance=IF(estimated_distance=0, 35 + id*8, estimated_distance), estimated_duration_minutes=IF(estimated_duration_minutes=0, 90 + id*18, estimated_duration_minutes), efficiency_score=IF(efficiency_score=85, 76 + (id % 20), efficiency_score), sla_risk=IF(status IN ('At Risk','Delayed'), 'High', sla_risk), cost_estimate=COALESCE(cost_estimate, 180 + id*65), notes=COALESCE(notes, 'Seeded route plan for NOVA/DC operations.')",
        "UPDATE route_stops SET company_id=1, customer_id=COALESCE(customer_id, ((id - 1) % 10) + 1), stop_type=COALESCE(stop_type, ELT((id % 3)+1,'Pickup','Drop-off','Service')), latitude=COALESCE(latitude, lat), longitude=COALESCE(longitude, lng), time_window_start=COALESCE(time_window_start, eta), time_window_end=COALESCE(time_window_end, DATE_ADD(eta, INTERVAL 45 MINUTE)), proof_status=IF(status='Completed','Captured', proof_status), notes=COALESCE(notes, 'Seeded stop window and SLA placeholder.')",
        "UPDATE dispatch_assignments SET assignment_status=COALESCE(assignment_status, status), match_reasons_json=COALESCE(match_reasons_json, JSON_ARRAY('Same region','Available driver','Vehicle type match','HOS risk acceptable','Proximity placeholder')), created_at=COALESCE(created_at, assigned_at)",
        @"INSERT INTO jobs (company_id, customer_id, job_code, job_number, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, sla_window_start, sla_window_end, status, priority, assigned_vehicle_id, assigned_driver_id, eta, sla_status, proof_status, customer_update_status, tracking_code, required_vehicle_type, required_driver_certification, risk_score, revenue_estimate, cost_estimate, margin_estimate, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 50)
          SELECT 1, ((n - 1) % 10) + 1, CONCAT('JOB-B2-', 2000+n), CONCAT('JOB-B2-', 2000+n),
                 ELT((n % 4)+1,'Delivery','Service Call','Pickup','Transfer'),
                 ELT((n % 8)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC','Baltimore, MD'),
                 ELT(((n+3) % 8)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC','Baltimore, MD'),
                 DATE_ADD(NOW(), INTERVAL n HOUR), DATE_ADD(NOW(), INTERVAL (n+3) HOUR), DATE_ADD(NOW(), INTERVAL n HOUR), DATE_ADD(NOW(), INTERVAL (n+4) HOUR),
                 ELT((n % 7)+1,'Unassigned','Assigned','En Route','At Stop','Completed','Delayed','At Risk'),
                 ELT((n % 4)+1,'Low','Normal','High','Critical'),
                 IF(n % 7 = 1, NULL, ((n - 1) % 20)+1), IF(n % 7 = 1, NULL, ((n - 1) % 20)+1),
                 DATE_ADD(NOW(), INTERVAL (n+3) HOUR), IF(n % 6 IN (0,5),'At Risk','On Track'), IF(n % 5 = 0,'Pending','Captured'), IF(n % 3 = 0,'Sent','Not Sent'),
                 CONCAT('B2ETA-', 2000+n), ELT((n % 4)+1,'Truck','Van','Box Truck','Reefer'), ELT((n % 4)+1,'CDL','Medical Card','Hazmat','Cold Chain Handling'),
                 IF(n % 6 IN (0,5), 74, 20 + (n % 35)), 550+n*28, 260+n*17, 290+n*11, 'Batch 2 seeded job across NOVA/DC.'
          FROM seq WHERE (SELECT COUNT(*) FROM jobs WHERE deleted_at IS NULL) < 50",
        @"INSERT INTO routes (company_id, route_code, name, route_name, status, assigned_vehicle_id, assigned_driver_id, region, route_type, planned_start, planned_end, total_stops, estimated_distance, estimated_duration_minutes, efficiency_score, sla_risk, cost_estimate, optimization_mode, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
          SELECT 1, CONCAT('RTE-B2-', LPAD(n,3,'0')), CONCAT('Batch 2 NOVA Route ', n), CONCAT('Batch 2 NOVA Route ', n),
                 ELT((n % 5)+1,'Planned','Active','Completed','Delayed','At Risk'), n, n,
                 ELT((n % 8)+1,'Manassas','Woodbridge','Alexandria','Dulles','Fairfax','Arlington','Washington DC','Baltimore'),
                 ELT((n % 3)+1,'Delivery','Service','Mixed'), DATE_ADD(NOW(), INTERVAL n HOUR), DATE_ADD(NOW(), INTERVAL (n+6) HOUR),
                 3 + (n % 6), 42 + n*6, 110 + n*15, 78 + (n % 18), IF(n % 5 IN (0,4),'High','Low'), 240+n*58, ELT((n % 3)+1,'Balanced','Fastest','Cost Saver'), 'Batch 2 route plan.'
          FROM seq WHERE (SELECT COUNT(*) FROM routes WHERE deleted_at IS NULL) < 12",
        @"INSERT INTO route_stops (company_id, route_id, job_id, customer_id, stop_sequence, stop_type, address, lat, lng, latitude, longitude, eta, time_window_start, time_window_end, status, proof_status, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 40)
          SELECT 1, ((n - 1) % 12)+1, ((n - 1) % 50)+1, ((n - 1) % 10)+1, ((n - 1) % 6)+1,
                 ELT((n % 3)+1,'Pickup','Drop-off','Service'),
                 ELT((n % 8)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC','Baltimore, MD'),
                 38.6 + (n * .006), -77.5 + (n * .007), 38.6 + (n * .006), -77.5 + (n * .007),
                 DATE_ADD(NOW(), INTERVAL n HOUR), DATE_ADD(NOW(), INTERVAL n HOUR), DATE_ADD(NOW(), INTERVAL (n+1) HOUR),
                 ELT((n % 5)+1,'Pending','Arrived','Completed','Delayed','Pending'), IF(n % 4 = 0,'Captured','Pending'), 'Batch 2 route stop.'
          FROM seq WHERE (SELECT COUNT(*) FROM route_stops) < 40",
        @"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, match_score, status, assignment_status, match_reasons_json)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
          SELECT 1, n, ((n - 1) % 20)+1, ((n - 1) % 20)+1, 82 + (n % 17), ELT((n % 3)+1,'Assigned','Accepted','In Progress'), ELT((n % 3)+1,'Assigned','Accepted','In Progress'),
                 JSON_ARRAY('Same region','Available driver','Required vehicle type match','Safety score in range','Proximity placeholder')
          FROM seq WHERE (SELECT COUNT(*) FROM dispatch_assignments) < 20",
        @"INSERT INTO customer_communications (company_id, customer_id, job_id, channel, message_type, message, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
          SELECT 1, ((n-1)%10)+1, n, ELT((n%3)+1,'Email','SMS','Portal'), 'ETA Update', CONCAT('Batch 2 ETA/customer update for job ', n), IF(n % 5 = 0,'Pending','Sent')
          FROM seq WHERE (SELECT COUNT(*) FROM customer_communications) < 15",
        @"INSERT INTO eta_updates (company_id, job_id, customer_id, tracking_code, eta, confidence_level, message, channel, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
          SELECT 1, n, ((n-1)%10)+1, CONCAT('B2ETA-', 2000+n), DATE_ADD(NOW(), INTERVAL (n+2) HOUR), ELT((n%4)+1,'High','Medium','Low','At Risk'), CONCAT('Batch 2 ETA update for job ', n), 'Email/SMS', IF(n % 5 = 0,'Queued','Sent')
          FROM seq WHERE (SELECT COUNT(*) FROM eta_updates) < 15",
        @"INSERT INTO customer_eta_links (company_id, job_id, customer_id, tracking_code, public_status, expires_at)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
          SELECT 1, n, ((n-1)%10)+1, COALESCE((SELECT tracking_code FROM jobs WHERE id=n), CONCAT('B2ETA-',2000+n)), 'Active', DATE_ADD(NOW(), INTERVAL 14 DAY)
          FROM seq WHERE (SELECT COUNT(*) FROM customer_eta_links) < 10",
        @"INSERT INTO proof_of_delivery (company_id, job_id, receiver_name, received_by, proof_type, status, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
          SELECT 1, n, ELT((n%5)+1,'R. Morgan','C. Rivera','D. Chen','M. Ahmed','S. Brooks'), ELT((n%5)+1,'R. Morgan','C. Rivera','D. Chen','M. Ahmed','S. Brooks'), 'Placeholder', IF(n % 4 = 0,'Pending','Captured'), 'Batch 2 proof placeholder.'
          FROM seq WHERE (SELECT COUNT(*) FROM proof_of_delivery) < 10",
        @"INSERT INTO dispatch_recommendations (company_id, job_id, vehicle_id, driver_id, recommendation, score, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
          SELECT 1, n, ((n - 1) % 20)+1, ((n - 1) % 20)+1, CONCAT('Batch 2 AI dispatch match for job ', n, ': same region, vehicle type fit, HOS acceptable, proximity placeholder.'), 84 + (n % 15), 'Recommended'
          FROM seq WHERE (SELECT COUNT(*) FROM dispatch_recommendations) < 12",
        @"INSERT INTO route_recommendations (company_id, route_id, recommendation_type, title, body, score, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
          SELECT 1, ((n-1)%12)+1, 'Route', CONCAT('Route optimization recommendation ', n), 'Review stop sequence, SLA risk, delay hotspot and cost leakage before route release.', 82 + (n % 16), 'Recommended'
          FROM seq WHERE (SELECT COUNT(*) FROM route_recommendations) < 12",
        @"INSERT INTO ai_recommendations (company_id, module_key, title, body, score, status)
          SELECT 1, m.module_key, m.title, m.body, 95, 'Recommended'
          FROM (
            SELECT 'jobs' module_key, 'Batch 2 job risk action' title, 'Assign high-priority unassigned jobs, send ETA updates, review proof pending and margin risk.' body
            UNION ALL SELECT 'dispatch','Batch 2 dispatch action','Use AI match scores to assign ready drivers and vehicles, then watch SLA exceptions.'
            UNION ALL SELECT 'routes','Batch 2 route advisor','Optimize stop sequence and reduce route cost leakage before release.'
            UNION ALL SELECT 'customer-eta','Batch 2 ETA action','Send proactive ETA updates for delayed or SLA-at-risk customers.'
            UNION ALL SELECT 'customer-portal','Batch 2 ETA portal action','Review customer communication history and tracking confidence.'
          ) m WHERE NOT EXISTS (SELECT 1 FROM ai_recommendations r WHERE r.module_key=m.module_key AND r.title=m.title)"
    ];
}
