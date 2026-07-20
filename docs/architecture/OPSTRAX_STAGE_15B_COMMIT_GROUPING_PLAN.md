# Stage 15B Commit Grouping Plan

| Commit Group | Files Included | Purpose | Risk | Recommended Commit Message |
|---|---|---|---|---|
| 1. Core backend foundations and schema services | `backend-dotnet/Program.cs`, `backend-dotnet/Controllers/EndpointMappings.cs`, schema/service files | Preserve the backend foundation and routing work. | Medium | `feat(backend): harden foundations and live endpoints` |
| 2. Operations / POD / Proof Center | `frontend/src/pages/OperationsProofCenterPage.tsx`, related services | Keep the proof workflow surfaces together. | Medium | `feat(frontend): add operations proof center` |
| 3. Platform commercial ops | `frontend/src/pages/platform/*`, `backend-dotnet/Controllers/PlatformEndpoints.cs` | Keep platform admin changes together. | Medium | `feat(platform): harden commercial ops` |
| 4. Telemetry / Live Map | `frontend/src/pages/LiveMapPage.tsx`, `frontend/src/services/telemetryApi.ts`, telemetry services | Keep live-map and telemetry wiring together. | Medium | `feat(telemetry): wire live map and telemetry state` |
| 5. Safety / Maintenance foundation | `backend-dotnet/Services/SafetyBackgroundService.cs`, `backend-dotnet/Services/MaintenanceBackgroundService.cs`, corresponding frontend pages | Keep safety/maintenance foundations coherent. | Medium | `feat(safety): harden maintenance and safety foundations` |
| 6. Dashboard / route-nav / live fallback hardening | `frontend/src/pages/CommandCenterPage.tsx`, `frontend/src/layouts/AppShell.tsx`, `frontend/src/modules/moduleConfig.ts` | Preserve the visible dashboard and route integrity. | Low | `feat(frontend): keep dashboard and trips navigation honest` |
| 7. Trips workflow | `frontend/src/pages/TripsPage.tsx`, trip-related backend/tests | Keep trip completion separate from other work. | Low | `feat(trips): add dedicated trip workflow` |
| 8. Finance / feature flag / productization hardening | `frontend/src/pages/FinancialAnalyticsPage.tsx`, `frontend/src/pages/FeatureFlagsPage.tsx`, finance backends | Keep finance and control-surface fixes together. | Medium | `fix(frontend): harden finance and feature flag flows` |
| 9. Regression tests | `backend-dotnet.Tests/*` | Keep new regression tests isolated. | Low | `test: lock in stage regressions` |
| 10. Architecture docs | `docs/architecture/OPSTRAX_STAGE_15B_*` | Preserve release-readiness evidence separately from runtime code. | Low | `docs: add stage 15B hardening report set` |

Excluded from any commit:
- `frontend/dist/`
- `frontend/node_modules/`
- `backend-dotnet/bin/`
- `backend-dotnet/obj/`
- `mobile/dist/`
- `mobile/node_modules/`
- local `.env`

