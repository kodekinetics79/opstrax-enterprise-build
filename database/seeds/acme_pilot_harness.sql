-- ACME TRANSPORT — Enterprise pilot harness
-- 1,000 vehicles · 1,250 drivers · 1,800 assets · 300 customers · realistic mixed states.
-- Idempotent: keyed off company_code 'ACME-TRANSPORT'. Re-run safe (skips if present).
-- Owner-role script (bypasses RLS) — bulk-seeds then relies on Stage22 RLS enrollment.

DO $acme$
DECLARE
    cid bigint;
BEGIN
    SELECT id INTO cid FROM companies WHERE company_code = 'ACME-TRANSPORT';
    IF cid IS NOT NULL THEN
        RAISE NOTICE 'ACME-TRANSPORT already seeded (company_id=%). Skipping.', cid;
        RETURN;
    END IF;

    INSERT INTO companies (company_code, name, industry, timezone, status)
    VALUES ('ACME-TRANSPORT', 'Acme Transport', 'Transport & Logistics', 'America/Chicago', 'Active')
    RETURNING id INTO cid;
    RAISE NOTICE 'Created ACME-TRANSPORT company_id=%', cid;

    -- ── 300 customers ──
    INSERT INTO customers (company_id, customer_code, name, contact_name, email, phone, status, sla_tier)
    SELECT cid, 'ACME-CUS-' || LPAD(g::text,4,'0'),
           (ARRAY['Global','Prime','Summit','Delta','Vertex','Apex','Nova','Titan','Orion','Zenith'])[(g%10)+1] || ' ' ||
           (ARRAY['Foods','Retail','Pharma','Logistics','Manufacturing','Chemicals','Grocers','Materials'])[(g%8)+1],
           'Contact ' || g, 'buyer' || g || '@acme-cus.example', '+1 312 555 ' || LPAD((1000+g)::text,4,'0'),
           (ARRAY['Active','Active','Active','At Risk'])[(g%4)+1],
           (ARRAY['Platinum','Gold','Silver','Bronze'])[(g%4)+1]
    FROM generate_series(1,300) g;

    -- ── 1,000 vehicles — mixed types + realistic operational states ──
    INSERT INTO vehicles (company_id, vehicle_code, type, make, model, year, vin, plate_number,
                          status, odometer_miles, readiness_score, data_quality_score, risk_score,
                          device_status, camera_status, out_of_service)
    SELECT cid, 'ACME-VEH-' || LPAD(g::text,4,'0'),
           (ARRAY['Truck','Truck','Truck','Van','Reefer','Box Truck','Tanker'])[(g%7)+1],
           (ARRAY['Freightliner','Volvo','Kenworth','Peterbilt','International','Mack'])[(g%6)+1],
           (ARRAY['Cascadia','VNL','T680','579','LT','Anthem'])[(g%6)+1],
           2018 + (g%7),
           'ACMEVIN' || LPAD(g::text,10,'0'),
           'ACM-' || LPAD(g::text,4,'0'),
           -- ~86% active, ~6% maintenance, ~4% OOS, ~4% offline
           (CASE WHEN g%25=0 THEN 'Out of Service' WHEN g%17=0 THEN 'Maintenance'
                 WHEN g%23=0 THEN 'Idle' ELSE 'Active' END),
           50000 + (g*137 % 300000),
           60 + (g % 40),               -- readiness 60-99
           70 + (g % 30),
           (g % 90),                    -- risk 0-89
           (CASE WHEN g%21=0 THEN 'Offline' WHEN g%29=0 THEN 'Degraded' ELSE 'Online' END),
           (CASE WHEN g%31=0 THEN 'Offline' ELSE 'Online' END),
           (g%25=0)
    FROM generate_series(1,1000) g;

    -- ── 1,250 drivers — mixed status incl. suspended + expiring licenses ──
    INSERT INTO drivers (company_id, driver_code, full_name, phone, email, license_number, license_expiry,
                        status, safety_score, readiness_score, risk_score, compliance_score)
    SELECT cid, 'ACME-DRV-' || LPAD(g::text,4,'0'),
           (ARRAY['James','Maria','David','Sarah','Mohammed','Chen','Robert','Aisha','Carlos','Priya'])[(g%10)+1] || ' ' ||
           (ARRAY['Smith','Garcia','Johnson','Lee','Khan','Patel','Brown','Nguyen','Martinez','Davis'])[(g%10)+1],
           '+1 312 556 ' || LPAD((1000+g)::text,4,'0'),
           'driver' || g || '@acme-transport.example',
           'CDL-' || LPAD(g::text,7,'0'),
           -- ~8% expired/expiring licenses
           (CURRENT_DATE + ((g%13-2) * 30) * INTERVAL '1 day'),
           (CASE WHEN g%40=0 THEN 'Suspended' WHEN g%9=0 THEN 'Off Duty'
                 WHEN g%5=0 THEN 'On Duty' ELSE 'Available' END),
           55 + (g % 45),
           60 + (g % 40),
           (g % 85),
           70 + (g % 30)
    FROM generate_series(1,1250) g;

    -- ── 1,800 assets/trailers/equipment ──
    INSERT INTO assets (company_id, asset_code, asset_type, name, status)
    SELECT cid, 'ACME-AST-' || LPAD(g::text,4,'0'),
           (ARRAY['Dry Van Trailer','Reefer Trailer','Flatbed','Tanker Trailer','Container Chassis','Forklift'])[(g%6)+1],
           (ARRAY['Trailer','Trailer','Trailer','Equipment'])[(g%4)+1] || ' ' || g,
           (ARRAY['Active','Active','In Use','Maintenance','Idle'])[(g%5)+1]
    FROM generate_series(1,1800) g;

    -- ── ELD devices — one per ~active vehicle ──
    INSERT INTO eld_devices (company_id, device_serial, device_model, provider, vehicle_id,
                            firmware_version, status, last_heartbeat_at, last_sync_at, created_at)
    SELECT cid, 'ACME-ELD-' || LPAD(row_number() OVER (ORDER BY v.id)::text,4,'0'),
           (ARRAY['GO9','VG34','LBB-3'])[(v.id%3)+1],
           (ARRAY['Geotab','Samsara','Motive'])[(v.id%3)+1],
           v.id,
           'v' || (4+(v.id%3)) || '.' || (v.id%9) || '.' || ((v.id*7)%9),
           (CASE WHEN v.status='Out of Service' THEN 'Malfunction'
                 WHEN v.device_status='Offline' THEN 'Diagnostic' ELSE 'Active' END),
           NOW() - ((v.id % 120) * INTERVAL '1 minute'),
           NOW() - ((v.id % 120) * INTERVAL '1 minute'),
           NOW() - INTERVAL '200 days'
    FROM vehicles v WHERE v.company_id = cid;

    -- ── Assign ~90% of active vehicles to drivers ──
    UPDATE vehicles v
       SET assigned_driver_id = d.id
      FROM (SELECT id, row_number() OVER (ORDER BY id) rn FROM drivers WHERE company_id=cid) d
     WHERE v.company_id=cid
       AND d.rn = ((v.id % 1200) + 1)
       AND v.status IN ('Active','Idle');

    -- ── ~4,000 jobs across statuses (daily trip volume) ──
    INSERT INTO jobs (company_id, customer_id, job_code, job_type, priority, status,
                     pickup_address, dropoff_address, scheduled_start, scheduled_end, risk_score)
    SELECT cid,
           (SELECT id FROM customers WHERE company_id=cid ORDER BY id OFFSET (g%300) LIMIT 1),
           'ACME-JOB-' || LPAD(g::text,5,'0'), 'Delivery',
           (ARRAY['Low','Medium','High','Critical'])[(g%4)+1],
           (ARRAY['scheduled','assigned','in_transit','delivered','delivered','exception','cancelled'])[(g%7)+1],
           (ARRAY['Chicago DC','Dallas Hub','Atlanta Yard','Denver Depot','Phoenix Cross-dock'])[(g%5)+1],
           (ARRAY['Houston Terminal','Memphis DC','KC Warehouse','Nashville Hub','Omaha Depot'])[(g%5)+1],
           NOW() - ((g%72) * INTERVAL '1 hour'),
           NOW() - ((g%72) * INTERVAL '1 hour') + INTERVAL '6 hours',
           (g%80)
    FROM generate_series(1,4000) g;

    -- ── Maintenance items — due + overdue ──
    INSERT INTO maintenance_items (company_id, vehicle_id, title, category, service_type, status, priority, due_date, estimated_cost)
    SELECT cid, v.id,
           (ARRAY['Oil Change','Brake Inspection','Tire Rotation','DOT Inspection','Coolant Flush'])[(v.id%5)+1],
           'Preventive',
           (ARRAY['Oil Change','Brake Service','Tire Service','Inspection','Fluid Service'])[(v.id%5)+1],
           (CASE WHEN v.id%7=0 THEN 'Overdue' ELSE 'Open' END),
           (ARRAY['Low','Medium','High'])[(v.id%3)+1],
           CURRENT_DATE + ((v.id%20)-7) * INTERVAL '1 day',
           150 + (v.id%40)*10
    FROM vehicles v WHERE v.company_id=cid AND v.id % 3 = 0;   -- ~333 maintenance items

    -- ── Safety events ──
    INSERT INTO safety_events (company_id, event_number, event_type, severity, status, driver_id, vehicle_id, risk_score, occurred_at)
    SELECT cid, 'ACME-SE-' || LPAD(g::text,5,'0'),
           (ARRAY['Harsh Braking','Speeding','Route Deviation','Harsh Acceleration','Distracted Driving'])[(g%5)+1],
           (ARRAY['Low','Medium','High','Critical'])[(g%4)+1],
           (ARRAY['New','Under Review','Reviewed','Resolved'])[(g%4)+1],
           (SELECT id FROM drivers WHERE company_id=cid ORDER BY id OFFSET (g%1250) LIMIT 1),
           (SELECT id FROM vehicles WHERE company_id=cid ORDER BY id OFFSET (g%1000) LIMIT 1),
           (g%90),
           NOW() - ((g%168) * INTERVAL '1 hour')
    FROM generate_series(1,500) g;

    -- ── Telemetry alerts (device offline, geofence, etc.) ──
    INSERT INTO telemetry_alerts (company_id, alert_type, message, severity, status, vehicle_id)
    SELECT cid,
           (ARRAY['device_offline','geofence_exit','harsh_braking','speeding','stale_gps'])[(v.id%5)+1],
           'Auto-generated pilot alert for ' || v.vehicle_code,
           (ARRAY['Low','Medium','High'])[(v.id%3)+1],
           'open', v.id
    FROM vehicles v WHERE v.company_id=cid AND (v.device_status <> 'Online' OR v.id%40=0);

    -- ── Vehicle + driver expiring documents ──
    INSERT INTO documents (company_id, entity_type, entity_id, document_type, title, status, expires_at)
    SELECT cid, 'vehicle', v.id, 'Insurance', v.vehicle_code || ' insurance',
           (CASE WHEN v.id%15=0 THEN 'Expiring' ELSE 'Active' END),
           CURRENT_DATE + ((v.id%20)-5) * INTERVAL '1 day'
    FROM vehicles v WHERE v.company_id=cid AND v.id%10=0;

    -- ── Live vehicle positions so the Live Map renders at scale (active+online) ──
    INSERT INTO latest_vehicle_positions (company_id, vehicle_id, lat, lng, speed_mph, event_time, received_at)
    SELECT v.company_id, v.id,
           32.0 + (v.id % 1500) / 100.0,
           -119.0 + (v.id % 4000) / 100.0,
           CASE WHEN v.status='Active' THEN (v.id % 70) ELSE 0 END,
           NOW() - ((v.id % 20) * INTERVAL '1 minute'),
           NOW() - ((v.id % 20) * INTERVAL '1 minute')
    FROM vehicles v
    WHERE v.company_id=cid AND v.status IN ('Active','Idle') AND v.device_status='Online';

    RAISE NOTICE 'ACME-TRANSPORT seeded: 1000 veh, 1250 drv, 1800 assets, 300 cust, 4000 jobs, live positions.';
END
$acme$;
