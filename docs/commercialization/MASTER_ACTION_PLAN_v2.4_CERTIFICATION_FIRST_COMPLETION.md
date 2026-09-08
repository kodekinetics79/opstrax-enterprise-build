# OpsTrax Master Commercialization & Certification Action Plan — v2.4 Certification-First Completion Amendment

**Status:** CONTROLLED MASTER AMENDMENT — ACTIVE ON MERGE  
**Effective date:** 2026-09-03  
**Change control:** `CR-2026-09-03-03`  
**Base master:** `docs/commercialization/MASTER_ACTION_PLAN.md` v2.1  
**Supersedes for execution priority:** v2.2 / `CR-2026-09-03-01` and v2.3 / `CR-2026-09-03-02`  
**Entry baseline:** `main@547f482dbf47e6f442c5d1f3e3b23806a49872cf`  
**Parent tracker:** #110

## 1. Program-owner directive

Finish the certifications that OpsTrax originally started. Do not substitute a controlled pilot, deferred gate, engineering-readiness batch, external closure hold, or later-wave feature work for the certification outcome in the original path.

The program therefore returns to exactly two critical certification tracks:

| Priority | Gate | State after activation | Required exit |
|---|---|---|---|
| 1 | G1A — Fleet Identity + Telematics certification completion (#108) | ACTIVE | Full evidence-backed certification decision on the current exact candidate |
| 2 | G1B — GT06 physical certification (#109) | ACTIVE-BLOCKED pending exact candidate acquisition | Exact hardware/firmware **Opstrax Certified Compatible**, then **Production Supported** when 72h/supportability criteria pass |

All other waves retain their current code and evidence but cease merge-bound feature progression until these certification lanes materially close.

## 2. G1A — finish, do not re-audit

The merged #113 work and all accepted evidence remain valid within their original scope. Re-entry is a focused gap-closure run against current `main`, not a new broad audit.

Only the remaining certification gaps may drive remediation:

- applicable authenticated role/persona coverage;
- tenant/branch isolation and direct-URL boundaries;
- representative large-fleet customer journeys and export integrity;
- current-candidate performance and failure/recovery evidence;
- responsive/accessibility acceptance;
- current DeviceOps/GPS/Live Map/geofence/diagnostic truth;
- exact-SHA deployment and same-journey retest;
- qualified independent Appendix B assurance.

Any defect follows:

`Observe -> Evidence -> Root Cause -> Fix -> Test -> Exact-SHA Deploy -> Same Journey Retest -> Close`

Acceptance requires 0 P0/P1 and no fabricated operational truth. The historical `CR-2026-09-01-02` 30-day LIMITED GO remains historical pilot evidence only and does not satisfy certification.

## 3. G1B — resume physical certification

The earlier listener/software evidence is preserved as supporting evidence, but the previous closed/deferred disposition is no longer the execution endpoint.

G1B resumes at Candidate acquisition/freeze and proceeds in order:

1. Candidate identity frozen — 2 identical units minimum, 3 preferred.
2. Protocol identified from real device bytes.
3. Bench compatible.
4. Vehicle tested.
5. Failure/recovery tested.
6. 24-hour soak.
7. Security reviewed.
8. **Opstrax Certified Compatible** exact model/firmware record.
9. 72-hour soak + install/procurement/support/RMA readiness for **Production Supported**.

No generic “GT06 compatible” claim is allowed. Certification belongs to the exact manufacturer/model/hardware revision/firmware tuple.

## 4. Later-wave state while certifications close

| Gate | Execution state | Commercial truth |
|---|---|---|
| G2A Samsara | HOLD / provider-evidence preparation only | PILOT |
| G2B Certified ELD partner | HOLD / provider-regulatory preparation only | DEVELOPMENT / ROADMAP |
| G3A HOS workflow | PAUSED / non-conflicting evidence/design only | DEVELOPMENT |
| G3B Dual-facing camera | PAUSED / provider/privacy design only | ROADMAP |
| G4A Video Safety | PAUSED / design/test-harness only | ROADMAP |
| G4B Provider breadth | PAUSED / provider research/contract work only | ROADMAP |
| G5–G6 | LOCKED | unchanged |

If a real provider or regulatory evidence opportunity would expire unless acted upon, the CTO may authorize a bounded evidence-acquisition exception without opening a third merge-bound production-code lane.

## 5. Binding evidence hierarchy

1. Source/code review — supporting only.
2. Automated tests — supporting only.
3. Persisted data reconciliation — supporting only.
4. Visible Chrome — required for customer-facing certification journeys.
5. Real provider account/API — required for provider certification.
6. Physical hardware — required for hardware certification.
7. Regulatory evidence — required for ELD/HOS certification claims.
8. Scale/recovery/soak — required for production-support tier.

Implementation teams may not self-certify. Appendix B mandatory specialists and independent assurance remain binding. P0 claims require the required independent perspectives.

## 6. Final program direction

After G1A and G1B materially close, resume the original sequence:

**Wave 2 provider/ELD certification -> Wave 3 HOS/camera -> Wave 4 safety/provider breadth -> Wave 5 device depth -> Wave 6 scale/commercial release.**

The objective remains **COMMERCIAL RELEASE — GO**, with capability status promoted only when the corresponding evidence gate actually passes.
