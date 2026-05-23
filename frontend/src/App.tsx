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
import { ApprovalsPage } from './pages/ApprovalsPage';
import { ShiftsPage } from './pages/ShiftsPage';
import { RecruitmentPage } from './pages/RecruitmentPage';
import { PerformancePage } from './pages/PerformancePage';
import { EmployeeSelfServicePage } from './pages/EmployeeSelfServicePage';
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
        <Route path="people" element={<EmployeesPage />} />
        <Route path="attendance" element={<AttendancePage />} />
        <Route path="leave" element={<LeavePage />} />
        <Route path="payroll" element={<PayrollPage />} />
        <Route path="approvals" element={<ApprovalsPage />} />
        <Route path="shifts" element={<ShiftsPage />} />
        <Route path="recruitment" element={<RecruitmentPage />} />
        <Route path="performance" element={<PerformancePage />} />
        <Route path="ess" element={<EmployeeSelfServicePage />} />
        <Route path="setup" element={<SetupPage />} />
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
