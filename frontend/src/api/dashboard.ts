import client from './client';

export interface DashboardSummary {
  totalEmployees: number;
  activeEmployees: number;
  presentToday: number;
  onLeave: number;
  absent: number;
  overtimeHours: number;
  churnRisk: number;
}

export interface DashboardTrend {
  month: string;
  attendanceRate: number;
  overtimeHours: number;
}

export interface ApprovalQueueItem {
  id: string;
  title: string;
  module: string;
  createdAtUtc: string;
}

export interface DashboardPayrollSummary {
  periodLabel: string;
  totalGross: number;
  totalNet: number;
  totalDeductions: number;
  employeeCount: number;
  status: string;
}

export interface NamedValue {
  name: string;
  value: number;
}

export interface DashboardAlert {
  title: string;
  severity: 'Info' | 'Warning' | 'Critical';
}

export interface DashboardOverview {
  pendingApprovals: number;
  approvalQueue: ApprovalQueueItem[];
  payrollSummary: DashboardPayrollSummary | null;
  payrollByEntity: NamedValue[];
  workforceMix: NamedValue[];
  alerts: DashboardAlert[];
  openLeaveRequests: number;
  newJoinersThisMonth: number;
}

export const dashboardApi = {
  summary: () => client.get<DashboardSummary>('/api/dashboard/summary').then((r) => r.data),
  trends: (months = 6) => client.get<DashboardTrend[]>('/api/dashboard/trends', { params: { months } }).then((r) => r.data),
  overview: () => client.get<DashboardOverview>('/api/dashboard/overview').then((r) => r.data),
};
