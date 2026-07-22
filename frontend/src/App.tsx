import { lazy, Suspense, type ReactNode } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/layouts/AppShell";
import { useAuth } from "@/hooks/useAuth";
import { FeatureFlagsProvider, useFlag } from "@/hooks/useFeatureFlags";
import { RequirePermission } from "@/hooks/usePermission";
import { GCC_COUNTRIES, RequireRegion } from "@/hooks/useTenantRegion";
import { getLandingRouteForSession } from "@/auth/sessionRouting";
import { modules } from "@/modules/moduleConfig";
import { LoginPage } from "@/pages/LoginPage";
import { SsoCallbackPage } from "@/pages/SsoCallbackPage";
import { ResetPasswordPage } from "@/pages/ResetPasswordPage";
import { LoadingState } from "@/components/ui";

const operatingRoutes = [
  "live-dashboard",
  "active-shipments",
  "map-view",
  "leads",
  "sales-pipeline",
  "opportunities",
  "campaigns",
  "account-health",
  "follow-ups",
  "support-tickets",
  "renewals",
  "upsell-opportunities",
  "customers",
  "contracts",
  "rate-cards",
  "price-simulation",
  "quotations",
  "load-bookings",
  "shipments",
  "trips",
  "dispatch-board",
  "route-plans",
  "proof-of-delivery",
  "last-mile-delivery",
] as const;

const AiCopilotPage = lazy(() => import("@/pages/AiCopilotPage").then((module) => ({ default: module.AiCopilotPage })));
const AlertsCenterPage = lazy(() => import("@/pages/AlertsCenterPage").then((module) => ({ default: module.AlertsCenterPage })));
const DispatchPage = lazy(() => import("@/pages/DispatchPage").then((module) => ({ default: module.DispatchPage })));
const DispatchCommandPage = lazy(() => import("@/pages/DispatchCommandPage").then((module) => ({ default: module.DispatchCommandPage })));
const ProofOfDeliveryPage = lazy(() => import("@/pages/ProofOfDeliveryPage").then((module) => ({ default: module.ProofOfDeliveryPage })));
const CustomerPortalPage = lazy(() => import("@/pages/CustomerPortalPage").then((module) => ({ default: module.CustomerPortalPage })));
const DriverScorecardsPage = lazy(() => import("@/pages/DriverScorecardsPage").then((module) => ({ default: module.DriverScorecardsPage })));
const LastMileDeliveryPage = lazy(() => import("@/pages/LastMileDeliveryPage").then((module) => ({ default: module.LastMileDeliveryPage })));
const OperationsProofCenterPage = lazy(() => import("@/pages/OperationsProofCenterPage").then((module) => ({ default: module.OperationsProofCenterPage })));
const GeofenceManagementPage = lazy(() => import("@/pages/GeofenceManagementPage").then((module) => ({ default: module.GeofenceManagementPage })));
const FleetUtilizationPage = lazy(() => import("@/pages/FleetUtilizationPage").then((module) => ({ default: module.FleetUtilizationPage })));
const CustomersPage = lazy(() => import("@/pages/CustomersPage").then((module) => ({ default: module.CustomersPage })));
const ContractsPage = lazy(() => import("@/pages/ContractsPage").then((module) => ({ default: module.ContractsPage })));
const LeadsPage = lazy(() => import("@/pages/LeadsPage").then((module) => ({ default: module.LeadsPage })));
const OpportunitiesPage = lazy(() => import("@/pages/OpportunitiesPage").then((module) => ({ default: module.OpportunitiesPage })));
const CampaignsPage = lazy(() => import("@/pages/CampaignsPage").then((module) => ({ default: module.CampaignsPage })));
const QuotationsPage = lazy(() => import("@/pages/QuotationsPage").then((module) => ({ default: module.QuotationsPage })));
const RateCardsPage = lazy(() => import("@/pages/RateCardsPage").then((module) => ({ default: module.RateCardsPage })));
const AccountHealthPage = lazy(() => import("@/pages/AccountHealthPage").then((module) => ({ default: module.AccountHealthPage })));
const TrafficViolationsPage = lazy(() => import("@/pages/TrafficViolationsPage").then((module) => ({ default: module.TrafficViolationsPage })));
const MaintenancePlanningPage = lazy(() => import("@/pages/MaintenancePlanningPage").then((module) => ({ default: module.MaintenancePlanningPage })));
const MaintenanceCommandPage = lazy(() => import("@/pages/MaintenanceCommandPage").then((module) => ({ default: module.MaintenanceCommandPage })));
const CustomerVisibilityPage = lazy(() => import("@/pages/CustomerVisibilityPage").then((module) => ({ default: module.CustomerVisibilityPage })));
const FinancialAnalyticsPage = lazy(() => import("@/pages/FinancialAnalyticsPage").then((module) => ({ default: module.FinancialAnalyticsPage })));
const TaxAdminPage = lazy(() => import("@/pages/TaxAdminPage").then((module) => ({ default: module.TaxAdminPage })));
const BillingConsolidationPage = lazy(() => import("@/pages/BillingConsolidationPage").then((m) => ({ default: m.BillingConsolidationPage })));
const SettlementPage = lazy(() => import("@/pages/SettlementPage").then((m) => ({ default: m.SettlementPage })));
const RevenueRecognitionPage = lazy(() => import("@/pages/RevenueRecognitionPage").then((m) => ({ default: m.RevenueRecognitionPage })));
const DetentionPage = lazy(() => import("@/pages/DetentionPage").then((m) => ({ default: m.DetentionPage })));
const DetentionEvidencePage = lazy(() => import("@/pages/DetentionEvidencePage").then((m) => ({ default: m.DetentionEvidencePage })));
const IntegrationsPage = lazy(() => import("@/pages/IntegrationsPage").then((module) => ({ default: module.IntegrationsPage })));
const FleetAssignmentsPage = lazy(() => import("@/pages/FleetAssignmentsPage").then((module) => ({ default: module.FleetAssignmentsPage })));
const CarbonTrackingPage = lazy(() => import("@/pages/CarbonTrackingPage").then((module) => ({ default: module.CarbonTrackingPage })));
const DigitalFormsPage = lazy(() => import("@/pages/DigitalFormsPage").then((module) => ({ default: module.DigitalFormsPage })));
const Batch3OperationsPage = lazy(() => import("@/pages/Batch3OperationsPage").then((module) => ({ default: module.Batch3OperationsPage })));
const Batch4SafetyPage = lazy(() => import("@/pages/Batch4SafetyPage").then((module) => ({ default: module.Batch4SafetyPage })));
const Batch5FinancePage = lazy(() => import("@/pages/Batch5FinancePage").then((module) => ({ default: module.Batch5FinancePage })));
const CommandCenterPage = lazy(() => import("@/pages/CommandCenterPage").then((module) => ({ default: module.CommandCenterPage })));
const AdminPage = lazy(() => import("@/pages/AdminPage").then((module) => ({ default: module.AdminPage })));
const CompliancePage = lazy(() => import("@/pages/CompliancePage").then((module) => ({ default: module.CompliancePage })));
const HosEldPage = lazy(() => import("@/pages/HosEldPage").then((module) => ({ default: module.HosEldPage })));
const SettingsPage = lazy(() => import("@/pages/SettingsPage").then((module) => ({ default: module.SettingsPage })));
const ControlTowerPage = lazy(() => import("@/pages/ControlTowerPage").then((module) => ({ default: module.ControlTowerPage })));
const LiveMapPage = lazy(() => import("@/pages/LiveMapPage").then((module) => ({ default: module.LiveMapPage })));
const FleetLiveWallPage = lazy(() => import("@/pages/FleetLiveWallPage").then((module) => ({ default: module.FleetLiveWallPage })));
const VehicleLiveMonitorPage = lazy(() => import("@/pages/VehicleLiveMonitorPage").then((module) => ({ default: module.VehicleLiveMonitorPage })));
const CustomerEtaPage = lazy(() => import("@/pages/CustomerEtaPage").then((module) => ({ default: module.CustomerEtaPage })));
const PublicEtaTrackingPage = lazy(() => import("@/pages/CustomerEtaPage").then((module) => ({ default: module.PublicEtaTrackingPage })));
const FleetWorkspacePage = lazy(() => import("@/pages/FleetWorkspacePage").then((module) => ({ default: module.FleetWorkspacePage })));
const PublicShipmentTrackingPage = lazy(() => import("@/pages/PublicShipmentTrackingPage").then((module) => ({ default: module.PublicShipmentTrackingPage })));
const FleetColdChainPage = lazy(() => import("@/pages/FleetColdChainPage").then((module) => ({ default: module.FleetColdChainPage })));
const FleetAssetManagementPage = lazy(() => import("@/pages/FleetAssetManagementPage").then((module) => ({ default: module.FleetAssetManagementPage })));
const FleetSaudiReadinessPage = lazy(() => import("@/pages/FleetSaudiReadinessPage").then((module) => ({ default: module.FleetSaudiReadinessPage })));
const FleetCompliancePage = lazy(() => import("@/pages/FleetCompliancePage").then((module) => ({ default: module.FleetCompliancePage })));
const DispatchWorkspacePage = lazy(() => import("@/pages/DispatchWorkspacePage").then((module) => ({ default: module.DispatchWorkspacePage })));
const EntityListPage = lazy(() => import("@/pages/EntityListPage").then((module) => ({ default: module.EntityListPage })));
const VehiclesModulePage = lazy(() => import("@/pages/VehiclesModulePage").then((module) => ({ default: module.VehiclesModulePage })));
const DriversModulePage = lazy(() => import("@/pages/DriversModulePage").then((module) => ({ default: module.DriversModulePage })));
const JobsPage = lazy(() => import("@/pages/JobsPage").then((module) => ({ default: module.JobsPage })));
const TripsPage = lazy(() => import("@/pages/TripsPage").then((module) => ({ default: module.TripsPage })));
const IotDevicesPage = lazy(() => import("@/pages/IotDevicesPage").then((module) => ({ default: module.IotDevicesPage })));
const TelematicsCommandPage = lazy(() => import("@/pages/TelematicsCommandPage").then((module) => ({ default: module.TelematicsCommandPage })));
const ModulePage = lazy(() => import("@/pages/ModulePage").then((module) => ({ default: module.ModulePage })));
const OperatingModulePage = lazy(() => import("@/pages/OperatingModulePage").then((module) => ({ default: module.OperatingModulePage })));
const RoutePlanningPage = lazy(() => import("@/pages/RoutePlanningPage").then((module) => ({ default: module.RoutePlanningPage })));
const ReportsPage = lazy(() => import("@/pages/ReportsPage").then((module) => ({ default: module.ReportsPage })));
const AnalyticsDashboardPage = lazy(() => import("@/pages/AnalyticsDashboardPage").then((module) => ({ default: module.AnalyticsDashboardPage })));
const SlaKpiPage = lazy(() => import("@/pages/SlaKpiPage").then((module) => ({ default: module.SlaKpiPage })));
const AuditLogsPage = lazy(() => import("@/pages/AuditLogsPage").then((module) => ({ default: module.AuditLogsPage })));
const ExecutivePage = lazy(() => import("@/pages/ExecutivePage").then((module) => ({ default: module.ExecutivePage })));
const AboutPage = lazy(() => import("@/pages/AboutPage").then((module) => ({ default: module.AboutPage })));
const PredictiveAnalyticsPage = lazy(() => import("@/pages/PredictiveAnalyticsPage").then((module) => ({ default: module.PredictiveAnalyticsPage })));
const AlertRulesPage = lazy(() => import("@/pages/AlertRulesPage").then((module) => ({ default: module.AlertRulesPage })));
const DriverMessagingPage = lazy(() => import("@/pages/DriverMessagingPage").then((module) => ({ default: module.DriverMessagingPage })));
const WorkforceManagementPage = lazy(() => import("@/pages/WorkforceManagementPage").then((module) => ({ default: module.WorkforceManagementPage })));
// P6 Mobile Driver Workflow — mobile-first portal at /driver/*
const DriverLayout        = lazy(() => import("@/pages/driver/DriverLayout").then(m => ({ default: m.DriverLayout })));
const DriverDashboardPage = lazy(() => import("@/pages/driver/DriverDashboardPage").then(m => ({ default: m.DriverDashboardPage })));
const DriverAssignmentPage = lazy(() => import("@/pages/driver/DriverAssignmentPage").then(m => ({ default: m.DriverAssignmentPage })));
const DriverDvirPage      = lazy(() => import("@/pages/driver/DriverDvirPage").then(m => ({ default: m.DriverDvirPage })));
const DriverCoachingPage  = lazy(() => import("@/pages/driver/DriverCoachingPage").then(m => ({ default: m.DriverCoachingPage })));
const DriverHosPage       = lazy(() => import("@/pages/driver/DriverHosPage").then(m => ({ default: m.DriverHosPage })));
// P7 Notifications + Messaging
const NotificationCenterPage = lazy(() => import("@/pages/NotificationCenterPage").then(m => ({ default: m.NotificationCenterPage })));
const MessageCenterPage      = lazy(() => import("@/pages/MessageCenterPage").then(m => ({ default: m.MessageCenterPage })));
const DriverNotificationsPage = lazy(() => import("@/pages/driver/DriverNotificationsPage").then(m => ({ default: m.DriverNotificationsPage })));
const PlatformOpsPage = lazy(() => import("@/pages/PlatformOpsPage"));
const FleetHealthPage = lazy(() => import("@/pages/FleetHealthPage").then(m => ({ default: m.FleetHealthPage })));
const FleetIntelligencePage = lazy(() => import("@/pages/FleetIntelligencePage").then(m => ({ default: m.FleetIntelligencePage })));
const FleetOverviewPage = lazy(() => import("@/pages/FleetOverviewPage").then(m => ({ default: m.FleetOverviewPage })));
// Platform Admin — global SaaS business control plane (own auth + routing)
const PlatformApp = lazy(() => import("@/pages/platform/PlatformApp"));

const FeatureFlagsPage = lazy(() => import("@/pages/FeatureFlagsPage").then((module) => ({ default: module.FeatureFlagsPage })));

function ProtectedShell() {
  const { session } = useAuth();
  // Flags are only resolvable for an authenticated user (they're evaluated per-user
  // server-side), so the provider lives inside the authenticated shell.
  return session
    ? <FeatureFlagsProvider><AppShell /></FeatureFlagsProvider>
    : <Navigate to="/login" replace />;
}

/**
 * Route guard for a feature flag. The server gate (Program.cs) is the real
 * enforcement — this just keeps the user out of a page whose API would 403.
 * `fallback` = what to assume before flags load: `true` for a kill switch over
 * existing behaviour, so the page doesn't flicker away on every load.
 */
function RequireFlag({ flag, fallback = true, children }: { flag: string; fallback?: boolean; children: ReactNode }) {
  const on = useFlag(flag, fallback);
  if (on) return <>{children}</>;
  return (
    <div className="flex flex-col items-center justify-center gap-3 p-16 text-center">
      <p className="text-lg font-bold text-slate-900">This feature is turned off</p>
      <p className="max-w-md text-sm text-slate-500">
        An administrator has switched it off for your organisation. It can be re-enabled
        under Settings → Feature flags.
      </p>
    </div>
  );
}

export default function App() {
  const { session } = useAuth();

  return (
    <Suspense fallback={<LoadingState />}>
      <Routes>
        {/* ── Platform Admin — fully isolated control plane, separate auth ── */}
        <Route path="/platform/*" element={<PlatformApp />} />

        <Route path="/login" element={session ? <Navigate to={getLandingRouteForSession(session)} replace /> : <LoginPage />} />
        <Route path="/sso/callback" element={<SsoCallbackPage />} />
        <Route path="/forgot-password" element={<ResetPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/eta/:trackingCode" element={<PublicEtaTrackingPage />} />
        <Route path="/track/:token" element={<PublicShipmentTrackingPage />} />
        <Route path="/evidence/:token" element={<DetentionEvidencePage />} />

        {/* ── P6 Driver Portal — mobile-first, separate layout, requires driver:self ── */}
        {/* The driver portal is a SEPARATE shell from ProtectedShell, so it needs its own
            FeatureFlagsProvider — without it useFlag() would silently return the fallback
            forever and the POD capture flag would gate nothing in the driver UI. */}
        {session ? (
          <Route element={<RequirePermission permission="driver:self"><FeatureFlagsProvider><DriverLayout /></FeatureFlagsProvider></RequirePermission>}>
            <Route path="/driver"               element={<DriverDashboardPage />} />
            <Route path="/driver/assignments"   element={<DriverAssignmentPage />} />
            <Route path="/driver/dvir"           element={<DriverDvirPage />} />
            <Route path="/driver/coaching"       element={<DriverCoachingPage />} />
            <Route path="/driver/hos"            element={<DriverHosPage />} />
            <Route path="/driver/notifications"  element={<DriverNotificationsPage />} />
          </Route>
        ) : null}

        <Route element={<ProtectedShell />}>
          <Route index element={<Navigate to={getLandingRouteForSession(session)} replace />} />

        {/* ── Control Tower ── */}
        <Route path="/live-dashboard" element={<RequirePermission permission="dashboard:view"><FleetOverviewPage /></RequirePermission>} />
        <Route path="/active-shipments" element={<RequirePermission permission="dispatch:view"><JobsPage /></RequirePermission>} />
        <Route path="/alerts" element={<RequirePermission permission="alerts:view"><AlertsCenterPage /></RequirePermission>} />
        <Route path="/map-view" element={<RequirePermission permission="telemetry.live_state.read"><LiveMapPage /></RequirePermission>} />
        <Route path="/fleet/live-wall" element={<RequirePermission permission="telemetry.live_state.read"><FleetLiveWallPage /></RequirePermission>} />
        <Route path="/vehicles/:id/live" element={<RequirePermission permission="telemetry.live_state.read"><VehicleLiveMonitorPage /></RequirePermission>} />
        <Route path="/geofences" element={<RequirePermission permission="map:view"><GeofenceManagementPage /></RequirePermission>} />
        <Route path="/fleet-utilization" element={<Navigate to="/fleet-utilization/overview" replace />} />
        <Route path="/fleet-utilization/*" element={<RequirePermission permission="fleet:view"><FleetUtilizationPage /></RequirePermission>} />

        {/* ── Intelligence ── */}
        <Route path="/command-center" element={<RequirePermission permission="dashboard:view"><CommandCenterPage /></RequirePermission>} />
        <Route path="/control-tower" element={<RequirePermission permission="dashboard:view"><ControlTowerPage /></RequirePermission>} />
        {/* Real flag consumer: the `ai_copilot` kill switch. Turning it off 403s /api/ai/*
            server-side and takes this page out of reach. fallback=true so the page doesn't
            vanish while flags are still loading. */}
        <Route path="/ai-copilot"           element={<RequirePermission permission="reports:view"><RequireFlag flag="ai_copilot"><AiCopilotPage /></RequireFlag></RequirePermission>} />
        <Route path="/predictive-analytics" element={<RequirePermission permission="reports:view"><PredictiveAnalyticsPage /></RequirePermission>} />
        <Route path="/reports-analytics" element={<RequirePermission permission="reports:view"><ReportsPage /></RequirePermission>} />
        <Route path="/reports" element={<RequirePermission permission="reports:view"><ReportsPage /></RequirePermission>} />
        <Route path="/analytics" element={<RequirePermission permission="reports:view"><AnalyticsDashboardPage /></RequirePermission>} />
        <Route path="/executive" element={<RequirePermission permission="dashboard:view"><ExecutivePage /></RequirePermission>} />

        {/* ── Fleet ── */}
        <Route path="/vehicles" element={<Navigate to="/vehicles/overview" replace />} />
        <Route path="/vehicles/*" element={<RequirePermission permission="vehicles:view"><VehiclesModulePage /></RequirePermission>} />
        <Route path="/drivers" element={<Navigate to="/drivers/overview" replace />} />
        <Route path="/drivers/*" element={<RequirePermission permission="drivers:view"><DriversModulePage /></RequirePermission>} />
        <Route path="/assets" element={<RequirePermission permission="vehicles:view"><EntityListPage kind="assets" /></RequirePermission>} />
        <Route path="/iot-devices" element={<RequirePermission permission="telemetry.devices.read"><IotDevicesPage /></RequirePermission>} />
        <Route path="/gps-tracking" element={<RequirePermission permission="telematics:gps:view"><TelematicsCommandPage kind="gps-tracking" /></RequirePermission>} />
        <Route path="/obd-j1939" element={<RequirePermission permission="telematics:diagnostics:view"><TelematicsCommandPage kind="obd-j1939" /></RequirePermission>} />
        <Route path="/sensor-health" element={<RequirePermission permission="telematics:sensors:view"><TelematicsCommandPage kind="sensor-health" /></RequirePermission>} />
        <Route path="/cold-chain" element={<RequirePermission permission="telematics:sensors:view"><TelematicsCommandPage kind="cold-chain" /></RequirePermission>} />
        <Route path="/documents" element={<RequirePermission permission="fleet:view"><Batch3OperationsPage kind="documents" /></RequirePermission>} />

        {/* ── Dispatch / Transport Ops ── */}
        <Route path="/dispatch" element={<RequirePermission permission="dispatch:view"><DispatchCommandPage /></RequirePermission>} />
        <Route path="/dispatch-legacy" element={<RequirePermission permission="dispatch:view"><DispatchPage /></RequirePermission>} />
        <Route path="/jobs" element={<RequirePermission permission="shipments:view"><JobsPage /></RequirePermission>} />
        <Route path="/trips" element={<RequirePermission permission="dispatch:view"><TripsPage /></RequirePermission>} />
        <Route path="/routes" element={<RequirePermission permission="dispatch:view"><RoutePlanningPage /></RequirePermission>} />
        <Route path="/route-planning" element={<RequirePermission permission="dispatch:view"><RoutePlanningPage /></RequirePermission>} />
        <Route path="/shipments" element={<RequirePermission permission="shipments:view"><JobsPage /></RequirePermission>} />
        <Route path="/load-bookings" element={<RequirePermission permission="shipments:view"><JobsPage /></RequirePermission>} />
        <Route path="/route-plans" element={<RequirePermission permission="dispatch:view"><RoutePlanningPage /></RequirePermission>} />
        <Route path="/operations/proof-center" element={<RequirePermission permission="operations.execution_summary.read"><OperationsProofCenterPage /></RequirePermission>} />
        <Route path="/proof-of-delivery" element={<RequirePermission permission="shipments:view"><ProofOfDeliveryPage /></RequirePermission>} />
        <Route path="/last-mile-delivery" element={<RequirePermission permission="dispatch:view"><LastMileDeliveryPage /></RequirePermission>} />

        {/* ── Customer Portal ── */}
        <Route path="/customer-eta" element={<RequirePermission permission="customer_portal:view"><CustomerEtaPage /></RequirePermission>} />
        <Route path="/customer-portal" element={<RequirePermission permission="customer_portal:view"><CustomerPortalPage /></RequirePermission>} />
        <Route path="/customer-visibility" element={<RequirePermission permission="customer_portal:view"><CustomerVisibilityPage /></RequirePermission>} />

        {/* ── Fleet Health + Safety Command Center ── */}
        <Route path="/fleet-health" element={<RequirePermission permission="dashboard:view"><FleetHealthPage /></RequirePermission>} />
        <Route path="/fleet-intelligence" element={<RequirePermission permission="dashboard:view"><FleetIntelligencePage /></RequirePermission>} />
        <Route path="/fleet-workspace" element={<RequirePermission permission="fleet:view"><FleetWorkspacePage mode="command" /></RequirePermission>} />
        <Route path="/fleet-cold-chain" element={<RequirePermission permission="fleet:view"><FleetColdChainPage /></RequirePermission>} />
        <Route path="/fleet-assets" element={<RequirePermission permission="fleet:view"><FleetAssetManagementPage /></RequirePermission>} />
        <Route path="/fleet-saudi-readiness" element={<RequirePermission permission="fleet:view"><RequireRegion countries={GCC_COUNTRIES} moduleTitle="Saudi Readiness"><FleetSaudiReadinessPage /></RequireRegion></RequirePermission>} />
        <Route path="/fleet-compliance" element={<RequirePermission permission="compliance:view"><FleetCompliancePage /></RequirePermission>} />
        <Route path="/logistics-workspace" element={<RequirePermission permission="dispatch:view"><DispatchWorkspacePage mode="dispatch" /></RequirePermission>} />

        {/* ── Maintenance ── */}
        <Route path="/maintenance" element={<RequirePermission permission="maintenance:view"><MaintenanceCommandPage /></RequirePermission>} />
        <Route path="/work-orders" element={<RequirePermission permission="maintenance:view"><MaintenanceCommandPage /></RequirePermission>} />
        <Route path="/dvir-inspections" element={<RequirePermission permission="maintenance:view"><MaintenanceCommandPage /></RequirePermission>} />
        <Route path="/inspections" element={<RequirePermission permission="maintenance:view"><MaintenanceCommandPage /></RequirePermission>} />

        {/* ── Safety ── */}
        <Route path="/safety" element={<RequirePermission permission="safety:view"><Batch4SafetyPage kind="safety" /></RequirePermission>} />
        <Route path="/dashcam" element={<RequirePermission permission="safety:evidence:view"><Batch4SafetyPage kind="dashcam" /></RequirePermission>} />
        <Route path="/ai-dashcam" element={<RequirePermission permission="safety:evidence:view"><Batch4SafetyPage kind="dashcam" /></RequirePermission>} />
        <Route path="/coaching" element={<RequirePermission permission="safety:view"><Batch4SafetyPage kind="coaching" /></RequirePermission>} />
        <Route path="/incidents" element={<RequirePermission permission="safety:view"><Batch4SafetyPage kind="incidents" /></RequirePermission>} />
        <Route path="/evidence-packages" element={<RequirePermission permission="safety:evidence:view"><Batch4SafetyPage kind="evidence" /></RequirePermission>} />
        <Route path="/driver-scorecards" element={<RequirePermission permission="safety:view"><DriverScorecardsPage /></RequirePermission>} />

        {/* ── Commercial / CRM ── */}
        <Route path="/customers" element={<RequirePermission permission="customers:view"><CustomersPage /></RequirePermission>} />
        <Route path="/contracts" element={<RequirePermission permission="customers:view"><ContractsPage /></RequirePermission>} />
        <Route path="/rate-cards" element={<RequirePermission permission="customers:view"><RateCardsPage /></RequirePermission>} />
        <Route path="/price-simulation" element={<RequirePermission permission="customers:view"><QuotationsPage /></RequirePermission>} />
        <Route path="/quotations" element={<RequirePermission permission="customers:view"><QuotationsPage /></RequirePermission>} />
        <Route path="/leads" element={<RequirePermission permission="customers:view"><LeadsPage /></RequirePermission>} />
        <Route path="/sales-pipeline" element={<RequirePermission permission="customers:view"><OpportunitiesPage /></RequirePermission>} />
        <Route path="/opportunities" element={<RequirePermission permission="customers:view"><OpportunitiesPage /></RequirePermission>} />
        <Route path="/campaigns" element={<RequirePermission permission="campaigns:view"><CampaignsPage /></RequirePermission>} />
        <Route path="/account-health" element={<RequirePermission permission="customers:view"><AccountHealthPage /></RequirePermission>} />
        <Route path="/follow-ups" element={<RequirePermission permission="customers:view"><AccountHealthPage /></RequirePermission>} />
        <Route path="/support-tickets" element={<RequirePermission permission="customers:view"><AccountHealthPage /></RequirePermission>} />
        <Route path="/renewals" element={<RequirePermission permission="customers:view"><AccountHealthPage /></RequirePermission>} />
        <Route path="/upsell-opportunities" element={<RequirePermission permission="customers:view"><AccountHealthPage /></RequirePermission>} />
        <Route path="/traffic-violations" element={<RequirePermission permission="safety:view"><TrafficViolationsPage /></RequirePermission>} />
        <Route path="/service-history" element={<RequirePermission permission="fleet:view"><MaintenancePlanningPage /></RequirePermission>} />
        <Route path="/downtime" element={<RequirePermission permission="fleet:view"><MaintenancePlanningPage /></RequirePermission>} />
        <Route path="/preventive-maintenance" element={<RequirePermission permission="fleet:view"><MaintenancePlanningPage /></RequirePermission>} />

        {/* ── Financials (standalone) ── */}
        <Route path="/invoices"      element={<RequirePermission permission="finance:view"><FinancialAnalyticsPage /></RequirePermission>} />
        <Route path="/ar-aging"      element={<RequirePermission permission="finance:view"><FinancialAnalyticsPage /></RequirePermission>} />
        <Route path="/payments"      element={<RequirePermission permission="finance:view"><FinancialAnalyticsPage /></RequirePermission>} />
        <Route path="/profitability" element={<RequirePermission permission="finance:view"><FinancialAnalyticsPage /></RequirePermission>} />
        <Route path="/finance/tax-config" element={<RequirePermission permission="tax.read"><TaxAdminPage /></RequirePermission>} />
        <Route path="/finance/billing" element={<RequirePermission permission="billing.read"><BillingConsolidationPage /></RequirePermission>} />
        <Route path="/finance/settlements" element={<RequirePermission permission="settlement.read"><SettlementPage /></RequirePermission>} />
        <Route path="/finance/revenue-recognition" element={<RequirePermission permission="revrec.read"><RevenueRecognitionPage /></RequirePermission>} />
        <Route path="/detention" element={<RequirePermission permission="finance:view"><DetentionPage /></RequirePermission>} />

        {/* ── Governance ── */}
        <Route path="/integrations"  element={<RequirePermission permission="telematics:providers:manage"><IntegrationsPage /></RequirePermission>} />
        <Route path="/feature-flags"   element={<RequirePermission permission="users:manage"><FeatureFlagsPage /></RequirePermission>} />
        <Route path="/carbon-tracking" element={<RequirePermission permission="reports:view"><CarbonTrackingPage /></RequirePermission>} />
        <Route path="/digital-forms"   element={<RequirePermission permission="safety:view"><DigitalFormsPage /></RequirePermission>} />

        {/* ── Fleet Ownership / Assignments ── */}
        <Route path="/owners" element={<Navigate to="/assignments/owners" replace />} />
        <Route path="/assignments" element={<Navigate to="/assignments/overview" replace />} />
        <Route path="/assignments/*" element={<RequirePermission permission="fleet:view"><FleetAssignmentsPage /></RequirePermission>} />

        {/* ── Finance ── */}
        <Route path="/fuel-idling" element={<RequirePermission permission="fuel:view"><Batch5FinancePage kind="fuel" /></RequirePermission>} />
        <Route path="/expenses" element={<RequirePermission permission="finance:view"><Batch5FinancePage kind="expenses" /></RequirePermission>} />
        <Route path="/contracts-rates" element={<RequirePermission permission="finance:view"><Batch5FinancePage kind="contracts" /></RequirePermission>} />
        <Route path="/carrier-management" element={<RequirePermission permission="finance:view"><Batch5FinancePage kind="carriers" /></RequirePermission>} />
        <Route path="/predictive-margin" element={<RequirePermission permission="finance:view"><Batch5FinancePage kind="cost-margin" /></RequirePermission>} />
        <Route path="/cost-leakage" element={<RequirePermission permission="finance:view"><Batch5FinancePage kind="cost-leakage" /></RequirePermission>} />

        {/* ── Compliance / Governance ── */}
        <Route path="/compliance" element={<RequirePermission permission="compliance:view"><CompliancePage /></RequirePermission>} />
        <Route path="/hos-eld" element={<RequirePermission permission="compliance:view"><HosEldPage /></RequirePermission>} />
        <Route path="/audit-logs" element={<RequirePermission permission="audit:view"><AuditLogsPage /></RequirePermission>} />
        <Route path="/sla-kpi" element={<RequirePermission permission="reports:view"><SlaKpiPage /></RequirePermission>} />

        {/* ── Alert Rules, Driver Messaging & Workforce ── */}
        <Route path="/alert-rules"       element={<RequirePermission permission="alerts:view"><AlertRulesPage /></RequirePermission>} />
        <Route path="/driver-messaging"  element={<RequirePermission permission="dispatch:view"><DriverMessagingPage /></RequirePermission>} />
        <Route path="/workforce"         element={<RequirePermission permission="dispatch:view"><WorkforceManagementPage /></RequirePermission>} />

        {/* ── P7 Notifications + Messaging ── */}
        <Route path="/notifications"   element={<RequirePermission permission="notifications:view"><NotificationCenterPage /></RequirePermission>} />
        <Route path="/messages"        element={<RequirePermission permission="messages:send"><MessageCenterPage /></RequirePermission>} />

        {/* ── Settings / Platform (accessible to all authenticated users) ── */}
        <Route path="/settings" element={<RequirePermission permission="settings:view"><SettingsPage /></RequirePermission>} />
        <Route path="/admin" element={<RequirePermission permissions={["users:view", "roles:view", "settings:view", "audit:view"]}><AdminPage /></RequirePermission>} />
        <Route path="/user-management" element={<RequirePermission permissions={["users:view", "roles:view", "settings:view", "audit:view"]}><AdminPage /></RequirePermission>} />
        <Route path="/ops" element={<RequirePermission permission="ops:view"><PlatformOpsPage /></RequirePermission>} />
        <Route path="/platform/operations" element={<RequirePermission permission="ops:view"><PlatformOpsPage /></RequirePermission>} />
          <Route path="/about" element={<RequirePermission permission="settings:view"><AboutPage /></RequirePermission>} />

        {/* ── Remaining module routes (permission from moduleConfig) ── */}
          {modules
            .filter((module) => ![
              "command-center","control-tower","live-dashboard","active-shipments","alerts","map-view","fleet-live-wall","alerts-center",
              "dispatch","dispatch-board","vehicles","drivers","jobs","route-planning","routes","iot-devices","gps-tracking","obd-j1939","sensor-health","cold-chain",
              "customer-portal","customer-eta","maintenance","work-orders","dvir-inspections","documents",
              "safety","dashcam","coaching","incidents","evidence-packages","customers","assets",
              "ai-copilot","predictive-analytics","fuel-idling","expenses","contracts-rates","carrier-management","predictive-margin",
              "tax-config","billing-consolidation","driver-pay","revenue-recognition","cost-leakage","compliance","hos-eld","settings","reports-analytics","sla-kpi","audit-logs",
              "executive","about","reports","shipments","load-bookings","route-plans","proof-of-delivery",
              "last-mile-delivery","leads","sales-pipeline","opportunities","campaigns","account-health",
              "follow-ups","support-tickets","renewals","upsell-opportunities","contracts","rate-cards",
              "price-simulation","quotations",
              "fleet-utilization","traffic-violations","service-history","downtime","preventive-maintenance",
              "invoices","payments","profitability","integrations","owners","assignments","feature-flags",
              "carbon-tracking","digital-forms",
              "geofences","driver-scorecards",
              "alert-rules","driver-messaging","workforce",
              "notifications","messages",
              "user-management",
              "fleet-health",
              ...operatingRoutes,
            ].includes(module.key))
            .map((module) => (
              <Route
                key={module.key}
                path={module.route.replace("/", "")}
                element={
                  module.requiredPermission ? (
                    <RequirePermission permission={module.requiredPermission}>
                      <ModulePage moduleKey={module.key} />
                    </RequirePermission>
                  ) : (
                    <ModulePage moduleKey={module.key} />
                  )
                }
              />
            ))}
        </Route>
        <Route path="*" element={<Navigate to={session ? getLandingRouteForSession(session) : "/login"} replace />} />
      </Routes>
    </Suspense>
  );
}
