import type { ReactNode } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/layouts/AppShell";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission } from "@/hooks/usePermission";
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

/** Redirects to /live-dashboard if the user lacks the required permission. */
function PermissionGuard({
  permission,
  children,
}: {
  permission: string;
  children: ReactNode;
}) {
  const hasPermission = useHasPermission();
  if (!hasPermission(permission)) return <Navigate to="/live-dashboard" replace />;
  return <>{children}</>;
}

export default function App() {
  const { session } = useAuth();

  return (
    <Routes>
      <Route path="/login" element={session ? <Navigate to="/live-dashboard" replace /> : <LoginPage />} />
      <Route path="/eta/:trackingCode" element={<PublicEtaTrackingPage />} />
      <Route element={<Protected />}>
        <Route index element={<Navigate to="/live-dashboard" replace />} />

        {/* ── Control Tower ── */}
        <Route path="/live-dashboard" element={<PermissionGuard permission="dashboard:view"><OperatingModulePage moduleKey="live-dashboard" /></PermissionGuard>} />
        <Route path="/active-shipments" element={<PermissionGuard permission="dispatch:view"><OperatingModulePage moduleKey="active-shipments" /></PermissionGuard>} />
        <Route path="/alerts" element={<PermissionGuard permission="fleet:view"><OperatingModulePage moduleKey="alerts" /></PermissionGuard>} />
        <Route path="/map-view" element={<PermissionGuard permission="map:view"><OperatingModulePage moduleKey="map-view" /></PermissionGuard>} />

        {/* ── Intelligence ── */}
        <Route path="/command-center" element={<PermissionGuard permission="dashboard:view"><CommandCenterPage /></PermissionGuard>} />
        <Route path="/control-tower" element={<PermissionGuard permission="dashboard:view"><ControlTowerPage /></PermissionGuard>} />
        <Route path="/ai-copilot" element={<PermissionGuard permission="intelligence:view"><AiCopilotPage /></PermissionGuard>} />
        <Route path="/reports-analytics" element={<PermissionGuard permission="intelligence:view"><ReportsPage /></PermissionGuard>} />
        <Route path="/reports" element={<PermissionGuard permission="intelligence:view"><ReportsPage /></PermissionGuard>} />
        <Route path="/executive" element={<PermissionGuard permission="dashboard:view"><ExecutivePage /></PermissionGuard>} />

        {/* ── Fleet ── */}
        <Route path="/vehicles" element={<PermissionGuard permission="fleet:view"><EntityListPage kind="vehicles" /></PermissionGuard>} />
        <Route path="/drivers" element={<PermissionGuard permission="fleet:view"><EntityListPage kind="drivers" /></PermissionGuard>} />
        <Route path="/assets" element={<PermissionGuard permission="fleet:view"><EntityListPage kind="assets" /></PermissionGuard>} />
        <Route path="/documents" element={<PermissionGuard permission="fleet:view"><Batch3OperationsPage kind="documents" /></PermissionGuard>} />

        {/* ── Dispatch / Transport Ops ── */}
        <Route path="/dispatch" element={<PermissionGuard permission="dispatch:view"><OperatingModulePage moduleKey="dispatch-board" /></PermissionGuard>} />
        <Route path="/jobs" element={<PermissionGuard permission="jobs:view"><JobsPage /></PermissionGuard>} />
        <Route path="/routes" element={<PermissionGuard permission="dispatch:view"><RoutePlanningPage /></PermissionGuard>} />
        <Route path="/route-planning" element={<PermissionGuard permission="dispatch:view"><RoutePlanningPage /></PermissionGuard>} />
        <Route path="/shipments" element={<PermissionGuard permission="dispatch:view"><OperatingModulePage moduleKey="shipments" /></PermissionGuard>} />
        <Route path="/load-bookings" element={<PermissionGuard permission="dispatch:view"><OperatingModulePage moduleKey="load-bookings" /></PermissionGuard>} />
        <Route path="/route-plans" element={<PermissionGuard permission="dispatch:view"><OperatingModulePage moduleKey="route-plans" /></PermissionGuard>} />
        <Route path="/proof-of-delivery" element={<PermissionGuard permission="dispatch:view"><OperatingModulePage moduleKey="proof-of-delivery" /></PermissionGuard>} />
        <Route path="/last-mile-delivery" element={<PermissionGuard permission="dispatch:view"><OperatingModulePage moduleKey="last-mile-delivery" /></PermissionGuard>} />

        {/* ── Customer Portal ── */}
        <Route path="/customer-eta" element={<PermissionGuard permission="customer-portal:view"><CustomerEtaPage /></PermissionGuard>} />
        <Route path="/customer-portal" element={<PermissionGuard permission="customer-portal:view"><CustomerEtaPage /></PermissionGuard>} />

        {/* ── Maintenance ── */}
        <Route path="/maintenance" element={<PermissionGuard permission="maintenance:view"><Batch3OperationsPage kind="maintenance" /></PermissionGuard>} />
        <Route path="/work-orders" element={<PermissionGuard permission="maintenance:view"><Batch3OperationsPage kind="work-orders" /></PermissionGuard>} />
        <Route path="/dvir-inspections" element={<PermissionGuard permission="maintenance:view"><Batch3OperationsPage kind="dvir" /></PermissionGuard>} />
        <Route path="/inspections" element={<PermissionGuard permission="maintenance:view"><Batch3OperationsPage kind="dvir" /></PermissionGuard>} />

        {/* ── Safety ── */}
        <Route path="/safety" element={<PermissionGuard permission="safety:view"><Batch4SafetyPage kind="safety" /></PermissionGuard>} />
        <Route path="/dashcam" element={<PermissionGuard permission="safety:view"><Batch4SafetyPage kind="dashcam" /></PermissionGuard>} />
        <Route path="/ai-dashcam" element={<PermissionGuard permission="safety:view"><Batch4SafetyPage kind="dashcam" /></PermissionGuard>} />
        <Route path="/coaching" element={<PermissionGuard permission="safety:view"><Batch4SafetyPage kind="coaching" /></PermissionGuard>} />
        <Route path="/incidents" element={<PermissionGuard permission="safety:view"><Batch4SafetyPage kind="incidents" /></PermissionGuard>} />
        <Route path="/evidence-packages" element={<PermissionGuard permission="safety:view"><Batch4SafetyPage kind="evidence" /></PermissionGuard>} />

        {/* ── Commercial / CRM ── */}
        <Route path="/customers" element={<PermissionGuard permission="customers:view"><OperatingModulePage moduleKey="customers" /></PermissionGuard>} />
        <Route path="/contracts" element={<PermissionGuard permission="customers:view"><OperatingModulePage moduleKey="contracts" /></PermissionGuard>} />
        <Route path="/rate-cards" element={<PermissionGuard permission="customers:view"><OperatingModulePage moduleKey="rate-cards" /></PermissionGuard>} />
        <Route path="/price-simulation" element={<PermissionGuard permission="customers:view"><OperatingModulePage moduleKey="price-simulation" /></PermissionGuard>} />
        <Route path="/quotations" element={<PermissionGuard permission="customers:view"><OperatingModulePage moduleKey="quotations" /></PermissionGuard>} />
        <Route path="/leads" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="leads" /></PermissionGuard>} />
        <Route path="/sales-pipeline" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="sales-pipeline" /></PermissionGuard>} />
        <Route path="/opportunities" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="opportunities" /></PermissionGuard>} />
        <Route path="/campaigns" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="campaigns" /></PermissionGuard>} />
        <Route path="/account-health" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="account-health" /></PermissionGuard>} />
        <Route path="/follow-ups" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="follow-ups" /></PermissionGuard>} />
        <Route path="/support-tickets" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="support-tickets" /></PermissionGuard>} />
        <Route path="/renewals" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="renewals" /></PermissionGuard>} />
        <Route path="/upsell-opportunities" element={<PermissionGuard permission="crm:view"><OperatingModulePage moduleKey="upsell-opportunities" /></PermissionGuard>} />

        {/* ── Finance ── */}
        <Route path="/fuel-idling" element={<PermissionGuard permission="finance:view"><Batch5FinancePage kind="fuel" /></PermissionGuard>} />
        <Route path="/expenses" element={<PermissionGuard permission="finance:view"><Batch5FinancePage kind="expenses" /></PermissionGuard>} />
        <Route path="/contracts-rates" element={<PermissionGuard permission="finance:view"><Batch5FinancePage kind="contracts" /></PermissionGuard>} />
        <Route path="/carrier-management" element={<PermissionGuard permission="finance:view"><Batch5FinancePage kind="carriers" /></PermissionGuard>} />
        <Route path="/predictive-margin" element={<PermissionGuard permission="finance:view"><Batch5FinancePage kind="cost-margin" /></PermissionGuard>} />
        <Route path="/cost-leakage" element={<PermissionGuard permission="finance:view"><Batch5FinancePage kind="cost-leakage" /></PermissionGuard>} />

        {/* ── Compliance / Governance ── */}
        <Route path="/compliance" element={<PermissionGuard permission="compliance:view"><CompliancePage /></PermissionGuard>} />
        <Route path="/hos-eld" element={<PermissionGuard permission="compliance:view"><HosEldPage /></PermissionGuard>} />
        <Route path="/audit-logs" element={<PermissionGuard permission="audit:view"><AuditLogsPage /></PermissionGuard>} />
        <Route path="/sla-kpi" element={<PermissionGuard permission="intelligence:view"><SlaKpiPage /></PermissionGuard>} />

        {/* ── Settings / Platform (accessible to all authenticated users) ── */}
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="/about" element={<AboutPage />} />

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
                  <PermissionGuard permission={module.requiredPermission}>
                    <ModulePage moduleKey={module.key} />
                  </PermissionGuard>
                ) : (
                  <ModulePage moduleKey={module.key} />
                )
              }
            />
          ))}
      </Route>
      <Route path="*" element={<Navigate to={session ? "/live-dashboard" : "/login"} replace />} />
    </Routes>
  );
}
