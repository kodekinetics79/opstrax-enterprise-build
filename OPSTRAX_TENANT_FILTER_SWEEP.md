# OpsTrax — Tenant-Filter Bug Sweep (grep pass, NO fixes applied)

Mechanical sweep for the two bug shapes found in Session 1
(`carbon-emissions` missing `company_id`; `vehicle_assignments` hardcoded
`company_id=1`). Method: extracted every SQL string literal in
`backend-dotnet/{Controllers,Services,Foundation}` and tested for
(1) `FROM vehicles|drivers|trips|jobs` with **no** `company_id/tenant_id` in the
statement, and (2) hardcoded `company_id=1` / `VALUES(1,…)`. Heuristic — verified
each hit by hand and classified below. **Nothing was changed.**

## Counts

| Category | Count |
|---|---|
| **A. LIVE-DATA-CORRUPTION-RISK** (hardcoded `company_id=1` on a live write path) | **5 write helpers** (one — `AuditService` — is reached from **129 call sites**) |
| **B. LIVE-UNPROTECTED** (reachable authenticated read, no tenant filter) | **10 query sites** (≈7 distinct endpoints) |
| **C. INTERNAL-ADMIN/SYSTEM-SAFE or not-a-bug** | **42** (3 transitively-scoped · 9 validation-weak · 1 false-positive · 2 background workers · 27 seed/reference-init) |

Raw scan totals: Category-1 (missing filter) = 25 hits; Category-2 (hardcoded id)
= 32 hits. After classification, the actionable set is **A (5) + B (10)**.

---

## A. LIVE-DATA-CORRUPTION-RISK — hardcoded `company_id=1` on live writes
*(sorted: audit-integrity/blast-radius, then finance → customer → operational)*

| # | File:line | Table | Sensitivity | Note |
|---|---|---|---|---|
| A1 | `backend-dotnet/Services/AuditService.cs:16` | `audit_logs` | **AUDIT INTEGRITY (cross-cutting)** | `LogAsync(action,entity,…)` overload inserts `VALUES (1, …)`. **129 call sites** use this overload (vs 84 using the correct `LogAsync(http,…)` at `:41`). Every one of those 129 audit rows is mis-attributed to tenant 1 → under RLS tenant 1 sees all tenants' audit trail, others see none of theirs. Highest-impact item in the sweep. |
| A2 | `backend-dotnet/Controllers/EndpointMappings.cs:6678` | `cost_leakage_actions` | **FINANCE** | `CostLeakageCreateAction` — `INSERT … VALUES (1, @itemId, …)`. Live route write (`POST /api/cost-leakage/items/{id}/create-action`). |
| A3 | `backend-dotnet/Controllers/EndpointMappings.cs:5534` | `document_timeline_events` | **CUSTOMER / COMPLIANCE** | `AddDocumentEvent` — `VALUES (1, @id, …)`. Called by document create/update/renew handlers. |
| A4 | `backend-dotnet/Controllers/EndpointMappings.cs:4856` | `entity_timeline_events` | **OPERATIONAL** | `AddTimeline` — `VALUES (1, @type, @id, …)`. Called by vehicle/driver/customer/asset mutations (incl. the `ChangeEntityStatus` path). |
| A5 | `backend-dotnet/Controllers/EndpointMappings.cs:5450` | `work_order_status_events` | **OPERATIONAL** | `AddWorkOrderEvent` — `VALUES (1, @id, …)`. Called on work-order status changes. |

> Same exact bug class as the `vehicle_assignments VALUES (1,…)` fixed in Session 1.
> A1 is the priority: it is a shared logger on 129 mutation paths.

---

## B. LIVE-UNPROTECTED — authenticated reads with no `company_id`
*(sorted: finance → customer → operational)*

| # | File:line | Endpoint / handler | Sensitivity | Query (trimmed) |
|---|---|---|---|---|
| B1 | `EndpointMappings.cs:6621` | `CostMarginRecalculateJob` (`POST /api/cost-margin/jobs/{jobId}/recalculate`) | **FINANCE** | `SELECT revenue_estimate, cost_estimate, margin_estimate FROM jobs WHERE id=@id` — `jobId` not scoped to company on the recalc path. |
| B2 | `EndpointMappings.cs:3714` | `CustomerEtaSummary` (`GET /api/customer-eta/summary`) | **CUSTOMER** | `SELECT COUNT(*) … FROM jobs …` no `company_id` — cross-tenant ETA/SLA counts. |
| B3 | `EndpointMappings.cs:3724` | customer-eta list (same module) | **CUSTOMER** | job list w/ `customer_name, driver_name, vehicle_code, tracking_code` — no `company_id`. |
| B4 | `EndpointMappings.cs:3740` | customer-eta communications/list | **CUSTOMER** | job list w/ `customer_name, pickup_address, dropoff_address` — no `company_id`. |
| B5 | `EndpointMappings.cs:3474` | `DispatchSendEtaUpdates` (`POST /api/dispatch/send-eta-updates`) | **CUSTOMER (action)** | `SELECT id FROM jobs WHERE deleted_at IS NULL AND (status IN ('Delayed','At Risk') …) LIMIT 10` — selects **other tenants'** jobs and triggers ETA comms on them. |
| B6 | `EndpointMappings.cs:1221` | documents/compliance doc list (review which route) | **CUSTOMER / COMPLIANCE** | `SELECT d.* FROM documents d LEFT JOIN (… drivers UNION vehicles …) WHERE d.deleted_at IS NULL` — outer `documents` not scoped. |
| B7 | `EndpointMappings.cs:625` | `GET /api/fleet/utilization` | OPERATIONAL | `SELECT v.* … FROM vehicles v …` no `company_id`. |
| B8 | `EndpointMappings.cs:669` | `GET /api/fleet/utilization/summary` | OPERATIONAL | `SELECT COUNT(*) … FROM vehicles …` no `company_id`. *(already flagged in Session 1 delta.)* |
| B9 | `EndpointMappings.cs:1169` | `GET /api/workforce/drivers` | OPERATIONAL | `SELECT d.* … FROM drivers d ORDER BY …` no `company_id`. |
| B10 | `EndpointMappings.cs:3842` | `MaintenanceSummary` (`GET /api/maintenance/summary`) | OPERATIONAL | aggregate over `vehicles`/maintenance, no `company_id`. |

---

## C. INTERNAL-ADMIN / SYSTEM-SAFE or not-a-bug (no action needed)

**C1. Transitively-scoped (parent already company-validated) — LOW risk**
- `EndpointMappings.cs:2666` — `VehicleDetail` → `SELECT * FROM trips WHERE vehicle_id=@id` (vehicle validated by company first).
- `EndpointMappings.cs:2718` — `CustomerDetail` → `SELECT * FROM jobs WHERE customer_id=@id`.
- `EndpointMappings.cs:2818` — `JobDetail` → `SELECT revenue_estimate … FROM jobs WHERE id=@id` (finance; relies on JobDetail's own company check — worth confirming).

**C2. Validation-weak FK existence checks (cross-tenant FK linking risk) — SECONDARY**
- `EndpointMappings.cs:5206,5207,5209,5219,5220,5230,5231` — `ValidateJob` (`SELECT COUNT(*)/status FROM drivers|vehicles|jobs WHERE id=@id`).
- `EndpointMappings.cs:5466,5467` — `ValidateDvir` (same shape).
- These run inside permission+tenant-gated create/update handlers, but do **not** confirm the *referenced* `driverId/vehicleId` belongs to the caller's company — so a tenant could link another tenant's driver/vehicle by id. Not a read leak; flag as a follow-up hardening item.

**C3. False positive (scoped at call site)**
- `EndpointMappings.cs:3825` — `DocumentsBaseSql` constant; callers append `WHERE d.company_id=@cid` (verified in Session-2/Step-2 work). The `vehicles/drivers` hits are name-lookup subqueries by id.

**C4. Background workers (all-tenant by design) — SYSTEM-SAFE**
- `backend-dotnet/Services/TripBackgroundService.cs:113,382` — `SELECT id FROM trips WHERE …` (worker processes every tenant's trips).

**C5. Seed / reference-data init (SchemaService `EnsureAsync`) — SEED-SAFE (≈27 Category-2 hits)**
- `Services/Batch2SchemaService.cs:173,176,209`; `Batch5SchemaService.cs:317,372` — demo seed of `route_stops`/`customers`/`expense_categories` under the demo tenant `company_id=1`.
- `Services/Batch6SchemaService.cs:324,333,346,359,392,405,418,431,449,457`; `Batch7SchemaService.cs:296,320,332,346,380,404,438,452,466,570,582` — `OVERRIDING SYSTEM VALUE … VALUES (1, …)` where the literal `1` is a **primary-key `id`** for global/reference rows (compliance profiles, HOS rulesets, KPI/report seeds), not a tenant id. Startup seeding only, gated by demo-seed config.

---

## Recommended priority for the fix session (not done here)
1. **A1 `AuditService` (129 sites)** — make the simple overload tenant-aware or migrate callers to the `http` overload. Highest blast radius.
2. **A2 (finance) → A3 (customer/compliance) → A4/A5 (operational)** — thread session `company_id` into the four event-writer helpers (same fix shape as `vehicle_assignments`).
3. **B1 (finance) → B2–B6 (customer) → B7–B10 (operational)** — add `RequirePermission` (where missing) + `WHERE company_id=@cid`, reusing existing patterns.
4. **C2 validation-weak FK checks** — scope referenced FKs by company (secondary hardening).
