# OpsTrax — Demo Tenant + Auth-Boundary Report

Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git`
(the `zayra` remote is the off-limits sibling; not touched). UI/UX Phase 1 and RLS
staging activation were not touched (the only Program.cs change is adding `customer_id`
to the pre-existing auth context — RLS logic untouched).

Full suite: **876 passed / 0 failed / 0 skipped** (5 new tests this session).

---

## STEP 0 — Auth-boundary gap CLOSED (delta)

**The gap was real.** The `Customer Portal User` and `Customer` roles carry
`shipments:view` / `alerts:view` — permissions **shared with internal endpoints**.
So a customer-portal principal could pass `RequirePermission("shipments:view")` on an
internal endpoint and see the whole company's shipments (company_id-only scoping).

**Fix (reusable, not per-endpoint):**
- The auth middleware now stashes the user's `customer_id` (`AuthCustomerIdItemKey`) —
  non-null only for customer-portal users.
- `RequirePermission` now rejects any customer-portal-bound principal (403) for **any
  non-`customer_portal:*` permission**. This automatically covers **every** permission-gated
  internal endpoint, including the `shipments:view`/`alerts:view` overlap — the principal
  is rejected even though its role grants the permission.
- Added a reusable `RequireInternalUser(http)` gate for endpoints without a per-permission
  check (used e.g. by the seed endpoint).

**Tested explicitly** (`CustomerPortalAuthBoundaryTests`, 4 tests, no DB — runs in CI)
across **7 internal permissions / modules**: `fleet:view`, `shipments:view`,
`customers:view`, `drivers:view`, `dispatch:view`, `finance.invoice.read`, `alerts:view`:
```
Internal user (no customer_id)   → RequirePermission == null (allowed) for all 7
Customer-portal user (customer_id set) → RequirePermission != null (403) for all 7,
    rejected OUTRIGHT (no authorization decision / data query reached)
Customer-portal user + customer_portal:view → still allowed (portal keeps its access)
Overlap proof: role HAS shipments:view, yet the binding still returns 403
RequireInternalUser: 403 for customer principal, null for internal
```

---

## STEP 1 — Demo tenant, built via the REAL service layer

**Tool:** a demo-tenant seeder (`DemoTenantSeeder`) exposed by a **triple-gated** endpoint
`POST /api/dev/seed-demo-tenant` (`DevSeedEndpoints`):
1. **Not mapped at all** when `ASPNETCORE_ENVIRONMENT=Production` (route doesn't exist).
2. Requires explicit opt-in `DemoSeed:Enabled=true` (else 404).
3. Rejects customer-portal principals (`RequireInternalUser`).
→ It is impossible to trigger against a live production tenant.

**Uses the REAL service/business-logic layer** (the same code paths a user action hits):
- `BusinessSpineService.CreateRateCardAsync` / `CreateJobChargeAsync`
- `RevenueReadinessService.MarkJobReadyToBillAsync → CreateInvoiceDraftFromJobAsync →
  UpdateInvoiceDraftAsync(approved) → (approval decision) → IssueInvoiceFromDraftAsync →
  RecordInvoicePaymentAsync` (the full charges→draft→issue→payment chain, with the real
  approval gate)
- `CustomerPortalService.SubmitFeedbackAsync` (enforces job-ownership)

Base entities (companies, customers, vehicles, drivers, jobs, trips, dispatch, proofs,
alerts/safety/work-orders) have **no dedicated service layer** in the codebase, so they
are created directly — reported honestly.

**Seeded (exact counts), tenant "Meridian Logistics — Demo" (`MERIDIAN-DEMO`):**

| Entity | Count | Notes |
|---|---|---|
| Vehicles | 5 | Truck/Van/Reefer/Box, statuses Available/On Route/Maintenance |
| Drivers | 5 | 1 with an **expiring** DOT Medical Card (expires in 12 days) |
| Customers | 3 | with contact info |
| Jobs | 12 | **9 distinct statuses** after seeding: draft, scheduled, assigned, in_progress, exception, cancelled, completed, delivered, **ready_to_bill** (the 4 finance jobs) |
| Trips (+ stops) | 4 | incl. **1 exception** trip |
| Dispatch assignments | 4 | accepted / in_transit / rejected / delivered (lifecycle) |
| Proof packages | 3 | **validated / rejected / pending** |
| Issued invoices | 4 | via the real finance chain |
| Payments | 1 | $1,450.00 (real `RecordInvoicePaymentAsync`) |
| Customer feedback | 1 | via real `SubmitFeedbackAsync` |
| Telemetry alerts | 2 | harsh-braking, geofence-exit |
| Safety events | 1 | Speeding (Under Review) |
| Work orders | 1 | Brake inspection (Open) |
| Login users | 2 | 1 internal admin, 1 customer-portal |

**AR aging spread (real chain + demo aging):** paid $1,450.00 · current $2,100.50 ·
31-60 overdue $875.25 · 90+ overdue $3,300.00 → **outstanding $6,275.75**.

**Idempotent:** the seeder checks for `company_code='MERIDIAN-DEMO'` first; a second run
returns `AlreadySeeded=true` and creates nothing (verified in the test).

---

## STEP 2 — Walkthrough verification (KPIs hand-calculated against seeded data)

The seeder integration test (`DemoTenantSeederPostgresTests`) verifies the numbers a
demo viewer would see, by hand-calculation:
- **KPI #1 — Jobs:** 12 rows, and each of draft/scheduled/assigned/in_progress/completed/
  cancelled/exception present (≥1).
- **KPI #2 — AR aging (Finance + Portal invoice view):** `Current == 2100.50`,
  `Days31To60 == 875.25`, `Days90Plus == 3300.00`, `TotalOutstanding == 6275.75`.
- **KPI #3 — AR summary:** `IssuedInvoiceCount == 4`, `PaidBalance == 1450.00`,
  `OpenBalance == 6275.75`.
- Proof lifecycle: exactly one each of validated / rejected / pending.
- A direct multi-module query pass confirmed **real data in every module** (Fleet 5,
  Drivers 5 + 1 expiring doc, Customers 3, Trips 4 + 1 exception, Dispatch 4, Proofs
  validated/rejected/pending, Invoices 4 / $6,275.75 outstanding / $1,450 paid, Feedback
  1, Alerts 2, Safety 1, Work orders 1).

*The frontend portal + finance pages compile/build; a live in-browser click-through was
not performed in this environment (no running app + browser available here), but the
data completeness and KPI correctness are proven by the tests + query pass above.*

### Bugs surfaced during seeding (reported, not hidden)

1. **Customer-feedback ownership mismatch (real bug — FIXED).** Seeding through the real
   `SubmitFeedbackAsync` surfaced that the seeder was filing feedback for a job that did
   **not** belong to the target customer (jobs are round-robin-assigned to customers, so
   `completedJobs[0]` belonged to a different customer). The real service **correctly
   rejected it** (returned null), but the seeder had hard-coded the count as 1 — so it
   *reported* 1 feedback while the DB had 0. Fixed to file against a job the customer owns
   and to derive the count from the service result; the test now asserts the DB row count
   (guards regression). **This is exactly the value of seeding through real logic.**
2. **`completed` → `ready_to_bill` transition (expected behavior, not a bug).** Running the
   real finance chain transitions a completed job's status to `ready_to_bill`; the initial
   seeder/test assumed completed jobs stay `completed`. Adjusted the job mix so a
   `completed` (and `delivered`) job remains for the status showcase. Real behavior, surfaced
   and accounted for.
3. **Seeder column typo (`trip_stops.sequence` → `stop_sequence`)** — a bug in my seeder
   code, fixed. Not a product bug.

No other product bugs surfaced; the finance chain, charges, approval gate, payments, and
feedback all executed correctly through the real services.

---

## STEP 3 — Access path & credentials (usable now)

The demo tenant is **seeded and persistent** in the local DB (`opstrax_local`), so it is
immediately usable against a locally-running app.

**Login (`POST /api/auth/login`, or the app's sign-in screen):**

| Role | Email | Password |
|---|---|---|
| Internal (Fleet Manager — full app) | `admin@meridian.demo` | `MeridianDemo!23` |
| Customer Portal (Acme Freight) | `portal@acme.demo` | `MeridianDemo!23` |

- The internal admin (33 permissions) sees Dashboard, Fleet, Drivers, Jobs, Trips,
  Dispatch, Proof Center, Finance/AR, Alerts, etc.
- The customer-portal user (`/customer-portal`) sees **only Acme's own** invoices (with
  AR status), shipments, proofs, and feedback — and is **rejected (403)** from every
  internal endpoint (STEP 0 boundary).
- Passwords use the app's demo-password mechanism (`users.demo_password`, `password_hash`
  null → fallback verified).

**To (re)seed in any non-production environment:** set `DemoSeed:Enabled=true`, sign in as
an internal user, and `POST /api/dev/seed-demo-tenant`. Idempotent — safe to call repeatedly.

**Files this session:**
`backend-dotnet/Services/DemoTenantSeeder.cs`, `backend-dotnet/Controllers/DevSeedEndpoints.cs`,
`backend-dotnet/Controllers/EndpointMappings.cs` (auth boundary), `backend-dotnet/Program.cs`
(customer_id in auth context + DI/map), `backend-dotnet.Tests/CustomerPortalAuthBoundaryTests.cs`,
`backend-dotnet.Tests/DemoTenantSeederPostgresTests.cs`.
