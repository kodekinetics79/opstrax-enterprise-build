# G4A — Video Safety Execution Charter

**Gate:** #125  
**Change control:** `CR-2026-09-03-02`  
**Entry baseline:** `main@547f482dbf47e6f442c5d1f3e3b23806a49872cf`  
**Branch:** `wave4/g4a-video-safety`  
**Commercial truth:** ROADMAP

## First bounded implementation batch

Build the provider-preserving operational spine only. Do not duplicate G3B camera ingest or fabricate media.

1. Safety review case state model: Unreviewed -> Reviewing -> Confirmed/Dismissed/Escalated -> CoachingRequired/NoCoaching -> AwaitingDriver -> FollowUp -> Closed.
2. Immutable source linkage to provider event/media reference plus tenant, branch, vehicle, driver, trip, event/source timestamps and provenance.
3. Reviewer decision ledger: severity, disposition, reason, reviewer, timestamps and non-destructive correction history.
4. Coaching case: assignee, driver, due date, coaching content/reference, acknowledgement state, notes, escalation and supervisor closure.
5. Driver acknowledgement/dispute/comment evidence without mutation of provider facts.
6. Privacy/access policy checks before inward-facing media/reference exposure; retention/legal-hold state carried into review and export paths.
7. High-volume review queue/detail UX contract with responsive/accessibility criteria and honest media-unavailable/provider-pending states.
8. Idempotency/retry/recovery tests for duplicate provider events, delayed media, expired media references and repeated workflow commands.

## Truth boundaries

- Provider event facts are immutable source evidence.
- Reviewer disposition is a human operational decision and must be stored separately from provider facts.
- Derived score/risk/AI outputs must identify their source inputs and cannot masquerade as provider evidence.
- No playable media is synthesized when the provider/media boundary is unavailable.
- No production privacy claim without independent Privacy + Security + SDET acceptance.

## Targeted evidence matrix

| Area | Supporting automation | Required final evidence |
|---|---|---|
| Review state machine | unit + DB integration | visible Chrome persisted journey |
| Tenant/branch/driver authorization | RBAC/RLS tests | negative direct-route/API Chrome checks |
| Event/media provenance | contract + DB reconciliation | real provider/camera evidence via G3B |
| Coaching/acknowledgement | unit + integration | Manager + Driver + Supervisor Chrome journey |
| Privacy/retention/access | policy/security tests | independent privacy/security evidence |
| Duplicate/retry/recovery | integration/replay tests | failure/recovery run on exact candidate |
| Scale/UI | load/UI automation | representative queue volume + responsive Chrome |

## First acceptance checkpoint

The first batch may merge only when:
- mandatory CI/release gates are green;
- 0 open P0/P1 in the changed scope;
- provider-pending data remains visibly and semantically non-authentic;
- no cross-tenant/branch/driver exposure;
- SDET independently repeats the bounded workflow against persisted data;
- the PR does not claim Video Safety production readiness.
