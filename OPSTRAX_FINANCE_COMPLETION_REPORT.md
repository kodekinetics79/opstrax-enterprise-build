# OpsTrax — Finance Module Completion Report

Finance feature work: AR aging, revenue leakage detection, payment summary, and
finance CSV export — built as **tested extensions of the existing proven
charges → invoice_drafts → issued_invoices → invoice_payments chain**. No parallel
schema. Backend-only; UI/UX Phase 1 and the RLS staging-activation track were not
touched.

- Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git` (the `zayra` remote is the off-limits sibling; not touched).
- Files changed (this session, 3 files, +628):
  - `backend-dotnet/Services/RevenueReadinessService.cs` — 3 new methods + 6 new records
  - `backend-dotnet/Controllers/RevenueReadinessEndpoints.cs` — 4 new endpoints + CSV builders
  - `backend-dotnet.Tests/RevenueReadinessPostgresTests.cs` — 4 new integration tests + seed helpers
- **Schema changes: NONE.** No additive migration was needed — every feature reuses
  existing tables:
  - AR aging / payment summary → `issued_invoices` (`due_at`, `balance_due`,
    `amount_paid`, `payment_status`, `issued_at`, `paid_at`) + `invoice_payments`.
  - Revenue leakage → **reused `cost_leakage_items`** (entity_type/entity_id = source
    ref, `category` = signal_type, `estimated_loss` = detected amount, `status` =
    open/reviewed/resolved) rather than a competing `leakage_signals` table — this
    honors the "extend, don't duplicate" rule; the table already has the exact shape.

---

## New endpoints (auth / tenant / test)

| Endpoint | Auth (`RequirePermission`) | Tenant filter | Test |
|---|---|---|---|
| `GET /api/finance/ar-aging` | `finance.ar.summary.read` | `GetCompanyId` → `WHERE company_id=@companyId` | `ArAging_BucketsOutstandingInvoicesByAge_AndIsTenantScoped` |
| `GET /api/finance/payment-summary` | `finance.revenue.summary.read` | `GetCompanyId` → company-scoped | `PaymentSummary_ComputesExactTotals…` |
| `POST /api/cost-leakage/detect` | `finance.revenue.summary.read` | `GetCompanyId` → company-scoped | `RevenueLeakage_DetectsNoChargeAndStaleDraft…` |
| `GET /api/finance/export` | `finance.ar.summary.read` | `GetCompanyId` → company-scoped | `FinanceExport_ArAgingCsv_ContainsOnlyLiveRows_NoPlaceholders` |

All four reuse the **exact** existing pattern from `RevenueReadinessEndpoints.cs`
(`RequirePermission(...)` then `GetCompanyId(http)` into a `RevenueReadinessService`
method whose every query carries `WHERE company_id=@companyId`). Permission keys are
existing finance permissions — none invented.

---

## STEP 1 — AR aging (`GetAccountsReceivableAgingAsync`)

Buckets **`balance_due`** (never `total`, so a partially-paid invoice ages only its
unpaid remainder) of `issued_invoices` by days past `due_at`:
`current` (due_at > NOW()), `1-30`, `31-60`, `61-90`, `90+`. Company totals + a
per-customer breakdown, both filtered by `company_id` and `balance_due > 0`.

**Test `ArAging_…` — exact assertions:** seeds 5 outstanding invoices (one per
bucket) + 1 fully-paid (0 balance) + a **second tenant** invoice, then asserts:
```
Current      == 1200.00     (due in +15d)
Days1To30    == 800.50      (due -10d)
Days31To60   == 2500.00     (due -45d)
Days61To90   == 1500.75     (due -75d)
Days90Plus   == 3000.00     (due -120d)
TotalOutstanding == 9001.25            // the 999.99 fully-paid invoice is excluded
Customers contains exactly the one tenant customer; TotalOutstanding == 9001.25
The other tenant's 7777.00 invoice never appears (DoesNotContain + no bucket == 7777.00)
```

## STEP 2 — Revenue leakage detection (`DetectRevenueLeakageAsync`)

Three signal types, persisted into `cost_leakage_items` (idempotent — one open
signal per `(entity, signal_type)`, deduped before insert):
1. `completed_job_no_charge` — completed/delivered/ready_to_bill job with **no**
   `job_charges`; detected amount = the rate card `minimum_charge` (expected but
   uncaptured).
2. `stale_draft_charge` — `job_charges.status='draft'` older than the (configurable,
   default 7-day) staleness threshold; detected amount = the charge `amount`.
3. `below_contract_rate` — completed job **with** charges whose `SUM(amount)` is
   below the rate card `minimum_charge`; detected amount = the shortfall.
   (Uses real contract data — `rate_cards.minimum_charge`; nothing invented.)

**Test `RevenueLeakage_…` — exact assertions:** seeds (a) completed job, no charge,
rate card `minimum_charge = 250.00`; (b) in-progress job with a `draft` charge of
`475.25` aged 10 days; (c) completed job with an approved charge of `620.00`
(≥ minimum). Then:
```
SignalsCreated == 2, SignalsAlreadyOpen == 0
signal 'completed_job_no_charge' present; DetectedAmount == 250.00; EntityId == job1
signal 'stale_draft_charge'      present; DetectedAmount == 475.25; EntityId == draftChargeId
signal 'below_contract_rate'     ABSENT  (job c is above minimum; job a has no charge to compare)
correctly-billed job (c) produces NO signal
cost_leakage_items has exactly 2 open rows for the tenant
re-run: SignalsCreated == 0, SignalsAlreadyOpen == 2, still exactly 2 open rows  (idempotent)
```

## STEP 3 — Payment summary (`GetPaymentSummaryAsync`) + CSV export

Payment summary over a `[from, to)` range: total collected
(`SUM(invoice_payments.amount)` in range), total outstanding (`SUM(balance_due)`),
average days-to-pay (`AVG(paid_at − issued_at)` over invoices paid in range),
payment count, paid-invoice count — company totals + per-customer.

CSV export (`GET /api/finance/export?type=ar-aging|payment-summary`) is built by
`BuildArAgingCsv` / `BuildPaymentSummaryCsv` **purely from the live record** — there
is no placeholder/sample/fallback code path. If the underlying query throws, the
endpoint throws (never emits fabricated rows).

**Test `PaymentSummary_…` — exact assertions (hand-calculated):** seeds two paid
invoices for customer 1 (1000.00 paid in 10 days, 2000.00 paid in 14 days), a
partial (500.00 of 1500.00) and an unpaid (800.00) for customer 2, all payments in
range:
```
TotalCollected   == 3500.00   (1000 + 2000 + 500)
TotalOutstanding == 1800.00   (1000 remaining on C + 800 on D)
PaymentCount     == 3
PaidInvoiceCount == 2         (A, B; C is partial, D unpaid)
AverageDaysToPay == 12.0      (avg of 10 and 14; issued_at/paid_at set in the same row → exact)
customer 1: Collected 3000.00, Outstanding 0, PaidInvoiceCount 2
customer 2: Collected  500.00, Outstanding 1800.00, PaidInvoiceCount 0
```

**Test `FinanceExport_…` — no-placeholder proof:** seeds 2 outstanding invoices
(1200.00 + 3000.00), builds the AR-aging CSV from the live record, and asserts the
output is **exactly 3 lines** (header + one live customer row + company-total row) —
proving no sample/placeholder rows can appear — with the live `4200.00` total in
both the customer and total rows.

---

## STEP 4 — Verification

- `dotnet build` → **Build succeeded, 0 errors** (474 warnings, unchanged baseline).
- `dotnet test` (full suite) → **Passed! Failed: 0, Passed: 867, Skipped: 0**
  (863 prior + 4 new finance integration tests). No test disabled/skipped/weakened.
- **Self-check (same audit method as prior sessions):** all 4 new endpoints have an
  auth check (`RequirePermission`, existing finance permission), a tenant filter
  (`GetCompanyId` → `WHERE company_id=@companyId` — the service carries 59
  `company_id=@companyId` predicates), and ≥1 integration test each (table above).

## Standing-rule compliance

- Every new piece of logic is proven by a **real Postgres integration test** with
  realistic seeded data, **real varied dollar amounts** (1200.00, 800.50, 2500.00,
  1500.75, 3000.00, 475.25, 620.00, 135-style reconciliation math) and **real date
  ranges** (issued/due/paid offsets), matching the `RevenueReadinessPostgresTests`
  pattern (the 135m reconciliation test). Test data is seeded and torn down per test
  (`CleanupTenantAsync` + explicit `cost_leakage_items` cleanup).
- **No UI mock, no user-facing fallback value, no demo data rendered live.** The CSV
  export in particular has no fallback path — verified by the 3-line export test.
- No parallel/competing schema: leakage reuses `cost_leakage_items`; AR/payments
  reuse `issued_invoices`/`invoice_payments`.
