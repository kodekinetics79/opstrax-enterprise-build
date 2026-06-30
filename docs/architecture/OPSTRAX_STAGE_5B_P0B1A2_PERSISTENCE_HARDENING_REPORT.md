# Stage 5B P0-B1A2 Persistence Hardening Report

## Summary Verdict

The foundation slice is now materially safer and persistence-ready for core security and workflow metadata. Authorization decisions, approval requests, domain events/outbox/inbox, idempotency, and AI foundation records now have PostgreSQL-backed implementations or durable schema support.

The remaining blocker for P0-B1B is not the foundation shape, but the absence of a real worker/dispatcher path and any business-module workflows built on top of these tables.

## Review Matrix

| Area | Status | Evidence | Risk | Fix Applied | Remaining Gap | Ready for P0-B1B? |
|---|---|---|---|---|---|---|
| Authorization engine correctness | Pass | `backend-dotnet/Controllers/EndpointMappings.cs`, `backend-dotnet/Foundation/FoundationServices.cs` | Silent allow-by-default would be catastrophic | `RequirePermission` now resolves the centralized auth service, records a decision, and fails closed on missing tenant/user/role/permission context | Route callers still need to pass correct tenant context; platform admin auth remains a separate surface | Yes, for tenant-gated routes |
| Permission gate behavior | Pass | `RequirePermission`, `GetCompanyId`, tests | Hidden tenant fallback | Removed the `company_id=1` fallback; missing tenant context now throws; tests cover deny paths and decision logging | Public or unauthenticated paths must not call tenant helpers | Partial |
| In-memory vs persistent service gap | Partial | `backend-dotnet/Foundation/FoundationPersistenceServices.cs`, `Program.cs` | Memory-only security metadata is not durable | Added PostgreSQL-backed implementations for feature access, approval workflow, outbox/inbox/event recording, idempotency, and authorization audit logging | No background worker or real event dispatcher yet | No |
| SQL migration completeness | Pass | `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql`, `database/migrations/2026_06_28_stage5b_p0b1a2_persistence_hardening.sql` | Schema drift or missing correlation/retry fields | Added correlation, retry, processing, and dedupe columns plus indexes | Needs a later migration engine rollback plan if broader rollout starts | Partial |
| Migration rollback readiness | Partial | Stage 5B additive migration and rollback notes | Hard to unwind if later workflows depend on these tables | Kept migration additive and non-destructive; documented reverse-order cleanup | No automated rollback runner | Partial |
| Domain events / outbox / inbox readiness | Pass | `domain_events`, `outbox_messages`, `inbox_messages`, `event_processing_logs` | Message loss or duplicate processing | Added tenant, status, correlation, idempotency, retry, next-attempt, and processed fields | No worker/consumer yet | Partial |
| Idempotency readiness | Pass | `idempotency_keys` table and `PostgresIdempotencyService` | Duplicate replay or conflicting request reuse | Added tenant+operation+key uniqueness, request-hash conflict detection, and completion updates | Expiry cleanup is still a later job | Partial |
| AI recommendation/action request readiness | Pass | `AiReasoningRunRecord`, `AiActionRequestRecord`, `PostgresAiFoundationService` | AI could directly mutate business tables | AI now persists reasoning runs, recommendations, action requests, and outcomes with correlation metadata; no direct business-table writes were added | No approval-to-execution workflow yet | Partial |
| Approval workflow readiness | Pass | `approval_requests`, `approval_decisions`, workflow service | High-risk actions could skip review | Approval requests and decisions now persist with tenant/correlation context and the high-risk catalog remains enforced | No real business workflow consumes the approval state yet | Partial |
| Audit / correlation readiness | Pass | `authorization_decision_logs`, `ICorrelationContext`, `AmbientCorrelationContext` | Missing traceability | Authorization decisions are persisted, and correlation/request ids flow into durable records | Broader request/job audit propagation still needs later module slices | Partial |
| Test coverage quality | Pass | `backend-dotnet.Tests/FoundationTests.cs` | Regression risk on deny paths | Added coverage for audit logging, feature-access tenant denial, and missing tenant helper failure | No DB integration tests were run in this shell | Partial |

## Ready State

P0-B1B can begin as a controlled implementation slice, but only if the next work stays additive and does not pretend the foundation already has a worker or business workflow engine.

