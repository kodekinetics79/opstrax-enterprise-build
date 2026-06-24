-- OpsTrax PostgreSQL Seed Data
-- Converted from MySQL: ELT→ARRAY, IF→CASE WHEN, DATE_ADD→interval, CURDATE→CURRENT_DATE,
-- JSON_ARRAY→jsonb_build_array, JSON_OBJECT→jsonb_build_object, LPAD with ::TEXT cast

-- Reset all data and sequences (idempotent re-seed)
TRUNCATE TABLE
  role_permissions, permissions, module_records, ai_recommendations, ai_insights,
  command_center_actions, operational_events, audit_logs, notifications, integrations,
  subscription_plans, kpi_records, sla_records, customer_communications,
  proof_of_delivery, dispatch_recommendations, eta_updates,
  entity_timeline_events, driver_certifications, vehicle_assignments,
  customer_addresses, customer_contacts, asset_documents, driver_documents,
  vehicle_documents, compliance_documents, inspections, hos_logs, expenses,
  carriers, documents, dashcam_events, safety_events, fuel_transactions,
  maintenance_items, work_orders, geofence_events, geofences,
  route_stops, dispatch_assignments, trips, location_events, routes,
  jobs, contracts, assets, customers, vehicles, drivers, users, roles, companies
RESTART IDENTITY CASCADE;

-- Companies (explicit IDs, requires OVERRIDING SYSTEM VALUE with GENERATED ALWAYS AS IDENTITY)
INSERT INTO companies (id, company_code, name, industry, timezone) OVERRIDING SYSTEM VALUE VALUES
(1, 'OPX-DEMO', 'OpsTrax Demo Logistics', 'Transport & Field Operations', 'America/New_York'),
(2, 'NOVA-HAUL', 'Northern Virginia Haulage', 'Regional Carrier', 'America/New_York'),
(3, 'CLIENT-TENANT', 'Client Tenant', 'Customer Portal', 'America/New_York'),
(4, 'VENDOR-TENANT', 'Vendor Services', 'Partner Services', 'America/New_York');
SELECT setval(pg_get_serial_sequence('companies', 'id'), MAX(id)) FROM companies;

INSERT INTO roles (id, name, permissions_json) OVERRIDING SYSTEM VALUE VALUES
(1,  'Super Admin',              jsonb_build_array('*')),
(2,  'Company Admin',            jsonb_build_array('*')),
(3,  'Fleet Manager',            jsonb_build_array('dashboard:view','fleet:view','fleet:manage','maintenance:view','maintenance:manage','telematics:view','dispatch:view','intelligence:view','map:view')),
(4,  'Dispatcher',               jsonb_build_array('dashboard:view','dispatch:view','dispatch:manage','fleet:view','jobs:view','jobs:manage','map:view','customers:view')),
(5,  'Driver',                   jsonb_build_array('driver:portal','jobs:view','dvir:manage')),
(6,  'Mechanic',                 jsonb_build_array('maintenance:view','maintenance:manage','dvir:review','fleet:view')),
(7,  'Safety Manager',           jsonb_build_array('dashboard:view','safety:view','safety:manage','compliance:view','fleet:view','telematics:view','intelligence:view')),
(8,  'Compliance Manager',       jsonb_build_array('dashboard:view','compliance:view','compliance:manage','audit:view','fleet:view','intelligence:view')),
(9,  'Customer Service',         jsonb_build_array('customers:view','customer-portal:view','dispatch:view','crm:view')),
(10, 'Customer Portal User',     jsonb_build_array('customer-portal:view')),
(11, 'Reseller / Partner Admin', jsonb_build_array('*')),
(12, 'Read-only Auditor',        jsonb_build_array('audit:view','fleet:view','dashboard:view')),
(13, 'Operations Manager',       jsonb_build_array('dashboard:view','map:view','fleet:view','dispatch:view','dispatch:manage','orders:view','orders:manage','shipments:view','shipments:manage','pod:view','pod:upload','maintenance:view','safety:view','dashcam:view','compliance:view','reports:view','settings:view')),
(14, 'Finance & Billing Manager',jsonb_build_array('finance:view','finance:manage','fuel:view','fuel:manage','reports:view','settings:view')),
(15, 'CRM & Sales Manager',      jsonb_build_array('crm:view','crm:manage','campaigns:view','campaigns:manage','customer_portal:view','reports:view')),
(16, 'Vendor Service Provider',  jsonb_build_array('vendor_portal:view','maintenance:view','pod:view'));
SELECT setval(pg_get_serial_sequence('roles', 'id'), MAX(id)) FROM roles;

INSERT INTO users (company_id, role_id, full_name, email, role_name, demo_password, permissions_json) VALUES
(1, 1, 'Mason Lee',       'superadmin@opstrax.com',   'Super Admin',             'demo123', jsonb_build_array('*')),
(1, 2, 'Avery Stone',     'admin@opstrax.com',        'Company Admin',           'demo123', jsonb_build_array('*')),
(1, 2, 'Avery Stone',     'admin@demo-fleet.com',     'Company Admin',           'demo123', jsonb_build_array('*')),
(1, 13, 'Erin Parker',    'operations@demo-fleet.com','Operations Manager',      'demo123', jsonb_build_array('dashboard:view','map:view','fleet:view','dispatch:view','dispatch:manage','orders:view','orders:manage','shipments:view','shipments:manage','pod:view','pod:upload','maintenance:view','safety:view','dashcam:view','compliance:view','reports:view','settings:view')),
(1, 4, 'Maya Patel',      'dispatcher@demo-fleet.com','Dispatcher',              'demo123', jsonb_build_array('dashboard:view','dispatch:view','dispatch:manage','fleet:view','jobs:view','jobs:manage','map:view','customers:view')),
(1, 3, 'Nolan Brooks',    'fleet@demo-fleet.com',     'Fleet Manager',           'demo123', jsonb_build_array('dashboard:view','fleet:view','fleet:manage','maintenance:view','maintenance:manage','telematics:view','dispatch:view','intelligence:view','map:view')),
(1, 5, 'Omar Ali',        'driver@demo-fleet.com',    'Driver',                  'demo123', jsonb_build_array('driver:portal','jobs:view','dvir:manage')),
(1, 7, 'Sofia Ramirez',   'safety@demo-fleet.com',    'Safety & Compliance Manager','demo123', jsonb_build_array('dashboard:view','safety:view','safety:manage','compliance:view','fleet:view','telematics:view','intelligence:view')),
(1, 14, 'Priya Shah',     'finance@demo-fleet.com',   'Finance & Billing Manager','demo123', jsonb_build_array('finance:view','finance:manage','fuel:view','fuel:manage','reports:view','settings:view')),
(1, 15, 'Jordan Kim',     'crm@demo-fleet.com',       'CRM & Sales Manager',     'demo123', jsonb_build_array('crm:view','crm:manage','campaigns:view','campaigns:manage','customer_portal:view','reports:view')),
(1, 6, 'Jordan Reyes',    'maintenance@demo-fleet.com','Maintenance Manager',    'demo123', jsonb_build_array('maintenance:view','maintenance:manage','dvir:review','fleet:view')),
(3, 10, 'Priya Shah',     'customer@client.com',      'Customer Portal User',    'demo123', jsonb_build_array('customer-portal:view')),
(4, 16, 'Victor Chen',    'vendor@service.com',       'Vendor Service Provider', 'demo123', jsonb_build_array('vendor_portal:view','maintenance:view','pod:view'));

INSERT INTO customers (company_id, customer_code, name, contact_name, email, status, sla_tier) VALUES
(1,'CUS-001','Prince William Logistics','Nora Lane','nora@pwl.example','Active','Platinum'),
(1,'CUS-002','Northern VA Medical Supply','Ethan Moore','ethan@nvmed.example','Active','Gold'),
(1,'CUS-003','Dulles Field Services','Ivy Brooks','ivy@dulles.example','Active','Gold'),
(1,'CUS-004','Capital Construction Group','Liam Ford','liam@capital.example','Active','Standard'),
(1,'CUS-005','Alexandria Retail Network','Mina Cole','mina@alexretail.example','Active','Gold'),
(1,'CUS-006','Arlington Events Authority','Sam Reed','sam@arlingtonevents.example','Active','Standard'),
(1,'CUS-007','Fairfax Food Distribution','Tara Wells','tara@fairfaxfood.example','Active','Platinum'),
(1,'CUS-008','DC Facilities Exchange','Chris Nguyen','chris@dcfx.example','Active','Standard');

INSERT INTO customers (company_id, customer_code, name, contact_name, email, phone, billing_address, shipping_address, status, sla_tier, sla_health_score, delivery_experience_score, risk_score) VALUES
(1,'CUS-009','Manassas Advanced Manufacturing','Elena Ward','elena@mamfg.example','+1 703 555 4109','9100 Balls Ford Rd, Manassas, VA','9100 Balls Ford Rd, Manassas, VA','Active','Gold',94,92,18),
(1,'CUS-010','Potomac Government Services','Victor James','victor@potomacgov.example','+1 202 555 4110','1200 Pennsylvania Ave NW, Washington DC','650 N Glebe Rd, Arlington, VA','At Risk','Platinum',82,79,42);

INSERT INTO drivers (company_id, driver_code, full_name, phone, email, license_number, license_expiry, status, safety_score, readiness_score)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1,
  'DRV-' || LPAD(n::TEXT, 3, '0'),
  (ARRAY['Omar Ali','Sara Mitchell','Daniel Cruz','Hassan Khan','Maya Carter','Leo Johnson','Ana Rivera','Marcus Lee','Nadia Khan','Theo Brooks'])[((n-1) % 10)+1],
  '+1 571 430 ' || LPAD((5300+n)::TEXT, 4, '0'),
  'driver' || n || '@opstrax.example',
  'VA-D' || LPAD(n::TEXT, 4, '0'),
  CURRENT_DATE + ((45+n*11) || ' days')::interval,
  (ARRAY['Available','On Route','At Stop','Idle','Available','Delayed'])[(n % 6)+1],
  86 + (n % 12),
  84 + (n % 14)
FROM seq;

INSERT INTO vehicles (company_id, vehicle_code, type, make, model, year, vin, plate_number, status, odometer_miles, readiness_score, data_quality_score, assigned_driver_id)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1,
  (ARRAY['TRK','VAN','BOX','REEFER'])[(n % 4)+1] || '-' || (100+n)::TEXT,
  (ARRAY['Truck','Van','Box Truck','Reefer'])[(n % 4)+1],
  (ARRAY['Freightliner','Ford','Isuzu','International','Mercedes'])[(n % 5)+1],
  (ARRAY['M2','Transit','NPR','MV','Sprinter'])[(n % 5)+1],
  2020 + (n % 5),
  'VINOPSTRAX' || LPAD(n::TEXT, 6, '0'),
  'VA-' || (100+n)::TEXT,
  (ARRAY['Available','On Route','At Stop','Idle','Delayed','Maintenance'])[(n % 6)+1],
  14000 + n * 3100,
  82 + (n % 16),
  86 + (n % 13),
  n
FROM seq;

UPDATE drivers SET assigned_vehicle_id=v.id FROM vehicles v WHERE v.assigned_driver_id=drivers.id;
UPDATE drivers SET risk_score = GREATEST(3, 100 - readiness_score), compliance_score = 82 + (id % 17);
UPDATE vehicles SET risk_score = GREATEST(4, 100 - readiness_score),
  device_status = CASE WHEN id % 7 = 0 THEN 'Degraded' ELSE 'Online' END,
  camera_status = CASE WHEN id % 6 = 0 THEN 'Needs Review' ELSE 'Online' END;

INSERT INTO assets (company_id, asset_code, asset_type, name, status, current_location, assigned_vehicle_id)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1,
  (ARRAY['TRL','GEN','REEFER','EQP'])[(n % 4)+1] || '-' || (500+n)::TEXT,
  (ARRAY['Trailer','Generator','Reefer Unit','Equipment'])[(n % 4)+1],
  (ARRAY['Dry Van Trailer','Portable Generator','Temperature Controlled Unit','Liftgate Kit'])[(n % 4)+1] || ' ' || (500+n)::TEXT,
  (ARRAY['Assigned','Available','Assigned','Maintenance'])[(n % 4)+1],
  (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'])[(n % 7)+1],
  CASE WHEN n % 3 = 0 THEN NULL ELSE n END
FROM seq;
UPDATE assets SET assigned_driver_id = assigned_vehicle_id, customer_id = ((id - 1) % 10) + 1,
  current_zone = current_location,
  geofence_status = CASE WHEN id % 5 = 0 THEN 'Outside authorized zone' ELSE 'Inside authorized zone' END,
  utilization_score = 70 + (id % 25),
  risk_score = CASE WHEN id % 5 = 0 THEN 72 ELSE 18 + (id % 22) END;

INSERT INTO contracts (company_id, customer_id, contract_code, title, rate_type, status, effective_date, expiration_date)
SELECT 1, id, 'CTR-' || LPAD(id::TEXT, 3, '0'), name || ' Master Transport Agreement',
  (ARRAY['Mileage','Zone','Dedicated'])[(id % 3)+1], 'Active', CURRENT_DATE, CURRENT_DATE + INTERVAL '1 year'
FROM customers;

INSERT INTO jobs (company_id, customer_id, job_code, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, status, priority, assigned_vehicle_id, assigned_driver_id, sla_due_at)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 40)
SELECT 1, ((n - 1) % 8) + 1, 'JOB-' || (1000+n)::TEXT,
  (ARRAY['Delivery','Service Call','Pickup','Transfer'])[(n % 4)+1],
  (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'])[(n % 7)+1],
  (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'])[((n+2) % 7)+1],
  NOW() + (n || ' hours')::interval,
  NOW() + ((n + 3) || ' hours')::interval,
  (ARRAY['Unassigned','Assigned','In Progress','At Stop','Completed','Delayed','At Risk'])[(n % 7)+1],
  (ARRAY['Low','Normal','High','Critical'])[(n % 4)+1],
  CASE WHEN n % 7 = 1 THEN NULL ELSE ((n - 1) % 20) + 1 END,
  CASE WHEN n % 7 = 1 THEN NULL ELSE ((n - 1) % 20) + 1 END,
  NOW() + ((n + 4) || ' hours')::interval
FROM seq;

INSERT INTO routes (company_id, route_code, name, status, assigned_vehicle_id, assigned_driver_id)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, 'RTE-' || LPAD(n::TEXT, 3, '0'), 'NOVA Corridor ' || n,
  (ARRAY['Planned','Active','At Risk','Completed'])[(n % 4)+1], n, n
FROM seq;

INSERT INTO route_stops (route_id, job_id, stop_sequence, address, lat, lng, eta, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT ((n - 1) % 8) + 1, n, ((n - 1) % 5) + 1,
  (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'])[(n % 7)+1],
  38.6 + (n * .009), -77.5 + (n * .008),
  NOW() + (n || ' hours')::interval,
  (ARRAY['Pending','Arrived','Completed','Delayed'])[(n % 4)+1]
FROM seq;

INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, match_score, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, n, n, n, 88 + n, (ARRAY['Assigned','Accepted','In Progress'])[(n % 3)+1]
FROM seq;

INSERT INTO trips (company_id, job_id, vehicle_id, driver_id, status, started_at, completed_at)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1, n, n, n, (ARRAY['In Progress','Completed','Delayed','Scheduled'])[(n % 4)+1],
  NOW() - (n || ' hours')::interval,
  CASE WHEN n % 2 = 0 THEN NOW() ELSE NULL END
FROM seq;

INSERT INTO location_events (company_id, vehicle_id, driver_id, vehicle_code, driver_code, lat, lng, speed_mph, heading, event_type)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, n, n,
  (ARRAY['TRK','VAN','BOX','REEFER'])[(n % 4)+1] || '-' || (100+n)::TEXT,
  'DRV-' || LPAD(n::TEXT, 3, '0'),
  38.62 + (n * .012), -77.55 + (n * .015),
  CASE WHEN n % 5 = 0 THEN 0 ELSE 22 + n END,
  n * 13,
  (ARRAY['location.updated','geofence.entered','job.delayed','vehicle.idle','safety.event','maintenance.warning','eta.sent'])[(n % 7)+1]
FROM seq;

INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status) VALUES
(1,'Manassas Yard','Circle',38.7509,-77.4753,1600,'Active'),
(1,'Dulles Staging','Circle',38.9531,-77.4565,2200,'Active'),
(1,'Alexandria Medical Zone','Circle',38.8048,-77.0469,1300,'Active'),
(1,'Fairfax Delivery Zone','Circle',38.8462,-77.3064,1800,'Active'),
(1,'Arlington Urban Core','Circle',38.8816,-77.0910,1200,'Active'),
(1,'Washington DC Service Zone','Circle',38.9072,-77.0369,2000,'Active');

INSERT INTO geofence_events (company_id, geofence_id, vehicle_id, event_type)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, ((n-1)%6)+1, n, (ARRAY['geofence.entered','geofence.exited'])[(n%2)+1]
FROM seq;

INSERT INTO maintenance_items (company_id, vehicle_id, title, category, due_date, status, risk_level)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
SELECT 1, ((n-1)%20)+1,
  (ARRAY['Brake inspection','Oil service','Tire rotation','Reefer calibration','DOT inspection'])[(n%5)+1] || ' required',
  (ARRAY['Brakes','Engine','Tires','Cold Chain','Compliance'])[(n%5)+1],
  CURRENT_DATE + (n || ' days')::interval,
  (ARRAY['Open','Scheduled','In Progress','Deferred'])[(n%4)+1],
  (ARRAY['Low','Medium','High','Critical'])[(n%4)+1]
FROM seq;

INSERT INTO work_orders (company_id, vehicle_id, work_order_code, title, priority, status, due_date, estimated_cost)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, n, 'WO-' || (2000+n)::TEXT,
  (ARRAY['Preventive maintenance','Brake inspection','Idle diagnostics','Tire replacement','Sensor calibration'])[(n%5)+1] || ' - unit ' || n,
  (ARRAY['Low','Normal','High','Critical'])[(n%4)+1],
  (ARRAY['Open','Scheduled','In Progress','Waiting Parts'])[(n%4)+1],
  CURRENT_DATE + (n || ' days')::interval,
  180 + n*75
FROM seq;

INSERT INTO fuel_transactions (company_id, vehicle_id, gallons, total_cost, idle_minutes, fuel_station)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
SELECT 1, ((n-1)%20)+1, 24 + n, (24+n)*3.86, n*7,
  (ARRAY['Shell Manassas','Exxon Woodbridge','BP Fairfax','Wawa Dulles','Sunoco Alexandria'])[(n%5)+1]
FROM seq;

INSERT INTO safety_events (company_id, vehicle_id, driver_id, event_type, severity, description, review_status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
SELECT 1, ((n-1)%20)+1, ((n-1)%20)+1,
  (ARRAY['Harsh Braking','Speeding','Following Distance','Lane Departure','Extended Idle'])[(n%5)+1],
  (ARRAY['Low','Medium','High','Critical'])[(n%4)+1],
  'Event detected by OpsTrax telemetry and queued for review.',
  (ARRAY['Open','Reviewing','Coaching Assigned'])[(n%3)+1]
FROM seq;

INSERT INTO dashcam_events (company_id, safety_event_id, title, severity, coaching_status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, 'AI dashcam review ' || n,
  (ARRAY['Low','Medium','High','Critical'])[(n%4)+1],
  (ARRAY['Needs Review','Coach Driver','Resolved'])[(n%3)+1]
FROM seq;

INSERT INTO compliance_documents (company_id, related_entity_type, related_entity_id, document_type, document_name, expiry_date, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, (ARRAY['Vehicle','Driver'])[(n%2)+1], n,
  (ARRAY['Insurance','Inspection','License','Permit'])[(n%4)+1],
  'Compliance document ' || n,
  CURRENT_DATE + (n*12 || ' days')::interval,
  (ARRAY['Valid','Expiring Soon','Review Required','Valid'])[(n%4)+1]
FROM seq;

INSERT INTO inspections (company_id, vehicle_id, driver_id, inspection_type, result, notes)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1, ((n-1)%20)+1, ((n-1)%20)+1,
  (ARRAY['Pre-trip DVIR','Post-trip DVIR','DOT Inspection'])[(n%3)+1],
  (ARRAY['Passed','Passed','Defect Found','Needs Review'])[(n%4)+1],
  'Inspection captured in OpsTrax DVIR workflow.'
FROM seq;

INSERT INTO hos_logs (company_id, driver_id, log_date, driving_hours, on_duty_hours, cycle_hours_left, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, n, CURRENT_DATE - (n || ' days')::interval, 6 + (n % 6), 8 + (n % 7), 70 - n*3,
  CASE WHEN n % 4 = 0 THEN 'Near Limit' ELSE 'Compliant' END
FROM seq;

INSERT INTO expenses (company_id, category, title, amount, status, expense_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, (ARRAY['Fuel','Maintenance','Tolls','Lodging','Parts'])[(n%5)+1],
  'Operational expense ' || n, 90 + n*44,
  (ARRAY['Approved','Pending','Review'])[(n%3)+1],
  CURRENT_DATE - (n || ' days')::interval
FROM seq;

INSERT INTO carriers (company_id, name, mc_number, safety_rating, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 6)
SELECT 1,
  (ARRAY['Blue Ridge Carrier','Capital Freight','Dulles Express','Potomac Partner','NOVA Regional','Arlington Courier'])[n],
  'MC-' || (90000+n)::TEXT,
  (ARRAY['Satisfactory','Preferred','Watchlist'])[(n%3)+1],
  'Active'
FROM seq;

INSERT INTO documents (company_id, title, document_type, owner_name, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, 'OpsTrax document ' || n,
  (ARRAY['Insurance','POD','Contract','Inspection','Permit'])[(n%5)+1],
  (ARRAY['Fleet','Dispatch','Compliance','Customer Success','Finance'])[(n%5)+1],
  (ARRAY['Active','Expiring','Archived'])[(n%3)+1]
FROM seq;

INSERT INTO sla_records (company_id, customer_id, metric_name, target_value, actual_value, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, ((n-1)%8)+1,
  (ARRAY['On-time delivery','ETA accuracy','Proof capture','Exception response'])[(n%4)+1],
  95, 88 + (n % 11),
  CASE WHEN n % 5 = 0 THEN 'At Risk' ELSE 'On Track' END
FROM seq;

INSERT INTO kpi_records (company_id, metric_key, label, value_text, trend, trend_value, status) VALUES
(1,'fleet_readiness','Fleet Readiness','92%','up','+3.2%','Healthy'),
(1,'jobs_active','Active Jobs','31','up','+8','Healthy'),
(1,'eta_risk','ETA Risk','6','down','-2','Warning'),
(1,'safety_score','Safety Score','91/100','up','+1.1','Healthy'),
(1,'maintenance_due','Maintenance Due','10','down','-3','Warning'),
(1,'fuel_leakage','Fuel Leakage','$4.8k','down','-9%','Warning'),
(1,'compliance_risk','Compliance Risk','4','flat','0','Watch'),
(1,'ai_actions','AI Actions','12','up','+5','Healthy'),
(1,'customer_sla','Customer SLA','96.2%','up','+1.8%','Healthy'),
(1,'driver_ready','Driver Ready','89%','flat','0','Healthy');

INSERT INTO ai_insights (company_id, insight_type, title, body, severity, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
SELECT 1,
  (ARRAY['Dispatch Risk','Cost Leakage','Maintenance Advisor','Driver Coaching','Compliance Audit','Customer SLA','Executive Summary'])[(n%7)+1],
  'OpsTrax AI insight ' || n,
  'Seeded operational intelligence item ' || n || ' across Northern Virginia/DC operations. Review evidence, assign owner, and complete the recommended action.',
  (ARRAY['Info','Warning','High','Critical'])[(n%4)+1],
  (ARRAY['Open','Acknowledged','In Progress'])[(n%3)+1]
FROM seq;

INSERT INTO ai_recommendations (company_id, module_key, title, body, score, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 70)
SELECT 1,
  (ARRAY['command-center','control-tower','dispatch','jobs','route-planning','vehicles','drivers','assets','maintenance','work-orders','fuel-idling','safety','dashcam','compliance','hos-eld','dvir-inspections','customer-portal','customers','contracts-rates','carrier-management','expenses','documents','reports-analytics','sla-kpi','predictive-margin','audit-logs','ai-copilot','integrations','user-management','settings','billing'])[(n%31)+1],
  'Recommended action ' || n,
  'OpsTrax AI recommends reviewing this operational signal and assigning the next best action.',
  70 + (n % 28), 'Recommended'
FROM seq;

INSERT INTO command_center_actions (company_id, title, module_key, priority, status, due_at)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1, 'Priority command action ' || n,
  (ARRAY['dispatch','maintenance','safety','compliance','fuel-idling','customer-portal'])[(n%6)+1],
  (ARRAY['Low','Medium','High','Critical'])[(n%4)+1],
  (ARRAY['Open','Acknowledged','In Progress'])[(n%3)+1],
  NOW() + (n || ' hours')::interval
FROM seq;

INSERT INTO operational_events (company_id, entity_type, entity_id, event_type, title, severity)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 25)
SELECT 1, (ARRAY['Vehicle','Driver','Job','Route','Customer'])[(n%5)+1], n,
  (ARRAY['location.updated','geofence.entered','job.delayed','vehicle.idle','safety.event','maintenance.warning','eta.sent'])[(n%7)+1],
  'Live operational event ' || n,
  (ARRAY['Info','Warning','High','Critical'])[(n%4)+1]
FROM seq;

INSERT INTO audit_logs (company_id, actor_name, action_name, entity_name, entity_id, details_json)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1,
  (ARRAY['Avery Stone','Maya Patel','OpsTrax AI','System'])[(n%4)+1],
  (ARRAY['vehicle.updated','driver.assigned','job.status.changed','eta.sent','dispatch.recommendation.accepted','command.action.completed','settings.changed','user.login'])[(n%8)+1],
  (ARRAY['Vehicle','Driver','Job','Dispatch','Settings'])[(n%5)+1],
  n, jsonb_build_object('seeded', true)
FROM seq;

INSERT INTO customer_communications (company_id, customer_id, job_id, channel, message, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, ((n-1)%8)+1, n, (ARRAY['Email','SMS','Portal'])[(n%3)+1],
  'ETA and service update for job ' || n, 'Sent'
FROM seq;

INSERT INTO proof_of_delivery (company_id, job_id, receiver_name, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, (ARRAY['R. Morgan','C. Rivera','D. Chen','M. Ahmed','S. Brooks'])[(n%5)+1], 'Captured'
FROM seq;

INSERT INTO dispatch_recommendations (company_id, job_id, vehicle_id, driver_id, recommendation, score, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, n, n,
  'Assign best-fit vehicle/driver pair for job ' || n || ' based on proximity, readiness, HOS and SLA risk.',
  88 + n, 'Recommended'
FROM seq;

INSERT INTO eta_updates (company_id, job_id, message, channel, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, 'ETA update sent for JOB-' || (1000+n)::TEXT, 'Email/SMS', 'Sent'
FROM seq;

INSERT INTO vehicle_documents (company_id, vehicle_id, document_type, document_name, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 24)
SELECT 1, ((n-1)%20)+1,
  (ARRAY['Registration','Insurance','DOT Inspection','Camera Calibration'])[(n%4)+1],
  'Vehicle document ' || n,
  (ARRAY['Active','Active','Expiring Soon','Review','Active'])[(n%5)+1],
  CURRENT_DATE + (n*9 || ' days')::interval
FROM seq;

INSERT INTO driver_documents (company_id, driver_id, document_type, document_name, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 24)
SELECT 1, ((n-1)%20)+1,
  (ARRAY['License','Medical Card','Background Check','Training Record'])[(n%4)+1],
  'Driver document ' || n,
  (ARRAY['Active','Active','Expiring Soon','Review','Active'])[(n%5)+1],
  CURRENT_DATE + (n*11 || ' days')::interval
FROM seq;

INSERT INTO customer_contacts (company_id, customer_id, full_name, title, email, phone, is_primary)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, ((n-1)%10)+1,
  (ARRAY['Nora Lane','Ethan Moore','Ivy Brooks','Liam Ford','Mina Cole','Sam Reed','Tara Wells','Chris Nguyen','Elena Ward','Victor James'])[(n%10)+1],
  (ARRAY['Operations Lead','Transportation Manager','Warehouse Supervisor','Customer Success'])[(n%4)+1],
  'contact' || n || '@customer.example',
  '+1 703 555 ' || LPAD((6000+n)::TEXT, 4, '0'),
  n <= 10
FROM seq;

INSERT INTO customer_addresses (company_id, customer_id, address_type, address_line, city, state, postal_code)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, ((n-1)%10)+1,
  (ARRAY['Billing','Shipping'])[(n%2)+1],
  (ARRAY['9200 Lee Ave','1450 Duke St','45020 Aviation Dr','3100 Wilson Blvd','1200 Pennsylvania Ave NW','8300 Boone Blvd','7700 Richmond Hwy'])[(n%7)+1],
  (ARRAY['Manassas','Alexandria','Dulles','Arlington','Washington','Tysons','Fairfax'])[(n%7)+1],
  CASE WHEN n % 5 = 0 THEN 'DC' ELSE 'VA' END,
  '20' || LPAD((100+n)::TEXT, 3, '0')
FROM seq;

INSERT INTO asset_documents (company_id, asset_id, document_type, document_name, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 18)
SELECT 1, ((n-1)%12)+1,
  (ARRAY['Registration','Inspection','Warranty','Calibration'])[(n%4)+1],
  'Asset document ' || n,
  (ARRAY['Active','Active','Expiring Soon','Review','Active'])[(n%5)+1],
  CURRENT_DATE + (n*13 || ' days')::interval
FROM seq;

INSERT INTO vehicle_assignments (company_id, vehicle_id, driver_id, assignment_type, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, n, n,
  (ARRAY['Primary Driver','Relief Driver','Dispatch Pairing'])[(n%3)+1], 'Active'
FROM seq;

INSERT INTO driver_certifications (company_id, driver_id, certification_type, certification_number, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 24)
SELECT 1, ((n-1)%20)+1,
  (ARRAY['CDL','Hazmat','Medical Examiner Certificate','Defensive Driving','Cold Chain Handling'])[(n%5)+1],
  'CERT-' || LPAD(n::TEXT, 5, '0'),
  (ARRAY['Valid','Valid','Expiring Soon','Review','Valid'])[(n%5)+1],
  CURRENT_DATE + (n*14 || ' days')::interval
FROM seq;

INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 120)
SELECT 1,
  (ARRAY['Vehicle','Driver','Customer','Asset'])[((n-1)%4)+1],
  ((n-1) % CASE WHEN ((n-1)%4)+1 IN (1,2) THEN 20 WHEN ((n-1)%4)+1 = 3 THEN 10 ELSE 12 END) + 1,
  (ARRAY['created','updated','assigned','document.review','risk.changed','ai.recommendation','audit.logged'])[(n%7)+1],
  'Batch 1 timeline event ' || n,
  'Seeded operational history for Batch 1 detail drawers across Northern Virginia/DC operations.',
  (ARRAY['Info','Warning','High','Info'])[(n%4)+1]
FROM seq;

INSERT INTO notifications (company_id, title, body, status) VALUES
(1,'OpsTrax AI Active','AI copilot is monitoring dispatch, cost, safety and compliance signals.','Delivered'),
(1,'Live Simulation Running','Node event stream is available for control tower updates.','Delivered');

INSERT INTO integrations (company_id, provider_name, category, status) VALUES
(1,'Samsara Import Adapter','Telematics','Connected'),
(1,'Motive ELD Bridge','ELD','Ready'),
(1,'QuickBooks Sync','Finance','Connected'),
(1,'Twilio ETA Messaging','Communications','Connected'),
(1,'Stripe Billing','Subscription','Connected'),
(1,'Azure Blob Documents','Documents','Connected');

INSERT INTO subscription_plans (company_id, plan_name, billing_status, seats, monthly_amount) VALUES
(1,'OpsTrax Enterprise Command','Active',75,3499.00);

INSERT INTO module_records (module_key, title, status, owner_name, location_name, due_at, risk_level, amount, metadata_json)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 93)
SELECT
  (ARRAY['command-center','control-tower','dispatch','jobs','route-planning','vehicles','drivers','assets','maintenance','work-orders','fuel-idling','safety','dashcam','compliance','hos-eld','dvir-inspections','customer-portal','customers','contracts-rates','carrier-management','expenses','documents','reports-analytics','sla-kpi','predictive-margin','audit-logs','ai-copilot','integrations','user-management','settings','billing'])[((n-1)%31)+1],
  'OpsTrax ' || REPLACE((ARRAY['Command Center','Control Tower','Dispatch Board','Jobs & Orders','Route Planning','Vehicles','Drivers','Assets / Trailers / Equipment','Maintenance','Work Orders','Fuel & Idling','Safety','AI Dashcam / Incident Review','Compliance','HOS / ELD Framework','DVIR / Inspections','Customer ETA Portal','Clients / Customers','Contracts / Rates','Carrier Management','Expenses','Documents','Reports & Analytics','SLA / KPI Center','Predictive Cost & Margin','Audit Logs','OpsTrax AI Copilot','Integrations','User Management','Settings','Billing / Subscription'])[((n-1)%31)+1], '/', '-') || ' record ' || n,
  (ARRAY['Open','Active','In Progress','At Risk','Completed','Review'])[(n%6)+1],
  (ARRAY['Avery Stone','Maya Patel','Jordan Reyes','OpsTrax AI','Customer Success','Finance Ops'])[(n%6)+1],
  (ARRAY['Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'])[(n%7)+1],
  NOW() + (n || ' hours')::interval,
  (ARRAY['Low','Medium','High','Critical'])[(n%4)+1],
  ROUND((100 + n*37.5)::NUMERIC, 2),
  jsonb_build_object('seeded', true, 'index', n)
FROM seq;

-- =====================================================================
-- RBAC: permissions catalogue
-- =====================================================================
INSERT INTO permissions (permission_key, label, module_group, description) VALUES
('*',                   'Super Admin — All Access',          'System',           'Grants unrestricted access to every module and action'),
('dashboard:view',      'Dashboard',                         'Control Tower',    'Live dashboard, command center, control tower views'),
('map:view',            'Map View',                         'Control Tower',    'Fleet 360 live map and location feeds'),
('dispatch:view',       'Dispatch — View',                  'Transport Ops',    'View dispatch board, jobs, assignments'),
('dispatch:manage',     'Dispatch — Manage',                'Transport Ops',    'Assign, reassign, and update dispatch records'),
('jobs:view',           'Jobs — View',                      'Transport Ops',    'View job records and status'),
('jobs:manage',         'Jobs — Manage',                    'Transport Ops',    'Create, update, and delete jobs'),
('fleet:view',          'Fleet — View',                     'Fleet',            'View vehicles, drivers, assets, assignments'),
('fleet:manage',        'Fleet — Manage',                   'Fleet',            'Create, update, and delete fleet entities'),
('telematics:view',     'Telematics & IoT',                 'Telematics',       'GPS tracking, IoT devices, OBD, cold chain, sensor health'),
('safety:view',         'Safety — View',                    'Safety',           'View safety events, dashcam, coaching, incidents'),
('safety:manage',       'Safety — Manage',                  'Safety',           'Manage coaching tasks, incidents, evidence packages'),
('maintenance:view',    'Maintenance — View',               'Maintenance',      'View maintenance items, work orders, DVIR, service history'),
('maintenance:manage',  'Maintenance — Manage',             'Maintenance',      'Create and update maintenance items, work orders, DVIR'),
('dvir:manage',         'DVIR — Submit & Update',           'Maintenance',      'Driver can submit and update DVIR reports'),
('dvir:review',         'DVIR — Mechanic Review',           'Maintenance',      'Mechanic can review and certify DVIR reports'),
('compliance:view',     'Compliance — View',                'Compliance',       'View HOS/ELD, compliance profiles, violations'),
('compliance:manage',   'Compliance — Manage',              'Compliance',       'Manage compliance profiles, violations, audit packages'),
('finance:view',        'Finance — View',                   'Financials',       'View fuel, expenses, invoices, cost margin'),
('finance:manage',      'Finance — Manage',                 'Financials',       'Approve expenses, manage invoices and cost records'),
('customers:view',      'Customers & Commercial — View',    'Commercial',       'View customers, contracts, rate cards, quotations'),
('crm:view',            'CRM & Growth — View',              'CRM',              'View leads, pipeline, campaigns, account health'),
('customer-portal:view','Customer Portal',                  'Portal',           'Customer self-service ETA and delivery portal'),
('intelligence:view',   'Intelligence & Reports',           'Intelligence',     'AI copilot, reports analytics, executive views'),
('audit:view',          'Audit Logs',                       'Governance',       'View immutable audit log and export requests'),
('governance:manage',   'Governance & Admin',               'Governance',       'Manage users, roles, feature flags, integrations'),
('driver:portal',       'Driver Portal',                    'Driver',           'Driver mobile-style portal for jobs, HOS, DVIR');

-- RBAC: role_permissions mapping
INSERT INTO role_permissions (role_id, permission_key) VALUES (1,'*');
INSERT INTO role_permissions (role_id, permission_key) VALUES (2,'*');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(3,'dashboard:view'),(3,'fleet:view'),(3,'fleet:manage'),(3,'maintenance:view'),
(3,'maintenance:manage'),(3,'telematics:view'),(3,'dispatch:view'),(3,'intelligence:view'),(3,'map:view');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(4,'dashboard:view'),(4,'dispatch:view'),(4,'dispatch:manage'),(4,'fleet:view'),
(4,'jobs:view'),(4,'jobs:manage'),(4,'map:view'),(4,'customers:view');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(5,'driver:portal'),(5,'jobs:view'),(5,'dvir:manage');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(6,'maintenance:view'),(6,'maintenance:manage'),(6,'dvir:review'),(6,'fleet:view');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(7,'dashboard:view'),(7,'safety:view'),(7,'safety:manage'),(7,'compliance:view'),
(7,'fleet:view'),(7,'telematics:view'),(7,'intelligence:view');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(8,'dashboard:view'),(8,'compliance:view'),(8,'compliance:manage'),(8,'audit:view'),
(8,'fleet:view'),(8,'intelligence:view');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(9,'customers:view'),(9,'customer-portal:view'),(9,'dispatch:view'),(9,'crm:view');
INSERT INTO role_permissions (role_id, permission_key) VALUES (10,'customer-portal:view');
INSERT INTO role_permissions (role_id, permission_key) VALUES (11,'*');
INSERT INTO role_permissions (role_id, permission_key) VALUES
(12,'audit:view'),(12,'fleet:view'),(12,'dashboard:view');
