# Stage 14B Route / Navigation Verification

| Visible Nav/Page | Route | Component | API Dependencies | Build Reachable | Runtime Risk | Broken/Missing | Fix Applied | Final Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Dashboard | `/command-center` | `CommandCenterPage` | `commandCenterApi`, `safetyApi`, `maintenanceApi`, `fleetHealthApi` | Yes | Low | None | None needed | Working |
| Live Map | `/map-view` | `LiveMapPage` | `telemetryApi`, `controlTowerApi`, `apiClient` | Yes | Medium | None obvious | None | Working |
| Fleet | shell section | `AppShell` + `moduleConfig` | `fleetDomainApi`, `fleetHealthApi` | Yes | Medium | Shared section, not a single page | None | Working Foundation |
| Vehicles / assets | `/vehicles/*`, `/assets`, `/fleet-assets` | `VehiclesModulePage`, `EntityListPage`, `FleetAssetManagementPage` | `vehiclesApi`, `assetsApi`, `fleetDomainApi` | Yes | Medium | Some subviews are foundation-level | None | Working Foundation |
| Drivers | `/drivers/*` | `DriversModulePage` | `driversApi`, `fleetDomainApi` | Yes | Medium | None obvious | None | Working Foundation |
| Jobs | `/jobs` | `JobsPage` | `jobsApi` | Yes | Low | None obvious | None | Working |
| Trips | backend only | `tripApi` | `tripApi`, backend `/api/trips/*` | No dedicated visible page | Medium | No dedicated UI route | Documented as a gap | Missing |
| Dispatch | `/dispatch`, `/dispatch-legacy` | `DispatchCommandPage`, `DispatchPage` | `dispatchApi`, `jobsApi`, `tripApi` | Yes | Low | None obvious | None | Working |
| Assignment planning | `/assignments/*` | `FleetAssignmentsPage` | `fleetDomainApi`, `vehiclesApi`, `driversApi` | Yes | Medium | Foundation-level workflows | None | Working Foundation |
| Operational Proof Center | `/operations/proof-center` | `OperationsProofCenterPage` | `operationsProofApi`, `stage9` / proof APIs | Yes | Low | None obvious | None | Working Foundation |
| Site access / gate pass / NOC | within proof center | `OperationsProofCenterPage` | `operationsProofApi` | Yes | Medium | No dedicated standalone nav item | None | Working Foundation |
| 3P pickup / warehouse handover | within proof center | `OperationsProofCenterPage` | `operationsProofApi` | Yes | Medium | No standalone nav item | None | Working Foundation |
| Safety | `/safety`, `/incidents`, `/coaching`, `/dashcam` | `Batch4SafetyPage` and companions | `safetyApi`, `incidentsApi`, `dashcamApi` | Yes | Low | Live endpoints now used | Fixed live masking in incidents client | Working |
| Maintenance | `/maintenance`, `/work-orders`, `/preventive-maintenance` | `MaintenanceCommandPage`, `MaintenancePlanningPage` | `maintenanceApi`, `workOrdersApi` | Yes | Low | None obvious | None | Working |
| Fleet Health | `/fleet-health` | `FleetHealthPage` | `fleetHealthApi`, bridge cards | Yes | Low | None obvious | None | Working |
| Alert Center | `/alerts` | `AlertsCenterPage` | `alertsApi`, `apiClient` | Yes | Low | None obvious | None | Working |
| Finance | `/invoices`, `/payments`, `/profitability`, `/expenses`, `/fuel-idling` | `FinancialAnalyticsPage`, `Batch5FinancePage` | finance services | Yes | Medium | Mostly foundation/reporting | None | Working Foundation |
| Reports | `/reports`, `/analytics`, `/sla-kpi`, `/carbon-tracking` | reporting pages | `batch7Api`, reporting services | Yes | Low | None obvious | None | Working |
| Compliance | `/compliance`, `/hos-eld`, `/documents`, `/digital-forms` | compliance pages | `fleetDomainApi`, docs/compliance services | Yes | Medium | Foundation-heavy, but visible | None | Working Foundation |
| CRM / Sales | `/customers`, `/contracts`, `/rate-cards`, `/quotations`, `/leads`, `/opportunities` | CRM pages | customer / contract / quote services | Yes | Medium | Some pages remain foundation-level | None | Working Foundation |
| Customer Portal | `/customer-portal`, `/customer-eta`, `/customer-visibility` | `CustomerEtaPage`, `CustomerVisibilityPage` | customer portal services | Yes | Medium | Scoped, not full portal suite | None | Working Foundation |
| Tenant Admin | `/admin`, `/user-management` | `AdminPage` | `adminApi`, `useAdmin` | Yes | Low | Permissions catalog was previously faked; fixed now | Added live permissions endpoint | Working |
| Platform Admin | `/platform/*` | `PlatformApp`, platform pages | `platformApi`, platform auth | Yes | Low | Separate shell preserved | None | Working |
| Settings / feature flags | `/settings`, `/feature-flags` | `SettingsPage`, `FeatureFlagsPage` | settings services | Yes | Low | None obvious | None | Working Foundation |
| AI recommendations / action requests | `AiCopilotPage`, recommendation panels | `aiApi`, recommendation endpoints | Yes | Medium | Recommendation-only, no direct mutation | None | Working Foundation |

