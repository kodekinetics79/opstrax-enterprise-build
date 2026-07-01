# OPSTRAX_REALITY_AUDIT_V2

Exhaustive re-audit of the current Opstrax / KynexOne fleet build. This version is intentionally harsher than v1 where the evidence supports it.

Scope rules: no sampling language, no representative wording, no assumed coverage. The route inventory below is generated from every `app.Map*` declaration in the four target controller files.

## Executive Re-Score

| Axis | v1 | v2 | Verdict | Why the score changed |
|---|---:|---:|---|---|
| Schema reality | 86/100 | 88/100 | Still real | The money-path trace is now fully proven through invoice drafts, issued invoices, and payments with an automated test carrying a real amount. |
| RBAC | 94/100 | 92/100 | Still fail-closed on audited paths | Good on critical paths, but the app still depends on route-layer discipline and multiple manual checks. |
| Tenant isolation | 88/100 | 64/100 | Downgraded | There is still no RLS and no EF/global query filter. Isolation depends on manually maintained `company_id` / `tenant_id` predicates. |
| Demo masking risk | 72/100 | 58/100 | Downgraded | Seed-backed presentation logic still ships in deployable frontend code for dashboard and some jobs/trips/reporting surfaces. |
| AI governance | 96/100 | 96/100 | Unchanged | AI remains recommendation-only in the audited flows. |
| Audit / idempotency / concurrency | 93/100 | 95/100 | Slightly stronger | The dispatcher / outbox / inbox / idempotency stack is more complete when traced end-to-end. |
| Build / test health | 98/100 | 98/100 | Unchanged | Previous build/test evidence remains valid; no new breakage was introduced by this audit. |
| Production readiness | 74/100 | 69/100 | Downgraded | Manual tenant predicates and deployable seed scaffolding are still the biggest credibility risks. |

Overall verdict: the platform is real, but the tenancy story is still single-point-of-failure architecture, and some visible product surfaces remain demo-shaped in shipped frontend code.

## Endpoint Inventory Method

Criteria used for the route table below:

- `Auth` = the route body or immediate handler text contains an explicit auth/permission/session check such as `RequirePermission`, `RequireAdminPermission`, `RequireAsync`, `HasPlatformPermission`, `AuthenticateAsync`, `GetUserId`, `GetCompanyId`, `GetTenantId`, or `BearerToken`.
- `Test` = the exact route path or handler name appears in `backend-dotnet.Tests/*.cs`.
- `Tenant` = the route body or immediate handler text contains an explicit `company_id` / `tenant_id` filter or a `GetCompanyId` / `GetTenantId`-style tenant-scoping call.

### Exact Counts

- `Auth` checks: **273/619**
- Automated tests: **155/619**
- Explicit tenant filters: **236/619**

- Mutation routes: **289**
- Mutation routes with explicit tenant filters: **122/289**
- Single-point-of-failure tenant predicates on mutation routes: **42.2%**

The app has no DB-native tenant isolation. That means any future refactor that deletes one `company_id` predicate can reopen cross-tenant leakage on the affected route immediately. That is the core reason tenant isolation drops hard from v1.

## EndpointMappings.cs

Source file total: **556** routes | Auth: **211** | Tests: **133** | Tenant filters: **186**

| Method | Route | Has authorization attribute/check (Y/N) | Has automated test (Y/N) | Has explicit company_id/tenant_id filter in its query (Y/N) |
|---|---|---:|---:|---:|
| `POST` | `"/api/auth/login"` | N | N | N |
| `GET` | `"/api/auth/me"` | Y | N | Y |
| `POST` | `"/api/auth/refresh"` | Y | N | N |
| `POST` | `"/api/auth/logout"` | Y | N | N |
| `POST` | `"/api/auth/change-password"` | Y | N | N |
| `GET` | `"/api/alerts/summary"` | Y | N | Y |
| `GET` | `"/api/alerts"` | Y | N | Y |
| `GET` | `"/api/alerts/{id:long}"` | Y | N | Y |
| `POST` | `"/api/alerts/{id:long}/acknowledge"` | Y | N | Y |
| `POST` | `"/api/alerts/{id:long}/close"` | Y | N | Y |
| `POST` | `"/api/alerts/{id:long}/tasks"` | Y | N | Y |
| `GET` | `"/api/command-center/summary"` | Y | N | Y |
| `GET` | `"/api/control-tower/summary"` | Y | N | Y |
| `GET` | `"/api/control-tower/entities"` | Y | N | Y |
| `GET` | `"/api/control-tower/entities/{entityType}/{id:long}"` | Y | N | Y |
| `GET` | `"/api/control-tower/events"` | N | N | N |
| `POST` | `"/api/control-tower/actions/send-eta-update"` | N | N | N |
| `POST` | `"/api/control-tower/actions/create-dispatch-review"` | N | N | N |
| `POST` | `"/api/control-tower/actions/create-maintenance-review"` | N | N | N |
| `GET` | `"/api/vehicles/summary"` | Y | Y | Y |
| `GET` | `"/api/vehicles/planning-insights"` | Y | N | Y |
| `GET` | `"/api/vehicles"` | Y | Y | Y |
| `GET` | `"/api/vehicles/{id:long}"` | Y | N | Y |
| `POST` | `"/api/vehicles"` | Y | N | Y |
| `PUT` | `"/api/vehicles/{id:long}"` | Y | Y | Y |
| `DELETE` | `"/api/vehicles/{id:long}"` | Y | N | N |
| `GET` | `"/api/vehicles/{id:long}/timeline"` | N | Y | N |
| `GET` | `"/api/vehicles/{id:long}/recommendations"` | N | Y | N |
| `POST` | `"/api/vehicles/{id:long}/assign-driver"` | N | N | N |
| `POST` | `"/api/vehicles/{id:long}/change-status"` | N | N | N |
| `POST` | `"/api/telemetry/ingest"` | N | Y | Y |
| `POST` | `"/api/telemetry/stream-ticket"` | Y | N | Y |
| `GET` | `"/api/telemetry/stream"` | Y | N | Y |
| `GET` | `"/api/telemetry/positions"` | Y | N | Y |
| `GET` | `"/api/telemetry/metrics"` | Y | Y | N |
| `GET` | `"/api/telemetry/live-map-summary"` | Y | N | Y |
| `GET` | `"/api/telemetry/assets/live-state"` | Y | N | Y |
| `GET` | `"/api/telemetry/assets/{vehicleId:long}/live-state"` | Y | N | Y |
| `GET` | `"/api/telemetry/alerts"` | Y | Y | Y |
| `POST` | `"/api/telemetry/alerts/{id:long}/acknowledge"` | Y | N | Y |
| `POST` | `"/api/telemetry/alerts/{id:long}/resolve"` | Y | N | Y |
| `GET` | `"/api/telemetry/rules"` | Y | N | Y |
| `PUT` | `"/api/telemetry/rules/{ruleType}"` | Y | N | Y |
| `GET` | `"/api/telemetry/devices"` | N | N | N |
| `GET` | `"/api/telemetry/devices/{id:long}"` | N | N | N |
| `POST` | `"/api/telemetry/devices/provision"` | N | N | N |
| `POST` | `"/api/telemetry/devices/{id:long}/rotate-secret"` | N | N | N |
| `POST` | `"/api/telemetry/devices/{id:long}/revoke"` | N | N | N |
| `POST` | `"/api/telemetry/devices/{id:long}/suspend"` | N | N | N |
| `POST` | `"/api/telemetry/devices/{id:long}/activate"` | N | N | N |
| `POST` | `"/api/telemetry/devices/{id:long}/assign"` | N | N | N |
| `GET` | `"/api/devices"` | Y | N | Y |
| `GET` | `"/api/devices/{id:long}"` | Y | N | Y |
| `POST` | `"/api/devices/provision"` | Y | N | Y |
| `POST` | `"/api/devices/{id:long}/rotate-secret"` | Y | N | Y |
| `POST` | `"/api/devices/{id:long}/revoke"` | Y | N | Y |
| `POST` | `"/api/devices/{id:long}/suspend"` | Y | N | Y |
| `POST` | `"/api/devices/{id:long}/activate"` | Y | N | Y |
| `POST` | `"/api/devices/{id:long}/assign"` | Y | N | Y |
| `GET` | `"/api/safety/events"` | Y | N | Y |
| `GET` | `"/api/safety/events/{id:long}"` | Y | N | Y |
| `POST` | `"/api/safety/events/{id:long}/review"` | Y | N | Y |
| `POST` | `"/api/safety/events/{id:long}/dismiss"` | Y | N | Y |
| `POST` | `"/api/safety/events/{id:long}/resolve"` | Y | N | Y |
| `POST` | `"/api/safety/events/{id:long}/coaching"` | Y | N | Y |
| `GET` | `"/api/safety/drivers/scores"` | Y | N | Y |
| `GET` | `"/api/safety/dashboard"` | Y | N | Y |
| `POST` | `"/api/safety/coaching/{id:long}/complete"` | Y | N | Y |
| `POST` | `"/api/safety/coaching/{id:long}/acknowledge"` | Y | N | Y |
| `GET` | `"/api/safety/rules"` | Y | N | Y |
| `PUT` | `"/api/safety/rules/{ruleType}"` | Y | N | Y |
| `GET` | `"/api/trips"` | Y | N | Y |
| `GET` | `"/api/trips/{id:long}"` | Y | N | Y |
| `GET` | `"/api/trips/{id:long}/breadcrumbs"` | Y | N | Y |
| `GET` | `"/api/trips/{id:long}/compliance"` | Y | Y | Y |
| `POST` | `"/api/trips/{id:long}/start"` | Y | Y | Y |
| `POST` | `"/api/trips/{id:long}/complete"` | Y | Y | Y |
| `POST` | `"/api/trips/{id:long}/exception"` | Y | N | Y |
| `GET` | `"/api/drivers/summary"` | Y | N | Y |
| `GET` | `"/api/drivers"` | Y | Y | Y |
| `GET` | `"/api/drivers/{id:long}"` | Y | N | Y |
| `POST` | `"/api/drivers"` | Y | N | Y |
| `PUT` | `"/api/drivers/{id:long}"` | Y | N | Y |
| `DELETE` | `"/api/drivers/{id:long}"` | Y | N | N |
| `GET` | `"/api/drivers/{id:long}/timeline"` | N | Y | N |
| `GET` | `"/api/drivers/{id:long}/recommendations"` | N | Y | N |
| `POST` | `"/api/drivers/{id:long}/assign-vehicle"` | N | N | N |
| `POST` | `"/api/drivers/{id:long}/change-status"` | N | N | N |
| `GET` | `"/api/customers/summary"` | Y | Y | Y |
| `GET` | `"/api/customers"` | Y | Y | Y |
| `GET` | `"/api/customers/{id:long}"` | Y | N | Y |
| `POST` | `"/api/customers"` | N | N | N |
| `PUT` | `"/api/customers/{id:long}"` | N | N | N |
| `DELETE` | `"/api/customers/{id:long}"` | N | N | N |
| `GET` | `"/api/customers/{id:long}/timeline"` | N | Y | N |
| `GET` | `"/api/customers/{id:long}/recommendations"` | N | Y | N |
| `GET` | `"/api/assets/summary"` | Y | N | Y |
| `GET` | `"/api/assets"` | Y | N | Y |
| `GET` | `"/api/assets/{id:long}"` | Y | N | Y |
| `POST` | `"/api/assets"` | N | N | N |
| `PUT` | `"/api/assets/{id:long}"` | N | N | N |
| `DELETE` | `"/api/assets/{id:long}"` | Y | N | N |
| `GET` | `"/api/assets/{id:long}/timeline"` | N | Y | N |
| `GET` | `"/api/assets/{id:long}/recommendations"` | N | Y | N |
| `POST` | `"/api/assets/{id:long}/assign"` | N | N | N |
| `GET` | `"/api/jobs/summary"` | Y | N | Y |
| `GET` | `"/api/jobs"` | Y | Y | Y |
| `GET` | `"/api/jobs/{id:long}"` | Y | N | Y |
| `POST` | `"/api/jobs"` | Y | Y | Y |
| `PUT` | `"/api/jobs/{id:long}"` | Y | N | Y |
| `DELETE` | `"/api/jobs/{id:long}"` | Y | N | N |
| `GET` | `"/api/jobs/{id:long}/timeline"` | N | Y | N |
| `GET` | `"/api/jobs/{id:long}/recommendations"` | N | Y | N |
| `POST` | `"/api/jobs/import-preview"` | N | N | N |
| `POST` | `"/api/jobs/{id:long}/assign"` | Y | N | Y |
| `POST` | `"/api/jobs/{id:long}/status"` | Y | N | Y |
| `POST` | `"/api/jobs/{id:long}/send-eta"` | Y | N | Y |
| `POST` | `"/api/jobs/{id:long}/proof-placeholder"` | Y | N | Y |
| `POST` | `"/api/jobs/{id:long}/proof"` | Y | N | Y |
| `GET` | `"/api/jobs/{id:long}/proof"` | N | N | N |
| `GET` | `"/api/proof-of-delivery"` | Y | N | Y |
| `GET` | `"/api/proof-of-delivery/summary"` | Y | N | Y |
| `GET` | `"/api/dispatch/summary"` | Y | N | Y |
| `GET` | `"/api/dispatch/board"` | Y | N | Y |
| `GET` | `"/api/dispatch/recommendations"` | Y | N | Y |
| `GET` | `"/api/dispatch/available-drivers"` | Y | Y | Y |
| `GET` | `"/api/dispatch/available-vehicles"` | Y | Y | Y |
| `POST` | `"/api/dispatch/assign"` | Y | Y | N |
| `POST` | `"/api/dispatch/status"` | Y | N | N |
| `POST` | `"/api/dispatch/auto-suggest"` | N | N | N |
| `POST` | `"/api/dispatch/send-eta-updates"` | N | N | N |
| `GET` | `"/api/dispatch/assignments"` | Y | N | Y |
| `GET` | `"/api/dispatch/assignments/{id:long}"` | Y | N | Y |
| `POST` | `"/api/dispatch/assignments"` | N | N | N |
| `POST` | `"/api/dispatch/assignments/{id:long}/accept"` | Y | N | Y |
| `POST` | `"/api/dispatch/assignments/{id:long}/status"` | Y | N | Y |
| `POST` | `"/api/dispatch/assignments/{id:long}/exception"` | Y | N | Y |
| `POST` | `"/api/dispatch/assignments/{id:long}/cancel"` | Y | N | Y |
| `POST` | `"/api/dispatch/assignments/{id:long}/proof"` | Y | N | Y |
| `GET` | `"/api/dispatch/eligibility"` | Y | N | Y |
| `GET` | `"/api/dispatch/exceptions"` | Y | N | Y |
| `GET` | `"/api/routes/summary"` | N | N | N |
| `GET` | `"/api/last-mile/deliveries"` | N | N | N |
| `GET` | `"/api/routes"` | N | N | N |
| `GET` | `"/api/routes/{id:long}"` | N | N | N |
| `POST` | `"/api/routes"` | N | N | N |
| `PUT` | `"/api/routes/{id:long}"` | N | N | N |
| `DELETE` | `"/api/routes/{id:long}"` | Y | N | N |
| `GET` | `"/api/routes/{id:long}/stops"` | N | N | N |
| `POST` | `"/api/routes/{id:long}/stops"` | N | N | N |
| `PUT` | `"/api/routes/{id:long}/stops/{stopId:long}"` | N | N | N |
| `DELETE` | `"/api/routes/{id:long}/stops/{stopId:long}"` | N | N | N |
| `POST` | `"/api/routes/{id:long}/optimize-preview"` | N | N | N |
| `POST` | `"/api/routes/{id:long}/assign"` | N | N | N |
| `GET` | `"/api/routes/{id:long}/timeline"` | N | Y | N |
| `GET` | `"/api/routes/{id:long}/recommendations"` | N | N | N |
| `GET` | `"/api/customer-eta/summary"` | N | N | N |
| `GET` | `"/api/customer-eta/track/{trackingCode}"` | N | N | N |
| `GET` | `"/api/customer-eta/job/{jobId:long}"` | N | N | N |
| `POST` | `"/api/customer-eta/job/{jobId:long}/send-update"` | N | N | N |
| `POST` | `"/api/customer-eta/job/{jobId:long}/feedback"` | N | N | N |
| `GET` | `"/api/customer-eta/communications"` | N | N | N |
| `GET` | `"/api/customer-eta/recommendations"` | N | Y | N |
| `GET` | `"/api/customer-visibility/shipments"` | N | N | N |
| `GET` | `"/api/customer-visibility/shipments/{id:long}"` | N | N | N |
| `POST` | `"/api/customer-visibility/shipments/{id:long}/share"` | N | N | N |
| `DELETE` | `"/api/customer-visibility/shipments/{id:long}/share"` | N | Y | N |
| `GET` | `"/api/customer-visibility/insights"` | N | N | N |
| `GET` | `"/api/customer-visibility/tracking/{token}"` | N | N | N |
| `GET` | `"/api/customer-visibility/tracking/{token}/events"` | N | N | Y |
| `GET` | `"/api/customer-visibility/tracking/{token}/proofs"` | N | N | Y |
| `GET` | `"/api/driver/me"` | N | N | N |
| `GET` | `"/api/driver/assignments"` | N | N | N |
| `GET` | `"/api/driver/assignments/current"` | N | N | N |
| `POST` | `"/api/driver/assignments/{id:long}/accept"` | N | N | N |
| `POST` | `"/api/driver/assignments/{id:long}/status"` | N | N | N |
| `POST` | `"/api/driver/assignments/{id:long}/exception"` | N | N | N |
| `POST` | `"/api/driver/assignments/{id:long}/proof"` | N | N | N |
| `GET` | `"/api/driver/dvir/templates"` | N | N | N |
| `POST` | `"/api/driver/dvir"` | N | N | N |
| `GET` | `"/api/driver/coaching"` | N | N | N |
| `POST` | `"/api/driver/coaching/{id:long}/acknowledge"` | N | N | N |
| `GET` | `"/api/driver/hos"` | N | N | N |
| `GET` | `"/api/geofences"` | Y | Y | Y |
| `GET` | `"/api/geofences/summary"` | Y | Y | Y |
| `GET` | `"/api/geofences/{id:long}/events"` | N | Y | N |
| `POST` | `"/api/geofences"` | N | Y | N |
| `PUT` | `"/api/geofences/{id:long}"` | N | Y | N |
| `DELETE` | `"/api/geofences/{id:long}"` | N | Y | N |
| `GET` | `"/api/invoices"` | N | N | N |
| `GET` | `"/api/payments"` | N | N | N |
| `GET` | `"/api/profitability"` | N | N | N |
| `GET` | `"/api/profitability/summary"` | N | N | N |
| `GET` | `"/api/carbon-emissions"` | N | N | N |
| `GET` | `"/api/digital-forms/templates"` | N | N | N |
| `GET` | `"/api/digital-forms/submissions"` | N | N | N |
| `POST` | `"/api/digital-forms/submissions"` | N | Y | N |
| `GET` | `"/api/feature-flags"` | N | N | N |
| `PUT` | `"/api/feature-flags/{key}/toggle"` | N | Y | N |
| `GET` | `"/api/integrations"` | N | N | N |
| `GET` | `"/api/vehicle-assignments"` | N | N | N |
| `GET` | `"/api/owners"` | N | N | N |
| `GET` | `"/api/traffic-violations"` | N | N | N |
| `GET` | `"/api/traffic-violations/summary"` | N | N | N |
| `GET` | `"/api/service-history"` | N | N | N |
| `GET` | `"/api/downtime"` | N | N | N |
| `GET` | `"/api/preventive-maintenance"` | N | N | N |
| `GET` | `"/api/fleet/utilization"` | N | N | N |
| `GET` | `"/api/fleet/utilization/summary"` | N | N | N |
| `GET` | `"/api/maintenance/dashboard"` | Y | N | Y |
| `GET` | `"/api/maintenance/inspections"` | Y | N | Y |
| `POST` | `"/api/maintenance/inspections"` | N | N | N |
| `GET` | `"/api/maintenance/inspections/{id:long}"` | Y | N | Y |
| `POST` | `"/api/maintenance/inspections/{id:long}/review"` | Y | N | Y |
| `GET` | `"/api/maintenance/defects"` | Y | N | Y |
| `POST` | `"/api/maintenance/defects/{id:long}/acknowledge"` | Y | N | Y |
| `POST` | `"/api/maintenance/defects/{id:long}/resolve"` | Y | N | Y |
| `GET` | `"/api/maintenance/work-orders"` | Y | N | Y |
| `POST` | `"/api/maintenance/work-orders"` | Y | N | Y |
| `POST` | `"/api/maintenance/work-orders/{id:long}/assign"` | Y | N | Y |
| `POST` | `"/api/maintenance/work-orders/{id:long}/complete"` | Y | N | Y |
| `GET` | `"/api/maintenance/rules"` | Y | N | Y |
| `PUT` | `"/api/maintenance/rules/{ruleType}"` | Y | N | Y |
| `POST` | `"/api/maintenance/fault-codes/ingest"` | N | N | Y |
| `GET` | `"/api/maintenance/fault-codes"` | Y | N | Y |
| `GET` | `"/api/maintenance/summary"` | N | N | N |
| `GET` | `"/api/maintenance/due"` | N | Y | N |
| `GET` | `"/api/maintenance/overdue"` | N | Y | N |
| `GET` | `"/api/maintenance/recommendations"` | N | Y | N |
| `GET` | `"/api/maintenance"` | N | N | N |
| `GET` | `"/api/maintenance/{id:long}"` | N | N | N |
| `POST` | `"/api/maintenance"` | N | N | N |
| `PUT` | `"/api/maintenance/{id:long}"` | N | N | N |
| `DELETE` | `"/api/maintenance/{id:long}"` | Y | N | N |
| `POST` | `"/api/maintenance/{id:long}/schedule"` | N | N | N |
| `POST` | `"/api/maintenance/{id:long}/defer"` | N | N | N |
| `POST` | `"/api/maintenance/{id:long}/create-workorder"` | N | N | N |
| `GET` | `"/api/workorders/summary"` | N | N | N |
| `GET` | `"/api/workorders"` | N | Y | N |
| `GET` | `"/api/workorders/{id:long}"` | N | N | N |
| `POST` | `"/api/workorders"` | N | N | N |
| `PUT` | `"/api/workorders/{id:long}"` | N | N | N |
| `DELETE` | `"/api/workorders/{id:long}"` | Y | N | N |
| `GET` | `"/api/workorders/{id:long}/timeline"` | N | N | N |
| `GET` | `"/api/workorders/{id:long}/recommendations"` | N | Y | N |
| `POST` | `"/api/workorders/{id:long}/assign"` | N | N | N |
| `POST` | `"/api/workorders/{id:long}/status"` | N | N | N |
| `POST` | `"/api/workorders/{id:long}/add-labor"` | N | N | N |
| `POST` | `"/api/workorders/{id:long}/add-part"` | N | N | N |
| `POST` | `"/api/workorders/{id:long}/complete"` | N | N | N |
| `POST` | `"/api/workorders/{id:long}/approve-cost"` | N | N | N |
| `GET` | `"/api/dvir/summary"` | N | N | N |
| `GET` | `"/api/dvir/templates"` | N | N | N |
| `POST` | `"/api/dvir/templates"` | N | N | N |
| `PUT` | `"/api/dvir/templates/{id:long}"` | N | N | N |
| `GET` | `"/api/dvir/recommendations"` | N | Y | N |
| `GET` | `"/api/dvir/reports"` | N | N | N |
| `GET` | `"/api/dvir/reports/{id:long}"` | N | N | N |
| `POST` | `"/api/dvir/reports"` | N | N | N |
| `PUT` | `"/api/dvir/reports/{id:long}"` | N | N | N |
| `DELETE` | `"/api/dvir/reports/{id:long}"` | N | N | N |
| `POST` | `"/api/dvir/reports/{id:long}/mechanic-review"` | N | N | N |
| `POST` | `"/api/dvir/reports/{id:long}/certify-repair"` | N | N | N |
| `POST` | `"/api/dvir/reports/{id:long}/driver-sign"` | N | N | N |
| `GET` | `"/api/dvir/reports/{id:long}/timeline"` | N | N | N |
| `GET` | `"/api/documents/summary"` | N | N | N |
| `GET` | `"/api/documents/expiring"` | N | Y | N |
| `GET` | `"/api/documents/recommendations"` | N | Y | N |
| `GET` | `"/api/documents"` | N | Y | N |
| `GET` | `"/api/documents/{id:long}"` | N | N | N |
| `POST` | `"/api/documents"` | N | N | N |
| `PUT` | `"/api/documents/{id:long}"` | N | N | N |
| `DELETE` | `"/api/documents/{id:long}"` | Y | N | N |
| `POST` | `"/api/documents/upload-placeholder"` | N | N | N |
| `POST` | `"/api/documents/{id:long}/renew-placeholder"` | N | N | N |
| `GET` | `"/api/documents/{id:long}/timeline"` | N | N | N |
| `GET` | `"/api/safety/summary"` | Y | N | Y |
| `GET` | `"/api/safety/drivers/scorecards"` | Y | N | Y |
| `GET` | `"/api/safety/vehicles/scorecards"` | N | Y | N |
| `GET` | `"/api/safety/recommendations"` | N | Y | N |
| `GET` | `"/api/safety/trends"` | Y | N | Y |
| `POST` | `"/api/safety/events/{id:long}/create-coaching-task"` | N | N | N |
| `POST` | `"/api/safety/events/{id:long}/create-incident"` | N | N | N |
| `GET` | `"/api/dashcam/summary"` | N | N | N |
| `GET` | `"/api/dashcam/events"` | N | N | N |
| `GET` | `"/api/dashcam/events/{id:long}"` | N | N | N |
| `POST` | `"/api/dashcam/events"` | N | N | N |
| `PUT` | `"/api/dashcam/events/{id:long}"` | N | N | N |
| `DELETE` | `"/api/dashcam/events/{id:long}"` | Y | N | N |
| `GET` | `"/api/dashcam/recommendations"` | N | Y | N |
| `POST` | `"/api/dashcam/events/{id:long}/review"` | N | N | N |
| `POST` | `"/api/dashcam/events/{id:long}/mark-false-positive"` | N | N | N |
| `POST` | `"/api/dashcam/events/{id:long}/create-coaching-task"` | N | N | N |
| `POST` | `"/api/dashcam/events/{id:long}/create-evidence-package"` | N | N | N |
| `POST` | `"/api/dashcam/events/{id:long}/create-incident-report"` | N | N | N |
| `GET` | `"/api/coaching/summary"` | N | N | N |
| `GET` | `"/api/coaching/tasks"` | N | Y | N |
| `GET` | `"/api/coaching/tasks/{id:long}"` | N | N | N |
| `POST` | `"/api/coaching/tasks"` | N | N | N |
| `PUT` | `"/api/coaching/tasks/{id:long}"` | N | N | N |
| `DELETE` | `"/api/coaching/tasks/{id:long}"` | Y | N | N |
| `GET` | `"/api/coaching/recommendations"` | N | Y | N |
| `POST` | `"/api/coaching/tasks/{id:long}/assign"` | N | N | N |
| `POST` | `"/api/coaching/tasks/{id:long}/acknowledge"` | N | N | N |
| `POST` | `"/api/coaching/tasks/{id:long}/complete"` | N | N | N |
| `POST` | `"/api/coaching/tasks/{id:long}/add-note"` | N | N | N |
| `GET` | `"/api/incidents/summary"` | N | N | N |
| `GET` | `"/api/incidents"` | N | Y | N |
| `GET` | `"/api/incidents/{id:long}"` | N | N | N |
| `POST` | `"/api/incidents"` | N | N | N |
| `PUT` | `"/api/incidents/{id:long}"` | N | N | N |
| `DELETE` | `"/api/incidents/{id:long}"` | Y | N | N |
| `GET` | `"/api/incidents/{id:long}/timeline"` | N | N | N |
| `GET` | `"/api/incidents/{id:long}/recommendations"` | N | Y | N |
| `POST` | `"/api/incidents/{id:long}/status"` | N | N | N |
| `POST` | `"/api/incidents/{id:long}/attach-evidence"` | N | N | N |
| `POST` | `"/api/incidents/{id:long}/create-insurance-report"` | N | N | N |
| `GET` | `"/api/evidence-packages/summary"` | N | N | N |
| `GET` | `"/api/evidence-packages"` | N | N | N |
| `GET` | `"/api/evidence-packages/{id:long}"` | N | N | N |
| `POST` | `"/api/evidence-packages"` | N | N | N |
| `PUT` | `"/api/evidence-packages/{id:long}"` | N | N | N |
| `DELETE` | `"/api/evidence-packages/{id:long}"` | Y | N | N |
| `POST` | `"/api/evidence-packages/{id:long}/generate-export-placeholder"` | N | N | N |
| `POST` | `"/api/evidence-packages/{id:long}/lock-package"` | N | N | N |
| `GET` | `"/api/ai/insights"` | N | Y | N |
| `POST` | `"/api/ai/ask"` | Y | N | Y |
| `GET` | `"/api/fuel/summary"` | N | N | N |
| `GET` | `"/api/fuel/transactions"` | N | N | N |
| `GET` | `"/api/fuel/transactions/{id:long}"` | N | N | N |
| `POST` | `"/api/fuel/transactions"` | N | N | N |
| `PUT` | `"/api/fuel/transactions/{id:long}"` | N | N | N |
| `DELETE` | `"/api/fuel/transactions/{id:long}"` | Y | N | N |
| `GET` | `"/api/fuel/idling-events"` | N | N | N |
| `GET` | `"/api/fuel/idling-events/{id:long}"` | N | N | N |
| `POST` | `"/api/fuel/idling-events"` | N | N | N |
| `PUT` | `"/api/fuel/idling-events/{id:long}"` | N | N | N |
| `GET` | `"/api/fuel/vehicle/{vehicleId:long}/summary"` | N | Y | N |
| `GET` | `"/api/fuel/driver/{driverId:long}/summary"` | N | Y | N |
| `GET` | `"/api/fuel/vehicle-summary"` | N | N | N |
| `GET` | `"/api/fuel/driver-summary"` | N | N | N |
| `GET` | `"/api/fuel/anomalies"` | N | N | N |
| `GET` | `"/api/fuel/recommendations"` | N | Y | N |
| `POST` | `"/api/fuel/import-preview"` | N | N | N |
| `POST` | `"/api/fuel/anomalies/{id:long}/review"` | N | N | N |
| `GET` | `"/api/expenses/summary"` | N | N | N |
| `GET` | `"/api/expenses"` | N | N | N |
| `GET` | `"/api/expenses/{id:long}"` | N | N | N |
| `POST` | `"/api/expenses"` | Y | N | Y |
| `PUT` | `"/api/expenses/{id:long}"` | Y | N | Y |
| `DELETE` | `"/api/expenses/{id:long}"` | Y | N | N |
| `POST` | `"/api/expenses/{id:long}/approve"` | Y | N | Y |
| `POST` | `"/api/expenses/{id:long}/reject"` | Y | N | Y |
| `GET` | `"/api/expenses/categories"` | N | N | N |
| `GET` | `"/api/expenses/recommendations"` | N | Y | N |
| `POST` | `"/api/expenses/import-preview"` | N | N | N |
| `GET` | `"/api/contracts/summary"` | N | N | N |
| `GET` | `"/api/contracts"` | N | N | N |
| `GET` | `"/api/contracts/{id:long}"` | Y | N | Y |
| `POST` | `"/api/contracts"` | Y | N | Y |
| `PUT` | `"/api/contracts/{id:long}"` | Y | N | Y |
| `DELETE` | `"/api/contracts/{id:long}"` | Y | N | N |
| `GET` | `"/api/contracts/{id:long}/rates"` | N | Y | N |
| `POST` | `"/api/contracts/{id:long}/rates"` | Y | N | Y |
| `PUT` | `"/api/contracts/{id:long}/rates/{rateId:long}"` | Y | N | Y |
| `DELETE` | `"/api/contracts/{id:long}/rates/{rateId:long}"` | N | N | N |
| `GET` | `"/api/contracts/recommendations"` | N | Y | N |
| `POST` | `"/api/contracts/{id:long}/activate"` | Y | N | Y |
| `POST` | `"/api/contracts/{id:long}/expire"` | Y | N | Y |
| `GET` | `"/api/carriers/summary"` | N | N | N |
| `GET` | `"/api/carriers"` | N | Y | N |
| `GET` | `"/api/carriers/{id:long}"` | N | N | N |
| `POST` | `"/api/carriers"` | Y | N | Y |
| `PUT` | `"/api/carriers/{id:long}"` | Y | N | Y |
| `DELETE` | `"/api/carriers/{id:long}"` | Y | N | N |
| `GET` | `"/api/carriers/{id:long}/performance"` | N | Y | N |
| `GET` | `"/api/carriers/{id:long}/documents"` | N | Y | N |
| `POST` | `"/api/carriers/{id:long}/status"` | Y | N | Y |
| `GET` | `"/api/carriers/recommendations"` | N | Y | N |
| `GET` | `"/api/cost-margin/summary"` | N | N | N |
| `GET` | `"/api/cost-margin/jobs"` | N | Y | N |
| `GET` | `"/api/cost-margin/routes"` | N | Y | N |
| `GET` | `"/api/cost-margin/vehicles"` | N | Y | N |
| `GET` | `"/api/cost-margin/customers"` | N | Y | N |
| `GET` | `"/api/cost-margin/predictions"` | N | Y | N |
| `GET` | `"/api/predictions/maintenance"` | N | N | N |
| `GET` | `"/api/predictions/driver-risk"` | N | N | N |
| `GET` | `"/api/predictions/sla-risk"` | N | N | N |
| `GET` | `"/api/workforce/drivers"` | N | N | N |
| `GET` | `"/api/workforce/schedule"` | N | N | N |
| `POST` | `"/api/workforce/schedule/assign"` | N | Y | N |
| `GET` | `"/api/cost-margin/recommendations"` | N | Y | N |
| `POST` | `"/api/cost-margin/recalculate"` | N | N | N |
| `POST` | `"/api/cost-margin/jobs/{jobId:long}/recalculate"` | N | N | N |
| `GET` | `"/api/cost-leakage/summary"` | N | N | N |
| `GET` | `"/api/cost-leakage/items"` | N | N | N |
| `GET` | `"/api/cost-leakage/recommendations"` | N | Y | N |
| `POST` | `"/api/cost-leakage/items/{id:long}/acknowledge"` | N | N | N |
| `POST` | `"/api/cost-leakage/items/{id:long}/create-action"` | N | N | Y |
| `GET` | `"/api/compliance/summary"` | N | Y | N |
| `GET` | `"/api/compliance/profiles"` | N | Y | N |
| `GET` | `"/api/compliance/rules"` | N | Y | N |
| `GET` | `"/api/compliance/violations"` | N | Y | N |
| `GET` | `"/api/compliance/violations/{id:long}"` | N | Y | N |
| `POST` | `"/api/compliance/violations/{id:long}/acknowledge"` | N | Y | N |
| `POST` | `"/api/compliance/violations/{id:long}/resolve"` | N | Y | N |
| `GET` | `"/api/compliance/documents"` | N | Y | N |
| `GET` | `"/api/compliance/audit-packages"` | N | Y | N |
| `GET` | `"/api/compliance/audit-packages/{id:long}"` | N | Y | N |
| `POST` | `"/api/compliance/audit-packages"` | N | N | N |
| `POST` | `"/api/compliance/audit-packages/{id:long}/finalize"` | N | Y | N |
| `GET` | `"/api/compliance/cross-border-watch"` | N | Y | N |
| `GET` | `"/api/compliance/driver-status"` | N | Y | N |
| `GET` | `"/api/compliance/vehicle-status"` | N | Y | N |
| `GET` | `"/api/compliance/ai/recommendations"` | N | Y | N |
| `GET` | `"/api/hos/summary"` | N | N | N |
| `GET` | `"/api/hos/drivers"` | N | Y | N |
| `GET` | `"/api/hos/clocks"` | N | Y | N |
| `GET` | `"/api/hos/logs"` | N | Y | N |
| `GET` | `"/api/hos/logs/{driverId:long}"` | N | Y | N |
| `POST` | `"/api/hos/logs/{id:long}/certify"` | N | N | N |
| `GET` | `"/api/hos/ai/recommendations"` | N | Y | N |
| `GET` | `"/api/eld/devices"` | N | Y | N |
| `GET` | `"/api/eld/devices/{id:long}"` | N | Y | N |
| `POST` | `"/api/eld/devices/{id:long}/mark-malfunction"` | N | N | N |
| `POST` | `"/api/eld/devices/{id:long}/resolve-malfunction"` | N | Y | N |
| `GET` | `"/api/localization/countries"` | N | Y | N |
| `GET` | `"/api/localization/languages"` | N | Y | N |
| `GET` | `"/api/localization/settings"` | N | Y | N |
| `PUT` | `"/api/localization/settings"` | Y | N | Y |
| `GET` | `"/api/localization/user-preferences"` | N | Y | N |
| `PUT` | `"/api/localization/user-preferences"` | Y | N | Y |
| `GET` | `"/api/reports/catalog"` | N | Y | N |
| `GET` | `"/api/reports/summary"` | Y | N | Y |
| `GET` | `"/api/reports/runs"` | N | Y | N |
| `POST` | `"/api/reports/{key}/run"` | Y | N | Y |
| `GET` | `"/api/reports/scheduled"` | N | Y | N |
| `POST` | `"/api/reports/scheduled"` | Y | Y | Y |
| `POST` | `"/api/reports/scheduled/{id:long}/pause"` | N | N | N |
| `POST` | `"/api/reports/scheduled/{id:long}/resume"` | N | N | N |
| `GET` | `"/api/reports/exports"` | N | Y | N |
| `POST` | `"/api/reports/exports"` | Y | N | Y |
| `GET` | `"/api/reports/ai/recommendations"` | N | Y | N |
| `GET` | `"/api/reports/datasets"` | N | N | N |
| `GET` | `"/api/reports/saved"` | N | N | N |
| `POST` | `"/api/reports/saved"` | N | N | N |
| `GET` | `"/api/reports/saved/{id:long}"` | N | N | N |
| `PUT` | `"/api/reports/saved/{id:long}"` | N | N | N |
| `DELETE` | `"/api/reports/saved/{id:long}"` | N | N | N |
| `POST` | `"/api/reports/run"` | N | N | N |
| `POST` | `"/api/reports/saved/{id:long}/run"` | N | N | N |
| `POST` | `"/api/reports/export"` | N | N | N |
| `GET` | `"/api/reports/saved/{id:long}/export"` | N | N | N |
| `POST` | `"/api/reports/scheduled/p8"` | N | N | N |
| `GET` | `"/api/analytics/executive"` | N | N | N |
| `GET` | `"/api/analytics/operations"` | N | N | N |
| `GET` | `"/api/analytics/dispatch"` | N | N | N |
| `GET` | `"/api/analytics/safety"` | N | N | N |
| `GET` | `"/api/analytics/maintenance"` | N | N | N |
| `GET` | `"/api/analytics/customer"` | N | N | N |
| `GET` | `"/api/analytics/trends"` | N | N | N |
| `GET` | `"/api/analytics/insights"` | N | N | N |
| `GET` | `"/api/kpi/metrics"` | N | N | N |
| `GET` | `"/api/kpi/summary"` | N | N | N |
| `GET` | `"/api/kpi/targets"` | N | Y | N |
| `GET` | `"/api/kpi/ai/recommendations"` | N | Y | N |
| `GET` | `"/api/sla/records"` | Y | Y | Y |
| `GET` | `"/api/sla/summary"` | N | N | N |
| `GET` | `"/api/sla/breaches"` | Y | Y | Y |
| `POST` | `"/api/sla/breaches/{id:long}/acknowledge"` | N | Y | N |
| `POST` | `"/api/sla/breaches/{id:long}/resolve"` | N | Y | N |
| `GET` | `"/api/audit/logs"` | N | Y | N |
| `GET` | `"/api/audit/logs/{id:long}"` | N | Y | N |
| `GET` | `"/api/audit/export-requests"` | N | Y | N |
| `POST` | `"/api/audit/export-requests"` | Y | N | Y |
| `GET` | `"/api/audit/ai/recommendations"` | N | Y | N |
| `GET` | `"/api/admin/overview"` | Y | N | Y |
| `GET` | `"/api/admin/users"` | Y | N | Y |
| `GET` | `"/api/admin/users/{id:long}"` | Y | N | N |
| `POST` | `"/api/admin/users"` | Y | N | Y |
| `PUT` | `"/api/admin/users/{id:long}"` | Y | N | Y |
| `DELETE` | `"/api/admin/users/{id:long}"` | Y | N | N |
| `GET` | `"/api/admin/roles"` | Y | Y | N |
| `GET` | `"/api/admin/permissions"` | Y | Y | N |
| `PUT` | `"/api/admin/roles/{id:long}"` | Y | N | N |
| `POST` | `"/api/admin/audit-events"` | Y | N | N |
| `GET` | `"/api/executive/snapshots"` | N | Y | N |
| `GET` | `"/api/executive/summary"` | N | N | N |
| `GET` | `"/api/executive/ai/recommendations"` | N | Y | N |
| `GET` | `"/api/alert-rules"` | Y | N | Y |
| `POST` | `"/api/alert-rules"` | N | Y | N |
| `PUT` | `"/api/alert-rules/{id:long}"` | N | Y | N |
| `PUT` | `"/api/alert-rules/{id:long}/toggle"` | N | Y | N |
| `DELETE` | `"/api/alert-rules/{id:long}"` | N | Y | N |
| `GET` | `"/api/driver-messages"` | N | N | N |
| `POST` | `"/api/driver-messages"` | N | Y | N |
| `POST` | `"/api/driver-messages/broadcast"` | N | Y | N |
| `GET` | `"/api/notifications"` | Y | Y | Y |
| `GET` | `"/api/notifications/unread-count"` | Y | N | Y |
| `POST` | `"/api/notifications/{id:long}/read"` | Y | N | Y |
| `POST` | `"/api/notifications/{id:long}/acknowledge"` | Y | Y | Y |
| `POST` | `"/api/notifications/acknowledge-all"` | Y | N | Y |
| `GET` | `"/api/messages/conversations"` | Y | N | Y |
| `GET` | `"/api/messages/conversations/{id:long}"` | Y | Y | Y |
| `POST` | `"/api/messages/conversations"` | Y | N | Y |
| `POST` | `"/api/messages/conversations/{id:long}/messages"` | Y | Y | Y |
| `POST` | `"/api/messages/conversations/{id:long}/read"` | Y | N | Y |
| `GET` | `"/api/escalation-rules"` | Y | N | Y |
| `POST` | `"/api/escalation-rules"` | Y | N | Y |
| `PUT` | `"/api/escalation-rules/{id:long}"` | Y | N | Y |
| `DELETE` | `"/api/escalation-rules/{id:long}"` | Y | N | Y |
| `GET` | `"/api/about/platform"` | N | N | N |
| `GET` | `"/api/about/health-summary"` | N | N | N |
| `GET` | `"/api/modules/{moduleKey}"` | N | N | N |
| `GET` | `"/api/modules/{moduleKey}/{id:long}"` | N | N | N |
| `POST` | `"/api/modules/{moduleKey}"` | N | N | N |
| `PUT` | `"/api/modules/{moduleKey}/{id:long}"` | N | N | N |
| `GET` | `/api/{moduleKey}` | N | N | N |
| `GET` | `/api/{moduleKey}/{id:long}` | N | N | N |
| `POST` | `/api/{moduleKey}` | N | N | N |
| `PUT` | `/api/{moduleKey}/{id:long}` | N | N | N |
| `GET` | `"/api/ops/metrics"` | N | Y | N |
| `GET` | `"/api/ops/services"` | N | N | N |
| `GET` | `"/api/ops/services/{name}"` | N | N | N |
| `GET` | `"/api/ops/incidents"` | N | N | N |
| `PATCH` | `"/api/ops/incidents/{id:long}/status"` | N | N | N |
| `GET` | `"/api/ops/config/check"` | N | N | N |
| `GET` | `"/api/security/settings"` | Y | N | Y |
| `PUT` | `"/api/security/settings"` | Y | N | Y |
| `GET` | `"/api/security/events"` | Y | N | Y |
| `GET` | `"/api/security/sso-connections"` | Y | N | Y |
| `POST` | `"/api/security/sso-connections"` | Y | N | Y |
| `PUT` | `"/api/security/sso-connections/{id:long}"` | Y | N | Y |
| `POST` | `"/api/security/sso-connections/{id:long}/disable"` | Y | N | Y |
| `GET` | `"/api/security/access-reviews"` | Y | N | Y |
| `POST` | `"/api/security/access-reviews"` | Y | N | Y |
| `GET` | `"/api/security/access-reviews/{id:long}"` | Y | N | Y |
| `POST` | `"/api/security/access-reviews/{id:long}/items/{iid:long}/approve"` | Y | N | Y |
| `POST` | `"/api/security/access-reviews/{id:long}/items/{iid:long}/revoke"` | Y | N | Y |
| `POST` | `"/api/security/access-reviews/{id:long}/complete"` | Y | N | Y |
| `GET` | `"/api/security/export-requests"` | Y | N | Y |
| `POST` | `"/api/security/export-requests"` | Y | N | Y |
| `POST` | `"/api/security/export-requests/{id:long}/approve"` | Y | N | Y |
| `POST` | `"/api/security/export-requests/{id:long}/reject"` | Y | N | Y |
| `GET` | `"/api/security/insights"` | Y | N | Y |
| `GET` | `"/api/compliance/controls"` | Y | N | N |
| `GET` | `"/api/compliance/evidence"` | Y | N | N |
| `POST` | `"/api/compliance/evidence/generate"` | Y | N | Y |
| `GET` | `"/api/compliance/backup-verifications"` | Y | N | Y |
| `POST` | `"/api/compliance/backup-verifications"` | Y | N | Y |
| `GET` | `"/api/compliance/retention"` | Y | N | Y |
| `PUT` | `"/api/compliance/retention"` | Y | N | Y |
| `GET` | `"/api/fleet-health/summary"` | Y | Y | Y |
| `GET` | `"/api/fleet-health/risks"` | Y | Y | Y |
| `GET` | `"/api/fleet-health/vehicles/{id:long}"` | Y | N | Y |
| `GET` | `"/api/fleet-health/drivers/{id:long}"` | Y | N | Y |

## PlatformEndpoints.cs

Source file total: **24** routes | Auth: **23** | Tests: **2** | Tenant filters: **12**

| Method | Route | Has authorization attribute/check (Y/N) | Has automated test (Y/N) | Has explicit company_id/tenant_id filter in its query (Y/N) |
|---|---|---:|---:|---:|
| `POST` | `"/api/platform/auth/login"` | N | N | N |
| `GET` | `"/api/platform/auth/me"` | Y | N | N |
| `POST` | `"/api/platform/auth/logout"` | Y | N | N |
| `GET` | `"/api/platform/command-center/summary"` | Y | Y | Y |
| `GET` | `"/api/platform/commercial-ops/summary"` | Y | Y | N |
| `GET` | `"/api/platform/tenants"` | Y | N | N |
| `GET` | `"/api/platform/tenants/{id:long}"` | Y | N | Y |
| `POST` | `"/api/platform/tenants"` | Y | N | Y |
| `PUT` | `"/api/platform/tenants/{id:long}"` | Y | N | Y |
| `POST` | `"/api/platform/tenants/{id:long}/status"` | Y | N | Y |
| `POST` | `"/api/platform/tenants/{id:long}/assign-package"` | Y | N | Y |
| `POST` | `"/api/platform/tenants/{id:long}/reset-admin-invite"` | Y | N | N |
| `GET` | `"/api/platform/tenants/{id:long}/audit"` | Y | N | N |
| `GET` | `"/api/platform/tenants/{id:long}/entitlements"` | Y | N | Y |
| `PUT` | `"/api/platform/tenants/{id:long}/entitlements"` | Y | N | Y |
| `GET` | `"/api/platform/packages"` | Y | N | N |
| `POST` | `"/api/platform/packages"` | Y | N | N |
| `PUT` | `"/api/platform/packages/{id:long}"` | Y | N | N |
| `GET` | `"/api/platform/invoices"` | Y | N | Y |
| `POST` | `"/api/platform/invoices"` | Y | N | Y |
| `POST` | `"/api/platform/invoices/{id:long}/mark-paid"` | Y | N | Y |
| `GET` | `"/api/platform/health"` | Y | N | Y |
| `GET` | `"/api/platform/audit"` | Y | N | N |
| `GET` | `"/api/platform/roles"` | Y | N | N |

## Stage9Endpoints.cs

Source file total: **26** routes | Auth: **26** | Tests: **12** | Tenant filters: **26**

| Method | Route | Has authorization attribute/check (Y/N) | Has automated test (Y/N) | Has explicit company_id/tenant_id filter in its query (Y/N) |
|---|---|---:|---:|---:|
| `GET` | `"/api/operations/jobs/{jobId:long}/execution-summary"` | Y | Y | Y |
| `GET` | `"/api/jobs/{jobId:long}/smart-assign/recommendations"` | Y | N | Y |
| `POST` | `"/api/jobs/{jobId:long}/smart-assign/recommend"` | Y | Y | Y |
| `POST` | `"/api/smart-assign/recommendations/{id:long}/accept"` | Y | Y | Y |
| `POST` | `"/api/smart-assign/recommendations/{id:long}/reject"` | Y | N | Y |
| `GET` | `"/api/jobs/{jobId:long}/site-access"` | Y | N | Y |
| `POST` | `"/api/jobs/{jobId:long}/site-access"` | Y | Y | Y |
| `PATCH` | `"/api/site-access/{id:long}"` | Y | Y | Y |
| `GET` | `"/api/jobs/{jobId:long}/access-documents"` | Y | N | Y |
| `POST` | `"/api/jobs/{jobId:long}/access-documents"` | Y | Y | Y |
| `PATCH` | `"/api/access-documents/{id:long}/status"` | Y | N | Y |
| `GET` | `"/api/jobs/{jobId:long}/pickup-authorizations"` | Y | N | Y |
| `POST` | `"/api/jobs/{jobId:long}/pickup-authorizations"` | Y | Y | Y |
| `PATCH` | `"/api/pickup-authorizations/{id:long}"` | Y | N | Y |
| `GET` | `"/api/jobs/{jobId:long}/warehouse-handovers"` | Y | N | Y |
| `POST` | `"/api/jobs/{jobId:long}/warehouse-handovers"` | Y | Y | Y |
| `PATCH` | `"/api/warehouse-handovers/{id:long}"` | Y | N | Y |
| `GET` | `"/api/jobs/{jobId:long}/proof-packages"` | Y | N | Y |
| `POST` | `"/api/jobs/{jobId:long}/proof-packages"` | Y | Y | Y |
| `GET` | `"/api/proof-packages/{id:long}"` | Y | N | Y |
| `PATCH` | `"/api/proof-packages/{id:long}"` | Y | N | Y |
| `POST` | `"/api/proof-packages/{id:long}/submit"` | Y | Y | Y |
| `POST` | `"/api/proof-packages/{id:long}/validate"` | Y | Y | Y |
| `GET` | `"/api/proof-packages/{proofPackageId:long}/artifacts"` | Y | N | Y |
| `POST` | `"/api/proof-packages/{proofPackageId:long}/artifacts"` | Y | Y | Y |
| `GET` | `"/api/proof-packages/{proofPackageId:long}/billing-confidence"` | Y | N | Y |

## RevenueReadinessEndpoints.cs

Source file total: **13** routes | Auth: **13** | Tests: **8** | Tenant filters: **12**

| Method | Route | Has authorization attribute/check (Y/N) | Has automated test (Y/N) | Has explicit company_id/tenant_id filter in its query (Y/N) |
|---|---|---:|---:|---:|
| `POST` | `"/api/jobs/{jobId:long}/mark-ready-to-bill"` | Y | Y | Y |
| `GET` | `"/api/invoice-drafts"` | Y | N | Y |
| `GET` | `"/api/invoice-drafts/{id:guid}"` | Y | Y | Y |
| `POST` | `"/api/jobs/{jobId:long}/invoice-draft"` | Y | Y | Y |
| `PATCH` | `"/api/invoice-drafts/{id:guid}"` | Y | Y | Y |
| `POST` | `"/api/invoice-drafts/{id:guid}/issue"` | Y | N | Y |
| `GET` | `"/api/issued-invoices"` | Y | N | Y |
| `GET` | `"/api/issued-invoices/{id:guid}"` | Y | Y | Y |
| `POST` | `"/api/issued-invoices/{id:guid}/payments"` | Y | N | Y |
| `GET` | `"/api/finance/ar-summary"` | Y | Y | Y |
| `POST` | `"/api/approval-requests/{id:long}/decide"` | Y | N | N |
| `GET` | `"/api/revenue/summary"` | Y | Y | Y |
| `GET` | `"/api/customers/{customerId:long}/summary"` | Y | Y | Y |

## Tenant Isolation Re-Score

Re-score: **64/100** (down from 88/100 in v1).

Reasoning:

- 122/289 mutation routes (42.2%) show an explicit tenant predicate in the audited code path.
- 236/619 total routes (38.1%) show an explicit tenant predicate in the audited code path.
- There is still no RLS and no EF/global query filter.
- Tenant isolation therefore depends on developers remembering to keep `WHERE company_id = ...` / `WHERE tenant_id = ...` clauses alive forever.
- That is a single-point-of-failure architecture, not defense in depth.

Concrete leak estimate: if one tenant predicate were accidentally removed during a refactor, roughly **42%** of mutation routes in the audited surface have an immediately visible tenant predicate to lose. If you include service-backed calls where tenant scoping is only inherited deeper in the stack, the practical exposure is higher, not lower.

## Frontend Data Leak Check

The audited frontend uses `RequirePermission` to hide UI, but the important question is whether the backend still withholds data. In the main operational surfaces, the backend does. The main issue is not backend leakage; it is frontend seed/presentation scaffolding that remains deployable.

| Page / surface | Backend data withheld? | Evidence | Verdict |
|---|---|---|---|
| Live Dashboard / Command Center / Fleet Health | Yes | backend routes use `RequirePermission`/tenant-scoped queries; frontend gate is not the only protection | Backend yes |
| Live Map | Yes | backend telemetry routes are permission-checked and tenant-scoped; page consumes live APIs and SSE | Backend yes |
| Jobs / Trips / Dispatch | Yes | backend routes enforce permissions and company filters; no frontend-only security | Backend yes |
| Reports / Analytics | Yes | backend report and finance routes enforce permissions; some report pages still ship seed presentation scaffolds | Backend yes, but some report-adjacent pages still ship seed presentation scaffolds |
| Executive / Dashboard | Yes | backend summary endpoint is permission-gated, but the page still overlays seed KPI tiles in deployable frontend code | Backend yes, frontend still polluted by seed KPI tiles |
| Operations Proof Center | Yes | backend Stage 9 endpoints require explicit permissions and preserve tenant scope; UI is backed by live APIs | Backend yes |

Important distinction: `OperatingModulePage.tsx` is not a backend data leak problem; it is a deployable synthetic-data problem because it imports `developmentFleetSeedData` from source.

## Money Path Trace

Trace the real money path as implemented today:

| Stage | Real table(s) | Real code evidence |
|---|---|---|
| Charge creation | `job_charges` | `backend-dotnet/Services/BusinessSpineSchemaService.cs:76`, `backend-dotnet/Services/BusinessSpineServices.cs:525` |
| Drafting | `invoice_drafts`, `invoice_draft_lines` | `backend-dotnet/Services/RevenueReadinessSchemaService.cs:31-76`, `backend-dotnet/Services/RevenueReadinessService.cs:267-390` |
| Issuance | `issued_invoices`, `issued_invoice_lines` | `backend-dotnet/Services/FinanceActivationSchemaService.cs:32-110`, `backend-dotnet/Services/RevenueReadinessService.cs:555-760` |
| Payment | `invoice_payments` | `backend-dotnet/Services/FinanceActivationSchemaService.cs:84-110`, `backend-dotnet/Services/RevenueReadinessService.cs:841-905` |

### Real invoice test evidence

The path is not just hypothetical. It is exercised in `backend-dotnet.Tests/RevenueReadinessPostgresTests.cs:353-416` with real amounts: two charges of `100m` and `35m`, an issued invoice, and a payment of `135m`. The test asserts that the invoice becomes `paid`, `AmountPaid == 135m`, `BalanceDue == 0m`, and the payment row is persisted.

Verdict: **yes, the money path has been exercised by an automated test with a real expected dollar amount**.

## Secret-Pattern Cross-Check

This audit searched the repo for hardcoded-looking JWT / signing-key / connection-string / password / API-key patterns in `.cs`, `.json`, `.ts`, and `.yml/.yaml` files. The important distinction is between real literals and env plumbing / test fixtures.

| File:line | Hit | Comment |
|---|---|---|
| `api-dotnet/appsettings.json:3` | hardcoded connection string | production-style credential literal in committed config |
| `backend-dotnet.Tests/FoundationDispatcherPostgresTests.cs:10` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/FoundationPostgresSmokeTests.cs:11` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/RevenueReadinessPostgresTests.cs:15` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/BusinessSpinePostgresTests.cs:10` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/IntegratedModuleSimulationTests.cs:12` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/PlatformCommercialOpsTests.cs:12` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/Stage9PostgresTests.cs:11` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/Stage10PostgresTests.cs:11` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/Stage12TelemetryTests.cs:12` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/Stage13BSafetyMaintenanceTests.cs:11` | hardcoded local connection string | test fixture |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:26` | secret-pattern fixture | password/JWT redaction test input |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:28` | secret-pattern fixture | password/JWT redaction test input |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:88` | secret-pattern fixture | password redaction test input |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:97` | secret-pattern fixture | token/password redaction test input |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:117` | JWT/signing-key fixture | observability config issue test input |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:199` | JWT/signing-key fixture | observability config issue test input |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:327` | secret-pattern fixture | sanitization assertion |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:468` | secret-pattern fixture | sanitization assertion |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:469` | secret-pattern fixture | sanitization assertion |
| `backend-dotnet.Tests/P9ObservabilityTests.cs:470` | secret-pattern fixture | sanitization assertion |
| `backend-dotnet/Controllers/EndpointMappings.cs:4557` | API key env lookup | not a hardcoded secret literal, but a committed secret-path reference |
| `backend-dotnet/Controllers/PlatformEndpoints.cs:84` | session token query | not a hardcoded secret literal |
| `backend-dotnet/Controllers/EndpointMappings.cs:2012` | session token query | not a hardcoded secret literal |
| `backend-dotnet/Controllers/EndpointMappings.cs:2053` | password hash update | not a secret literal |
| `backend-dotnet/Data/Database.cs:14` | PG_CONNECTION env fallback | not a hardcoded secret literal |
| `backend-dotnet/appsettings.json:2` | env-only config note | not a hardcoded secret literal |
| `backend-dotnet/appsettings.json:3` | env-only config note | not a hardcoded secret literal |
| `docker-compose.yml:27` | PG_CONNECTION env wiring | not a hardcoded secret literal |
| `render.yaml:17` | PG_CONNECTION env wiring | not a hardcoded secret literal |
| `frontend/src/services/apiClient.ts:100` | JWT comment | not a hardcoded secret literal |
| `backend/src/lib/env.ts:8` | PG_CONNECTION env var schema | not a hardcoded secret literal |
| `backend/src/lib/db.ts:32` | DATABASE_URL / PG_CONNECTION env fallback | not a hardcoded secret literal |

Bottom line: the repository still contains hardcoded connection-string literals in committed source, plus multiple test fixtures that intentionally embed password/JWT-like strings for redaction checks. The strongest production-style issue is `api-dotnet/appsettings.json:3`.

## Module Re-Verdicts

The v1 audit was too generous on the following modules because deployable frontend code still contains synthetic data or presentation scaffolding.

| Module | v1 verdict | v2 verdict | Why it changed |
|---|---|---|---|
| Dashboard | REAL-END-TO-END | PARTIAL | `frontend/src/pages/ExecutivePage.tsx:25` ships `SEED_KPIS` in deployable code. |
| Jobs/Trips | REAL-END-TO-END | PARTIAL | `frontend/src/pages/OperatingModulePage.tsx:37` imports `developmentFleetSeedData`, which imports `mockOperatingData`; that can run in deployed environments. |
| Live Map | REAL-END-TO-END | REAL-END-TO-END | `frontend/src/pages/LiveMapPage.tsx` uses live telemetry APIs and SSE, with no seed fallback imported. Local telemetry seed scripts exist, but they are clearly local init fixtures. |
| Reports | REAL-END-TO-END | PARTIAL | `frontend/src/pages/SlaKpiPage.tsx:16-21` and `frontend/src/pages/ExecutivePage.tsx:25` still carry seed-backed reporting/dashboard presentation data. |

### Seed-data location test

- Seed data under `database/init/005_local_module_test_data.sql` and `database/init/006_local_telemetry_live_state_seed.sql` is clearly local-init-only.
- Seed data imported in `frontend/src/pages/ExecutivePage.tsx` and `frontend/src/pages/OperatingModulePage.tsx` is not local-only; it is deployable frontend code and can run in a shipped build.
- That is why Dashboard, Jobs/Trips, and some Reports surfaces are downgraded, while Live Map is not.

## Score Changes Relative to v1

- Tenant isolation: **88 -> 64** because there is no RLS/global query filter and the isolation model depends on manual predicates everywhere.
- Demo masking risk: **72 -> 58** because seeded presentation logic still ships in deployable frontend code, not just local fixtures.
- Production readiness: **74 -> 69** because the repo still mixes real backend surfaces with deployable synthetic data in a few visible modules.

## Final Verdict

The backend is real. The audit trail is real. The money path is real. The weakest part is still tenancy defense-in-depth and a handful of frontend surfaces that remain demo-shaped even though the backend is no longer fake.
