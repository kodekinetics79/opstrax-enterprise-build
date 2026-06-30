# Stage 7 P0-B1C Startup Check

| Area | Finding | Evidence | Risk | Action |
|---|---|---|---|---|
| Repo context | Local checkout is correct and unchanged on the deployment side. | `pwd` = `/Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx`; work stayed local. | Low | Keep all work local. |
| Backend startup path | Revenue readiness is wired into the main backend startup path. | `backend-dotnet/Program.cs` registers `RevenueReadinessSchemaService`, `RevenueReadinessService`, and maps `MapRevenueReadinessEndpoints()`. | Low | Keep startup additive. |
| Authorization | Canonical permission aliases cover revenue-readiness routes. | `backend-dotnet/Controllers/EndpointMappings.cs` and `backend-dotnet/Foundation/FoundationServices.cs`. | Medium | Preserve fail-closed behavior. |
| DB safety | Dispatcher and revenue tables are local-runtime only and additive. | `OutboxDispatcherBackgroundService` is gated by config; revenue tables are created by `RevenueReadinessSchemaService`. | Low | Do not enable production automation by default. |
| Local DB | Revenue tests bootstrap a disposable DB slice. | `backend-dotnet.Tests/RevenueReadinessPostgresTests.cs` now resets company-scoped fixtures locally. | Medium | Keep fixture reset local-only. |
| Known schema drift | Older local tables lacked some current revenue columns. | Test harness now adds missing `jobs` and `ai_recommendations` compatibility columns locally. | Medium | Treat as local compatibility only, not production DDL. |

## Verdict

The startup path is safe for local execution, but the disposable DB still needs fixture bootstrapping before revenue tests run cleanly. The runtime path is additive and does not introduce uncontrolled production background work.
