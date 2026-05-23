import { useEffect, useState } from 'react';
import {
  AlertTriangle,
  ArrowRight,
  Bot,
  CheckCircle2,
  ChevronRight,
  CircleDollarSign,
  Clock3,
  Info,
  ShieldAlert,
  Sparkles,
  UsersRound,
  Zap,
} from 'lucide-react';
import { dashboardApi } from '../api/dashboard';
import type { DashboardSummary, DashboardTrend } from '../api/dashboard';
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
import { Avatar } from '../components/Avatar';
import {
  aiInsights,
  alertItems,
  approvalQueue,
  attendanceByHour,
  attendanceSummary,
  featuredAction,
  kpiMetrics,
  payrollByEntity,
  payrollSummary,
  quickActions,
  selfServiceItems,
  workforceMix,
} from '../modules/dashboard/dashboardData';
import type { AiInsight } from '../types/ui';

const mixColors = ['#2F6BFF', '#00C896', '#5EEBFF', '#94A3B8'];
const mixDotClass = [
  'bg-sapphire',
  'bg-emeraldZ',
  'bg-cyanAccent',
  'bg-slate-400',
];

const priorityTone = {
  High: 'rose',
  Medium: 'amber',
  Low: 'slate',
} as const;

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

function InsightCard({ insight }: { insight: AiInsight }) {
  const sev = insight.severity ?? 'info';
  return (
    <div
      className={`rounded-lg border bg-white p-3.5 dark:bg-white/[0.04] ${severityBorder[sev]}`}
    >
      <div className="flex items-center gap-2">
        {severityIcon[sev]}
        <p className="text-xs font-bold text-slate-900 dark:text-white">{insight.title}</p>
      </div>
      <p className="mt-1.5 text-xs leading-relaxed text-slate-600 dark:text-slate-400">
        {insight.body}
      </p>
    </div>
  );
}

const tooltipStyle = {
  borderRadius: 8,
  border: '1px solid rgba(148,163,184,0.25)',
  fontSize: 12,
  boxShadow: '0 4px 16px rgba(0,0,0,0.08)',
};

export function DashboardPage() {
  const FeaturedIcon = featuredAction.icon;
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [trends, setTrends] = useState<DashboardTrend[]>([]);

  useEffect(() => {
    dashboardApi.summary().then(setSummary).catch(() => {});
    dashboardApi.trends(6).then(setTrends).catch(() => {});
  }, []);

  const today = new Date().toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

  const liveKpis = summary
    ? [
        { label: 'Total Active Employees', value: summary.activeEmployees.toLocaleString(), delta: `${summary.totalEmployees} total`, tone: 'blue' as const, trend: 'up' as const },
        { label: 'Present Today', value: summary.presentToday.toLocaleString(), delta: summary.totalEmployees > 0 ? `${Math.round((summary.presentToday / summary.activeEmployees) * 100)}%` : '—', tone: 'emerald' as const, trend: 'up' as const },
        { label: 'On Leave', value: summary.onLeave.toLocaleString(), delta: `${summary.absent} absent`, tone: 'amber' as const, trend: 'neutral' as const },
        { label: 'OT Hours (Month)', value: summary.overtimeHours.toFixed(0), delta: `${summary.churnRisk} churn risk`, tone: 'rose' as const, trend: summary.churnRisk > 0 ? 'down' as const : 'neutral' as const },
      ]
    : kpiMetrics;

  const liveAttendanceSummary = summary
    ? [
        { label: 'Present', value: summary.presentToday.toString() },
        { label: 'On Leave', value: summary.onLeave.toString() },
        { label: 'Absent', value: summary.absent.toString() },
        { label: 'OT Hours (MTD)', value: summary.overtimeHours.toFixed(1) },
      ]
    : attendanceSummary;

  const useLiveTrend = trends.length > 0;

  return (
    <div className="mx-auto flex max-w-[1600px] flex-col gap-5">
      {/* Page header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-sapphire dark:text-cyanAccent">
            Executive Dashboard
          </p>
          <h1 className="mt-1 text-xl font-bold tracking-tight text-slate-950 dark:text-white sm:text-2xl">
            GCC Workforce Command Center
          </h1>
          <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">
            {today} &nbsp;·&nbsp; {summary ? `${summary.activeEmployees} active employees` : 'Loading…'}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button type="button" className="btn-secondary">Export Report</button>
          <button type="button" className="btn-primary">
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
            title="Attendance Live Dashboard"
            description="Today across UAE, KSA, Qatar, Oman and Bahrain entities."
            action={<StatusChip label="Live" tone="emerald" dot />}
          >
            <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_200px]">
              <div className="h-[240px] min-w-0">
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={(useLiveTrend ? trends : attendanceByHour) as Record<string, unknown>[]} margin={{ left: -20, right: 4, top: 6, bottom: 0 }}>
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
                    <XAxis dataKey={useLiveTrend ? 'month' : 'hour'} tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
                    <YAxis tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
                    <Tooltip contentStyle={tooltipStyle} />
                    {useLiveTrend ? (
                      <>
                        <Area type="monotone" dataKey="attendanceRate" name="Attendance %" stroke="#2F6BFF" strokeWidth={2} fill="url(#grad-present)" />
                        <Area type="monotone" dataKey="overtimeHours" name="OT Hours" stroke="#00C896" strokeWidth={2} fill="url(#grad-late)" />
                      </>
                    ) : (
                      <>
                        <Area type="monotone" dataKey="present" name="Present" stroke="#2F6BFF" strokeWidth={2} fill="url(#grad-present)" />
                        <Area type="monotone" dataKey="late" name="Late" stroke="#00C896" strokeWidth={2} fill="url(#grad-late)" />
                      </>
                    )}
                  </AreaChart>
                </ResponsiveContainer>
              </div>
              <div className="grid grid-cols-2 gap-2 lg:grid-cols-1">
                {liveAttendanceSummary.map((item) => (
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
              <div className="overflow-x-auto">
                <table className="w-full min-w-[500px] border-collapse text-left text-sm">
                  <thead>
                    <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                      {['Request', 'Owner', 'Module', 'Due', 'Priority'].map((h) => (
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
                      >
                        <td className="py-2.5 pr-4">
                          <p className="font-semibold text-slate-900 dark:text-white">{item.title}</p>
                          <p className="text-[11px] text-slate-400 dark:text-slate-500">{item.id}</p>
                        </td>
                        <td className="py-2.5 pr-4">
                          <div className="flex items-center gap-1.5">
                            <Avatar name={item.owner} size="xs" />
                            <span className="truncate text-slate-600 dark:text-slate-300">{item.owner}</span>
                          </div>
                        </td>
                        <td className="py-2.5 pr-4">
                          <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-slate-600 dark:bg-white/10 dark:text-slate-300">
                            {item.module}
                          </span>
                        </td>
                        <td className="py-2.5 pr-4 text-slate-600 dark:text-slate-300">{item.due}</td>
                        <td className="py-2.5">
                          <StatusChip label={item.priority} tone={priorityTone[item.priority]} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </DataPanel>

            {/* Payroll Command Center */}
            <DataPanel
              title="Payroll Command Center"
              description="Pre-run health for the May 2026 cycle · WPS cutoff May 28."
            >
              <div className="grid grid-cols-2 gap-2">
                {payrollSummary.map((item) => (
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
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={payrollByEntity} margin={{ left: -20, right: 4, top: 4, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" stroke="currentColor" className="text-slate-100 dark:text-white/[0.06]" />
                    <XAxis dataKey="entity" tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
                    <YAxis tickLine={false} axisLine={false} tick={{ fontSize: 11 }} />
                    <Tooltip contentStyle={tooltipStyle} formatter={(v) => [`AED ${v}M`, 'Gross']} />
                    <Bar dataKey="amount" fill="#2F6BFF" radius={[4, 4, 0, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </DataPanel>
          </div>
        </div>

        {/* Right column — AI + Quick actions */}
        <aside className="flex flex-col gap-5 xl:max-w-[320px]">
          {/* AI Workforce Insights */}
          <DataPanel
            title="AI Workforce Insights"
            description="Explainable signals for HR and finance leaders."
            action={
              <span className="flex items-center gap-1 rounded-full bg-sapphire/10 px-2 py-0.5 text-[10px] font-bold text-sapphire dark:bg-cyanAccent/10 dark:text-cyanAccent">
                <Sparkles className="h-3 w-3" />
                Zayra AI
              </span>
            }
          >
            <div className="space-y-2">
              {aiInsights.map((insight) => (
                <InsightCard key={insight.title} insight={insight} />
              ))}
            </div>
            <button type="button" className="btn-secondary mt-3 w-full justify-center">
              <Bot className="h-3.5 w-3.5" />
              Open Zayra AI
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
        <DataPanel title="Reports & Analytics" description="Workforce distribution by GCC jurisdiction.">
          <div className="flex items-center gap-4">
            <div className="h-[190px] w-[190px] shrink-0">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={workforceMix} dataKey="value" innerRadius={54} outerRadius={80} paddingAngle={2}>
                    {workforceMix.map((entry, index) => (
                      <Cell key={entry.name} fill={mixColors[index % mixColors.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={tooltipStyle} formatter={(v) => [`${v}%`, 'Share']} />
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
                    <span
                      className={`h-2 w-2 rounded-full ${mixDotClass[index % mixDotClass.length]}`}
                    />
                    {item.name}
                  </span>
                  <span className="text-sm font-bold text-slate-950 dark:text-white">{item.value}%</span>
                </div>
              ))}
            </div>
          </div>
        </DataPanel>

        {/* Employee Self-Service */}
        <DataPanel title="Employee Self-Service" description="Employee-facing demand and SLA signals." viewAll>
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
            <SummaryLine icon={UsersRound} label="New joiners" value="128" tone="blue" />
            <SummaryLine icon={CircleDollarSign} label="Exceptions" value="23" tone="amber" />
            <SummaryLine icon={Clock3} label="Coverage" value="91%" tone="emerald" />
          </div>
        </DataPanel>

        {/* Alerts & Anomalies */}
        <DataPanel title="Alerts & Anomalies" description="High-signal operational risks.">
          <div className="space-y-2">
            {alertItems.map((item) => (
              <div
                key={item.label}
                className="flex items-center gap-2.5 rounded-lg border border-slate-100 bg-slate-50 px-3 py-2.5 dark:border-white/[0.07] dark:bg-white/[0.04]"
              >
                {item.tone === 'rose' ? (
                  <ShieldAlert className="h-4 w-4 shrink-0 text-rose-500" />
                ) : (
                  <AlertTriangle className="h-4 w-4 shrink-0 text-amber-500" />
                )}
                <span className="text-sm font-medium text-slate-700 dark:text-slate-300">{item.label}</span>
              </div>
            ))}
          </div>

          {/* Featured action card */}
          <div className="mt-4 rounded-xl bg-midnight p-4 dark:bg-white/[0.06]">
            <div className="flex items-start gap-3">
              <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-cyanAccent text-midnight">
                <FeaturedIcon className="h-4 w-4" />
              </span>
              <div className="min-w-0">
                <p className="text-sm font-bold text-white">{featuredAction.label}</p>
                <p className="mt-0.5 text-xs leading-relaxed text-slate-400">{featuredAction.description}</p>
              </div>
            </div>
            <button type="button" className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg bg-white/10 py-2 text-xs font-semibold text-white transition hover:bg-white/15">
              <Zap className="h-3.5 w-3.5" />
              Open Now
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
            { icon: UsersRound, label: 'New joiners this month', value: '128' },
            { icon: ShieldAlert, label: 'Missing documents', value: '73' },
          ]}
        />
        <ModuleSummary
          icon={CircleDollarSign}
          title="Payroll"
          description="Finance controls and WPS confidence."
          lines={[
            { icon: CircleDollarSign, label: 'Payroll exceptions', value: '23' },
            { icon: CheckCircle2, label: 'Validated employees', value: '8,103' },
          ]}
        />
        <ModuleSummary
          icon={Clock3}
          title="Attendance"
          description="Operational coverage and shift risk."
          lines={[
            { icon: Clock3, label: 'Unassigned shifts', value: '37' },
            { icon: CheckCircle2, label: 'Coverage score', value: '91%' },
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
