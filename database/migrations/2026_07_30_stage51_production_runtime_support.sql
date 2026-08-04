-- Stage 51 — production runtime support for Fleet process workers
-- Owner-only, additive/idempotent, and fail-fast under psql ON_ERROR_STOP=1.
-- Scope is deliberately narrow: observability/heartbeat, notification escalation,
-- and scheduled-report contracts used by hosted services in the API process.

BEGIN;

CREATE TABLE IF NOT EXISTS service_run_history (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  service_name VARCHAR(100) NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'running'
    CHECK (status IN ('running','succeeded','failed','degraded')),
  started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  finished_at TIMESTAMPTZ NULL,
  duration_ms INT NULL,
  processed_count INT NOT NULL DEFAULT 0,
  failed_count INT NOT NULL DEFAULT 0,
  error_code VARCHAR(100) NULL,
  error_message_safe TEXT NULL,
  next_run_at TIMESTAMPTZ NULL,
  heartbeat_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS idx_srh_service ON service_run_history(service_name);
CREATE INDEX IF NOT EXISTS idx_srh_started ON service_run_history(started_at);
CREATE INDEX IF NOT EXISTS idx_srh_status ON service_run_history(status);

CREATE TABLE IF NOT EXISTS service_heartbeats (
  service_name VARCHAR(100) PRIMARY KEY,
  last_heartbeat_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_run_at TIMESTAMPTZ NULL,
  last_run_status VARCHAR(50) NULL,
  consecutive_failures INT NOT NULL DEFAULT 0,
  last_error_safe TEXT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS platform_incidents (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NULL,
  severity VARCHAR(20) NOT NULL DEFAULT 'medium'
    CHECK (severity IN ('critical','high','medium','low','info')),
  source_service VARCHAR(100) NOT NULL,
  source_event VARCHAR(200) NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'open'
    CHECK (status IN ('open','investigating','mitigated','resolved')),
  title VARCHAR(500) NOT NULL,
  safe_description TEXT NULL,
  opened_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  resolved_at TIMESTAMPTZ NULL,
  assigned_to VARCHAR(200) NULL,
  acknowledged_at TIMESTAMPTZ NULL,
  acknowledged_by VARCHAR(200) NULL,
  affected_service VARCHAR(100) NULL,
  affected_tenants TEXT NULL,
  root_cause TEXT NULL,
  actions_taken TEXT NULL,
  trace_id VARCHAR(64) NULL,
  deployment_version VARCHAR(100) NULL
);
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS acknowledged_at TIMESTAMPTZ NULL;
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS acknowledged_by VARCHAR(200) NULL;
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS affected_service VARCHAR(100) NULL;
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS affected_tenants TEXT NULL;
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS root_cause TEXT NULL;
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS actions_taken TEXT NULL;
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS trace_id VARCHAR(64) NULL;
ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS deployment_version VARCHAR(100) NULL;
CREATE INDEX IF NOT EXISTS idx_pi_status ON platform_incidents(status);
CREATE INDEX IF NOT EXISTS idx_pi_service ON platform_incidents(source_service);
CREATE INDEX IF NOT EXISTS idx_pi_opened ON platform_incidents(opened_at);

-- Base init contains a legacy notification shape. Reconcile every column used by
-- NotificationService/EscalationBackgroundService without deleting legacy data.
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS event_type VARCHAR(120) NOT NULL DEFAULT 'system';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS source_type VARCHAR(80) NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS source_id BIGINT NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS severity VARCHAR(40) NOT NULL DEFAULT 'Medium';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS message TEXT NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS audience_type VARCHAR(80) NOT NULL DEFAULT 'dispatcher';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS channel VARCHAR(40) NOT NULL DEFAULT 'in_app';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS dedupe_key VARCHAR(255) NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS priority INT NOT NULL DEFAULT 5;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS delivered_at TIMESTAMPTZ NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS read_at TIMESTAMPTZ NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS acknowledged_at TIMESTAMPTZ NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS acknowledged_by BIGINT NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS acknowledgement_note TEXT NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS escalated_from BIGINT NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

CREATE TABLE IF NOT EXISTS notification_recipients (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  notification_id BIGINT NOT NULL,
  company_id BIGINT NOT NULL,
  user_id BIGINT NULL,
  driver_id BIGINT NULL,
  role_target VARCHAR(80) NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'unread',
  delivered_at TIMESTAMPTZ NULL,
  read_at TIMESTAMPTZ NULL,
  acknowledged_at TIMESTAMPTZ NULL,
  channel VARCHAR(40) NOT NULL DEFAULT 'in_app',
  external_ref VARCHAR(255) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
CREATE TABLE IF NOT EXISTS escalation_rules (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  rule_name VARCHAR(200) NOT NULL,
  event_type VARCHAR(120) NOT NULL,
  severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
  initial_audience VARCHAR(80) NOT NULL,
  escalation_audience VARCHAR(80) NOT NULL,
  time_to_escalate_minutes INT NOT NULL DEFAULT 30,
  repeat_interval_minutes INT NOT NULL DEFAULT 60,
  max_repeats INT NOT NULL DEFAULT 3,
  enabled BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS idx_notifications_company ON notifications(company_id);
CREATE INDEX IF NOT EXISTS idx_notifications_event_type ON notifications(event_type);
CREATE INDEX IF NOT EXISTS idx_notifications_dedupe ON notifications(company_id,dedupe_key,status);
CREATE INDEX IF NOT EXISTS idx_notifications_status ON notifications(company_id,status,created_at);
CREATE INDEX IF NOT EXISTS idx_notification_recipients_notif ON notification_recipients(notification_id);
CREATE INDEX IF NOT EXISTS idx_notification_recipients_user ON notification_recipients(user_id,company_id);
CREATE INDEX IF NOT EXISTS idx_notification_recipients_driver ON notification_recipients(driver_id);
CREATE INDEX IF NOT EXISTS idx_escalation_rules_company ON escalation_rules(company_id);
CREATE INDEX IF NOT EXISTS idx_escalation_rules_event ON escalation_rules(event_type);

CREATE TABLE IF NOT EXISTS saved_reports (
  id BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  owner_user_id BIGINT NOT NULL,
  name VARCHAR(220) NOT NULL,
  description TEXT NULL,
  dataset_key VARCHAR(100) NOT NULL,
  selected_fields_json JSONB NOT NULL,
  filters_json JSONB NULL,
  sort_json JSONB NULL,
  group_by_json JSONB NULL,
  visibility VARCHAR(40) NOT NULL DEFAULT 'private',
  shared_role VARCHAR(80) NULL,
  last_run_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
CREATE TABLE IF NOT EXISTS report_execution_log (
  id BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  user_id BIGINT NULL,
  dataset_key VARCHAR(100) NOT NULL,
  saved_report_id BIGINT NULL,
  row_count INT NULL,
  execution_ms INT NULL,
  export_format VARCHAR(20) NULL,
  filters_json JSONB NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'completed',
  error_message TEXT NULL,
  executed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS scheduled_report_deliveries (
  id BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  scheduled_report_id BIGINT NOT NULL,
  company_id BIGINT NOT NULL,
  execution_log_id BIGINT NULL,
  recipient_count INT NOT NULL DEFAULT 0,
  delivery_method VARCHAR(40) NOT NULL DEFAULT 'in_app',
  status VARCHAR(40) NOT NULL DEFAULT 'pending',
  error_message TEXT NULL,
  scheduled_for TIMESTAMPTZ NULL,
  delivered_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE scheduled_reports ADD COLUMN IF NOT EXISTS saved_report_id BIGINT NULL;
ALTER TABLE scheduled_reports ADD COLUMN IF NOT EXISTS owner_user_id BIGINT NULL;
ALTER TABLE scheduled_reports ADD COLUMN IF NOT EXISTS format VARCHAR(20) NOT NULL DEFAULT 'csv';
ALTER TABLE scheduled_reports ADD COLUMN IF NOT EXISTS last_status VARCHAR(40) NULL;
ALTER TABLE scheduled_reports ADD COLUMN IF NOT EXISTS last_error TEXT NULL;
ALTER TABLE scheduled_reports ADD COLUMN IF NOT EXISTS recipient_type VARCHAR(40) NOT NULL DEFAULT 'users';
ALTER TABLE scheduled_reports ADD COLUMN IF NOT EXISTS delivery_method VARCHAR(40) NOT NULL DEFAULT 'in_app';
CREATE INDEX IF NOT EXISTS idx_sr_company_owner ON saved_reports(company_id,owner_user_id);
CREATE INDEX IF NOT EXISTS idx_sr_visibility ON saved_reports(company_id,visibility);
CREATE INDEX IF NOT EXISTS idx_sr_dataset ON saved_reports(company_id,dataset_key);
CREATE INDEX IF NOT EXISTS idx_rel_company ON report_execution_log(company_id);
CREATE INDEX IF NOT EXISTS idx_rel_dataset ON report_execution_log(company_id,dataset_key);
CREATE INDEX IF NOT EXISTS idx_srd_scheduled ON scheduled_report_deliveries(scheduled_report_id);

-- Hosted workers run in the API process in Production, while startup schema
-- services are intentionally disabled for the restricted runtime role. Reconcile
-- the exact worker-facing Fleet contracts here so a clean migrated database does
-- not start "ready" and then fail every background cycle.
ALTER TABLE routes ADD COLUMN IF NOT EXISTS route_name VARCHAR(180) NULL;
ALTER TABLE routes ADD COLUMN IF NOT EXISTS planned_start TIMESTAMPTZ NULL;
ALTER TABLE routes ADD COLUMN IF NOT EXISTS planned_end TIMESTAMPTZ NULL;
ALTER TABLE routes ADD COLUMN IF NOT EXISTS estimated_distance DECIMAL(10,2) NOT NULL DEFAULT 0;
ALTER TABLE routes ADD COLUMN IF NOT EXISTS estimated_duration_minutes INT NOT NULL DEFAULT 0;

ALTER TABLE route_stops ADD COLUMN IF NOT EXISTS company_id BIGINT NULL;
ALTER TABLE route_stops ALTER COLUMN company_id DROP DEFAULT;
UPDATE route_stops rs SET company_id=r.company_id
FROM routes r
WHERE rs.route_id=r.id AND rs.company_id IS DISTINCT FROM r.company_id;
DO $route_stop_scope$
BEGIN
  IF EXISTS (SELECT 1 FROM route_stops WHERE company_id IS NULL) THEN
    RAISE EXCEPTION 'Stage 51 cannot resolve route_stops.company_id from parent routes';
  END IF;
END
$route_stop_scope$;
ALTER TABLE route_stops ALTER COLUMN company_id SET NOT NULL;
ALTER TABLE route_stops ADD COLUMN IF NOT EXISTS latitude DECIMAL(10,7) NULL;
ALTER TABLE route_stops ADD COLUMN IF NOT EXISTS longitude DECIMAL(10,7) NULL;
ALTER TABLE route_stops ADD COLUMN IF NOT EXISTS time_window_start TIMESTAMPTZ NULL;
ALTER TABLE route_stops ADD COLUMN IF NOT EXISTS time_window_end TIMESTAMPTZ NULL;

ALTER TABLE trips ADD COLUMN IF NOT EXISTS route_id BIGINT NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS trip_ref VARCHAR(60) NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS planned_start_time TIMESTAMPTZ NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS actual_start_time TIMESTAMPTZ NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS planned_end_time TIMESTAMPTZ NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS actual_end_time TIMESTAMPTZ NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS origin TEXT NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS destination TEXT NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS planned_distance_miles DECIMAL(10,2) NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS actual_distance_miles DECIMAL(10,2) NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS planned_duration_minutes INT NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS actual_duration_minutes INT NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS total_planned_stops INT NOT NULL DEFAULT 0;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS stops_completed INT NOT NULL DEFAULT 0;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS stops_on_time INT NOT NULL DEFAULT 0;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS start_delay_minutes INT NOT NULL DEFAULT 0;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS max_telemetry_gap_minutes INT NOT NULL DEFAULT 0;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS speeding_events_count INT NOT NULL DEFAULT 0;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS route_compliance_score DECIMAL(5,2) NULL DEFAULT 100;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS compliance_breakdown_json JSONB NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE trips ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

CREATE TABLE IF NOT EXISTS trip_stops (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  trip_id BIGINT NOT NULL,
  route_stop_id BIGINT NULL,
  stop_sequence INT NOT NULL DEFAULT 0,
  stop_type VARCHAR(60) NOT NULL DEFAULT 'Delivery',
  address VARCHAR(200) NULL,
  lat DECIMAL(10,7) NULL,
  lng DECIMAL(10,7) NULL,
  planned_arrival_time TIMESTAMPTZ NULL,
  planned_departure_time TIMESTAMPTZ NULL,
  actual_arrival_time TIMESTAMPTZ NULL,
  actual_departure_time TIMESTAMPTZ NULL,
  time_window_start TIMESTAMPTZ NULL,
  time_window_end TIMESTAMPTZ NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'pending',
  arrival_delay_minutes INT NOT NULL DEFAULT 0,
  deviation_flagged BOOLEAN NOT NULL DEFAULT false,
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS trip_id BIGINT NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS trip_sequence INT NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS device_id BIGINT NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS odometer_miles DECIMAL(12,2) NULL;
ALTER TABLE location_events ADD COLUMN IF NOT EXISTS received_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
CREATE INDEX IF NOT EXISTS idx_trips_company_status ON trips(company_id,status);
CREATE INDEX IF NOT EXISTS idx_trips_vehicle ON trips(vehicle_id,company_id,actual_start_time);
CREATE INDEX IF NOT EXISTS idx_trips_route ON trips(route_id,company_id);
CREATE INDEX IF NOT EXISTS idx_trip_stops_trip ON trip_stops(trip_id,stop_sequence);
CREATE INDEX IF NOT EXISTS idx_le_trip ON location_events(trip_id,trip_sequence);

CREATE TABLE IF NOT EXISTS latest_vehicle_positions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  device_id BIGINT NULL,
  driver_id BIGINT NULL,
  lat DECIMAL(10,7) NOT NULL,
  lng DECIMAL(10,7) NOT NULL,
  speed_mph DECIMAL(6,2) NOT NULL DEFAULT 0,
  heading SMALLINT NOT NULL DEFAULT 0,
  accuracy_meters DECIMAL(8,2) NULL,
  engine_status VARCHAR(40) NULL,
  fuel_level DECIMAL(6,2) NULL,
  odometer_miles DECIMAL(12,2) NULL,
  battery_voltage DECIMAL(6,2) NULL,
  event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  event_count BIGINT NOT NULL DEFAULT 1,
  UNIQUE(company_id,vehicle_id)
);
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS telemetry_status VARCHAR(40) NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS risk_level VARCHAR(40) NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS alert_count INT NOT NULL DEFAULT 0;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS open_alert_count INT NOT NULL DEFAULT 0;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS next_action VARCHAR(160) NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS summary_json JSONB NULL;
ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

CREATE TABLE IF NOT EXISTS telemetry_alerts (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NULL,
  device_id BIGINT NULL,
  driver_id BIGINT NULL,
  alert_type VARCHAR(60) NOT NULL,
  severity VARCHAR(40) NOT NULL DEFAULT 'Warning',
  message TEXT NOT NULL,
  source_event_id BIGINT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'Open',
  acknowledged_at TIMESTAMPTZ NULL,
  acknowledged_by VARCHAR(120) NULL,
  resolved_at TIMESTAMPTZ NULL,
  resolved_by VARCHAR(120) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
ALTER TABLE telemetry_alerts ADD COLUMN IF NOT EXISTS ai_recommendation_id BIGINT NULL;
CREATE TABLE IF NOT EXISTS telemetry_rules (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  rule_type VARCHAR(60) NOT NULL,
  threshold_value DECIMAL(12,4) NOT NULL DEFAULT 65,
  severity VARCHAR(40) NOT NULL DEFAULT 'High',
  enabled BOOLEAN NOT NULL DEFAULT true,
  notes TEXT NULL,
  created_by BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  UNIQUE(company_id,rule_type)
);
CREATE TABLE IF NOT EXISTS telemetry_nonces (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_id BIGINT NOT NULL,
  nonce VARCHAR(128) NOT NULL,
  used_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(device_id,nonce)
);
CREATE TABLE IF NOT EXISTS gps_gateway_replay (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  gateway_id VARCHAR(120) NOT NULL DEFAULT 'default',
  signature VARCHAR(256) NOT NULL,
  signed_at TIMESTAMPTZ NOT NULL,
  device_id BIGINT NULL,
  company_id BIGINT NULL,
  received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(gateway_id,signature)
);
CREATE INDEX IF NOT EXISTS idx_ta_company_status ON telemetry_alerts(company_id,status);
CREATE INDEX IF NOT EXISTS idx_lvp_tenant ON latest_vehicle_positions(company_id,received_at);
CREATE INDEX IF NOT EXISTS idx_tr_company ON telemetry_rules(company_id,rule_type,enabled);
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS polygon_json JSONB NULL;

-- SafetyBackgroundService consumes telemetry and persists derived safety state.
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS device_id BIGINT NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS source_telemetry_alert_id BIGINT NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS source_location_event_id BIGINT NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS score_impact DECIMAL(6,2) NOT NULL DEFAULT 15;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS status VARCHAR(40) NOT NULL DEFAULT 'open';
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS reviewed_by BIGINT NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMPTZ NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS resolved_by BIGINT NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS resolved_at TIMESTAMPTZ NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS notes TEXT NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS evidence_hash VARCHAR(64) NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS meta_json JSONB NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS risk_score DECIMAL(6,2) NOT NULL DEFAULT 35;
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_se_telemetry_alert ON safety_events(source_telemetry_alert_id)
  WHERE source_telemetry_alert_id IS NOT NULL;
CREATE TABLE IF NOT EXISTS driver_safety_scores (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  driver_id BIGINT NOT NULL,
  score_7d DECIMAL(5,2) NOT NULL DEFAULT 100,
  score_30d DECIMAL(5,2) NOT NULL DEFAULT 100,
  score_90d DECIMAL(5,2) NOT NULL DEFAULT 100,
  events_7d INT NOT NULL DEFAULT 0,
  events_30d INT NOT NULL DEFAULT 0,
  events_90d INT NOT NULL DEFAULT 0,
  breakdown_json JSONB NULL,
  computed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(company_id,driver_id)
);
CREATE TABLE IF NOT EXISTS telemetry_live_asset_states (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_id BIGINT NOT NULL,
  device_id BIGINT NULL,
  driver_id BIGINT NULL,
  vehicle_code VARCHAR(60) NULL,
  device_serial VARCHAR(120) NULL,
  driver_name VARCHAR(160) NULL,
  lat DECIMAL(10,7) NOT NULL,
  lng DECIMAL(10,7) NOT NULL,
  speed_mph DECIMAL(6,2) NOT NULL DEFAULT 0,
  heading SMALLINT NOT NULL DEFAULT 0,
  engine_status VARCHAR(40) NULL,
  telemetry_status VARCHAR(40) NOT NULL DEFAULT 'healthy',
  risk_level VARCHAR(40) NOT NULL DEFAULT 'low',
  alert_count INT NOT NULL DEFAULT 0,
  open_alert_count INT NOT NULL DEFAULT 0,
  stale_seconds BIGINT NOT NULL DEFAULT 0,
  last_event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  source_event_id BIGINT NULL,
  correlation_id VARCHAR(120) NULL,
  causation_id VARCHAR(120) NULL,
  source_channel VARCHAR(40) NULL,
  next_action VARCHAR(160) NULL,
  summary_json JSONB NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(company_id,vehicle_id)
);
CREATE TABLE IF NOT EXISTS fleet_health_snapshots (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  scope_type VARCHAR(40) NOT NULL DEFAULT 'company',
  scope_value VARCHAR(120) NOT NULL DEFAULT 'company',
  snapshot_date DATE NOT NULL,
  fleet_health_score DECIMAL(6,2) NOT NULL DEFAULT 0,
  safety_score DECIMAL(6,2) NOT NULL DEFAULT 0,
  maintenance_score DECIMAL(6,2) NOT NULL DEFAULT 0,
  telemetry_score DECIMAL(6,2) NOT NULL DEFAULT 0,
  risk_level VARCHAR(40) NOT NULL DEFAULT 'medium',
  reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
  next_action VARCHAR(160) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  UNIQUE(company_id,scope_type,scope_value,snapshot_date)
);
CREATE INDEX IF NOT EXISTS idx_fhs_company_date ON fleet_health_snapshots(company_id,snapshot_date DESC);
ALTER TABLE evidence_package_items ADD COLUMN IF NOT EXISTS evidence_package_id BIGINT NULL;
UPDATE evidence_package_items
SET evidence_package_id=package_id
WHERE evidence_package_id IS NULL AND package_id IS NOT NULL;
DO $evidence_parent$
BEGIN
  IF EXISTS (SELECT 1 FROM evidence_package_items WHERE evidence_package_id IS NULL) THEN
    RAISE EXCEPTION 'Stage 51 cannot resolve evidence_package_items.evidence_package_id from legacy package_id';
  END IF;
END
$evidence_parent$;
ALTER TABLE evidence_package_items ALTER COLUMN evidence_package_id SET NOT NULL;
ALTER TABLE vehicle_safety_scorecards ADD COLUMN IF NOT EXISTS safety_score DECIMAL(6,2) NOT NULL DEFAULT 90;
ALTER TABLE vehicle_safety_scorecards ADD COLUMN IF NOT EXISTS risk_score DECIMAL(6,2) NOT NULL DEFAULT 20;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS tenant_id BIGINT NULL;
UPDATE ai_recommendations SET tenant_id=company_id WHERE tenant_id IS NULL;
ALTER TABLE ai_recommendations ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS recommendation_type VARCHAR(120) NOT NULL DEFAULT 'general';
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS summary TEXT NOT NULL DEFAULT '';
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS confidence_score NUMERIC(6,3) NOT NULL DEFAULT 0;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS urgency_score NUMERIC(6,3) NOT NULL DEFAULT 0;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS impact_json JSONB NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS reason_json JSONB NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS proposed_action_json JSONB NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS risk_level VARCHAR(40) NOT NULL DEFAULT 'Medium';
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS source_event_id VARCHAR(120) NULL;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS actor_type VARCHAR(40) NULL;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS actor_id VARCHAR(120) NULL;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL;
ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL;
ALTER TABLE ai_recommendations ALTER COLUMN module_key SET DEFAULT 'fleet.foundation';
ALTER TABLE ai_recommendations ALTER COLUMN body SET DEFAULT '';

CREATE TABLE IF NOT EXISTS maintenance_pm_rules (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  rule_name VARCHAR(180) NOT NULL,
  service_type VARCHAR(120) NOT NULL,
  vehicle_class VARCHAR(80) NULL,
  trigger_type VARCHAR(40) NOT NULL DEFAULT 'mileage',
  interval_miles INT NULL,
  interval_engine_hours INT NULL,
  interval_days INT NULL,
  warning_threshold_pct INT NOT NULL DEFAULT 10,
  overdue_threshold_pct INT NOT NULL DEFAULT 0,
  priority VARCHAR(40) NOT NULL DEFAULT 'Medium',
  estimated_cost DECIMAL(12,2) NULL,
  enabled BOOLEAN NOT NULL DEFAULT true,
  notes TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL,
  UNIQUE(company_id,service_type,trigger_type,vehicle_class)
);
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS out_of_service BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS availability_status VARCHAR(60) NOT NULL DEFAULT 'available';
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS engine_hours DECIMAL(12,2) NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS service_type VARCHAR(120) NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS priority VARCHAR(40) NOT NULL DEFAULT 'Medium';
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS estimated_cost DECIMAL(12,2) NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS risk_score DECIMAL(6,2) NOT NULL DEFAULT 20;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS description TEXT NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS recommended_action TEXT NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS odometer_miles DECIMAL(12,2) NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS engine_hours DECIMAL(12,2) NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS vehicle_id BIGINT NULL;
ALTER TABLE dvir_defects ADD COLUMN IF NOT EXISTS out_of_service BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE work_orders ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
CREATE INDEX IF NOT EXISTS idx_pm_rules_company ON maintenance_pm_rules(company_id,enabled);

ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS assignment_status VARCHAR(60) NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS previous_status VARCHAR(30) NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS exception_count INT NOT NULL DEFAULT 0;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
UPDATE dispatch_assignments
SET assignment_status=LOWER(REPLACE(COALESCE(status,'assigned'),' ','_'))
WHERE assignment_status IS NULL;
CREATE TABLE IF NOT EXISTS dispatch_exceptions (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  assignment_id BIGINT NOT NULL,
  job_id BIGINT NULL,
  trip_id BIGINT NULL,
  exception_type VARCHAR(60) NOT NULL DEFAULT 'general',
  severity VARCHAR(30) NOT NULL DEFAULT 'Medium',
  status VARCHAR(30) NOT NULL DEFAULT 'open',
  title VARCHAR(255) NULL,
  notes TEXT NULL,
  created_by BIGINT NULL,
  acknowledged_by BIGINT NULL,
  resolved_by BIGINT NULL,
  acknowledged_at TIMESTAMPTZ NULL,
  resolved_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS idx_dex_company_assignment ON dispatch_exceptions(company_id,assignment_id);

ALTER TABLE integrations ADD COLUMN IF NOT EXISTS integration_key VARCHAR(100) NULL;
ALTER TABLE integrations ADD COLUMN IF NOT EXISTS config_json JSONB NULL;
ALTER TABLE integrations ADD COLUMN IF NOT EXISTS last_sync_at TIMESTAMPTZ NULL;
ALTER TABLE integrations ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_integrations_tenant_key
  ON integrations(company_id,integration_key) WHERE integration_key IS NOT NULL;

DO $runtime_support_rls$
DECLARE t TEXT;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    RAISE EXCEPTION 'Stage 51 requires restricted opstrax_app; apply Stage 20 first';
  END IF;
  FOREACH t IN ARRAY ARRAY[
    'platform_incidents','notifications','notification_recipients','escalation_rules',
    'saved_reports','report_execution_log','scheduled_report_deliveries','scheduled_reports',
    'routes','route_stops','trips','trip_stops','location_events','latest_vehicle_positions',
    'telemetry_alerts','telemetry_rules','safety_events','driver_safety_scores',
    'telemetry_live_asset_states','fleet_health_snapshots','evidence_package_items','vehicle_safety_scorecards',
    'ai_recommendations',
    'maintenance_pm_rules','vehicles','maintenance_items','integrations','geofences',
    'dispatch_assignments','dispatch_exceptions'
  ]
  LOOP
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',t);
    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',t);
    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON public.%I',t);
    EXECUTE format('CREATE POLICY tenant_isolation ON public.%I FOR ALL USING (%I = NULLIF(current_setting(''app.current_tenant_id'',true),'''')::bigint) WITH CHECK (%I = NULLIF(current_setting(''app.current_tenant_id'',true),'''')::bigint)',
      t, CASE WHEN t='scheduled_reports' THEN 'tenant_id' ELSE 'company_id' END,
      CASE WHEN t='scheduled_reports' THEN 'tenant_id' ELSE 'company_id' END);
    EXECUTE format('DROP POLICY IF EXISTS platform_admin_bypass ON public.%I',t);
    EXECUTE format('CREATE POLICY platform_admin_bypass ON public.%I FOR ALL USING (NULLIF(current_setting(''app.platform_admin'',true),'''')=''on'') WITH CHECK (NULLIF(current_setting(''app.platform_admin'',true),'''')=''on'')',t);
    EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.%I TO opstrax_app',t);
  END LOOP;
  GRANT SELECT,INSERT,UPDATE,DELETE ON service_run_history,service_heartbeats,telemetry_nonces,gps_gateway_replay TO opstrax_app;
  FOR t IN
    SELECT seq_ns.nspname || '.' || quote_ident(seq.relname)
    FROM pg_class tbl
    JOIN pg_namespace tbl_ns ON tbl_ns.oid=tbl.relnamespace
    JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
    JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
    JOIN pg_namespace seq_ns ON seq_ns.oid=seq.relnamespace
    WHERE tbl_ns.nspname='public' AND tbl.relname=ANY(ARRAY[
      'service_run_history','platform_incidents','notification_recipients','escalation_rules',
      'saved_reports','report_execution_log','scheduled_report_deliveries','trip_stops',
      'latest_vehicle_positions','telemetry_alerts','telemetry_rules','telemetry_nonces',
      'gps_gateway_replay','driver_safety_scores','telemetry_live_asset_states','fleet_health_snapshots',
      'evidence_package_items','vehicle_safety_scorecards','ai_recommendations',
      'maintenance_pm_rules','dispatch_exceptions'
    ])
  LOOP
    EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_app',t);
  END LOOP;
END
$runtime_support_rls$;

COMMIT;
