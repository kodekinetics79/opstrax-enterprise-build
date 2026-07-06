-- 005_enrich_ops_demo.sql
-- Enriches the OpsTrax Demo Logistics tenant with derived operational test data so
-- Trips, Proof of Delivery, Last Mile and Logistics Workspace run realistic
-- simulations. Everything is DERIVED from the tenant's existing live jobs — no
-- invented entities — and every block is idempotent (count-guarded) and scoped
-- to the demo tenant ONLY. Real tenants are never touched.
-- Run:  psql "$PG_CONNECTION" -f db/init/005_enrich_ops_demo.sql

DO $$
DECLARE
  demo_id BIGINT;
  trip_count INT;
  pod_count INT;
  order_count INT;
BEGIN
  SELECT id INTO demo_id FROM companies WHERE name = 'OpsTrax Demo Logistics' LIMIT 1;
  IF demo_id IS NULL THEN
    RAISE NOTICE 'Demo tenant not found — skipping enrichment.';
    RETURN;
  END IF;

  ------------------------------------------------------------------
  -- TRIPS: one per recent job that has a vehicle, mirroring the job's
  -- lifecycle stage. Deterministic figures derived from the job id.
  ------------------------------------------------------------------
  SELECT COUNT(*) INTO trip_count FROM trips WHERE company_id = demo_id;
  IF trip_count < 12 THEN
    INSERT INTO trips (company_id, job_id, vehicle_id, driver_id, status, trip_number, route_id,
                       origin, destination, planned_start_time, actual_start_time,
                       planned_end_time, actual_end_time, started_at, completed_at,
                       planned_distance_miles, actual_distance_miles,
                       planned_duration_minutes, actual_duration_minutes,
                       total_planned_stops, stops_completed, stops_on_time,
                       start_delay_minutes, speeding_events_count,
                       compliance_score, route_compliance_score)
    SELECT demo_id, j.id, j.assigned_vehicle_id, j.assigned_driver_id,
           CASE WHEN j.status IN ('Completed','Delivered') THEN 'Completed'
                WHEN j.status IN ('En Route','In Progress','At Stop') THEN 'In Progress'
                ELSE 'Planned' END,
           'TRIP-' || LPAD(j.id::TEXT, 5, '0'),
           j.route_id,
           j.pickup_address, j.dropoff_address,
           COALESCE(j.scheduled_start, NOW() - INTERVAL '1 day'),
           CASE WHEN j.status NOT IN ('Unassigned','Assigned') THEN COALESCE(j.scheduled_start, NOW() - INTERVAL '1 day') + ((j.id % 21)::TEXT || ' minutes')::INTERVAL END,
           COALESCE(j.scheduled_end, NOW() - INTERVAL '2 hours'),
           CASE WHEN j.status IN ('Completed','Delivered') THEN COALESCE(j.scheduled_end, NOW() - INTERVAL '2 hours') + ((j.id % 34)::TEXT || ' minutes')::INTERVAL END,
           CASE WHEN j.status NOT IN ('Unassigned','Assigned') THEN COALESCE(j.scheduled_start, NOW() - INTERVAL '1 day') END,
           CASE WHEN j.status IN ('Completed','Delivered') THEN COALESCE(j.scheduled_end, NOW() - INTERVAL '2 hours') END,
           40 + (j.id % 160), 40 + (j.id % 160) + (j.id % 9),
           90 + (j.id % 180)::INT, 90 + (j.id % 180)::INT + (j.id % 25)::INT,
           2 + (j.id % 4)::INT,
           CASE WHEN j.status IN ('Completed','Delivered') THEN 2 + (j.id % 4)::INT ELSE (j.id % 2)::INT END,
           CASE WHEN j.status IN ('Completed','Delivered') THEN 1 + (j.id % 4)::INT ELSE (j.id % 2)::INT END,
           (j.id % 21)::INT, (j.id % 3)::INT,
           82 + (j.id % 17), 80 + (j.id % 19)
    FROM jobs j
    WHERE j.company_id = demo_id AND j.deleted_at IS NULL
      AND j.assigned_vehicle_id IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM trips t WHERE t.job_id = j.id)
    ORDER BY j.id DESC
    LIMIT 14;
    RAISE NOTICE 'Trips enriched.';
  END IF;

  ------------------------------------------------------------------
  -- PROOF OF DELIVERY: evidence rows for delivered/completed jobs.
  ------------------------------------------------------------------
  SELECT COUNT(*) INTO pod_count FROM proof_of_delivery WHERE company_id = demo_id;
  IF pod_count < 10 THEN
    INSERT INTO proof_of_delivery (company_id, job_id, receiver_name, status, captured_at,
                                   proof_type, received_by, notes)
    SELECT demo_id, j.id,
           (ARRAY['Site desk','Warehouse supervisor','Store manager','Dock receiver'])[1 + (j.id % 4)::INT],
           CASE WHEN j.status IN ('Completed','Delivered') THEN 'Captured' ELSE 'Pending' END,
           CASE WHEN j.status IN ('Completed','Delivered') THEN COALESCE(j.updated_at, NOW()) - ((j.id % 7)::TEXT || ' hours')::INTERVAL END,
           (ARRAY['Signature','Photo','Signature + Photo'])[1 + (j.id % 3)::INT],
           (ARRAY['R. Haddad','M. Chen','S. Okafor','L. Petrov'])[1 + (j.id % 4)::INT],
           'Derived demo evidence for job ' || COALESCE(j.job_number, j.job_code)
    FROM jobs j
    WHERE j.company_id = demo_id AND j.deleted_at IS NULL
      AND j.status IN ('Completed','Delivered','In Progress','En Route')
      AND NOT EXISTS (SELECT 1 FROM proof_of_delivery p WHERE p.job_id = j.id)
    ORDER BY j.id DESC
    LIMIT 12;
    RAISE NOTICE 'Proof of delivery enriched.';
  END IF;

  ------------------------------------------------------------------
  -- LAST MILE: dispatch orders, delivery routes and stops derived
  -- from jobs + customers so the logistics workspace has live flow.
  ------------------------------------------------------------------
  SELECT COUNT(*) INTO order_count FROM fleet_tms_dispatch_orders WHERE company_id = demo_id;
  IF order_count < 8 THEN
    INSERT INTO fleet_tms_delivery_routes (company_id, route_code, hub, territory, driver_name,
                                           vehicle_number, status, planned_stops, completed_stops,
                                           distance_km, completion_percent, current_stop, next_stop,
                                           planned_for_date, departure_time_utc, eta_complete_utc, notes)
    SELECT demo_id, 'LM-R' || LPAD(gs::TEXT, 3, '0'),
           (ARRAY['North Hub','Central Hub','South Hub'])[1 + (gs % 3)],
           (ARRAY['City Core','Industrial Belt','Harbor District'])[1 + (gs % 3)],
           d.full_name, v.vehicle_code,
           CASE WHEN gs % 3 = 0 THEN 'Completed' ELSE 'Active' END,
           6 + (gs % 5), CASE WHEN gs % 3 = 0 THEN 6 + (gs % 5) ELSE (gs % 4) END,
           18 + gs * 3, CASE WHEN gs % 3 = 0 THEN 100 ELSE ((gs % 4) * 100.0 / (6 + (gs % 5)))::NUMERIC(5,1) END,
           'Stop ' || (1 + (gs % 4)), 'Stop ' || (2 + (gs % 4)),
           CURRENT_DATE, NOW() - INTERVAL '3 hours', NOW() + ((2 + gs % 4)::TEXT || ' hours')::INTERVAL,
           'Derived from live demo jobs'
    FROM generate_series(1, 4) gs
    JOIN LATERAL (SELECT full_name FROM drivers WHERE company_id = demo_id AND deleted_at IS NULL ORDER BY id OFFSET (gs % 4) LIMIT 1) d ON TRUE
    JOIN LATERAL (SELECT vehicle_code FROM vehicles WHERE company_id = demo_id AND deleted_at IS NULL ORDER BY id OFFSET (gs % 4) LIMIT 1) v ON TRUE
    WHERE NOT EXISTS (SELECT 1 FROM fleet_tms_delivery_routes r WHERE r.company_id = demo_id AND r.route_code = 'LM-R' || LPAD(gs::TEXT, 3, '0'));

    INSERT INTO fleet_tms_dispatch_orders (company_id, order_number, customer_name, customer_segment,
                                           sales_channel, city, area, status, priority, item_count,
                                           order_value, route_code, driver_name, vehicle_number,
                                           dispatch_notes, created_at_utc, promised_at_utc,
                                           dispatched_at_utc, delivered_at_utc, updated_at_utc)
    SELECT demo_id, 'LMO-' || LPAD(j.id::TEXT, 5, '0'),
           COALESCE(c.name, 'Account ' || j.customer_id), COALESCE(c.sla_tier, 'Standard'), 'Portal',
           (ARRAY['Riyadh','Jeddah','Dammam'])[1 + (j.id % 3)::INT],
           (ARRAY['City Core','Industrial Belt','Harbor District'])[1 + (j.id % 3)::INT],
           CASE WHEN j.status IN ('Completed','Delivered') THEN 'Delivered'
                WHEN j.status IN ('En Route','In Progress') THEN 'Dispatched'
                ELSE 'Pending' END,
           COALESCE(j.priority, 'Normal'),
           1 + (j.id % 6)::INT, 120 + (j.id % 900),
           'LM-R' || LPAD((1 + (j.id % 4))::TEXT, 3, '0'),
           COALESCE(d.full_name, 'Unassigned'), COALESCE(v.vehicle_code, 'Unassigned'),
           'Derived from job ' || COALESCE(j.job_number, j.job_code),
           COALESCE(j.created_at, NOW() - INTERVAL '1 day'),
           COALESCE(j.sla_due_at, NOW() + INTERVAL '4 hours'),
           CASE WHEN j.status NOT IN ('Unassigned','Assigned') THEN COALESCE(j.scheduled_start, NOW() - INTERVAL '5 hours') END,
           CASE WHEN j.status IN ('Completed','Delivered') THEN COALESCE(j.updated_at, NOW() - INTERVAL '1 hour') END,
           NOW()
    FROM jobs j
    LEFT JOIN customers c ON c.id = j.customer_id
    LEFT JOIN drivers d ON d.id = j.assigned_driver_id
    LEFT JOIN vehicles v ON v.id = j.assigned_vehicle_id
    WHERE j.company_id = demo_id AND j.deleted_at IS NULL
      AND NOT EXISTS (SELECT 1 FROM fleet_tms_dispatch_orders o WHERE o.company_id = demo_id AND o.order_number = 'LMO-' || LPAD(j.id::TEXT, 5, '0'))
    ORDER BY j.id DESC
    LIMIT 10;

    INSERT INTO fleet_tms_last_mile_stops (company_id, order_number, route_code, customer_name,
                                           address_line, city, region, postal_code, country,
                                           status, proof_status, recipient_name, attempt_count,
                                           rider_name, time_window, eta_utc, delivered_at_utc,
                                           exception_reason, created_at_utc, updated_at_utc)
    SELECT o.company_id, o.order_number, o.route_code, o.customer_name,
           COALESCE(NULLIF(j.dropoff_address, ''), o.area || ' delivery point'),
           o.city, o.area, '11' || LPAD((o.id % 900)::TEXT, 3, '0'), 'SA',
           CASE WHEN o.status = 'Delivered' THEN 'Delivered'
                WHEN o.status = 'Dispatched' THEN 'Out for Delivery'
                ELSE 'Pending' END,
           CASE WHEN o.status = 'Delivered' THEN 'Captured' ELSE 'Pending' END,
           (ARRAY['Reception','Site contact','Store manager','Security desk'])[1 + (o.id % 4)::INT],
           CASE WHEN o.status = 'Delivered' THEN 1 ELSE (o.id % 2)::INT END,
           o.driver_name,
           (ARRAY['09:00-12:00','12:00-15:00','15:00-18:00'])[1 + (o.id % 3)::INT],
           COALESCE(o.promised_at_utc, NOW() + INTERVAL '3 hours'),
           o.delivered_at_utc, '', o.created_at_utc, NOW()
    FROM fleet_tms_dispatch_orders o
    LEFT JOIN jobs j ON j.company_id = o.company_id AND ('LMO-' || LPAD(j.id::TEXT, 5, '0')) = o.order_number
    WHERE o.company_id = demo_id
      AND NOT EXISTS (SELECT 1 FROM fleet_tms_last_mile_stops s WHERE s.company_id = demo_id AND s.order_number = o.order_number);
    RAISE NOTICE 'Last-mile flow enriched.';
  END IF;
END $$;
