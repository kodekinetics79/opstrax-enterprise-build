'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import dynamic from 'next/dynamic';
import {
  Activity,
  AlertTriangle,
  ArrowRight,
  ArrowUpRight,
  BadgeDollarSign,
  Bot,
  Building2,
  CalendarCheck,
  CalendarPlus,
  ChevronRight,
  Clock,
  FileCheck2,
  FileWarning,
  Info,
  RefreshCw,
  ShieldAlert,
  Sparkles,
  TrendingDown,
  TrendingUp,
  UserPlus,
  Users,
  Zap,
} from 'lucide-react';
import { dashboardApi } from '../api/dashboard';
import type { DashboardSummary, DashboardTrend, DashboardOverview, DashboardKpis } from '../api/dashboard';
import { aiAssistantApi } from '../api/intelligence';
import type { AIInsight } from '../api/intelligence';
import { useFeatureFlags } from '../contexts/FeatureFlagContext';
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

// ── Constants ─────────────────────────────────────────────────────────────────

const MIX_BAR_CLS  = ['bg-[#2F6BFF]', 'bg-[#00C896]', 'bg-[#5EEBFF]', 'bg-slate-400', 'bg-violet-400', 'bg-amber-500'];
const MIX_DOT_CLS  = MIX_BAR_CLS;
const ATTEND_CLRS  = { present: '#00C896', leave: '#5EEBFF', absent: '#F43F5E' };
const ATTEND_DOT   = { Present: 'bg-[#00C896]', 'On Leave': 'bg-[#5EEBFF]', Absent: 'bg-[#F43F5E]' } as Record<string, string>;

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtMoney(n: number): string {
  if (Math.abs(n) >= 1_000_000) return `AED ${(n / 1_000_000).toFixed(1)}M`;
  if (Math.abs(n) >= 1_000)     return `AED ${(n / 1_000).toFixed(1)}K`;
  return `AED ${Math.round(n).toLocaleString()}`;
}

function timeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const days = Math.floor(diff / 86_400_000);
  if (days <= 0) return 'Today';
  if (days === 1) return 'Yesterday';
  if (days < 7)  return `${days}d ago`;
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
}

function useClock() {
  const [time, setTime] = useState(() => new Date());
  useEffect(() => {
    const id = setInterval(() => setTime(new Date()), 1000);
    return () => clearInterval(id);
  }, []);
  return time;
}

// ── Sub-components ────────────────────────────────────────────────────────────

/** Primary KPI tile — the big-number cards at the top */
function StatTile({
  label, value, delta, tone = 'neutral', trend, icon: Icon, onClick,
}: {
  label: string;
  value: string;
  delta?: string;
  tone?: 'blue' | 'green' | 'cyan' | 'amber' | 'rose' | 'neutral';
  trend?: 'up' | 'down' | 'neutral';
  icon?: React.ElementType;
  onClick?: () => void;
}) {
  const accent: Record<string, string> = {
    blue:    'from-blue-500/[0.08] border-blue-500/20',
    green:   'from-emerald-500/[0.08] border-emerald-500/20',
    cyan:    'from-cyan-500/[0.08] border-cyan-500/20',
    amber:   'from-amber-500/[0.08] border-amber-500/20',
    rose:    'from-rose-500/[0.08] border-rose-500/20',
    neutral: 'from-slate-500/[0.05] border-white/[0.08]',
  };
  const valClr: Record<string, string> = {
    blue: 'text-blue-500 dark:text-blue-400', green: 'text-emerald-500 dark:text-emerald-400',
    cyan: 'text-cyan-500 dark:text-cyan-400', amber: 'text-amber-500 dark:text-amber-400',
    rose: 'text-rose-500 dark:text-rose-400', neutral: 'text-slate-900 dark:text-white',
  };
  const iconClr: Record<string, string> = {
    blue: 'bg-blue-500/10 text-blue-500', green: 'bg-emerald-500/10 text-emerald-500',
    cyan: 'bg-cyan-500/10 text-cyan-500', amber: 'bg-amber-500/10 text-amber-500',
    rose: 'bg-rose-500/10 text-rose-500', neutral: 'bg-slate-100 text-slate-400 dark:bg-white/[0.07] dark:text-slate-400',
  };

  const Wrapper = onClick ? 'button' : 'div';

  return (
    <Wrapper
      {...(onClick ? { type: 'button' as const, onClick } : {})}
      className={`group relative flex flex-col gap-3 overflow-hidden rounded-2xl border bg-gradient-to-b to-transparent p-5 text-left transition-all
        ${accent[tone]}
        ${onClick ? 'cursor-pointer hover:-translate-y-0.5 hover:shadow-lg dark:hover:shadow-black/40' : ''}
        dark:bg-[#0e1729]/80`}
    >
      {/* Top row */}
      <div className="flex items-center justify-between">
        <p className="text-[10px] font-bold uppercase tracking-[0.15em] text-slate-500 dark:text-slate-400">{label}</p>
        {Icon && (
          <span className={`flex h-7 w-7 items-center justify-center rounded-lg ${iconClr[tone]}`}>
            <Icon className="h-3.5 w-3.5" />
          </span>
        )}
      </div>

      {/* Value */}
      <div className="flex items-end justify-between gap-2">
        <p className={`font-mono text-3xl font-extrabold leading-none tracking-tight ${valClr[tone]}`}>
          {value}
        </p>
        {trend === 'up'   && <TrendingUp   className="h-4 w-4 shrink-0 text-emerald-500 mb-0.5" />}
        {trend === 'down' && <TrendingDown className="h-4 w-4 shrink-0 text-rose-500 mb-0.5" />}
      </div>

      {/* Delta */}
      {delta && (
        <p className="text-[11px] font-medium text-slate-500 dark:text-slate-400 leading-tight">{delta}</p>
      )}

      {/* Hover arrow for clickable tiles */}
      {onClick && (
        <ArrowUpRight className="absolute right-3 top-3 h-3.5 w-3.5 text-slate-300 opacity-0 transition-opacity group-hover:opacity-100 dark:text-slate-600" />
      )}
    </Wrapper>
  );
}

/** Compact operational metric — the status-board row */
function OpsMetric({
  label, value, tone, to, router,
}: {
  label: string; value: number; tone: 'rose' | 'amber' | 'green' | 'neutral';
  to: string; router: ReturnType<typeof useRouter>;
}) {
  const dot: Record<string, string> = {
    rose: 'bg-rose-500', amber: 'bg-amber-500', green: 'bg-emerald-500', neutral: 'bg-slate-400',
  };
  const num: Record<string, string> = {
    rose:    value > 0 ? 'text-rose-500 dark:text-rose-400'       : 'text-slate-900 dark:text-white',
    amber:   value > 0 ? 'text-amber-500 dark:text-amber-400'     : 'text-slate-900 dark:text-white',
    green:   'text-emerald-500 dark:text-emerald-400',
    neutral: 'text-slate-900 dark:text-white',
  };
  const border: Record<string, string> = {
    rose:    value > 0 ? 'border-rose-500/30 dark:border-rose-500/20'   : 'border-slate-200 dark:border-white/[0.08]',
    amber:   value > 0 ? 'border-amber-500/30 dark:border-amber-500/20' : 'border-slate-200 dark:border-white/[0.08]',
    green:   'border-emerald-500/30 dark:border-emerald-500/20',
    neutral: 'border-slate-200 dark:border-white/[0.08]',
  };

  return (
    <button
      type="button"
      onClick={() => router.push(to)}
      className={`group flex flex-1 flex-col gap-2 rounded-xl border bg-white px-4 py-3 text-left transition hover:shadow-md dark:bg-white/[0.03] ${border[tone]}`}
    >
      <div className="flex items-center gap-1.5">
        <span className={`h-1.5 w-1.5 rounded-full ${value > 0 ? dot[tone] : 'bg-slate-300 dark:bg-slate-700'}`} />
        <span className="text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500 dark:text-slate-500">{label}</span>
      </div>
      <span className={`font-mono text-2xl font-extrabold leading-none ${num[tone]}`}>
        {value.toLocaleString()}
      </span>
      <span className="flex items-center gap-1 text-[10px] font-medium text-slate-400 opacity-0 transition-opacity group-hover:opacity-100 dark:text-slate-600">
        View <ChevronRight className="h-3 w-3" />
      </span>
    </button>
  );
}

/** Panel wrapper */
function Panel({
  title, sub, action, children, className = '', noPad = false,
}: {
  title?: string; sub?: string; action?: React.ReactNode;
  children: React.ReactNode; className?: string; noPad?: boolean;
}) {
  return (
    <section className={`rounded-2xl border border-slate-200/80 bg-white dark:border-white/[0.07] dark:bg-[#0e1729]/80 ${className}`}>
      {title && (
        <div className="flex items-center justify-between gap-4 border-b border-slate-100 px-5 py-3.5 dark:border-white/[0.06]">
          <div className="min-w-0">
            <h2 className="text-[13px] font-bold text-slate-900 dark:text-white">{title}</h2>
            {sub && <p className="mt-0.5 text-[11px] text-slate-500 dark:text-slate-400">{sub}</p>}
          </div>
          {action && <div className="shrink-0">{action}</div>}
        </div>
      )}
      <div className={noPad ? '' : 'p-5'}>{children}</div>
    </section>
  );
}

/** Section divider label */
function SectionLabel({ label, icon: Icon }: { label: string; icon?: React.ElementType }) {
  return (
    <div className="flex items-center gap-2">
      {Icon && <Icon className="h-3.5 w-3.5 text-slate-400 dark:text-slate-600" />}
      <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-400 dark:text-slate-600">{label}</p>
      <div className="flex-1 border-t border-slate-100 dark:border-white/[0.06]" />
    </div>
  );
}

/** Empty state */
function Empty({ msg }: { msg: string }) {
  return (
    <div className="flex min-h-[72px] items-center justify-center rounded-xl border border-dashed border-slate-200 px-4 py-6 text-center text-xs text-slate-400 dark:border-white/[0.07] dark:text-slate-600">
      {msg}
    </div>
  );
}

/** AI insight row */
function InsightRow({ insight }: { insight: AiInsight }) {
  const sev = insight.severity ?? 'info';
  const cfg = {
    critical: { icon: ShieldAlert,     cls: 'text-rose-500',   bg: 'bg-rose-500/10' },
    warning:  { icon: AlertTriangle,   cls: 'text-amber-500',  bg: 'bg-amber-500/10' },
    info:     { icon: Info,            cls: 'text-blue-500',   bg: 'bg-blue-500/10' },
  } as const;
  const { icon: SevIcon, cls, bg } = cfg[sev];
  return (
    <div className="flex items-start gap-3 rounded-xl border border-slate-100 p-3 dark:border-white/[0.06]">
      <span className={`mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-lg ${bg}`}>
        <SevIcon className={`h-3.5 w-3.5 ${cls}`} />
      </span>
      <div className="min-w-0">
        <p className="text-xs font-semibold text-slate-900 dark:text-white">{insight.title}</p>
        <p className="mt-0.5 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">{insight.body}</p>
      </div>
    </div>
  );
}

const sevToTone = { Critical: 'critical', Warning: 'warning', Info: 'info' } as const;

// ── Page ──────────────────────────────────────────────────────────────────────

export function DashboardPage() {
  const router    = useRouter();
  const clock     = useClock();
  const { isFeatureEnabled } = useFeatureFlags();
  const [loading, setLoading] = useState(false);
  const loadRef   = useRef(false);

  const [summary,  setSummary]  = useState<DashboardSummary | null>(null);
  const [trends,   setTrends]   = useState<DashboardTrend[]>([]);
  const [overview, setOverview] = useState<DashboardOverview | null>(null);
  const [opsKpis,  setOpsKpis]  = useState<DashboardKpis | null>(null);
  const [insights, setInsights] = useState<AIInsight[]>([]);

  const load = async () => {
    if (loadRef.current) return;
    loadRef.current = true;
    setLoading(true);
    const tasks: Promise<void>[] = [
      dashboardApi.full(6).then((d) => { setSummary(d.summary); setTrends(d.trends); setOverview(d.overview); }),
      dashboardApi.kpis().then(setOpsKpis),
    ];
    if (isFeatureEnabled('ai_assistant')) {
      tasks.push(aiAssistantApi.listInsights({ acknowledged: false }).then((r) => setInsights(r.items)).catch(() => {}));
    }
    await Promise.allSettled(tasks);
    setLoading(false);
    loadRef.current = false;
  };

  useEffect(() => { load(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Derived ──────────────────────────────────────────────────────────────

  const attendanceRate = summary && summary.activeEmployees > 0
    ? Math.round((summary.presentToday / summary.activeEmployees) * 100) : 0;

  const approvalQueue   = overview?.approvalQueue ?? [];
  const payroll         = overview?.payrollSummary ?? null;
  const payrollByEntity = overview?.payrollByEntity ?? [];
  const workforceMix    = overview?.workforceMix ?? [];
  const headcountByDept = overview?.headcountByDepartment ?? [];
  const alerts          = overview?.alerts ?? [];
  const workforceTotal  = workforceMix.reduce((s, x) => s + x.value, 0);
  const deptMax         = Math.max(1, ...headcountByDept.map((d) => d.value));
  const criticalAlerts  = alerts.filter((a) => a.severity === 'Critical').length;
  const hasTrend        = trends.length > 0;

  const attendanceDonut = [
    { name: 'Present',  value: summary?.presentToday ?? 0, color: ATTEND_CLRS.present },
    { name: 'On Leave', value: summary?.onLeave      ?? 0, color: ATTEND_CLRS.leave   },
    { name: 'Absent',   value: summary?.absent        ?? 0, color: ATTEND_CLRS.absent  },
  ];
  const attendanceTotal = attendanceDonut.reduce((s, x) => s + x.value, 0);

  const dateStr  = clock.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
  const timeStr  = clock.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

  const needsAttention = criticalAlerts > 0 || (overview?.pendingApprovals ?? 0) >= 5;

  // ── Main KPI tiles ────────────────────────────────────────────────────────

  const mainKpis = [
    {
      label: 'Active Headcount', icon: Users,
      value: (summary?.activeEmployees ?? 0).toLocaleString(),
      delta: `${summary?.totalEmployees ?? 0} total · ${overview?.newJoinersThisMonth ?? 0} new this month`,
      tone: 'blue' as const, trend: 'up' as const,
      onClick: () => router.push('/people'),
    },
    {
      label: 'Present Today', icon: CalendarCheck,
      value: (summary?.presentToday ?? 0).toLocaleString(),
      delta: summary ? `${attendanceRate}% attendance rate` : '—',
      tone: attendanceRate >= 80 ? 'green' as const : attendanceRate >= 60 ? 'amber' as const : 'rose' as const,
      trend: attendanceRate >= 80 ? 'up' as const : 'neutral' as const,
      onClick: () => router.push('/attendance'),
    },
    {
      label: 'On Leave', icon: CalendarPlus,
      value: (summary?.onLeave ?? 0).toLocaleString(),
      delta: `${summary?.absent ?? 0} absent · ${(summary?.overtimeHours ?? 0).toFixed(0)}h OT (MTD)`,
      tone: 'cyan' as const, trend: 'neutral' as const,
      onClick: () => router.push('/leave'),
    },
    {
      label: 'Pending Approvals', icon: Clock,
      value: (overview?.pendingApprovals ?? 0).toLocaleString(),
      delta: `${overview?.openLeaveRequests ?? 0} open leave requests`,
      tone: (overview?.pendingApprovals ?? 0) > 0 ? 'amber' as const : 'green' as const,
      trend: 'neutral' as const,
      onClick: () => router.push('/approvals'),
    },
    {
      label: 'Net Payroll', icon: BadgeDollarSign,
      value: payroll ? fmtMoney(payroll.totalNet) : '—',
      delta: payroll ? `${payroll.periodLabel} · ${payroll.employeeCount} paid` : 'No run yet',
      tone: 'neutral' as const, trend: payroll ? 'up' as const : 'neutral' as const,
      onClick: () => router.push('/payroll'),
    },
    {
      label: 'Compliance Alerts', icon: ShieldAlert,
      value: alerts.length.toLocaleString(),
      delta: criticalAlerts > 0 ? `${criticalAlerts} critical need action` : 'No critical items',
      tone: criticalAlerts > 0 ? 'rose' as const : alerts.length > 0 ? 'amber' as const : 'green' as const,
      trend: criticalAlerts > 0 ? 'down' as const : 'neutral' as const,
      onClick: () => router.push('/compliance'),
    },
  ];

  // ── Quick actions ─────────────────────────────────────────────────────────

  const quickActions = [
    { label: 'Add Employee',      desc: 'Start onboarding',      icon: UserPlus,    to: '/people',     cls: 'bg-blue-500/10 text-blue-500 border-blue-500/20' },
    { label: 'Run Payroll Check', desc: 'Validate WPS',          icon: BadgeDollarSign, to: '/payroll', cls: 'bg-emerald-500/10 text-emerald-600 border-emerald-500/20' },
    { label: 'Create Roster',     desc: 'Plan coverage',         icon: CalendarPlus, to: '/shifts',    cls: 'bg-violet-500/10 text-violet-600 border-violet-500/20' },
    { label: 'Review Compliance', desc: 'Expiring documents',    icon: FileCheck2,  to: '/compliance', cls: 'bg-amber-500/10 text-amber-600 border-amber-500/20' },
  ];

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto flex max-w-[1600px] flex-col gap-5">

      {/* ── Command header ──────────────────────────────────────────────────── */}
      <header className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0">
          <div className="flex items-center gap-2.5">
            <div className="flex items-center gap-1.5">
              <span className="relative flex h-2 w-2">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
              </span>
              <span className="text-[10px] font-bold uppercase tracking-[0.18em] text-emerald-600 dark:text-emerald-400">Live</span>
            </div>
            <span className="h-3 w-px bg-slate-200 dark:bg-white/[0.10]" />
            <span className="font-mono text-[11px] text-slate-400 dark:text-slate-500">{dateStr} · {timeStr}</span>
          </div>
          <h1 className="mt-1.5 text-xl font-extrabold tracking-tight text-slate-900 dark:text-white sm:text-2xl">
            Workforce Command Center
          </h1>
          <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">
            {summary
              ? `${summary.activeEmployees.toLocaleString()} active employees · ${attendanceRate}% present today`
              : 'Loading live data…'}
          </p>
        </div>

        <div className="flex shrink-0 items-center gap-2">
          <button
            type="button"
            onClick={load}
            disabled={loading}
            aria-label="Refresh"
            title="Refresh"
            className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 bg-white text-slate-500 transition hover:border-slate-300 hover:text-slate-800 disabled:opacity-40 dark:border-white/[0.08] dark:bg-white/[0.04] dark:text-slate-400 dark:hover:text-white"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button
            type="button"
            onClick={() => router.push('/reports')}
            className="h-8 rounded-lg border border-slate-200 bg-white px-3 text-xs font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 dark:border-white/[0.08] dark:bg-white/[0.04] dark:text-slate-300 dark:hover:bg-white/[0.08]"
          >
            Reports
          </button>
          <button
            type="button"
            onClick={() => router.push('/approvals')}
            className="flex h-8 items-center gap-1.5 rounded-lg bg-blue-600 px-3 text-xs font-semibold text-white transition hover:bg-blue-500"
          >
            Approvals
            {(overview?.pendingApprovals ?? 0) > 0 && (
              <span className="rounded-full bg-white/25 px-1.5 py-0.5 text-[10px] font-bold leading-none">
                {overview!.pendingApprovals}
              </span>
            )}
            <ArrowRight className="h-3 w-3" />
          </button>
        </div>
      </header>

      {/* ── Critical alert banner ────────────────────────────────────────────── */}
      {needsAttention && (
        <div className="flex flex-wrap items-center gap-3 rounded-xl border border-rose-300/50 bg-rose-50 px-4 py-3 dark:border-rose-500/20 dark:bg-rose-500/[0.06]">
          <span className="relative flex h-2 w-2 shrink-0">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-rose-400 opacity-75" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-rose-500" />
          </span>
          <p className="flex-1 text-sm font-semibold text-rose-700 dark:text-rose-300">
            Action required —
            {criticalAlerts > 0 && ` ${criticalAlerts} critical compliance alert${criticalAlerts !== 1 ? 's' : ''}`}
            {criticalAlerts > 0 && (overview?.pendingApprovals ?? 0) >= 5 && ' and'}
            {(overview?.pendingApprovals ?? 0) >= 5 && ` ${overview!.pendingApprovals} approvals waiting`}
          </p>
          <div className="flex items-center gap-2">
            {criticalAlerts > 0 && (
              <button
                type="button"
                onClick={() => router.push('/compliance')}
                className="rounded-lg bg-rose-600 px-3 py-1.5 text-xs font-semibold text-white transition hover:bg-rose-700"
              >
                View Compliance
              </button>
            )}
            {(overview?.pendingApprovals ?? 0) >= 5 && (
              <button
                type="button"
                onClick={() => router.push('/approvals')}
                className="rounded-lg border border-rose-300 bg-white px-3 py-1.5 text-xs font-semibold text-rose-600 transition hover:bg-rose-50 dark:border-rose-500/30 dark:bg-transparent dark:text-rose-400"
              >
                Review Approvals
              </button>
            )}
          </div>
        </div>
      )}

      {/* ── Section: Primary KPIs ────────────────────────────────────────────── */}
      <div>
        <SectionLabel label="Key Metrics" icon={Activity} />
        <div className="mt-3 grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
          {mainKpis.map((k) => (
            <StatTile key={k.label} {...k} />
          ))}
        </div>
      </div>

      {/* ── Section: Operational status bar ──────────────────────────────────── */}
      {opsKpis && (
        <div>
          <SectionLabel label="Operations Queue" icon={Zap} />
          <div className="mt-3 flex gap-3 overflow-x-auto pb-1">
            {([
              { label: 'Pending Leave',    value: opsKpis.pendingLeaveRequests,         tone: 'amber'   as const, to: '/leave'      },
              { label: 'Att. Corrections', value: opsKpis.pendingAttendanceCorrections,  tone: 'amber'   as const, to: '/attendance' },
              { label: 'Att. Exceptions',  value: opsKpis.attendanceExceptions,          tone: 'rose'    as const, to: '/attendance' },
              { label: 'Expiring Docs',    value: opsKpis.expiringDocuments,             tone: 'amber'   as const, to: '/compliance' },
              { label: 'Expired Docs',     value: opsKpis.expiredDocuments,              tone: 'rose'    as const, to: '/compliance' },
              { label: 'Missing Docs',     value: opsKpis.missingDocuments,              tone: opsKpis.missingDocuments > 0 ? 'rose' as const : 'green' as const, to: '/compliance' },
            ]).map((m) => (
              <OpsMetric key={m.label} {...m} router={router} />
            ))}
          </div>
        </div>
      )}

      {/* ── Section: Analytics ───────────────────────────────────────────────── */}
      <div>
        <SectionLabel label="Analytics" icon={Activity} />
        <div className="mt-3 grid grid-cols-1 gap-4 xl:grid-cols-12">

          {/* Attendance & OT trend */}
          <Panel
            title="Attendance & Overtime Trend"
            sub="Last 6 months — attendance rate vs overtime hours"
            className="xl:col-span-8"
          >
            <div className="h-[220px]">
              {hasTrend
                ? <DashboardAttendanceTrendChart data={trends} />
                : <Empty msg="No attendance history recorded yet." />}
            </div>
          </Panel>

          {/* Today donut */}
          <Panel title="Today's Attendance" sub="Live workforce status" className="xl:col-span-4">
            {attendanceTotal === 0 ? (
              <Empty msg="No attendance recorded today." />
            ) : (
              <div className="flex items-center gap-4">
                <div className="relative h-[140px] w-[140px] shrink-0">
                  <DashboardAttendanceDonutChart data={attendanceDonut} />
                  <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                    <span className={`font-mono text-2xl font-extrabold leading-none ${
                      attendanceRate >= 80 ? 'text-emerald-500' : attendanceRate >= 60 ? 'text-amber-500' : 'text-rose-500'
                    }`}>{attendanceRate}%</span>
                    <span className="mt-0.5 text-[10px] font-semibold text-slate-400">present</span>
                  </div>
                </div>
                <div className="flex-1 space-y-3">
                  {attendanceDonut.map((e) => (
                    <div key={e.name} className="flex items-center justify-between gap-3">
                      <span className="flex items-center gap-2 text-xs font-medium text-slate-600 dark:text-slate-300">
                        <span className={`h-2 w-2 shrink-0 rounded-full ${ATTEND_DOT[e.name] ?? 'bg-slate-400'}`} />
                        {e.name}
                      </span>
                      <span className="font-mono text-sm font-bold text-slate-900 dark:text-white">{e.value}</span>
                    </div>
                  ))}
                  <div className="border-t border-slate-100 pt-2 dark:border-white/[0.06]">
                    <div className="flex items-center justify-between">
                      <span className="text-xs font-medium text-slate-500">OT this month</span>
                      <span className="font-mono text-sm font-bold text-slate-900 dark:text-white">{(summary?.overtimeHours ?? 0).toFixed(0)}h</span>
                    </div>
                  </div>
                </div>
              </div>
            )}
          </Panel>
        </div>
      </div>

      {/* ── Section: Operations ─────────────────────────────────────────────── */}
      <div>
        <SectionLabel label="Operations" icon={Clock} />
        <div className="mt-3 grid grid-cols-1 gap-4 xl:grid-cols-12">

          {/* Approvals queue */}
          <Panel
            title="Approval Queue"
            sub={`${approvalQueue.length} pending across HR, payroll, leave and overtime`}
            className="xl:col-span-8"
            action={
              <button
                type="button"
                onClick={() => router.push('/approvals')}
                className="flex items-center gap-1 text-xs font-semibold text-blue-600 hover:underline dark:text-blue-400"
              >
                View all <ArrowRight className="h-3 w-3" />
              </button>
            }
          >
            {approvalQueue.length === 0 ? (
              <Empty msg="No pending approvals — all caught up." />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[420px] border-collapse text-left">
                  <thead>
                    <tr className="border-b border-slate-100 dark:border-white/[0.06]">
                      {['Request', 'Module', 'Submitted'].map((h) => (
                        <th key={h} className="pb-3 pr-4 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-400 dark:text-slate-600">{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-50 dark:divide-white/[0.04]">
                    {approvalQueue.map((item) => (
                      <tr
                        key={item.id}
                        className="group cursor-pointer transition-colors hover:bg-slate-50 dark:hover:bg-white/[0.03]"
                        onClick={() => router.push('/approvals')}
                      >
                        <td className="py-3 pr-4">
                          <span className="text-sm font-semibold text-slate-900 dark:text-white">{item.title}</span>
                        </td>
                        <td className="py-3 pr-4">
                          <span className="rounded-md bg-blue-500/10 px-2 py-0.5 text-[11px] font-semibold text-blue-600 dark:text-blue-400">{item.module}</span>
                        </td>
                        <td className="py-3 pr-4">
                          <span className="text-xs text-slate-500 dark:text-slate-400">{timeAgo(item.createdAtUtc)}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Panel>

          {/* Payroll snapshot */}
          <Panel
            title="Payroll Snapshot"
            sub={payroll ? `${payroll.periodLabel} · ${payroll.status}` : 'No payroll run yet'}
            className="xl:col-span-4"
            action={
              <button type="button" onClick={() => router.push('/payroll')} className="flex items-center gap-1 text-xs font-semibold text-blue-600 hover:underline dark:text-blue-400">
                View <ArrowRight className="h-3 w-3" />
              </button>
            }
          >
            {payroll ? (
              <>
                <div className="grid grid-cols-2 gap-2">
                  {[
                    { label: 'Gross',       value: fmtMoney(payroll.totalGross) },
                    { label: 'Net',         value: fmtMoney(payroll.totalNet) },
                    { label: 'Deductions',  value: fmtMoney(payroll.totalDeductions) },
                    { label: 'Employees',   value: payroll.employeeCount.toLocaleString() },
                  ].map((t) => (
                    <div key={t.label} className="rounded-xl border border-slate-100 bg-slate-50/80 p-3 dark:border-white/[0.06] dark:bg-white/[0.03]">
                      <p className="text-[10px] font-bold uppercase tracking-[0.12em] text-slate-400 dark:text-slate-500">{t.label}</p>
                      <p className="mt-1 font-mono text-base font-bold text-slate-900 dark:text-white">{t.value}</p>
                    </div>
                  ))}
                </div>
                <div className="mt-3 h-[110px]">
                  {payrollByEntity.length === 0
                    ? <Empty msg="No payslip breakdown." />
                    : <DashboardPayrollByEntityChart data={payrollByEntity} />}
                </div>
              </>
            ) : (
              <Empty msg="Create a payroll run to see the snapshot." />
            )}
          </Panel>
        </div>
      </div>

      {/* ── Section: Workforce ───────────────────────────────────────────────── */}
      <div>
        <SectionLabel label="Workforce" icon={Users} />
        <div className="mt-3 grid grid-cols-1 gap-4 xl:grid-cols-12">

          {/* Headcount by department */}
          <Panel title="Headcount by Department" sub="Active employees" className="xl:col-span-4">
            {headcountByDept.length === 0 ? (
              <Empty msg="No active employees yet." />
            ) : (
              <div className="space-y-3">
                {headcountByDept.map((d, i) => (
                  <div key={d.name}>
                    <div className="mb-1.5 flex items-center justify-between">
                      <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-600 dark:text-slate-300">
                        <Building2 className="h-3 w-3 text-slate-400" />
                        {d.name}
                      </span>
                      <span className="font-mono text-xs font-bold text-slate-900 dark:text-white">{d.value}</span>
                    </div>
                    <div className="h-1.5 overflow-hidden rounded-full bg-slate-100 dark:bg-white/[0.06]">
                      {/* eslint-disable-next-line */}
                      <div
                        className={`h-full rounded-full transition-all duration-700 ${MIX_BAR_CLS[i % MIX_BAR_CLS.length]}`}
                        style={{ width: `${(d.value / deptMax) * 100}%` }}
                      />
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Panel>

          {/* Workforce mix */}
          <Panel title="Workforce Mix" sub="By employment type" className="xl:col-span-4">
            {workforceMix.length === 0 ? (
              <Empty msg="No active employees to chart." />
            ) : (
              <div className="flex items-center gap-4">
                <div className="h-[150px] w-[150px] shrink-0">
                  <DashboardWorkforceMixChart data={workforceMix} />
                </div>
                <div className="flex-1 space-y-2">
                  {workforceMix.slice(0, 5).map((item, i) => (
                    <div key={item.name} className="flex items-center justify-between text-xs">
                      <span className="flex items-center gap-2 font-medium text-slate-600 dark:text-slate-300">
                        <span className={`h-2 w-2 rounded-full ${MIX_DOT_CLS[i % MIX_DOT_CLS.length]}`} />
                        {item.name}
                      </span>
                      <span className="font-mono font-bold text-slate-900 dark:text-white">
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
          </Panel>

          {/* AI insights */}
          <Panel
            title="AI Workforce Insights"
            sub="Advisory signals from the AI engine"
            className="xl:col-span-4"
            action={
              <span className="flex items-center gap-1 rounded-full bg-blue-500/10 px-2 py-0.5 text-[10px] font-bold text-blue-600 dark:text-blue-400">
                <Sparkles className="h-3 w-3" /> AI
              </span>
            }
          >
            <div className="space-y-2">
              {insights.length === 0 ? (
                <Empty msg="No active AI insights." />
              ) : (
                insights.slice(0, 3).map((ins) => (
                  <InsightRow
                    key={ins.id ?? ins.title}
                    insight={{ title: ins.title, body: ins.summary, severity: sevToTone[ins.severity] ?? 'info' }}
                  />
                ))
              )}
            </div>
            <button
              type="button"
              onClick={() => router.push('/ai-assistant')}
              className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-xl border border-slate-200 bg-slate-50 py-2 text-xs font-semibold text-slate-700 transition hover:border-blue-500/30 hover:bg-blue-500/[0.04] hover:text-blue-600 dark:border-white/[0.07] dark:bg-white/[0.03] dark:text-slate-300 dark:hover:text-blue-400"
            >
              <Bot className="h-3.5 w-3.5" />
              Open AI Assistant
            </button>
          </Panel>
        </div>
      </div>

      {/* ── Section: Compliance & Actions ──────────────────────────────────── */}
      <div>
        <SectionLabel label="Compliance & Quick Actions" icon={ShieldAlert} />
        <div className="mt-3 grid grid-cols-1 gap-4 xl:grid-cols-12">

          {/* Compliance alerts */}
          <Panel
            title="Alerts & Anomalies"
            sub="Documents nearing or past expiry"
            className="xl:col-span-8"
            action={
              <button type="button" onClick={() => router.push('/compliance')} className="flex items-center gap-1 text-xs font-semibold text-blue-600 hover:underline dark:text-blue-400">
                View all <ArrowRight className="h-3 w-3" />
              </button>
            }
          >
            {alerts.length === 0 ? (
              <Empty msg="No compliance alerts — all documents are in order." />
            ) : (
              <div className="grid gap-2 sm:grid-cols-2">
                {alerts.slice(0, 6).map((item) => {
                  const sev = item.severity;
                  const cfg = {
                    Critical: { icon: ShieldAlert,   cls: 'text-rose-500',  bg: 'bg-rose-500/10',  border: 'border-rose-200 dark:border-rose-500/20' },
                    Warning:  { icon: AlertTriangle, cls: 'text-amber-500', bg: 'bg-amber-500/10', border: 'border-amber-200 dark:border-amber-500/20' },
                    Info:     { icon: Info,          cls: 'text-blue-500',  bg: 'bg-blue-500/10',  border: 'border-blue-200 dark:border-blue-500/20' },
                  } as const;
                  const c = cfg[sev as keyof typeof cfg] ?? cfg.Info;
                  const SevIcon = c.icon;
                  return (
                    <div key={item.title} className={`flex items-center gap-3 rounded-xl border p-3 ${c.border}`}>
                      <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-lg ${c.bg}`}>
                        <SevIcon className={`h-3.5 w-3.5 ${c.cls}`} />
                      </span>
                      <span className="truncate text-xs font-semibold text-slate-700 dark:text-slate-200">{item.title}</span>
                    </div>
                  );
                })}
              </div>
            )}
          </Panel>

          {/* Quick actions */}
          <Panel title="Quick Actions" sub="Frequent workflows" className="xl:col-span-4">
            <div className="grid grid-cols-2 gap-2">
              {quickActions.map((a) => {
                const Icon = a.icon;
                return (
                  <button
                    key={a.label}
                    type="button"
                    onClick={() => router.push(a.to)}
                    className="group flex flex-col gap-2.5 rounded-xl border border-slate-100 bg-slate-50/80 p-3.5 text-left transition hover:-translate-y-0.5 hover:shadow-md dark:border-white/[0.07] dark:bg-white/[0.03] dark:hover:bg-white/[0.06]"
                  >
                    <span className={`flex h-8 w-8 items-center justify-center rounded-xl border ${a.cls}`}>
                      <Icon className="h-4 w-4" />
                    </span>
                    <span>
                      <span className="block text-xs font-bold text-slate-900 group-hover:text-blue-600 dark:text-white dark:group-hover:text-blue-400 transition-colors">{a.label}</span>
                      <span className="block text-[10px] text-slate-500 dark:text-slate-400">{a.desc}</span>
                    </span>
                  </button>
                );
              })}
            </div>

            {/* Doc summary footer */}
            {opsKpis && (
              <div className="mt-4 grid grid-cols-3 gap-2 border-t border-slate-100 pt-4 dark:border-white/[0.06]">
                {[
                  { label: 'Missing', value: opsKpis.missingDocuments,  icon: FileWarning, cls: 'text-rose-500'  },
                  { label: 'Expiring', value: opsKpis.expiringDocuments, icon: AlertTriangle, cls: 'text-amber-500' },
                  { label: 'Expired', value: opsKpis.expiredDocuments,   icon: ShieldAlert, cls: 'text-rose-600'  },
                ].map(({ label, value, icon: Ic, cls }) => (
                  <button
                    key={label}
                    type="button"
                    onClick={() => router.push('/compliance')}
                    className="flex flex-col items-center gap-1 rounded-xl py-2 text-center transition hover:bg-slate-50 dark:hover:bg-white/[0.04]"
                  >
                    <Ic className={`h-4 w-4 ${value > 0 ? cls : 'text-slate-300 dark:text-slate-700'}`} />
                    <span className={`font-mono text-lg font-extrabold leading-none ${value > 0 ? cls : 'text-slate-300 dark:text-slate-700'}`}>{value}</span>
                    <span className="text-[10px] font-medium text-slate-400 dark:text-slate-600">{label}</span>
                  </button>
                ))}
              </div>
            )}
          </Panel>

        </div>
      </div>

    </div>
  );
}
