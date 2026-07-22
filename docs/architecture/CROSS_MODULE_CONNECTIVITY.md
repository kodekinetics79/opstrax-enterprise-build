# Cross-Module Connectivity Map & Findings

How OpsTrax modules actually connect at the entity level, graded from a 4-specialist audit
(2026-07-14). OpsTrax is a **modular monolith**: one `Opstrax.Api`, one shared Neon Postgres.
Modules "connect" via (a) shared tables + foreign keys and (b) in-process handler calls — there
is **no** event bus between most modules and **no** systematic integration-contract enforcement.
Grades: **WIRED** (real FK/query/logic) · **PARTIAL** · **DISCONNECTED** · **FAKED** (seed/hardcoded).

## What is genuinely WIRED (verified — leave alone)
- `latest_vehicle_positions` → Live Map (REST + SSE, single source, honest `GREATEST(receipt,fix)` freshness).
- vehicle ↔ device telemetry gating (company/vehicle bound from `eld_devices`, body can't override).
- vehicle ↔ driver current assignment (real FK `vehicles.assigned_driver_id`, bidirectional).
- dispatch assignment → **board** → telemetry (validated tenant-owned FKs; `lvp.vehicle_id = da.vehicle_id`).
- dispatch assignment status → **job + trip** lifecycle (shared `ApplyAssignmentTransitionAsync`, forward path).
- telemetry alerts (speeding/geofence/stale) → `telemetry_alerts` → `safety_events` (real, deduped).
- `availability_status` computation from DVIR defects + blocking work orders (maintenance engine).
- invoice draft → issue → payment → AR aging (real, DB-backed, no seed fallback — *downstream of a charge*).
- device/gateway ingest, public-token tracking, SSO — tenant resolved server-side (correct).

## FIXED this cycle (commit d59184b)
| Seam | Was | Now |
|---|---|---|
| Customer ETA (`ComputeEtaAsync`) | queried non-existent `recorded_at` → **500** on every customer tracker with a vehicle | staleness from `received_at` in SQL; regression test |
| OOS → dispatch | red-tagged (`out_of_service`) vehicle **dispatchable**; caught only by a 15-min sweep | gate (`ValidateAssignment`) + picker (`AvailableVehicles`) honor `out_of_service`/`availability_status` |
| `resolve-malfunction` | `UPDATE eld_devices … WHERE id=@id` (no tenant) → cross-tenant write | scoped by `company_id` |

## Open backlog — ranked by business impact

### P1 (high impact)
1. **No rating engine** — `job_charges.unit_rate/amount` are hand-keyed from the request body; `rate_card_id` is decorative. Every billed dollar is manual; contracted rates never compute. *Fix: `RateJobAsync(jobId)` reads the rate card/contract + accessorials and inserts computed charges.* (Larger — new logic.)
2. **Delivery doesn't trigger billing; POD not enforced on issue** — a `delivered` load creates no charge/`ready_to_bill`, so delivered loads silently go un-billed; and an invoice can issue with **zero** proof of delivery. *Fix: `delivered` branch enqueues a billing event (an OutboxDispatcher exists); add a POD-exists guard on issue.*
3. **Cancel/exception bypass the shared transition** — `DispatchAssignmentCancel`/`Exception` write `dispatch_assignments` directly, so `jobs.status` never mirrors and open `invoice_drafts` aren't voided. *Fix: route both through `ApplyAssignmentTransitionAsync`; void open drafts on cancel.* (Bounded.)
4. **Three disconnected customer trackers** — `customer-visibility` (real model but no `lat/lng`, no POD image), `customer-eta/track` (static `jobs.eta`), `public/shipments/track` (separate seeded `fleet_tms_*` island). Customer never sees the live position the internal map shows. *Fix: converge on the `customer_visibility` model; surface live position + freshness + signed POD artifact; retire/relabel the seed-fed public tracker.*
5. **Geofence arrival doesn't advance `assignment_status`** — the canonical P4 status (that dispatch, ETA, and the customer timeline key off) is human-tap-only; a truck can sit at the delivery geofence and stay `in_transit`. Trip stops DO advance from position, but assignments don't. *Fix: on geofence-enter at a stop for the active assignment, transition status via the normalize path.*

### P2 (medium / defense-in-depth)
6. Safety scores computed into `driver_safety_scores` but never propagate to `drivers.safety_score`/assignability — a driver with real harsh-driving events stays fully assignable.
7. `vehicles.device_status` is **seed-only** — never recomputed from telemetry freshness (`last_seen_at`/`event_time`), yet many fleet surfaces read it as live health.
8. Freshness rules inconsistent — map uses honest `device_fix_time`; ETA/insights use different/weaker clocks. *Fix: one shared freshness helper.*
9. Telemetry/safety high-severity events → **notifications**: nothing enqueues a dispatcher/customer notification.
10. Job-status edits (`ChangeJobStatus`) don't sync back to the assignment (one-way desync).
11. Tenant scoping stragglers (RLS-covered in prod, defense-in-depth): `kpi/targets`, `kpi/metrics`, cost-leakage reads/writes lack an explicit `company_id` predicate; RLS-off background workers JOIN cross-module without `company_id` equality; `eld_devices` has no explicit RLS policy (enrollment incidental).
12. POD split across `proof_of_delivery` (job path) and `dispatch_proofs` (driver path) — don't reconcile.
13. `vehicle_assignments` history accumulates multiple concurrent `Active` rows (point-in-time FK is fine; history table unreliable).

### Net-new (scope, not a bug)
- **Settlement / carrier-pay / AP** — entirely absent; there is no payables mirror of the AR chain.

## The through-line
The **state machine and data plumbing are real**; the **cross-module "intelligence" — the automatic
rules that make one module's entity drive another's behavior — is largely missing or one-way.**
Delivery doesn't bill, rates don't compute charges, geofences don't advance status, safety doesn't
gate assignability, device silence doesn't change health. These are the seams to build with real
business logic, in impact order above.
