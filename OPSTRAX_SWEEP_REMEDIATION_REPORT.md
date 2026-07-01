# OpsTrax — Tenant-Filter Sweep Remediation Report

Session scope: the **15 actionable items** (Category A + B) from
`OPSTRAX_TENANT_FILTER_SWEEP.md`. Backend-only. Category C (42 items) untouched.
Pre-Session-2 (RLS activation remains a separate session).

- Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git` (the `zayra` remote is the off-limits sibling; not touched).
- Files changed (this session): **3** —
  `backend-dotnet/Controllers/EndpointMappings.cs`,
  `backend-dotnet/Services/AuditService.cs`,
  `backend-dotnet/Services/ScheduledReportBackgroundService.cs`
  (`+478 / −342`). No test files. No frontend / api-dotnet / .gitignore /
  tokens.ts / RLS-migration touched (those are prior Phase 0 / Session 1 /
  UIUX changes, left intact).

---

## A1 — `AuditService` hardcoded `company_id=1` (129 call sites)

**Step 1 — context audit (checked every site, did not assume).** All 129 call
sites resolve to an authenticated request context or a true background worker:
- **127** in `EndpointMappings.cs` — every one reachable only via an
  authenticated `/api/*` route (tenant context is always resolvable).
  - **118** already had `HttpContext http` in scope.
  - **9** named handlers lacked the `http` param but are live tenant routes, so
    context was *available* — threaded `http` in: `CreateGenericModuleRecord`,
    `UpdateGenericModuleRecord`, `CostMarginRecalculate`, `CostMarginRecalculateJob`,
    `CostLeakageAcknowledge`, `CostLeakageCreateAction`, `HosCertify`,
    `EldMarkMalfunction`, `CreateAuditPackage`.
  - Several shared helpers surfaced by the compiler also needed `http` threaded
    (`SimpleAction`, `SimpleUpdateStatus` +9 callers, `DispatchAutoSuggest`,
    `DeleteEntity`, `SoftDelete`, the `CreateModuleRecord`/`UpdateModuleRecord`
    delegates). All authenticated routes → all migrated to the tenant-correct
    overload.
- **2** in `ScheduledReportBackgroundService` — a true background worker with **no
  request context**.

**→ Migration result:**
| Path | Count |
|---|---|
| Migrated to `LogAsync(HttpContext http, …)` (real `company_id`) | **127** |
| Routed to new `LogSystemAsync(…)` (no tenant context) | **2** |
| Left fabricating `company_id=1` | **0** |

**Step 2/3 — no fabricated-tenant path remains.** The broken overload
`LogAsync(action, entity, …) → VALUES (1, …)` was **removed and replaced** by
`LogSystemAsync(…)`, which writes a **platform sentinel `company_id = 0`** (the
column is `NOT NULL`, no FK, and no real company owns id 0 — min id is 1). So no
code path can write a fabricated real tenant id.

**Step 4 — old signature is gone.** `LogAsync(string, string, …)` no longer
exists; the compiler is the guard. Verified: **0** remaining `.LogAsync("…"`
(simple-overload) calls anywhere in `backend-dotnet`.

**Login special-case (checked, not assumed).** `POST /api/auth/login` is in the
public allowlist, so the middleware is bypassed and `http.Items` is empty at that
point — `GetCompanyId(http)` would throw. The user is authenticated there and the
`user` row carries `company_id`, so I establish the authenticated user's tenant
context (`http.Items[AuthCompanyIdItemKey]/[AuthUserIdItemKey]`) *before* the
audit call, then use the `http` overload. Login now audits against the real
company.

**Step 5 — EXISTING DATA (flagged for Zack, NOT auto-remediated).**
Local `opstrax_local`: **146 of 146** `audit_logs` rows have `company_id=1`
(the entire table, since the bug hardcoded it). These cannot be re-attributed
without knowing the true tenant per historical row. **→ OWNER ACTION:** decide on
data remediation in each real environment (Neon/Render) — quantify
`SELECT count(*) FROM audit_logs WHERE company_id=1` there and choose
purge / archive / best-effort backfill. Not guessed or backfilled here.

---

## A2–A5 — `VALUES (1, …)` writers (same shape as Session-1 `vehicle_assignments`)

Sourced `company_id` from the request context (via a threaded `companyId` /
`GetCompanyId(http)`), matching the Session-1 pattern. Before → after:

| Item | Table | Writer / callers threaded | Before | After |
|---|---|---|---|---|
| A2 | `cost_leakage_actions` | `CostLeakageCreateAction` (has http) | `VALUES (1, …)` | `VALUES (@cid, …)` ✓ |
| A3 | `document_timeline_events` | `AddDocumentEvent` + 3 callers | `VALUES (1, …)` | `VALUES (@cid, …)` ✓ |
| A4 | `entity_timeline_events` | `AddTimeline` + 22 callers | `VALUES (1, …)` | `VALUES (@cid, …)` ✓ |
| A5 | `work_order_status_events` | `AddWorkOrderEvent` + 4 callers | `VALUES (1, …)` | `VALUES (@cid, …)` ✓ |

Verified: **0** remaining `INTO {those tables} … VALUES (1,` and no `VALUES (1,`
in `AuditService`.

**Build+test after Category A:** Build succeeded, **862 passed / 0 failed / 0 skipped**.

---

## B1–B10 — LIVE-UNPROTECTED reads

Added `WHERE company_id=@cid` (reusing the existing tenant-filter pattern;
threaded `http` where the handler lacked it). Subquery aggregates over
tenant-owned tables were scoped too (all of `idling_events`, `fuel_transactions`,
`jobs`, `work_orders`, `vehicles`, `customer_communications`, `maintenance_items`
carry `company_id`).

| # | Endpoint | Sensitivity | Fix |
|---|---|---|---|
| B1 | `POST /api/cost-margin/jobs/{jobId}/recalculate` | FINANCE | `FROM jobs WHERE id=@id` → `… AND company_id=@cid` |
| B2 | `GET /api/customer-eta/summary` (summary) | CUSTOMER | `FROM jobs j WHERE …` → `… j.company_id=@cid …` |
| B3 | `GET /api/customer-eta/summary` (jobs list) | CUSTOMER | `… j.company_id=@cid …` |
| B4 | `GET /api/customer-eta/communications` | CUSTOMER | added `WHERE cc.company_id=@cid` |
| B5 | `POST /api/dispatch/send-eta-updates` | CUSTOMER (action) | job selection scoped `… company_id=@cid …` (no longer touches other tenants' jobs) |
| B6 | `GET /api/compliance/documents` | CUSTOMER/COMPLIANCE | `FROM documents d WHERE …` → `… d.company_id=@cid …` |
| B7 | `GET /api/fleet/utilization` | OPERATIONAL | `vehicles` + idle/fuel/jobs subqueries scoped |
| B8 | `GET /api/fleet/utilization/summary` | OPERATIONAL | `vehicles` + idle/fuel subqueries scoped |
| B9 | `GET /api/workforce/drivers` | OPERATIONAL | `FROM drivers d` → `… WHERE d.company_id=@cid …` |
| B10 | `GET /api/maintenance/summary` | OPERATIONAL | `maintenance_items` + vehicles/work_orders subqueries scoped |

**Build+test after Category B:** Build succeeded, **862 passed / 0 failed / 0 skipped**.

---

## Verify (steps 10–11)

- **Final build:** `Build succeeded — 0 warnings, 0 errors`.
- **Final tests:** `Passed! — Failed: 0, Passed: 862, Skipped: 0`.
- **Re-ran the sweep:** **0 remaining actionable Category A or B hits.**
  - Category-A tables: no `VALUES (1,` remains on any of the 5 live-write tables.
  - Category-B endpoints: all 10 now carry `company_id=@cid` (verified per endpoint).
  - Remaining raw sweep hits are **all Category C** and were deliberately left:
    transitive by-id subqueries in `VehicleDetail`/`CustomerDetail`/`JobDetail`
    (parent already company-checked); `ValidateJob`/`ValidateDvir` existence
    checks; the `DocumentsBaseSql` constant (scoped at every call site); and the
    two `TripBackgroundService` worker queries (all-tenant by design). Remaining
    Category-2 hits are all `Batch*SchemaService` seed / reference-data `id`-PK
    literals.

---

## Additional gaps observed (NOT fixed — outside the 15-item scope; flagged)

- **`SimpleUpdateStatus`** (`compliance violation ack/resolve`, `sla breach
  ack/resolve`, `eld resolve-malfunction`, `audit-package finalize`, `scheduled
  report pause/resume`) does `UPDATE {table} SET status WHERE id=@id` with **no
  company filter** — a cross-tenant *write*. I only threaded `http` for its audit
  attribution (A1); its UPDATE scoping is not in the 15 items. Recommend a
  follow-up.
- **`GET /api/customer-eta/recommendations`** reads `ai_recommendations` with no
  `company_id` (minor; not among the 10).
- These are RLS-backstopped once Session 2 lands.

---

## Compliance statement

- **No Category C item was touched.**
- **No existing test was weakened, disabled, or skipped** — 862/862 throughout,
  the same baseline, and no test files were modified.
- No fabricated real `company_id` remains on any write path (system path writes
  sentinel `0`; all tenant paths write the real `company_id`).
- Existing mis-attributed `audit_logs` data is **reported, not silently altered**.

Session 2 (RLS activation — Option A1, `SET LOCAL` + request-scoped transaction)
remains its own separate session, to start after this report is confirmed clean.
