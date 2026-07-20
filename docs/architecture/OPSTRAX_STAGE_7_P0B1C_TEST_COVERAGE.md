# Stage 7 P0-B1C Test Coverage

## Full verification

- `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore`
- Result: `824` passed, `0` failed

## Stage 7-specific coverage

- Ready-to-bill success path writes the domain event and outbox row.
- Missing-charges path creates a governed AI recommendation.
- Invoice draft creation copies job charges and is idempotent.
- Revenue summary and customer summary are tenant-scoped.
- Cross-tenant job operations are rejected.
- Leakage signals are created for missing pricing and missing drafts.
- Active rate-card changes require approval.

## Harness notes

- The revenue tests bootstrap the disposable DB slice locally.
- Fixture reset was added so reruns do not fail on stale tenant rows.
- Older local schema drift is handled in test harness only.

## Residual gap

- The stage is covered well for the revenue slice, but it does not yet include the later invoicing/payment/AR runtime.
