# Stage 15A-2 Remaining Module Productization Matrix

| Module | Status | Evidence | Risk | Recommendation |
| --- | --- | --- | --- | --- |
| Customer Portal / Client Visibility | Mostly productized | `CustomerVisibilityPage.tsx`, `customerVisibilityApi`, live export from query rows. | Low | Keep as a live customer-safe surface; avoid reintroducing seeded fallback. |
| CRM / Sales / Quote-to-Contract | Mostly productized | `LeadsPage.tsx`, `OpportunitiesPage.tsx`, `CustomersPage.tsx`, `ContractsPage.tsx`, `QuotationsPage.tsx`, `RateCardsPage.tsx`. | Medium | Continue tightening source-of-truth exports and contract lifecycle messaging. |
| Compliance / Documents / Expiry / Permits / Insurance | Productized with a few error-state gaps | `CompliancePage.tsx`, `FleetCompliancePage.tsx`, `DriverScorecardsPage.tsx`, `documents` surfaces. | Low-Medium | Keep live backend dependency honest in error states. |
| Finance UI / Billing / Invoices / AR polish | Partially productized | `FinancialAnalyticsPage.tsx` had seed-based export helpers; fixed to export live rows. | Medium | Continue replacing any remaining seed-derived presentation logic. |
| Tenant Admin controls | Productized | `AdminPage.tsx`, `useAdmin*` hooks, RBAC gating. | Low | Maintain strict tenant scope and avoid platform bleed. |
| Platform Admin controls verification | Productized and separated | `PlatformApp.tsx`, `PlatformShell.tsx`, `platformApi.ts`. | Low | Keep the separate session/auth model. |
| Fleet / Assets / Vehicles polish | Mostly productized | `VehiclesModulePage.tsx`, `FleetAssetManagementPage.tsx`, `FleetCompliancePage.tsx`. | Low-Medium | Keep summary/detail surfaces honest and operational. |
| Drivers / Operators polish | Mostly productized | `DriversModulePage.tsx`, `DriverDashboardPage.tsx`, `driverApi`. | Low-Medium | Continue using live read models for readiness and records. |
| Assignment Planning polish | Productized | `FleetAssignmentsPage.tsx`, `DispatchCommandPage.tsx`, `dispatchApi`. | Low | Preserve live dispatch recommendations and exception views. |
| Reports / Analytics polish | Productized | `ReportsPage.tsx`, `AnalyticsDashboardPage.tsx`, `PredictiveAnalyticsPage.tsx`. | Low | Keep permission gating and failure states explicit. |

Overall assessment: the remaining gaps are mostly polish, export truthfulness, and failure-state handling rather than missing core modules.
