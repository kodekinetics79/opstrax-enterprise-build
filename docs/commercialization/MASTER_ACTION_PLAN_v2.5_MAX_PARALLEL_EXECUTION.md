# OpsTrax Master Commercialization & Certification Action Plan — v2.5 Maximum Parallel Execution Amendment

**Status:** CONTROLLED MASTER AMENDMENT — ACTIVE ON MERGE  
**Effective date:** 2026-09-03  
**Change control:** `CR-2026-09-03-04`  
**Base master:** `docs/commercialization/MASTER_ACTION_PLAN.md` v2.1  
**Supersedes for execution priority:** v2.4 / `CR-2026-09-03-03` pause/lock model  
**Entry baseline:** `main@1f3b5de029b33e9315fb96c80988e610665c41b0`  
**Parent tracker:** #110

## 1. Program-owner directive

Execute all governed Waves 1–6 concurrently at the maximum safe throughput. Do not wait for one wave to become administratively closed before performing independent engineering, evidence preparation, provider/hardware acquisition, performance/DR, support, commercial, or isolated acceptance work in another wave.

Parallel execution does **not** convert missing evidence into a pass. Final capability promotion remains dependency-aware and evidence-bound.

## 2. Portfolio state

| Gate | Parallel state | Truth until exit |
|---|---|---|
| G1A Fleet Identity + Telematics | ACTIVE — certification priority | PILOT |
| G1B GT06 physical certification | ACTIVE — hardware acquisition/physical evidence | PILOT software / NOT CERTIFIED hardware |
| G2A Samsara | ACTIVE — provider certification | PILOT |
| G2B Certified ELD partner | ACTIVE — partner/regulatory/integration | DEVELOPMENT / ROADMAP |
| G3A HOS workflow | ACTIVE — engineering + source-bound acceptance | DEVELOPMENT |
| G3B Dual camera | ACTIVE — engineering + provider/privacy acceptance | ROADMAP |
| G4A Video Safety | ACTIVE — workflow/evidence integration | ROADMAP |
| G4B Provider breadth | ACTIVE — Motive/Geotab/OEM connectors | ROADMAP |
| G5A DeviceOps 2.0 | ACTIVE | DEVELOPMENT until accepted |
| G5B J1939/PT40/OEM hardware depth | ACTIVE | DEVELOPMENT until exact hardware evidence |
| G6A Scale/DR/observability | ACTIVE | RELEASE READINESS work; no commercial GO by activation |
| G6B Support/billing/packaging | ACTIVE | COMMERCIAL READINESS work; no commercial GO by activation |

## 3. Maximum-safe concurrency

All lanes may execute at once. Shared-core merge-bound work uses four default integration slots, expandable only for demonstrably disjoint domains. Research/evidence/provider/hardware/regulatory/performance/support/commercial work has no wave-count cap.

Serialized authorities:

- production migration chain — one schema-authority writer at a time;
- shared auth/RBAC/session — one security-authority writer at a time;
- shared frontend primitives/tokens — coordinated design-system authority;
- frozen certification candidates — immutable until their evidence disposition is recorded.

Every active branch declares its owned file/domain set before integration. Cross-domain collision pauses the branch at integration; it does not justify dropping another lane's changes or weakening acceptance.

## 4. Parallel close strategy

### Wave 1
Finish G1A full software certification and G1B exact-device certification. Continue the existing real-evidence rules with no waiver masquerading as CERTIFIED.

### Wave 2
G2A proceeds to real Samsara Connect/Auth/Discover/Map/Sync/Monitor/Recovery evidence when account authority is available. G2B continues partner/device/regulatory/commercial-rights integration with U.S. and Canada kept separate.

### Wave 3
G3A finishes HOS operational workflow and fail-closed source authority now; regulatory graduation waits for accepted G2B source evidence. G3B finishes camera privacy/media/event/driver-trip linkage now; camera graduation waits for real provider/device evidence.

### Wave 4
G4A finishes authentic-event-to-review-to-coaching-to-acknowledgement-to-closure workflow and provider-evidence preservation. G4B certifies Motive, Geotab and OEM connectors one provider at a time; no umbrella provider claim.

### Wave 5
G5A builds DeviceOps 2.0: inventory, SIM/eSIM/carrier, install evidence, firmware campaigns, remote-command governance, RMA/warranty/replacement and compatibility catalog. G5B builds/certifies J1939 high-value acquisition and PT40/OEM hardware paths against real devices.

### Wave 6
G6A executes representative 1K–5K+ performance, reconnect storms, backlog recovery, restart/failure, backup/restore, DR, RPO/RTO, observability and operational SLO evidence. G6B completes tenant onboarding, support/SLA/escalation, billing/metering/entitlements, hardware/provider add-ons, packaging, price/limits, training, RMA and release operations.

Wave 6 subgates may technically pass before every earlier capability is certified, but `COMMERCIAL RELEASE — GO` is package-aware and cannot include a capability whose owning gate has not passed.

## 5. Universal evidence and truth rules remain binding

- Visible Chrome + real persisted data for customer-facing acceptance.
- Real provider for provider claims.
- Physical hardware for hardware claims.
- Jurisdiction-specific regulatory evidence for ELD/HOS claims.
- Exact-SHA final acceptance.
- 0 unresolved P0/P1 at CERTIFIED / PRODUCTION READY status.
- Implementation teams cannot self-certify.
- Appendix B qualified-human acceptance remains required where specified.
- AI/non-human reviews are supporting analysis only.
- No fabricated telemetry, diagnostics, HOS, camera/video, provider or hardware evidence.

## 6. Integration/closure cadence

Each lane maintains a concise evidence ledger and works defect-by-defect. The integration board reviews conflicts continuously rather than waiting for a wave boundary.

A lane is allowed to declare `ENGINEERING COMPLETE / EXTERNAL EVIDENCE HOLD` when all controllable work is green and only genuine external evidence remains. That state keeps the lane active without blocking other waves.

## 7. Final objective

The program now attempts to close Waves 1–6 concurrently. Final outcome remains:

**COMMERCIAL RELEASE — GO**

with package-specific certified capabilities, production-scale evidence, recovery proof, supportability, billing and truthful commercial claims.