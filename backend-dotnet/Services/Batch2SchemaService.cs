using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Batch2SchemaService(Database db, IConfiguration? configuration = null)
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
        new("jobs", "job_number", "VARCHAR(60) NULL"),
        new("jobs", "contract_id", "BIGINT NULL"),
        new("jobs", "pickup_latitude", "DECIMAL(10,7) NULL"),
        new("jobs", "pickup_longitude", "DECIMAL(10,7) NULL"),
        new("jobs", "dropoff_latitude", "DECIMAL(10,7) NULL"),
        new("jobs", "dropoff_longitude", "DECIMAL(10,7) NULL"),
        new("jobs", "sla_window_start", "TIMESTAMPTZ NULL"),
        new("jobs", "sla_window_end", "TIMESTAMPTZ NULL"),
        new("jobs", "required_vehicle_type", "VARCHAR(80) NULL"),
        new("jobs", "required_driver_certification", "VARCHAR(120) NULL"),
        new("jobs", "route_id", "BIGINT NULL"),
        new("jobs", "eta", "TIMESTAMPTZ NULL"),
        new("jobs", "sla_status", "VARCHAR(60) NOT NULL DEFAULT 'On Track'"),
        new("jobs", "proof_status", "VARCHAR(60) NOT NULL DEFAULT 'Pending'"),
        new("jobs", "customer_update_status", "VARCHAR(60) NOT NULL DEFAULT 'Not Sent'"),
        new("jobs", "tracking_code", "VARCHAR(80) NULL"),
        new("jobs", "risk_score", "DECIMAL(6,2) NOT NULL DEFAULT 20"),
        new("jobs", "revenue_estimate", "DECIMAL(12,2) NULL"),
        new("jobs", "cost_estimate", "DECIMAL(12,2) NULL"),
        new("jobs", "margin_estimate", "DECIMAL(12,2) NULL"),
        new("jobs", "notes", "TEXT NULL"),
        new("jobs", "updated_at", "TIMESTAMPTZ NULL"),
        new("jobs", "deleted_at", "TIMESTAMPTZ NULL"),
        new("routes", "route_name", "VARCHAR(180) NULL"),
        new("routes", "region", "VARCHAR(120) NULL"),
        new("routes", "route_type", "VARCHAR(80) NOT NULL DEFAULT 'Delivery'"),
        new("routes", "planned_start", "TIMESTAMPTZ NULL"),
        new("routes", "planned_end", "TIMESTAMPTZ NULL"),
        new("routes", "total_stops", "INT NOT NULL DEFAULT 0"),
        new("routes", "estimated_distance", "DECIMAL(10,2) NOT NULL DEFAULT 0"),
        new("routes", "estimated_duration_minutes", "INT NOT NULL DEFAULT 0"),
        new("routes", "efficiency_score", "DECIMAL(6,2) NOT NULL DEFAULT 85"),
        new("routes", "sla_risk", "VARCHAR(60) NOT NULL DEFAULT 'Low'"),
        new("routes", "cost_estimate", "DECIMAL(12,2) NULL"),
        new("routes", "optimization_mode", "VARCHAR(80) NOT NULL DEFAULT 'Balanced'"),
        new("routes", "notes", "TEXT NULL"),
        new("routes", "updated_at", "TIMESTAMPTZ NULL"),
        new("routes", "deleted_at", "TIMESTAMPTZ NULL"),
        new("route_stops", "company_id", "BIGINT NOT NULL DEFAULT 1"),
        new("route_stops", "customer_id", "BIGINT NULL"),
        new("route_stops", "stop_type", "VARCHAR(60) NOT NULL DEFAULT 'Delivery'"),
        new("route_stops", "latitude", "DECIMAL(10,7) NULL"),
        new("route_stops", "longitude", "DECIMAL(10,7) NULL"),
        new("route_stops", "time_window_start", "TIMESTAMPTZ NULL"),
        new("route_stops", "time_window_end", "TIMESTAMPTZ NULL"),
        new("route_stops", "proof_status", "VARCHAR(60) NOT NULL DEFAULT 'Pending'"),
        new("route_stops", "notes", "TEXT NULL"),
        new("route_stops", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("route_stops", "updated_at", "TIMESTAMPTZ NULL"),
        new("customer_eta_links", "public_status", "VARCHAR(80) NOT NULL DEFAULT 'Active'"),
        new("dispatch_assignments", "assigned_by_user_id", "BIGINT NULL"),
        new("dispatch_assignments", "assignment_status", "VARCHAR(60) NULL"),
        new("dispatch_assignments", "match_reasons_json", "JSONB NULL"),
        new("dispatch_assignments", "started_at", "TIMESTAMPTZ NULL"),
        new("dispatch_assignments", "arrived_at", "TIMESTAMPTZ NULL"),
        new("dispatch_assignments", "completed_at", "TIMESTAMPTZ NULL"),
        new("dispatch_assignments", "cancelled_at", "TIMESTAMPTZ NULL"),
        new("dispatch_assignments", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("dispatch_assignments", "updated_at", "TIMESTAMPTZ NULL"),
        new("customer_communications", "message_type", "VARCHAR(80) NOT NULL DEFAULT 'ETA Update'"),
        new("customer_communications", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("eta_updates", "customer_id", "BIGINT NULL"),
        new("eta_updates", "tracking_code", "VARCHAR(80) NULL"),
        new("eta_updates", "eta", "TIMESTAMPTZ NULL"),
        new("eta_updates", "confidence_level", "VARCHAR(40) NOT NULL DEFAULT 'Medium'"),
        new("eta_updates", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("proof_of_delivery", "proof_type", "VARCHAR(80) NOT NULL DEFAULT 'Placeholder'"),
        new("proof_of_delivery", "photo_url", "VARCHAR(400) NULL"),
        new("proof_of_delivery", "signature_url", "VARCHAR(400) NULL"),
        new("proof_of_delivery", "received_by", "VARCHAR(160) NULL"),
        new("proof_of_delivery", "notes", "TEXT NULL"),
        new("proof_of_delivery", "created_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        // ai_recommendations drift backfill: the CREATE TABLE defines these, but an ai_recommendations
        // table that predates them (e.g. production, built before these columns were added) is skipped
        // by CREATE IF NOT EXISTS and never gets them — so code that INSERTs/SELECTs them 42703's.
        // Backfilling via EnsureColumnAsync fixes existing tables everywhere (prod, CI, dev).
        new("ai_recommendations", "recommendation_type", "VARCHAR(80) NOT NULL DEFAULT 'general'"),
        new("ai_recommendations", "tenant_id", "BIGINT NULL"),
        new("ai_recommendations", "summary", "TEXT NULL"),
        new("ai_recommendations", "confidence_score", "DECIMAL(5,2) NULL"),
        new("ai_recommendations", "urgency_score", "DECIMAL(5,2) NULL"),
        new("ai_recommendations", "impact_json", "JSONB NULL"),
        new("ai_recommendations", "reason_json", "JSONB NULL"),
        new("ai_recommendations", "proposed_action_json", "JSONB NULL"),
        new("ai_recommendations", "risk_level", "VARCHAR(40) NULL"),
        new("ai_recommendations", "source_event_id", "VARCHAR(120) NULL")
    ];

    private static readonly string[] TableStatements =
    [
        @"CREATE TABLE IF NOT EXISTS job_status_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NOT NULL,
            from_status VARCHAR(60) NULL,
            to_status VARCHAR(60) NOT NULL,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS route_paths (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            route_id BIGINT NOT NULL,
            path_json JSONB NULL,
            distance_miles DECIMAL(10,2) NULL,
            duration_minutes INT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS route_recommendations (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            route_id BIGINT NULL,
            recommendation_type VARCHAR(80) NOT NULL DEFAULT 'Route',
            title VARCHAR(220) NOT NULL,
            body TEXT NOT NULL,
            score DECIMAL(6,2) NOT NULL DEFAULT 85,
            status VARCHAR(50) NOT NULL DEFAULT 'Recommended',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS customer_eta_links (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NOT NULL,
            customer_id BIGINT NULL,
            tracking_code VARCHAR(80) NOT NULL,
            public_status VARCHAR(80) NOT NULL DEFAULT 'Active',
            expires_at TIMESTAMPTZ NULL,
            last_viewed_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())",
        @"CREATE TABLE IF NOT EXISTS customer_feedback (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NOT NULL,
            tracking_code VARCHAR(80) NULL,
            rating INT NULL,
            sentiment VARCHAR(80) NULL,
            comments TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())"
    ];

    private static readonly string[] SeedStatements =
    [
        @"UPDATE jobs SET job_number=COALESCE(job_number, job_code), sla_window_start=COALESCE(sla_window_start, scheduled_start), sla_window_end=COALESCE(sla_window_end, sla_due_at, scheduled_end), eta=COALESCE(eta, scheduled_end), tracking_code=COALESCE(tracking_code, 'ETA-' || job_code), required_vehicle_type=COALESCE(required_vehicle_type, (ARRAY['Truck','Van','Box Truck','Reefer'])[(id % 4)+1]), required_driver_certification=COALESCE(required_driver_certification, (ARRAY['CDL','Medical Card','Hazmat','Cold Chain Handling'])[(id % 4)+1]), sla_status=CASE WHEN status IN ('Delayed','At Risk') THEN 'At Risk' ELSE sla_status END, proof_status=CASE WHEN status IN ('Completed','Delivered') THEN 'Captured' ELSE proof_status END, customer_update_status=CASE WHEN id % 3 = 0 THEN 'Sent' ELSE customer_update_status END, risk_score=CASE WHEN risk_score=20 THEN CASE WHEN status IN ('Delayed','At Risk') OR priority='Critical' THEN 72 ELSE 18 + (id % 35) END ELSE risk_score END, revenue_estimate=COALESCE(revenue_estimate, 450 + id*22), cost_estimate=COALESCE(cost_estimate, 240 + id*15), margin_estimate=COALESCE(margin_estimate, revenue_estimate - cost_estimate), notes=COALESCE(notes, 'Seeded Northern Virginia/DC job record.')",
        @"UPDATE routes SET route_name=COALESCE(route_name, name), region=COALESCE(region, (ARRAY['Manassas','Woodbridge','Alexandria','Dulles','Fairfax','Arlington','Washington DC'])[(id % 7)+1]), planned_start=COALESCE(planned_start, created_at), planned_end=COALESCE(planned_end, created_at + 6 * INTERVAL '1 hour'), total_stops=(SELECT COUNT(*) FROM route_stops rs WHERE rs.route_id=routes.id), estimated_distance=CASE WHEN estimated_distance=0 THEN 35 + id*8 ELSE estimated_distance END, estimated_duration_minutes=CASE WHEN estimated_duration_minutes=0 THEN 90 + id*18 ELSE estimated_duration_minutes END, efficiency_score=CASE WHEN efficiency_score=85 THEN 76 + (id % 20) ELSE efficiency_score END, sla_risk=CASE WHEN status IN ('At Risk','Delayed') THEN 'High' ELSE sla_risk END, cost_estimate=COALESCE(cost_estimate, 180 + id*65), notes=COALESCE(notes, 'Seeded route plan for NOVA/DC operations.')",
        @"UPDATE route_stops SET company_id=1, customer_id=COALESCE(customer_id, (SELECT ids[((id - 1) % COALESCE(array_length(ids,1),1)) + 1] FROM (SELECT array_agg(id ORDER BY id) ids FROM customers WHERE company_id=1) customer_ids)), stop_type=COALESCE(stop_type, (ARRAY['Pickup','Drop-off','Service'])[(id % 3)+1]), latitude=COALESCE(latitude, lat), longitude=COALESCE(longitude, lng), time_window_start=COALESCE(time_window_start, eta), time_window_end=COALESCE(time_window_end, eta + 45 * INTERVAL '1 minute'), proof_status=CASE WHEN status='Completed' THEN 'Captured' ELSE proof_status END, notes=COALESCE(notes, 'Seeded stop window and SLA placeholder.')",
        @"UPDATE dispatch_assignments SET assignment_status=COALESCE(assignment_status, status), match_reasons_json=COALESCE(match_reasons_json, jsonb_build_array('Same region','Available driver','Vehicle type match','HOS risk acceptable','Proximity placeholder')), created_at=COALESCE(created_at, assigned_at)",
        @"INSERT INTO jobs (company_id, customer_id, job_code, job_number, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, sla_window_start, sla_window_end, status, priority, assigned_vehicle_id, assigned_driver_id, eta, sla_status, proof_status, customer_update_status, tracking_code, required_vehicle_type, required_driver_certification, risk_score, revenue_estimate, cost_estimate, margin_estimate, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 50), customer_ids AS (SELECT array_agg(id ORDER BY id) AS ids FROM customers WHERE company_id=1)
          SELECT 1, (SELECT ids[((n - 1) % COALESCE(array_length(ids,1),1)) + 1] FROM customer_ids), 'JOB-B2-' || (2000+n), 'JOB-B2-' || (2000+n),
                 (ARRAY['Delivery','Service Call','Pickup','Transfer'])[(n % 4)+1],
                 (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC','Baltimore, MD'])[(n % 8)+1],
                 (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC','Baltimore, MD'])[((n+3) % 8)+1],
                 NOW() + n * INTERVAL '1 hour', NOW() + (n+3) * INTERVAL '1 hour',
                 NOW() + n * INTERVAL '1 hour', NOW() + (n+4) * INTERVAL '1 hour',
                 (ARRAY['Unassigned','Assigned','En Route','At Stop','Completed','Delayed','At Risk'])[(n % 7)+1],
                 (ARRAY['Low','Normal','High','Critical'])[(n % 4)+1],
                 CASE WHEN n % 7 = 1 THEN NULL ELSE ((n - 1) % 20)+1 END,
                 CASE WHEN n % 7 = 1 THEN NULL ELSE ((n - 1) % 20)+1 END,
                 NOW() + (n+3) * INTERVAL '1 hour',
                 CASE WHEN n % 6 IN (0,5) THEN 'At Risk' ELSE 'On Track' END,
                 CASE WHEN n % 5 = 0 THEN 'Pending' ELSE 'Captured' END,
                 CASE WHEN n % 3 = 0 THEN 'Sent' ELSE 'Not Sent' END,
                 'B2ETA-' || (2000+n),
                 (ARRAY['Truck','Van','Box Truck','Reefer'])[(n % 4)+1],
                 (ARRAY['CDL','Medical Card','Hazmat','Cold Chain Handling'])[(n % 4)+1],
                 CASE WHEN n % 6 IN (0,5) THEN 74 ELSE 20 + (n % 35) END,
                 550+n*28, 260+n*17, 290+n*11, 'Batch 2 seeded job across NOVA/DC.'
          FROM seq WHERE (SELECT COUNT(*) FROM jobs WHERE deleted_at IS NULL) < 50",
        @"INSERT INTO routes (company_id, route_code, name, route_name, status, assigned_vehicle_id, assigned_driver_id, region, route_type, planned_start, planned_end, total_stops, estimated_distance, estimated_duration_minutes, efficiency_score, sla_risk, cost_estimate, optimization_mode, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
          SELECT 1, 'RTE-B2-' || LPAD(n::TEXT,3,'0'), 'Batch 2 NOVA Route ' || n, 'Batch 2 NOVA Route ' || n,
                 (ARRAY['Planned','Active','Completed','Delayed','At Risk'])[(n % 5)+1], n, n,
                 (ARRAY['Manassas','Woodbridge','Alexandria','Dulles','Fairfax','Arlington','Washington DC','Baltimore'])[(n % 8)+1],
                 (ARRAY['Delivery','Service','Mixed'])[(n % 3)+1],
                 NOW() + n * INTERVAL '1 hour', NOW() + (n+6) * INTERVAL '1 hour',
                 3 + (n % 6), 42 + n*6, 110 + n*15, 78 + (n % 18),
                 CASE WHEN n % 5 IN (0,4) THEN 'High' ELSE 'Low' END,
                 240+n*58, (ARRAY['Balanced','Fastest','Cost Saver'])[(n % 3)+1], 'Batch 2 route plan.'
          FROM seq WHERE (SELECT COUNT(*) FROM routes WHERE deleted_at IS NULL) < 12",
        @"INSERT INTO route_stops (company_id, route_id, job_id, customer_id, stop_sequence, stop_type, address, lat, lng, latitude, longitude, eta, time_window_start, time_window_end, status, proof_status, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 40), customer_ids AS (SELECT array_agg(id ORDER BY id) AS ids FROM customers WHERE company_id=1)
          SELECT 1, ((n - 1) % 12)+1, ((n - 1) % 50)+1, (SELECT ids[((n - 1) % COALESCE(array_length(ids,1),1)) + 1] FROM customer_ids), ((n - 1) % 6)+1,
                 (ARRAY['Pickup','Drop-off','Service'])[(n % 3)+1],
                 (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC','Baltimore, MD'])[(n % 8)+1],
                 38.6 + (n * .006), -77.5 + (n * .007), 38.6 + (n * .006), -77.5 + (n * .007),
                 NOW() + n * INTERVAL '1 hour', NOW() + n * INTERVAL '1 hour', NOW() + (n+1) * INTERVAL '1 hour',
                 (ARRAY['Pending','Arrived','Completed','Delayed','Pending'])[(n % 5)+1],
                 CASE WHEN n % 4 = 0 THEN 'Captured' ELSE 'Pending' END,
                 'Batch 2 route stop.'
          FROM seq WHERE (SELECT COUNT(*) FROM route_stops) < 40",
        @"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, match_score, status, assignment_status, match_reasons_json)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
          SELECT 1, n, ((n - 1) % 20)+1, ((n - 1) % 20)+1, 82 + (n % 17),
                 (ARRAY['Assigned','Accepted','In Progress'])[(n % 3)+1],
                 (ARRAY['Assigned','Accepted','In Progress'])[(n % 3)+1],
                 jsonb_build_array('Same region','Available driver','Required vehicle type match','Safety score in range','Proximity placeholder')
          FROM seq WHERE (SELECT COUNT(*) FROM dispatch_assignments) < 20",
        @"INSERT INTO customer_communications (company_id, customer_id, job_id, channel, message_type, message, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
          SELECT 1, ((n-1)%10)+1, n, (ARRAY['Email','SMS','Portal'])[(n%3)+1], 'ETA Update',
                 'Batch 2 ETA/customer update for job ' || n,
                 CASE WHEN n % 5 = 0 THEN 'Pending' ELSE 'Sent' END
          FROM seq WHERE (SELECT COUNT(*) FROM customer_communications) < 15",
        @"INSERT INTO eta_updates (company_id, job_id, customer_id, tracking_code, eta, confidence_level, message, channel, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
          SELECT 1, n, ((n-1)%10)+1, 'B2ETA-' || (2000+n), NOW() + (n+2) * INTERVAL '1 hour',
                 (ARRAY['High','Medium','Low','At Risk'])[(n%4)+1],
                 'Batch 2 ETA update for job ' || n, 'Email/SMS',
                 CASE WHEN n % 5 = 0 THEN 'Queued' ELSE 'Sent' END
          FROM seq WHERE (SELECT COUNT(*) FROM eta_updates) < 15",
        @"INSERT INTO customer_eta_links (company_id, job_id, customer_id, tracking_code, public_status, expires_at)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
          SELECT 1, n, ((n-1)%10)+1,
                 COALESCE((SELECT tracking_code FROM jobs WHERE id=n), 'B2ETA-' || (2000+n)),
                 'Active', NOW() + 14 * INTERVAL '1 day'
          FROM seq WHERE (SELECT COUNT(*) FROM customer_eta_links) < 10",
        @"INSERT INTO proof_of_delivery (company_id, job_id, receiver_name, received_by, proof_type, status, notes)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
          SELECT 1, n,
                 (ARRAY['R. Morgan','C. Rivera','D. Chen','M. Ahmed','S. Brooks'])[(n%5)+1],
                 (ARRAY['R. Morgan','C. Rivera','D. Chen','M. Ahmed','S. Brooks'])[(n%5)+1],
                 'Placeholder',
                 CASE WHEN n % 4 = 0 THEN 'Pending' ELSE 'Captured' END,
                 'Batch 2 proof placeholder.'
          FROM seq WHERE (SELECT COUNT(*) FROM proof_of_delivery) < 10",
        @"INSERT INTO dispatch_recommendations (company_id, job_id, vehicle_id, driver_id, recommendation, score, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
          SELECT 1, n, ((n - 1) % 20)+1, ((n - 1) % 20)+1,
                 'Batch 2 AI dispatch match for job ' || n || ': same region, vehicle type fit, HOS acceptable, proximity placeholder.',
                 84 + (n % 15), 'Recommended'
          FROM seq WHERE (SELECT COUNT(*) FROM dispatch_recommendations) < 12",
        @"INSERT INTO route_recommendations (company_id, route_id, recommendation_type, title, body, score, status)
          WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
          SELECT 1, ((n-1)%12)+1, 'Route', 'Route optimization recommendation ' || n,
                 'Review stop sequence, SLA risk, delay hotspot and cost leakage before route release.',
                 82 + (n % 16), 'Recommended'
          FROM seq WHERE (SELECT COUNT(*) FROM route_recommendations) < 12",
        @"INSERT INTO ai_recommendations (company_id, tenant_id, recommendation_type, module_key, title, summary, body, confidence_score, urgency_score, impact_json, reason_json, proposed_action_json, risk_level, status, score)
          SELECT 1, 1, 'batch2_action', m.module_key, m.title, m.body, m.body, 95, 86, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 'Medium', 'Recommended', 95
          FROM (
            SELECT 'jobs' module_key, 'Batch 2 job risk action' title, 'Assign high-priority unassigned jobs, send ETA updates, review proof pending and margin risk.' body
            UNION ALL SELECT 'dispatch','Batch 2 dispatch action','Use AI match scores to assign ready drivers and vehicles, then watch SLA exceptions.'
            UNION ALL SELECT 'routes','Batch 2 route advisor','Optimize stop sequence and reduce route cost leakage before release.'
            UNION ALL SELECT 'customer-eta','Batch 2 ETA action','Send proactive ETA updates for delayed or SLA-at-risk customers.'
            UNION ALL SELECT 'customer-portal','Batch 2 ETA portal action','Review customer communication history and tracking confidence.'
          ) m WHERE NOT EXISTS (SELECT 1 FROM ai_recommendations r WHERE r.module_key=m.module_key AND r.title=m.title)"
    ];
}
