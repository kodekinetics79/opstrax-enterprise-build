# OpsTrax Master ERD - PostgreSQL

## Layer 1 - Platform Admin / SaaS Control
Entities: `platform_roles`, `platform_role_permissions`, `platform_admins`, `platform_sessions`, `platform_audit_log`, `packages`, `tenant_subscriptions`, `tenant_entitlements`, `platform_invoices`, `platform_impersonation_sessions`

## Layer 2 - Tenant Organization / RBAC / Security
Entities: `companies`, `users`, `roles`, `permissions`, `user_sessions`, `audit_logs`, `feature_flags`

## Layer 3 - Customer / CRM / Sales / Contracts
Entities: `customers`, `contacts`, `leads`, `opportunities`, `quotes`, `quote_approvals`, `contracts`, `contract_versions`, `rate_cards`, `sla_rules`

## Layer 4 - Revenue / Finance / Billing / Profitability
Entities: `job_charges`, `invoice_headers`, `invoice_lines`, `payments`, `credit_notes`, `disputes`, `ar_aging_snapshots`, `revenue_events`, `job_costs`, `trip_costs`, `margin_snapshots`, `renewals`

## Layer 5 - Fleet Master Data
Entities: `vehicles`, `drivers`, `assets`, `yards`, `depots`, `customers` links, `vehicle_assignments`

## Layer 6 - IoT / Devices / Telematics / Live Map
Entities: `eld_devices`, `device_keys`, `telemetry_messages`, `location_events`, `latest_vehicle_positions`, `telemetry_alerts`, `telemetry_rules`

## Layer 7 - Dispatch / Jobs / Trips / POD
Entities: `jobs`, `trips`, `route_plans`, `stops`, `proof_of_delivery`, `dispatch_assignments`

## Layer 8 - Maintenance / Fleet Health
Entities: `work_orders`, `pm_plans`, `defects`, `fault_codes`, `maintenance_events`, `fleet_health_snapshots`

## Layer 9 - Alerts / Notifications
Entities: `alert_rules`, `alerts`, `alert_follow_up_tasks`, `notifications`, `notification_deliveries`

## Layer 10 - Safety / Dashcam / Incidents / Evidence
Entities: `safety_events`, `coaching_tasks`, `incidents`, `dashcam_clips`, `evidence_packages`

## Layer 11 - Compliance / Audit Readiness
Entities: `compliance_profiles`, `violations`, `inspection_records`, `document_requirements`, `audit_findings`, `retention_policies`

## Layer 12 - Autonomous AI / LLM Intelligence
Entities: `ai_observations`, `ai_reasoning_runs`, `ai_recommendations`, `ai_action_requests`, `ai_approvals`, `ai_executions`, `ai_outcomes`, `ai_memory`, `ai_prompt_templates`, `ai_usage_logs`

## Layer 13 - Files / Integrations / Event Bus / Reporting
Entities: `files`, `document_links`, `integration_connections`, `integration_jobs`, `webhook_deliveries`, `outbox_events`, `inbox_events`, `report_definitions`, `report_runs`

## Canonical Flow
Customer -> Contract -> Job -> Trip -> Charge -> Invoice -> Payment -> Margin -> Renewal

## Mermaid - Core Business ERD
```mermaid
erDiagram
  customers ||--o{ contracts : signs
  customers ||--o{ jobs : requests
  contracts ||--o{ rate_cards : defines
  contracts ||--o{ sla_rules : governs
  jobs ||--o{ trips : executes
  trips ||--o{ job_charges : generates
  job_charges ||--o{ invoice_lines : billed_as
  invoices ||--o{ payments : settled_by
  invoices ||--o{ disputes : disputed_by
  contracts ||--o{ renewals : renews
```

## Mermaid - Fleet / IoT ERD
```mermaid
erDiagram
  vehicles ||--o{ location_events : emits
  eld_devices ||--o{ telemetry_messages : sends
  telemetry_messages ||--o{ telemetry_alerts : raises
  vehicles ||--|| latest_vehicle_positions : current_state
  drivers ||--o{ safety_events : involved_in
```

## Mermaid - Revenue / Finance ERD
```mermaid
erDiagram
  customers ||--o{ contracts : owns
  contracts ||--o{ rate_cards : prices
  jobs ||--o{ job_charges : produces
  invoices ||--o{ invoice_lines : contains
  invoices ||--o{ payments : receives
  invoices ||--o{ credit_notes : adjusts
```

## Mermaid - AI Autonomy ERD
```mermaid
erDiagram
  ai_observations ||--o{ ai_reasoning_runs : feeds
  ai_reasoning_runs ||--o{ ai_recommendations : emits
  ai_recommendations ||--o{ ai_action_requests : requests
  ai_action_requests ||--o{ ai_approvals : requires
  ai_action_requests ||--o{ ai_executions : executes
  ai_executions ||--o{ ai_outcomes : results_in
```

## Mermaid - Event Bus ERD
```mermaid
erDiagram
  outbox_events ||--o{ integration_jobs : publishes
  integration_jobs ||--o{ webhook_deliveries : delivers
  inbox_events ||--o{ report_runs : informs
```

