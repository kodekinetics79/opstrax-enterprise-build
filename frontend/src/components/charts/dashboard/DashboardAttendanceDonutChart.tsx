'use client';

import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';

const tooltipStyle = {
  borderRadius: 8,
  border: '1px solid rgba(148,163,184,0.25)',
  fontSize: 12,
  boxShadow: '0 4px 16px rgba(0,0,0,0.08)',
};

export interface AttendanceDonutSlice {
  name: string;
  value: number;
  color: string;
}

export function DashboardAttendanceDonutChart({ data }: { data: AttendanceDonutSlice[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <PieChart>
        <Pie data={data} dataKey="value" nameKey="name" innerRadius={46} outerRadius={66} paddingAngle={2}>
          {data.map((e) => <Cell key={e.name} fill={e.color} />)}
        </Pie>
        <Tooltip contentStyle={tooltipStyle} />
      </PieChart>
    </ResponsiveContainer>
  );
}
