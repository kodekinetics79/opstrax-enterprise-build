import { lazy, Suspense } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/layouts/AppShell";
import { useAuth } from "@/hooks/useAuth";
import { RequirePermission } from "@/hooks/usePermission";
import { hasPermission } from "@/auth/rbacConfig";
import { modules } from "@/modules/moduleConfig";
import { LoginPage } from "@/pages/LoginPage";
import { LoadingState } from "@/components/ui";

const operatingRoutes = [
  "live-dashboard",
  "active-shipments",
  "alerts",
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
  "dispatch-board",
  "route-plans",
  "proof-of-delivery",
  "last-mile-delivery",
] as const;

const AiCopilotPage = lazy(() => import("@/pages/AiCopilotPage").then((module) => ({ default: module.AiCopilotPage })));
const Batch3OperationsPage = lazy(() => import("@/pages/Batch3OperationsPage").then((module) => ({ default: module.Batch3OperationsPage })));
const Batch4SafetyPage = lazy(() => import("@/pages/Batch4SafetyPage").then((module) => ({ default: module.Batch4SafetyPage })));
const Batch5FinancePage = lazy(() => import("@/pages/Batch5FinancePage").then((module) => ({ default: module.Batch5FinancePage })));
const CommandCenterPage = lazy(() => import("@/pages/CommandCenterPage").then((module) => ({ default: module.CommandCenterPage })));
const CompliancePage = lazy(() => import("@/pages/CompliancePage").then((module) => ({ default: module.CompliancePage })));
const HosEldPage = lazy(() => import("@/pages/HosEldPage").then((module) => ({ default: module.HosEldPage })));
const SettingsPage = lazy(() => import("@/pages/SettingsPage").then((module) => ({ default: module.SettingsPage })));
const ControlTowerPage = lazy(() => import("@/pages/ControlTowerPage").then((module) => ({ default: module.ControlTowerPage })));
const CustomerEtaPage = lazy(() => import("@/pages/CustomerEtaPage").then((module) => ({ default: module.CustomerEtaPage })));
const PublicEtaTrackingPage = lazy(() => import("@/pages/CustomerEtaPage").then((module) => ({ default: module.PublicEtaTrackingPage })));
const EntityListPage = lazy(() => import("@/pages/EntityListPage").then((module) => ({ default: module.EntityListPage })));
const JobsPage = lazy(() => import("@/pages/JobsPage").then((module) => ({ default: module.JobsPage })));
const ModulePage = lazy(() => import("@/pages/ModulePage").then((module) => ({ default: module.ModulePage })));
const OperatingModulePage = lazy(() => import("@/pages/OperatingModulePage").then((module) => ({ default: module.OperatingModulePage })));
const RoutePlanningPage = lazy(() => import("@/pages/RoutePlanningPage").then((module) => ({ default: module.RoutePlanningPage })));
const ReportsPage = lazy(() => import("@/pages/ReportsPage").then((module) => ({ default: module.ReportsPage })));
const SlaKpiPage = lazy(() => import("@/pages/SlaKpiPage").then((module) => ({ default: module.SlaKpiPage })));
const AuditLogsPage = lazy(() => import("@/pages/AuditLogsPage").then((module) => ({ default: module.AuditLogsPage })));
const ExecutivePage = lazy(() => import("@/pages/ExecutivePage").then((module) => ({ default: module.ExecutivePage })));
const AboutPage = lazy(() => import("@/pages/AboutPage").then((module) => ({ default: module.AboutPage })));

function getLandingRoute(session: ReturnType<typeof useAuth>["session"]) {
  const permissions = session?.permissions ?? [];
  if (hasPermission(permissions, "dashboard:view")) return "/live-dashboard";
  if (hasPermission(permissions, "customer_portal:view")) return "/customer-portal";
  if (hasPermission(permissions, "shipments:view")) return "/shipments";
  if (hasPermission(permissions, "drivers:view")) return "/drivers";
  return "/live-dashboard";
}

function ProtectedShell() {
  const { session } = useAuth();
  return session ? <AppShell /> : <Navigate to="/login" replace />;
}

export default function App() {
  const { session } = useAuth();

  return (
    <Suspense fallback={<LoadingState />}>
      <Routes>
        <Route path="/login" element={session ? <Navigate to={getLandingRoute(session)} replace /> : <LoginPage />} />
        <Route path="/eta/:trackingCode" element={<PublicEtaTrackingPage />} />
        <Route element={<ProtectedShell />}>
          <Route index element={<Navigate to={getLandingRoute(session)} replace />} />

        {/* ── Control Tower ── */}
        <Route path="/live-dashboard" element={<RequirePermission permission="dashboard:view"><OperatingModulePage moduleKey="live-dashboard" /></RequirePermission>} />
        <Route path="/active-shipments" element={<RequirePermission permission="dispatch:view"><OperatingModulePage moduleKey="active-shipments" /></RequirePermission>} />
        <Route path="/alerts" element={<RequirePermission permission="alerts:view"><OperatingModulePage moduleKey="alerts" /></RequirePermission>} />
        <Route path="/map-view" element={<RequirePermission permission="map:view"><OperatingModulePage moduleKey="map-view" /></RequirePermission>} />

        {/* ── Intelligence ── */}
        <Route path="/command-center" element={<RequirePermission permission="dashboard:view"><CommandCenterPage /></RequirePermission>} />
        <Route path="/control-tower" element={<RequirePermission permission="dashboard:view"><ControlTowerPage /></RequirePermission>} />
        <Route path="/ai-copilot" element={<RequirePermission permission="reports:view"><AiCopilotPage /></RequirePermission>} />
        <Route path="/reports-analytics" element={<RequirePermission permission="reports:view"><ReportsPage /></RequirePermission>} />
        <Route path="/reports" element={<RequirePermission permission="reports:view"><ReportsPage /></RequirePermission>} />
        <Route path="/executive" element={<RequirePermission permission="dashboard:view"><ExecutivePage /></RequirePermission>} />

        {/* ── Fleet ── */}
        <Route path="/vehicles" element={<RequirePermission permission="vehicles:view"><EntityListPage kind="vehicles" /></RequirePermission>} />
        <Route path="/drivers" element={<RequirePermission permission="drivers:view"><EntityListPage kind="drivers" /></RequirePermission>} />
        <Route path="/assets" element={<RequirePermission permission="vehicles:view"><EntityListPage kind="assets" /></RequirePermission>} />
        <Route path="/documents" element={<RequirePermission permission="fleet:view"><Batch3OperationsPage kind="documents" /></RequirePermission>} />

        {/* ── Dispatch / Transport Ops ── */}
        <Route path="/dispatch" element={<RequirePermission permission="dispatch:view"><OperatingModulePage moduleKey="dispatch-board" /></RequirePermission>} />
        <Route path="/jobs" element={<RequirePermission permission="shipments:view"><JobsPage /></RequirePermission>} />
        <Route path="/routes" element={<RequirePermission permission="dispatch:view"><RoutePlanningPage /></RequirePermission>} />
        <Route path="/route-planning" element={<RequirePermission permission="dispatch:view"><RoutePlanningPage /></RequirePermission>} />
        <Route path="/shipments" element={<RequirePermission permission="shipments:view"><OperatingModulePage moduleKey="shipments" /></RequirePermission>} />
        <Route path="/load-bookings" element={<RequirePermission permission="shipments:view"><OperatingModulePage moduleKey="load-bookings" /></RequirePermission>} />
        <Route path="/route-plans" element={<RequirePermission permission="dispatch:view"><OperatingModulePage moduleKey="route-plans" /></RequirePermission>} />
        <Route path="/proof-of-delivery" element={<RequirePermission permission="pod:view"><OperatingModulePage moduleKey="proof-of-delivery" /></RequirePermission>} />
        <Route path="/last-mile-delivery" element={<RequirePermission permission="dispatch:view"><OperatingModulePage moduleKey="last-mile-delivery" /></RequirePermission>} />

        {/* ── Customer Portal ── */}
        <Route path="/customer-eta" element={<RequirePermission permission="customer_portal:view"><CustomerEtaPage /></RequirePermission>} />
        <Route path="/customer-portal" element={<RequirePermission permission="customer_portal:view"><CustomerEtaPage /></RequirePermission>} />

        {/* ── Maintenance ── */}
        <Route path="/maintenance" element={<RequirePermission permission="maintenance:view"><Batch3OperationsPage kind="maintenance" /></RequirePermission>} />
        <Route path="/work-orders" element={<RequirePermission permission="maintenance:view"><Batch3OperationsPage kind="work-orders" /></RequirePermission>} />
        <Route path="/dvir-inspections" element={<RequirePermission permission="maintenance:view"><Batch3OperationsPage kind="dvir" /></RequirePermission>} />
        <Route path="/inspections" element={<RequirePermission permission="maintenance:view"><Batch3OperationsPage kind="dvir" /></RequirePermission>} />

        {/* ── Safety ── */}
        <Route path="/safety" element={<RequirePermission permission="safety:view"><Batch4SafetyPage kind="safety" /></RequirePermission>} />
        <Route path="/dashcam" element={<RequirePermission permission="safety:evidence:view"><Batch4SafetyPage kind="dashcam" /></RequirePermission>} />
        <Route path="/ai-dashcam" element={<RequirePermission permission="safety:evidence:view"><Batch4SafetyPage kind="dashcam" /></RequirePermission>} />
        <Route path="/coaching" element={<RequirePermission permission="safety:view"><Batch4SafetyPage kind="coaching" /></RequirePermission>} />
        <Route path="/incidents" element={<RequirePermission permission="safety:view"><Batch4SafetyPage kind="incidents" /></RequirePermission>} />
        <Route path="/evidence-packages" element={<RequirePermission permission="safety:evidence:view"><Batch4SafetyPage kind="evidence" /></RequirePermission>} />

        {/* ── Commercial / CRM ── */}
        <Route path="/customers" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="customers" /></RequirePermission>} />
        <Route path="/contracts" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="contracts" /></RequirePermission>} />
        <Route path="/rate-cards" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="rate-cards" /></RequirePermission>} />
        <Route path="/price-simulation" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="price-simulation" /></RequirePermission>} />
        <Route path="/quotations" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="quotations" /></RequirePermission>} />
        <Route path="/leads" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="leads" /></RequirePermission>} />
        <Route path="/sales-pipeline" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="sales-pipeline" /></RequirePermission>} />
        <Route path="/opportunities" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="opportunities" /></RequirePermission>} />
        <Route path="/campaigns" element={<RequirePermission permission="campaigns:view"><OperatingModulePage moduleKey="campaigns" /></RequirePermission>} />
        <Route path="/account-health" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="account-health" /></RequirePermission>} />
        <Route path="/follow-ups" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="follow-ups" /></RequirePermission>} />
        <Route path="/support-tickets" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="support-tickets" /></RequirePermission>} />
        <Route path="/renewals" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="renewals" /></RequirePermission>} />
        <Route path="/upsell-opportunities" element={<RequirePermission permission="customers:view"><OperatingModulePage moduleKey="upsell-opportunities" /></RequirePermission>} />

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

        {/* ── Settings / Platform (accessible to all authenticated users) ── */}
        <Route path="/settings" element={<RequirePermission permission="settings:view"><SettingsPage /></RequirePermission>} />
          <Route path="/about" element={<RequirePermission permission="settings:view"><AboutPage /></RequirePermission>} />

        {/* ── Remaining module routes (permission from moduleConfig) ── */}
          {modules
            .filter((module) => ![
              "command-center","control-tower","live-dashboard","active-shipments","alerts","map-view",
              "dispatch","dispatch-board","vehicles","drivers","jobs","route-planning","routes",
              "customer-portal","customer-eta","maintenance","work-orders","dvir-inspections","documents",
              "safety","dashcam","coaching","incidents","evidence-packages","customers","assets",
              "ai-copilot","fuel-idling","expenses","contracts-rates","carrier-management","predictive-margin",
              "cost-leakage","compliance","hos-eld","settings","reports-analytics","sla-kpi","audit-logs",
              "executive","about","reports","shipments","load-bookings","route-plans","proof-of-delivery",
              "last-mile-delivery","leads","sales-pipeline","opportunities","campaigns","account-health",
              "follow-ups","support-tickets","renewals","upsell-opportunities","contracts","rate-cards",
              "price-simulation","quotations",
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
        <Route path="*" element={<Navigate to={session ? getLandingRoute(session) : "/login"} replace />} />
      </Routes>
    </Suspense>
  );
}
