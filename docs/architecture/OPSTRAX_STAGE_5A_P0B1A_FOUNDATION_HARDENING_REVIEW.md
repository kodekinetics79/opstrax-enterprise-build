# Stage 5A P0-B1A Foundation Hardening Review

## Summary Verdict

The foundation slice is materially safer after hardening, but several pieces remain transitional and must be persistent-backed before P0-B1B is treated as production-adjacent.

## Review Matrix

| Area | Status | Evidence | Risk | Fix Applied | Remaining Gap | Ready for P0-B1B? |
|---|---|---|---|---|---|---|
| Authorization engine correctness | Partial | `backend-dotnet/Controllers/EndpointMappings.cs`, `backend-dotnet/Foundation/FoundationServices.cs` | Permission gate could have fallen back to tenant default context | Added fail-closed tenant/user/role checks in `RequirePermission`; added tenant-context denial in `AuthorizationDecisionService` | No persistent policy source yet; platform vs tenant admin split still relies on caller path discipline | Partial |
| Permission gate behavior | Pass | `RequirePermission` now stores a decision object and denies on missing context | Accidental allow-by-default | Deny on missing permissions, missing user/tenant/role, and unsupported decision state | GetCompanyId still has a legacy default helper for other call sites | Yes, for guarded tenant routes |
| In-memory vs persistent gap | Partial | `FoundationServices.cs` is in-memory only | Data loss on restart; no durable workflows yet | Documented as transitional; added SQL schema and test coverage | No DB-backed repositories, workers, or command handlers yet | No |
| SQL migration completeness | Partial | `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql` | Schema is additive but lacks rollback companion | Added additive PostgreSQL tables, indexes, JSONB/timestamptz usage, and tenant-scoped keys where needed | `approval_policies` scope is still hybrid, and there is no paired rollback script | Partial |
| Migration rollback readiness | Partial | Local migration file plus rollback notes | Hard to reverse cleanly if applied locally | Kept migration additive and non-destructive; added rollback checklist artifact | Rollback is still manual and file-driven rather than a formal migration engine rollback | Partial |
| Domain event/outbox/inbox readiness | Pass | `domain_events`, `outbox_messages`, `inbox_messages`, `event_processing_logs` tables and in-memory publisher | Durable delivery not yet implemented | Modeled tenant, correlation, causation, idempotency, status, and processing logs | No background dispatcher/worker yet | Partial |
| Idempotency readiness | Pass | `idempotency_keys` schema and `InMemoryIdempotencyService` | Duplicate/replay handling could be inconsistent without persistence | Unique `(tenant_id, operation, idempotency_key)` plus duplicate-hash test | Still in-memory only | Partial |
| AI recommendation/action request readiness | Pass | AI records, tests, and schema in `Foundation*` files | AI could be overtrusted if later wired directly to business tables | Added reasoning runs, recommendation/action request/outcome records, and a no-direct-execute test | No persistent AI workflow orchestration yet | Partial |
| Approval workflow readiness | Partial | `ApprovalRequestRecord`, `ApprovalDecisionRecord`, approval tests, and high-risk catalog | Approval could be skipped if future callers bypass the catalog | Added approval-required catalog for the listed high-risk actions and lifecycle tests | Approval persistence and tenant-scoped policy evaluation still need a real workflow path | Partial |
| Audit/correlation readiness | Pass | `ICorrelationContext`, `AmbientCorrelationContext`, `IAuditLogService`, decision object in `HttpContext.Items` | Audit trail remains memory-only | Added correlation and authorization decision records/objects | No durable audit log writer yet | Partial |
| Test coverage quality | Pass | `backend-dotnet.Tests/FoundationTests.cs` | Review could miss key negative cases | Added negative tests for missing tenant context, missing permission, feature access denial, approval catalog, idempotency, and AI no-direct-execute behavior | Coverage is still foundation-level, not workflow-level | Yes, for foundation only |

## Critical Findings

- The permission gate now fails closed on missing tenant/user/role context.
- The authorization service still depends on caller-provided tenant context and in-memory permissions, so it is not yet a durable policy engine.
- The approval model now captures high-risk actions, but it does not yet persist real approval workflows into business flows.
- The migration is additive and safe to stage locally, but there is still no paired rollback artifact or worker infrastructure.

## Explicit Gap List Before P0-B1B Is Treated As Safe

- Persistent authorization policy storage and resolution.
- Persistent approval request/decision writes from at least one real workflow.
- Persistent outbox/inbox dispatch and replay handling.
- Persistent idempotency reservation/completion path.
- Durable audit log writer and correlation propagation across requests and jobs.

## P0-B1B Readiness

The foundation is ready for a controlled P0-B1B implementation slice, but not for production-adjacent rollout.
The gating reason is persistence, not shape: the in-memory services still need durable storage and a worker path before any sensitive module should rely on them.
