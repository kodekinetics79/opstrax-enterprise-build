# Stage 5B P0-B1A2 Test Coverage

## Covered

- `RequirePermission` allows a valid tenant permission.
- `RequirePermission` denies missing permissions.
- `RequirePermission` denies missing tenant context.
- `RequirePermission` records a durable authorization decision when an audit service is available.
- `AuthorizationDecisionService` returns `ApprovalRequired` when the policy asks for it.
- `AuthorizationDecisionService` denies cross-tenant requests.
- `AuthorizationDecisionService` denies missing tenant context.
- `PassthroughFeatureAccessService` denies disabled subscriptions and missing tenant context.
- Approval workflow request/decision lifecycle in memory.
- Idempotency duplicate-key conflict handling.
- Domain event / outbox / inbox record creation in memory.
- High-risk approval catalog membership.
- AI reasoning run, recommendation, action request, and outcome lifecycle in memory.
- `GetCompanyId` now fails closed when tenant context is absent.

## Strengths

- Negative-path coverage exists for the main permission gate.
- The tests now prove the helper no longer silently defaults to company `1`.
- The audit path is exercised with an injected service provider.

## Remaining Gaps

- No end-to-end PostgreSQL integration test was run in this shell.
- No worker or dispatcher behavior is covered yet.
- No business-module workflow consumes the durable foundation tables yet.

