# Stage 5C Foundation Smoke Test Report

## Result

Passed.

## Test Command

- `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore`

## Coverage Confirmed

- Authorization decision persistence
- Approval request and approval decision persistence
- Idempotency reserve duplicate detection
- Domain event persistence
- Outbox write persistence
- Inbox record persistence
- Event processing log persistence
- AI reasoning run persistence
- AI recommendation persistence
- AI action request persistence
- No AI outcome written automatically

## Observed Behavior

- `AuthorizationDecisionService` returned `Allowed` for the smoke actor and permission set.
- `PostgresAuditLogService` wrote the authorization decision log row.
- `PostgresApprovalWorkflowService` created an approval request and then stored an approval decision.
- `PostgresIdempotencyService` returned the same record for the duplicate request hash and rejected a conflicting hash.
- `PostgresDomainEventPublisher.Publish(...)` wrote a domain event and its paired outbox row.
- `PostgresDomainEventPublisher.Write(...)` wrote an additional outbox row.
- `PostgresDomainEventPublisher.Record(...)` wrote an inbox row and a matching event processing log row.
- `PostgresAiFoundationService` created a reasoning run, completed it, created a recommendation, and created an approval-required action request.
- No `ai_action_outcomes` row was created unless explicitly recorded later.

## Key Counts Observed

- `authorization_decision_logs`: 1
- `approval_requests`: 1
- `approval_decisions`: 1
- `idempotency_keys`: 1
- `domain_events`: 1
- `outbox_messages`: 2
- `inbox_messages`: 1
- `event_processing_logs`: 1
- `ai_reasoning_runs`: 1
- `ai_recommendations`: 1
- `ai_action_requests`: 1
- `ai_action_outcomes`: 0

## Interpretation

- The foundation is not just a superficial wrapper.
- The local PostgreSQL persistence path is behaving as intended for the Stage 5C slice.
- The remaining gap is runtime processing of queued rows, not schema shape.
