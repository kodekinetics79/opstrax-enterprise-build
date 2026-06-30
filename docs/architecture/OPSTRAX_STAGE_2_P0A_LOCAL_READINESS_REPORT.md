# OpsTrax Stage 2 P0-A Local Readiness Report

## Repo Inspection

| Area | Finding | Evidence | Risk | Action Taken | Next Action |
|---|---|---|---|---|---|
| Working tree | Clean for source changes in this run | `git status --short -- .` | Low | No source edits yet | Keep unrelated untracked files untouched |
| Branch | `opstrax-product-main` | `git branch --show-current` | Low | Documented | None |
| Backend root | `backend-dotnet/` is the active .NET backend | `backend-dotnet/Program.cs`, `docker-compose.yml` | Medium | Confirmed | Use this for local build checks |
| Frontend root | `frontend/` | `frontend/package.json` | Low | Confirmed | Use this for build/lint checks |
| DB provider | PostgreSQL | `.env.example`, `backend-dotnet/Data/Database.cs`, `backend/src/lib/db.ts` | Medium | Confirmed | Update docs that still say MySQL |
| Migration tooling | Idempotent schema bootstrap services | `backend-dotnet/Services/*.cs` | Medium | Confirmed | Plan formal migrations in P0-B |
| Local run commands | `start-local.sh`, `docker compose up --build` | `start-local.sh`, `docker-compose.yml` | Low | Confirmed | None |

## Build Verification

| Area | Result | Evidence | Notes |
|---|---|---|---|
| .NET backend build | Passed | `dotnet build backend-dotnet/Opstrax.Api.csproj` | 0 errors, warnings only |
| Backend tests | Passed | `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore` | 790 tests passed |
| Frontend build | Passed | `npm run build` in `frontend/` | Vite production build completed |
| Frontend lint | Passed | `npm run lint` in `frontend/` | ESLint completed cleanly |
| Node backend build | Passed | `npm run build` in `backend/` | TypeScript compiled cleanly |

## Postgres Transition Gap Check

| Item | Current State | PostgreSQL Target | Gap | Risk | Recommended Fix | P0/P1/P2 |
|---|---|---|---|---|---|---|
| Provider docs | README still says MySQL | PostgreSQL-first | Documentation mismatch | Confusion during implementation | Update docs only, keep code as truth | P0 |
| Connection strings | `PG_CONNECTION` and `ConnectionStrings__DefaultConnection` | Same | None | Low | Preserve current env contract | P0 |
| Backend schema | Runtime bootstrap tables in .NET services | Formal migrations + versioning | No rollback history | Schema drift | Add migration framework later | P1 |
| Node backend config | Parses PostgreSQL connection strings | PostgreSQL | None | Low | Keep current parser | P0 |
| Legacy config | `api-dotnet/appsettings.json` still has MySQL | PostgreSQL | Legacy file mismatch | Repo confusion | Treat as legacy and document explicitly | P1 |

## Auth / API / Session Check

| Item | Finding | Evidence | Risk | Action Taken | Next Action |
|---|---|---|---|---|---|
| Login flow | Present and token-based | `frontend/src/hooks/useAuth.tsx`, `backend-dotnet/Controllers/EndpointMappings.cs` | Medium | Reviewed | None |
| Token storage | `opstrax.session.v2` localStorage | `frontend/src/hooks/useAuth.tsx`, `frontend/src/services/apiClient.ts` | Medium | Reviewed | Keep consistent key usage |
| Refresh/session handling | Server-side logout plus local clear | `frontend/src/services/apiClient.ts` | Medium | Reviewed | Avoid false-positive logout on non-session 401s |
| API base URL | Derived from Vite env with localhost fallback | `frontend/src/services/apiClient.ts` | Low | Reviewed | None |
| Bearer attachment | Request interceptor sets Authorization | `frontend/src/services/apiClient.ts` | Low | Reviewed | None |
| 401 handling | Clears session and redirects unless skipped | `frontend/src/services/apiClient.ts` | Medium | Reviewed | Watch for route-specific false positives |
| Platform vs tenant auth | Separate platform auth hook/client | `frontend/src/hooks/usePlatformAuth.tsx`, `backend-dotnet/Controllers/EndpointMappings.cs` | Low | Reviewed | Preserve the separation |

## QA Issue Verification

| Issue | Status | Evidence | Notes |
|---|---|---|---|
| Header search icon/input collapse issue | Not reproduced | `frontend/src/layouts/AppShell.tsx` | Search field exists and is wired |
| Search not working | Not reproduced | `frontend/src/layouts/AppShell.tsx` | Enter key navigation is implemented |
| Translation feature not working | Deferred | `frontend/src/layouts/AppShell.tsx` | UI is intentionally hidden in the shell |
| Profile icon logs user out unexpectedly | Not reproduced | `frontend/src/layouts/AppShell.tsx` | Logout is a separate menu action |
| Missing skeleton loaders during API calls | Not reproduced | `frontend/src/components/ui.tsx`, `frontend/src/pages/*` | Skeleton/loading states exist |
| Missing form validation across screens | Partially built | `frontend/src/pages/*` | Some forms have validation, some remain thin |
| Command Center UI/UX not enterprise-grade | Partially built | `frontend/src/pages/CommandCenterPage.tsx` | Product still has polish headroom |
| Live Map logs user out due to auth issue | Blocked by missing runtime proof | `frontend/src/pages/LiveMapPage.tsx`, `frontend/src/services/apiClient.ts` | Needs live browser validation |
| Fleet Health header/layout inconsistency | Not reproduced | `frontend/src/pages/FleetHealthPage.tsx` | Layout is present and structured |
| Fleet Health Summary API auth failure | Blocked by missing runtime proof | `frontend/src/services/fleetHealthApi.ts` | Needs live API/session verification |
| Fleet Health Risks API auth failure | Blocked by missing runtime proof | `frontend/src/services/fleetHealthApi.ts` | Needs live API/session verification |
| Fleet Health tab text hidden under stats card | Not reproduced | `frontend/src/pages/FleetHealthPage.tsx` | No obvious code-level overlap found |
| Create Work Order API failure | Not reproduced | `frontend/src/pages/FleetHealthPage.tsx`, `backend-dotnet/Controllers/EndpointMappings.cs` | The API path exists |
| Vehicle detail view non-functional | Blocked by missing runtime proof | `frontend/src/pages/VehiclesPage.tsx` | Needs browser/runtime check |
| Driver detail view non-functional | Blocked by missing runtime proof | `frontend/src/pages/DriversModulePage.tsx` | Needs browser/runtime check |
| Alert Center layout inconsistency | Partially built | `frontend/src/pages/AlertsCenterPage.tsx` | Layout exists but can still be refined |
| Alert Center card UI needs improvement | Partially built | `frontend/src/pages/AlertsCenterPage.tsx` | Visual polish remains |
| Hardcoded fake/demo data | Partially built | `frontend/src/data/developmentFleetSeedData.ts` | Demo data is still present by design |
| Tenant isolation gaps | Needs tenant isolation check | `backend-dotnet/Controllers/EndpointMappings.cs`, `backend-dotnet/Services/*` | Requires deeper module-by-module audit |
| API calls missing bearer token | Not reproduced | `frontend/src/services/apiClient.ts` | Interceptor attaches token |
| Inconsistent API base URL usage | Not reproduced | `frontend/src/services/apiClient.ts`, `frontend/src/services/platformApi.ts` | Current env logic is consistent |

## Safe Fixes Applied
- None in this run.

## Commands Run
- `pwd`
- `git status --short -- .`
- `git branch --show-current`
- `dotnet build backend-dotnet/Opstrax.Api.csproj`
- `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore`
- `npm run build` in `frontend/`
- `npm run lint` in `frontend/`
- `npm run build` in `backend/`

## Remaining Blockers
- Formal PostgreSQL migration framework
- Full tenant-isolation audit
- Live browser proof for a few runtime-only QA items
- Deeper canonical ERD field detail

