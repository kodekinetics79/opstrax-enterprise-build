# OpsTrax app-wide UX coverage ledger

> Status: source inventory complete; bounded Firefox first-view observations are recorded for the routes explicitly listed below. This document does not certify visual quality, behavior, accessibility, real data, or production rendering.

## Snapshot and method

- Review date: 2026-09-11.
- Repository HEAD at review start: `469209a8db329a5313cb66fdea66c1c394eb6021`; the active working tree includes the current UX standardization changes.
- Governing registries reviewed: `frontend/src/App.tsx`, `frontend/src/modules/moduleConfig.ts`, and `frontend/src/layouts/AppShell.tsx`.
- Coverage: all 92 tenant navigation modules in `moduleConfig`, plus protected aliases/detail routes and separate driver/customer/public surfaces below.
- “Source reviewed” means the route mapping and component JSX were inspected for top-level content order, explicit height/overflow, KPI density, panels, tables, drawers, warnings, and actions. It does not mean the route was rendered.
- Browser labels are deliberately narrow: **Desktop first view** means the named state was rendered in Firefox against the dirty local working tree; **Desktop + mobile first view** adds one 390×844 responsive rendering; **Desktop gate view** means the route rendered a permission, entitlement, or region gate rather than its operating workspace. These observations are not exact-SHA acceptance and do not cover every action, role, data state, or shared-component mode. All unobserved rows remain **Pending**.

## Risk legend

| Code | Priority | Source-level meaning |
|---|---:|---|
| H1 | High | More than six KPI/stat cards precede the primary records or work surface. |
| H2 | High | Route root owns `h-full overflow-y-auto`, creating a nested page scroll inside `AppShell`. |
| H3 | High | Fixed/minimum-height work regions or stretched columns can force empty height and bury adjacent work. |
| H4 | High | Optional warnings/insights can expand ahead of the primary work surface. |
| H5 | High | KPI/configuration/summary content precedes the primary table, queue, map, or action workspace. |
| M3 | Medium | Large bounded map/chart/console surface needs viewport testing for balance and usable information density. |
| M4 | Medium | Primary route is reasonable; dense full-height drawer/modal still needs responsive review. |
| M5 | Medium | One shared component serves several routes/tabs; each mode needs a separate browser pass. |
| C1 | Current-pass compact | Source has already been compacted in the current working branch; browser verification remains pending. |
| C2 | No obvious blocker | Static JSX scan found no clear first-viewport hierarchy blocker; this is not browser acceptance. |

## Tenant navigation modules

### Operations

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Dashboard (`command-center`) | `/command-center` | `CommandCenterPage`<br><sub>`frontend/src/pages/CommandCenterPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Fleet Health (`fleet-health`) | `/fleet-health` | `FleetHealthPage`<br><sub>`frontend/src/pages/FleetHealthPage.tsx`</sub> | Yes | Desktop first view | C2 |
| Fleet Overview (`live-dashboard`) | `/live-dashboard` | `FleetOverviewPage`<br><sub>`frontend/src/pages/FleetOverviewPage.tsx`</sub> | Yes | Desktop + mobile first view | C1 M3 |
| Fleet Position Map (`map-view`) | `/map-view` | `LiveMapPage`<br><sub>`frontend/src/pages/LiveMapPage.tsx`</sub> | Yes | Desktop first view | C1 M3 |
| Fleet Live Wall (`fleet-live-wall`) | `/fleet/live-wall` | `FleetLiveWallPage`<br><sub>`frontend/src/pages/FleetLiveWallPage.tsx`</sub> | Yes | Desktop first view | C2 |
| Alerts (`alerts`) | `/alerts` | `AlertsCenterPage`<br><sub>`frontend/src/pages/AlertsCenterPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Active Shipments (`active-shipments`) | `/active-shipments` | `ActiveShipmentsPage`<br><sub>`frontend/src/pages/ActiveShipmentsPage.tsx`</sub> | Yes | Desktop first view | C2 |
| Geofence Management (`geofences`) | `/geofences` | `GeofenceManagementPage`<br><sub>`frontend/src/pages/GeofenceManagementPage.tsx`</sub> | Yes | Desktop first view | M3 |

### CRM & Growth

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Leads (`leads`) | `/leads` | `LeadsPage`<br><sub>`frontend/src/pages/LeadsPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Sales Pipeline (`sales-pipeline`) | `/sales-pipeline` | `OpportunitiesPage`<br><sub>`frontend/src/pages/OpportunitiesPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Opportunities (`opportunities`) | `/opportunities` | `OpportunitiesPage`<br><sub>`frontend/src/pages/OpportunitiesPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Campaigns (`campaigns`) | `/campaigns` | `CampaignsPage`<br><sub>`frontend/src/pages/CampaignsPage.tsx`</sub> | Yes | Pending | C1 M4 |
| Account Health (`account-health`) | `/account-health` | `AccountHealthPage`<br><sub>`frontend/src/pages/AccountHealthPage.tsx`</sub> | Yes | Desktop first view | C2 M5 |
| Follow-ups (`follow-ups`) | `/follow-ups` | `AccountHealthPage`<br><sub>`frontend/src/pages/AccountHealthPage.tsx`</sub> | Yes | Desktop first view | C2 M5 |
| Support Tickets (`support-tickets`) | `/support-tickets` | `AccountHealthPage`<br><sub>`frontend/src/pages/AccountHealthPage.tsx`</sub> | Yes | Desktop first view | C2 M5 |
| Renewals (`renewals`) | `/renewals` | `AccountHealthPage`<br><sub>`frontend/src/pages/AccountHealthPage.tsx`</sub> | Yes | Desktop first view | C2 M5 |
| Upsell Opportunities (`upsell-opportunities`) | `/upsell-opportunities` | `AccountHealthPage`<br><sub>`frontend/src/pages/AccountHealthPage.tsx`</sub> | Yes | Desktop first view | C2 M5 |

### Commercial

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Customers (`customers`) | `/customers` | `CustomersPage`<br><sub>`frontend/src/pages/CustomersPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Contracts (`contracts`) | `/contracts` | `ContractsPage`<br><sub>`frontend/src/pages/ContractsPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Rate Cards (`rate-cards`) | `/rate-cards` | `RateCardsPage`<br><sub>`frontend/src/pages/RateCardsPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Price Simulation (`price-simulation`) | `/price-simulation` | `OperatingModulePage (moduleKey=price-simulation)`<br><sub>`frontend/src/pages/OperatingModulePage.tsx`</sub> | Yes | Desktop gate view | C2 |
| Quotations (`quotations`) | `/quotations` | `QuotationsPage`<br><sub>`frontend/src/pages/QuotationsPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Customer ETA (`customer-eta`) | `/customer-eta` | `CustomerEtaPage`<br><sub>`frontend/src/pages/CustomerEtaPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Customer Portal (`customer-portal`) | `/customer-portal` | `CustomerPortalPage`<br><sub>`frontend/src/pages/CustomerPortalPage.tsx`</sub> | Yes | Desktop gate view | C1 H5 |
| Customer Visibility (`customer-visibility`) | `/customer-visibility` | `CustomerVisibilityPage`<br><sub>`frontend/src/pages/CustomerVisibilityPage.tsx`</sub> | Yes | Desktop first view | C1 |

### Transport Operations

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Load Bookings (`load-bookings`) | `/load-bookings` | `JobsPage`<br><sub>`frontend/src/pages/JobsPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Shipments (`shipments`) | `/shipments` | `JobsPage`<br><sub>`frontend/src/pages/JobsPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Dispatch Board (`dispatch-board`) | `/dispatch` | `DispatchCommandPage`<br><sub>`frontend/src/pages/DispatchCommandPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Jobs (`jobs`) | `/jobs` | `JobsPage`<br><sub>`frontend/src/pages/JobsPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Trips (`trips`) | `/trips` | `TripsPage`<br><sub>`frontend/src/pages/TripsPage.tsx`</sub> | Yes | Desktop first view | H5 |
| Route Plans (`route-plans`) | `/route-plans` | `RoutePlanningPage`<br><sub>`frontend/src/pages/RoutePlanningPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Proof of Delivery (`proof-of-delivery`) | `/proof-of-delivery` | `ProofOfDeliveryPage`<br><sub>`frontend/src/pages/ProofOfDeliveryPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| Last Mile Delivery (`last-mile-delivery`) | `/last-mile-delivery` | `LastMileDeliveryPage`<br><sub>`frontend/src/pages/LastMileDeliveryPage.tsx`</sub> | Yes | Desktop first view | C2 |
| Operational Proof Center (`operations-proof-center`) | `/operations/proof-center` | `OperationsProofCenterPage`<br><sub>`frontend/src/pages/OperationsProofCenterPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Logistics Workspace (`logistics-workspace`) | `/logistics-workspace` | `DispatchWorkspacePage (mode=dispatch)`<br><sub>`frontend/src/pages/DispatchWorkspacePage.tsx`</sub> | Yes | Desktop first view | C1 |
| Driver Messaging (`driver-messaging`) | `/driver-messaging` → `/messages` | `MessageCenterPage`<br><sub>`frontend/src/pages/MessageCenterPage.tsx`</sub> | Yes | Desktop first view | C2; redirect destination is the active experience |
| Workforce Management (`workforce`) | `/workforce` | `WorkforceManagementPage`<br><sub>`frontend/src/pages/WorkforceManagementPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |

### Fleet

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Fleet Utilization (`fleet-utilization`) | `/fleet-utilization` | `FleetUtilizationPage`<br><sub>`frontend/src/pages/FleetUtilizationPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Fleet TMS Workspace (`fleet-workspace`) | `/fleet-workspace` | `FleetWorkspacePage (mode=command)`<br><sub>`frontend/src/pages/FleetWorkspacePage.tsx`</sub> | Yes | Desktop first view | C1 |
| Cold Chain Monitor (`fleet-cold-chain`) | `/fleet-cold-chain` | `FleetColdChainPage`<br><sub>`frontend/src/pages/FleetColdChainPage.tsx`</sub> | Yes | Desktop first view | C1 M3 |
| Returnable Assets (`fleet-assets`) | `/fleet-assets` | `FleetAssetManagementPage`<br><sub>`frontend/src/pages/FleetAssetManagementPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Branches (`branches`) | `/branches` | `BranchesPage`<br><sub>`frontend/src/pages/BranchesPage.tsx`</sub> | Yes | Desktop first view | C2 |
| Saudi Readiness (`fleet-saudi-readiness`) | `/fleet-saudi-readiness` | `FleetCompliancePage (initialTab=saudi)`<br><sub>`frontend/src/pages/FleetCompliancePage.tsx`</sub> | Yes | Desktop gate view | H5 M5 |
| Fleet Compliance (`fleet-compliance`) | `/fleet-compliance` | `FleetCompliancePage`<br><sub>`frontend/src/pages/FleetCompliancePage.tsx`</sub> | Yes | Desktop gate view | H5 M5 |
| Vehicles (`vehicles`) | `/vehicles` → `/vehicles/overview` | `VehiclesModulePage` via `/vehicles/*`; `VehiclesPage` is embedded only in the roster tab<br><sub>`frontend/src/pages/VehiclesModulePage.tsx`</sub> | Yes | Desktop first view | C1 H3 M5 |
| Drivers (`drivers`) | `/drivers` → `/drivers/overview` | `DriversModulePage` via `/drivers/*`<br><sub>`frontend/src/pages/DriversModulePage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Owners (`owners`) | `/owners` → `/assignments/owners` | `FleetAssignmentsPage` via `/assignments/*`<br><sub>`frontend/src/pages/FleetAssignmentsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Assignments (`assignments`) | `/assignments` → `/assignments/overview` | `FleetAssignmentsPage` via `/assignments/*`<br><sub>`frontend/src/pages/FleetAssignmentsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Documents (`documents`) | `/documents` | `Batch3OperationsPage (kind=documents)`<br><sub>`frontend/src/pages/Batch3OperationsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |

### Telematics & IoT

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Device Health (`iot-devices`) | `/iot-devices` | `IotDevicesPage`<br><sub>`frontend/src/pages/IotDevicesPage.tsx`</sub> | Yes | Desktop first view | H5 M4 |
| Telematics Control Tower (`telematics-control-tower`) | `/telematics-control-tower` | `TelematicsControlTowerPage`<br><sub>`frontend/src/pages/TelematicsControlTowerPage.tsx`</sub> | Yes | Desktop first view | H5 |
| GPS Tracking (`gps-tracking`) | `/gps-tracking` | `TelematicsCommandPage (kind=gps-tracking)`<br><sub>`frontend/src/pages/TelematicsCommandPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| OBD / J1939 (`obd-j1939`) | `/obd-j1939` | `TelematicsCommandPage (kind=obd-j1939)`<br><sub>`frontend/src/pages/TelematicsCommandPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Cold Chain (`cold-chain`) | `/cold-chain` | `TelematicsCommandPage (kind=cold-chain)`<br><sub>`frontend/src/pages/TelematicsCommandPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Sensor Health (`sensor-health`) | `/sensor-health` | `TelematicsCommandPage (kind=sensor-health)`<br><sub>`frontend/src/pages/TelematicsCommandPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |

### Safety & Compliance

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Camera Safety (`dashcam`) | `/dashcam` | `Batch4SafetyPage (kind=dashcam)`<br><sub>`frontend/src/pages/Batch4SafetyPage.tsx`</sub> | Yes | Desktop first view | C1 M4 M5 |
| Driver Scorecards (`driver-scorecards`) | `/driver-scorecards` | `DriverScorecardsPage`<br><sub>`frontend/src/pages/DriverScorecardsPage.tsx`</sub> | Yes | Desktop first view | C1 H5 |
| Safety Center (`safety-center`) | `/safety` | `Batch4SafetyPage (kind=safety)`<br><sub>`frontend/src/pages/Batch4SafetyPage.tsx`</sub> | Yes | Desktop first view | C1 M4 M5 |
| Incidents (`incidents`) | `/incidents` | `Batch4SafetyPage (kind=incidents)`<br><sub>`frontend/src/pages/Batch4SafetyPage.tsx`</sub> | Yes | Desktop first view | C1 M4 M5 |
| Driver Coaching (`coaching`) | `/coaching` | `Batch4SafetyPage (kind=coaching)`<br><sub>`frontend/src/pages/Batch4SafetyPage.tsx`</sub> | Yes | Desktop first view | C1 M4 M5 |
| DVIR (`dvir-inspections`) | `/dvir-inspections` | `DvirInspectionsPage`<br><sub>`frontend/src/pages/DvirInspectionsPage.tsx`</sub> | Yes | Desktop first view | C1 M4 |
| HOS / ELD (`hos-eld`) | `/hos-eld` | `HosEldPage`<br><sub>`frontend/src/pages/HosEldPage.tsx`</sub> | Yes | Desktop first view | C1 H5 |
| Traffic Violations (`traffic-violations`) | `/traffic-violations` | `TrafficViolationsPage`<br><sub>`frontend/src/pages/TrafficViolationsPage.tsx`</sub> | Yes | Desktop first view | H2 M4 |
| Evidence Packages (`evidence-packages`) | `/evidence-packages` | `Batch4SafetyPage (kind=evidence)`<br><sub>`frontend/src/pages/Batch4SafetyPage.tsx`</sub> | Yes | Desktop first view | C1 M4 M5 |
| Digital Forms (`digital-forms`) | `/digital-forms` | `DigitalFormsPage`<br><sub>`frontend/src/pages/DigitalFormsPage.tsx`</sub> | Yes | Desktop first view | H2 M4 |
| Compliance Center (`compliance-center`) | `/compliance` | `CompliancePage`<br><sub>`frontend/src/pages/CompliancePage.tsx`</sub> | Yes | Desktop first view | C1 H5 |

### Maintenance

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Work Orders (`work-orders`) | `/work-orders` | `MaintenanceCommandPage`<br><sub>`frontend/src/pages/MaintenanceCommandPage.tsx`</sub> | Yes | Pending | C1 M5 |
| Maintenance Center (`maintenance-center`) | `/maintenance` | `MaintenanceCommandPage`<br><sub>`frontend/src/pages/MaintenanceCommandPage.tsx`</sub> | Yes | Pending | C1 M5 |
| Service History (`service-history`) | `/service-history` | `MaintenancePlanningPage`<br><sub>`frontend/src/pages/MaintenancePlanningPage.tsx`</sub> | Yes | Desktop first view | H5 M5 |
| Downtime (`downtime`) | `/downtime` | `MaintenancePlanningPage`<br><sub>`frontend/src/pages/MaintenancePlanningPage.tsx`</sub> | Yes | Desktop first view | H5 M5 |
| Preventive Maintenance (`preventive-maintenance`) | `/preventive-maintenance` | `MaintenancePlanningPage`<br><sub>`frontend/src/pages/MaintenancePlanningPage.tsx`</sub> | Yes | Desktop first view | H5 M5 |

### Financials

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Fuel Transactions (`fuel-idling`) | `/fuel-idling` | `Batch5FinancePage (kind=fuel)`<br><sub>`frontend/src/pages/Batch5FinancePage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Expenses (`expenses`) | `/expenses` | `Batch5FinancePage (kind=expenses)`<br><sub>`frontend/src/pages/Batch5FinancePage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Invoices (`invoices`) | `/invoices` | `FinancialAnalyticsPage`<br><sub>`frontend/src/pages/FinancialAnalyticsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| AR Aging (`ar-aging`) | `/ar-aging` | `FinancialAnalyticsPage`<br><sub>`frontend/src/pages/FinancialAnalyticsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Payments (`payments`) | `/payments` | `FinancialAnalyticsPage`<br><sub>`frontend/src/pages/FinancialAnalyticsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Profitability (`profitability`) | `/profitability` | `FinancialAnalyticsPage`<br><sub>`frontend/src/pages/FinancialAnalyticsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Tax Configuration (`tax-config`) | `/finance/tax-config` | `TaxAdminPage`<br><sub>`frontend/src/pages/TaxAdminPage.tsx`</sub> | Yes | Desktop first view | C2 |
| Consolidate Invoices (`billing-consolidation`) | `/finance/billing` | `BillingConsolidationPage`<br><sub>`frontend/src/pages/BillingConsolidationPage.tsx`</sub> | Yes | Pending | C2 |
| Driver Pay (`driver-pay`) | `/finance/settlements` | `SettlementPage`<br><sub>`frontend/src/pages/SettlementPage.tsx`</sub> | Yes | Pending | C2 |
| Revenue Recognition (`revenue-recognition`) | `/finance/revenue-recognition` | `RevenueRecognitionPage`<br><sub>`frontend/src/pages/RevenueRecognitionPage.tsx`</sub> | Yes | Pending | C2 |

### Governance

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Users & Roles (`user-management`) | `/user-management` | `AdminPage`<br><sub>`frontend/src/pages/AdminPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |
| Audit Logs (`audit-logs`) | `/audit-logs` | `AuditLogsPage`<br><sub>`frontend/src/pages/AuditLogsPage.tsx`</sub> | Yes | Desktop first view | C1 |
| Integrations (`integrations`) | `/integrations` | `IntegrationsPage`<br><sub>`frontend/src/pages/IntegrationsPage.tsx`</sub> | Yes | Desktop first view | H2 M4 |
| Alert Rules (`alert-rules`) | `/alert-rules` | `AlertRulesPage`<br><sub>`frontend/src/pages/AlertRulesPage.tsx`</sub> | Yes | Desktop first view | H2 H5 |
| Feature Flags (`feature-flags`) | `/feature-flags` | `FeatureFlagsPage`<br><sub>`frontend/src/pages/FeatureFlagsPage.tsx`</sub> | Yes | Pending | H2 |
| About OpsTrax (`about`) | `/about` | `AboutPage`<br><sub>`frontend/src/pages/AboutPage.tsx`</sub> | Yes | Desktop first view | H2 |

### Intelligence

| Module | Route | Component and source | Source reviewed | Browser | Risks |
|---|---|---|---|---|---|
| Carbon Tracking (`carbon-tracking`) | `/carbon-tracking` | `CarbonTrackingPage`<br><sub>`frontend/src/pages/CarbonTrackingPage.tsx`</sub> | Yes | Desktop first view | C1 M3 |
| Operations Copilot (`ai-copilot`) | `/ai-copilot` | `AiCopilotPage`<br><sub>`frontend/src/pages/AiCopilotPage.tsx`</sub> | Yes | Desktop first view | C2 |
| Fleet Intelligence (`predictive-analytics`) | `/predictive-analytics` | `PredictiveAnalyticsPage`<br><sub>`frontend/src/pages/PredictiveAnalyticsPage.tsx`</sub> | Yes | Desktop first view | M3 |
| Control Tower (`control-tower`) | `/control-tower` | `ControlTowerPage`<br><sub>`frontend/src/pages/ControlTowerPage.tsx`</sub> | Yes | Desktop first view | C1 M3 |
| Reports & Analytics (`reports-analytics`) | `/reports-analytics` | `ReportsPage`<br><sub>`frontend/src/pages/ReportsPage.tsx`</sub> | Yes | Desktop first view | C1 M5 |

## Protected tenant routes outside the navigation-module ledger

These routes are aliases, detail routes, legacy/support surfaces, or administrative paths. They remain part of app-wide browser coverage even when no sidebar item points directly to them.

Route status matters for evidence: `/fleet-utilization`, `/vehicles`, `/drivers`, `/owners`, `/assignments`, and `/driver-messaging` are redirect entry points. Their destination route must be rendered; a screenshot of the redirect itself is not acceptance. `VehiclesPage.tsx` is active only as the embedded `/vehicles/roster` tab, not as a standalone `/vehicles` page. `DriverMessagingPage.tsx` remains in source but has no active route; the valid experience is `/messages`. `/contracts` renders the commercial `ContractsPage`, while `/contracts-rates` is the separate Batch5 finance mode. `/dispatch-legacy` is also still directly routed.

| Route | Component / behavior | Source reviewed | Browser | Risks / notes |
|---|---|---|---|---|
| `/fleet-utilization/*` | `FleetUtilizationPage`; `/fleet-utilization` redirects to `/fleet-utilization/overview`<br><sub>`frontend/src/pages/FleetUtilizationPage.tsx`</sub> | Yes | Desktop first view | C1 M5; Active wildcard family |
| `/vehicles/*` | `VehiclesModulePage`; roster tab embeds `VehiclesPage`<br><sub>`frontend/src/pages/VehiclesModulePage.tsx`</sub> | Yes | Desktop first view | C1 H3 M5; active family, roster has a desktop-only 460px minimum |
| `/drivers/*` | `DriversModulePage`<br><sub>`frontend/src/pages/DriversModulePage.tsx`</sub> | Yes | Desktop first view | C1 M5; Active wildcard family |
| `/assignments/*` | `FleetAssignmentsPage`<br><sub>`frontend/src/pages/FleetAssignmentsPage.tsx`</sub> | Yes | Desktop first view | C1 M5; Active wildcard family |
| `/vehicles/:id/live` | `VehicleLiveMonitorPage`<br><sub>`frontend/src/pages/VehicleLiveMonitorPage.tsx`</sub> | Yes | Pending | C2; Detail route |
| `/reports` | `ReportsPage`<br><sub>`frontend/src/pages/ReportsPage.tsx`</sub> | Yes | Pending | C1 M5; Alias |
| `/analytics` | `AnalyticsDashboardPage`<br><sub>`frontend/src/pages/AnalyticsDashboardPage.tsx`</sub> | Yes | Pending | C1 M5; KPI groups are separated by tabs, not one wall |
| `/executive` | `ExecutivePage`<br><sub>`frontend/src/pages/ExecutivePage.tsx`</sub> | Yes | Pending | H2; Direct protected route |
| `/fleet-intelligence` | `FleetIntelligencePage`<br><sub>`frontend/src/pages/FleetIntelligencePage.tsx`</sub> | Yes | Pending | C1; Direct protected route distinct from `/predictive-analytics` |
| `/assets` | `EntityListPage (kind=assets)`<br><sub>`frontend/src/pages/EntityListPage.tsx`</sub> | Yes | Pending | C1 M5; Legacy entity registry remains directly routed |
| `/dispatch-legacy` | `DispatchPage`<br><sub>`frontend/src/pages/DispatchPage.tsx`</sub> | Yes | Pending | C1; Current-pass compact legacy board |
| `/routes` | `RoutePlanningPage`<br><sub>`frontend/src/pages/RoutePlanningPage.tsx`</sub> | Yes | Pending | C1 M4; Alias |
| `/route-planning` | `RoutePlanningPage`<br><sub>`frontend/src/pages/RoutePlanningPage.tsx`</sub> | Yes | Pending | C1 M4; Alias |
| `/inspections` | `MaintenanceCommandPage`<br><sub>`frontend/src/pages/MaintenanceCommandPage.tsx`</sub> | Yes | Pending | C1 M5; Alias |
| `/ai-dashcam` | `Batch4SafetyPage (kind=dashcam)`<br><sub>`frontend/src/pages/Batch4SafetyPage.tsx`</sub> | Yes | Pending | C1 M4 M5; Alias |
| `/detention` | `DetentionPage`<br><sub>`frontend/src/pages/DetentionPage.tsx`</sub> | Yes | Pending | C2; Protected finance route |
| `/contracts` | `ContractsPage`<br><sub>`frontend/src/pages/ContractsPage.tsx`</sub> | Yes | Desktop first view | C1 M4; direct commercial route |
| `/contracts-rates` | `Batch5FinancePage (kind=contracts)`<br><sub>`frontend/src/pages/Batch5FinancePage.tsx`</sub> | Yes | Desktop first view | C1 M5; separate shared finance mode |
| `/carrier-management` | `Batch5FinancePage (kind=carriers)`<br><sub>`frontend/src/pages/Batch5FinancePage.tsx`</sub> | Yes | Pending | C1 M5; Shared finance mode |
| `/predictive-margin` | `Batch5FinancePage (kind=cost-margin)`<br><sub>`frontend/src/pages/Batch5FinancePage.tsx`</sub> | Yes | Pending | C1 M5; Shared finance mode |
| `/cost-leakage` | `Batch5FinancePage (kind=cost-leakage)`<br><sub>`frontend/src/pages/Batch5FinancePage.tsx`</sub> | Yes | Pending | C1 M5; Shared finance mode |
| `/sla-kpi` | `SlaKpiPage`<br><sub>`frontend/src/pages/SlaKpiPage.tsx`</sub> | Yes | Pending | C1; Direct protected route |
| `/notifications` | `NotificationCenterPage`<br><sub>`frontend/src/pages/NotificationCenterPage.tsx`</sub> | Yes | Pending | C1; Protected support route |
| `/messages` | `MessageCenterPage`<br><sub>`frontend/src/pages/MessageCenterPage.tsx`</sub> | Yes | Desktop first view | C2; Driver-messaging redirect target |
| `/settings` | `SettingsPage`<br><sub>`frontend/src/pages/SettingsPage.tsx`</sub> | Yes | Pending | C1; Authenticated settings route |
| `/admin` | `AdminPage`<br><sub>`frontend/src/pages/AdminPage.tsx`</sub> | Yes | Pending | C1 H5 M5; Governance alias |
| `/ops` | `PlatformOpsPage`<br><sub>`frontend/src/pages/PlatformOpsPage.tsx`</sub> | Yes | Pending | H2; Tenant-shell operations admin |
| `/platform/operations` | `PlatformOpsPage`<br><sub>`frontend/src/pages/PlatformOpsPage.tsx`</sub> | Yes | Pending | H2; Tenant-shell alias despite platform prefix |

## Separate-shell and public-route coverage

These routes are outside the internal tenant `AppShell`, but they are included so “whole app” does not silently omit a customer, driver, authentication, or public journey.

| Surface | Routes | Component(s) | Source reviewed | Browser |
|---|---|---|---|---|
| Driver portal | `/driver`, `/driver/assignments`, `/driver/dvir`, `/driver/coaching`, `/driver/hos`, `/driver/earnings`, `/driver/messages`, `/driver/notifications` | `frontend/src/pages/driver/*` | Registry and route shell reviewed | Pending |
| Customer portal | `/customer-portal` | `CustomerPortalPage` inside `CustomerLayout` | Yes | Desktop gate view |
| Authentication | `/login`, `/sso/callback`, `/forgot-password`, `/reset-password` | `LoginPage`, `SsoCallbackPage`, `ResetPasswordPage` | Registry reviewed | Pending |
| Public tracking/evidence | `/eta/:trackingCode`, `/track/:token`, `/evidence/:token` | `CustomerEtaPage`, `PublicShipmentTrackingPage`, `DetentionEvidencePage` | Registry reviewed | Pending |
| Platform control plane | `/platform/*` | `PlatformApp` and platform child routes | Registry and source family reviewed | Pending |

## Ranked source findings

1. **The route-root scroll removals follow the shell contract.** `AppShell` owns the vertical page scroller, and the changed tenant routes now flow through it while bounded tables, maps, dialogs, and drawers retain their own overflow. `CustomerPortalPage` also remains scrollable through the natural document flow provided by `CustomerLayout`. The `TimelineList` change at `IotDevicesPage.tsx:3986` is an internal list inside the already-scrollable device drawer, not a route-root change; removing its unconstrained nested scroller does not remove access to the timeline in source.
2. **One compact-summary inconsistency needs correction before browser review.** `OperatingModulePage.tsx:718-723` changes the Alerts summary container from a grid to a compact flex rail but leaves all four `KpiCard` instances in full-card mode. Unlike the generic operating-module path, those cards have no compact sizing in the rail and can render unevenly or squeeze content. Add `compact` to the four alert KPI cards or restore a grid.
3. **A smaller set of active tenant routes still owns nested page scrolling.** Remaining route roots include `Batch3OperationsPage.tsx:297`, `TrafficViolationsPage.tsx:117`, `DigitalFormsPage.tsx:237`, `IntegrationsPage.tsx:1809`, `AlertRulesPage.tsx:72`, `FeatureFlagsPage.tsx:53`, `AboutPage.tsx:98`, `ExecutivePage.tsx:95`, and `PlatformOpsPage.tsx:438`. `ModulePage.tsx:95` also retains route-level overflow if any navigation module falls through to the generic renderer. These should be reviewed separately from valid drawer/modal scrollers.
4. **The active vehicle roster still has an artificial desktop height.** `/vehicles` redirects to `VehiclesModulePage`; only its `/vehicles/roster` tab embeds `VehiclesPage` (`VehiclesModulePage.tsx:22`). That embedded roster uses `VehiclesPage.tsx:430` with a 460px minimum. Test empty and one-row states before deciding whether the minimum is needed.
5. **The current collapsed sections retain primary risk values in source.** Control Tower keeps status/action/alert counts visible; Customer Visibility keeps the exception count visible; safety modes keep critical/provider states in the compact rail; invoice outstanding balances remain visible per currency; and AR 90+ exposure remains in the visible summary. The collapsed sections contain supporting insight, duplicated previews, charts, or calculation detail. This is a static hierarchy review, not proof that API responses populate correctly.
6. **Mobile control intent remains intact in the reviewed diffs.** Vehicle and driver workspace controls use `min-h-11` below the small breakpoint and compact to `sm:min-h-9`; drawers remain full-width and full-height on narrow screens. Full-height drawers are valid, although their desktop inner density still needs rendering review. Examples include `RoutePlanningPage.tsx:139`, `Batch4SafetyPage.tsx:1016-1068`, `AuditLogsPage.tsx:676`, `EntityListPage.tsx:834`, and `IntegrationsPage.tsx:556,1041`.
7. **Shared multi-route components multiply verification scope.** `AccountHealthPage`, `JobsPage`, `MaintenancePlanningPage`, `Batch4SafetyPage`, `Batch5FinancePage`, `TelematicsCommandPage`, `FinancialAnalyticsPage`, and `OperatingModulePage` each serve multiple routes or modes. The analytics KPI groups are separated by tabs, so their aggregate card count is not one wall; each tab still needs its own browser pass.

## Bounded Firefox observations — dirty local working tree

These are bounded first-view observations from the current Firefox session. They are useful layout evidence, but they are not certification evidence: no deployed SHA was captured, and most mutations, alternative roles, permission combinations, responsive widths, loading/error states, and large-data extremes remain unexercised.

| View / state | Observed behavior | Remaining limitation |
|---|---|---|
| `/command-center` | A 20-vehicle / 29-job state rendered a compact toolbar and five-metric strip, with the exception queue and priority actions in the first desktop viewport. | Actions and alternate data volumes were not exercised. |
| `/live-dashboard` | The 20-row fleet registry began in the desktop first view with natural page height. At 390×844, the page had no horizontal overflow; the status strip scrolled horizontally and the roster table retained its own readable horizontal scroll. | This was one dataset and one mobile size; row actions and empty/error states remain unverified. |
| `/customers` | Ten customer rows were visible in the first desktop viewport with a single filter row. | Drawer actions, long values, permissions, and mutations remain unverified. |
| `/customer-eta` | Forty job records rendered with formatted dates and visible primary actions in the desktop view. | Tracking actions and public-recipient transitions were not completed. |
| `/dispatch` | With the MERIDIAN filter applied, two rows were visible; clearing it showed five, while the primary board remained in the first viewport. | This confirms one filter transition only, not dispatch mutations or large-volume behavior. |
| `/proof-of-delivery` | A 40-record, full-width table rendered with the Capture action visible. | Capture, upload, permission, and evidence persistence were not exercised. |
| `/logistics-workspace` | Compact summaries kept the empty primary queue visible. Opening **Create order** revealed the light-surface form and its fields above the fold. | Submission, validation failures, and persisted creation remain unverified. |
| `/fleet-utilization` | The compact overview exposed the primary empty action queue without oversized navigation and KPI blocks burying it. | Non-empty records, subordinate tabs, and actions remain unverified. |
| `/fleet-assets` | Inventory labels and controls were readable on the light surface, and the inventory region used natural height. | Registration/update mutations and dense inventory states remain unverified. |
| `/fleet-cold-chain` | Zone labels were readable; sensor operations and the required registration action appeared earlier in the desktop flow. | Real-device readings, alarm transitions, and registration persistence remain unverified. |
| `/fleet-saudi-readiness` | The route rendered the expected **region not enabled** gate. | The enabled-region operating workspace was not viewed. |
| `/documents` | Four compact KPIs preceded the primary ten-row document table, which appeared early in the desktop view. | Other document modes, uploads, and long metadata remain unverified. |
| `/user-management` | Eleven users rendered with a compact six-item status rail and one filter row. | Role variants, drawers, access mutations, and narrow layouts remain unverified. |
| `/predictive-analytics` | The primary risk queue appeared before optional chart detail; the charts were collapsed. | Chart expansion, non-empty chart series, and responsive behavior remain unverified. |
| `/carbon-tracking` | Five compact KPIs and the truthful one-month trend note rendered without a large empty chart region. | Multi-month chart series, target mutations, and responsive behavior remain unverified. |
| `/control-tower` · Dispatch | The dispatch table appeared in the first viewport below the compact status strip and workspace tabs. | No row action or persisted transition was exercised. |
| `/control-tower?view=Telemetry%20Alerts` | The selected view remained encoded in the URL; 12 alert cards rendered in three desktop columns. | Narrow-layout wrapping, acknowledgement, and resolution mutations remain unverified. |
| `/control-tower?view=Fleet%20Map` | The map stayed within its bounded region and showed the explicit no-location empty state. | The FleetManager session received a `403` from the telemetry stream. Live map behavior was not accepted. |

### Additional OPX-DEMO desktop first views

The following grouped observations record the visible state and count only. Unless a transition is named, they confirm route rendering and first-view hierarchy, not workflow behavior.

| Routes | Observed OPX-DEMO state |
|---|---|
| `/fleet-health`, `/map-view`, `/fleet/live-wall`, `/alerts`, `/active-shipments`, `/geofences` | Fleet Health showed truthful incomplete-evidence messaging; the map showed 0 positions in a bounded region; Live Wall showed 0; Alerts showed 9; Active Shipments showed 40; Geofences showed 6. |
| `/leads`, `/opportunities`, `/sales-pipeline`, `/quotations` | All four loaded after the payload repair with honest empty states and no visible error. The Opportunities/Sales Pipeline chart detail was collapsed below the primary table. |
| `/account-health`, `/follow-ups`, `/support-tickets`, `/renewals`, `/upsell-opportunities` | Account Health showed 10 unrated accounts; Follow-ups, Support Tickets, and Upsell Opportunities showed unavailable states; Renewals showed an empty state. |
| `/contracts`, `/contracts-rates`, `/rate-cards`, `/price-simulation`, `/customer-visibility`, `/customer-portal` | Commercial Contracts showed 0 records with six compact summaries, one filter row, and empty-state feedback; Contracts & Rates was separately rendered; Rate Cards was empty; Price Simulation showed its provider gate; Customer Visibility showed 0; Customer Portal returned the Company Admin a `403` and displayed gated values as **Unavailable**, rather than zero. |
| `/load-bookings`, `/shipments`, `/jobs`, `/trips`, `/route-plans`, `/last-mile-delivery`, `/operations/proof-center`, `/messages`, `/workforce` | Load Bookings, Shipments, and Jobs each showed 40 records; Trips showed 14; Route Plans showed an empty state; Last Mile showed 0; Proof Center showed its job input with no job loaded; Messages was empty; Workforce showed 20 workers and 6 available. |
| `/fleet-workspace`, `/branches`, `/fleet-compliance`, `/vehicles/overview`, `/drivers/overview`, `/assignments/owners`, `/assignments/overview` | Fleet Workspace and Branches showed 0; Fleet Compliance showed the region-not-enabled gate; the small MERIDIAN vehicle state showed 5; Drivers showed 20 on Overview; Owners showed 0; Assignments showed a 20-record history state. |
| `/iot-devices`, `/telematics-control-tower`, `/gps-tracking`, `/obd-j1939`, `/cold-chain`, `/sensor-health` | Device Health and the Telematics Control Tower showed 0-device / 0-queue states. All four protocol/sensor command routes rendered compact empty states. |
| `/dashcam`, `/safety`, `/incidents`, `/coaching`, `/evidence-packages`, `/driver-scorecards`, `/dvir-inspections`, `/hos-eld`, `/traffic-violations`, `/compliance`, `/digital-forms` | Dashcam showed awaiting-provider status and an empty manual lane; the four shared safety modes were empty; Driver Scorecards showed 20 drivers with no qualified scores; DVIR showed 0; HOS/ELD showed 0 clocks; Traffic Violations showed 17 rows, including raw ISO dates and an open minor defect; Compliance and Digital Forms showed confirmed empty-state feedback. |
| `/service-history`, `/downtime`, `/preventive-maintenance` | Service History and Downtime showed 0; Preventive Maintenance showed 15 items, including 8 due soon. |
| `/fuel-idling`, `/expenses`, `/invoices`, `/ar-aging`, `/payments`, `/profitability`, `/finance/tax-config` | Fuel showed 15 unverified records; Expenses showed 10 pending records; the four analytics routes showed 0; Tax Configuration showed empty profile/rule states. Finance repair work remained open, so none of these views was accepted. |
| `/audit-logs`, `/integrations`, `/alert-rules`, `/about` | Audit Logs showed 24 records; Integrations showed 6 available and 31 evaluation-only connectors; Alert Rules showed 0; About rendered deployment identity. Feature Flags remained repair-blocked and pending. |
| `/reports-analytics`, `/ai-copilot` | Reports exposed its dataset selector; Operations Copilot exposed its composer, but no query was submitted. |

Campaigns and Feature Flags remain **Pending** because their repair work was still active during this observation pass. The unvisited billing-consolidation, settlement, and revenue-recognition routes also remain pending despite other finance pages rendering.

## Browser evidence still required

For every row above, capture the route, role/permission, entitlement and region state, viewport, dataset/provenance state, and exact deployed SHA. The minimum UX pass must verify: first meaningful work in the initial viewport; no duplicate page/header hierarchy; no nested page scroll; stable table and side-panel geometry; 44px touch targets on narrow screens; keyboard focus/order; empty/loading/error/large-data states; and transitions to every primary action and linked module.
