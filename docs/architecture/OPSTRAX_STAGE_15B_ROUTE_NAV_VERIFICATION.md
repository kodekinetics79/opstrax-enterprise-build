# Stage 15B Route / Navigation Verification

| Route/Nav Item | Component | Source File | Build Reachable | API Dependencies | Risk | Final Status |
|---|---|---|---|---|---|---|
| `/command-center` Dashboard | `CommandCenterPage` | `frontend/src/pages/CommandCenterPage.tsx` | Yes | `commandCenterApi`, `safetyApi`, `maintenanceApi`, `fleetHealthApi` | Low | Working |
| `/trips` | `TripsPage` | `frontend/src/pages/TripsPage.tsx` | Yes | `tripApi` | Low | Working |
| Jobs | `JobsPage` | `frontend/src/App.tsx` / `frontend/src/pages/JobsPage.tsx` | Yes | `jobsApi` | Low | Working |
| Dispatch | `DispatchPage` / `DispatchCommandPage` | `frontend/src/App.tsx` | Yes | `dispatchApi` | Low | Working |
| Live Map | `LiveMapPage` | `frontend/src/pages/LiveMapPage.tsx` | Yes | `telemetryApi`, map services | Low | Working |
| Proof Center | `OperationsProofCenterPage` | `frontend/src/pages/OperationsProofCenterPage.tsx` | Yes | `operationsProofApi` | Low | Working |
| Fleet / Vehicles | `VehiclesModulePage` | `frontend/src/App.tsx` | Yes | `fleetDomainApi`, vehicle services | Low | Working |
| Drivers | `DriversModulePage` | `frontend/src/App.tsx` | Yes | `driverApi` | Low | Working |
| Safety | `Batch4SafetyPage` / safety pages | `frontend/src/App.tsx` | Yes | `safetyApi` | Low | Working |
| Maintenance | `MaintenanceCommandPage` / maintenance pages | `frontend/src/App.tsx` | Yes | `maintenanceApi` | Low | Working |
| Fleet Health | `FleetHealthPage` | `frontend/src/pages/FleetHealthPage.tsx` | Yes | `fleetHealthApi` | Low | Working |
| Alerts | `AlertsCenterPage` | `frontend/src/App.tsx` | Yes | `alertsApi` | Low | Working |
| Finance | `Batch5FinancePage` / `FinancialAnalyticsPage` | `frontend/src/App.tsx`, `frontend/src/pages/FinancialAnalyticsPage.tsx` | Yes | finance APIs | Low | Working |
| Reports | `ReportsPage` / analytics pages | `frontend/src/App.tsx` | Yes | reporting APIs | Low | Working |
| Compliance | `CompliancePage` | `frontend/src/App.tsx` | Yes | compliance APIs | Low | Working |
| CRM / Sales | `LeadsPage`, `OpportunitiesPage`, `CustomersPage` | `frontend/src/App.tsx` | Yes | CRM APIs | Low | Working |
| Customer Portal | `CustomerVisibilityPage` | `frontend/src/App.tsx` | Yes | customer visibility APIs | Low | Working |
| Tenant Admin | `AdminPage` | `frontend/src/App.tsx` | Yes | `adminApi` | Low | Working |
| Platform Admin | `PlatformApp` / `PlatformCommandCenterPage` | `frontend/src/pages/platform/*` | Yes | platform APIs | Low | Working |
| Settings / Feature Flags | `SettingsPage`, `FeatureFlagsPage` | `frontend/src/App.tsx` | Yes | settings/feature flag APIs | Low | Working |
| AI recommendations/actions | `AiCopilotPage` and ops recommendation surfaces | `frontend/src/App.tsx`, `frontend/src/pages/AiCopilotPage.tsx` | Yes | AI recommendation-only services | Medium | Working |

