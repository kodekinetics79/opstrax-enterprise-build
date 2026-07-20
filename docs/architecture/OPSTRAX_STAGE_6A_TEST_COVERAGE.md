# Stage 6A Test Coverage

## Existing coverage retained

- Foundation authorization / approval / idempotency / outbox tests remain passing.
- Foundation dispatcher tests remain passing.
- Local DB-backed business-spine tests remain passing.

## New coverage added

- Canonical permission `customer.account.read` is accepted when the actor holds legacy `customers:view`.
- Canonical rate-card mirror upsert is tenant-scoped and idempotent by code.
- Canonical rate-card update persists in the local stage table.

## Current test result

- `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore`
- Result: `816 passed, 0 failed`

## Residual gaps

- Full legacy-table runtime coverage for customers/contracts/jobs/trips is still limited by the reduced disposable local DB shape.
- A future stage should add direct bridge tests once the local disposable DB includes the legacy business tables or an isolated bridge schema.

