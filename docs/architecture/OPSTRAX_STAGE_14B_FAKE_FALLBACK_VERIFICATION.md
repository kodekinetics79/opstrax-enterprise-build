# Stage 14B Fake / Demo / Seed Fallback Verification

| File / Surface | Pattern Found | Live Surface? | Risk | Fixed / Deferred | Reason | Final Status |
| --- | --- | --- | --- | --- | --- | --- |
| `frontend/src/services/adminApi.ts` | Old seed-backed shadow DB fallback and fake success paths | Yes | High | Fixed | Live tenant admin should fail closed and use the real backend | Fixed |
| `frontend/src/services/incidentsApi.ts` | Static timeline/recommendation stubs and fake success paths | Yes | High | Fixed | Safety incidents need real audit/history | Fixed |
| `frontend/src/services/fuelApi.ts` | Seed imports and `withFallback` compatibility residue | Yes | Medium | Fixed in Stage 14A | Seed fallback removed | Fixed |
| `frontend/src/services/safetyApi.ts` | Fake-success create fallback | Yes | High | Fixed in Stage 14A | Removed in earlier stage | Fixed |
| `frontend/src/services/fleetDomainApi.ts` | `withFallback` helper retained but no-op | Yes | Low | Deferred | Compatibility shim only; does not fabricate data | Deferred |
| `frontend/src/pages/CustomersPage.tsx` | Seed builder code remains behind inert helper | Yes | Low | Deferred | No runtime fallback after no-op helper | Deferred |
| `frontend/src/pages/AccountHealthPage.tsx` | Seed builder code remains behind inert helper | Yes | Low | Deferred | No runtime fallback after no-op helper | Deferred |
| `frontend/src/services/routesApi.ts` | Seed imports behind inert helper | Yes | Low | Deferred | Compatibility residue, not masking failure | Deferred |
| `frontend/src/services/adminApi.ts` (after fix) | No silent seed fallback remains | Yes | Low | Fixed | Uses live backend and honest errors | Fixed |
| `frontend/src/services/incidentsApi.ts` (after fix) | No silent fake success remains | Yes | Low | Fixed | Uses live backend and honest errors | Fixed |
| `backend-dotnet/Services/*SchemaService.cs` | Seed/demo language in migrations and foundation bootstraps | No, backend setup only | Low | Keep | These are explicit seeders, not live UI masking | Keep |

