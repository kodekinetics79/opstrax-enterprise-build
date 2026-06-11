'use client';

import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { DashboardTrend } from '../../../api/dashboard';

const tooltipStyle = {
  borderRadius: 8,
  border: '1px solid rgba(148,163,184,0.25)',
  fontSize: 12,
  boxShadow: '0 4px 16px rgba(0,0,0,0.08)',
};

export function DashboardAttendanceTrendChart({ data }: { data: DashboardTrend[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <AreaChart data={data as unknown as Record<string, unknown>[]} margin={{ left: -20, right: 4, top: 6, bottom: 0 }}>
        <defs>
          <linearGradient id="grad-present" x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%" stopColor="#2F6BFF" stopOpacity={0.3} />
            <stop offset="95%" stopColor="#2F6BFF" stopOpacity={0} />
          </linearGradient>
          <linearGradient id="grad-late" x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%" stopColor="#00C896" stopOpacity={0.25} />
            <stop offset="95%" stopColor="#00C896" stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke="currentColor" className="text-slate-100 dark:text-white/[0.06]" />
        <XAxis dataKey="month" tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
        <YAxis tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
        <Tooltip contentStyle={tooltipStyle} />
        <Area type="monotone" dataKey="attendanceRate" name="Attendance %" stroke="#2F6BFF" strokeWidth={2} fill="url(#grad-present)" />
        <Area type="monotone" dataKey="overtimeHours" name="OT Hours" stroke="#00C896" strokeWidth={2} fill="url(#grad-late)" />
      </AreaChart>
    </ResponsiveContainer>
  );
}
