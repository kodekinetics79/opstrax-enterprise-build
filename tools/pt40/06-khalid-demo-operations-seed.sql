-- ─────────────────────────────────────────────────────────────────────────────
-- KHALID-DEMO (company 8) — operational seed: customers, routes, stops, jobs, trips
--
-- WHY
--   The tenant had 101 vehicles and 100 drivers but ZERO routes, stops, trips and
--   jobs, so Dispatch, Routes, Trips and Jobs all rendered empty. This gives the
--   tenant a coherent day of operations to demo against and to run simulations on.
--
-- GEOGRAPHY — this is not decoration
--   companies.id=8 is country='CA', currency='CAD', timezone='America/Toronto'.
--   Every coordinate below is a real Greater Toronto Area location, and every route
--   is a plausible GTA lane (Mississauga depot -> Oakville/Burlington, Vaughan ->
--   Brampton, Scarborough -> Pickering, YYZ cargo, downtown, Hamilton linehaul).
--   Seeding Virginia or Riyadh coordinates into a Toronto tenant is exactly the kind
--   of incoherence a knowledgeable buyer spots in five seconds at street zoom.
--
-- VOCABULARY
--   Statuses are taken from the values already in use in company 1, so the UI's
--   status matching, filters and colour coding work unchanged:
--     routes       Planned | Active | At Risk | Delayed | Completed
--     stop_type    Delivery | Drop-off | Pickup | Service   (verified: no 'Return')
--     route_stops  Pending | Arrived | Completed | Delayed
--     jobs         Unassigned | Assigned | En Route | At Stop | In Progress
--                  | Completed | Delayed | At Risk
--     trips        Scheduled | Completed | Delayed
--     job sla      On Track | At Risk        (verified: no 'On Time'/'Breached')
--     job priority Critical | High | Low | Normal
--
-- REALISM
--   Times are relative to NOW(), so the data is always "today" whenever you demo.
--   Stops carry genuine time windows, distances are consistent with the coordinates,
--   statuses vary, and one route is deliberately At Risk and one Delayed -- a board
--   where everything is green reads as fake.
--
-- THE PT40 LANE
--   Vehicle 1024 (KHALID-PILOT-01, the PT40-Q asset) is assigned to the ACTIVE route
--   GTA-WEST-AM with a live job. When the physical device reports, its fix lands on a
--   vehicle that is genuinely mid-route rather than an orphan dot.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/06-khalid-demo-operations-seed.sql
--
-- Idempotent: every insert is guarded on its natural key, so re-running adds nothing.
-- Additive: touches only company_id=8. No other tenant is read or written.
-- ─────────────────────────────────────────────────────────────────────────────

SET lock_timeout = '5s';

-- ── Customers ──────────────────────────────────────────────────────────────
INSERT INTO customers (company_id, customer_code, name, contact_name, email, phone,
                       billing_address, shipping_address, status, sla_tier,
                       sla_health_score, delivery_experience_score, risk_score)
SELECT 8, v.code, v.name, v.contact, v.email, v.phone, v.addr, v.addr,
       'Active', v.tier, v.health, v.exp, v.risk
FROM (VALUES
  ('CUS-GTA-001','Maple Ridge Grocers','Alice Tremblay','ops@mapleridge.ca','+1 905-555-0142','2200 Dixie Rd, Mississauga, ON L4Y 3C4','Gold',97,96,6),
  ('CUS-GTA-002','Lakeshore Building Supply','Marc Bouchard','dispatch@lakeshorebuild.ca','+1 905-555-0177','1450 Speers Rd, Oakville, ON L6L 2X5','Standard',92,90,14),
  ('CUS-GTA-003','Northline Pharma Distribution','Priya Raman','logistics@northlinepharma.ca','+1 905-555-0193','8500 Torbram Rd, Brampton, ON L6T 5C6','Platinum',99,98,4),
  ('CUS-GTA-004','Scarborough Fresh Foods','Daniel Okonkwo','receiving@scarbfresh.ca','+1 416-555-0128','1900 Markham Rd, Scarborough, ON M1B 2W3','Standard',88,86,22),
  ('CUS-GTA-005','Vaughan Industrial Parts','Sofia Ricci','orders@vaughanparts.ca','+1 905-555-0164','3800 Highway 7, Vaughan, ON L4L 8N9','Gold',94,93,11),
  ('CUS-GTA-006','Harbourfront Retail Group','Nathan Leclerc','supply@harbourfrontretail.ca','+1 416-555-0155','200 Front St W, Toronto, ON M5V 3K2','Standard',90,89,17),
  ('CUS-GTA-007','Steeltown Manufacturing','Grace Whitfield','inbound@steeltownmfg.ca','+1 905-555-0186','500 Burlington St E, Hamilton, ON L8L 4J4','Standard',86,84,26),
  ('CUS-GTA-008','Pearson Air Cargo Services','Omar Haddad','cargo@pearsonaircargo.ca','+1 905-555-0119','6301 Silver Dart Dr, Mississauga, ON L5P 1B2','Platinum',98,97,5)
) AS v(code,name,contact,email,phone,addr,tier,health,exp,risk)
WHERE NOT EXISTS (SELECT 1 FROM customers c WHERE c.company_id=8 AND c.customer_code=v.code);

-- ── Routes ─────────────────────────────────────────────────────────────────
-- Vehicle/driver ids are resolved by code so this stays correct if ids differ.
INSERT INTO routes (company_id, route_code, name, route_name, status, route_type, region,
                    assigned_vehicle_id, assigned_driver_id, planned_start, planned_end,
                    total_stops, estimated_distance, estimated_duration_minutes,
                    efficiency_score, sla_risk, cost_estimate, optimization_mode, notes)
SELECT 8, v.code, v.name, v.name, v.status, v.rtype, v.region,
       (SELECT id FROM vehicles WHERE company_id=8 AND vehicle_code=v.veh AND deleted_at IS NULL),
       (SELECT id FROM drivers  WHERE company_id=8 AND driver_code =v.drv AND deleted_at IS NULL),
       NOW() + (v.start_offset || ' minutes')::interval,
       NOW() + (v.end_offset   || ' minutes')::interval,
       v.stops, v.dist, v.mins, v.eff, v.risk, v.cost, 'Balanced', v.notes
FROM (VALUES
  ('RTE-GTA-WEST-AM','GTA West Morning Delivery','Active','Delivery','GTA West','KHALID-PILOT-01','DRV-TST-0001',-95, 145,4,142.5,255,91,'Low',780.00,'Mississauga depot to Oakville and Burlington. PT40-Q pilot asset runs this lane.'),
  ('RTE-GTA-NORTH-AM','GTA North Morning Delivery','Active','Delivery','GTA North','TRK-TST-0001','DRV-TST-0002',-70, 170,4,118.0,215,88,'Low',655.00,'Vaughan and Brampton industrial corridor.'),
  ('RTE-GTA-EAST-PM','GTA East Afternoon Delivery','At Risk','Delivery','GTA East','TRK-TST-0002','DRV-TST-0003', 45, 395,4,131.0,240,74,'High',712.00,'Scarborough to Pickering. Running against tight windows on the last two stops.'),
  ('RTE-YYZ-CARGO','Pearson Cargo Shuttle','Delayed','Mixed','GTA West','TRK-TST-0003','DRV-TST-0004',-160, 60,3,64.5,150,69,'Medium',430.00,'YYZ cargo shuttle. Delayed on airside access this morning.'),
  ('RTE-DT-EXPRESS','Downtown Toronto Express','Planned','Delivery','Toronto Core','TRK-TST-0004','DRV-TST-0005',180, 480,3,48.0,175,86,'Medium',395.00,'Downtown core. Restricted delivery windows.'),
  ('RTE-HAM-LINEHAUL','Hamilton Linehaul','Completed','Delivery','Golden Horseshoe','TRK-TST-0005','DRV-TST-0006',-540,-120,3,171.0,225,94,'Low',845.00,'Overnight linehaul to Hamilton. Closed out on time.')
) AS v(code,name,status,rtype,region,veh,drv,start_offset,end_offset,stops,dist,mins,eff,risk,cost,notes)
WHERE NOT EXISTS (SELECT 1 FROM routes r WHERE r.company_id=8 AND r.route_code=v.code);

-- ── Route stops ────────────────────────────────────────────────────────────
-- Both lat/lng AND latitude/longitude are populated: the optimizer reads
-- Coordinate(stop,"latitude","lat"), while the map reads lat/lng. Filling only one
-- pair makes route optimization report "optimizationAvailable: false".
INSERT INTO route_stops (company_id, route_id, stop_sequence, address, lat, lng,
                         latitude, longitude, status, stop_type, customer_id,
                         time_window_start, time_window_end, eta, proof_status, notes)
SELECT 8,
       (SELECT id FROM routes WHERE company_id=8 AND route_code=v.rcode),
       v.seq, v.addr, v.lat, v.lng, v.lat, v.lng, v.status, v.stype,
       (SELECT id FROM customers WHERE company_id=8 AND customer_code=v.cust),
       NOW() + (v.win_start || ' minutes')::interval,
       NOW() + (v.win_end   || ' minutes')::interval,
       NOW() + (v.eta       || ' minutes')::interval,
       v.proof, v.notes
FROM (VALUES
  -- GTA West AM — in progress: two done, one arrived, one pending
  ('RTE-GTA-WEST-AM',1,'2200 Dixie Rd, Mississauga, ON',      43.620500,-79.624800,'Completed','Pickup',  'CUS-GTA-001', -95, -65, -88,'Captured','Depot load-out completed.'),
  ('RTE-GTA-WEST-AM',2,'1450 Speers Rd, Oakville, ON',        43.467500,-79.687700,'Completed','Delivery','CUS-GTA-002', -45, -15, -32,'Captured','POD signed by receiving.'),
  ('RTE-GTA-WEST-AM',3,'3350 Fairview St, Burlington, ON',    43.362800,-79.793200,'Arrived',  'Delivery','CUS-GTA-002',  -5,  40,   2,'Pending','On site, waiting on dock door.'),
  ('RTE-GTA-WEST-AM',4,'6301 Silver Dart Dr, Mississauga, ON',43.677700,-79.624800,'Pending',  'Delivery','CUS-GTA-008',  75, 145, 108,'Pending',NULL),
  -- GTA North AM — early in the run
  ('RTE-GTA-NORTH-AM',1,'2200 Dixie Rd, Mississauga, ON',     43.620500,-79.624800,'Completed','Pickup',  'CUS-GTA-001', -70, -40, -62,'Captured','Depot load-out completed.'),
  ('RTE-GTA-NORTH-AM',2,'3800 Highway 7, Vaughan, ON',        43.794200,-79.519900,'Arrived',  'Delivery','CUS-GTA-005', -10,  35,   5,'Pending','Arrived at Vaughan industrial park.'),
  ('RTE-GTA-NORTH-AM',3,'8500 Torbram Rd, Brampton, ON',      43.731500,-79.762400,'Pending',  'Delivery','CUS-GTA-003',  60, 120,  85,'Pending','Pharma delivery — temperature check required.'),
  ('RTE-GTA-NORTH-AM',4,'8300 Woodbine Ave, Markham, ON',     43.856100,-79.337000,'Pending',  'Delivery','CUS-GTA-005', 130, 170, 152,'Pending',NULL),
  -- GTA East PM — at risk, tight windows
  ('RTE-GTA-EAST-PM',1,'2200 Dixie Rd, Mississauga, ON',      43.620500,-79.624800,'Pending','Pickup',  'CUS-GTA-001',  45,  80,  55,'Pending','Afternoon load-out.'),
  ('RTE-GTA-EAST-PM',2,'1900 Markham Rd, Scarborough, ON',    43.776400,-79.231800,'Pending','Delivery','CUS-GTA-004', 150, 195, 175,'Pending','Receiving closes at window end — no grace.'),
  ('RTE-GTA-EAST-PM',3,'1355 Kingston Rd, Pickering, ON',     43.838400,-79.086800,'Pending','Delivery','CUS-GTA-004', 230, 265, 258,'Pending','Tight against close.'),
  ('RTE-GTA-EAST-PM',4,'8300 Woodbine Ave, Markham, ON',      43.856100,-79.337000,'Pending','Delivery','CUS-GTA-005', 300, 340, 345,'Pending','Projected arrival past window — SLA risk.'),
  -- YYZ cargo shuttle — delayed
  ('RTE-YYZ-CARGO',1,'2200 Dixie Rd, Mississauga, ON',        43.620500,-79.624800,'Completed','Pickup',  'CUS-GTA-001',-160,-130,-152,'Captured','Depot load-out completed.'),
  ('RTE-YYZ-CARGO',2,'6301 Silver Dart Dr, Mississauga, ON',  43.677700,-79.624800,'Delayed',  'Delivery','CUS-GTA-008', -90, -50,  15,'Pending','Airside access delay — 65 minutes late.'),
  ('RTE-YYZ-CARGO',3,'2200 Dixie Rd, Mississauga, ON',        43.620500,-79.624800,'Pending',  'Drop-off',  'CUS-GTA-001',  20,  60,  48,'Pending',NULL),
  -- Downtown express — planned
  ('RTE-DT-EXPRESS',1,'2200 Dixie Rd, Mississauga, ON',       43.620500,-79.624800,'Pending','Pickup',  'CUS-GTA-001', 180, 215, 190,'Pending','Afternoon load-out.'),
  ('RTE-DT-EXPRESS',2,'200 Front St W, Toronto, ON',          43.642600,-79.387100,'Pending','Delivery','CUS-GTA-006', 280, 320, 295,'Pending','Loading dock booking required.'),
  ('RTE-DT-EXPRESS',3,'220 Yonge St, Toronto, ON',            43.654200,-79.380600,'Pending','Delivery','CUS-GTA-006', 380, 430, 400,'Pending',NULL),
  -- Hamilton linehaul — completed overnight
  ('RTE-HAM-LINEHAUL',1,'2200 Dixie Rd, Mississauga, ON',     43.620500,-79.624800,'Completed','Pickup',  'CUS-GTA-001',-540,-500,-532,'Captured','Overnight load-out.'),
  ('RTE-HAM-LINEHAUL',2,'500 Burlington St E, Hamilton, ON',  43.260900,-79.821400,'Completed','Delivery','CUS-GTA-007',-400,-340,-368,'Captured','POD captured at gate.'),
  ('RTE-HAM-LINEHAUL',3,'2200 Dixie Rd, Mississauga, ON',     43.620500,-79.624800,'Completed','Drop-off',  'CUS-GTA-001',-200,-140,-165,'Captured','Returned to depot.')
) AS v(rcode,seq,addr,lat,lng,status,stype,cust,win_start,win_end,eta,proof,notes)
WHERE NOT EXISTS (
  SELECT 1 FROM route_stops s
   WHERE s.company_id=8 AND s.stop_sequence=v.seq
     AND s.route_id=(SELECT id FROM routes WHERE company_id=8 AND route_code=v.rcode));

-- ── Jobs ───────────────────────────────────────────────────────────────────
INSERT INTO jobs (company_id, job_code, job_number, job_type, customer_id, route_id,
                  assigned_vehicle_id, assigned_driver_id, status, priority,
                  pickup_address, dropoff_address,
                  pickup_latitude, pickup_longitude, dropoff_latitude, dropoff_longitude,
                  scheduled_start, scheduled_end, sla_due_at, eta,
                  sla_status, proof_status, tracking_code,
                  revenue_estimate, cost_estimate, risk_score, notes)
SELECT 8, v.code, v.code, v.jtype,
       (SELECT id FROM customers WHERE company_id=8 AND customer_code=v.cust),
       (SELECT id FROM routes    WHERE company_id=8 AND route_code=v.rcode),
       (SELECT id FROM vehicles  WHERE company_id=8 AND vehicle_code=v.veh AND deleted_at IS NULL),
       (SELECT id FROM drivers   WHERE company_id=8 AND driver_code =v.drv AND deleted_at IS NULL),
       v.status, v.priority, v.pickup, v.dropoff,
       v.plat, v.plng, v.dlat, v.dlng,
       NOW() + (v.sched_start || ' minutes')::interval,
       NOW() + (v.sched_end   || ' minutes')::interval,
       NOW() + (v.sla_due     || ' minutes')::interval,
       NOW() + (v.eta         || ' minutes')::interval,
       v.sla_status, v.proof, 'TRK-'||substr(md5(v.code),1,10),
       v.revenue, v.cost, v.risk, v.notes
FROM (VALUES
  ('JOB-GTA-1001','Delivery','CUS-GTA-002','RTE-GTA-WEST-AM','KHALID-PILOT-01','DRV-TST-0001','Completed','Normal','2200 Dixie Rd, Mississauga, ON','1450 Speers Rd, Oakville, ON',43.620500,-79.624800,43.467500,-79.687700,-95,-15,-15,-32,'On Track','Captured',420.00,268.00,8,'Delivered and signed.'),
  ('JOB-GTA-1002','Delivery','CUS-GTA-002','RTE-GTA-WEST-AM','KHALID-PILOT-01','DRV-TST-0001','At Stop','Normal','2200 Dixie Rd, Mississauga, ON','3350 Fairview St, Burlington, ON',43.620500,-79.624800,43.362800,-79.793200,-95,40,40,2,'On Track','Pending',515.00,331.00,12,'On site, waiting on dock door.'),
  ('JOB-GTA-1003','Delivery','CUS-GTA-008','RTE-GTA-WEST-AM','KHALID-PILOT-01','DRV-TST-0001','En Route','High','2200 Dixie Rd, Mississauga, ON','6301 Silver Dart Dr, Mississauga, ON',43.620500,-79.624800,43.677700,-79.624800,-95,145,145,108,'On Track','Pending',690.00,402.00,15,'Air cargo connection — hard cutoff.'),
  ('JOB-GTA-1004','Delivery','CUS-GTA-005','RTE-GTA-NORTH-AM','TRK-TST-0001','DRV-TST-0002','At Stop','Normal','2200 Dixie Rd, Mississauga, ON','3800 Highway 7, Vaughan, ON',43.620500,-79.624800,43.794200,-79.519900,-70,35,35,5,'On Track','Pending',465.00,289.00,10,NULL),
  ('JOB-GTA-1005','Delivery','CUS-GTA-003','RTE-GTA-NORTH-AM','TRK-TST-0001','DRV-TST-0002','Assigned','High','2200 Dixie Rd, Mississauga, ON','8500 Torbram Rd, Brampton, ON',43.620500,-79.624800,43.731500,-79.762400,-70,120,120,85,'On Track','Pending',780.00,441.00,9,'Temperature-controlled pharma load.'),
  ('JOB-GTA-1006','Delivery','CUS-GTA-005','RTE-GTA-NORTH-AM','TRK-TST-0001','DRV-TST-0002','Assigned','Normal','2200 Dixie Rd, Mississauga, ON','8300 Woodbine Ave, Markham, ON',43.620500,-79.624800,43.856100,-79.337000,-70,170,170,152,'On Track','Pending',505.00,318.00,13,NULL),
  ('JOB-GTA-1007','Delivery','CUS-GTA-004','RTE-GTA-EAST-PM','TRK-TST-0002','DRV-TST-0003','Assigned','High','2200 Dixie Rd, Mississauga, ON','1900 Markham Rd, Scarborough, ON',43.620500,-79.624800,43.776400,-79.231800,45,195,195,175,'At Risk','Pending',540.00,347.00,38,'Receiving closes at window end.'),
  ('JOB-GTA-1008','Delivery','CUS-GTA-004','RTE-GTA-EAST-PM','TRK-TST-0002','DRV-TST-0003','At Risk','High','2200 Dixie Rd, Mississauga, ON','1355 Kingston Rd, Pickering, ON',43.620500,-79.624800,43.838400,-79.086800,45,265,265,258,'At Risk','Pending',595.00,382.00,52,'Projected arrival inside the final minutes of the window.'),
  ('JOB-GTA-1009','Delivery','CUS-GTA-005','RTE-GTA-EAST-PM','TRK-TST-0002','DRV-TST-0003','At Risk','Normal','2200 Dixie Rd, Mississauga, ON','8300 Woodbine Ave, Markham, ON',43.620500,-79.624800,43.856100,-79.337000,45,340,340,345,'At Risk','Pending',480.00,311.00,64,'Projected past window — needs reschedule or split.'),
  ('JOB-GTA-1010','Transfer','CUS-GTA-008','RTE-YYZ-CARGO','TRK-TST-0003','DRV-TST-0004','Delayed','High','2200 Dixie Rd, Mississauga, ON','6301 Silver Dart Dr, Mississauga, ON',43.620500,-79.624800,43.677700,-79.624800,-160,-50,-50,15,'At Risk','Pending',725.00,458.00,58,'Airside access delay — 65 minutes late.'),
  ('JOB-GTA-1011','Delivery','CUS-GTA-006','RTE-DT-EXPRESS','TRK-TST-0004','DRV-TST-0005','Unassigned','Normal','2200 Dixie Rd, Mississauga, ON','200 Front St W, Toronto, ON',43.620500,-79.624800,43.642600,-79.387100,180,320,320,295,'On Track','Pending',430.00,276.00,18,'Requires dock booking.'),
  ('JOB-GTA-1012','Delivery','CUS-GTA-006','RTE-DT-EXPRESS','TRK-TST-0004','DRV-TST-0005','Unassigned','Normal','2200 Dixie Rd, Mississauga, ON','220 Yonge St, Toronto, ON',43.620500,-79.624800,43.654200,-79.380600,180,430,430,400,'On Track','Pending',455.00,291.00,20,NULL),
  ('JOB-GTA-1013','Delivery','CUS-GTA-007','RTE-HAM-LINEHAUL','TRK-TST-0005','DRV-TST-0006','Completed','Normal','2200 Dixie Rd, Mississauga, ON','500 Burlington St E, Hamilton, ON',43.620500,-79.624800,43.260900,-79.821400,-540,-340,-340,-368,'On Track','Captured',910.00,548.00,7,'Overnight linehaul closed out on time.'),
  ('JOB-GTA-1014','Pickup','CUS-GTA-001','RTE-HAM-LINEHAUL','TRK-TST-0005','DRV-TST-0006','Completed','Normal','500 Burlington St E, Hamilton, ON','2200 Dixie Rd, Mississauga, ON',43.260900,-79.821400,43.620500,-79.624800,-400,-140,-140,-165,'On Track','Captured',365.00,224.00,6,'Return leg to depot.')
) AS v(code,jtype,cust,rcode,veh,drv,status,priority,pickup,dropoff,plat,plng,dlat,dlng,sched_start,sched_end,sla_due,eta,sla_status,proof,revenue,cost,risk,notes)
WHERE NOT EXISTS (SELECT 1 FROM jobs j WHERE j.company_id=8 AND j.job_code=v.code);

-- ── Trips ──────────────────────────────────────────────────────────────────
INSERT INTO trips (company_id, trip_ref, trip_number, route_id, vehicle_id, driver_id, status,
                   origin, destination, planned_start_time, planned_end_time,
                   actual_start_time, actual_end_time, started_at, completed_at,
                   planned_distance_miles, planned_duration_minutes, total_planned_stops,
                   stops_completed, stops_on_time, compliance_score, route_compliance_score,
                   speeding_events_count)
SELECT 8, v.ref, v.ref,
       (SELECT id FROM routes   WHERE company_id=8 AND route_code=v.rcode),
       (SELECT id FROM vehicles WHERE company_id=8 AND vehicle_code=v.veh AND deleted_at IS NULL),
       (SELECT id FROM drivers  WHERE company_id=8 AND driver_code =v.drv AND deleted_at IS NULL),
       v.status, v.origin, v.dest,
       NOW() + (v.p_start || ' minutes')::interval,
       NOW() + (v.p_end   || ' minutes')::interval,
       CASE WHEN v.a_start IS NULL THEN NULL ELSE NOW() + (v.a_start || ' minutes')::interval END,
       CASE WHEN v.a_end   IS NULL THEN NULL ELSE NOW() + (v.a_end   || ' minutes')::interval END,
       CASE WHEN v.a_start IS NULL THEN NULL ELSE NOW() + (v.a_start || ' minutes')::interval END,
       CASE WHEN v.a_end   IS NULL THEN NULL ELSE NOW() + (v.a_end   || ' minutes')::interval END,
       v.dist, v.mins, v.stops, v.done, v.ontime, v.compliance, v.compliance, v.speeding
FROM (VALUES
  ('TRP-GTA-W-001','RTE-GTA-WEST-AM','KHALID-PILOT-01','DRV-TST-0001','Scheduled','Mississauga Depot','Pearson Air Cargo', -95, 145, -95,  NULL,142.5,255,4,2,2,92,0),
  ('TRP-GTA-N-001','RTE-GTA-NORTH-AM','TRK-TST-0001','DRV-TST-0002','Scheduled','Mississauga Depot','Markham',            -70, 170, -70,  NULL,118.0,215,4,1,1,89,1),
  ('TRP-GTA-E-001','RTE-GTA-EAST-PM','TRK-TST-0002','DRV-TST-0003','Scheduled','Mississauga Depot','Markham',              45, 395, NULL, NULL,131.0,240,4,0,0,76,0),
  ('TRP-YYZ-001','RTE-YYZ-CARGO','TRK-TST-0003','DRV-TST-0004','Delayed','Mississauga Depot','Pearson Air Cargo',        -160,  60,-160,  NULL, 64.5,150,3,1,0,68,2),
  ('TRP-DT-001','RTE-DT-EXPRESS','TRK-TST-0004','DRV-TST-0005','Scheduled','Mississauga Depot','Toronto Core',            180, 480, NULL, NULL, 48.0,175,3,0,0,85,0),
  ('TRP-HAM-001','RTE-HAM-LINEHAUL','TRK-TST-0005','DRV-TST-0006','Completed','Mississauga Depot','Hamilton',            -540,-120,-540, -155,171.0,225,3,3,3,96,0)
) AS v(ref,rcode,veh,drv,status,origin,dest,p_start,p_end,a_start,a_end,dist,mins,stops,done,ontime,compliance,speeding)
WHERE NOT EXISTS (SELECT 1 FROM trips t WHERE t.company_id=8 AND t.trip_ref=v.ref);

-- Keep routes.total_stops consistent with what was actually inserted.
UPDATE routes r
   SET total_stops = (SELECT count(*) FROM route_stops s WHERE s.route_id=r.id),
       updated_at  = NOW()
 WHERE r.company_id=8;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== KHALID-DEMO operational data =='
SELECT (SELECT count(*) FROM customers   WHERE company_id=8) AS customers,
       (SELECT count(*) FROM routes      WHERE company_id=8) AS routes,
       (SELECT count(*) FROM route_stops WHERE company_id=8) AS stops,
       (SELECT count(*) FROM jobs        WHERE company_id=8) AS jobs,
       (SELECT count(*) FROM trips       WHERE company_id=8) AS trips;

\echo ''
\echo '== routes (expect varied statuses, stops all > 0) =='
SELECT route_code, status, total_stops, region,
       round(estimated_distance,1) AS est_km, sla_risk
FROM routes WHERE company_id=8 ORDER BY route_code;

\echo ''
\echo '== the PT40 lane: vehicle 1024 should be on an ACTIVE route with live jobs =='
SELECT r.route_code, r.status AS route_status, v.vehicle_code,
       (SELECT count(*) FROM jobs j WHERE j.route_id=r.id) AS jobs_on_route
FROM routes r JOIN vehicles v ON v.id=r.assigned_vehicle_id
WHERE r.company_id=8 AND v.vehicle_code='KHALID-PILOT-01';
