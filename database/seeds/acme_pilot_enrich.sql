-- ACME TRANSPORT — operational-entity enrichment (trips, dispatch, routes,
-- geofences, work orders, alerts, notifications). Run AFTER acme_pilot_harness.sql.
-- Idempotent: each block guarded so re-runs are safe.

DO $enrich$
DECLARE cid bigint;
BEGIN
    SELECT id INTO cid FROM companies WHERE company_code='ACME-TRANSPORT';
    IF cid IS NULL THEN RAISE NOTICE 'ACME-TRANSPORT not found — run harness first.'; RETURN; END IF;

    -- ── Routes (50) ──
    IF (SELECT count(*) FROM routes WHERE company_id=cid)=0 THEN
        INSERT INTO routes (company_id, route_code, name, status)
        SELECT cid, 'ACME-RTE-'||LPAD(g::text,3,'0'),
               (ARRAY['Chicago→Dallas','Dallas→Houston','Atlanta→Memphis','Denver→KC','Phoenix→Denver'])[(g%5)+1]||' Lane '||g,
               (ARRAY['Active','Active','Planned'])[(g%3)+1]
        FROM generate_series(1,50) g;
    END IF;

    -- ── Dispatch assignments (600) — link assigned/in_transit jobs to veh+drv ──
    IF (SELECT count(*) FROM dispatch_assignments WHERE company_id=cid)=0 THEN
        INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, status, assignment_status, assigned_at)
        SELECT cid, j.id,
               (SELECT id FROM vehicles WHERE company_id=cid AND status='Active' ORDER BY id OFFSET (j.id%800) LIMIT 1),
               (SELECT id FROM drivers WHERE company_id=cid AND status IN ('Available','On Duty') ORDER BY id OFFSET (j.id%1000) LIMIT 1),
               tok.s, tok.s, NOW() - ((j.id%48)*INTERVAL '1 hour')
        FROM jobs j
        CROSS JOIN LATERAL (SELECT (ARRAY['assigned','accepted','en_route_pickup','loaded','in_transit','delivered'])[(j.id%6)+1] s) tok
        WHERE j.company_id=cid AND j.status IN ('assigned','in_transit','delivered')
        LIMIT 600;
    END IF;

    -- ── Trips (400) from active dispatch ──
    IF (SELECT count(*) FROM trips WHERE company_id=cid)=0 THEN
        INSERT INTO trips (company_id, job_id, status, started_at)
        SELECT cid, da.job_id,
               (CASE WHEN da.assignment_status='delivered' THEN 'completed'
                     WHEN da.assignment_status='in_transit' THEN 'active' ELSE 'active' END),
               NOW() - ((da.job_id%36)*INTERVAL '1 hour')
        FROM dispatch_assignments da WHERE da.company_id=cid LIMIT 400;
    END IF;

    -- ── Geofences (30) — depots/yards/customer sites ──
    IF (SELECT count(*) FROM geofences WHERE company_id=cid)=0 THEN
        INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status)
        SELECT cid,
               (ARRAY['Chicago DC','Dallas Hub','Atlanta Yard','Denver Depot','Phoenix Cross-dock','Houston Terminal'])[(g%6)+1]||' Zone '||g,
               'Circle', 32.0+(g%15), -119.0+(g%40), 500+(g%10)*100, 'Active'
        FROM generate_series(1,30) g;
    END IF;

    -- ── Work orders (120) for maintenance/OOS vehicles ──
    IF (SELECT count(*) FROM work_orders WHERE company_id=cid)=0 THEN
        INSERT INTO work_orders (company_id, work_order_code, title, status, priority, vehicle_id)
        SELECT cid, 'ACME-WO-'||LPAD(row_number() OVER (ORDER BY v.id)::text,4,'0'),
               (ARRAY['Brake repair','Engine diagnostic','Tire replacement','Coolant leak','Transmission service'])[(v.id%5)+1],
               (ARRAY['Open','In Progress','Waiting Parts','Completed'])[(v.id%4)+1],
               (ARRAY['Low','Medium','High'])[(v.id%3)+1], v.id
        FROM vehicles v WHERE v.company_id=cid AND v.status IN ('Maintenance','Out of Service') LIMIT 120;
    END IF;

    -- ── Operational alerts (ai_insights, 80) ──
    IF (SELECT count(*) FROM ai_insights WHERE company_id=cid)=0 THEN
        INSERT INTO ai_insights (company_id, insight_type, title, body, severity, status, category, entity_type, entity_id, created_at)
        SELECT cid, 'alert',
               (ARRAY['Vehicle offline > 2h','Maintenance overdue','SLA breach risk','Driver HOS warning','Route deviation detected'])[(g%5)+1],
               'Auto-detected operational alert requiring dispatcher review.',
               (ARRAY['Critical','High','Warning','Info'])[(g%4)+1],
               (ARRAY['Open','Open','Acknowledged','Closed'])[(g%4)+1],
               'operations', 'vehicle',
               (SELECT id FROM vehicles WHERE company_id=cid ORDER BY id OFFSET (g%1000) LIMIT 1),
               NOW() - ((g%72)*INTERVAL '1 hour')
        FROM generate_series(1,80) g;
    END IF;

    -- ── Notifications (40) ──
    IF (SELECT count(*) FROM notifications WHERE company_id=cid)=0 THEN
        INSERT INTO notifications (company_id, event_type, source_type, severity, title, body, message, status)
        SELECT cid,
               (ARRAY['maintenance_due','sla_risk','safety_event','document_expiry'])[(g%4)+1],
               (ARRAY['maintenance','dispatch','safety','compliance'])[(g%4)+1],
               (ARRAY['High','Medium','Low'])[(g%3)+1],
               (ARRAY['PM overdue','Shipment SLA at risk','New safety event','License expiring'])[(g%4)+1],
               'Pilot notification body for review.', 'Pilot notification body for review.',
               (ARRAY['unread','unread','read'])[(g%3)+1]
        FROM generate_series(1,40) g;
    END IF;

    -- Notification recipients — notifications are targeted per recipient (role/user),
    -- so without a recipient row they are invisible in /api/notifications (correct
    -- behavior). Target the Company Admin role so the pilot admin sees them.
    INSERT INTO notification_recipients (company_id, notification_id, role_target, status)
    SELECT n.company_id, n.id, 'Company Admin', 'unread'
    FROM notifications n
    WHERE n.company_id=cid AND NOT EXISTS (SELECT 1 FROM notification_recipients nr WHERE nr.notification_id=n.id);

    -- ── Branches (12) + depots/yards (20), fleet distribution, branch manager ──
    -- Requires Stage 25 (branches table + branch_id columns).
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name='branches')
       AND (SELECT count(*) FROM branches WHERE company_id=cid)=0 THEN
        INSERT INTO branches (company_id, branch_code, name, branch_type, region, city, status)
        SELECT cid, 'ACME-BR-'||LPAD(g::text,2,'0'),
               (ARRAY['Midwest','South','Northeast','West','Southeast','Mountain'])[(g%6)+1]||' Branch '||g,
               'branch', (ARRAY['Midwest','South','Northeast','West'])[(g%4)+1],
               (ARRAY['Chicago','Dallas','Atlanta','Denver','Phoenix','Houston'])[(g%6)+1], 'Active'
        FROM generate_series(1,12) g;
        INSERT INTO branches (company_id, branch_code, name, branch_type, city, status)
        SELECT cid, 'ACME-DP-'||LPAD(g::text,2,'0'), 'Depot '||g, (CASE WHEN g%3=0 THEN 'yard' ELSE 'depot' END),
               (ARRAY['Chicago','Dallas','Atlanta','Denver'])[(g%4)+1], 'Active'
        FROM generate_series(1,20) g;
        UPDATE vehicles v SET branch_id = (SELECT id FROM branches WHERE company_id=cid AND branch_type='branch' ORDER BY id OFFSET (v.id % 12) LIMIT 1) WHERE v.company_id=cid;
        UPDATE drivers d  SET branch_id = (SELECT id FROM branches WHERE company_id=cid AND branch_type='branch' ORDER BY id OFFSET (d.id % 12) LIMIT 1) WHERE d.company_id=cid;
        INSERT INTO users (company_id, branch_id, email, full_name, role_name, role_id, status, permissions_json, demo_password)
        VALUES (cid, (SELECT id FROM branches WHERE company_id=cid AND branch_type='branch' ORDER BY id LIMIT 1),
                'branchmgr@acme-transport.com', 'Acme Branch Manager', 'Fleet Manager', 3, 'Active',
                '["dashboard:view","vehicles:view","drivers:view","shipments:view","dispatch:view"]'::jsonb, NULL)
        ON CONFLICT (email) DO NOTHING;
    END IF;

    RAISE NOTICE 'ACME enrichment: 50 routes, 600 dispatch, 400 trips, 30 geofences, 120 WOs, 80 alerts, 40 notifications, 12 branches + 20 depots.';
END
$enrich$;
