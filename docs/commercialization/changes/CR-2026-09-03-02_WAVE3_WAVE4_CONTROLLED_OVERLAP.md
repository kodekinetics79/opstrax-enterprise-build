# CR-2026-09-03-02 — Wave 3 / Wave 4 Controlled Overlap

**Status:** APPROVED / ACTIVE  
**Effective date:** 2026-09-03  
**Executive owner:** CTO Office / OpsTrax Commercialization Program  
**Parent tracker:** #110  
**Entry baseline:** `main@c968c85bda25a21a54e2c5472eb9d42163e09b50`  
**Supersedes only the Wave-4 lock/concurrency portion of:** `CR-2026-09-03-01`  

## Decision

Activate Wave 4 in controlled overlap while Wave 3 remains active. This accelerates engineering preparation and bounded implementation; it does **not** close, waive, inherit, or promote any Wave 2 or Wave 3 gate.

Active gates after this change:
- G3A / #128 — OpsTrax HOS operational workflow.
- G3B / #129 — dual-facing camera partner integration.
- G4A / #125 — Video Safety workflow.
- G4B / #123 — Geotab/Motive/OEM provider breadth.

Wave 5–6 remain LOCKED.

## Concurrency rule

Four gates may be ACTIVE, but at most **two merge-bound engineering-intensive implementation lanes** may modify shared production code at the same time. The other active gates execute architecture, contract definition, provider evaluation, threat/privacy analysis, deterministic test-harness work, UI/UX workflow design, and non-conflicting preparatory changes until a code lane is available.

Priority order for shared-code capacity:
1. P0/P1 remediation in an active gate.
2. G3A source-truth/HOS safety work.
3. G3B camera/provider/media truth work.
4. G4A Video Safety workflow implementation.
5. G4B provider-breadth implementation.

A gate may temporarily consume a code lane when its dependency is sufficiently stable and the change is isolated. No cross-lane merge may proceed with unresolved P0/P1 or a broken mandatory CI/release gate.

## Wave 3 protections

- G3A remains DEVELOPMENT/ROADMAP until certified-source and jurisdiction-specific ELD/HOS evidence exists.
- G3B remains ROADMAP until a real authorized provider account, exact dual-facing hardware/firmware, authentic events/clips, privacy/security controls and independent SDET acceptance are proven.
- Current Wave 3 CI defects, including migration-enrollment parity, are real defects and may not be bypassed to make room for Wave 4.

## G4A — Video Safety scope authorized now

Start the provider-preserving safety workflow:
`Event -> Review -> Severity -> Driver -> Vehicle -> Trip -> Coaching -> Driver acknowledgement -> Supervisor closure -> Safety history`.

Authorized preparation/implementation includes:
- canonical review/coaching/acknowledgement/closure state model;
- immutable linkage to source camera/provider evidence;
- reviewer decision provenance and non-destructive history;
- RBAC/tenant/branch/driver boundaries;
- inward-facing privacy, retention, access audit and legal-hold enforcement points;
- coaching assignment, due dates, acknowledgement, escalation and supervisor closure;
- responsive high-volume review UX and accessibility contracts;
- idempotency, retry/recovery and exact-SHA evidence harnesses;
- provider-pending/unavailable states that never fabricate playable media or AI findings.

Video Safety remains **ROADMAP** until real camera/provider evidence and independent Safety + Privacy + Security + SDET acceptance close the gate.

## G4B — Geotab/Motive/OEM provider breadth scope authorized now

Start with the existing canonical telemetry model and shared connector lifecycle. Do not fork provider-specific truth models.

Authorized preparation/implementation includes:
- provider priority/scoring based on actual sales pipeline, installed base, API depth and commercial rights;
- common Connect -> Authenticate -> Discover -> Map -> Validate -> Backfill -> Incremental Sync -> Monitor -> Disconnect/Reconnect lifecycle;
- deterministic mapping/reconciliation and explicit ambiguous/unmatched states;
- provider provenance, optional/unknown measurements, stale-feed truth and sync-freshness semantics;
- token lifecycle, least privilege, tenant/branch isolation and audit;
- rate limits, pagination/cursors, webhook/polling, retry/idempotency and recovery contract tests;
- provider-specific adapter scaffolds only where contracts are evidenced; unsupported fields remain explicit;
- visible customer onboarding/health/recovery UX contracts.

No Geotab, Motive or OEM connector is CERTIFIED/PRODUCTION READY until a real authorized account/API path, authentic provider data, scale/recovery evidence, exact-SHA Chrome journey and independent Security + SDET + Fleet Product acceptance exist.

## Evidence and acceptance rules retained

All Appendix B SME requirements, no-self-certification rules, exact-SHA deployment evidence, persisted-data reconciliation, visible Chrome acceptance, real-provider/device requirements, privacy/security review, performance/recovery gates and Capability Truth Matrix controls remain binding.

Every defect follows:
`Observe -> Evidence -> Root Cause -> Fix -> Targeted Test -> Integration -> Exact-SHA Deploy -> Same Journey Retest -> Independent Acceptance -> Close`.

This change authorizes execution only. It is not a capability promotion, certification, production deployment approval, regulatory waiver, privacy waiver, or provider commercial-rights evidence.
