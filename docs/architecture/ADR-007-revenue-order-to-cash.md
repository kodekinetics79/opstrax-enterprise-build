# ADR-007 — Order-to-Cash completion: Rating, Delivery→Billing, Settlement/AP

**Status:** Accepted (design). Phased implementation underway. Synthesizes three specialist designs.
Each is phased so a thin, tenant-scoped, idempotent, fail-closed first increment ships against the
current schema plus a small additive migration. **All three need `*PostgresTests` integration
coverage (a live Postgres) to be production-trusted — that gate runs in CI, not this sandbox.**

## A. Rating engine (kills the #1 revenue leak: charges are hand-keyed)
Today `CreateJobCharge` takes `unit_rate`/`amount` from the request body; `rate_card_id` is stored
but never used to compute anything.

- **New `RatingService.RateJobAsync(companyId, jobId, mode)`** (mode Preview|Commit). Resolves the
  job's rate card (by `job.rate_card_id`, else the contract's active/effective card, most-specific by
  `vehicle_type` + newest). Computes base by canonical `billing_basis`: `flat`/`per_load`/`per_unit`
  → base_rate; `per_mile` → `trips.actual_distance_miles ?? planned_distance_miles ?? haversine(job
  pickup→dropoff)` × base_rate; `per_km` → miles×1.60934; `per_stop` → `trips.total_planned_stops`;
  `per_hour` → `actual_duration_minutes/60`. Applies `minimum_charge`, then `fuel_surcharge_percent`
  on the base. **Fail-closed:** no card → `Priced=false, reason=no_rate_card`, zero writes, existing
  `job_without_contract_or_rate_card` leakage signal stands. Never invent a rate.
- **Idempotency migration (Phase-1 blocker):** `job_charges` needs `source VARCHAR(20) DEFAULT
  'manual'`, `rate_basis VARCHAR(40)`, `rated_at TIMESTAMPTZ`, and a partial unique index
  `(company_id, job_id, charge_type) WHERE source='rating'`. Commit = delete-and-recompute of
  `source='rating'` charges NOT already on an issued invoice (manual charges never touched; issued
  charges immutable — guard on `invoice_drafts.status IN ('issued','pending_review','approved')`).
- **Plug-in:** `POST /api/jobs/{id}/rate` (perm `charge.create`, preview/commit); Phase-2 auto-rate
  inside `MarkJobReadyToBillAsync` (behind a flag) — the already-centralized seam.
- **Data gaps to add later:** `rate_card_accessorials` table (detention/layover — the current
  single free-text `accessorial_type` can't price), `rate_cards.fuel_surcharge_per_mile`,
  `jobs.billable_weight_kg` (Weight-Based). **Phase 1 = flat + per-mile + fuel% + minimum, on-demand.**

## B. Delivery → Billing automation (+ POD-gated issuance)
Today `delivered` triggers no billing, and invoices can issue with **zero** proof of delivery.

- **Phase 1 (smallest, no migration): POD guard in `IssueInvoiceFromDraftAsync`** — after the
  already-issued check, block issuance when a job-linked draft has no POD, behind flag
  `billing.require_pod_to_issue`. POD is reconciled across BOTH stores (neither alone authoritative):
  `proof_of_delivery.status='Captured' (by job_id)` OR `dispatch_proofs.proof_type='delivery'` via
  `dispatch_assignments (by assignment→job)`. Do NOT trust `jobs.proof_status` (driver PODs never set
  it). `JobId IS NULL` ad-hoc drafts are exempt. Evaluated live at issue-time → late POD "just works".
- **Phase 2: `delivered` → durable `job.delivered` outbox event** (the real `IDomainEventPublisher`
  + `outbox_messages` + `PostgresOutboxDispatcher` exist). Enqueue atomically in the `delivered`
  branch of `ApplyAssignmentTransitionAsync` with a partial unique index
  `(tenant_id, event_type, aggregate_id) WHERE event_type='job.delivered'` + `ON CONFLICT DO NOTHING`.
  Handler `JobDeliveredBillingHandler` → `RateJobAsync(Commit)` → `MarkJobReadyToBillAsync` (both
  idempotent). Billing failure retries/dead-letters; never rolls back delivery (delivery is truth).
- **Correctness note:** `DispatchAssignmentProof` sets `delivered` via raw SQL — route it through the
  shared transition or it skips both the jobs mirror and the billing enqueue.

## C. Settlement / carrier-&-driver-pay (AP) — entirely absent; mirror the AR chain
- **New tenant-scoped tables (WITH company_id + RLS — unlike the gateway infra tables):**
  `settlement_statements → settlement_lines → settlement_payments`, mirroring `invoice_drafts →
  _lines → issued_invoices → payments`. Plus `pay_agreements` (the AP analogue of `rate_cards`:
  `basis percent|per_mile|flat`, rate, min_pay, effective/expiry). Owner migration in stage33 house
  style + `SettlementSchemaService.EnsureAsync` + RLS `tenant_isolation`/`platform_admin_bypass`
  policies (per stage22 reconcile pattern).
- **Pay is computed from the load + pay agreement, NEVER from `job_charges`** (settlement is
  decoupled from the customer invoice; store the `basis_amount` used for audit). Driver payee resolves
  from `dispatch_assignments.driver_id` (works today); carrier needs a new `jobs.carrier_id`.
- **Endpoints** mirror `RevenueReadinessEndpoints` (generate/get/list/lines/approve[high-risk
  approval]/payments/ap-summary), RBAC `settlement.*` added to `ExpandPermission` +
  `ApprovalPolicyCatalog.HighRiskActions`.
- **Data gaps:** `pay_agreements`, `jobs.carrier_id`, `jobs.billable_miles`, driver advance ledger.
  **Phase 1 = driver flat/per-mile statements only.**

## Sequencing recommendation
1. **B-Phase-1 POD guard** (no migration, read-only check, immediate dispute/compliance win) — landing now.
2. **A-Phase-1 rating** (one additive migration + service + endpoint).
3. **B-Phase-2 delivered→outbox** (depends on A).
4. **C settlement** (largest; net-new tables).
Each lands behind a flag where behavior changes, with `*PostgresTests`, and must be CI-integration-verified before production.
