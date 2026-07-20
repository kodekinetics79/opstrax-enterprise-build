# Stage 9 Local Worktree Baseline

This baseline was captured before Stage 9 implementation work so the new operational-proof slice can be traced cleanly against the current local changes.

| Area | Finding | Evidence | Risk | Action Taken | Next Action |
|---|---|---|---|---|---|
| Branch and cwd | Working in the expected Opstrax checkout on `opstrax-product-main`. | `pwd`, `git branch --show-current` | Low | Confirmed repo and branch before edits. | Keep Stage 9 changes local only. |
| Existing local changes | Worktree already contains prior stage edits and untracked files. | `git status --short`, `git diff --stat` | Medium | Noted existing modifications without reverting anything. | Avoid mixing unrelated changes into the Stage 9 diff. |
| Finance foundation | Stage 8 finance activation is already present and verified locally. | `backend-dotnet/Program.cs`, `backend-dotnet/Services/FinanceActivationSchemaService.cs`, `backend-dotnet/Controllers/RevenueReadinessEndpoints.cs`, `backend-dotnet.Tests/RevenueReadinessPostgresTests.cs` | Low | Treated as upstream context only. | Do not regress revenue readiness while adding Stage 9 proof flows. |
| Mobile readiness note | The repo already contains a mobile-readiness architecture note that Stage 9 must respect. | `docs/architecture/OPSTRAX_STAGE_9_MOBILE_READINESS_NOTES.md` | Medium | Read as a hard architectural constraint. | Keep all new APIs tenant-safe, role-aware, and mobile-capable. |
| Auth and RBAC | Authorization is centralized and fail-closed at the endpoint layer. | `backend-dotnet/Controllers/EndpointMappings.cs`, `backend-dotnet/Foundation/FoundationServices.cs` | Medium | Confirmed there is a single permission gate path. | Bridge new Stage 9 permissions into the canonical auth engine. |
| Outbox/inbox runtime | A foundation dispatcher already exists for outbox and inbox processing. | `backend-dotnet/Foundation/FoundationDispatcherServices.cs`, `backend-dotnet/Services/OutboxDispatcherBackgroundService.cs`, `database/migrations/2026_06_28_stage5d_p0b1a3_dispatcher.sql` | Low | Verified the runtime dispatcher slice is already in place. | Reuse the dispatcher and event log model for Stage 9 events. |
| Business spine | Job/trip/customer/dispatch/revenue primitives already exist and should be extended, not replaced. | `backend-dotnet/Services/BusinessSpineServices.cs`, `backend-dotnet/Controllers/BusinessSpineEndpoints.cs` | Medium | Confirmed the canonical spine is already seeded. | Build Stage 9 on top of the existing job/trip model. |
| POD legacy surface | A legacy `proof_of_delivery` flow exists and is admin-oriented. | `backend-dotnet/Controllers/EndpointMappings.cs` | High | Identified as insufficient for Stage 9 mobile proof flow. | Add new proof-package foundation instead of expanding the placeholder flow. |
| Local DB readiness | Local Postgres is the intended target for tests and additive schema work. | `backend-dotnet.Tests/*.cs` and prior stage smoke tests | Medium | Scoped Stage 9 to local DB-safe, additive changes only. | Apply any migration only to clearly local DB contexts. |

