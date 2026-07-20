# Stage 15A-2 Source and API Findings

| Area | Finding | Evidence | Risk | Decision |
| --- | --- | --- | --- | --- |
| Shared live data layer | `withFallback()` is intentionally inert, so runtime does not silently substitute seed rows. | `frontend/src/services/fleetDomainApi.ts` | Low | Keep this pattern; it prevents fake success. |
| Customer visibility | Uses live API reads and exports current query rows. | `CustomerVisibilityPage.tsx`, `customerVisibilityApi` | Low | No change needed. |
| CRM / sales | Leads and opportunities pages call live endpoints and have explicit error states. | `LeadsPage.tsx`, `OpportunitiesPage.tsx`, `CustomersPage.tsx` | Low-Medium | Keep seed builders out of runtime paths. |
| Finance UI | Export paths were still using seed helpers instead of live data. | `FinancialAnalyticsPage.tsx` | Medium | Fixed export helpers to call live APIs. |
| Feature flags | Optimistic toggle state could drift if the backend rejected the write. | `FeatureFlagsPage.tsx` | Medium | Added rollback and error state handling. |
| Tenant admin | Tenant-admin controls are permissioned and remain inside the tenant shell. | `AdminPage.tsx`, `useHasPermission`, `adminApi.ts` | Low | Keep platform admin separate. |
| Platform admin | Platform controls use a separate session and shell. | `PlatformApp.tsx`, `PlatformShell.tsx`, `platformApi.ts` | Low | Preserve the split auth model. |
| Fleet / drivers / assignments | Live data and scoped row filtering are already in place. | `FleetAssignmentsPage.tsx`, `DriversModulePage.tsx`, `VehiclesModulePage.tsx`, `fleetDomainApi.ts` | Low | Keep these as live operational surfaces. |
| Reports / analytics | Query-driven reporting surfaces already gate access and show load states. | `ReportsPage.tsx`, `AnalyticsDashboardPage.tsx` | Low | Ensure error states stay explicit. |
| Backend RBAC / tenant scope | Backend route mapping still enforces permission checks and tenant-scoped data reads. | `Program.cs`, `EndpointMappings.cs` | Low | No scope expansion required for this stage. |

Decision: Stage 15A-2 can proceed as a productization pass rather than a foundation rewrite.
