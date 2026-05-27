USE opstrax;

INSERT INTO tenants (tenant_code, name, industry, timezone)
VALUES ('KK-DEMO', 'Kode Kinetics Demo Fleet', 'Transport & Field Operations', 'America/New_York')
ON DUPLICATE KEY UPDATE name = VALUES(name);

SET @tenant_id = (SELECT id FROM tenants WHERE tenant_code = 'KK-DEMO');

INSERT INTO users (tenant_id, full_name, email, role_name) VALUES
(@tenant_id, 'Zack Khan', 'zack@kodekinetics.com', 'Owner'),
(@tenant_id, 'Aisha Morgan', 'dispatcher@opstrax.ai', 'Dispatcher'),
(@tenant_id, 'Ryan Brooks', 'fleetmanager@opstrax.ai', 'Fleet Manager')
ON DUPLICATE KEY UPDATE role_name = VALUES(role_name);

INSERT INTO drivers (tenant_id, driver_code, full_name, phone, email, license_number, license_expiry, safety_score, status) VALUES
(@tenant_id, 'DRV-001', 'Omar Ali', '+1 571 430 5333', 'omar@example.com', 'VA-D001', DATE_ADD(CURDATE(), INTERVAL 180 DAY), 94.0, 'On Route'),
(@tenant_id, 'DRV-002', 'Sara Mitchell', '+1 571 430 5334', 'sara@example.com', 'VA-D002', DATE_ADD(CURDATE(), INTERVAL 365 DAY), 89.0, 'At Stop'),
(@tenant_id, 'DRV-003', 'Daniel Cruz', '+1 571 430 5335', 'daniel@example.com', 'VA-D003', DATE_ADD(CURDATE(), INTERVAL 90 DAY), 82.0, 'Delayed'),
(@tenant_id, 'DRV-004', 'Hassan Khan', '+1 571 430 5336', 'hassan@example.com', 'VA-D004', DATE_ADD(CURDATE(), INTERVAL 14 DAY), 76.0, 'Idle')
ON DUPLICATE KEY UPDATE full_name = VALUES(full_name), safety_score = VALUES(safety_score), status = VALUES(status);

INSERT INTO vehicles (tenant_id, vehicle_code, type, make, model, year, vin, plate_number, odometer_miles, status, assigned_driver_id) VALUES
(@tenant_id, 'TRK-104', 'Truck', 'Freightliner', 'M2', 2022, 'VINOPSTRAX104', 'VA-104', 48210, 'On Route', (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-001')),
(@tenant_id, 'VAN-218', 'Van', 'Ford', 'Transit', 2023, 'VINOPSTRAX218', 'VA-218', 18840, 'At Stop', (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-002')),
(@tenant_id, 'BOX-331', 'Box Truck', 'Isuzu', 'NPR', 2021, 'VINOPSTRAX331', 'VA-331', 63750, 'Delayed', (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-003')),
(@tenant_id, 'TRK-117', 'Truck', 'International', 'MV', 2020, 'VINOPSTRAX117', 'VA-117', 91330, 'Idle', (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-004'))
ON DUPLICATE KEY UPDATE status = VALUES(status), assigned_driver_id = VALUES(assigned_driver_id);

INSERT INTO assets (tenant_id, asset_code, asset_type, name, status, current_location, assigned_vehicle_id) VALUES
(@tenant_id, 'TRL-501', 'Trailer', 'Dry Van Trailer 501', 'Assigned', 'Manassas, VA', (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-104')),
(@tenant_id, 'GEN-220', 'Equipment', 'Portable Generator 220', 'Available', 'Warehouse A', NULL),
(@tenant_id, 'REEFER-77', 'Reefer', 'Temperature Controlled Unit 77', 'Assigned', 'Alexandria, VA', (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='VAN-218'))
ON DUPLICATE KEY UPDATE status = VALUES(status);

INSERT INTO jobs (tenant_id, job_code, customer_name, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, status, priority, assigned_vehicle_id, assigned_driver_id) VALUES
(@tenant_id, 'JOB-1001', 'Prince William Logistics', 'Delivery', 'Manassas, VA', 'Alexandria, VA', NOW(), DATE_ADD(NOW(), INTERVAL 2 HOUR), 'In Progress', 'High', (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-104'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-001')),
(@tenant_id, 'JOB-1002', 'Northern VA Medical Supply', 'Delivery', 'Woodbridge, VA', 'Fairfax, VA', NOW(), DATE_ADD(NOW(), INTERVAL 3 HOUR), 'At Risk', 'High', (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='BOX-331'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-003')),
(@tenant_id, 'JOB-1003', 'Dulles Field Services', 'Service Call', 'Dulles, VA', 'Reston, VA', NOW(), DATE_ADD(NOW(), INTERVAL 4 HOUR), 'Scheduled', 'Normal', (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='VAN-218'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-002'))
ON DUPLICATE KEY UPDATE status = VALUES(status);

INSERT INTO maintenance_work_orders (tenant_id, vehicle_id, work_order_code, title, priority, status, due_date, estimated_cost) VALUES
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='VAN-218'), 'WO-2001', 'Preventive maintenance due', 'Normal', 'Open', DATE_ADD(CURDATE(), INTERVAL 7 DAY), 425.00),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='BOX-331'), 'WO-2002', 'Brake inspection required', 'Critical', 'Open', DATE_ADD(CURDATE(), INTERVAL 2 DAY), 875.00),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-117'), 'WO-2003', 'Idle diagnostics review', 'High', 'Open', DATE_ADD(CURDATE(), INTERVAL 4 DAY), 250.00)
ON DUPLICATE KEY UPDATE status = VALUES(status), estimated_cost = VALUES(estimated_cost);

INSERT INTO location_events (tenant_id, vehicle_id, driver_id, vehicle_code, driver_code, lat, lng, speed_mph, heading, event_type) VALUES
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-104'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-001'), 'TRK-104', 'DRV-001', 38.7509, -77.4753, 57, 92, 'LOCATION'),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='VAN-218'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-002'), 'VAN-218', 'DRV-002', 38.8048, -77.0469, 0, 10, 'ARRIVED'),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='BOX-331'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-003'), 'BOX-331', 'DRV-003', 38.6270, -77.3683, 23, 111, 'DELAYED'),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-117'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-004'), 'TRK-117', 'DRV-004', 38.6582, -77.2497, 0, 0, 'IDLE');

INSERT INTO safety_events (tenant_id, vehicle_id, driver_id, event_type, severity, description, review_status) VALUES
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='BOX-331'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-003'), 'Harsh Braking', 'Medium', 'Harsh braking detected near Manassas route corridor.', 'New'),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-117'), (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-004'), 'Extended Idle', 'High', 'Vehicle idling outside approved staging window.', 'New');

INSERT INTO fuel_transactions (tenant_id, vehicle_id, gallons, total_cost, fuel_station) VALUES
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-104'), 42.5, 164.48, 'Shell Manassas'),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='BOX-331'), 38.2, 147.88, 'Exxon Woodbridge'),
(@tenant_id, (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='TRK-117'), 44.1, 170.63, 'BP Fairfax');

INSERT INTO compliance_documents (tenant_id, related_entity_type, related_entity_id, document_type, document_name, expiry_date, status) VALUES
(@tenant_id, 'Driver', (SELECT id FROM drivers WHERE tenant_id=@tenant_id AND driver_code='DRV-004'), 'Driver Certification', 'Hassan Khan Certification', DATE_ADD(CURDATE(), INTERVAL 14 DAY), 'Expiring Soon'),
(@tenant_id, 'Vehicle', (SELECT id FROM vehicles WHERE tenant_id=@tenant_id AND vehicle_code='BOX-331'), 'Vehicle Inspection', 'BOX-331 Annual Inspection', DATE_ADD(CURDATE(), INTERVAL 45 DAY), 'Valid');

INSERT INTO ai_insights (tenant_id, insight_type, title, body, severity) VALUES
(@tenant_id, 'Delay Risk', 'Route C delivery risk detected', 'Three jobs are at risk of missing customer delivery windows. Route C is showing repeat delays near I-95 between 4-6 PM.', 'Warning'),
(@tenant_id, 'Fuel Leakage', 'Idle cost abnormality detected', 'TRK-117 alone accounts for approximately $86 in avoidable idle cost this week. Review staging time and driver waiting pattern.', 'High'),
(@tenant_id, 'Maintenance Advisor', 'Preventive maintenance recommended', 'VAN-218 and BOX-331 should be scheduled for preventive maintenance before next Friday.', 'Info');
