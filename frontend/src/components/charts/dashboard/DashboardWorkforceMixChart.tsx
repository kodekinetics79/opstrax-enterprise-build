'use client';

import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
import type { NamedValue } from '../../../api/dashboard';

const mixColors = ['#2F6BFF', '#00C896', '#5EEBFF', '#94A3B8', '#A78BFA', '#F59E0B'];

const tooltipStyle = {
  borderRadius: 8,
  border: '1px solid rgba(148,163,184,0.25)',
  fontSize: 12,
  boxShadow: '0 4px 16px rgba(0,0,0,0.08)',
};

export function DashboardWorkforceMixChart({ data }: { data: NamedValue[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <PieChart>
        <Pie data={data} dataKey="value" nameKey="name" innerRadius={46} outerRadius={66} paddingAngle={2}>
          {data.map((entry, index) => (
            <Cell key={entry.name} fill={mixColors[index % mixColors.length]} />
          ))}
        </Pie>
        <Tooltip contentStyle={tooltipStyle} formatter={(v, n) => [`${v}`, n]} />
      </PieChart>
    </ResponsiveContainer>
  );
}
