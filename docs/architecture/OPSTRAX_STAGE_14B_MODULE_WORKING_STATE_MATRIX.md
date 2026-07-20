# Stage 14B Module Working-State Matrix

| Module | Backend | Frontend | API Client | RBAC | Data Truth | UX | Tests | Demo Ready? | Status | Evidence | Next Fix |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| App shell / navigation / layout | Working | Working | Working | Working | Working | Working | Source regression exists | Yes | Working | `AppShell`, `PlatformShell`, route tree | Keep compatibility and labeling stable |
| Auth / session | Working | Working | Working | Working | Working | Working | Backend auth tests exist | Yes | Working | `apiClient`, login/session flow, backend auth middleware | Monitor tenant headers and login guards |
| Dashboard | Working | Working | Working | Working | Working | Working | Covered by source regression | Yes | Working | `/command-center`, `CommandCenterPage` | None |
| Fleet / assets / vehicles | Working | Working Foundation | Working | Working | Working | Working Foundation | Partial | Mostly | Working Foundation | `vehiclesApi`, `assetsApi`, fleet workspace pages | Keep tightening detail views |
| Drivers / operators | Working | Working Foundation | Working | Working | Working | Working Foundation | Partial | Mostly | Working Foundation | `driversApi`, driver modules | Complete more operator flows later |
| Jobs / trips / dispatch | Working | Partial | Working | Working | Working | Working Foundation | Partial | Mostly | Working Foundation | `jobsApi`, `tripApi`, `dispatch` pages | Add a dedicated trips surface if needed |
| Assignment planning | Working | Working Foundation | Working | Working | Working | Working Foundation | Partial | Mostly | Working Foundation | `FleetAssignmentsPage`, dispatch links | Tighten route and assignment polish |
| Live Map / telemetry | Working | Working | Working | Working | Working | Working | Partial | Yes | Working | `LiveMapPage`, `telemetryApi`, `controlTowerApi` | None |
| POD / proof center | Working Foundation | Working Foundation | Working | Working | Working Foundation | Working Foundation | Stage 9/10 tests exist | Yes | Working Foundation | `OperationsProofCenterPage`, `operationsProofApi` | Continue hardening workflow summaries |
| Site access / gate pass / NOC | Working Foundation | Working Foundation | Working Foundation | Working | Working Foundation | Working Foundation | Stage 9 tests exist | Yes | Working Foundation | proof center workflow | Add dedicated page only if product needs it |
| 3P pickup / warehouse handover | Working Foundation | Working Foundation | Working Foundation | Working | Working Foundation | Working Foundation | Stage 9 tests exist | Yes | Working Foundation | proof center workflow | Add dedicated page only if product needs it |
| Safety Center | Working | Working | Working | Working | Working | Working | Updated source regression | Yes | Working | `safetyApi`, `incidentsApi`, `Batch4SafetyPage` | Keep live-only client behavior |
| Maintenance Center | Working | Working | Working | Working | Working | Working | Backend/frontend tests exist | Yes | Working | `maintenanceApi`, `MaintenanceCommandPage` | None |
| Fleet Health | Working | Working | Working | Working | Working | Working | Source regression exists | Yes | Working | `FleetHealthPage`, `fleetHealthApi` | None |
| Alert Center | Working | Working | Working | Working | Working | Working | Partial | Yes | Working | `AlertsCenterPage`, alerts APIs | None |
| Finance / invoices / AR | Working Foundation | Working Foundation | Working Foundation | Working | Working Foundation | Working Foundation | Stage 8/7 tests exist | Mostly | Working Foundation | finance pages and read models | Continue finance hardening later |
| Platform Admin | Working | Working | Working | Working | Working | Working | Platform tests exist | Yes | Working | `/platform/*`, `PlatformApp` | None |
| Tenant Admin | Working | Working Foundation | Working | Working | Working | Working Foundation | Updated source regression | Yes | Working Foundation | `AdminPage`, `adminApi`, permissions endpoint | Add more live admin journeys later |
| Customer Portal | Working | Working Foundation | Working | Working | Working | Working Foundation | Partial | Mostly | Working Foundation | customer portal pages and APIs | Keep role scoping honest |
| CRM / sales / quote-to-contract | Working Foundation | Working Foundation | Working Foundation | Working | Working Foundation | Working Foundation | Partial | Mostly | Working Foundation | customers/contracts/quotes/leads pages | Add deeper workflow automation later |
| Compliance / documents / expiry | Working | Working Foundation | Working | Working | Working | Working Foundation | Partial | Mostly | Working Foundation | compliance/documents pages | Continue hardening document flows |
| Reports / analytics | Working | Working | Working | Working | Working | Working | Tests exist | Yes | Working | reporting pages and services | None |
| AI recommendations / action requests | Working Foundation | Working Foundation | Working Foundation | Working | Working Foundation | Working Foundation | Stage 9/10 tests exist | Yes | Working Foundation | AI surfaces are recommendation-only | Keep mutation blocked |
| Settings / feature flags | Working | Working Foundation | Working | Working | Working | Working Foundation | Partial | Mostly | Working Foundation | settings / feature flags pages | Continue admin polish |

