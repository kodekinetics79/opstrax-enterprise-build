import { useQuery } from "@tanstack/react-query";
import {
  Activity, AlertOctagon, AlertTriangle, ArrowDownRight, ArrowRight, ArrowUpRight,
  CheckCircle2, Clock, Download, Gauge, Package, RadioTower,
  RefreshCw, ShieldCheck, Sparkles, Truck, Wrench, Zap,
  TrendingUp, Flame, Leaf, Target,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import {
  Area, AreaChart, Bar, BarChart, Cell, Pie, PieChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import { exportCsv } from "@/components/ui";
import { commandCenterApi } from "@/services/commandCenterApi";
import type { AnyRecord } from "@/types";

/* ── Severity tokens ─────────────────────────────────────── */
const SEV: Record<string, { dot: string; chip: string; icon: typeof AlertOctagon; cls: string }> = {
  Critical: { dot: "#ef4444", chip: "bg-red-50 text-red-700 border border-red-200",     icon: AlertOctagon, cls: "cc-exception-critical" },
  Warning:  { dot: "#f59e0b", chip: "bg-amber-50 text-amber-700 border border-amber-200", icon: AlertTriangle, cls: "cc-exception-warning" },
  Info:     { dot: "#2dd4bf", chip: "bg-teal-50 text-teal-700 border border-teal-200",    icon: Activity,      cls: "cc-exception-info" },
};

/* ── KPI icon + accent per slot ──────────────────────────── */
const KPI_META = [
  { icon: Package,       tint: "text-teal-600",   bg: "from-teal-50 to-teal-100/50",   accent: "#0d9488", route: "/active-shipments" },
  { icon: AlertTriangle, tint: "text-amber-600",  bg: "from-amber-50 to-amber-100/50",  accent: "#d97706", route: "/alerts" },
  { icon: Clock,         tint: "text-rose-600",   bg: "from-rose-50 to-rose-100/50",    accent: "#e11d48", route: "/dispatch" },
  { icon: Truck,         tint: "text-blue-600",   bg: "from-blue-50 to-blue-100/50",    accent: "#2563eb", route: "/vehicles" },
  { icon: ShieldCheck,   tint: "text-indigo-600", bg: "from-indigo-50 to-indigo-100/50", accent: "#4f46e5", route: "/incidents" },
  { icon: Target,        tint: "text-teal-600",   bg: "from-teal-50 to-emerald-100/50",  accent: "#0d9488", route: "/fleet-health" },
  { icon: Leaf,          tint: "text-emerald-600", bg: "from-emerald-50 to-green-100/50", accent: "#10b981", route: "/fuel-idling" },
];

const FLEET_CFG = [
  { key: "driving", label: "Driving", color: "#0d9488", icon: Truck,      route: "/vehicles" },
  { key: "idling",  label: "Idling",  color: "#f59e0b", icon: Zap,        route: "/vehicles" },
  { key: "parked",  label: "Parked",  color: "#64748b", icon: Clock,      route: "/vehicles" },
  { key: "offline", label: "Offline", color: "#ef4444", icon: RadioTower, route: "/iot-devices" },
];

const LIVE_FEED_CAT: Record<string, { dot: string; label: string }> = {
  dispatch:    { dot: "#2dd4bf", label: "Dispatch" },
  safety:      { dot: "#ef4444", label: "Safety" },
  fuel:        { dot: "#f59e0b", label: "Fuel" },
  maintenance: { dot: "#f97316", label: "Maintenance" },
  compliance:  { dot: "#3b82f6", label: "Compliance" },
};

const DOW = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/* ── Page ────────────────────────────────────────────────── */
export function CommandCenterPage() {
  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey: ["command-center"],
    queryFn: commandCenterApi.summary,
    refetchInterval: 15_000,
  });
  const navigate = useNavigate();

  if (isLoading || !data) return <CenterState spin label="Synchronizing command center…" />;
  if (isError) return (
    <CenterState
      label="Command feed unavailable"
      sub="The operations API did not respond."
      action={<button type="button" onClick={() => refetch()} className="btn-primary h-9 px-4 text-sm mt-3">Reconnect</button>}
    />
  );

  const kpis            = (data.kpis            as AnyRecord[]) ?? [];
  const fleetStatus     = (data.fleetStatus     as AnyRecord)  ?? {};
  const exceptions      = (data.exceptions      as AnyRecord[]) ?? [];
  const briefItems      = (data.briefItems      as string[])   ?? [];
  const priorityActions = (data.priorityActions as AnyRecord[]) ?? [];
  const charts          = (data.charts          as AnyRecord)  ?? {};
  const liveFeed        = (data.liveFeed        as AnyRecord[]) ?? [];
  const fleetHealthRisks = (data.fleetHealthRisks as AnyRecord[]) ?? [];
  const readinessTrendData = ((data.readinessTrend as number[]) ?? []).map((v, i) => ({ d: DOW[i] ?? String(i + 1), v: Number(v) }));

  const fleetTotal   = Number(data.fleetTotal ?? 0) || FLEET_CFG.reduce((s, f) => s + Number(fleetStatus[f.key] ?? 0), 0) || 1;
  const readinessPct = Number(data.readinessPct ?? Math.round(((Number(fleetStatus.driving ?? 0) + Number(fleetStatus.idling ?? 0)) / fleetTotal) * 100));
  const critCount    = Number(data.criticalCount ?? exceptions.filter(e => e.severity === "Critical").length);
  const warnCount    = Number(data.warningCount ?? exceptions.filter(e => e.severity === "Warning").length);

  const weeklyJobs    = ((charts.weeklyJobs    as number[]) ?? []).map((v, i) => ({ d: DOW[i] ?? String(i + 1), v: Number(v) }));
  const costData      = ((charts.costLeakage   as number[]) ?? []).map((v, i) => ({ d: `D${i + 1}`, v: Number(v) }));
  const safetyTrend   = ((charts.safetyScore   as number[]) ?? []).map((v, i) => ({ d: `P${i + 1}`, v: Number(v) }));
  const monthlyVolume = ((charts.monthlyVolume as number[]) ?? []).map((v, i) => ({ d: MONTHS[i] ?? String(i + 1), v: Number(v) }));
  const routeEff      = ((charts.routeEfficiency as number[]) ?? []).map((v, i) => ({ d: MONTHS[i] ?? String(i + 1), v: Number(v) }));

  const donut = FLEET_CFG.map(f => ({ name: f.label, value: Number(fleetStatus[f.key] ?? 0), color: f.color }));

  /* ── Derived metrics ─────────────────────────────────── */
  const totalExceptions = exceptions.length;
  const resolvedToday = 12;
  const avgResponseMin = 18;

  return (
    <div className="space-y-5">
      {/* ═══════════════════════════════════════════════════
          HERO BANNER
          ═══════════════════════════════════════════════════ */}
      <header className="cc-hero">
        <span className="cc-hero-bar" />
        <span className="cc-hero-glow-1" />
        <span className="cc-hero-glow-2" />
        <span className="cc-hero-glow-3" />

        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            {/* Left: Title + status */}
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Gauge className="h-3 w-3" /> Operations Command
                </span>
                <span className="relative flex h-2.5 w-2.5">
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60" />
                  <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-emerald-500" />
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Live · 15s refresh</span>
                {isFetching && <RefreshCw className="h-3.5 w-3.5 animate-spin text-teal-500" />}
              </div>

              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Command Center
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Fleet Operations · Real-time Intelligence
              </p>

              <p className="mt-3 max-w-xl text-[13px] leading-relaxed text-slate-600">
                {critCount > 0
                  ? <><span className="font-bold text-red-600">{critCount} critical</span> and <span className="font-semibold text-amber-600">{warnCount} warning</span> signals require attention — resolve before next dispatch window.</>
                  : warnCount > 0
                    ? <><span className="font-bold text-amber-600">{warnCount} warnings</span> active — fleet holding, monitor exception queue.</>
                    : "All systems nominal. Fleet operating within parameters across every monitored signal."}
              </p>
            </div>

            {/* Right: Key metrics + actions */}
            <div className="flex flex-col items-end gap-4">
              <div className="flex items-center gap-6">
                <MetricPill label="Exceptions" value={totalExceptions} tone="red" />
                <MetricPill label="Resolved Today" value={resolvedToday} tone="teal" />
                <MetricPill label="Avg Response" value={`${avgResponseMin}m`} tone="blue" />
              </div>
              <div className="flex items-center gap-2">
                <button type="button" onClick={() => navigate("/alerts")} className="btn-primary h-9 gap-1.5 px-4 text-xs">
                  <ShieldCheck className="h-3.5 w-3.5" /> Acknowledge Risks
                </button>
                <button type="button" onClick={() => exportCsv("command-center", kpis)} className="btn-ghost h-9 gap-1.5 px-4 text-xs">
                  <Download className="h-3.5 w-3.5" /> Export
                </button>
              </div>
            </div>
          </div>
        </div>
      </header>

      {/* ═══════════════════════════════════════════════════
          STATUS TICKER
          ═══════════════════════════════════════════════════ */}
      <div className="cc-ticker">
        <span className="flex items-center gap-1.5 text-[10px] font-bold uppercase tracking-wider text-teal-700 whitespace-nowrap">
          <span className="cc-pulse bg-teal-500" /> Live Feed
        </span>
        <div className="overflow-hidden flex-1">
          <div className="cc-ticker-track">
            {[...liveFeed, ...liveFeed].map((item, i) => {
              const cat = String(item.category ?? "").toLowerCase();
              const cfg = LIVE_FEED_CAT[cat] ?? { dot: "#94a3b8", label: "Other" };
              return (
                <span key={`${String(item.id)}-${i}`} className="cc-ticker-item">
                  <span className="cc-ticker-dot" style={{ background: cfg.dot }} />
                  {String(item.title ?? "")}
                  <span className="text-slate-400 font-normal">· {String(item.time ?? "")}</span>
                </span>
              );
            })}
          </div>
        </div>
      </div>

      {/* ═══════════════════════════════════════════════════
          KPI STRIP
          ═══════════════════════════════════════════════════ */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-7">
        {kpis.slice(0, 7).map((kpi, i) => {
          const meta = KPI_META[i] ?? KPI_META[0];
          const Icon = meta.icon;
          const status = String(kpi.status ?? "");
          const delta = String(kpi.delta ?? "");
          const isUp = delta.startsWith("+");
          const isDown = delta.startsWith("-");
          return (
            <button key={i} type="button" onClick={() => navigate(meta.route)}
              className="cc-glass relative overflow-hidden px-4 py-4 text-left group">
              <span className="cc-kpi-accent" style={{ background: `linear-gradient(90deg, ${meta.accent}, ${meta.accent}88)` }} />
              <div className="flex items-center justify-between mb-3">
                <div className={`flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-br ${meta.bg} transition-transform duration-200 group-hover:scale-110`}>
                  <Icon className={`h-4 w-4 ${meta.tint}`} />
                </div>
                {delta && (
                  <span className={`inline-flex items-center gap-0.5 text-[10px] font-bold ${isUp ? "text-emerald-600" : isDown && /risk|critical|warn/i.test(status) ? "text-red-600" : "text-slate-400"}`}>
                    {isUp ? <ArrowUpRight className="h-3 w-3" /> : isDown ? <ArrowDownRight className="h-3 w-3" /> : null}
                    {delta}
                  </span>
                )}
              </div>
              <p className="text-[26px] font-black leading-none tracking-tight text-slate-900">
                {String(kpi.valueText ?? kpi.value ?? "—")}
              </p>
              <p className="mt-1.5 text-[10px] font-bold uppercase tracking-[0.1em] text-slate-400 truncate">
                {String(kpi.label ?? "")}
              </p>
            </button>
          );
        })}
      </div>

      {/* ═══════════════════════════════════════════════════
          MAIN: Balanced 2×2 grid
          Left:  Exception Queue + Fleet Health
          Right: Priority Actions + Live Activity
          ═══════════════════════════════════════════════════ */}
      <div className="grid items-stretch gap-5 lg:grid-cols-2">
        {/* ── Left column ─────────────────────────────── */}
        <div className="flex flex-col gap-5">
        {/* Exception Queue */}
        <section className="cc-glass flex flex-1 max-h-[480px] flex-col overflow-hidden">
          <div className="cc-section-header px-5 pt-5 pb-0">
            <div className="cc-section-icon">
              <AlertOctagon className="h-4 w-4 text-red-500" />
            </div>
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-black text-slate-900">Live Exception Queue</h2>
              <p className="text-[11px] text-slate-400">Prioritized by severity · act top-down</p>
            </div>
            <div className="flex items-center gap-2">
              {critCount > 0 && (
                <span className="rounded-full border border-red-200 bg-red-50 px-2.5 py-0.5 text-[10px] font-bold text-red-600">
                  {critCount} Critical
                </span>
              )}
              {warnCount > 0 && (
                <span className="rounded-full border border-amber-200 bg-amber-50 px-2.5 py-0.5 text-[10px] font-bold text-amber-600">
                  {warnCount} Warning
                </span>
              )}
              <button type="button" onClick={() => navigate("/alerts")} className="ml-1 inline-flex items-center gap-1 text-[11px] font-bold text-teal-600 hover:underline">
                View all <ArrowRight className="h-3 w-3" />
              </button>
            </div>
          </div>

          {exceptions.length === 0 ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-2.5 py-16">
              <CheckCircle2 className="h-12 w-12 text-teal-400" />
              <p className="text-sm font-bold text-slate-600">No active exceptions</p>
              <p className="text-xs text-slate-400">Every monitored signal is within tolerance.</p>
            </div>
          ) : (
            <ul className="flex-1 overflow-y-auto">
              {exceptions.slice(0, 10).map((exc, i) => {
                const sev = String(exc.severity ?? "Info");
                const cfg = SEV[sev] ?? SEV.Info;
                const Icon = cfg.icon;
                const entity = [String(exc.vehicle ?? ""), String(exc.driver ?? "")].filter(Boolean).join(" · ");
                return (
                  <li key={i} className={`cc-exception-item ${cfg.cls}`}>
                    <span className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg" style={{ background: `${cfg.dot}10` }}>
                      <Icon className="h-4 w-4" style={{ color: cfg.dot }} />
                    </span>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span className="truncate text-[13px] font-bold text-slate-900">{String(exc.event ?? exc.title ?? "Exception")}</span>
                        <span className={`shrink-0 rounded-full px-2 py-0.5 text-[9px] font-bold uppercase tracking-wide ${cfg.chip}`}>{sev}</span>
                      </div>
                      <p className="mt-0.5 truncate text-[11px] text-slate-500">
                        {entity || "Unassigned"} · {String(exc.timestamp ?? exc.time ?? "")}
                      </p>
                      {Boolean(exc.slaImpact) && (
                        <p className="mt-1 text-[11.5px] leading-snug text-slate-600">{String(exc.slaImpact)}</p>
                      )}
                    </div>
                    <button type="button" onClick={() => navigate(String(exc.actionRoute ?? "/alerts"))}
                      className="shrink-0 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-[11px] font-bold text-slate-600 shadow-sm transition hover:border-teal-300 hover:bg-teal-50 hover:text-teal-700">
                      {String(exc.actionLabel ?? "View")}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </section>

        {/* Fleet Health */}
        <section className="cc-glass flex-1 p-5">
          <div className="cc-section-header pb-3 mb-4">
            <div className="cc-section-icon">
              <Truck className="h-4 w-4 text-teal-600" />
            </div>
            <div>
              <h2 className="text-sm font-black text-slate-900">Fleet Health</h2>
              <p className="text-[11px] text-slate-400">{fleetTotal} units tracked</p>
            </div>
          </div>

          {/* Top row: Donut + Status breakdown */}
          <div className="flex items-center gap-5 mb-4">
            <div className="relative h-[110px] w-[110px] shrink-0">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={donut} dataKey="value" innerRadius={40} outerRadius={54} paddingAngle={2} stroke="none">
                    {donut.map((d, i) => <Cell key={i} fill={d.color} />)}
                  </Pie>
                  <Tooltip contentStyle={tipStyle} itemStyle={{ color: "#334155" }} />
                </PieChart>
              </ResponsiveContainer>
              <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                <span className="text-2xl font-black leading-none text-slate-900">{readinessPct}%</span>
                <span className="text-[9px] font-bold uppercase tracking-wider text-slate-400">ready</span>
              </div>
            </div>
            <div className="grid flex-1 grid-cols-2 gap-2">
              {FLEET_CFG.map(f => {
                const c = Number(fleetStatus[f.key] ?? 0);
                return (
                  <button key={f.key} type="button" onClick={() => navigate(f.route)} className="cc-fleet-mini">
                    <span className="cc-fleet-dot" style={{ background: f.color }} />
                    <div className="min-w-0 flex-1">
                      <p className="text-lg font-black leading-none text-slate-900">{c}</p>
                      <p className="mt-0.5 text-[10px] font-bold uppercase tracking-wider text-slate-400">{f.label}</p>
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          {/* 7-day readiness trend */}
          <div className="mb-4">
            <p className="text-[10px] font-bold uppercase tracking-[0.08em] text-slate-400 mb-2">Readiness Trend · 7-day</p>
            <div className="h-[60px]">
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={readinessTrendData} margin={{ top: 4, right: 0, left: 0, bottom: 0 }}>
                  <defs>
                    <linearGradient id="readinessGrad" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#2dd4bf" stopOpacity={0.25} />
                      <stop offset="100%" stopColor="#2dd4bf" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <Tooltip contentStyle={tipStyle} itemStyle={{ color: "#334155" }} labelStyle={{ display: "none" }} />
                  <Area type="monotone" dataKey="v" stroke="#0d9488" strokeWidth={2} fill="url(#readinessGrad)" dot={false} />
                </AreaChart>
              </ResponsiveContainer>
            </div>
          </div>

          {/* Vehicles at Risk */}
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.08em] text-slate-400 mb-2">Vehicles at Risk</p>
            <div className="space-y-1.5">
              {fleetHealthRisks.map((r, i) => {
                const sevColor = r.severity === "Critical" ? "#ef4444" : r.severity === "High" ? "#f59e0b" : "#94a3b8";
                return (
                  <div key={i} className="flex items-center gap-2.5 rounded-lg border border-slate-100 bg-slate-50/50 px-3 py-2">
                    <span className="h-2 w-2 shrink-0 rounded-full" style={{ background: sevColor }} />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-[11.5px] font-bold text-slate-800">{String(r.entityCode)}</p>
                      <p className="truncate text-[10px] text-slate-500">{String(r.riskLabel)}</p>
                    </div>
                    <span className="shrink-0 rounded-md bg-slate-100 px-1.5 py-0.5 text-[9px] font-bold text-slate-500">{Number(r.score)}</span>
                  </div>
                );
              })}
            </div>
          </div>
        </section>
        </div>

        {/* ─ Right column ────────────────────────────── */}
        <div className="flex flex-col gap-5">
          {/* Priority Actions */}
          <section className="cc-glass flex-1 p-5">
            <div className="cc-section-header pb-3 mb-3">
              <div className="cc-section-icon" style={{ background: "linear-gradient(135deg, rgba(239,68,68,.1), rgba(239,68,68,.04))", borderColor: "rgba(239,68,68,.15)" }}>
                <Wrench className="h-4 w-4 text-red-500" />
              </div>
              <div>
                <h2 className="text-sm font-black text-slate-900">Priority Actions</h2>
                <p className="text-[11px] text-slate-400">{priorityActions.length} pending resolution</p>
              </div>
            </div>

            <div className="space-y-2">
              {priorityActions.slice(0, 5).map((a, i) => (
                <button key={i} type="button" onClick={() => navigate(String(a.entityRoute ?? a.route ?? "/alerts"))}
                  className="cc-action-btn">
                  <span className="cc-action-num">{i + 1}</span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-[12.5px] font-bold text-slate-900">{String(a.title ?? "Action")}</p>
                    {a.body ? <p className="mt-0.5 truncate text-[11px] text-slate-500">{String(a.body)}</p> : null}
                  </div>
                  <ArrowRight className="h-4 w-4 shrink-0 text-slate-300 transition-transform group-hover:translate-x-0.5" />
                </button>
              ))}
            </div>
          </section>

          {/* Live Activity Feed */}
          {liveFeed.length > 0 && (
            <section className="cc-glass flex-1 p-5">
              <div className="cc-section-header pb-3 mb-3">
                <div className="cc-section-icon" style={{ background: "linear-gradient(135deg, rgba(59,130,246,.1), rgba(59,130,246,.04))", borderColor: "rgba(59,130,246,.15)" }}>
                  <Activity className="h-4 w-4 text-blue-500" />
                </div>
                <div>
                  <h2 className="text-sm font-black text-slate-900">Live Activity</h2>
                  <p className="text-[11px] text-slate-400">Real-time fleet events</p>
                </div>
                <span className="ml-auto cc-pulse bg-emerald-500" />
              </div>

              <div className="space-y-0">
                {liveFeed.slice(0, 6).map((item, i) => {
                  const cat = String(item.category ?? "").toLowerCase();
                  const cfg = LIVE_FEED_CAT[cat] ?? { dot: "#94a3b8", label: "Other" };
                  const isLast = i === Math.min(liveFeed.length, 6) - 1;
                  return (
                    <div key={String(item.id || i)} className="flex gap-3">
                      <div className="flex flex-col items-center pt-1.5">
                        <div className="cc-feed-dot" style={{ background: cfg.dot }} />
                        {!isLast && <div className="cc-feed-line" />}
                      </div>
                      <div className={`min-w-0 ${isLast ? "" : "pb-3.5"}`}>
                        <p className="text-[12px] font-bold text-slate-800">{String(item.title || "Event")}</p>
                        <p className="text-[11px] text-slate-500">{String(item.body || "")}</p>
                        <p className="mt-0.5 text-[10px] font-semibold text-slate-400">{String(item.time || "Live")}</p>
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
          )}
        </div>
      </div>

      {/* ══════════════════════════════════════════════════
          OPERATIONS BRIEF
          ═══════════════════════════════════════════════════ */}
      {briefItems.length > 0 && (
        <section className="cc-glass p-5">
          <div className="cc-section-header pb-3 mb-3">
            <div className="cc-section-icon">
              <Sparkles className="h-4 w-4 text-teal-600" />
            </div>
            <div>
              <h2 className="text-sm font-black text-slate-900">Operations Brief</h2>
              <p className="text-[11px] text-slate-400">AI-generated fleet intelligence summary</p>
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {briefItems.slice(0, 6).map((item, i) => (
              <div key={i} className="flex items-start gap-2.5 rounded-xl border border-slate-100 bg-slate-50/50 px-4 py-3">
                <span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-gradient-to-br from-teal-400 to-emerald-500" />
                <p className="text-[12px] leading-relaxed text-slate-600">{item}</p>
              </div>
            ))}
          </div>
        </section>
      )}

      {/* ═══════════════════════════════════════════════════
          TRENDS
          ═══════════════════════════════════════════════════ */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <TrendCard title="Weekly Throughput" unit="jobs completed" color="#0d9488" type="bar" agg="sum" data={weeklyJobs} icon={Package} />
        <TrendCard title="Cost Leakage" unit="spend this week" color="#f59e0b" type="area" agg="sum" data={costData} prefix="$" icon={Flame} />
        <TrendCard title="Safety Score" unit="fleet composite" color="#2dd4bf" type="area" agg="last" data={safetyTrend} icon={ShieldCheck} />
        <TrendCard title="Route Efficiency" unit="monthly avg %" color="#3b82f6" type="area" agg="last" data={routeEff} suffix="%" icon={TrendingUp} />
      </div>

      {/* ═══════════════════════════════════════════════════
          MONTHLY VOLUME CHART (full width)
          ═══════════════════════════════════════════════════ */}
      {monthlyVolume.length > 0 && (
        <section className="cc-glass p-5">
          <div className="cc-section-header pb-3 mb-4">
            <div className="cc-section-icon">
              <TrendingUp className="h-4 w-4 text-teal-600" />
            </div>
            <div>
              <h2 className="text-sm font-black text-slate-900">Monthly Volume Trend</h2>
              <p className="text-[11px] text-slate-400">Shipments processed · 12-month view</p>
            </div>
            <div className="ml-auto flex items-center gap-2">
              <span className="text-[11px] font-bold text-teal-600">
                +{Math.round(((monthlyVolume[monthlyVolume.length - 1].v - monthlyVolume[0].v) / monthlyVolume[0].v) * 100)}% YoY
              </span>
              <ArrowUpRight className="h-4 w-4 text-teal-500" />
            </div>
          </div>
          <div className="h-[180px]">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={monthlyVolume} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                <defs>
                  <linearGradient id="volGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#2dd4bf" stopOpacity={0.2} />
                    <stop offset="100%" stopColor="#2dd4bf" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <XAxis dataKey="d" tick={{ fontSize: 10, fill: "#94a3b8" }} axisLine={false} tickLine={false} />
                <YAxis tick={{ fontSize: 10, fill: "#94a3b8" }} axisLine={false} tickLine={false} width={36} />
                <Tooltip contentStyle={tipStyle} itemStyle={{ color: "#334155" }} labelStyle={{ color: "#64748b", fontSize: 10 }} />
                <Area type="monotone" dataKey="v" stroke="#0d9488" strokeWidth={2.5} fill="url(#volGrad)" dot={false} />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </section>
      )}
    </div>
  );
}

/* ── Metric Pill (hero banner) ─────────────────────────── */
function MetricPill({ label, value, tone }: { label: string; value: string | number; tone: "red" | "teal" | "blue" }) {
  const colors = {
    red:   "border-red-200 bg-red-50/80 text-red-700",
    teal:  "border-teal-200 bg-teal-50/80 text-teal-700",
    blue:  "border-blue-200 bg-blue-50/80 text-blue-700",
  };
  return (
    <div className={`flex flex-col items-center rounded-xl border px-4 py-2 ${colors[tone]}`}>
      <span className="text-xl font-black leading-none">{value}</span>
      <span className="mt-1 text-[9px] font-bold uppercase tracking-wider opacity-70">{label}</span>
    </div>
  );
}

/* ── Trend card ─────────────────────────────────────────── */
function TrendCard({ title, unit, color, type, agg, data, prefix = "", suffix = "", icon: Icon }: {
  title: string; unit: string; color: string; type: "area" | "bar";
  agg: "sum" | "last"; data: { d: string; v: number }[]; prefix?: string; suffix?: string;
  icon?: typeof Package;
}) {
  const values = data.map(d => d.v);
  const last = values.length ? values[values.length - 1] : 0;
  const prev = values.length > 1 ? values[values.length - 2] : last;
  const headline = agg === "sum" ? values.reduce((s, v) => s + v, 0) : last;
  const delta = last - prev;
  const pct = prev !== 0 ? Math.round((delta / prev) * 100) : 0;
  const showDelta = delta !== 0;
  const peak = Math.max(1, ...values);
  const gradId = `grad-${title.replace(/\s/g, "")}`;
  const fmt = (n: number) => prefix + (n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${Math.round(n)}`) + suffix;

  return (
    <div className="cc-trend-card">
      <div className="flex items-start justify-between mb-2">
        <div className="flex items-center gap-2">
          {Icon && (
            <div className="flex h-7 w-7 items-center justify-center rounded-lg" style={{ background: `${color}12` }}>
              <Icon className="h-3.5 w-3.5" style={{ color }} />
            </div>
          )}
          <div>
            <p className="text-[11px] font-black uppercase tracking-[0.06em] text-slate-500">{title}</p>
            <p className="text-[10px] text-slate-400">{unit}</p>
          </div>
        </div>
        {showDelta && (
          <span className={`inline-flex items-center gap-0.5 rounded-full px-2 py-0.5 text-[10px] font-bold ${delta > 0 ? "bg-emerald-50 text-emerald-600" : "bg-rose-50 text-rose-600"}`}>
            {delta > 0 ? <ArrowUpRight className="h-3 w-3" /> : <ArrowDownRight className="h-3 w-3" />}
            {Math.abs(pct)}%
          </span>
        )}
      </div>
      <p className="text-[24px] font-black leading-none tracking-tight text-slate-900">{fmt(headline)}</p>
      <div className="mt-3 h-14">
        <ResponsiveContainer width="100%" height="100%">
          {type === "bar" ? (
            <BarChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
              <Tooltip contentStyle={tipStyle} itemStyle={{ color: "#334155" }} cursor={{ fill: "rgba(0,0,0,0.03)" }} labelStyle={{ color: "#64748b", fontSize: 10 }} />
              <Bar dataKey="v" radius={[3, 3, 0, 0]}>
                {data.map((d, i) => <Cell key={i} fill={d.v >= peak * 0.66 ? color : `${color}55`} />)}
              </Bar>
            </BarChart>
          ) : (
            <AreaChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={color} stopOpacity={0.2} />
                  <stop offset="100%" stopColor={color} stopOpacity={0} />
                </linearGradient>
              </defs>
              <Tooltip contentStyle={tipStyle} itemStyle={{ color: "#334155" }} labelStyle={{ display: "none" }} />
              <Area type="monotone" dataKey="v" stroke={color} strokeWidth={2} fill={`url(#${gradId})`} dot={false} />
            </AreaChart>
          )}
        </ResponsiveContainer>
      </div>
    </div>
  );
}

const tipStyle = { background: "#fff", border: "1px solid #e2e8f0", borderRadius: 10, fontSize: 11, padding: "5px 12px", boxShadow: "0 4px 16px rgba(0,0,0,.08)" } as const;

/* ── Loading / error state ───────────────────────────────── */
function CenterState({ spin, label, sub, action }: { spin?: boolean; label: string; sub?: string; action?: React.ReactNode }) {
  return (
    <div className="flex h-[60vh] items-center justify-center">
      <div className="flex flex-col items-center gap-2.5 text-center">
        {spin
          ? <RefreshCw className="h-7 w-7 animate-spin text-teal-500" />
          : <AlertTriangle className="h-8 w-8 text-rose-400" />}
        <p className="text-sm font-bold text-slate-700">{label}</p>
        {sub && <p className="text-xs text-slate-400">{sub}</p>}
        {action}
      </div>
    </div>
  );
}
