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

export const dashboardApi = {
  summary: () => client.get<DashboardSummary>('/api/dashboard/summary').then((r) => r.data),
  trends: (months = 6) => client.get<DashboardTrend[]>('/api/dashboard/trends', { params: { months } }).then((r) => r.data),
};
