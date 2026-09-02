# Phase 1 Dispositions

| Finding | Disposition | Evidence |
|---|---|---|
| AUD-003 symmetric permission escalation | REMEDIATED LOCALLY | directed policy, Stage9/backend/frontend/seed reconciliation, independent re-review PASS and full 2,275-test suite green |
| AUD-004 unbound/malformed customer identity | REMEDIATED LOCALLY | binding required at create/update/login/MFA/SSO; customer roles portal-only; DB and auth negatives green |
| AUD-005 production outbox disabled/invisible | CONFIRMED; ENABLEMENT BLOCKED | exact-SHA production startup logs have no dispatcher start; code adds heartbeat/readiness, expired-claim recovery and payload-free reliability counters, but protected backlog/type distribution is unavailable |
| AUD-029 branch analytics exposure | REMEDIATED LOCALLY | all eight handlers reject branch-bound principals; tenant-wide positive retained |

AUD-005 backlog magnitude and event-type/handler compatibility remain BLOCKED pending
the sanitized aggregate-only query in `AUD-005_RUNTIME_DISPOSITION.md`. The candidate
therefore does **not** enable the production worker in `render.yaml`: doing so could
drain pre-existing unhandled informational events into dead letter. Staging may enable
both switches only after its isolated aggregate is known. There is still no operator
replay UI in this Phase 1 scope; deterministic retry/dead-letter behavior is covered by
DB tests and payload-free counters are exposed for operations.
