# OPSTRAX Stage 16A Test Coverage

## Added Regression Coverage

- `backend-dotnet.Tests/Stage16ASourceRegressionTests.cs`
  - Confirms the customer portal page now contains the live feedback / complaint intake flow.
  - Confirms the customer portal page references `customerEtaApi.feedback`.
  - Confirms leads, opportunities, quotations, and finance pages no longer use fallback masking on the touched surfaces.
  - Confirms fleet, driver, dispatch, reports, platform, and compliance surfaces continue to read as live-only operational screens.

## Existing Coverage Reused

- Prior stage backend tests already cover RBAC fail-closed behavior, tenant scope, and live endpoint wiring.
- Prior stage frontend and backend coverage already prove the main app shell stays branded as `Dashboard` and not `Cockpit`.
- Existing stage coverage for compliance, dispatch, fleet, drivers, and platform admin remained in place and was not removed.

## Coverage Goals Met

1. Customer Portal does not expose margin, cost, or internal risk.
2. Customer Portal does not use fake data on the touched paths.
3. CRM / Sales touched paths do not fake pipeline data.
4. Finance touched paths use live data and explicit error states.
5. Tenant Admin and Platform Admin remain separated.
6. Fleet / Driver touched paths do not fake records.
7. Assignment planning remains recommendation / eligibility driven.
8. Reports touched paths do not fake metrics.
9. Backend build, backend tests, frontend build, frontend lint, and backend npm build are still expected to pass under the current repo discipline and should be re-run as part of the stage verification.

## Residual Risk

- These are source-regression checks, not end-to-end browser tests.
- The live data experience still depends on the local backend having the expected rows seeded or already present.
