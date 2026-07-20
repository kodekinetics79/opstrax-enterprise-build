# Stage 15A Trips Completion

| Area | Delivered | Evidence | Gap | Final Status |
|---|---|---|---|---|
| Route | `/trips` now exists and is build-reachable. | `frontend/src/App.tsx` | None | Done |
| Navigation | Trips is visible in the Dispatch section. | `frontend/src/layouts/AppShell.tsx`, `frontend/src/modules/moduleConfig.ts` | None | Done |
| Page | Trips page uses the live trip API and shows honest states. | `frontend/src/pages/TripsPage.tsx` | Minor UI polish only | Done |
| Actions | Start / complete / exception actions are permissioned. | `frontend/src/pages/TripsPage.tsx`, `backend-dotnet/Controllers/EndpointMappings.cs` | None | Done |
| Detail workflow | Stops, compliance, breadcrumbs and links to dispatch/jobs/proof are surfaced. | `frontend/src/pages/TripsPage.tsx` | Some rows may be empty until data exists | Done |
| Regression coverage | Trips source wiring is covered. | `backend-dotnet.Tests/Stage15SourceRegressionTests.cs` | More runtime coverage would be useful | Done |

