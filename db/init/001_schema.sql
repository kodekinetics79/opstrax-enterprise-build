CREATE DATABASE IF NOT EXISTS opstrax CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE opstrax;

CREATE TABLE IF NOT EXISTS tenants (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_code VARCHAR(50) NOT NULL UNIQUE,
  name VARCHAR(200) NOT NULL,
  industry VARCHAR(100) NULL,
  timezone VARCHAR(80) NOT NULL DEFAULT 'America/New_York',
  status VARCHAR(30) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS users (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  full_name VARCHAR(150) NOT NULL,
  email VARCHAR(200) NOT NULL,
  role_name VARCHAR(80) NOT NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_users_tenant_email (tenant_id, email),
  CONSTRAINT fk_users_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS drivers (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  driver_code VARCHAR(50) NOT NULL,
  full_name VARCHAR(150) NOT NULL,
  phone VARCHAR(40) NULL,
  email VARCHAR(200) NULL,
  license_number VARCHAR(100) NULL,
  license_expiry DATE NULL,
  safety_score DECIMAL(5,2) NOT NULL DEFAULT 100,
  status VARCHAR(30) NOT NULL DEFAULT 'Available',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_drivers_code (tenant_id, driver_code),
  CONSTRAINT fk_drivers_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS vehicles (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  vehicle_code VARCHAR(50) NOT NULL,
  type VARCHAR(80) NOT NULL,
  make VARCHAR(100) NULL,
  model VARCHAR(100) NULL,
  year INT NULL,
  vin VARCHAR(100) NULL,
  plate_number VARCHAR(50) NULL,
  odometer_miles DECIMAL(12,2) NOT NULL DEFAULT 0,
  status VARCHAR(30) NOT NULL DEFAULT 'Active',
  assigned_driver_id BIGINT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_vehicles_code (tenant_id, vehicle_code),
  CONSTRAINT fk_vehicles_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_vehicles_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS assets (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  asset_code VARCHAR(50) NOT NULL,
  asset_type VARCHAR(80) NOT NULL,
  name VARCHAR(150) NOT NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'Available',
  current_location VARCHAR(200) NULL,
  assigned_vehicle_id BIGINT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_assets_code (tenant_id, asset_code),
  CONSTRAINT fk_assets_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_assets_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id)
);

CREATE TABLE IF NOT EXISTS jobs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  job_code VARCHAR(50) NOT NULL,
  customer_name VARCHAR(200) NOT NULL,
  job_type VARCHAR(80) NOT NULL,
  pickup_address VARCHAR(300) NULL,
  dropoff_address VARCHAR(300) NULL,
  scheduled_start DATETIME NULL,
  scheduled_end DATETIME NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Scheduled',
  priority VARCHAR(30) NOT NULL DEFAULT 'Normal',
  assigned_vehicle_id BIGINT NULL,
  assigned_driver_id BIGINT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_jobs_code (tenant_id, job_code),
  CONSTRAINT fk_jobs_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_jobs_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_jobs_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS routes (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  route_code VARCHAR(50) NOT NULL,
  name VARCHAR(150) NOT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Planned',
  assigned_vehicle_id BIGINT NULL,
  assigned_driver_id BIGINT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_routes_code (tenant_id, route_code),
  CONSTRAINT fk_routes_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_routes_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_routes_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS route_stops (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  route_id BIGINT NOT NULL,
  stop_sequence INT NOT NULL,
  job_id BIGINT NULL,
  address VARCHAR(300) NOT NULL,
  lat DECIMAL(10,7) NULL,
  lng DECIMAL(10,7) NULL,
  eta DATETIME NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  CONSTRAINT fk_stops_route FOREIGN KEY (route_id) REFERENCES routes(id),
  CONSTRAINT fk_stops_job FOREIGN KEY (job_id) REFERENCES jobs(id)
);

CREATE TABLE IF NOT EXISTS location_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  vehicle_code VARCHAR(50) NULL,
  driver_code VARCHAR(50) NULL,
  lat DECIMAL(10,7) NOT NULL,
  lng DECIMAL(10,7) NOT NULL,
  speed_mph DECIMAL(8,2) NOT NULL DEFAULT 0,
  heading DECIMAL(8,2) NULL,
  event_type VARCHAR(60) NOT NULL DEFAULT 'LOCATION',
  event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_location_vehicle_time (vehicle_id, event_time),
  INDEX ix_location_tenant_time (tenant_id, event_time),
  CONSTRAINT fk_location_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_location_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_location_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS geofences (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  name VARCHAR(150) NOT NULL,
  geofence_type VARCHAR(50) NOT NULL DEFAULT 'Circle',
  center_lat DECIMAL(10,7) NULL,
  center_lng DECIMAL(10,7) NULL,
  radius_meters INT NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'Active',
  CONSTRAINT fk_geofences_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS maintenance_work_orders (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  work_order_code VARCHAR(50) NOT NULL,
  title VARCHAR(200) NOT NULL,
  priority VARCHAR(30) NOT NULL DEFAULT 'Normal',
  status VARCHAR(40) NOT NULL DEFAULT 'Open',
  due_date DATE NULL,
  estimated_cost DECIMAL(12,2) NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_work_order_code (tenant_id, work_order_code),
  CONSTRAINT fk_workorders_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_workorders_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);

CREATE TABLE IF NOT EXISTS inspections (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  driver_id BIGINT NULL,
  inspection_type VARCHAR(80) NOT NULL,
  result VARCHAR(40) NOT NULL DEFAULT 'Passed',
  notes TEXT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_inspections_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_inspections_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_inspections_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS safety_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  event_type VARCHAR(80) NOT NULL,
  severity VARCHAR(40) NOT NULL DEFAULT 'Low',
  description TEXT NULL,
  event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  review_status VARCHAR(40) NOT NULL DEFAULT 'New',
  CONSTRAINT fk_safety_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_safety_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_safety_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS fuel_transactions (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  gallons DECIMAL(10,2) NOT NULL,
  total_cost DECIMAL(12,2) NOT NULL,
  fuel_station VARCHAR(150) NULL,
  transaction_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_fuel_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_fuel_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);

CREATE TABLE IF NOT EXISTS compliance_documents (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  related_entity_type VARCHAR(50) NOT NULL,
  related_entity_id BIGINT NOT NULL,
  document_type VARCHAR(100) NOT NULL,
  document_name VARCHAR(200) NOT NULL,
  expiry_date DATE NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Valid',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_compliance_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS ai_insights (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  insight_type VARCHAR(80) NOT NULL,
  title VARCHAR(200) NOT NULL,
  body TEXT NOT NULL,
  severity VARCHAR(40) NOT NULL DEFAULT 'Info',
  status VARCHAR(40) NOT NULL DEFAULT 'Open',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_ai_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS alert_rules (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  rule_name VARCHAR(150) NOT NULL,
  module_name VARCHAR(80) NOT NULL,
  condition_json JSON NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_rules_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS audit_logs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  tenant_id BIGINT NOT NULL,
  actor_user_id BIGINT NULL,
  action_name VARCHAR(150) NOT NULL,
  entity_name VARCHAR(100) NULL,
  entity_id BIGINT NULL,
  details_json JSON NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_audit_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_audit_user FOREIGN KEY (actor_user_id) REFERENCES users(id)
);
