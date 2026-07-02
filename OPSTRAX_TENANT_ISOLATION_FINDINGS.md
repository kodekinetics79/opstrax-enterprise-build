# OpsTrax — Tenant Isolation Findings (2026-07-01)

**Severity: CRITICAL for multi-tenant pilot.** Not a UI issue — a data-isolation
issue. Masked today only because there is effectively one real tenant with data.

## The two-layer isolation model (as designed)
1. **Application layer** — every query filters by `company_id` (hand-written WHERE).
2. **Database layer (backstop)** — Postgres RLS: `tenant_isolation` policy per table,
   the app connects as a restricted non-bypass role (`opstrax_app`), and each request
   runs in a transaction with `set_config('app.current_tenant_id', …)`.

If both layers were active, a forgotten WHERE clause would fail *closed* (RLS returns
0 rows) instead of leaking. **Today, layer 2 is inactive**, so layer 1 is the only
protection — and layer 1 has gaps.

## Finding 1 — RLS enforcement is OFF in the running build
- `Rls:EnforceTenantContext` is **unset** (defaults `false`) → no per-request tenant
  scope is applied.
- The app connects as **`zayra`** — the **superuser/owner**, which **bypasses RLS
  entirely** (even the FORCE'd policies).
- Net effect: the Stage 19/20/22 RLS policies I verified are present but **inert** in
  this deployment. Confirmed live.

## Finding 2 — Read/aggregate endpoints missing `company_id` (real leaks)
Writes are correctly scoped (`WHERE id=@id AND company_id=@companyId`). But many
**summary/list/detail reads aggregate across ALL tenants**:
- Confirmed leaking: `/api/eld/devices` (returned 15 devices across tenants — **fixed**
  in this session), `ComplianceSummary`, `HosSummary`.
- ~18 `*Summary` handlers take only `(Database db, …)` — no `HttpContext`, so they
  *cannot* scope by tenant: SafetySummary, DashcamSummary, EvidenceSummary,
  FuelSummary, ExpensesSummary, ContractsSummary, CarriersSummary, CostMarginSummary,
  CostLeakageSummary, ComplianceSummary, HosSummary, KpiSummary, ExecutiveSummary, etc.
  (Some — About/health — are legitimately global; most are tenant data.)
- Sibling list/detail methods (e.g. `SafetyEvents`, `SafetyEventDetail`) similarly
  read without `company_id`.

## Why it's hidden right now
The demo has one populated tenant (MERIDIAN-DEMO). With one tenant, cross-tenant
aggregation returns only that tenant's data, so nothing *looks* wrong — until a second
real tenant's data lands in the same DB. That is exactly the Canada + Saudi pilot.

## Remediation plan (priority order)
1. **Enable RLS enforcement (the backstop):** set `Rls:EnforceTenantContext=true` and
   connect as `opstrax_app` in every deployed environment. This makes ALL missing-WHERE
   reads fail-closed. **Must be validated first** — run the full suite + a GET smoke
   test against `opstrax_app` with enforcement on to catch endpoints that break inside
   the single tenant transaction (557 endpoints). Fix breakages, then flip.
2. **Defense-in-depth:** add `HttpContext` + `company_id` to every tenant read/summary
   handler (correct regardless of RLS). Add a code-review/test guard against `FROM
   <tenant_table>` without a tenant predicate.
3. **Regression:** extend the isolation tests to cover a representative summary/list
   endpoint per module under two tenants sharing the pool.

## Status
- eld/devices scoping: FIXED (this session).
- Everything else above: OPEN — this is the top pilot-blocking work item.
