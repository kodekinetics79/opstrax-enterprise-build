# OpsTrax Master Commercialization Action Plan — v2.3 Controlled Wave 3 / Wave 4 Overlap Amendment

**Status:** CONTROLLED MASTER AMENDMENT — ACTIVE  
**Effective date:** 2026-09-03  
**Change control:** `CR-2026-09-03-02`  
**Base master:** `docs/commercialization/MASTER_ACTION_PLAN.md` v2.1  
**Prior amendment:** v2.2 / `CR-2026-09-03-01`  
**Entry baseline:** `main@c968c85bda25a21a54e2c5472eb9d42163e09b50`

## Amendment purpose

The program owner has directed that Wave 3 and Wave 4 start in parallel. This amendment activates Wave 4 without falsifying the still-open real-world evidence gates in Waves 2 and 3.

## Active program state

| Wave | Gate | State | Commercial truth |
|---|---|---|---|
| 2 | G2A Samsara | EXTERNAL CLOSURE HOLD / OPEN | PILOT |
| 2 | G2B Certified ELD partner | EXTERNAL CLOSURE HOLD / OPEN | DEVELOPMENT / ROADMAP |
| 3 | G3A HOS workflow | ACTIVE | DEVELOPMENT |
| 3 | G3B Dual-facing camera | ACTIVE | ROADMAP |
| 4 | G4A Video Safety | ACTIVE | ROADMAP |
| 4 | G4B Geotab/Motive/OEM provider breadth | ACTIVE | ROADMAP |
| 5–6 | — | LOCKED | unchanged |

## Controlled concurrency

Four gates may remain active, but no more than two shared-production-code engineering lanes execute concurrently. Non-code architecture, provider research, acceptance design, privacy/security threat analysis, contract tests and isolated preparatory work may proceed in the other active gates. This protects integration integrity while avoiding idle time.

## G4A exit result

Competitive Video Safety requires real provider-preserved evidence through the full safety workflow: event review, severity, driver/vehicle/trip context, coaching, driver acknowledgement, supervisor closure and safety history. Privacy/retention/access policy is part of the release boundary, not post-release documentation.

## G4B exit result

Provider breadth requires at least the prioritized Geotab/Motive/OEM connector path to pass the shared connector lifecycle with real authorized provider evidence, deterministic mapping, backfill/incremental sync, failure/recovery, disconnect/reconnect, tenant isolation, operator health visibility and exact-SHA customer acceptance.

## Non-negotiable dependency rules

- G4A may build provider-agnostic workflow foundations, but it may not treat G3B placeholder or provider-pending media as authentic safety evidence.
- G4B may reuse the canonical telemetry and connector lifecycle, but provider-specific claims require real account/API evidence and commercial rights.
- Wave 4 cannot close an unresolved Wave 3 P0/P1 by reclassifying it as a later-wave limitation.
- Current G3A/G3B mandatory CI defects, including migration enrollment/release-path parity, remain merge blockers.

All remaining rules in the base master and v2.2 remain unchanged unless explicitly superseded by `CR-2026-09-03-02`.
