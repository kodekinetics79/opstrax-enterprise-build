import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/layouts/AppShell";
import { useAuth } from "@/hooks/useAuth";
import { modules } from "@/modules/moduleConfig";
import { AiCopilotPage } from "@/pages/AiCopilotPage";
import { Batch3OperationsPage } from "@/pages/Batch3OperationsPage";
import { Batch4SafetyPage } from "@/pages/Batch4SafetyPage";
import { Batch5FinancePage } from "@/pages/Batch5FinancePage";
import { CommandCenterPage } from "@/pages/CommandCenterPage";
import { ControlTowerPage } from "@/pages/ControlTowerPage";
import { CustomerEtaPage, PublicEtaTrackingPage } from "@/pages/CustomerEtaPage";
import { DispatchPage } from "@/pages/DispatchPage";
import { EntityListPage } from "@/pages/EntityListPage";
import { JobsPage } from "@/pages/JobsPage";
import { LoginPage } from "@/pages/LoginPage";
import { ModulePage } from "@/pages/ModulePage";
import { RoutePlanningPage } from "@/pages/RoutePlanningPage";

function Protected() {
  const { session } = useAuth();
  return session ? <AppShell /> : <Navigate to="/login" replace />;
}

export default function App() {
  const { session } = useAuth();

  return (
    <Routes>
      <Route path="/login" element={session ? <Navigate to="/command-center" replace /> : <LoginPage />} />
      <Route path="/eta/:trackingCode" element={<PublicEtaTrackingPage />} />
      <Route element={<Protected />}>
        <Route index element={<Navigate to="/command-center" replace />} />
        <Route path="/command-center" element={<CommandCenterPage />} />
        <Route path="/control-tower" element={<ControlTowerPage />} />
        <Route path="/dispatch" element={<DispatchPage />} />
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
        <Route path="/customers" element={<EntityListPage kind="customers" />} />
        <Route path="/assets" element={<EntityListPage kind="assets" />} />
        <Route path="/ai-copilot" element={<AiCopilotPage />} />
        <Route path="/fuel-idling" element={<Batch5FinancePage kind="fuel" />} />
        <Route path="/expenses" element={<Batch5FinancePage kind="expenses" />} />
        <Route path="/contracts-rates" element={<Batch5FinancePage kind="contracts" />} />
        <Route path="/carrier-management" element={<Batch5FinancePage kind="carriers" />} />
        <Route path="/predictive-margin" element={<Batch5FinancePage kind="cost-margin" />} />
        <Route path="/cost-leakage" element={<Batch5FinancePage kind="cost-leakage" />} />
        {modules
          .filter((module) => !["command-center", "control-tower", "dispatch", "vehicles", "drivers", "jobs", "route-planning", "customer-portal", "maintenance", "work-orders", "dvir-inspections", "documents", "safety", "dashcam", "coaching", "incidents", "evidence-packages", "customers", "assets", "ai-copilot", "fuel-idling", "expenses", "contracts-rates", "carrier-management", "predictive-margin", "cost-leakage"].includes(module.key))
          .map((module) => (
            <Route key={module.key} path={module.route.replace("/", "")} element={<ModulePage moduleKey={module.key} />} />
          ))}
      </Route>
      <Route path="*" element={<Navigate to={session ? "/command-center" : "/login"} replace />} />
    </Routes>
  );
}
