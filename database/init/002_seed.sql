USE opstrax;

INSERT INTO companies (id, company_code, name, industry, timezone) VALUES
(1, 'OPX-DEMO', 'OpsTrax Demo Logistics', 'Transport & Field Operations', 'America/New_York'),
(2, 'NOVA-HAUL', 'Northern Virginia Haulage', 'Regional Carrier', 'America/New_York');

INSERT INTO roles (name, permissions_json) VALUES
('Super Admin',            JSON_ARRAY('*')),
('Company Admin',          JSON_ARRAY('*')),
('Fleet Manager',          JSON_ARRAY('dashboard:view','fleet:view','fleet:manage','maintenance:view','maintenance:manage','telematics:view','dispatch:view','intelligence:view','map:view')),
('Dispatcher',             JSON_ARRAY('dashboard:view','dispatch:view','dispatch:manage','fleet:view','jobs:view','jobs:manage','map:view','customers:view')),
('Driver',                 JSON_ARRAY('driver:portal','jobs:view','dvir:manage')),
('Mechanic',               JSON_ARRAY('maintenance:view','maintenance:manage','dvir:review','fleet:view')),
('Safety Manager',         JSON_ARRAY('dashboard:view','safety:view','safety:manage','compliance:view','fleet:view','telematics:view','intelligence:view')),
('Compliance Manager',     JSON_ARRAY('dashboard:view','compliance:view','compliance:manage','audit:view','fleet:view','intelligence:view')),
('Customer Service',       JSON_ARRAY('customers:view','customer-portal:view','dispatch:view','crm:view')),
('Customer Portal User',   JSON_ARRAY('customer-portal:view')),
('Reseller / Partner Admin', JSON_ARRAY('*')),
('Read-only Auditor',      JSON_ARRAY('audit:view','fleet:view','dashboard:view'));

INSERT INTO users (company_id, role_id, full_name, email, role_name, demo_password, permissions_json) VALUES
(1, 2, 'Avery Stone',    'admin@opstrax.com',      'Company Admin',       'demo123', JSON_ARRAY('*')),
(1, 4, 'Maya Patel',     'dispatcher@opstrax.com', 'Dispatcher',          'demo123', JSON_ARRAY('dashboard:view','dispatch:view','dispatch:manage','fleet:view','jobs:view','jobs:manage','map:view','customers:view')),
(1, 5, 'Omar Ali',       'driver@opstrax.com',     'Driver',              'demo123', JSON_ARRAY('driver:portal','jobs:view','dvir:manage')),
(1, 6, 'Jordan Reyes',   'mechanic@opstrax.com',   'Mechanic',            'demo123', JSON_ARRAY('maintenance:view','maintenance:manage','dvir:review','fleet:view')),
(1, 10, 'Priya Shah',    'customer@opstrax.com',   'Customer Portal User','demo123', JSON_ARRAY('customer-portal:view'));

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
SELECT 1, CONCAT('DRV-', LPAD(n,3,'0')),
ELT(((n-1) % 10)+1,'Omar Ali','Sara Mitchell','Daniel Cruz','Hassan Khan','Maya Carter','Leo Johnson','Ana Rivera','Marcus Lee','Nadia Khan','Theo Brooks'),
CONCAT('+1 571 430 ', LPAD(5300+n,4,'0')),
CONCAT('driver', n, '@opstrax.example'),
CONCAT('VA-D', LPAD(n,4,'0')),
DATE_ADD(CURDATE(), INTERVAL (45+n*11) DAY),
ELT((n % 6)+1,'Available','On Route','At Stop','Idle','Available','Delayed'),
86 + (n % 12),
84 + (n % 14)
FROM seq;

INSERT INTO vehicles (company_id, vehicle_code, type, make, model, year, vin, plate_number, status, odometer_miles, readiness_score, data_quality_score, assigned_driver_id)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1,
CONCAT(ELT((n % 4)+1,'TRK','VAN','BOX','REEFER'), '-', 100+n),
ELT((n % 4)+1,'Truck','Van','Box Truck','Reefer'),
ELT((n % 5)+1,'Freightliner','Ford','Isuzu','International','Mercedes'),
ELT((n % 5)+1,'M2','Transit','NPR','MV','Sprinter'),
2020 + (n % 5),
CONCAT('VINOPSTRAX', LPAD(n,6,'0')),
CONCAT('VA-', 100+n),
ELT((n % 6)+1,'Available','On Route','At Stop','Idle','Delayed','Maintenance'),
14000 + n * 3100,
82 + (n % 16),
86 + (n % 13),
n
FROM seq;

UPDATE drivers d JOIN vehicles v ON v.assigned_driver_id=d.id SET d.assigned_vehicle_id=v.id;
UPDATE drivers SET risk_score = GREATEST(3, 100 - readiness_score), compliance_score = 82 + (id % 17);
UPDATE vehicles SET risk_score = GREATEST(4, 100 - readiness_score), device_status = IF(id % 7 = 0, 'Degraded', 'Online'), camera_status = IF(id % 6 = 0, 'Needs Review', 'Online');

INSERT INTO assets (company_id, asset_code, asset_type, name, status, current_location, assigned_vehicle_id)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1, CONCAT(ELT((n % 4)+1,'TRL','GEN','REEFER','EQP'), '-', 500+n),
ELT((n % 4)+1,'Trailer','Generator','Reefer Unit','Equipment'),
CONCAT(ELT((n % 4)+1,'Dry Van Trailer','Portable Generator','Temperature Controlled Unit','Liftgate Kit'), ' ', 500+n),
ELT((n % 4)+1,'Assigned','Available','Assigned','Maintenance'),
ELT((n % 7)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'),
IF(n % 3 = 0, NULL, n)
FROM seq;
UPDATE assets SET assigned_driver_id = assigned_vehicle_id, customer_id = ((id - 1) % 10) + 1, current_zone = current_location, geofence_status = IF(id % 5 = 0, 'Outside authorized zone', 'Inside authorized zone'), utilization_score = 70 + (id % 25), risk_score = IF(id % 5 = 0, 72, 18 + (id % 22));

INSERT INTO contracts (company_id, customer_id, contract_code, title, rate_type, status, effective_date, expiration_date)
SELECT 1, id, CONCAT('CTR-', LPAD(id,3,'0')), CONCAT(name, ' Master Transport Agreement'), ELT((id % 3)+1,'Mileage','Zone','Dedicated'), 'Active', CURDATE(), DATE_ADD(CURDATE(), INTERVAL 1 YEAR)
FROM customers;

INSERT INTO jobs (company_id, customer_id, job_code, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, status, priority, assigned_vehicle_id, assigned_driver_id, sla_due_at)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 40)
SELECT 1, ((n - 1) % 8) + 1, CONCAT('JOB-', 1000+n),
ELT((n % 4)+1,'Delivery','Service Call','Pickup','Transfer'),
ELT((n % 7)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'),
ELT(((n+2) % 7)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'),
DATE_ADD(NOW(), INTERVAL n HOUR),
DATE_ADD(NOW(), INTERVAL (n + 3) HOUR),
ELT((n % 7)+1,'Unassigned','Assigned','In Progress','At Stop','Completed','Delayed','At Risk'),
ELT((n % 4)+1,'Low','Normal','High','Critical'),
IF(n % 7 = 1, NULL, ((n - 1) % 20) + 1),
IF(n % 7 = 1, NULL, ((n - 1) % 20) + 1),
DATE_ADD(NOW(), INTERVAL (n + 4) HOUR)
FROM seq;

INSERT INTO routes (company_id, route_code, name, status, assigned_vehicle_id, assigned_driver_id)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, CONCAT('RTE-', LPAD(n,3,'0')), CONCAT('NOVA Corridor ', n), ELT((n % 4)+1,'Planned','Active','At Risk','Completed'), n, n FROM seq;

INSERT INTO route_stops (route_id, job_id, stop_sequence, address, lat, lng, eta, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT ((n - 1) % 8) + 1, n, ((n - 1) % 5) + 1,
ELT((n % 7)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'),
38.6 + (n * .009), -77.5 + (n * .008), DATE_ADD(NOW(), INTERVAL n HOUR), ELT((n % 4)+1,'Pending','Arrived','Completed','Delayed')
FROM seq;

INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, match_score, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, n, n, n, 88 + n, ELT((n % 3)+1,'Assigned','Accepted','In Progress') FROM seq;

INSERT INTO trips (company_id, job_id, vehicle_id, driver_id, status, started_at, completed_at)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1, n, n, n, ELT((n % 4)+1,'In Progress','Completed','Delayed','Scheduled'), DATE_SUB(NOW(), INTERVAL n HOUR), IF(n % 2 = 0, NOW(), NULL) FROM seq;

INSERT INTO location_events (company_id, vehicle_id, driver_id, vehicle_code, driver_code, lat, lng, speed_mph, heading, event_type)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, n, n, CONCAT(ELT((n % 4)+1,'TRK','VAN','BOX','REEFER'), '-', 100+n), CONCAT('DRV-', LPAD(n,3,'0')),
38.62 + (n * .012), -77.55 + (n * .015), IF(n % 5 = 0, 0, 22 + n), n * 13, ELT((n % 7)+1,'location.updated','geofence.entered','job.delayed','vehicle.idle','safety.event','maintenance.warning','eta.sent')
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
SELECT 1, ((n-1)%6)+1, n, ELT((n%2)+1,'geofence.entered','geofence.exited') FROM seq;

INSERT INTO maintenance_items (company_id, vehicle_id, title, category, due_date, status, risk_level)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
SELECT 1, ((n-1)%20)+1, CONCAT(ELT((n%5)+1,'Brake inspection','Oil service','Tire rotation','Reefer calibration','DOT inspection'), ' required'), ELT((n%5)+1,'Brakes','Engine','Tires','Cold Chain','Compliance'), DATE_ADD(CURDATE(), INTERVAL n DAY), ELT((n%4)+1,'Open','Scheduled','In Progress','Deferred'), ELT((n%4)+1,'Low','Medium','High','Critical') FROM seq;

INSERT INTO work_orders (company_id, vehicle_id, work_order_code, title, priority, status, due_date, estimated_cost)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, n, CONCAT('WO-',2000+n), CONCAT(ELT((n%5)+1,'Preventive maintenance','Brake inspection','Idle diagnostics','Tire replacement','Sensor calibration'), ' - unit ', n), ELT((n%4)+1,'Low','Normal','High','Critical'), ELT((n%4)+1,'Open','Scheduled','In Progress','Waiting Parts'), DATE_ADD(CURDATE(), INTERVAL n DAY), 180 + n*75 FROM seq;

INSERT INTO fuel_transactions (company_id, vehicle_id, gallons, total_cost, idle_minutes, fuel_station)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
SELECT 1, ((n-1)%20)+1, 24 + n, (24+n)*3.86, n*7, ELT((n%5)+1,'Shell Manassas','Exxon Woodbridge','BP Fairfax','Wawa Dulles','Sunoco Alexandria') FROM seq;

INSERT INTO safety_events (company_id, vehicle_id, driver_id, event_type, severity, description, review_status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 15)
SELECT 1, ((n-1)%20)+1, ((n-1)%20)+1, ELT((n%5)+1,'Harsh Braking','Speeding','Following Distance','Lane Departure','Extended Idle'), ELT((n%4)+1,'Low','Medium','High','Critical'), 'Event detected by OpsTrax telemetry and queued for review.', ELT((n%3)+1,'New','In Review','Coaching Assigned') FROM seq;

INSERT INTO dashcam_events (company_id, safety_event_id, title, severity, coaching_status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, CONCAT('AI dashcam review ', n), ELT((n%4)+1,'Low','Medium','High','Critical'), ELT((n%3)+1,'Needs Review','Coach Driver','Resolved') FROM seq;

INSERT INTO compliance_documents (company_id, related_entity_type, related_entity_id, document_type, document_name, expiry_date, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, ELT((n%2)+1,'Vehicle','Driver'), n, ELT((n%4)+1,'Insurance','Inspection','License','Permit'), CONCAT('Compliance document ', n), DATE_ADD(CURDATE(), INTERVAL n*12 DAY), ELT((n%4)+1,'Valid','Expiring Soon','Review Required','Valid') FROM seq;

INSERT INTO inspections (company_id, vehicle_id, driver_id, inspection_type, result, notes)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1, ((n-1)%20)+1, ((n-1)%20)+1, ELT((n%3)+1,'Pre-trip DVIR','Post-trip DVIR','DOT Inspection'), ELT((n%4)+1,'Passed','Passed','Defect Found','Needs Review'), 'Inspection captured in OpsTrax DVIR workflow.' FROM seq;

INSERT INTO hos_logs (company_id, driver_id, log_date, driving_hours, on_duty_hours, cycle_hours_left, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, n, DATE_SUB(CURDATE(), INTERVAL n DAY), 6 + (n % 6), 8 + (n % 7), 70 - n*3, IF(n % 4 = 0, 'Near Limit', 'Compliant') FROM seq;

INSERT INTO expenses (company_id, category, title, amount, status, expense_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, ELT((n%5)+1,'Fuel','Maintenance','Tolls','Lodging','Parts'), CONCAT('Operational expense ', n), 90 + n*44, ELT((n%3)+1,'Approved','Pending','Review'), DATE_SUB(CURDATE(), INTERVAL n DAY) FROM seq;

INSERT INTO carriers (company_id, name, mc_number, safety_rating, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 6)
SELECT 1, CONCAT(ELT(n,'Blue Ridge Carrier','Capital Freight','Dulles Express','Potomac Partner','NOVA Regional','Arlington Courier')), CONCAT('MC-',90000+n), ELT((n%3)+1,'Satisfactory','Preferred','Watchlist'), 'Active' FROM seq;

INSERT INTO documents (company_id, title, document_type, owner_name, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, CONCAT('OpsTrax document ', n), ELT((n%5)+1,'Insurance','POD','Contract','Inspection','Permit'), ELT((n%5)+1,'Fleet','Dispatch','Compliance','Customer Success','Finance'), ELT((n%3)+1,'Active','Expiring','Archived') FROM seq;

INSERT INTO sla_records (company_id, customer_id, metric_name, target_value, actual_value, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 10)
SELECT 1, ((n-1)%8)+1, ELT((n%4)+1,'On-time delivery','ETA accuracy','Proof capture','Exception response'), 95, 88 + (n % 11), IF(n % 5 = 0, 'At Risk', 'On Track') FROM seq;

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
SELECT 1, ELT((n%7)+1,'Dispatch Risk','Cost Leakage','Maintenance Advisor','Driver Coaching','Compliance Audit','Customer SLA','Executive Summary'),
CONCAT('OpsTrax AI insight ', n),
CONCAT('Seeded operational intelligence item ', n, ' across Northern Virginia/DC operations. Review evidence, assign owner, and complete the recommended action.'),
ELT((n%4)+1,'Info','Warning','High','Critical'), ELT((n%3)+1,'Open','Acknowledged','In Progress') FROM seq;

INSERT INTO ai_recommendations (company_id, module_key, title, body, score, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 70)
SELECT 1,
ELT((n%31)+1,'command-center','control-tower','dispatch','jobs','route-planning','vehicles','drivers','assets','maintenance','work-orders','fuel-idling','safety','dashcam','compliance','hos-eld','dvir-inspections','customer-portal','customers','contracts-rates','carrier-management','expenses','documents','reports-analytics','sla-kpi','predictive-margin','audit-logs','ai-copilot','integrations','user-management','settings','billing'),
CONCAT('Recommended action ', n),
'OpsTrax AI recommends reviewing this operational signal and assigning the next best action.',
70 + (n % 28), 'Recommended'
FROM seq;

INSERT INTO command_center_actions (company_id, title, module_key, priority, status, due_at)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 12)
SELECT 1, CONCAT('Priority command action ', n), ELT((n%6)+1,'dispatch','maintenance','safety','compliance','fuel-idling','customer-portal'), ELT((n%4)+1,'Low','Medium','High','Critical'), ELT((n%3)+1,'Open','Acknowledged','In Progress'), DATE_ADD(NOW(), INTERVAL n HOUR) FROM seq;

INSERT INTO operational_events (company_id, entity_type, entity_id, event_type, title, severity)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 25)
SELECT 1, ELT((n%5)+1,'Vehicle','Driver','Job','Route','Customer'), n, ELT((n%7)+1,'location.updated','geofence.entered','job.delayed','vehicle.idle','safety.event','maintenance.warning','eta.sent'), CONCAT('Live operational event ', n), ELT((n%4)+1,'Info','Warning','High','Critical') FROM seq;

INSERT INTO audit_logs (company_id, actor_name, action_name, entity_name, entity_id, details_json)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, ELT((n%4)+1,'Avery Stone','Maya Patel','OpsTrax AI','System'), ELT((n%8)+1,'vehicle.updated','driver.assigned','job.status.changed','eta.sent','dispatch.recommendation.accepted','command.action.completed','settings.changed','user.login'), ELT((n%5)+1,'Vehicle','Driver','Job','Dispatch','Settings'), n, JSON_OBJECT('seeded', true) FROM seq;

INSERT INTO customer_communications (company_id, customer_id, job_id, channel, message, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, ((n-1)%8)+1, n, ELT((n%3)+1,'Email','SMS','Portal'), CONCAT('ETA and service update for job ', n), 'Sent' FROM seq;

INSERT INTO proof_of_delivery (company_id, job_id, receiver_name, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, ELT((n%5)+1,'R. Morgan','C. Rivera','D. Chen','M. Ahmed','S. Brooks'), 'Captured' FROM seq;

INSERT INTO dispatch_recommendations (company_id, job_id, vehicle_id, driver_id, recommendation, score, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, n, n, CONCAT('Assign best-fit vehicle/driver pair for job ', n, ' based on proximity, readiness, HOS and SLA risk.'), 88 + n, 'Recommended' FROM seq;

INSERT INTO eta_updates (company_id, job_id, message, channel, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 8)
SELECT 1, n, CONCAT('ETA update sent for JOB-',1000+n), 'Email/SMS', 'Sent' FROM seq;

INSERT INTO vehicle_documents (company_id, vehicle_id, document_type, document_name, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 24)
SELECT 1, ((n-1)%20)+1, ELT((n%4)+1,'Registration','Insurance','DOT Inspection','Camera Calibration'),
CONCAT('Vehicle document ', n), ELT((n%5)+1,'Active','Active','Expiring Soon','Review','Active'), DATE_ADD(CURDATE(), INTERVAL n*9 DAY)
FROM seq;

INSERT INTO driver_documents (company_id, driver_id, document_type, document_name, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 24)
SELECT 1, ((n-1)%20)+1, ELT((n%4)+1,'License','Medical Card','Background Check','Training Record'),
CONCAT('Driver document ', n), ELT((n%5)+1,'Active','Active','Expiring Soon','Review','Active'), DATE_ADD(CURDATE(), INTERVAL n*11 DAY)
FROM seq;

INSERT INTO customer_contacts (company_id, customer_id, full_name, title, email, phone, is_primary)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, ((n-1)%10)+1,
ELT((n%10)+1,'Nora Lane','Ethan Moore','Ivy Brooks','Liam Ford','Mina Cole','Sam Reed','Tara Wells','Chris Nguyen','Elena Ward','Victor James'),
ELT((n%4)+1,'Operations Lead','Transportation Manager','Warehouse Supervisor','Customer Success'),
CONCAT('contact', n, '@customer.example'), CONCAT('+1 703 555 ', LPAD(6000+n,4,'0')), n <= 10
FROM seq;

INSERT INTO customer_addresses (company_id, customer_id, address_type, address_line, city, state, postal_code)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, ((n-1)%10)+1, ELT((n%2)+1,'Billing','Shipping'),
ELT((n%7)+1,'9200 Lee Ave','1450 Duke St','45020 Aviation Dr','3100 Wilson Blvd','1200 Pennsylvania Ave NW','8300 Boone Blvd','7700 Richmond Hwy'),
ELT((n%7)+1,'Manassas','Alexandria','Dulles','Arlington','Washington','Tysons','Fairfax'), IF(n % 5 = 0, 'DC', 'VA'), CONCAT('20', LPAD(100+n,3,'0'))
FROM seq;

INSERT INTO asset_documents (company_id, asset_id, document_type, document_name, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 18)
SELECT 1, ((n-1)%12)+1, ELT((n%4)+1,'Registration','Inspection','Warranty','Calibration'),
CONCAT('Asset document ', n), ELT((n%5)+1,'Active','Active','Expiring Soon','Review','Active'), DATE_ADD(CURDATE(), INTERVAL n*13 DAY)
FROM seq;

INSERT INTO vehicle_assignments (company_id, vehicle_id, driver_id, assignment_type, status)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 20)
SELECT 1, n, n, ELT((n%3)+1,'Primary Driver','Relief Driver','Dispatch Pairing'), 'Active'
FROM seq;

INSERT INTO driver_certifications (company_id, driver_id, certification_type, certification_number, status, expiry_date)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 24)
SELECT 1, ((n-1)%20)+1, ELT((n%5)+1,'CDL','Hazmat','Medical Examiner Certificate','Defensive Driving','Cold Chain Handling'),
CONCAT('CERT-', LPAD(n,5,'0')), ELT((n%5)+1,'Valid','Valid','Expiring Soon','Review','Valid'), DATE_ADD(CURDATE(), INTERVAL n*14 DAY)
FROM seq;

INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 120)
SELECT 1,
ELT(((n-1)%4)+1,'Vehicle','Driver','Customer','Asset'),
((n-1)%IF(((n-1)%4)+1 IN (1,2),20,IF(((n-1)%4)+1=3,10,12)))+1,
ELT((n%7)+1,'created','updated','assigned','document.review','risk.changed','ai.recommendation','audit.logged'),
CONCAT('Batch 1 timeline event ', n),
'Seeded operational history for Batch 1 detail drawers across Northern Virginia/DC operations.',
ELT((n%4)+1,'Info','Warning','High','Info')
FROM seq;

INSERT INTO notifications (company_id, title, body, status) VALUES
(1,'OpsTrax AI Active','AI copilot is monitoring dispatch, cost, safety and compliance signals.','Unread'),
(1,'Live Simulation Running','Node event stream is available for control tower updates.','Unread');

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
ELT(((n-1)%31)+1,'command-center','control-tower','dispatch','jobs','route-planning','vehicles','drivers','assets','maintenance','work-orders','fuel-idling','safety','dashcam','compliance','hos-eld','dvir-inspections','customer-portal','customers','contracts-rates','carrier-management','expenses','documents','reports-analytics','sla-kpi','predictive-margin','audit-logs','ai-copilot','integrations','user-management','settings','billing'),
CONCAT('OpsTrax ', REPLACE(ELT(((n-1)%31)+1,'Command Center','Control Tower','Dispatch Board','Jobs & Orders','Route Planning','Vehicles','Drivers','Assets / Trailers / Equipment','Maintenance','Work Orders','Fuel & Idling','Safety','AI Dashcam / Incident Review','Compliance','HOS / ELD Framework','DVIR / Inspections','Customer ETA Portal','Clients / Customers','Contracts / Rates','Carrier Management','Expenses','Documents','Reports & Analytics','SLA / KPI Center','Predictive Cost & Margin','Audit Logs','OpsTrax AI Copilot','Integrations','User Management','Settings','Billing / Subscription'), '/', '-'), ' record ', n),
ELT((n%6)+1,'Open','Active','In Progress','At Risk','Completed','Review'),
ELT((n%6)+1,'Avery Stone','Maya Patel','Jordan Reyes','OpsTrax AI','Customer Success','Finance Ops'),
ELT((n%7)+1,'Manassas, VA','Woodbridge, VA','Alexandria, VA','Dulles, VA','Fairfax, VA','Arlington, VA','Washington DC'),
DATE_ADD(NOW(), INTERVAL n HOUR),
ELT((n%4)+1,'Low','Medium','High','Critical'),
ROUND(100 + n*37.5, 2),
JSON_OBJECT('seeded', true, 'index', n)
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

-- =====================================================================
-- RBAC: role_permissions mapping
-- =====================================================================
-- Super Admin (role_id=1): wildcard covers all
INSERT INTO role_permissions (role_id, permission_key) VALUES (1,'*');

-- Company Admin (role_id=2): full access
INSERT INTO role_permissions (role_id, permission_key) VALUES (2,'*');

-- Fleet Manager (role_id=3)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(3,'dashboard:view'),(3,'fleet:view'),(3,'fleet:manage'),(3,'maintenance:view'),
(3,'maintenance:manage'),(3,'telematics:view'),(3,'dispatch:view'),(3,'intelligence:view'),(3,'map:view');

-- Dispatcher (role_id=4)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(4,'dashboard:view'),(4,'dispatch:view'),(4,'dispatch:manage'),(4,'fleet:view'),
(4,'jobs:view'),(4,'jobs:manage'),(4,'map:view'),(4,'customers:view');

-- Driver (role_id=5)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(5,'driver:portal'),(5,'jobs:view'),(5,'dvir:manage');

-- Mechanic (role_id=6)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(6,'maintenance:view'),(6,'maintenance:manage'),(6,'dvir:review'),(6,'fleet:view');

-- Safety Manager (role_id=7)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(7,'dashboard:view'),(7,'safety:view'),(7,'safety:manage'),(7,'compliance:view'),
(7,'fleet:view'),(7,'telematics:view'),(7,'intelligence:view');

-- Compliance Manager (role_id=8)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(8,'dashboard:view'),(8,'compliance:view'),(8,'compliance:manage'),(8,'audit:view'),
(8,'fleet:view'),(8,'intelligence:view');

-- Customer Service (role_id=9)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(9,'customers:view'),(9,'customer-portal:view'),(9,'dispatch:view'),(9,'crm:view');

-- Customer Portal User (role_id=10)
INSERT INTO role_permissions (role_id, permission_key) VALUES (10,'customer-portal:view');

-- Reseller / Partner Admin (role_id=11)
INSERT INTO role_permissions (role_id, permission_key) VALUES (11,'*');

-- Read-only Auditor (role_id=12)
INSERT INTO role_permissions (role_id, permission_key) VALUES
(12,'audit:view'),(12,'fleet:view'),(12,'dashboard:view');
