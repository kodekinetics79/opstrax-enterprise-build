'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import dynamic from 'next/dynamic';
import {
  AlertTriangle,
  ArrowRight,
  BadgeDollarSign,
  Bot,
  Building2,
  CalendarPlus,
  ChevronRight,
  FileCheck2,
  Info,
  PlaneTakeoff,
  ShieldAlert,
  Sparkles,
  UserPlus,
  Zap,
} from 'lucide-react';
import { dashboardApi } from '../api/dashboard';
import type { DashboardSummary, DashboardTrend, DashboardOverview, DashboardKpis } from '../api/dashboard';
import { aiAssistantApi } from '../api/intelligence';
import type { AIInsight } from '../api/intelligence';

import { DataPanel } from '../components/DataPanel';
import { KpiCard } from '../components/KpiCard';
import { StatusChip } from '../components/StatusChip';
import type { AiInsight } from '../types/ui';

const DashboardAttendanceTrendChart = dynamic(
  () => import('../components/charts/dashboard/DashboardAttendanceTrendChart').then((m) => m.DashboardAttendanceTrendChart),
  { ssr: false },
);
const DashboardAttendanceDonutChart = dynamic(
  () => import('../components/charts/dashboard/DashboardAttendanceDonutChart').then((m) => m.DashboardAttendanceDonutChart),
  { ssr: false },
);
const DashboardPayrollByEntityChart = dynamic(
  () => import('../components/charts/dashboard/DashboardPayrollByEntityChart').then((m) => m.DashboardPayrollByEntityChart),
  { ssr: false },
);
const DashboardWorkforceMixChart = dynamic(
  () => import('../components/charts/dashboard/DashboardWorkforceMixChart').then((m) => m.DashboardWorkforceMixChart),
  { ssr: false },
);

const mixColors = ['#2F6BFF', '#00C896', '#5EEBFF', '#94A3B8', '#A78BFA', '#F59E0B'];
const mixDotClass = ['bg-sapphire', 'bg-emeraldZ', 'bg-cyanAccent', 'bg-slate-400', 'bg-violet-400', 'bg-amber-500'];
const attendanceColors = { present: '#00C896', leave: '#5EEBFF', absent: '#F43F5E' };

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
    <div className={`rounded-lg border bg-white p-3 dark:bg-white/[0.04] ${severityBorder[sev]}`}>
      <div className="flex items-center gap-2">
        {severityIcon[sev]}
        <p className="text-xs font-bold text-slate-900 dark:text-white">{insight.title}</p>
      </div>
      <p className="mt-1 text-xs leading-relaxed text-slate-600 dark:text-slate-400">{insight.body}</p>
    </div>
  );
}

function EmptyState({ message }: { message: string }) {
  return (
    <div className="flex h-full min-h-[80px] items-center justify-center rounded-lg border border-dashed border-slate-200 px-4 py-5 text-center text-xs text-slate-400 dark:border-white/10 dark:text-slate-500">
      {message}
    </div>
  );
}

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
  const router = useRouter();
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [trends, setTrends] = useState<DashboardTrend[]>([]);
  const [overview, setOverview] = useState<DashboardOverview | null>(null);
  const [opsKpis, setOpsKpis] = useState<DashboardKpis | null>(null);
  const [insights, setInsights] = useState<AIInsight[]>([]);

  useEffect(() => {
    dashboardApi.full(6).then((d) => {
      setSummary(d.summary);
      setTrends(d.trends);
      setOverview(d.overview);
    }).catch(() => {});
    dashboardApi.kpis().then(setOpsKpis).catch(() => {});
    aiAssistantApi.listInsights({ acknowledged: false }).then((r) => setInsights(r.items)).catch(() => {});
  }, []);

  const today = new Date().toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  const attendanceRate = summary && summary.activeEmployees > 0
    ? Math.round((summary.presentToday / summary.activeEmployees) * 100)
    : 0;

  const hasTrend = trends.length > 0;
  const approvalQueue = overview?.approvalQueue ?? [];
  const payroll = overview?.payrollSummary ?? null;
  const payrollByEntity = overview?.payrollByEntity ?? [];
  const workforceMix = overview?.workforceMix ?? [];
  const headcountByDept = overview?.headcountByDepartment ?? [];
  const alerts = overview?.alerts ?? [];
  const workforceTotal = workforceMix.reduce((s, x) => s + x.value, 0);
  const deptMax = Math.max(1, ...headcountByDept.map((d) => d.value));
  const criticalAlerts = alerts.filter((a) => a.severity === 'Critical').length;

  const attendanceDonut = [
    { name: 'Present', value: summary?.presentToday ?? 0, color: attendanceColors.present },
    { name: 'On Leave', value: summary?.onLeave ?? 0, color: attendanceColors.leave },
    { name: 'Absent', value: summary?.absent ?? 0, color: attendanceColors.absent },
  ];
  const attendanceTotal = attendanceDonut.reduce((s, x) => s + x.value, 0);

  const kpis = [
    { label: 'Active Headcount', value: (summary?.activeEmployees ?? 0).toLocaleString(), delta: `${summary?.totalEmployees ?? 0} total · ${overview?.newJoinersThisMonth ?? 0} new`, tone: 'blue' as const, trend: 'up' as const },
    { label: 'Present Today', value: (summary?.presentToday ?? 0).toLocaleString(), delta: summary ? `${attendanceRate}% attendance` : '—', tone: 'emerald' as const, trend: 'up' as const },
    { label: 'On Leave', value: (summary?.onLeave ?? 0).toLocaleString(), delta: `${summary?.absent ?? 0} absent today`, tone: 'cyan' as const, trend: 'neutral' as const },
    { label: 'Pending Approvals', value: (overview?.pendingApprovals ?? 0).toLocaleString(), delta: `${overview?.openLeaveRequests ?? 0} open leave`, tone: 'amber' as const, trend: 'neutral' as const },
    { label: 'Net Payroll', value: payroll ? fmtMoney(payroll.totalNet) : '—', delta: payroll ? `${payroll.periodLabel} · ${payroll.employeeCount} paid` : 'No run yet', tone: 'cyan' as const, trend: 'up' as const },
    { label: 'Compliance Alerts', value: alerts.length.toLocaleString(), delta: `${criticalAlerts} critical`, tone: 'rose' as const, trend: criticalAlerts > 0 ? 'down' as const : 'neutral' as const },
  ];

  return (
    <div className="mx-auto flex max-w-[1600px] flex-col gap-4">
      {/* Header */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <p className="text-[11px] font-bold uppercase tracking-[0.16em] text-sapphire dark:text-cyanAccent">Command Center</p>
            <StatusChip label="Live" tone="emerald" dot />
          </div>
          <h1 className="mt-1 text-2xl font-extrabold tracking-tight text-slate-950 dark:text-white sm:text-2xl">
            Workforce Command Center
          </h1>
          <p className="mt-0.5 truncate text-sm text-slate-500 dark:text-slate-400">
            {today} · {summary ? `${summary.activeEmployees} active · ${attendanceRate}% present` : 'Loading live data…'}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button type="button" className="btn-secondary" onClick={() => router.push('/reports')}>Reports</button>
          <button type="button" className="btn-primary" onClick={() => router.push('/approvals')}>
            Approvals
            {overview && overview.pendingApprovals > 0 && (
              <span className="ml-0.5 rounded-full bg-white/25 px-1.5 text-[10px] font-bold">{overview.pendingApprovals}</span>
            )}
            <ArrowRight className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {/* KPI strip — 6 at a glance */}
      <section className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6" aria-label="Workforce KPIs">
        {kpis.map((metric) => (
          <KpiCard key={metric.label} metric={metric} />
        ))}
      </section>

      {/* Operations KPI strip — role-scoped live counts */}
      {opsKpis && (
        <section className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6" aria-label="Operations KPIs">
          {[
            { label: 'Pending Leave', value: opsKpis.pendingLeaveRequests, tone: 'amber' as const, to: '/leave' },
            { label: 'Attendance Corrections', value: opsKpis.pendingAttendanceCorrections, tone: 'amber' as const, to: '/attendance' },
            { label: 'Attendance Exceptions', value: opsKpis.attendanceExceptions, tone: 'rose' as const, to: '/attendance' },
            { label: 'Expiring Documents', value: opsKpis.expiringDocuments, tone: 'amber' as const, to: '/compliance' },
            { label: 'Expired Documents', value: opsKpis.expiredDocuments, tone: 'rose' as const, to: '/compliance' },
            { label: 'Missing Documents', value: opsKpis.missingDocuments, tone: opsKpis.missingDocuments > 0 ? 'rose' as const : 'emerald' as const, to: '/compliance' },
          ].map(({ label, value, tone, to }) => (
            <button
              key={label}
              type="button"
              onClick={() => router.push(to)}
              className="flex flex-col gap-1 rounded-xl border border-slate-100 bg-white p-3 text-left transition hover:border-sapphire/30 dark:border-white/[0.07] dark:bg-white/[0.03]"
            >
              <span className="text-[10px] font-bold uppercase tracking-[0.12em] text-slate-400 dark:text-slate-500">{label}</span>
              <span className={`text-2xl font-extrabold ${
                tone === 'rose' && value > 0 ? 'text-rose-500' :
                tone === 'amber' && value > 0 ? 'text-amber-500' :
                'text-slate-900 dark:text-white'
              }`}>{value.toLocaleString()}</span>
            </button>
          ))}
        </section>
      )}

      {/* Bento grid */}
      <section className="grid grid-cols-1 gap-4 xl:grid-cols-12">
        {/* Attendance & OT trend */}
        <DataPanel
          title="Attendance & Overtime Trend"
          description="Last 6 months — attendance rate vs overtime hours."
          className="xl:col-span-8"
        >
          <div className="h-[230px]">
            {hasTrend ? (
              <DashboardAttendanceTrendChart data={trends} />
            ) : (
              <EmptyState message="No attendance history recorded yet." />
            )}
          </div>
        </DataPanel>

        {/* Attendance today donut */}
        <DataPanel title="Attendance Today" description="Live workforce status." className="xl:col-span-4">
          {attendanceTotal === 0 ? (
            <EmptyState message="No attendance recorded today." />
          ) : (
            <div className="flex items-center gap-3">
              <div className="relative h-[150px] w-[150px] shrink-0">
                <DashboardAttendanceDonutChart data={attendanceDonut} />
                <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                  <span className="text-xl font-bold text-slate-950 dark:text-white">{attendanceRate}%</span>
                  <span className="text-[10px] font-medium text-slate-400">present</span>
                </div>
              </div>
              <div className="flex-1 space-y-2">
                {attendanceDonut.map((e) => (
                  <div key={e.name} className="flex items-center justify-between">
                    <span className="flex items-center gap-2 text-xs font-semibold text-slate-600 dark:text-slate-300">
                      <span className="h-2 w-2 rounded-full" style={{ background: e.color }} />
                      {e.name}
                    </span>
                    <span className="text-sm font-bold text-slate-950 dark:text-white">{e.value}</span>
                  </div>
                ))}
                <div className="flex items-center justify-between border-t border-slate-100 pt-2 dark:border-white/[0.07]">
                  <span className="text-xs font-semibold text-slate-500">OT (MTD)</span>
                  <span className="text-sm font-bold text-slate-950 dark:text-white">{(summary?.overtimeHours ?? 0).toFixed(0)}h</span>
                </div>
              </div>
            </div>
          )}
        </DataPanel>

        {/* Payroll snapshot */}
        <DataPanel
          title="Payroll Command"
          description={payroll ? `${payroll.periodLabel} · ${payroll.status}` : 'No payroll run yet.'}
          className="xl:col-span-4"
        >
          {payroll ? (
            <>
              <div className="grid grid-cols-2 gap-2">
                {[
                  { label: 'Gross', value: fmtMoney(payroll.totalGross) },
                  { label: 'Net', value: fmtMoney(payroll.totalNet) },
                  { label: 'Deductions', value: fmtMoney(payroll.totalDeductions) },
                  { label: 'Employees', value: payroll.employeeCount.toLocaleString() },
                ].map((t) => (
                  <div key={t.label} className="rounded-lg border border-slate-100 bg-slate-50 p-2.5 dark:border-white/[0.07] dark:bg-white/[0.04]">
                    <p className="text-[10px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">{t.label}</p>
                    <p className="mt-0.5 text-base font-bold text-slate-950 dark:text-white">{t.value}</p>
                  </div>
                ))}
              </div>
              <div className="mt-3 h-[120px]">
                {payrollByEntity.length === 0 ? (
                  <EmptyState message="No payslip breakdown." />
                ) : (
                  <DashboardPayrollByEntityChart data={payrollByEntity} />
                )}
              </div>
            </>
          ) : (
            <EmptyState message="Create a payroll run to see the command center." />
          )}
        </DataPanel>

        {/* Headcount by department */}
        <DataPanel title="Headcount by Department" description="Active employees per department." className="xl:col-span-4">
          {headcountByDept.length === 0 ? (
            <EmptyState message="No active employees yet." />
          ) : (
            <div className="space-y-2.5">
              {headcountByDept.map((d, i) => (
                <div key={d.name}>
                  <div className="mb-1 flex items-center justify-between text-xs">
                    <span className="flex items-center gap-1.5 font-semibold text-slate-600 dark:text-slate-300">
                      <Building2 className="h-3 w-3 text-slate-400" />
                      {d.name}
                    </span>
                    <span className="font-bold text-slate-950 dark:text-white">{d.value}</span>
                  </div>
                  <div className="h-1.5 overflow-hidden rounded-full bg-slate-100 dark:bg-white/[0.06]">
                    <div
                      className="h-full rounded-full"
                      style={{ width: `${(d.value / deptMax) * 100}%`, background: mixColors[i % mixColors.length] }}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </DataPanel>

        {/* Workforce mix */}
        <DataPanel title="Workforce Mix" description="Active employees by employment type." className="xl:col-span-4">
          {workforceMix.length === 0 ? (
            <EmptyState message="No active employees to chart yet." />
          ) : (
            <div className="flex items-center gap-3">
              <div className="h-[150px] w-[150px] shrink-0">
                <DashboardWorkforceMixChart data={workforceMix} />
              </div>
              <div className="flex-1 space-y-1.5">
                {workforceMix.slice(0, 5).map((item, index) => (
                  <div key={item.name} className="flex items-center justify-between text-xs">
                    <span className="flex items-center gap-2 font-semibold text-slate-600 dark:text-slate-300">
                      <span className={`h-2 w-2 rounded-full ${mixDotClass[index % mixDotClass.length]}`} />
                      {item.name}
                    </span>
                    <span className="font-bold text-slate-950 dark:text-white">
                      {item.value}
                      {workforceTotal > 0 && (
                        <span className="ml-1 text-[10px] font-medium text-slate-400">
                          {Math.round((item.value / workforceTotal) * 100)}%
                        </span>
                      )}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </DataPanel>

        {/* Approvals queue */}
        <DataPanel title="Approval Center" description="Pending across HR, payroll, leave and overtime." className="xl:col-span-8" viewAll onViewAll={() => router.push('/approvals')}>
          {approvalQueue.length === 0 ? (
            <EmptyState message="No pending approvals — you're all caught up." />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[420px] border-collapse text-left text-sm">
                <thead>
                  <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                    {['Request', 'Module', 'Submitted'].map((h) => (
                      <th key={h} className="pb-2.5 pr-4 text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50 dark:divide-white/[0.05]">
                  {approvalQueue.map((item) => (
                    <tr
                      key={item.id}
                      className="group cursor-pointer transition-colors hover:bg-slate-50 dark:hover:bg-white/[0.03]"
                      onClick={() => router.push('/approvals')}
                    >
                      <td className="py-2.5 pr-4 font-semibold text-slate-900 dark:text-white">{item.title}</td>
                      <td className="py-2.5 pr-4">
                        <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-slate-600 dark:bg-white/10 dark:text-slate-300">{item.module}</span>
                      </td>
                      <td className="py-2.5 pr-4 text-slate-600 dark:text-slate-300">{timeAgo(item.createdAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </DataPanel>

        {/* AI insights */}
        <DataPanel
          title="AI Workforce Insights"
          description="Advisory signals."
          className="xl:col-span-4"
          action={
            <span className="flex items-center gap-1 rounded-full bg-sapphire/10 px-2 py-0.5 text-[10px] font-bold text-sapphire dark:bg-cyanAccent/10 dark:text-cyanAccent">
              <Sparkles className="h-3 w-3" />
              AI Insights
            </span>
          }
        >
          <div className="space-y-2">
            {insights.length === 0 ? (
              <EmptyState message="No active AI insights." />
            ) : (
              insights.slice(0, 3).map((insight) => (
                <InsightCard
                  key={insight.id ?? insight.title}
                  insight={{ title: insight.title, body: insight.summary, severity: sevToTone[insight.severity] ?? 'info' }}
                />
              ))
            )}
          </div>
          <button type="button" className="btn-secondary mt-3 w-full justify-center" onClick={() => router.push('/ai-assistant')}>
            <Bot className="h-3.5 w-3.5" />
            Open AI Assistant
          </button>
        </DataPanel>

        {/* Alerts & compliance */}
        <DataPanel title="Alerts & Anomalies" description="Documents nearing or past expiry." className="xl:col-span-8">
          <div className="grid gap-2 sm:grid-cols-2">
            {alerts.length === 0 ? (
              <div className="sm:col-span-2"><EmptyState message="No compliance alerts." /></div>
            ) : (
              alerts.slice(0, 6).map((item) => (
                <div key={item.title} className="flex items-center gap-2.5 rounded-lg border border-slate-100 bg-slate-50 px-3 py-2.5 dark:border-white/[0.07] dark:bg-white/[0.04]">
                  {item.severity === 'Critical' ? (
                    <ShieldAlert className="h-4 w-4 shrink-0 text-rose-500" />
                  ) : item.severity === 'Warning' ? (
                    <AlertTriangle className="h-4 w-4 shrink-0 text-amber-500" />
                  ) : (
                    <Info className="h-4 w-4 shrink-0 text-sapphire dark:text-cyanAccent" />
                  )}
                  <span className="truncate text-sm font-medium text-slate-700 dark:text-slate-300">{item.title}</span>
                </div>
              ))
            )}
          </div>
          <button
            type="button"
            onClick={() => router.push('/compliance')}
            className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg bg-midnight py-2 text-xs font-semibold text-white transition hover:opacity-90 dark:bg-white/[0.08] dark:hover:bg-white/[0.12]"
          >
            <PlaneTakeoff className="h-3.5 w-3.5" />
            Review compliance readiness
          </button>
        </DataPanel>

        {/* Quick actions */}
        <DataPanel title="Quick Actions" description="Frequent workflows." className="xl:col-span-4">
          <div className="space-y-1.5">
            {quickActions.map((action) => {
              const Icon = action.icon;
              return (
                <button
                  key={action.label}
                  type="button"
                  onClick={() => router.push(action.to)}
                  className="flex w-full items-center gap-3 rounded-lg border border-slate-100 bg-slate-50 px-3 py-2 text-left transition hover:border-sapphire/20 hover:bg-sapphire/[0.04] dark:border-white/[0.07] dark:bg-white/[0.03] dark:hover:bg-white/[0.06]"
                >
                  <span className="grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-sapphire/10 text-sapphire dark:bg-cyanAccent/10 dark:text-cyanAccent">
                    <Icon className="h-3.5 w-3.5" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">{action.label}</span>
                    <span className="block truncate text-[11px] text-slate-500 dark:text-slate-400">{action.description}</span>
                  </span>
                  <ChevronRight className="h-3.5 w-3.5 shrink-0 text-slate-300 dark:text-slate-600" />
                </button>
              );
            })}
          </div>
        </DataPanel>
      </section>
    </div>
  );
}
