# OpsTrax — Customer Portal Completion Report

The wedge feature: a real, authenticated customer-facing portal that exposes ONLY
a customer's own data (jobs, trip status, proof-of-delivery, invoices from the
Finance module, feedback intake) — with a **new customer-within-tenant isolation
boundary** and query-level internal-field stripping, every piece proven by a real
Postgres integration test. UI/UX Phase 1 and RLS staging activation were not
touched.

- Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git` (the `zayra` remote is the off-limits sibling; not touched).
- Files this session:
  - `database/migrations/2026_06_30_stage21_customer_portal.sql` **(new, additive)**
  - `backend-dotnet/Services/CustomerPortalService.cs` **(new)**
  - `backend-dotnet/Controllers/CustomerPortalEndpoints.cs` **(new)**
  - `backend-dotnet.Tests/CustomerPortalPostgresTests.cs` **(new — 4 integration tests)**
  - `backend-dotnet/Program.cs` (+2 lines: DI registration + endpoint map — not the RLS middleware)
  - `frontend/src/services/portalApi.ts`, `frontend/src/pages/CustomerPortalPage.tsx` **(new)**
  - `frontend/src/App.tsx` (+1 route wired to the real page; App.tsx was not part of the on-hold diff)

---

## STEP 0 — Audit: what was real vs stub (checked before building)

| Thing | Reality found | Decision |
|---|---|---|
| `customer_users` table (per audit) | **Does NOT exist**; no customer-auth table or code anywhere | Extend existing auth: a portal customer-user is a `users` row bound to a new `customer_id` — no parallel auth subsystem |
| `/api/customer-eta/*`, `/api/customer-visibility/*` | Real but **internal/dispatcher-facing** (company-scoped) or **anonymous token tracking** — not a logged-in customer portal | Left as-is; built a new `/api/portal/*` family scoped to the authenticated customer |
| `/customer-portal` frontend route | Stub — rendered `CustomerEtaPage` (the ETA page) | Rewired to a new real `CustomerPortalPage` |
| `customer_feedback` table | **Exists** (company_id, job_id, customer_id, rating, comment, feedback_type) but no status lifecycle | Reused; added `status`/`subject`/`updated_at` (additive) |
| `proof_packages` / `proof_artifacts` | **Exist**, rich — many internal fields | Reused; selected only customer-safe columns |
| `issued_invoices` / `invoice_payments` (Finance) | **Exist** (customer_id present) | Reused directly for the portal invoice view |

**Schema change (additive migration `stage21`):** `users.customer_id` (+ partial
index), and `customer_feedback.status` / `subject` / `updated_at`. No new/parallel
tables.

---

## The new security boundary (customer-within-tenant)

Internal endpoints scope by `company_id`. That is **insufficient** for a portal —
two customers of the same tenant must not see each other. Every portal endpoint:
1. `RequirePermission("customer_portal:view")` (existing permission);
2. resolves the authenticated user's **`customer_id`** (`ResolveCustomerIdForUserAsync`);
   a user with no `customer_id` (internal staff) is **denied 403**;
3. scopes **every** query by `company_id AND customer_id` (and, for proofs/feedback,
   by a job the customer owns).

This is treated as its own boundary, not an extension of tenant RBAC, and is tested
**separately** from company-level isolation.

---

## New endpoints — self-check (Step 5b)

| Endpoint | Auth enforced | Customer-within-tenant isolation tested | Internal-field stripping tested |
|---|---|---|---|
| `GET /api/portal/invoices` | ✅ `customer_portal:view` + customer_id (403 if not portal user) | ✅ `PortalInvoices_AreScopedToTheCustomer…` — A sees only A's 2 invoices, never B's `INV-B-1` | ✅ selects only safe invoice columns (no cost/margin/internal refs) |
| `GET /api/portal/jobs` | ✅ same | ✅ jobs scoped by customer_id (seeded per customer across tests) | ✅ same SELECT as job detail (below) |
| `GET /api/portal/jobs/{id}` | ✅ same | ✅ ownership in query (`AND customer_id=@customerId`) | ✅ `PortalJobDetail_StripsInternalFields…` — asserts `riskScore/costEstimate/marginEstimate/revenueEstimate/notes/assignedDriverId` all ABSENT + no AI/driver text in payload |
| `GET /api/portal/jobs/{id}/proofs` | ✅ same | ✅ `PortalProofs…` — customer B gets **empty** for A's job | ✅ `PortalProofs…` — asserts `capturedByUserId/deviceId/validationSummary/notes/metadataJson/correlationId` ABSENT + no "INTERNAL" text |
| `POST /api/portal/feedback` | ✅ same | ✅ `PortalFeedback…` — a customer **cannot** file against another customer's job (returns null → 404) | n/a (write) |
| `GET /api/portal/feedback` | ✅ same | ✅ `PortalFeedback…` — B does NOT see A's feedback; other tenant's never appears | ✅ selects only customer-safe feedback columns |

---

## STEP 1 — Customer-safe invoice view

`GetOwnInvoicesAsync(company, customer)` returns the customer's `issued_invoices`
(safe columns) + their `invoice_payments`, plus a **plain-English AR status**
(`DeriveArStatus` → "Paid" / "Due in N day(s)" / "Overdue N day(s)") — never raw
aging-bucket jargon. No `subtotal→margin`, no cost, no internal refs.

**Test assertions (`PortalInvoices_…`):** seeds customer A (invoices `INV-A-1`
$1800 due +12d, `INV-A-2` $2450.75 paid) and customer B (`INV-B-1` $9999.99 overdue)
in the SAME company; a portal user for A and an internal staff user.
```
ResolveCustomerIdForUserAsync(portalUserA) == customerA
ResolveCustomerIdForUserAsync(internalStaff) == null   (denied — not a portal user)
GetOwnInvoicesAsync(companyId, customerA).Count == 2
numbers contains INV-A-1, INV-A-2;  DOES NOT contain INV-B-1   ← customer-within-tenant isolation
INV-A-1.arStatus == "Due in 12 day(s)";  INV-A-2.arStatus == "Paid"
```

## STEP 2 — Job / trip / proof visibility (internal fields stripped at the query level)

`GetOwnJobsAsync` / `GetOwnJobDetailAsync` select only `job_number, status,
sla_status, scheduled_start/end, pickup/dropoff_address, tracking_code, eta` — a
status timeline is derived from those. `GetOwnProofsAsync` selects only
`proof_type, status, completed_at, receiver_name, signature_file_id, geo` +
artifact `artifact_type, file_id, captured_at, geo`. The withheld columns are
**never in the SELECT list**, so the backend actually withholds them (not just the UI).

**Test assertions (`PortalJobDetail_…`):** seeds a job with `risk_score=91.5`,
`cost_estimate=640`, `margin_estimate=210`, `revenue_estimate=850`, a dispatcher
note, an assigned risky driver, and an internal AI recommendation
(`source_event_id='job:{id}:leakage'`). Asserts the customer job payload contains
**none** of `riskScore/costEstimate/marginEstimate/revenueEstimate/notes/
assignedDriverId`, and the serialized response contains none of the AI text
("Internal AI", "underbilled"), the dispatcher note, or the driver name.
**`PortalProofs_…`** additionally asserts the internal proof fields are absent and
customer B sees an empty proof list for A's job.

## STEP 3 — Feedback / complaint intake

`SubmitFeedbackAsync` validates the job belongs to the customer, then inserts
`customer_feedback` with `status='open'` (lifecycle open→under_review→resolved→
closed). `GetOwnFeedbackAsync` lists the customer's own feedback.

**Test assertions (`PortalFeedback_…`):** customer A files a complaint on job A;
```
A sees it (comment "Late and damaged", status "open")
B (same company) DOES NOT see it
SubmitFeedbackAsync(A, jobB) == null   (cannot file against another customer's job)
a second tenant's feedback never appears for tenant 1
```

## STEP 4 — Portal UX

`CustomerPortalPage.tsx` consumes the real `/api/portal/*` endpoints via
`portalApi.ts`, using the **existing** design-system components (`PageHeader`,
`KpiCard`, `StatusBadge`, `DataTable`, `EmptyState`, `LoadingState`) — no new visual
style, no modification to `ui.tsx`. It shows own invoices (with the plain-English AR
status, not bucket jargon), shipments + status, a proof-of-delivery gallery for the
selected shipment, and a feedback form. **No seed/demo fallback anywhere:** each
query renders `LoadingState` while loading, `EmptyState` when empty, and an
`ErrorPanel` on failure — never a placeholder invoice or fake trip.

---

## STEP 5 — Verification

- `dotnet build` → **Build succeeded, 0 errors.**
- `dotnet test` (full suite) → **Passed! Failed: 0, Passed: 871, Skipped: 0**
  (867 prior + 4 new portal integration tests). No test disabled/skipped/weakened.
- `npm run build` (frontend) → **✓ built**; `npm run lint` → **clean**.
- The self-check table above confirms, per endpoint: auth enforced,
  customer-within-tenant isolation tested, internal-field stripping tested.

## Standing-rule compliance

Every piece of business logic is proven by a **real Postgres integration test**
with realistic seeded data and **real dollar amounts** ($1800.00, $2450.75,
$9999.99, cost/margin/revenue 640/210/850) and real dates — matching the Finance
module test pattern; data is seeded and torn down per test (`CleanupAsync`). **No UI
mocks, no user-facing fallback data, no demo screens** — the portal renders live API
results only, with EmptyState/ErrorPanel on failure.
