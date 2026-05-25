import { useEffect, useState } from 'react';
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { Layers3 } from 'lucide-react';
import { AuthProvider } from './contexts/AuthContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { AppLayout } from './layouts/AppLayout';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { EmployeesPage } from './pages/EmployeesPage';
import { SetupPage } from './pages/SetupPage';
import { AttendancePage } from './pages/AttendancePage';
import { LeavePage } from './pages/LeavePage';
import { PayrollPage } from './pages/PayrollPage';
import { OvertimePage } from './pages/OvertimePage';
import { ApprovalsPage } from './pages/ApprovalsPage';
import { ShiftsPage } from './pages/ShiftsPage';
import { RecruitmentPage } from './pages/RecruitmentPage';
import { PerformancePage } from './pages/PerformancePage';
import CompliancePage from './pages/CompliancePage';
import { LoansPage } from './pages/LoansPage';
import { ReportsPage } from './pages/ReportsPage';
import { EmployeeSelfServicePage } from './pages/EmployeeSelfServicePage';
import AIAssistantPage from './pages/AIAssistantPage';
import HRRequestCenterPage from './pages/HRRequestCenterPage';
import TenantAdminPage from './pages/TenantAdminPage';
import { UserManagementPage } from './pages/UserManagementPage';
import { applyTheme, getStoredTheme } from './utils/theme';
import type { ThemeMode } from './types/ui';

function ComingSoonPage() {
  const { pathname } = useLocation();
  const name = pathname.replace(/^\//, '').replace(/-/g, ' ');
  return (
    <div className="flex flex-col items-center justify-center py-32 text-center">
      <Layers3 className="mx-auto mb-4 h-12 w-12 text-slate-200 dark:text-slate-700" />
      <h2 className="text-lg font-semibold capitalize text-slate-800 dark:text-slate-200">{name}</h2>
      <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">This module is coming soon</p>
    </div>
  );
}

function AppShell() {
  const [theme, setTheme] = useState<ThemeMode>(() => getStoredTheme());

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  const toggleTheme = () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'));

  return (
    <AppLayout theme={theme} onToggleTheme={toggleTheme}>
      <Routes>
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard" element={<DashboardPage />} />
        <Route path="people" element={<ProtectedRoute requiredPermissions={['employees.read']}><EmployeesPage /></ProtectedRoute>} />
        <Route path="attendance" element={<ProtectedRoute requiredPermissions={['attendance.read','attendance.write','attendance.kiosk']}><AttendancePage /></ProtectedRoute>} />
        <Route path="leave" element={<ProtectedRoute requiredPermissions={['leave.read','leave.write']}><LeavePage /></ProtectedRoute>} />
        <Route path="overtime" element={<ProtectedRoute requiredPermissions={['overtime.read','overtime.write']}><OvertimePage /></ProtectedRoute>} />
        <Route path="payroll" element={<ProtectedRoute requiredPermissions={['payroll.read']}><PayrollPage /></ProtectedRoute>} />
        <Route path="approvals" element={<ProtectedRoute requiredPermissions={['approvals.read','approvals.decide']}><ApprovalsPage /></ProtectedRoute>} />
        <Route path="shifts" element={<ProtectedRoute requiredPermissions={['attendance.read']}><ShiftsPage /></ProtectedRoute>} />
        <Route path="recruitment" element={<ProtectedRoute requiredPermissions={['recruitment.read','recruitment.write']}><RecruitmentPage /></ProtectedRoute>} />
        <Route path="compliance" element={<ProtectedRoute requiredPermissions={['compliance.read','compliance.write']}><CompliancePage /></ProtectedRoute>} />
        <Route path="loans" element={<ProtectedRoute requiredPermissions={['loans.read','loans.write']}><LoansPage /></ProtectedRoute>} />
        <Route path="reports" element={<ProtectedRoute requiredPermissions={['reports.read','reports.schedule']}><ReportsPage /></ProtectedRoute>} />
        <Route path="performance" element={<ProtectedRoute requiredPermissions={['performance.read','performance.write']}><PerformancePage /></ProtectedRoute>} />
        <Route path="ess" element={<ProtectedRoute requiredPermissions={['ess.read']}><EmployeeSelfServicePage /></ProtectedRoute>} />
        <Route path="ai-assistant" element={<AIAssistantPage />} />
        <Route path="hr-requests" element={<ProtectedRoute requiredPermissions={['approvals.read','approvals.write','approvals.decide']}><HRRequestCenterPage /></ProtectedRoute>} />
        <Route path="tenant-admin" element={<ProtectedRoute requiredPermissions={['security.manage']}><TenantAdminPage /></ProtectedRoute>} />
        <Route path="user-management" element={<ProtectedRoute requiredPermissions={['users.manage','roles.manage','security.manage']}><UserManagementPage /></ProtectedRoute>} />
        <Route path="setup" element={<ProtectedRoute requiredPermissions={['organization.write']}><SetupPage /></ProtectedRoute>} />
        <Route path="*" element={<ComingSoonPage />} />
      </Routes>
    </AppLayout>
  );
}

export function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/*"
            element={
              <ProtectedRoute>
                <AppShell />
              </ProtectedRoute>
            }
          />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
