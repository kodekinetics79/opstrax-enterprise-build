# Stage 9 Test Coverage

## Added coverage

- Site access requirement creation persists tenant-scoped rows and emits an AI recommendation for missing access.
- Access document waiver requests create approval requests and do not auto-approve the waiver.
- Proof package submission without artifacts is blocked and creates an AI recommendation instead of an unsafe submit.
- High-risk smart assignment acceptance returns approval required and creates an approval request.

## Existing foundation coverage reused

- Authorization remains fail-closed.
- Approval workflow persistence remains covered.
- Domain event, outbox, inbox, event-processing, AI reasoning, and idempotency persistence remain covered by the Stage 5C / 5D foundation tests.
- Postgres dispatcher coverage already proves the worker path can consume durable events safely.

## Verification result

- `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore` passed with 831 tests.

