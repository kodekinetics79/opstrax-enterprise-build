# Stage 15B Fake / Demo / Fallback Verification

| File/Surface | Pattern Found | Live Surface? | Risk | Fixed / Deferred | Reason | Final Status |
|---|---|---|---|---|---|---|
| `frontend/src/services/adminApi.ts` | No seed fallback remains. | Yes | Low | Fixed | Live permissions endpoint now drives the UI. | Fixed |
| `frontend/src/services/incidentsApi.ts` | Static/fake success residue removed. | Yes | Low | Fixed | Incident actions now surface honest failures. | Fixed |
| `frontend/src/services/fuelApi.ts` | Legacy compatibility residue removed. | Yes | Low | Fixed | Finance surface uses live rows now. | Fixed |
| `frontend/src/services/safetyApi.ts` | No fake success on live dashboard paths. | Yes | Low | Fixed | Safety dashboard bridge remains honest. | Fixed |
| `frontend/src/services/fleetDomainApi.ts` | `withFallback` exists only as a no-op compatibility shim. | Yes | Low | Deferred | It does not fabricate runtime rows. | Deferred |
| `frontend/src/pages/TripsPage.tsx` | No fake trip success added. | Yes | Low | Kept | Trip page stays live and permissioned. | Kept |
| `frontend/src/pages/CommandCenterPage.tsx` | No demo masking added. | Yes | Low | Kept | Dashboard remains live-data first. | Kept |
| `frontend/src/pages/LeadsPage.tsx` / `OpportunitiesPage.tsx` / `QuotationsPage.tsx` | Seed builders remain behind existing compatibility layers. | Yes | Medium | Deferred | Older commercial surfaces still need separate cleanup if they are ever promoted further. | Deferred |
| `frontend/src/pages/GeofenceManagementPage.tsx` / `routesApi.ts` / `dashcamApi.ts` / similar legacy surfaces | Seed/demo scaffolding exists in older surfaces. | Mixed | Medium | Deferred | These are not the live surfaces changed in this pass. | Deferred |
| `backend-dotnet/Services/*SchemaService.cs` | Explicit seed/bootstrap language exists in backend setup. | No | Low | Kept | Backend bootstraps are not runtime masking. | Kept |

