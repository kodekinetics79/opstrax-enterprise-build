# OpsTrax Accelerated Hardening Factory — Master Action Plan

**Status:** CONTROLLED MASTER — ACTIVE  
**Version:** 2.0  
**Effective date:** 2026-09-02  
**Executive owner:** CTO Office / OpsTrax Commercialization Program  
**Parent tracker:** #110  
**Current main at activation:** `a859dffd47400774f9992eb7e361517222a6d1ea`  
**Wave 1:** G1A merged/closed under a time-bounded LIMITED GO; M1/M2 remain PILOT.  
**GT06 physical certification:** EXTERNAL HOLD / NOT CERTIFIED until an exact authorized physical candidate is available.  
**Wave 2:** ACTIVE. #115 Samsara production connector; #116 certified ELD partner selection/integration.  
**Recent integration state:** PR #118 merged; PR #119 merged; PR #120 active for Samsara truth/recovery hardening.

## 1. Executive objective
Move OpsTrax rapidly from a strong but uneven pilot-capable build to a commercially deployable connected-fleet platform without sacrificing customer truth, tenant isolation, hardware truth, provider truth, regulatory compliance, or production reliability.

The governing execution model is now a hardening factory, not a serial certification queue.

## 2. Core operating model

### SPEED 1 — BUILD
Up to **six bounded engineering squads** may work in parallel. Each squad owns a vertical slice, batches related defects, runs targeted and integration tests, and produces a reviewable PR. Minor fixes do not trigger a full certification cycle.

### SPEED 2 — INTEGRATE
Accepted squad work is combined into controlled integration candidates. Full automated regression, PostgreSQL integration, security contracts, frontend/mobile contracts, telematics regression, release-container build, migration rehearsal, and bounded load smoke run here.

### SPEED 3 — CERTIFY
At most **two certification candidates** may exist simultaneously. Only frozen candidates receive exact-SHA staging deployment, visible Chrome acceptance, real persisted data, provider/device/regulatory evidence where applicable, recovery/failure testing, performance acceptance, independent assurance, and final CTO GO / LIMITED GO / NO-GO.

**Rule:** certification rigor is preserved, but applied to meaningful frozen batches rather than every small correction.

## 3. Concurrency policy
- Maximum **6 bounded engineering squads** active at once.
- Maximum **2 certification candidates** active at once.
- A squad blocked solely by hardware, provider approval/account, external commercial agreement, or qualified external review moves to **EXTERNAL HOLD** and frees capacity immediately.
- Human/qualified SME acceptance can queue; it blocks certification/promotion, not unrelated engineering progress.
- P0 findings may preempt any squad and can stop affected integration/certification lanes.

## 4. Active factory squads

| Squad | Scope | Mandatory leadership/review |
|---|---|---|
| S1 Fleet/TMS Core | fleet identity, vehicles, drivers, assets, dispatch, jobs, routes, TMS, maintenance, customer workflows | Fleet Product, enterprise architect, frontend/backend, independent SDET |
| S2 Connected Vehicle | telematics, DeviceOps, Samsara, Motive/Geotab/OEM adapters, GPS, J1939, protocol gateway | Telematics/IoT, protocol/data, security, independent SDET |
| S3 Compliance | ELD partner, HOS, DVIR/compliance workflow, U.S./Canada evidence | ELD/HOS regulatory, Fleet Ops, security, SDET |
| S4 Video Safety | dual-facing camera integration, event video, privacy, incidents, coaching | Video telematics, driver safety, privacy/security, SDET |
| S5 Platform Hardening | tenancy/RBAC/RLS, DB, migrations, SRE, observability, recovery, security, warning debt | Security, PostgreSQL, SRE, platform architecture, SDET |
| S6 UX/Performance | enterprise-density UX, responsive/accessibility, GIS/map usability, frontend performance | Enterprise UI/UX, GIS, performance, Fleet Product, SDET |

The CTO may change squad allocation based on evidence and commercial priority without changing the factory model.

## 5. Independent assurance model
Implementation teams do not certify their own work.

A central assurance function owns:
- adversarial browser journeys;
- tenant/branch boundary testing;
- API/contract regression;
- PostgreSQL integration;
- security abuse cases;
- load/stress/soak where applicable;
- failure/recovery evidence;
- evidence reconciliation and defect reopening.

For P0 domains, require at least two logically independent expert perspectives before closure. Qualified-human acceptance remains mandatory wherever the commercial/regulatory release contract says so; AI-assisted review is supporting evidence only.

## 6. Severity and execution policy

### P0 — stop-the-line
Tenant leakage, auth bypass, cross-tenant device attribution, data corruption/loss, regulatory safety corruption, fabricated telemetry/HOS/video truth, critical secret exposure.

Action: immediate containment, focused remediation, adversarial regression, affected integration/certification lane blocked.

### P1 — release blocker
Broken core workflow, materially wrong GPS/diagnostics/HOS, critical availability failure, major role boundary defect, unsafe provider mapping, material recovery failure.

Action: fix in current batch before integration candidate can pass.

### P2 — hardening batch
Non-critical workflow defects, bounded performance issues, moderate UX/accessibility defects, incomplete operational behavior.

Action: batch with owning module; do not force a certification cycle per item.

### P3 — backlog/polish
Minor copy, cosmetic spacing, low-risk enhancement.

Action: backlog unless trivial and safe.

## 7. Batch-first defect closure
Related findings are grouped by vertical slice, for example:
- Vehicle Master batch: validation, correction, import, archive/reactivate, assignment/history, export.
- Telematics Truth batch: freshness, device state, position truth, diagnostics, alerts, branch scope, provenance.
- RBAC batch: role catalog, direct URL, export/mutation, branch/customer/driver boundaries.
- Provider batch: authentication, discovery, mapping, backfill, pagination, cursor/replay, stale-feed health, disconnect/reconnect.

A batch receives one integration promotion and one certification run unless a P0 forces earlier isolation.

## 8. External dependency policy
Use status **EXTERNAL HOLD** when internal code readiness is bounded but final evidence depends on an unavailable external dependency.

Examples:
- GT06 physical model unavailable → EXTERNAL HOLD; no certified-hardware claim.
- Samsara real account/token unavailable → internal connector hardening may continue; provider certification stays HOLD.
- ELD partner/device/commercial rights unresolved → compliance architecture/research may continue; regulated package stays HOLD.
- Camera vendor unavailable → canonical video/event/privacy architecture may continue; hardware/video certification stays HOLD.

Never fabricate external evidence and never let one external hold freeze unrelated squads.

## 9. Current program truth at v2.0 activation
- G1A: CLOSED to a **time-bounded LIMITED GO** for controlled pilot; M1/M2 remain PILOT, not generally certified/production-ready.
- G1B GT06 physical: **EXTERNAL HOLD / NOT CERTIFIED**.
- G2A Samsara: ACTIVE, currently PILOT/HOLD for final provider certification. Real-account/provider evidence remains mandatory.
- G2B Certified ELD partner: ACTIVE due diligence/integration readiness; no regulated package claim yet.
- Motive: connector readiness work exists; no provider certification claim.
- HOS: DEVELOPMENT until sourced from a certified/qualified path and proven end-to-end.
- Video Safety: ROADMAP/engineering-readiness until a real camera/provider path exists.

## 10. Factory board — vertical slices
Each slice moves independently through `RED -> HARDENING -> INTEGRATION GREEN -> CERTIFICATION -> LOCKED`.

1. Fleet Identity
2. Vehicle Operations
3. Driver Operations
4. Device Operations
5. GPS / Live Operations
6. Dispatch
7. Jobs / Orders
8. Routes
9. Maintenance
10. Safety
11. TMS / Shipments
12. Last Mile
13. Customer Portal
14. Cold Chain
15. Reporting / Analytics
16. Platform Admin / Security
17. Billing / Commercial Operations
18. Integrations / Provider Hub
19. HOS / ELD
20. Video Safety

## 11. Integration gate — mandatory checks
A candidate cannot advance to certification unless applicable checks are green:
- full build with zero errors;
- warning ceiling must not increase; warning debt gets a tracked burn-down target;
- unit/contract suites;
- PostgreSQL-backed integration;
- migration enrollment and production-shaped rehearsal;
- RBAC/RLS/tenant isolation regression;
- frontend lint/contracts/build/bundle budget;
- mobile contracts/build where touched;
- telematics/protocol regression where touched;
- dependency/security checks;
- bounded load smoke;
- release containers/provenance;
- no open P0/P1 in candidate scope.

## 12. Certification gate — mandatory evidence
Only frozen candidates are certified.

Customer-facing claims require visible Chrome and real persisted data. Provider claims require a real provider account/API response. Hardware claims require real physical hardware. ELD/HOS claims require jurisdiction-appropriate regulatory evidence and end-to-end operational proof. Production-support claims require scale, recovery, observability and supportability evidence.

Final acceptance must record exact frontend/API/build identity and limitations.

## 13. Daily executive velocity dashboard
The program reports only decision-driving metrics daily:
- open P0;
- open P1;
- P0/P1 closed in last 24h;
- slices RED / HARDENING / INTEGRATION GREEN / CERTIFICATION / LOCKED;
- active squads and blocked squads;
- active certification candidates;
- regression pass/fail count;
- new regressions;
- compiler warning count and delta;
- staging availability/readiness;
- key p95 performance indicators;
- external holds;
- commercial packages newly eligible or blocked.

Long narrative evidence remains in issues/PR artifacts; executive status stays concise.

## 14. Current immediate execution directive
1. **S2 Connected Vehicle:** finish PR #120 as a batched Samsara correctness/recovery integration unit; do not over-certify each internal fix. Keep Samsara provider certification HOLD until real account evidence exists.
2. **S3 Compliance:** continue #116 partner/regulatory/commercial evidence and define the canonical ELD/HOS integration contract without claiming certification.
3. **S5 Platform Hardening:** run continuous warning-debt, dependency/security, migration, DB/RLS, backup/restore, readiness, worker and recovery hardening against current main.
4. **S1 Fleet/TMS Core:** begin post-Wave-1 module-by-module P0/P1 hardening of dispatch/jobs/routes/maintenance/TMS/customer workflows using realistic large-fleet data; batch related defects.
5. **S6 UX/Performance:** execute shared-token/primitive enterprise-density hardening plus performance/accessibility regression; avoid page-by-page cosmetic patching.
6. **S4 Video Safety:** prepare provider-agnostic camera/event/retention/privacy contracts and vendor evaluation criteria; final hardware/video evidence stays EXTERNAL HOLD until a real camera partner is selected.
7. Central assurance consumes completed batches and forms integration candidates; no more full certification deployment after every minor fix.

## 15. Commercial release policy
Revenue activates package-by-package; the entire roadmap does not need to be complete.

- Controlled Fleet/TMS Pilot: allowed only within the active limited-pilot boundaries and stop triggers.
- Existing-provider Connected Fleet: after the relevant provider connector is certified with real-account evidence.
- Certified GPS: only after exact hardware model/firmware passes physical certification.
- Regulated ELD/HOS: only after certified/qualified source, commercial rights and OpsTrax end-to-end HOS acceptance.
- Video Safety: only after real camera/provider integration, privacy controls, incident/coaching workflow and acceptance.

## 16. Non-negotiable truth rules
- UI/schema presence is not capability proof.
- Simulator evidence is not hardware certification.
- Catalog presence is not provider connectivity.
- Provider reachability is not production integration proof.
- AI review is not qualified-human regulatory/hardware acceptance.
- Stale telemetry is never presented as current/live.
- No ordinary GPS device may be marketed as an ELD.
- Sales/marketing status must match the Capability Truth Matrix.

## 17. Change control
This v2.0 factory model supersedes the serial wave-execution mechanics in v1.x while preserving all prior commercial-truth, security, physical-evidence, provider-evidence, regulatory-evidence and time-bounded waiver constraints.

- Squad allocation/change within the six-lane factory: CTO Class 0/1 decision.
- New certification candidate: CTO approval; max two concurrent.
- Sequence/commercial claim change: documented change record.
- Commercial waiver: quantified, time-bounded, explicit stop triggers and expiry.
- Any P0 may override schedule.

## 18. End-state
The factory is complete when OpsTrax has commercially deployable, evidence-backed fleet/TMS operations; production provider connectivity; supportable device operations; valid regulated ELD/HOS integration; real video safety; hardened security/RBAC/tenancy; proven performance/recovery; and package-level commercial release gates that can be defended to customers, auditors and partners.

**Governing principle:** build in parallel, integrate deliberately, certify only frozen meaningful candidates, and never trade truth for speed.