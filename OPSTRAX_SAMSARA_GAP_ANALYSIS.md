# OpsTrax — Samsara Gap Analysis & Roadmap

**Purpose:** the end-of-program benchmark. With the module-completion pass done (P0 to
genuine-done, P1 verified), this is the honest "where we are vs. Samsara/Motive, and what
remains" — the deliverable requested when we set out ("complete our build, then compare").

**Method:** each module scored on the standard incumbent (Samsara/Motive/Verizon Connect)
table-stakes. Scoring: ✅ Pilot-ready · 🟡 Functional, depth gaps · 🔵 Endpoints healthy,
needs data/UI polish · ⛔ Not built.

---

## Where OpsTrax stands after this program

**Verified this session, under REAL RLS tenant-isolation enforcement (the production
posture), against a realistic seeded tenant:**
- **0 × 500 across all 292 GET routes.** Core write lifecycles proven end-to-end.
- RLS enforcement genuinely active + fail-closed (was the blocked staging gate).
- Full test suite 884/884 throughout; ~15 commits pushed to origin this session.

**Systemic classes of defect eliminated (had been failing writes across every module):**
RLS shared-connection concurrency; audit-log null-entity + non-JSON details; null
parameter binding; MySQL `UPDATE…JOIN`; NOT-NULL create defaults; permission-taxonomy
gaps; status-vocabulary mismatches.

---

## Module scorecard vs Samsara table-stakes

| Module | Samsara table-stakes | OpsTrax now | Score |
|---|---|---|---|
| **Finance/Revenue** | rate cards→charges→invoice→payment, AR aging, leakage | full chain verified (payment→paid, AR $6,275.75 reconciles), quotations wired | ✅ |
| **Dispatch** | board, eligibility, status lifecycle, POD, ETA, auto-assign | lifecycle+POD+auto-suggest+bulk-ETA verified; canonical P4 tokens | ✅ |
| **Jobs/Shipments** | create (manual+import), assign, track, POD, SLA | create→assign→status→proof→ETA verified; import-preview exists | ✅ |
| **Fleet/Vehicles/Assets** | registry, docs/expiry, assignment, readiness, utilization | CRUD verified, reads green, readiness/utilization present | ✅ |
| **Drivers/Workforce** | roster, scorecards, HOS/ELD, coaching, messaging | roster/scorecards/coaching verified; HOS present | 🟡 (HOS depth) |
| **Safety** | event capture, review/resolve, coaching, dashcam | events/resolve/dismiss/coaching verified | 🟡 (no dashcam/AI vision) |
| **Maintenance** | PM schedules, defects, work orders, DVIR | PM items, defect resolve, work orders, DVIR verified | 🟡 (PM auto-scheduling depth) |
| **Telematics/Live-map** | live positions, geofencing, device mgmt, alerts | positions/metrics/geofence/control-tower green | 🟡 (no real device ingest; simulator only) |
| **Trips/Routing** | trip lifecycle, route plan/optimize, compliance score | trips + routes green; compliance score computed | 🟡 (route optimization is heuristic) |
| **Compliance** | HOS/IFTA, docs/expiry, audit packages, regional (ZATCA/Saudi) | compliance profiles + fleet-compliance + Saudi readiness present | 🟡 (ZATCA e-invoice gen not built) |
| **Customer Portal** | tracking, ETA, docs, feedback | portal + visibility + public tracking verified | ✅ |
| **Platform Admin** | tenant CRUD, entitlements, packages, billing, country profiles | full platform control plane + country-profile cascade | ✅ |
| **CRM/Growth** | leads, opportunities, quotes, campaigns | pages exist; module_records-backed; quotes wired | 🔵 |
| **Analytics/Exec/AI** | dashboards, predictive, AI copilot | analytics/exec/command-center green; AI copilot present | 🟡 (AI is heuristic, not ML) |

**Tally:** ✅ 6 pilot-ready · 🟡 7 functional-with-depth-gaps · 🔵 1 polish · ⛔ 0 unbuilt.

---

## The real gaps vs Samsara (honest, prioritized)

These are **depth/maturity** gaps, not missing scaffolding — OpsTrax has the surface;
Samsara has years of depth in specific areas:

### Tier 1 — material product gaps (roadmap, multi-sprint each)
1. **Real telemetry ingest + hardware.** Samsara ships gateways/cameras; OpsTrax has a
   simulator + ingest endpoint but no real device fleet. → integrate a telematics
   provider (Samsara/Motive API, or ELD via `hos_records`) for live GPS/HOS.
2. **AI dashcam / safety vision.** Samsara's differentiator. OpsTrax logs safety events
   but has no video/vision. → out of scope short-term; partner or defer.
3. **Route optimization engine.** Current routing is heuristic scoring. → real optimizer
   (VRP solver) for multi-stop sequencing.
4. **ZATCA Phase-2 e-invoicing (Saudi pilot).** Country-profile *structure* exists (the
   cascade turns it on); actual ZATCA invoice XML generation + reporting is NOT built.
   **This is the one P0-ish gap for the Saudi pilot specifically.**

### Tier 2 — depth/polish
5. HOS/ELD rules engine depth; IFTA fuel-tax automation.
6. PM auto-scheduling from real odometer/engine-hour telematics.
7. Predictive analytics as real ML (currently heuristic placeholders).
8. Bulk job/shipment import UX hardening.

### Tier 3 — operational (from earlier reports, still open)
9. Staging environment + RLS flag flip in production (infra — Zack).
10. Credential rotation at provider (Zack).
11. External monitoring on `/health/deep` (Zack).
12. A stable, test-isolated demo tenant (the demo id currently drifts because the
    DemoTenantSeeder test recreates it each `dotnet test`).

---

## Go / no-go for pilot

**Canada pilot: GREEN for a supervised pilot** on the ✅/🟡 modules (fleet ops, dispatch,
jobs, finance, portal, drivers, maintenance, safety-logging) — with real telematics as a
fast-follow (Tier-1 #1).

**Saudi pilot: GREEN except ZATCA.** Everything works; but **ZATCA Phase-2 e-invoicing
(Tier-1 #4) is a hard regulatory requirement** for live invoicing in KSA and is not built.
Recommend: pilot operational modules now, gate live invoicing on ZATCA delivery.

**Not "Samsara-level" yet** — honestly, the hardware/vision/optimization depth is a
multi-quarter program. But OpsTrax is a **genuinely functional, tenant-isolated, pilot-ready
logistics SaaS** across its core, which is the near-term goal.

---

## Recommended next sequence
1. **ZATCA Phase-2 e-invoice generation** (unblocks Saudi live invoicing). 
2. **Telematics provider integration** (real GPS/HOS → live-map, safety, PM).
3. Stabilize demo tenant + broaden seed for shipments/cold-chain.
4. Staging + RLS production activation (infra).
5. Route optimizer + predictive ML (differentiation, later).
