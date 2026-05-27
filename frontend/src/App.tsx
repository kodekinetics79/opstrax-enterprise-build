import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/layouts/AppShell";
import { useAuth } from "@/hooks/useAuth";
import { modules } from "@/modules/moduleConfig";
import { AiCopilotPage } from "@/pages/AiCopilotPage";
import { Batch3OperationsPage } from "@/pages/Batch3OperationsPage";
import { Batch4SafetyPage } from "@/pages/Batch4SafetyPage";
import { Batch5FinancePage } from "@/pages/Batch5FinancePage";
import { CommandCenterPage } from "@/pages/CommandCenterPage";
import { CompliancePage } from "@/pages/CompliancePage";
import { HosEldPage } from "@/pages/HosEldPage";
import { SettingsPage } from "@/pages/SettingsPage";
import { ControlTowerPage } from "@/pages/ControlTowerPage";
import { CustomerEtaPage, PublicEtaTrackingPage } from "@/pages/CustomerEtaPage";
import { EntityListPage } from "@/pages/EntityListPage";
import { JobsPage } from "@/pages/JobsPage";
import { LoginPage } from "@/pages/LoginPage";
import { ModulePage } from "@/pages/ModulePage";
import { OperatingModulePage } from "@/pages/OperatingModulePage";
import { RoutePlanningPage } from "@/pages/RoutePlanningPage";
import { ReportsPage } from "@/pages/ReportsPage";
import { SlaKpiPage } from "@/pages/SlaKpiPage";
import { AuditLogsPage } from "@/pages/AuditLogsPage";
import { ExecutivePage } from "@/pages/ExecutivePage";
import { AboutPage } from "@/pages/AboutPage";

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

function Protected() {
  const { session } = useAuth();
  return session ? <AppShell /> : <Navigate to="/login" replace />;
}

export default function App() {
  const { session } = useAuth();

  return (
    <Routes>
      <Route path="/login" element={session ? <Navigate to="/live-dashboard" replace /> : <LoginPage />} />
      <Route path="/eta/:trackingCode" element={<PublicEtaTrackingPage />} />
      <Route element={<Protected />}>
        <Route index element={<Navigate to="/live-dashboard" replace />} />
        <Route path="/command-center" element={<CommandCenterPage />} />
        <Route path="/control-tower" element={<ControlTowerPage />} />
        <Route path="/live-dashboard" element={<OperatingModulePage moduleKey="live-dashboard" />} />
        <Route path="/active-shipments" element={<OperatingModulePage moduleKey="active-shipments" />} />
        <Route path="/alerts" element={<OperatingModulePage moduleKey="alerts" />} />
        <Route path="/map-view" element={<OperatingModulePage moduleKey="map-view" />} />
        <Route path="/dispatch" element={<OperatingModulePage moduleKey="dispatch-board" />} />
        <Route path="/vehicles" element={<EntityListPage kind="vehicles" />} />
        <Route path="/drivers" element={<EntityListPage kind="drivers" />} />
        <Route path="/jobs" element={<JobsPage />} />
        <Route path="/routes" element={<RoutePlanningPage />} />
        <Route path="/route-planning" element={<RoutePlanningPage />} />
        <Route path="/customer-eta" element={<CustomerEtaPage />} />
        <Route path="/customer-portal" element={<CustomerEtaPage />} />
        <Route path="/maintenance" element={<Batch3OperationsPage kind="maintenance" />} />
        <Route path="/work-orders" element={<Batch3OperationsPage kind="work-orders" />} />
        <Route path="/dvir-inspections" element={<Batch3OperationsPage kind="dvir" />} />
        <Route path="/inspections" element={<Batch3OperationsPage kind="dvir" />} />
        <Route path="/documents" element={<Batch3OperationsPage kind="documents" />} />
        <Route path="/safety" element={<Batch4SafetyPage kind="safety" />} />
        <Route path="/dashcam" element={<Batch4SafetyPage kind="dashcam" />} />
        <Route path="/ai-dashcam" element={<Batch4SafetyPage kind="dashcam" />} />
        <Route path="/coaching" element={<Batch4SafetyPage kind="coaching" />} />
        <Route path="/incidents" element={<Batch4SafetyPage kind="incidents" />} />
        <Route path="/evidence-packages" element={<Batch4SafetyPage kind="evidence" />} />
        <Route path="/customers" element={<OperatingModulePage moduleKey="customers" />} />
        <Route path="/contracts" element={<OperatingModulePage moduleKey="contracts" />} />
        <Route path="/rate-cards" element={<OperatingModulePage moduleKey="rate-cards" />} />
        <Route path="/price-simulation" element={<OperatingModulePage moduleKey="price-simulation" />} />
        <Route path="/quotations" element={<OperatingModulePage moduleKey="quotations" />} />
        <Route path="/load-bookings" element={<OperatingModulePage moduleKey="load-bookings" />} />
        <Route path="/shipments" element={<OperatingModulePage moduleKey="shipments" />} />
        <Route path="/route-plans" element={<OperatingModulePage moduleKey="route-plans" />} />
        <Route path="/proof-of-delivery" element={<OperatingModulePage moduleKey="proof-of-delivery" />} />
        <Route path="/last-mile-delivery" element={<OperatingModulePage moduleKey="last-mile-delivery" />} />
        <Route path="/leads" element={<OperatingModulePage moduleKey="leads" />} />
        <Route path="/sales-pipeline" element={<OperatingModulePage moduleKey="sales-pipeline" />} />
        <Route path="/opportunities" element={<OperatingModulePage moduleKey="opportunities" />} />
        <Route path="/campaigns" element={<OperatingModulePage moduleKey="campaigns" />} />
        <Route path="/account-health" element={<OperatingModulePage moduleKey="account-health" />} />
        <Route path="/follow-ups" element={<OperatingModulePage moduleKey="follow-ups" />} />
        <Route path="/support-tickets" element={<OperatingModulePage moduleKey="support-tickets" />} />
        <Route path="/renewals" element={<OperatingModulePage moduleKey="renewals" />} />
        <Route path="/upsell-opportunities" element={<OperatingModulePage moduleKey="upsell-opportunities" />} />
        <Route path="/assets" element={<EntityListPage kind="assets" />} />
        <Route path="/ai-copilot" element={<AiCopilotPage />} />
        <Route path="/fuel-idling" element={<Batch5FinancePage kind="fuel" />} />
        <Route path="/expenses" element={<Batch5FinancePage kind="expenses" />} />
        <Route path="/contracts-rates" element={<Batch5FinancePage kind="contracts" />} />
        <Route path="/carrier-management" element={<Batch5FinancePage kind="carriers" />} />
        <Route path="/predictive-margin" element={<Batch5FinancePage kind="cost-margin" />} />
        <Route path="/cost-leakage" element={<Batch5FinancePage kind="cost-leakage" />} />
        <Route path="/compliance" element={<CompliancePage />} />
        <Route path="/hos-eld" element={<HosEldPage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="/reports-analytics" element={<ReportsPage />} />
        <Route path="/sla-kpi" element={<SlaKpiPage />} />
        <Route path="/audit-logs" element={<AuditLogsPage />} />
        <Route path="/executive" element={<ExecutivePage />} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="/reports" element={<ReportsPage />} />
        {modules
          .filter((module) => !["command-center", "control-tower", "dispatch", "dispatch-board", "vehicles", "drivers", "jobs", "route-planning", "customer-portal", "maintenance", "work-orders", "dvir-inspections", "documents", "safety", "dashcam", "coaching", "incidents", "evidence-packages", "customers", "assets", "ai-copilot", "fuel-idling", "expenses", "contracts-rates", "carrier-management", "predictive-margin", "cost-leakage", "compliance", "hos-eld", "settings", "reports-analytics", "sla-kpi", "audit-logs", "executive", "about", ...operatingRoutes].includes(module.key))
          .map((module) => (
            <Route key={module.key} path={module.route.replace("/", "")} element={<ModulePage moduleKey={module.key} />} />
          ))}
      </Route>
      <Route path="*" element={<Navigate to={session ? "/live-dashboard" : "/login"} replace />} />
    </Routes>
  );
}
