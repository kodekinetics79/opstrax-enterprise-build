# OpsTrax — Module Target Spec & Samsara Benchmark Map

**Purpose:** the destination for the module-by-module completion program. Each module
has a **table-stakes target** (what a fleet SaaS must do to be credible vs Samsara/Motive)
and a **current state** (what OpsTrax has today). The deep Samsara gap analysis happens at
the END; this map exists so we build the *right depth* now, not the wrong thing.

**Method:** table-stakes derived from the standard capabilities of Samsara / Motive /
Verizon Connect (the incumbents), scoped to what a logistics/dispatch SaaS pilot needs.
Not a claim about Samsara's internal implementation.

**"Genuine done" per module = all 5:**
1. Backend endpoints return real tenant-scoped data under RLS (no 500s, no seed-fallback).
2. Write/mutation flows work end-to-end (create → transition → complete).
3. Frontend page renders that real data (not empty/seed) and its actions succeed.
4. Business logic is correct (the numbers/status transitions reconcile).
5. Realistic demo data seeded so a pilot can exercise it immediately.

---

## Module list (grouped) + pilot priority

Priority tiers reflect the Canada + Saudi pilots' actual daily workflow.

| # | Module | OpsTrax pages | Pilot tier |
|---|---|---|---|
| 1 | **Finance / Revenue / Billing** | Batch5Finance, FinancialAnalytics, RateCards, Quotations, Contracts, SlaKpi | **P0** |
| 2 | **Dispatch** | Dispatch, DispatchCommand, DispatchWorkspace | **P0** |
| 3 | **Jobs / Shipments / Orders** | Jobs, LastMileDelivery, ProofOfDelivery, OperationsProofCenter | **P0** |
| 4 | **Fleet / Vehicles / Assets** | Vehicles(Module), FleetOverview/Health/Workspace, FleetAssetManagement, FleetUtilization, FleetAssignments | **P0** |
| 5 | **Drivers / Workforce** | DriversModule, DriverScorecards, DriverMessaging, WorkforceManagement, HosEld | **P1** |
| 6 | **Telematics / Live Map** | LiveMap, TelematicsCommand, ControlTower, GeofenceManagement, IotDevices | **P1** |
| 7 | **Trips / Routing** | Trips, RoutePlanning | **P1** |
| 8 | **Safety** | Batch4Safety, AlertsCenter, AlertRules, TrafficViolations | **P1** |
| 9 | **Maintenance** | MaintenanceCommand, MaintenancePlanning | **P1** |
| 10 | **Compliance** | Compliance, FleetCompliance, FleetSaudiReadiness, AuditLogs | **P1** (Saudi pilot: high) |
| 11 | **Customer Portal / Visibility** | CustomerPortal, CustomerVisibility, CustomerEta, PublicShipmentTracking | **P1** |
| 12 | **Platform Admin** | PlatformOps, Admin, Settings, FeatureFlags, Integrations | **P2** |
| 13 | **CRM / Growth** | Leads, Opportunities, Campaigns, Quotations, AccountHealth | **P2** |
| 14 | **Analytics / Exec / AI** | Executive, CommandCenter, AnalyticsDashboard, PredictiveAnalytics, AiCopilot, CarbonTracking | **P2** |

---

## Per-module table-stakes target (the "done" bar) + current state

### 1. Finance / Revenue / Billing  — **P0**
**Target:** rate cards + contracts → job charges → invoice drafts → issue → payments →
AR aging → revenue-leakage detection; per-customer AR; CSV export; multi-currency.
**Current:** ✅ AR aging correct ($6,275.75 verified); issued-invoices, payments (201,
balance→0 verified), invoice drafts, mark-ready-to-bill, revenue/summary all green under
RLS. Multi-currency via country profiles. **Gap:** verify contracts/rate-cards → charge
auto-calc; quotations→contract flow; frontend AR-aging page wiring.

### 2. Dispatch — **P0**
**Target:** assignment board, driver/vehicle eligibility (HOS/cert/capacity), status
lifecycle (assigned→…→delivered), exceptions, POD, ETA. **Current:** ✅ status lifecycle
+ POD verified end-to-end; eligibility endpoint exists; canonical P4 token vocabulary.
**Gap:** auto-suggest/smart-assign depth; exception workflow UI; ETA push integration.

### 3. Jobs / Shipments / Orders — **P0**
**Target:** create (manual + bulk import), assign, track, POD capture, status, SLA.
**Current:** full jobs CRUD + lifecycle endpoints; import-preview; proof packages.
**Gap:** confirm import flow; shipment↔job unification; SLA breach auto-detection.

### 4. Fleet / Vehicles / Assets — **P0**
**Target:** vehicle registry, docs/expiry, assignment, readiness/health score, utilization,
cold-chain (Saudi). **Current:** broad page set exists; vehicle docs seeder fixed
(company_id). **Gap:** verify readiness-score computation is real; utilization math.

### 5–14: audited per-module as we reach them (targets summarized above).

---

## Execution order (this program)
P0 first, each to genuine-done: **Finance → Dispatch → Jobs → Fleet**, then P1
(Drivers, Telematics/Live-map, Trips, Safety, Maintenance, Compliance, Portal), then P2.
Final step: full Samsara gap analysis → remaining roadmap + effort estimate.

## Honest framing
OpsTrax already has a **wide** surface (79 nav modules). The program is **completion &
verification to depth**, not greenfield build. "All modules, Samsara-level, zero flaws"
is a multi-month effort; the near-term deliverable is a **pilot-ready P0 vertical slice**
the Canada + Saudi customers can run on real data, with the rest on an honest roadmap.
