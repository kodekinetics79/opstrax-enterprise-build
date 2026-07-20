# Stage 15C Worktree Inventory

| Path | Status | Category | Should Stage? | Reason | Action |
|---|---|---|---|---|---|
| `.gitignore` | Modified | config | Yes | Safe hygiene addition for local env and temp artifacts. | Stage with the hygiene docs if desired. |
| `backend-dotnet/Controllers/EndpointMappings.cs` | Modified | backend source | Yes | Existing product work from prior stages. | Stage only in the backend source group. |
| `backend-dotnet/Controllers/PlatformEndpoints.cs` | Modified | backend source | Yes | Existing product work from prior stages. | Stage only in the backend source group. |
| `backend-dotnet/Program.cs` | Modified | backend source | Yes | Existing product work from prior stages. | Stage only in the backend source group. |
| `backend-dotnet/Services/*` | Modified | backend source | Yes | Existing product and schema wiring changes. | Stage in the backend source group. |
| `backend-dotnet.Tests/*` | Untracked | test source | Yes | Regression test additions from prior stages. | Stage in the test group, excluding generated output. |
| `backend-dotnet/Foundation/*` | Untracked | backend source | Yes | Foundation runtime code already under review. | Stage in the backend source group. |
| `backend-dotnet/Controllers/SafetyMaintenanceFoundationEndpoints.cs` | Untracked | backend source | Yes | Live foundation endpoint work. | Stage in the backend source group. |
| `backend-dotnet/Controllers/Stage9Endpoints.cs` | Untracked | backend source | Yes | Stage 9 API work. | Stage in the backend source group. |
| `backend-dotnet/Controllers/BusinessSpineEndpoints.cs` | Untracked | backend source | Yes | Business spine support code. | Stage in the backend source group. |
| `backend-dotnet/Controllers/RevenueReadinessEndpoints.cs` | Untracked | backend source | Yes | Revenue readiness support code. | Stage in the backend source group. |
| `backend-dotnet/Services/OutboxDispatcherBackgroundService.cs` | Untracked | backend source | Yes | Foundation dispatcher runtime code. | Stage in the backend source group. |
| `database/migrations/` | Untracked | database migration | Yes, additive only | Contains additive stage migrations. | Stage only reviewed additive migrations. |
| `docs/architecture/` | Untracked | docs | Yes | Release-readiness and architecture records. | Stage review docs separately. |
| `frontend/src/App.tsx` | Modified | frontend source | Yes | Route wiring changes from prior stages. | Stage in the frontend source group. |
| `frontend/src/layouts/AppShell.tsx` | Modified | frontend source | Yes | Dashboard / nav wiring changes. | Stage in the frontend source group. |
| `frontend/src/modules/moduleConfig.ts` | Modified | frontend source | Yes | Visible module/nav metadata. | Stage in the frontend source group. |
| `frontend/src/pages/CommandCenterPage.tsx` | Modified | frontend source | Yes | Dashboard live UX changes. | Stage in the frontend source group. |
| `frontend/src/pages/TripsPage.tsx` | Untracked | frontend source | Yes | Dedicated Trips page. | Stage in the frontend source group. |
| `frontend/src/pages/OperationsProofCenterPage.tsx` | Untracked | frontend source | Yes | Proof Center surface. | Stage in the frontend source group. |
| `frontend/src/pages/FeatureFlagsPage.tsx` | Modified | frontend source | Yes | Live error handling improvements. | Stage in the frontend source group. |
| `frontend/src/pages/FinancialAnalyticsPage.tsx` | Modified | frontend source | Yes | Live finance export/error hardening. | Stage in the frontend source group. |
| `frontend/src/pages/LiveMapPage.tsx` | Modified | frontend source | Yes | Live map hardening. | Stage in the frontend source group. |
| `frontend/src/pages/platform/*` | Modified/Untracked | frontend source | Yes | Platform admin/commercial ops work. | Stage in the frontend source group. |
| `frontend/src/services/*.ts` | Modified/Untracked | frontend source | Yes | API client hardening and live endpoints. | Stage in the frontend source group. |
| `mobile/` | Untracked workspace | mobile workspace | No | Explicitly excluded from this stage. | Leave local-only. |
| `frontend/dist/` | Untracked generated artifact | generated artifact | No | Build output. | Exclude. |
| `frontend/node_modules/` | Untracked dependency folder | dependency folder | No | Installed dependencies. | Exclude. |
| `backend-dotnet/bin/` | Untracked generated artifact | generated artifact | No | .NET build output. | Exclude. |
| `backend-dotnet/obj/` | Untracked generated artifact | generated artifact | No | .NET intermediate output. | Exclude. |
| `.env` | Untracked local env | secret/local env | No | Local connection string risk; must stay local. | Keep untracked and out of any stage. |
| `.claude/`, `CLAUDE.md` | Untracked local agent files | unknown | No | Local tooling metadata, not release source. | Exclude. |

