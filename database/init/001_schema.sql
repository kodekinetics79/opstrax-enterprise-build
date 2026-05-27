CREATE DATABASE IF NOT EXISTS opstrax CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE opstrax;

CREATE TABLE companies (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_code VARCHAR(50) NOT NULL UNIQUE,
  name VARCHAR(200) NOT NULL,
  industry VARCHAR(120) NOT NULL,
  timezone VARCHAR(80) NOT NULL DEFAULT 'America/New_York',
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE roles (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  name VARCHAR(100) NOT NULL UNIQUE,
  permissions_json JSON NULL
);

CREATE TABLE users (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  role_id BIGINT NULL,
  full_name VARCHAR(160) NOT NULL,
  email VARCHAR(220) NOT NULL UNIQUE,
  role_name VARCHAR(100) NOT NULL,
  demo_password VARCHAR(120) NOT NULL DEFAULT 'Admin@12345',
  permissions_json JSON NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_users_company FOREIGN KEY (company_id) REFERENCES companies(id),
  CONSTRAINT fk_users_role FOREIGN KEY (role_id) REFERENCES roles(id)
);

CREATE TABLE drivers (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  driver_code VARCHAR(50) NOT NULL,
  full_name VARCHAR(160) NOT NULL,
  phone VARCHAR(50) NULL,
  email VARCHAR(220) NULL,
  license_number VARCHAR(100) NULL,
  license_expiry DATE NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Available',
  safety_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  readiness_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
  compliance_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  assigned_vehicle_id BIGINT NULL,
  deleted_at TIMESTAMP NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_driver_company_code (company_id, driver_code),
  CONSTRAINT fk_drivers_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE vehicles (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_code VARCHAR(50) NOT NULL,
  type VARCHAR(80) NOT NULL,
  make VARCHAR(100) NULL,
  model VARCHAR(100) NULL,
  year INT NULL,
  vin VARCHAR(120) NULL,
  plate_number VARCHAR(50) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Available',
  odometer_miles DECIMAL(12,2) NOT NULL DEFAULT 0,
  readiness_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  data_quality_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
  device_status VARCHAR(60) NOT NULL DEFAULT 'Online',
  camera_status VARCHAR(60) NOT NULL DEFAULT 'Online',
  assigned_driver_id BIGINT NULL,
  deleted_at TIMESTAMP NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_vehicle_company_code (company_id, vehicle_code),
  CONSTRAINT fk_vehicles_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

ALTER TABLE drivers ADD CONSTRAINT fk_drivers_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id);
ALTER TABLE vehicles ADD CONSTRAINT fk_vehicles_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id);

CREATE TABLE customers (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  customer_code VARCHAR(50) NOT NULL,
  name VARCHAR(220) NOT NULL,
  contact_name VARCHAR(160) NULL,
  email VARCHAR(220) NULL,
  phone VARCHAR(50) NULL,
  billing_address VARCHAR(300) NULL,
  shipping_address VARCHAR(300) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  sla_tier VARCHAR(50) NOT NULL DEFAULT 'Standard',
  sla_health_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  delivery_experience_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
  deleted_at TIMESTAMP NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_customer_company_code (company_id, customer_code),
  CONSTRAINT fk_customers_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE contracts (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NOT NULL,
  contract_code VARCHAR(80) NOT NULL,
  title VARCHAR(220) NOT NULL,
  rate_type VARCHAR(80) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  effective_date DATE NULL,
  expiration_date DATE NULL,
  CONSTRAINT fk_contracts_company FOREIGN KEY (company_id) REFERENCES companies(id),
  CONSTRAINT fk_contracts_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE assets (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  asset_code VARCHAR(50) NOT NULL,
  asset_type VARCHAR(80) NOT NULL,
  name VARCHAR(180) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Available',
  current_location VARCHAR(220) NULL,
  assigned_vehicle_id BIGINT NULL,
  assigned_driver_id BIGINT NULL,
  customer_id BIGINT NULL,
  current_zone VARCHAR(160) NULL,
  geofence_status VARCHAR(80) NOT NULL DEFAULT 'Inside authorized zone',
  utilization_score DECIMAL(6,2) NOT NULL DEFAULT 80,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
  deleted_at TIMESTAMP NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_asset_company_code (company_id, asset_code),
  CONSTRAINT fk_assets_company FOREIGN KEY (company_id) REFERENCES companies(id),
  CONSTRAINT fk_assets_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_assets_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id),
  CONSTRAINT fk_assets_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE vehicle_documents (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  expiry_date DATE NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_vehicle_documents_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);

CREATE TABLE driver_documents (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  driver_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  expiry_date DATE NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_driver_documents_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE customer_contacts (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NOT NULL,
  full_name VARCHAR(160) NOT NULL,
  title VARCHAR(120) NULL,
  email VARCHAR(220) NULL,
  phone VARCHAR(50) NULL,
  is_primary BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_customer_contacts_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE customer_addresses (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NOT NULL,
  address_type VARCHAR(60) NOT NULL,
  address_line VARCHAR(300) NOT NULL,
  city VARCHAR(120) NULL,
  state VARCHAR(80) NULL,
  postal_code VARCHAR(30) NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_customer_addresses_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE asset_documents (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  asset_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  expiry_date DATE NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_asset_documents_asset FOREIGN KEY (asset_id) REFERENCES assets(id)
);

CREATE TABLE vehicle_assignments (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  driver_id BIGINT NULL,
  assignment_type VARCHAR(80) NOT NULL DEFAULT 'Primary Driver',
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  assigned_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_vehicle_assignments_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_vehicle_assignments_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE driver_certifications (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  driver_id BIGINT NOT NULL,
  certification_type VARCHAR(120) NOT NULL,
  certification_number VARCHAR(120) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Valid',
  expiry_date DATE NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_driver_certifications_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE entity_timeline_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  entity_type VARCHAR(80) NOT NULL,
  entity_id BIGINT NOT NULL,
  event_type VARCHAR(120) NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Info',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_entity_timeline_lookup (entity_type, entity_id, created_at)
);

CREATE TABLE jobs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  job_code VARCHAR(60) NOT NULL,
  job_type VARCHAR(80) NOT NULL,
  pickup_address VARCHAR(300) NULL,
  dropoff_address VARCHAR(300) NULL,
  scheduled_start DATETIME NULL,
  scheduled_end DATETIME NULL,
  status VARCHAR(60) NOT NULL DEFAULT 'Unassigned',
  priority VARCHAR(40) NOT NULL DEFAULT 'Normal',
  assigned_vehicle_id BIGINT NULL,
  assigned_driver_id BIGINT NULL,
  sla_due_at DATETIME NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_job_company_code (company_id, job_code),
  CONSTRAINT fk_jobs_company FOREIGN KEY (company_id) REFERENCES companies(id),
  CONSTRAINT fk_jobs_customer FOREIGN KEY (customer_id) REFERENCES customers(id),
  CONSTRAINT fk_jobs_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_jobs_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id)
);

CREATE TABLE routes (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  route_code VARCHAR(60) NOT NULL,
  name VARCHAR(180) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Planned',
  assigned_vehicle_id BIGINT NULL,
  assigned_driver_id BIGINT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_routes_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE route_stops (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  route_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  stop_sequence INT NOT NULL,
  address VARCHAR(300) NOT NULL,
  lat DECIMAL(10,7) NULL,
  lng DECIMAL(10,7) NULL,
  eta DATETIME NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Pending',
  CONSTRAINT fk_route_stops_route FOREIGN KEY (route_id) REFERENCES routes(id)
);

CREATE TABLE dispatch_assignments (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  job_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  match_score DECIMAL(6,2) NOT NULL DEFAULT 90,
  status VARCHAR(50) NOT NULL DEFAULT 'Assigned',
  assigned_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE trips (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'In Progress',
  started_at DATETIME NULL,
  completed_at DATETIME NULL
);

CREATE TABLE location_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  vehicle_code VARCHAR(50) NULL,
  driver_code VARCHAR(50) NULL,
  lat DECIMAL(10,7) NOT NULL,
  lng DECIMAL(10,7) NOT NULL,
  speed_mph DECIMAL(8,2) NOT NULL DEFAULT 0,
  heading DECIMAL(8,2) NULL,
  event_type VARCHAR(80) NOT NULL DEFAULT 'location.updated',
  event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_location_company_time (company_id, event_time)
);

CREATE TABLE geofences (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  name VARCHAR(160) NOT NULL,
  geofence_type VARCHAR(60) NOT NULL DEFAULT 'Circle',
  center_lat DECIMAL(10,7) NULL,
  center_lng DECIMAL(10,7) NULL,
  radius_meters INT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active'
);

CREATE TABLE geofence_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  geofence_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  event_type VARCHAR(80) NOT NULL,
  event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE maintenance_items (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  title VARCHAR(220) NOT NULL,
  category VARCHAR(100) NOT NULL,
  due_date DATE NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  risk_level VARCHAR(50) NOT NULL DEFAULT 'Medium'
);

CREATE TABLE work_orders (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  work_order_code VARCHAR(60) NOT NULL,
  title VARCHAR(220) NOT NULL,
  priority VARCHAR(50) NOT NULL DEFAULT 'Normal',
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  due_date DATE NULL,
  estimated_cost DECIMAL(12,2) NULL
);

CREATE TABLE fuel_transactions (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  gallons DECIMAL(10,2) NOT NULL,
  total_cost DECIMAL(12,2) NOT NULL,
  idle_minutes INT NOT NULL DEFAULT 0,
  fuel_station VARCHAR(180) NULL,
  transaction_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE safety_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  event_type VARCHAR(100) NOT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Low',
  description TEXT NULL,
  review_status VARCHAR(50) NOT NULL DEFAULT 'New',
  event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE dashcam_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  safety_event_id BIGINT NULL,
  title VARCHAR(220) NOT NULL,
  severity VARCHAR(50) NOT NULL,
  coaching_status VARCHAR(60) NOT NULL DEFAULT 'Needs Review',
  event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE compliance_documents (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  related_entity_type VARCHAR(80) NOT NULL,
  related_entity_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  expiry_date DATE NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Valid'
);

CREATE TABLE inspections (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  inspection_type VARCHAR(100) NOT NULL,
  result VARCHAR(50) NOT NULL DEFAULT 'Passed',
  notes TEXT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE hos_logs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  driver_id BIGINT NOT NULL,
  log_date DATE NOT NULL,
  driving_hours DECIMAL(5,2) NOT NULL,
  on_duty_hours DECIMAL(5,2) NOT NULL,
  cycle_hours_left DECIMAL(5,2) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Compliant'
);

CREATE TABLE expenses (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  category VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  amount DECIMAL(12,2) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Approved',
  expense_date DATE NOT NULL
);

CREATE TABLE carriers (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  name VARCHAR(220) NOT NULL,
  mc_number VARCHAR(80) NULL,
  safety_rating VARCHAR(80) NOT NULL DEFAULT 'Satisfactory',
  status VARCHAR(50) NOT NULL DEFAULT 'Active'
);

CREATE TABLE documents (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  title VARCHAR(220) NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  owner_name VARCHAR(160) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  uploaded_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE sla_records (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  metric_name VARCHAR(120) NOT NULL,
  target_value DECIMAL(10,2) NOT NULL,
  actual_value DECIMAL(10,2) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'On Track'
);

CREATE TABLE kpi_records (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  metric_key VARCHAR(80) NOT NULL,
  label VARCHAR(160) NOT NULL,
  value_text VARCHAR(80) NOT NULL,
  trend VARCHAR(40) NOT NULL DEFAULT 'up',
  trend_value VARCHAR(40) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Healthy'
);

CREATE TABLE ai_insights (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  insight_type VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NOT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Info',
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE ai_recommendations (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  module_key VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NOT NULL,
  score DECIMAL(6,2) NOT NULL DEFAULT 80,
  status VARCHAR(50) NOT NULL DEFAULT 'Recommended'
);

CREATE TABLE notifications (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Unread',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE audit_logs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  actor_user_id BIGINT NULL,
  actor_name VARCHAR(160) NULL,
  action_name VARCHAR(160) NOT NULL,
  entity_name VARCHAR(100) NULL,
  entity_id BIGINT NULL,
  details_json JSON NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE integrations (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  provider_name VARCHAR(160) NOT NULL,
  category VARCHAR(100) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Connected'
);

CREATE TABLE subscription_plans (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  plan_name VARCHAR(160) NOT NULL,
  billing_status VARCHAR(60) NOT NULL DEFAULT 'Active',
  seats INT NOT NULL DEFAULT 25,
  monthly_amount DECIMAL(12,2) NOT NULL DEFAULT 0
);

CREATE TABLE command_center_actions (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  title VARCHAR(220) NOT NULL,
  module_key VARCHAR(100) NOT NULL,
  priority VARCHAR(50) NOT NULL DEFAULT 'Medium',
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  due_at DATETIME NULL
);

CREATE TABLE operational_events (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  entity_type VARCHAR(100) NOT NULL,
  entity_id BIGINT NULL,
  event_type VARCHAR(120) NOT NULL,
  title VARCHAR(220) NOT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Info',
  event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE customer_communications (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  job_id BIGINT NULL,
  channel VARCHAR(80) NOT NULL,
  message TEXT NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Sent',
  sent_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE proof_of_delivery (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  job_id BIGINT NOT NULL,
  receiver_name VARCHAR(160) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Captured',
  captured_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE dispatch_recommendations (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  recommendation TEXT NOT NULL,
  score DECIMAL(6,2) NOT NULL DEFAULT 90,
  status VARCHAR(50) NOT NULL DEFAULT 'Recommended'
);

CREATE TABLE eta_updates (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  message TEXT NOT NULL,
  channel VARCHAR(80) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Queued',
  sent_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE module_records (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  module_key VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  status VARCHAR(60) NOT NULL DEFAULT 'Open',
  owner_name VARCHAR(160) NULL,
  location_name VARCHAR(180) NULL,
  due_at DATETIME NULL,
  risk_level VARCHAR(50) NOT NULL DEFAULT 'Medium',
  amount DECIMAL(12,2) NULL,
  metadata_json JSON NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_module_key (module_key)
);

CREATE INDEX ix_vehicles_tenant_status_risk ON vehicles(company_id, status, risk_score);
CREATE INDEX ix_vehicles_assigned_driver ON vehicles(assigned_driver_id);
CREATE INDEX ix_vehicles_deleted_at ON vehicles(deleted_at);
CREATE INDEX ix_drivers_tenant_status_risk ON drivers(company_id, status, risk_score);
CREATE INDEX ix_drivers_assigned_vehicle ON drivers(assigned_vehicle_id);
CREATE INDEX ix_drivers_deleted_at ON drivers(deleted_at);
CREATE INDEX ix_customers_tenant_status_risk ON customers(company_id, status, risk_score);
CREATE INDEX ix_customers_deleted_at ON customers(deleted_at);
CREATE INDEX ix_assets_tenant_status_risk ON assets(company_id, status, risk_score);
CREATE INDEX ix_assets_assigned_vehicle ON assets(assigned_vehicle_id);
CREATE INDEX ix_assets_assigned_driver ON assets(assigned_driver_id);
CREATE INDEX ix_assets_customer_type_deleted ON assets(customer_id, asset_type, deleted_at);
CREATE INDEX ix_vehicle_documents_vehicle_status ON vehicle_documents(vehicle_id, status);
CREATE INDEX ix_driver_documents_driver_status ON driver_documents(driver_id, status);
CREATE INDEX ix_asset_documents_asset_status ON asset_documents(asset_id, status);
CREATE INDEX ix_customer_contacts_customer ON customer_contacts(customer_id);
CREATE INDEX ix_customer_addresses_customer ON customer_addresses(customer_id);
CREATE INDEX ix_driver_certifications_driver_status ON driver_certifications(driver_id, status);
