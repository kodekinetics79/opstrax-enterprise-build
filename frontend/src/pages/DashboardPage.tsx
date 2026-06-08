import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  ArrowRight,
  BadgeDollarSign,
  Bot,
  CalendarPlus,
  CheckCircle2,
  ChevronRight,
  CircleDollarSign,
  Clock3,
  FileCheck2,
  Info,
  PlaneTakeoff,
  ShieldAlert,
  Sparkles,
  UserPlus,
  UsersRound,
  Zap,
} from 'lucide-react';
import { dashboardApi } from '../api/dashboard';
import type { DashboardSummary, DashboardTrend, DashboardOverview } from '../api/dashboard';
import { aiAssistantApi } from '../api/intelligence';
import type { AIInsight } from '../api/intelligence';
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { DataPanel } from '../components/DataPanel';
import { KpiCard } from '../components/KpiCard';
import { StatusChip } from '../components/StatusChip';
import type { AiInsight } from '../types/ui';

const mixColors = ['#2F6BFF', '#00C896', '#5EEBFF', '#94A3B8', '#A78BFA', '#F59E0B'];
const mixDotClass = ['bg-sapphire', 'bg-emeraldZ', 'bg-cyanAccent', 'bg-slate-400', 'bg-violet-400', 'bg-amber-500'];

const severityIcon = {
  critical: <AlertTriangle className="h-3.5 w-3.5 text-rose-500" />,
  warning: <AlertTriangle className="h-3.5 w-3.5 text-amber-500" />,
  info: <Info className="h-3.5 w-3.5 text-sapphire dark:text-cyanAccent" />,
};

const severityBorder = {
  critical: 'border-rose-200 dark:border-rose-500/20',
  warning: 'border-amber-200 dark:border-amber-500/20',
  info: 'border-sapphire/20 dark:border-cyanAccent/20',
};

const quickActions = [
  { label: 'Add employee', description: 'Start onboarding flow', icon: UserPlus, to: '/people' },
  { label: 'Run payroll checks', description: 'Validate WPS and exceptions', icon: BadgeDollarSign, to: '/payroll' },
  { label: 'Create roster', description: 'Plan branch coverage', icon: CalendarPlus, to: '/shifts' },
  { label: 'Review compliance', description: 'Documents nearing expiry', icon: FileCheck2, to: '/compliance' },
];

function InsightCard({ insight }: { insight: AiInsight }) {
  const sev = insight.severity ?? 'info';
  return (
    <div className={`rounded-lg border bg-white p-3.5 dark:bg-white/[0.04] ${severityBorder[sev]}`}>
      <div className="flex items-center gap-2">
        {severityIcon[sev]}
        <p className="text-xs font-bold text-slate-900 dark:text-white">{insight.title}</p>
      </div>
      <p className="mt-1.5 text-xs leading-relaxed text-slate-600 dark:text-slate-400">{insight.body}</p>
    </div>
  );
}

function EmptyState({ message }: { message: string }) {
  return (
    <div className="flex items-center justify-center rounded-lg border border-dashed border-slate-200 px-4 py-6 text-center text-xs text-slate-400 dark:border-white/10 dark:text-slate-500">
      {message}
    </div>
  );
}

const tooltipStyle = {
  borderRadius: 8,
  border: '1px solid rgba(148,163,184,0.25)',
  fontSize: 12,
  boxShadow: '0 4px 16px rgba(0,0,0,0.08)',
};

const sevToTone = { Critical: 'critical', Warning: 'warning', Info: 'info' } as const;

function fmtMoney(n: number): string {
  if (Math.abs(n) >= 1_000_000) return `AED ${(n / 1_000_000).toFixed(1)}M`;
  if (Math.abs(n) >= 1_000) return `AED ${(n / 1_000).toFixed(1)}K`;
  return `AED ${Math.round(n).toLocaleString()}`;
}

function timeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const days = Math.floor(diff / 86_400_000);
  if (days <= 0) return 'Today';
  if (days === 1) return 'Yesterday';
  if (days < 7) return `${days}d ago`;
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
}

export function DashboardPage() {
  const navigate = useNavigate();
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [trends, setTrends] = useState<DashboardTrend[]>([]);
  const [overview, setOverview] = useState<DashboardOverview | null>(null);
  const [insights, setInsights] = useState<AIInsight[]>([]);

  useEffect(() => {
    dashboardApi.summary().then(setSummary).catch(() => {});
    dashboardApi.trends(6).then(setTrends).catch(() => {});
    dashboardApi.overview().then(setOverview).catch(() => {});
    aiAssistantApi.listInsights({ acknowledged: false }).then((r) => setInsights(r.items)).catch(() => {});
  }, []);

  const today = new Date().toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  const attendanceRate = summary && summary.activeEmployees > 0
    ? Math.round((summary.presentToday / summary.activeEmployees) * 100)
    : 0;

  const liveKpis = [
    { label: 'Total Active Employees', value: (summary?.activeEmployees ?? 0).toLocaleString(), delta: `${summary?.totalEmployees ?? 0} total`, tone: 'blue' as const, trend: 'up' as const },
    { label: 'Present Today', value: (summary?.presentToday ?? 0).toLocaleString(), delta: summary ? `${attendanceRate}% attendance` : '—', tone: 'emerald' as const, trend: 'up' as const },
    { label: 'Pending Approvals', value: (overview?.pendingApprovals ?? 0).toLocaleString(), delta: `${overview?.openLeaveRequests ?? 0} open leave`, tone: 'amber' as const, trend: 'neutral' as const },
    { label: 'OT Hours (Month)', value: (summary?.overtimeHours ?? 0).toFixed(0), delta: `${summary?.churnRisk ?? 0} churn risk`, tone: 'rose' as const, trend: (summary?.churnRisk ?? 0) > 0 ? 'down' as const : 'neutral' as const },
  ];

  const attendanceTiles = [
    { label: 'Present', value: (summary?.presentToday ?? 0).toString() },
    { label: 'On Leave', value: (summary?.onLeave ?? 0).toString() },
    { label: 'Absent', value: (summary?.absent ?? 0).toString() },
    { label: 'OT Hours (MTD)', value: (summary?.overtimeHours ?? 0).toFixed(1) },
  ];

  const hasTrend = trends.length > 0;
  const approvalQueue = overview?.approvalQueue ?? [];
  const payroll = overview?.payrollSummary ?? null;
  const payrollByEntity = overview?.payrollByEntity ?? [];
  const workforceMix = overview?.workforceMix ?? [];
  const alerts = overview?.alerts ?? [];
  const workforceTotal = workforceMix.reduce((s, x) => s + x.value, 0);

  const payrollTiles = payroll
    ? [
        { label: 'Gross Payroll', value: fmtMoney(payroll.totalGross) },
        { label: 'Net Payroll', value: fmtMoney(payroll.totalNet) },
        { label: 'Deductions', value: fmtMoney(payroll.totalDeductions) },
        { label: 'Employees', value: payroll.employeeCount.toLocaleString() },
      ]
    : [];

  const selfServiceItems = [
    { label: 'Open leave requests', value: (overview?.openLeaveRequests ?? 0).toString() },
    { label: 'Pending approvals', value: (overview?.pendingApprovals ?? 0).toString() },
    { label: 'New joiners (this month)', value: (overview?.newJoinersThisMonth ?? 0).toString() },
  ];

  return (
    <div className="mx-auto flex max-w-[1600px] flex-col gap-5">
      {/* Page header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-sapphire dark:text-cyanAccent">
            Command Center
          </p>
          <h1 className="mt-1 text-xl font-bold tracking-tight text-slate-950 dark:text-white sm:text-2xl">
            Workforce Command Center
          </h1>
          <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">
            {today} &nbsp;·&nbsp; {summary ? `${summary.activeEmployees} active employees` : 'Loading…'}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button type="button" className="btn-secondary" onClick={() => navigate('/reports')}>Reports</button>
          <button type="button" className="btn-primary" onClick={() => navigate('/approvals')}>
            Approval Center
            <ArrowRight className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {/* KPI grid */}
      <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-label="Workforce KPIs">
        {liveKpis.map((metric) => (
          <KpiCard key={metric.label} metric={metric} />
        ))}
      </section>

      {/* Main content: charts + right panel */}
      <section className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_320px]">
        {/* Left column */}
        <div className="flex flex-col gap-5">
          {/* Attendance live */}
          <DataPanel
            title="Attendance & Overtime Trend"
            description="Monthly attendance rate and overtime hours."
            action={<StatusChip label="Live" tone="emerald" dot />}
          >
            <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_200px]">
              <div className="h-[240px] min-w-0">
                {hasTrend ? (
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={trends as unknown as Record<string, unknown>[]} margin={{ left: -20, right: 4, top: 6, bottom: 0 }}>
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
                ) : (
                  <div className="flex h-full items-center justify-center">
                    <EmptyState message="No attendance data recorded yet." />
                  </div>
                )}
              </div>
              <div className="grid grid-cols-2 gap-2 lg:grid-cols-1">
                {attendanceTiles.map((item) => (
                  <div
                    key={item.label}
                    className="rounded-lg border border-slate-100 bg-slate-50 p-3 dark:border-white/[0.07] dark:bg-white/[0.04]"
                  >
                    <p className="text-[11px] font-semibold text-slate-500 dark:text-slate-400">{item.label}</p>
                    <p className="mt-1 text-lg font-bold text-slate-950 dark:text-white">{item.value}</p>
                  </div>
                ))}
              </div>
            </div>
          </DataPanel>

          {/* Approval queue + Payroll side-by-side */}
          <div className="grid gap-5 lg:grid-cols-2">
            {/* Approval Center preview */}
            <DataPanel
              title="Approval Center"
              description="Pending across HR, payroll, leave, overtime and documents."
              viewAll
            >
              {approvalQueue.length === 0 ? (
                <EmptyState message="No pending approvals." />
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[420px] border-collapse text-left text-sm">
                    <thead>
                      <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                        {['Request', 'Module', 'Submitted'].map((h) => (
                          <th
                            key={h}
                            className="pb-2.5 pr-4 text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500"
                          >
                            {h}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-50 dark:divide-white/[0.05]">
                      {approvalQueue.map((item) => (
                        <tr
                          key={item.id}
                          className="group cursor-pointer transition-colors hover:bg-slate-50 dark:hover:bg-white/[0.03]"
                          onClick={() => navigate('/approvals')}
                        >
                          <td className="py-2.5 pr-4">
                            <p className="font-semibold text-slate-900 dark:text-white">{item.title}</p>
                          </td>
                          <td className="py-2.5 pr-4">
                            <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-slate-600 dark:bg-white/10 dark:text-slate-300">
                              {item.module}
                            </span>
                          </td>
                          <td className="py-2.5 pr-4 text-slate-600 dark:text-slate-300">{timeAgo(item.createdAtUtc)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </DataPanel>

            {/* Payroll Command Center */}
            <DataPanel
              title="Payroll Command Center"
              description={payroll ? `Latest run · ${payroll.periodLabel} · ${payroll.status}` : 'No payroll run created yet.'}
            >
              {payroll ? (
                <>
                  <div className="grid grid-cols-2 gap-2">
                    {payrollTiles.map((item) => (
                      <div
                        key={item.label}
                        className="rounded-lg border border-slate-100 bg-slate-50 p-3 dark:border-white/[0.07] dark:bg-white/[0.04]"
                      >
                        <p className="text-[11px] font-semibold text-slate-500 dark:text-slate-400">{item.label}</p>
                        <p className="mt-0.5 text-lg font-bold text-slate-950 dark:text-white">{item.value}</p>
                      </div>
                    ))}
                  </div>
                  <div className="mt-4 h-[160px]">
                    {payrollByEntity.length === 0 ? (
                      <EmptyState message="No payslip breakdown for this run." />
                    ) : (
                      <ResponsiveContainer width="100%" height="100%">
                        <BarChart data={payrollByEntity} margin={{ left: -20, right: 4, top: 4, bottom: 0 }}>
                          <CartesianGrid strokeDasharray="3 3" stroke="currentColor" className="text-slate-100 dark:text-white/[0.06]" />
                          <XAxis dataKey="name" tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
                          <YAxis tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
                          <Tooltip contentStyle={tooltipStyle} formatter={(v) => [fmtMoney(Number(v)), 'Net']} />
                          <Bar dataKey="value" fill="#2F6BFF" radius={[4, 4, 0, 0]} />
                        </BarChart>
                      </ResponsiveContainer>
                    )}
                  </div>
                </>
              ) : (
                <EmptyState message="Create a payroll run to see the command center." />
              )}
            </DataPanel>
          </div>
        </div>

        {/* Right column — AI + Quick actions */}
        <aside className="flex flex-col gap-5 xl:max-w-[320px]">
          {/* AI Workforce Insights */}
          <DataPanel
            title="AI Workforce Insights"
            description="Advisory signals for HR and finance leaders."
            action={
              <span className="flex items-center gap-1 rounded-full bg-sapphire/10 px-2 py-0.5 text-[10px] font-bold text-sapphire dark:bg-cyanAccent/10 dark:text-cyanAccent">
                <Sparkles className="h-3 w-3" />
                KynexOne AI
              </span>
            }
          >
            <div className="space-y-2">
              {insights.length === 0 ? (
                <EmptyState message="No active AI insights." />
              ) : (
                insights.slice(0, 4).map((insight) => (
                  <InsightCard
                    key={insight.id ?? insight.title}
                    insight={{ title: insight.title, body: insight.summary, severity: sevToTone[insight.severity] ?? 'info' }}
                  />
                ))
              )}
            </div>
            <button type="button" className="btn-secondary mt-3 w-full justify-center" onClick={() => navigate('/ai-assistant')}>
              <Bot className="h-3.5 w-3.5" />
              Open KynexOne AI
            </button>
          </DataPanel>

          {/* Quick Actions */}
          <DataPanel title="Quick Actions" description="Frequent workflows, fewer clicks.">
            <div className="space-y-1.5">
              {quickActions.map((action) => {
                const Icon = action.icon;
                return (
                  <button
                    key={action.label}
                    type="button"
                    onClick={() => navigate(action.to)}
                    className="flex w-full items-center gap-3 rounded-lg border border-slate-100 bg-slate-50 px-3 py-2.5 text-left transition hover:border-sapphire/20 hover:bg-sapphire/[0.04] dark:border-white/[0.07] dark:bg-white/[0.03] dark:hover:bg-white/[0.06]"
                  >
                    <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-sapphire/10 text-sapphire dark:bg-cyanAccent/10 dark:text-cyanAccent">
                      <Icon className="h-3.5 w-3.5" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">
                        {action.label}
                      </span>
                      <span className="block truncate text-[11px] text-slate-500 dark:text-slate-400">
                        {action.description}
                      </span>
                    </span>
                    <ChevronRight className="h-3.5 w-3.5 shrink-0 text-slate-300 dark:text-slate-600" />
                  </button>
                );
              })}
            </div>
          </DataPanel>
        </aside>
      </section>

      {/* Analytics + ESS + Alerts row */}
      <section className="grid gap-5 xl:grid-cols-[1fr_1fr_300px]">
        {/* Workforce distribution donut */}
        <DataPanel title="Workforce Distribution" description="Active employees by employment type.">
          {workforceMix.length === 0 ? (
            <EmptyState message="No active employees to chart yet." />
          ) : (
            <div className="flex items-center gap-4">
              <div className="h-[190px] w-[190px] shrink-0">
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie data={workforceMix} dataKey="value" nameKey="name" innerRadius={54} outerRadius={80} paddingAngle={2}>
                      {workforceMix.map((entry, index) => (
                        <Cell key={entry.name} fill={mixColors[index % mixColors.length]} />
                      ))}
                    </Pie>
                    <Tooltip contentStyle={tooltipStyle} formatter={(v, n) => [`${v}`, n]} />
                  </PieChart>
                </ResponsiveContainer>
              </div>
              <div className="flex-1 space-y-2">
                {workforceMix.map((item, index) => (
                  <div
                    key={item.name}
                    className="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-2 dark:bg-white/[0.04]"
                  >
                    <span className="flex items-center gap-2 text-sm font-semibold text-slate-700 dark:text-slate-300">
                      <span className={`h-2 w-2 rounded-full ${mixDotClass[index % mixDotClass.length]}`} />
                      {item.name}
                    </span>
                    <span className="text-sm font-bold text-slate-950 dark:text-white">
                      {item.value}
                      {workforceTotal > 0 && (
                        <span className="ml-1 text-[11px] font-medium text-slate-400">
                          ({Math.round((item.value / workforceTotal) * 100)}%)
                        </span>
                      )}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </DataPanel>

        {/* Employee Self-Service */}
        <DataPanel title="Employee Self-Service" description="Employee-facing demand signals." viewAll>
          <div className="space-y-2">
            {selfServiceItems.map((item) => (
              <div
                key={item.label}
                className="flex items-center justify-between rounded-lg border border-slate-100 px-3 py-3 dark:border-white/[0.07]"
              >
                <span className="text-sm font-semibold text-slate-700 dark:text-slate-300">{item.label}</span>
                <span className="text-sm font-bold text-slate-950 dark:text-white">{item.value}</span>
              </div>
            ))}
          </div>
          <div className="mt-4 grid grid-cols-3 gap-2">
            <SummaryLine icon={UsersRound} label="New joiners" value={(overview?.newJoinersThisMonth ?? 0).toString()} tone="blue" />
            <SummaryLine icon={CircleDollarSign} label="Approvals" value={(overview?.pendingApprovals ?? 0).toString()} tone="amber" />
            <SummaryLine icon={Clock3} label="Attendance" value={`${attendanceRate}%`} tone="emerald" />
          </div>
        </DataPanel>

        {/* Alerts & Anomalies */}
        <DataPanel title="Alerts & Anomalies" description="Documents nearing or past expiry.">
          <div className="space-y-2">
            {alerts.length === 0 ? (
              <EmptyState message="No compliance alerts." />
            ) : (
              alerts.map((item) => (
                <div
                  key={item.title}
                  className="flex items-center gap-2.5 rounded-lg border border-slate-100 bg-slate-50 px-3 py-2.5 dark:border-white/[0.07] dark:bg-white/[0.04]"
                >
                  {item.severity === 'Critical' ? (
                    <ShieldAlert className="h-4 w-4 shrink-0 text-rose-500" />
                  ) : item.severity === 'Warning' ? (
                    <AlertTriangle className="h-4 w-4 shrink-0 text-amber-500" />
                  ) : (
                    <Info className="h-4 w-4 shrink-0 text-sapphire dark:text-cyanAccent" />
                  )}
                  <span className="text-sm font-medium text-slate-700 dark:text-slate-300">{item.title}</span>
                </div>
              ))
            )}
          </div>

          {/* Featured action card */}
          <div className="mt-4 rounded-xl bg-midnight p-4 dark:bg-white/[0.06]">
            <div className="flex items-start gap-3">
              <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-cyanAccent text-midnight">
                <PlaneTakeoff className="h-4 w-4" />
              </span>
              <div className="min-w-0">
                <p className="text-sm font-bold text-white">Compliance readiness</p>
                <p className="mt-0.5 text-xs leading-relaxed text-slate-400">Review employees blocked by document expiry.</p>
              </div>
            </div>
            <button
              type="button"
              onClick={() => navigate('/compliance')}
              className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg bg-white/10 py-2 text-xs font-semibold text-white transition hover:bg-white/15"
            >
              <Zap className="h-3.5 w-3.5" />
              Open Compliance
            </button>
          </div>
        </DataPanel>
      </section>

      {/* Bottom summary row */}
      <section className="grid gap-4 lg:grid-cols-3">
        <ModuleSummary
          icon={UsersRound}
          title="People"
          description="Headcount health and onboarding velocity."
          lines={[
            { icon: UsersRound, label: 'Active employees', value: (summary?.activeEmployees ?? 0).toLocaleString() },
            { icon: UserPlus, label: 'New joiners this month', value: (overview?.newJoinersThisMonth ?? 0).toString() },
          ]}
        />
        <ModuleSummary
          icon={CircleDollarSign}
          title="Payroll"
          description="Finance controls and latest run."
          lines={[
            { icon: CircleDollarSign, label: 'Latest net payroll', value: payroll ? fmtMoney(payroll.totalNet) : '—' },
            { icon: CheckCircle2, label: 'Employees paid', value: payroll ? payroll.employeeCount.toLocaleString() : '—' },
          ]}
        />
        <ModuleSummary
          icon={Clock3}
          title="Attendance"
          description="Operational coverage today."
          lines={[
            { icon: Clock3, label: 'Present today', value: (summary?.presentToday ?? 0).toLocaleString() },
            { icon: CheckCircle2, label: 'Attendance rate', value: `${attendanceRate}%` },
          ]}
        />
      </section>
    </div>
  );
}

/* ---- Sub-components ---- */

function SummaryLine({
  icon: Icon,
  label,
  value,
  tone,
}: {
  icon: typeof UsersRound;
  label: string;
  value: string;
  tone: 'blue' | 'emerald' | 'amber';
}) {
  const color = tone === 'blue' ? 'text-sapphire dark:text-blue-400' : tone === 'emerald' ? 'text-emeraldZ dark:text-emerald-400' : 'text-amber-500';
  return (
    <div className="flex flex-col items-center gap-1 rounded-lg bg-slate-50 py-2.5 dark:bg-white/[0.04]">
      <Icon className={`h-4 w-4 ${color}`} />
      <p className="text-base font-bold text-slate-950 dark:text-white">{value}</p>
      <p className="text-center text-[10px] font-medium text-slate-500 dark:text-slate-400">{label}</p>
    </div>
  );
}

function ModuleSummary({
  title,
  description,
  lines,
}: {
  icon: typeof UsersRound;
  title: string;
  description: string;
  lines: { icon: typeof UsersRound; label: string; value: string }[];
}) {
  return (
    <div className="surface rounded-xl p-4">
      <h3 className="text-sm font-bold text-slate-950 dark:text-white">{title}</h3>
      <p className="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{description}</p>
      <div className="mt-3 space-y-2">
        {lines.map(({ icon: Icon, label, value }) => (
          <div
            key={label}
            className="flex items-center justify-between rounded-lg border border-slate-100 px-3 py-2.5 dark:border-white/[0.07]"
          >
            <span className="flex min-w-0 items-center gap-2.5 text-sm font-medium text-slate-600 dark:text-slate-300">
              <Icon className="h-3.5 w-3.5 shrink-0 text-sapphire dark:text-cyanAccent" />
              <span className="truncate">{label}</span>
            </span>
            <strong className="ml-3 shrink-0 text-sm font-bold text-slate-950 dark:text-white">{value}</strong>
          </div>
        ))}
      </div>
    </div>
  );
}
