-- OpsTrax PostgreSQL Schema
-- Converted from MySQL to PostgreSQL (timestamptz, GENERATED ALWAYS AS IDENTITY, JSONB, proper indexes)

CREATE TABLE IF NOT EXISTS companies (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_code VARCHAR(50) NOT NULL UNIQUE,
  name VARCHAR(200) NOT NULL,
  industry VARCHAR(120) NOT NULL,
  timezone VARCHAR(80) NOT NULL DEFAULT 'America/New_York',
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS roles (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  name VARCHAR(100) NOT NULL UNIQUE,
  permissions_json JSONB NULL
);

CREATE TABLE IF NOT EXISTS users (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  role_id BIGINT NULL,
  full_name VARCHAR(160) NOT NULL,
  email VARCHAR(220) NOT NULL UNIQUE,
  role_name VARCHAR(100) NOT NULL,
  demo_password VARCHAR(120),
  password_hash VARCHAR(255) NULL,
  permissions_json JSONB NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_users_company FOREIGN KEY (company_id) REFERENCES companies(id),
  CONSTRAINT fk_users_role FOREIGN KEY (role_id) REFERENCES roles(id)
);

CREATE TABLE IF NOT EXISTS drivers (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
  deleted_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (company_id, driver_code),
  CONSTRAINT fk_drivers_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE IF NOT EXISTS vehicles (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
  deleted_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (company_id, vehicle_code),
  CONSTRAINT fk_vehicles_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

ALTER TABLE drivers ADD CONSTRAINT fk_drivers_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id);
ALTER TABLE vehicles ADD CONSTRAINT fk_vehicles_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id);

CREATE TABLE IF NOT EXISTS customers (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
  deleted_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (company_id, customer_code),
  CONSTRAINT fk_customers_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE IF NOT EXISTS contracts (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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

CREATE TABLE IF NOT EXISTS assets (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
  deleted_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (company_id, asset_code),
  CONSTRAINT fk_assets_company FOREIGN KEY (company_id) REFERENCES companies(id),
  CONSTRAINT fk_assets_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_assets_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id),
  CONSTRAINT fk_assets_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE IF NOT EXISTS vehicle_documents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  expiry_date DATE NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_vehicle_documents_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);

CREATE TABLE IF NOT EXISTS driver_documents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  driver_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  expiry_date DATE NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_driver_documents_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS customer_contacts (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NOT NULL,
  full_name VARCHAR(160) NOT NULL,
  title VARCHAR(120) NULL,
  email VARCHAR(220) NULL,
  phone VARCHAR(50) NULL,
  is_primary BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_customer_contacts_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE IF NOT EXISTS customer_addresses (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NOT NULL,
  address_type VARCHAR(60) NOT NULL,
  address_line VARCHAR(300) NOT NULL,
  city VARCHAR(120) NULL,
  state VARCHAR(80) NULL,
  postal_code VARCHAR(30) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_customer_addresses_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE IF NOT EXISTS asset_documents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  asset_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  expiry_date DATE NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_asset_documents_asset FOREIGN KEY (asset_id) REFERENCES assets(id)
);

CREATE TABLE IF NOT EXISTS vehicle_assignments (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  driver_id BIGINT NULL,
  assignment_type VARCHAR(80) NOT NULL DEFAULT 'Primary Driver',
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_vehicle_assignments_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_vehicle_assignments_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS driver_certifications (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  driver_id BIGINT NOT NULL,
  certification_type VARCHAR(120) NOT NULL,
  certification_number VARCHAR(120) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Valid',
  expiry_date DATE NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_driver_certifications_driver FOREIGN KEY (driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS entity_timeline_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  entity_type VARCHAR(80) NOT NULL,
  entity_id BIGINT NOT NULL,
  event_type VARCHAR(120) NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Info',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_entity_timeline_lookup ON entity_timeline_events (entity_type, entity_id, created_at);

CREATE TABLE IF NOT EXISTS jobs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  job_code VARCHAR(60) NOT NULL,
  job_type VARCHAR(80) NOT NULL,
  pickup_address VARCHAR(300) NULL,
  dropoff_address VARCHAR(300) NULL,
  scheduled_start TIMESTAMPTZ NULL,
  scheduled_end TIMESTAMPTZ NULL,
  status VARCHAR(60) NOT NULL DEFAULT 'Unassigned',
  priority VARCHAR(40) NOT NULL DEFAULT 'Normal',
  assigned_vehicle_id BIGINT NULL,
  assigned_driver_id BIGINT NULL,
  sla_due_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (company_id, job_code),
  CONSTRAINT fk_jobs_company FOREIGN KEY (company_id) REFERENCES companies(id),
  CONSTRAINT fk_jobs_customer FOREIGN KEY (customer_id) REFERENCES customers(id),
  CONSTRAINT fk_jobs_vehicle FOREIGN KEY (assigned_vehicle_id) REFERENCES vehicles(id),
  CONSTRAINT fk_jobs_driver FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id)
);

CREATE TABLE IF NOT EXISTS routes (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  route_code VARCHAR(60) NOT NULL,
  name VARCHAR(180) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Planned',
  assigned_vehicle_id BIGINT NULL,
  assigned_driver_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  deleted_at TIMESTAMPTZ NULL,
  CONSTRAINT fk_routes_company FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE IF NOT EXISTS route_stops (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  route_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  stop_sequence INT NOT NULL,
  address VARCHAR(300) NOT NULL,
  lat DECIMAL(10,7) NULL,
  lng DECIMAL(10,7) NULL,
  eta TIMESTAMPTZ NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Pending',
  CONSTRAINT fk_route_stops_route FOREIGN KEY (route_id) REFERENCES routes(id)
);

CREATE TABLE IF NOT EXISTS dispatch_assignments (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  job_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  match_score DECIMAL(6,2) NOT NULL DEFAULT 90,
  status VARCHAR(50) NOT NULL DEFAULT 'Assigned',
  assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS trips (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'In Progress',
  started_at TIMESTAMPTZ NULL,
  completed_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS location_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
  event_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_location_company_time ON location_events (company_id, event_time);

CREATE TABLE IF NOT EXISTS geofences (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  name VARCHAR(160) NOT NULL,
  geofence_type VARCHAR(60) NOT NULL DEFAULT 'Circle',
  center_lat DECIMAL(10,7) NULL,
  center_lng DECIMAL(10,7) NULL,
  radius_meters INT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active'
);

CREATE TABLE IF NOT EXISTS geofence_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  geofence_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  event_type VARCHAR(80) NOT NULL,
  event_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS maintenance_items (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  title VARCHAR(220) NOT NULL,
  category VARCHAR(100) NOT NULL,
  due_date DATE NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  risk_level VARCHAR(50) NOT NULL DEFAULT 'Medium'
);

CREATE TABLE IF NOT EXISTS work_orders (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  work_order_code VARCHAR(60) NOT NULL,
  title VARCHAR(220) NOT NULL,
  priority VARCHAR(50) NOT NULL DEFAULT 'Normal',
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  due_date DATE NULL,
  estimated_cost DECIMAL(12,2) NULL
);

CREATE TABLE IF NOT EXISTS fuel_transactions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  gallons DECIMAL(10,2) NOT NULL,
  total_cost DECIMAL(12,2) NOT NULL,
  idle_minutes INT NOT NULL DEFAULT 0,
  fuel_station VARCHAR(180) NULL,
  transaction_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS safety_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  event_type VARCHAR(100) NOT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Low',
  description TEXT NULL,
  review_status VARCHAR(50) NOT NULL DEFAULT 'New',
  event_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS dashcam_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  safety_event_id BIGINT NULL,
  title VARCHAR(220) NOT NULL,
  severity VARCHAR(50) NOT NULL,
  coaching_status VARCHAR(60) NOT NULL DEFAULT 'Needs Review',
  event_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS compliance_documents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  related_entity_type VARCHAR(80) NOT NULL,
  related_entity_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_name VARCHAR(220) NOT NULL,
  expiry_date DATE NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Valid'
);

CREATE TABLE IF NOT EXISTS inspections (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  inspection_type VARCHAR(100) NOT NULL,
  result VARCHAR(50) NOT NULL DEFAULT 'Passed',
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS hos_logs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  driver_id BIGINT NOT NULL,
  log_date DATE NOT NULL,
  driving_hours DECIMAL(5,2) NOT NULL,
  on_duty_hours DECIMAL(5,2) NOT NULL,
  cycle_hours_left DECIMAL(5,2) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Compliant'
);

CREATE TABLE IF NOT EXISTS expenses (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  category VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  amount DECIMAL(12,2) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Approved',
  expense_date DATE NOT NULL
);

CREATE TABLE IF NOT EXISTS carriers (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  name VARCHAR(220) NOT NULL,
  mc_number VARCHAR(80) NULL,
  safety_rating VARCHAR(80) NOT NULL DEFAULT 'Satisfactory',
  status VARCHAR(50) NOT NULL DEFAULT 'Active'
);

CREATE TABLE IF NOT EXISTS documents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  title VARCHAR(220) NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  owner_name VARCHAR(160) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS sla_records (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  metric_name VARCHAR(120) NOT NULL,
  target_value DECIMAL(10,2) NOT NULL,
  actual_value DECIMAL(10,2) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'On Track'
);

CREATE TABLE IF NOT EXISTS kpi_records (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  metric_key VARCHAR(80) NOT NULL,
  label VARCHAR(160) NOT NULL,
  value_text VARCHAR(80) NOT NULL,
  trend VARCHAR(40) NOT NULL DEFAULT 'up',
  trend_value VARCHAR(40) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Healthy'
);

CREATE TABLE IF NOT EXISTS ai_insights (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  insight_type VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NOT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Info',
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_recommendations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  module_key VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NOT NULL,
  score DECIMAL(6,2) NOT NULL DEFAULT 80,
  status VARCHAR(50) NOT NULL DEFAULT 'Recommended'
);

CREATE TABLE IF NOT EXISTS notifications (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Unread',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS audit_logs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  actor_user_id BIGINT NULL,
  actor_name VARCHAR(160) NULL,
  action_name VARCHAR(160) NOT NULL,
  entity_name VARCHAR(100) NULL,
  entity_type VARCHAR(100) NULL,
  entity_id BIGINT NULL,
  details_json JSONB NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_audit_logs_tenant_time ON audit_logs (company_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_logs_entity ON audit_logs (entity_name, entity_id);

CREATE TABLE IF NOT EXISTS integrations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  provider_name VARCHAR(160) NOT NULL,
  category VARCHAR(100) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Connected'
);

CREATE TABLE IF NOT EXISTS subscription_plans (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  plan_name VARCHAR(160) NOT NULL,
  billing_status VARCHAR(60) NOT NULL DEFAULT 'Active',
  seats INT NOT NULL DEFAULT 25,
  monthly_amount DECIMAL(12,2) NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS command_center_actions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  title VARCHAR(220) NOT NULL,
  module_key VARCHAR(100) NOT NULL,
  priority VARCHAR(50) NOT NULL DEFAULT 'Medium',
  status VARCHAR(50) NOT NULL DEFAULT 'Open',
  due_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS operational_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  entity_type VARCHAR(100) NOT NULL,
  entity_id BIGINT NULL,
  event_type VARCHAR(120) NOT NULL,
  title VARCHAR(220) NOT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Info',
  event_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS customer_communications (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  job_id BIGINT NULL,
  channel VARCHAR(80) NOT NULL,
  message TEXT NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Sent',
  sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS proof_of_delivery (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  job_id BIGINT NOT NULL,
  receiver_name VARCHAR(160) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Captured',
  captured_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS dispatch_recommendations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  recommendation TEXT NOT NULL,
  score DECIMAL(6,2) NOT NULL DEFAULT 90,
  status VARCHAR(50) NOT NULL DEFAULT 'Recommended'
);

CREATE TABLE IF NOT EXISTS eta_updates (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  message TEXT NOT NULL,
  channel VARCHAR(80) NOT NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Queued',
  sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS module_records (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  module_key VARCHAR(100) NOT NULL,
  title VARCHAR(220) NOT NULL,
  status VARCHAR(60) NOT NULL DEFAULT 'Open',
  owner_name VARCHAR(160) NULL,
  location_name VARCHAR(180) NULL,
  due_at TIMESTAMPTZ NULL,
  risk_level VARCHAR(50) NOT NULL DEFAULT 'Medium',
  amount DECIMAL(12,2) NULL,
  metadata_json JSONB NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_module_key ON module_records (module_key);

-- Core entity indexes for common SaaS access patterns
CREATE INDEX IF NOT EXISTS ix_vehicles_tenant_status_risk ON vehicles (company_id, status, risk_score);
CREATE INDEX IF NOT EXISTS ix_vehicles_assigned_driver ON vehicles (assigned_driver_id);
CREATE INDEX IF NOT EXISTS ix_vehicles_deleted_at ON vehicles (deleted_at);
CREATE INDEX IF NOT EXISTS ix_drivers_tenant_status_risk ON drivers (company_id, status, risk_score);
CREATE INDEX IF NOT EXISTS ix_drivers_assigned_vehicle ON drivers (assigned_vehicle_id);
CREATE INDEX IF NOT EXISTS ix_drivers_deleted_at ON drivers (deleted_at);
CREATE INDEX IF NOT EXISTS ix_customers_tenant_status_risk ON customers (company_id, status, risk_score);
CREATE INDEX IF NOT EXISTS ix_customers_deleted_at ON customers (deleted_at);
CREATE INDEX IF NOT EXISTS ix_assets_tenant_status_risk ON assets (company_id, status, risk_score);
CREATE INDEX IF NOT EXISTS ix_assets_assigned_vehicle ON assets (assigned_vehicle_id);
CREATE INDEX IF NOT EXISTS ix_assets_assigned_driver ON assets (assigned_driver_id);
CREATE INDEX IF NOT EXISTS ix_assets_customer_type_deleted ON assets (customer_id, asset_type, deleted_at);
CREATE INDEX IF NOT EXISTS ix_vehicle_documents_vehicle_status ON vehicle_documents (vehicle_id, status);
CREATE INDEX IF NOT EXISTS ix_driver_documents_driver_status ON driver_documents (driver_id, status);
CREATE INDEX IF NOT EXISTS ix_asset_documents_asset_status ON asset_documents (asset_id, status);
CREATE INDEX IF NOT EXISTS ix_customer_contacts_customer ON customer_contacts (customer_id);
CREATE INDEX IF NOT EXISTS ix_customer_addresses_customer ON customer_addresses (customer_id);
CREATE INDEX IF NOT EXISTS ix_driver_certifications_driver_status ON driver_certifications (driver_id, status);
CREATE INDEX IF NOT EXISTS ix_jobs_tenant_status ON jobs (company_id, status, priority);
CREATE INDEX IF NOT EXISTS ix_jobs_assigned_vehicle ON jobs (assigned_vehicle_id);
CREATE INDEX IF NOT EXISTS ix_jobs_assigned_driver ON jobs (assigned_driver_id);

-- =====================================================================
-- RBAC: permissions, role_permissions, user_sessions
-- =====================================================================
CREATE TABLE IF NOT EXISTS permissions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  permission_key VARCHAR(100) NOT NULL UNIQUE,
  label VARCHAR(160) NOT NULL,
  module_group VARCHAR(100) NULL,
  description TEXT NULL
);

CREATE TABLE IF NOT EXISTS role_permissions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  role_id BIGINT NOT NULL,
  permission_key VARCHAR(100) NOT NULL,
  CONSTRAINT fk_rp_role FOREIGN KEY (role_id) REFERENCES roles(id),
  UNIQUE (role_id, permission_key)
);

CREATE TABLE IF NOT EXISTS user_sessions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  user_id BIGINT NOT NULL,
  company_id BIGINT NOT NULL,
  session_token VARCHAR(128) NOT NULL UNIQUE,
  expires_at TIMESTAMPTZ NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_sessions_user FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE INDEX IF NOT EXISTS ix_user_sessions_token ON user_sessions (session_token);
CREATE INDEX IF NOT EXISTS ix_user_sessions_user ON user_sessions (user_id);

-- =====================================================================
-- BATCH 2: Jobs/Dispatch/Routes enrichment tables
-- =====================================================================
CREATE TABLE IF NOT EXISTS job_status_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  job_id BIGINT NOT NULL,
  previous_status VARCHAR(60) NULL,
  new_status VARCHAR(60) NOT NULL,
  event_title VARCHAR(180) NOT NULL,
  event_description TEXT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_by_user_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS route_paths (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  route_id BIGINT NOT NULL,
  encoded_polyline TEXT NULL,
  total_distance_km DECIMAL(10,2) NOT NULL DEFAULT 0,
  total_duration_minutes INT NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS route_recommendations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  route_id BIGINT NULL,
  recommendation_type VARCHAR(120) NOT NULL,
  title VARCHAR(220) NOT NULL,
  body TEXT NOT NULL,
  score DECIMAL(6,2) NOT NULL DEFAULT 80,
  status VARCHAR(50) NOT NULL DEFAULT 'Recommended',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS customer_eta_links (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  job_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  tracking_code VARCHAR(80) NOT NULL UNIQUE,
  eta TIMESTAMPTZ NULL,
  status VARCHAR(60) NOT NULL DEFAULT 'Active',
  expires_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS customer_feedback (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  job_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  rating INT NOT NULL DEFAULT 5,
  comment TEXT NULL,
  feedback_type VARCHAR(80) NOT NULL DEFAULT 'Delivery',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =====================================================================
-- BATCH 3: Maintenance, DVIR, Documents
-- =====================================================================
CREATE TABLE IF NOT EXISTS maintenance_schedules (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  vehicle_id BIGINT NULL,
  asset_id BIGINT NULL,
  service_type VARCHAR(120) NOT NULL,
  trigger_type VARCHAR(80) NOT NULL DEFAULT 'Date',
  interval_miles INT NULL,
  interval_engine_hours INT NULL,
  interval_days INT NULL,
  last_service_date DATE NULL,
  last_service_odometer INT NULL,
  next_due_date DATE NULL,
  next_due_odometer INT NULL,
  next_due_engine_hours INT NULL,
  priority VARCHAR(40) NOT NULL DEFAULT 'Medium',
  status VARCHAR(60) NOT NULL DEFAULT 'Active',
  estimated_cost DECIMAL(12,2) NULL,
  vendor_id BIGINT NULL,
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS work_order_labor (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  work_order_id BIGINT NOT NULL,
  technician_name VARCHAR(160) NOT NULL,
  labor_hours DECIMAL(8,2) NOT NULL DEFAULT 0,
  labor_rate DECIMAL(10,2) NOT NULL DEFAULT 0,
  total_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS work_order_parts (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  work_order_id BIGINT NOT NULL,
  part_name VARCHAR(180) NOT NULL,
  part_number VARCHAR(100) NULL,
  quantity DECIMAL(8,2) NOT NULL DEFAULT 1,
  unit_cost DECIMAL(10,2) NOT NULL DEFAULT 0,
  total_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  status VARCHAR(80) NOT NULL DEFAULT 'Needed',
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS work_order_status_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  work_order_id BIGINT NOT NULL,
  previous_status VARCHAR(80) NULL,
  new_status VARCHAR(80) NOT NULL,
  event_title VARCHAR(180) NOT NULL,
  event_description TEXT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_by_user_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS dvir_reports (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  report_number VARCHAR(80) NOT NULL,
  driver_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  country_code VARCHAR(12) NULL,
  inspection_type VARCHAR(80) NOT NULL,
  inspection_status VARCHAR(80) NOT NULL DEFAULT 'Submitted',
  defects_found INT NOT NULL DEFAULT 0,
  safe_to_operate BOOLEAN NOT NULL DEFAULT TRUE,
  driver_signature_status VARCHAR(80) NOT NULL DEFAULT 'Pending',
  mechanic_review_status VARCHAR(80) NOT NULL DEFAULT 'Pending',
  repair_certification_status VARCHAR(80) NOT NULL DEFAULT 'Pending',
  submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  mechanic_reviewed_at TIMESTAMPTZ NULL,
  repair_certified_at TIMESTAMPTZ NULL,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 30,
  recommended_action VARCHAR(240) NULL,
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS dvir_defects (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  dvir_report_id BIGINT NOT NULL,
  defect_category VARCHAR(120) NOT NULL,
  defect_description TEXT NOT NULL,
  severity VARCHAR(40) NOT NULL DEFAULT 'Minor',
  status VARCHAR(80) NOT NULL DEFAULT 'Open',
  linked_work_order_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS dvir_templates (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  template_name VARCHAR(180) NOT NULL,
  country_code VARCHAR(12) NULL,
  vehicle_type VARCHAR(80) NULL,
  inspection_type VARCHAR(80) NOT NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS inspection_checklist_items (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  template_id BIGINT NOT NULL,
  item_label VARCHAR(220) NOT NULL,
  item_category VARCHAR(120) NOT NULL,
  required BOOLEAN NOT NULL DEFAULT TRUE,
  sort_order INT NOT NULL DEFAULT 1,
  status VARCHAR(80) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS document_timeline_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  document_id BIGINT NOT NULL,
  event_title VARCHAR(180) NOT NULL,
  event_description TEXT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =====================================================================
-- BATCH 4: Safety, Coaching, Incidents, Evidence
-- =====================================================================
CREATE TABLE IF NOT EXISTS coaching_tasks (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  task_number VARCHAR(80) NOT NULL,
  driver_id BIGINT NOT NULL,
  safety_event_id BIGINT NULL,
  dashcam_event_id BIGINT NULL,
  assigned_to_user_id BIGINT NULL,
  coaching_type VARCHAR(120) NOT NULL,
  priority VARCHAR(50) NOT NULL DEFAULT 'Medium',
  status VARCHAR(80) NOT NULL DEFAULT 'Draft',
  title VARCHAR(220) NOT NULL,
  description TEXT NULL,
  ai_script TEXT NULL,
  driver_acknowledged BOOLEAN NOT NULL DEFAULT FALSE,
  acknowledged_at TIMESTAMPTZ NULL,
  completed_at TIMESTAMPTZ NULL,
  before_safety_score DECIMAL(6,2) NULL,
  after_safety_score DECIMAL(6,2) NULL,
  effectiveness_score DECIMAL(6,2) NULL,
  due_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS coaching_notes (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  coaching_task_id BIGINT NOT NULL,
  note_type VARCHAR(80) NOT NULL DEFAULT 'Manager Note',
  note_text TEXT NOT NULL,
  created_by_user_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS incidents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  incident_number VARCHAR(80) NOT NULL,
  safety_event_id BIGINT NULL,
  dashcam_event_id BIGINT NULL,
  driver_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  job_id BIGINT NULL,
  route_id BIGINT NULL,
  incident_type VARCHAR(120) NOT NULL,
  severity VARCHAR(50) NOT NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'New',
  location_description VARCHAR(220) NULL,
  latitude DECIMAL(10,7) NULL,
  longitude DECIMAL(10,7) NULL,
  occurred_at TIMESTAMPTZ NULL,
  driver_statement TEXT NULL,
  witness_statement TEXT NULL,
  customer_statement TEXT NULL,
  ai_summary TEXT NULL,
  recommended_action VARCHAR(260) NULL,
  insurance_report_status VARCHAR(80) NOT NULL DEFAULT 'Not Created',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS incident_evidence (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  incident_id BIGINT NOT NULL,
  evidence_type VARCHAR(120) NOT NULL,
  evidence_title VARCHAR(220) NOT NULL,
  evidence_url VARCHAR(400) NULL,
  evidence_json JSONB NULL,
  source_entity_type VARCHAR(100) NULL,
  source_entity_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS evidence_packages (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  package_number VARCHAR(80) NOT NULL,
  incident_id BIGINT NULL,
  safety_event_id BIGINT NULL,
  dashcam_event_id BIGINT NULL,
  driver_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  job_id BIGINT NULL,
  package_type VARCHAR(120) NOT NULL DEFAULT 'Insurance Evidence',
  status VARCHAR(80) NOT NULL DEFAULT 'Draft',
  locked BOOLEAN NOT NULL DEFAULT FALSE,
  export_url VARCHAR(400) NULL,
  summary TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS evidence_package_items (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  package_id BIGINT NOT NULL,
  item_type VARCHAR(120) NOT NULL,
  item_title VARCHAR(220) NOT NULL,
  item_url VARCHAR(400) NULL,
  source_entity_type VARCHAR(100) NULL,
  source_entity_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS insurance_reports (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  incident_id BIGINT NOT NULL,
  report_number VARCHAR(80) NOT NULL,
  insurer_name VARCHAR(180) NULL,
  claim_number VARCHAR(120) NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'Draft',
  estimated_claim DECIMAL(12,2) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS driver_safety_scorecards (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  driver_id BIGINT NOT NULL,
  period_start DATE NOT NULL,
  period_end DATE NOT NULL,
  overall_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  speeding_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  harsh_braking_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  harsh_acceleration_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  phone_use_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  seatbelt_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  events_count INT NOT NULL DEFAULT 0,
  incidents_count INT NOT NULL DEFAULT 0,
  coaching_tasks_count INT NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS vehicle_safety_scorecards (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  vehicle_id BIGINT NOT NULL,
  period_start DATE NOT NULL,
  period_end DATE NOT NULL,
  overall_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  events_count INT NOT NULL DEFAULT 0,
  incidents_count INT NOT NULL DEFAULT 0,
  maintenance_risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS safety_trends (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  period_start DATE NOT NULL,
  period_end DATE NOT NULL,
  total_events INT NOT NULL DEFAULT 0,
  critical_events INT NOT NULL DEFAULT 0,
  coaching_completion_rate DECIMAL(6,2) NOT NULL DEFAULT 80,
  fleet_safety_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  trend_direction VARCHAR(40) NOT NULL DEFAULT 'Stable',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =====================================================================
-- BATCH 5: Finance, Fuel, Carriers, Cost Margin
-- =====================================================================
CREATE TABLE IF NOT EXISTS idling_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  event_number VARCHAR(80) NOT NULL,
  vehicle_id BIGINT NOT NULL,
  driver_id BIGINT NULL,
  job_id BIGINT NULL,
  route_id BIGINT NULL,
  location_description VARCHAR(220) NULL,
  started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  ended_at TIMESTAMPTZ NULL,
  duration_minutes DECIMAL(8,2) NOT NULL DEFAULT 0,
  estimated_fuel_burn DECIMAL(10,3) NOT NULL DEFAULT 0,
  estimated_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  currency VARCHAR(10) NOT NULL DEFAULT 'USD',
  status VARCHAR(80) NOT NULL DEFAULT 'Open',
  reviewed_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS fuel_anomalies (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  fuel_transaction_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  anomaly_type VARCHAR(120) NOT NULL,
  severity VARCHAR(50) NOT NULL DEFAULT 'Medium',
  description TEXT NULL,
  estimated_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
  status VARCHAR(80) NOT NULL DEFAULT 'Open',
  reviewed_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS expense_categories (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  category_name VARCHAR(120) NOT NULL,
  category_type VARCHAR(80) NOT NULL DEFAULT 'Operating',
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS contract_rates (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  contract_id BIGINT NOT NULL,
  rate_code VARCHAR(80) NOT NULL,
  rate_type VARCHAR(80) NOT NULL DEFAULT 'Per Mile',
  origin_zone VARCHAR(120) NULL,
  destination_zone VARCHAR(120) NULL,
  vehicle_type VARCHAR(80) NULL,
  base_rate DECIMAL(12,4) NOT NULL DEFAULT 0,
  minimum_charge DECIMAL(12,2) NULL,
  fuel_surcharge_percent DECIMAL(6,2) NULL,
  accessorial_type VARCHAR(120) NULL,
  effective_date DATE NULL,
  expiry_date DATE NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS carrier_documents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  carrier_id BIGINT NOT NULL,
  document_type VARCHAR(120) NOT NULL,
  document_number VARCHAR(120) NULL,
  expiry_date DATE NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Active',
  file_url VARCHAR(400) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS carrier_performance (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  carrier_id BIGINT NOT NULL,
  period_start DATE NOT NULL,
  period_end DATE NOT NULL,
  jobs_handled INT NOT NULL DEFAULT 0,
  on_time_percent DECIMAL(6,2) NOT NULL DEFAULT 90,
  incident_count INT NOT NULL DEFAULT 0,
  expense_total DECIMAL(12,2) NOT NULL DEFAULT 0,
  performance_score DECIMAL(6,2) NOT NULL DEFAULT 85,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS cost_margin_records (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  entity_type VARCHAR(80) NOT NULL,
  entity_id BIGINT NOT NULL,
  customer_id BIGINT NULL,
  job_id BIGINT NULL,
  route_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  revenue_estimate DECIMAL(12,2) NOT NULL DEFAULT 0,
  fuel_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  driver_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  maintenance_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  overhead_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  total_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  gross_margin DECIMAL(12,2) NOT NULL DEFAULT 0,
  gross_margin_percent DECIMAL(6,2) NOT NULL DEFAULT 0,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
  status VARCHAR(60) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS cost_margin_predictions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  entity_type VARCHAR(80) NOT NULL,
  entity_id BIGINT NOT NULL,
  prediction_type VARCHAR(80) NOT NULL DEFAULT 'Margin Forecast',
  predicted_revenue DECIMAL(12,2) NOT NULL DEFAULT 0,
  predicted_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
  predicted_margin DECIMAL(12,2) NOT NULL DEFAULT 0,
  predicted_margin_percent DECIMAL(6,2) NOT NULL DEFAULT 0,
  confidence_level VARCHAR(50) NOT NULL DEFAULT 'Medium',
  risk_level VARCHAR(50) NOT NULL DEFAULT 'Low',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS cost_leakage_items (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  leakage_number VARCHAR(80) NOT NULL,
  category VARCHAR(120) NOT NULL,
  entity_type VARCHAR(80) NULL,
  entity_id BIGINT NULL,
  title VARCHAR(220) NOT NULL,
  description TEXT NULL,
  estimated_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
  projected_monthly_loss DECIMAL(12,2) NOT NULL DEFAULT 0,
  severity VARCHAR(50) NOT NULL DEFAULT 'Medium',
  status VARCHAR(80) NOT NULL DEFAULT 'Open',
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 40,
  recommended_action VARCHAR(260) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS cost_leakage_actions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL DEFAULT 1,
  cost_leakage_item_id BIGINT NOT NULL,
  action_title VARCHAR(220) NOT NULL,
  action_description TEXT NULL,
  estimated_savings DECIMAL(12,2) NOT NULL DEFAULT 0,
  status VARCHAR(80) NOT NULL DEFAULT 'Open',
  assigned_to_user_id BIGINT NULL,
  due_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =====================================================================
-- BATCH 6: Compliance, HOS/ELD, Localization
-- =====================================================================
CREATE TABLE IF NOT EXISTS countries (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code VARCHAR(10) NOT NULL UNIQUE,
  name VARCHAR(200) NOT NULL,
  currency VARCHAR(10) NOT NULL DEFAULT 'USD',
  distance_unit VARCHAR(20) NOT NULL DEFAULT 'Miles',
  volume_unit VARCHAR(20) NOT NULL DEFAULT 'Gallons',
  status VARCHAR(40) NOT NULL DEFAULT 'Active'
);

CREATE TABLE IF NOT EXISTS languages (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code VARCHAR(10) NOT NULL UNIQUE,
  name VARCHAR(100) NOT NULL,
  native_name VARCHAR(100) NOT NULL,
  country_code VARCHAR(10) NULL,
  rtl BOOLEAN NOT NULL DEFAULT FALSE,
  status VARCHAR(40) NOT NULL DEFAULT 'Active'
);

CREATE TABLE IF NOT EXISTS tenant_locale_settings (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NULL,
  default_language VARCHAR(10) NOT NULL DEFAULT 'en-US',
  default_country VARCHAR(10) NOT NULL DEFAULT 'US',
  timezone VARCHAR(80) NOT NULL DEFAULT 'America/New_York',
  date_format VARCHAR(40) NOT NULL DEFAULT 'MM/DD/YYYY',
  currency VARCHAR(10) NOT NULL DEFAULT 'USD',
  distance_unit VARCHAR(20) NOT NULL DEFAULT 'Miles',
  volume_unit VARCHAR(20) NOT NULL DEFAULT 'Gallons',
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS user_locale_preferences (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  user_id BIGINT NULL,
  language VARCHAR(10) NOT NULL DEFAULT 'en-US',
  country_code VARCHAR(10) NULL,
  timezone VARCHAR(80) NULL,
  date_format VARCHAR(40) NULL,
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS compliance_profiles (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  country_code VARCHAR(10) NOT NULL,
  profile_name VARCHAR(200) NOT NULL,
  authority VARCHAR(200) NULL,
  hos_ruleset VARCHAR(80) NULL,
  eld_required BOOLEAN NOT NULL DEFAULT FALSE,
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS compliance_rules (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  profile_id BIGINT NOT NULL,
  rule_code VARCHAR(80) NOT NULL,
  rule_name VARCHAR(200) NOT NULL,
  category VARCHAR(80) NOT NULL DEFAULT 'HOS',
  description TEXT NULL,
  max_value DECIMAL(10,2) NULL,
  unit VARCHAR(40) NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Active'
);

CREATE TABLE IF NOT EXISTS driver_compliance_status (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  driver_id BIGINT NOT NULL,
  country_code VARCHAR(10) NOT NULL DEFAULT 'US',
  profile_id BIGINT NULL,
  overall_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
  license_valid BOOLEAN NOT NULL DEFAULT TRUE,
  medical_cert_valid BOOLEAN NOT NULL DEFAULT TRUE,
  hos_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
  violations_count INT NOT NULL DEFAULT 0,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS vehicle_compliance_status (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  vehicle_id BIGINT NOT NULL,
  country_code VARCHAR(10) NOT NULL DEFAULT 'US',
  profile_id BIGINT NULL,
  overall_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
  registration_valid BOOLEAN NOT NULL DEFAULT TRUE,
  inspection_valid BOOLEAN NOT NULL DEFAULT TRUE,
  eld_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
  violations_count INT NOT NULL DEFAULT 0,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS hos_clocks (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  driver_id BIGINT NOT NULL,
  country_code VARCHAR(10) NOT NULL DEFAULT 'US',
  profile_id BIGINT NULL,
  cycle_type VARCHAR(80) NOT NULL DEFAULT '70hr/8day',
  drive_time_remaining_minutes INT NOT NULL DEFAULT 660,
  shift_time_remaining_minutes INT NOT NULL DEFAULT 840,
  cycle_time_remaining_minutes INT NOT NULL DEFAULT 4200,
  duty_status VARCHAR(80) NOT NULL DEFAULT 'Off Duty',
  status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
  last_synced_at TIMESTAMPTZ NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS eld_devices (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial VARCHAR(120) NOT NULL UNIQUE,
  device_model VARCHAR(120) NULL,
  provider VARCHAR(120) NULL,
  vehicle_id BIGINT NULL,
  driver_id BIGINT NULL,
  firmware_version VARCHAR(80) NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'Active',
  last_heartbeat_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS compliance_violations (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  violation_code VARCHAR(80) NOT NULL,
  rule_id BIGINT NULL,
  profile_id BIGINT NULL,
  country_code VARCHAR(10) NOT NULL DEFAULT 'US',
  driver_id BIGINT NULL,
  vehicle_id BIGINT NULL,
  severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
  description TEXT NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'Open',
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  resolved_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS compliance_audit_packages (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  package_code VARCHAR(80) NOT NULL UNIQUE,
  country_code VARCHAR(10) NOT NULL DEFAULT 'US',
  profile_id BIGINT NULL,
  created_by VARCHAR(120) NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'Draft',
  date_from DATE NULL,
  date_to DATE NULL,
  export_url VARCHAR(400) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

-- =====================================================================
-- BATCH 7: Reports, KPIs, SLA, Executive, Audit
-- =====================================================================
CREATE TABLE IF NOT EXISTS report_catalog (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NULL,
  report_key VARCHAR(100) NOT NULL UNIQUE,
  report_name VARCHAR(220) NOT NULL,
  report_category VARCHAR(100) NOT NULL,
  description TEXT NULL,
  default_filters_json JSONB NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS report_runs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  report_key VARCHAR(100) NOT NULL,
  report_name VARCHAR(220) NOT NULL,
  filters_json JSONB NULL,
  run_by_user_id BIGINT NULL,
  run_by_name VARCHAR(160) NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Completed',
  row_count INT NULL,
  result_summary_json JSONB NULL,
  started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  completed_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS scheduled_reports (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  report_key VARCHAR(100) NOT NULL,
  report_name VARCHAR(220) NOT NULL,
  schedule_name VARCHAR(200) NOT NULL,
  frequency VARCHAR(40) NOT NULL DEFAULT 'Weekly',
  recipients_json JSONB NULL,
  filters_json JSONB NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  next_run_at TIMESTAMPTZ NULL,
  last_run_at TIMESTAMPTZ NULL,
  created_by_user_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS report_exports (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  report_run_id BIGINT NULL,
  report_key VARCHAR(100) NOT NULL,
  export_type VARCHAR(40) NOT NULL DEFAULT 'CSV',
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  export_url VARCHAR(500) NULL,
  requested_by_user_id BIGINT NULL,
  requested_by_name VARCHAR(160) NULL,
  requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  completed_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS kpi_metrics (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  kpi_code VARCHAR(80) NOT NULL,
  kpi_name VARCHAR(220) NOT NULL,
  category VARCHAR(80) NOT NULL,
  target_value DECIMAL(12,4) NULL,
  actual_value DECIMAL(12,4) NOT NULL DEFAULT 0,
  unit VARCHAR(40) NOT NULL DEFAULT '%',
  trend VARCHAR(20) NOT NULL DEFAULT 'stable',
  status VARCHAR(40) NOT NULL DEFAULT 'On Target',
  owner_role VARCHAR(80) NULL,
  recommendation TEXT NULL,
  last_calculated_at TIMESTAMPTZ NULL DEFAULT NOW(),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS kpi_targets (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  kpi_code VARCHAR(80) NOT NULL,
  target_value DECIMAL(12,4) NOT NULL,
  unit VARCHAR(40) NOT NULL DEFAULT '%',
  effective_date DATE NOT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS sla_breaches (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  sla_record_id BIGINT NOT NULL,
  breach_type VARCHAR(80) NOT NULL DEFAULT 'Delivery Delay',
  severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
  description TEXT NULL,
  root_cause_placeholder VARCHAR(200) NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Open',
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  resolved_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS executive_snapshots (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  snapshot_date DATE NOT NULL,
  operations_health_score DECIMAL(5,2) NOT NULL DEFAULT 80,
  cost_health_score DECIMAL(5,2) NOT NULL DEFAULT 75,
  safety_health_score DECIMAL(5,2) NOT NULL DEFAULT 85,
  compliance_health_score DECIMAL(5,2) NOT NULL DEFAULT 88,
  customer_sla_score DECIMAL(5,2) NOT NULL DEFAULT 82,
  fleet_readiness_score DECIMAL(5,2) NOT NULL DEFAULT 79,
  dispatch_readiness_score DECIMAL(5,2) NOT NULL DEFAULT 86,
  top_risks_json JSONB NULL,
  top_savings_json JSONB NULL,
  ai_brief TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS audit_export_requests (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL DEFAULT 1,
  requested_by_user_id BIGINT NULL,
  requested_by_name VARCHAR(160) NULL,
  date_from DATE NULL,
  date_to DATE NULL,
  filters_json JSONB NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Pending',
  export_url VARCHAR(500) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  completed_at TIMESTAMPTZ NULL
);

-- =====================================================================
-- Object Storage Metadata (no binary data in PostgreSQL)
-- =====================================================================
CREATE TABLE IF NOT EXISTS file_storage_metadata (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL,
  owner_type VARCHAR(80) NOT NULL,
  owner_id BIGINT NOT NULL,
  bucket VARCHAR(220) NOT NULL,
  object_key VARCHAR(500) NOT NULL,
  file_name VARCHAR(500) NOT NULL,
  mime_type VARCHAR(120) NOT NULL DEFAULT 'application/octet-stream',
  size_bytes BIGINT NOT NULL DEFAULT 0,
  checksum VARCHAR(128) NULL,
  uploaded_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  deleted_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_file_storage_tenant ON file_storage_metadata (tenant_id, owner_type, owner_id);
CREATE INDEX IF NOT EXISTS ix_file_storage_object_key ON file_storage_metadata (bucket, object_key);
