# OpsTrax P0-A QA Verification

| Issue | Status | Evidence | Notes |
|---|---|---|---|
| Header search icon/input collapse issue | Not reproduced | `frontend/src/layouts/AppShell.tsx` | Search input is present and usable |
| Search not working | Not reproduced | `frontend/src/layouts/AppShell.tsx` | Enter key navigation logic exists |
| Translation feature not working | Deferred | `frontend/src/layouts/AppShell.tsx` | Feature is intentionally hidden from the demo shell |
| Profile icon logs user out unexpectedly | Not reproduced | `frontend/src/layouts/AppShell.tsx` | Logout is a separate action |
| Missing skeleton loaders during API calls | Not reproduced | `frontend/src/components/ui.tsx` | Skeleton loaders exist |
| Missing form validation across screens | Partially built | `frontend/src/pages/*` | Uneven validation coverage remains |
| Command Center UI/UX not enterprise-grade | Partially built | `frontend/src/pages/CommandCenterPage.tsx` | Further polish can be scheduled later |
| Live Map logs user out due to auth issue | Blocked by missing runtime proof | `frontend/src/pages/LiveMapPage.tsx` | Needs live browser session check |
| Fleet Health header/layout inconsistency | Not reproduced | `frontend/src/pages/FleetHealthPage.tsx` | No obvious code-level issue |
| Fleet Health Summary API auth failure | Blocked by missing runtime proof | `frontend/src/services/fleetHealthApi.ts` | Needs runtime verification |
| Fleet Health Risks API auth failure | Blocked by missing runtime proof | `frontend/src/services/fleetHealthApi.ts` | Needs runtime verification |
| Fleet Health tab text hidden under stats card | Not reproduced | `frontend/src/pages/FleetHealthPage.tsx` | No visible overlap in code |
| Create Work Order API failure | Not reproduced | `frontend/src/pages/FleetHealthPage.tsx`, backend route map | API route exists |
| Vehicle detail view non-functional | Blocked by missing runtime proof | `frontend/src/pages/VehiclesPage.tsx` | Needs browser validation |
| Driver detail view non-functional | Blocked by missing runtime proof | `frontend/src/pages/DriversModulePage.tsx` | Needs browser validation |
| Alert Center layout inconsistency | Partially built | `frontend/src/pages/AlertsCenterPage.tsx` | Could still be improved |
| Alert Center card UI needs improvement | Partially built | `frontend/src/pages/AlertsCenterPage.tsx` | Polishing opportunity |
| Hardcoded fake/demo data | Partially built | `frontend/src/data/developmentFleetSeedData.ts` | Demo data remains by design |
| Tenant isolation gaps | Needs tenant isolation check | `backend-dotnet/Controllers/EndpointMappings.cs`, `backend-dotnet/Services/*` | Requires deeper audit |
| API calls missing bearer token | Not reproduced | `frontend/src/services/apiClient.ts` | Bearer token is attached |
| Inconsistent API base URL usage | Not reproduced | `frontend/src/services/apiClient.ts` | Env logic is consistent |

