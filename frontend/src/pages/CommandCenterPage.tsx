import { useQuery } from "@tanstack/react-query";
import { tokens, chart } from "@/styles/tokens";
import {
  Activity, AlertOctagon, AlertTriangle, ArrowDownRight, ArrowRight, ArrowUpRight,
  CheckCircle2, Clock, Download, Gauge, Package, RadioTower,
  RefreshCw, ShieldCheck, Sparkles, Truck, Wrench, Zap, type LucideIcon,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import {
  Area, AreaChart, Bar, BarChart, Cell, Pie, PieChart,
  ResponsiveContainer, Tooltip,
} from "recharts";
import { exportCsv } from "@/components/ui";
import { commandCenterApi } from "@/services/commandCenterApi";
import { fleetHealthApi } from "@/services/fleetHealthApi";
import { maintenanceApi } from "@/services/maintenanceApi";
import { safetyApi } from "@/services/safetyApi";
import type { AnyRecord } from "@/types";

/* ── Severity tokens ─────────────────────────────────────── */
const SEV: Record<string, { dot: string; ring: string; chip: string; bar: string; icon: typeof AlertOctagon }> = {
  Critical: { dot: chart.red500, ring: "ring-red-200",    chip: "bg-red-50 text-red-700 border-red-200",       bar: "bg-red-500",   icon: AlertOctagon },
  Warning:  { dot: chart.amber500, ring: "ring-amber-200",  chip: "bg-amber-50 text-amber-700 border-amber-200",  bar: "bg-amber-400", icon: AlertTriangle },
  Info:     { dot: chart.blue500, ring: "ring-blue-200",   chip: "bg-blue-50 text-blue-700 border-blue-200",     bar: "bg-blue-400",  icon: Activity },
};

/* ── KPI presentation (icon + accent per slot) ───────────── */
const KPI_META = [
  { icon: Package,       tint: "text-teal-600",   ring: "bg-teal-50",   accent: chart.teal600, route: "/active-shipments" },
  { icon: AlertTriangle, tint: "text-amber-600",  ring: "bg-amber-50",  accent: chart.amber600, route: "/alerts" },
  { icon: Clock,         tint: "text-rose-600",   ring: "bg-rose-50",   accent: chart.rose600, route: "/dispatch" },
  { icon: Truck,         tint: "text-blue-600",   ring: "bg-blue-50",   accent: chart.blue600, route: "/vehicles" },
  { icon: ShieldCheck,   tint: "text-indigo-600", ring: "bg-indigo-50", accent: chart.indigo600, route: "/incidents" },
];

const FLEET_CFG = [
  { key: "driving", label: "Driving", color: chart.teal600, icon: Truck,     route: "/vehicles" },
  { key: "idling",  label: "Idling",  color: chart.amber500, icon: Zap,       route: "/vehicles" },
  { key: "parked",  label: "Parked",  color: chart.slate500, icon: Clock,     route: "/vehicles" },
  { key: "offline", label: "Offline", color: chart.red500, icon: RadioTower, route: "/iot-devices" },
];

const POSTURE: Record<string, { chip: string; dot: string }> = {
  Elevated: { chip: "border-red-200 bg-red-50 text-red-700",         dot: chart.red500 },
  Guarded:  { chip: "border-amber-200 bg-amber-50 text-amber-700",   dot: chart.amber500 },
  Stable:   { chip: "border-emerald-200 bg-emerald-50 text-emerald-700", dot: chart.emerald500 },
};

const DOW = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

/* ── Page ────────────────────────────────────────────────── */
export function CommandCenterPage() {
  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey: ["command-center"],
    queryFn: commandCenterApi.summary,
    refetchInterval: 15_000,
  });
  const safetyBridge = useQuery<AnyRecord>({
    queryKey: ["command-center", "bridge", "safety"],
    queryFn: safetyApi.dashboard,
    refetchInterval: 60_000,
  });
  const maintenanceBridge = useQuery<AnyRecord>({
    queryKey: ["command-center", "bridge", "maintenance"],
    queryFn: maintenanceApi.dashboard,
    refetchInterval: 60_000,
  });
  const fleetHealthBridge = useQuery<AnyRecord>({
    queryKey: ["command-center", "bridge", "fleet-health"],
    queryFn: fleetHealthApi.summary,
    refetchInterval: 60_000,
  });
  const navigate = useNavigate();

  if (isLoading || !data) return <CenterState spin label="Synchronizing dashboard…" />;
  if (isError) return (
    <CenterState
      label="Dashboard feed unavailable"
      sub="The operations dashboard API did not respond."
      action={<button type="button" onClick={() => refetch()} className="btn-primary h-9 px-4 text-xs mt-3">Reconnect</button>}
    />
  );

  const kpis            = (data.kpis            as AnyRecord[]) ?? [];
  const fleetStatus     = (data.fleetStatus     as AnyRecord)  ?? {};
  const exceptions      = (data.exceptions      as AnyRecord[]) ?? [];
  const briefItems      = (data.briefItems      as string[])   ?? [];
  const priorityActions = (data.priorityActions as AnyRecord[]) ?? [];
  const charts          = (data.charts          as AnyRecord)  ?? {};
  const maintenanceKpis = (maintenanceBridge.data?.kpis as AnyRecord) ?? {};

  const fleetTotal   = Number(data.fleetTotal ?? 0) || FLEET_CFG.reduce((s, f) => s + Number(fleetStatus[f.key] ?? 0), 0) || 1;
  const readinessPct = Number(data.readinessPct ?? Math.round(((Number(fleetStatus.driving ?? 0) + Number(fleetStatus.idling ?? 0)) / fleetTotal) * 100));
  const posture      = String(data.posture ?? "Stable");
  const critCount    = Number(data.criticalCount ?? exceptions.filter(e => e.severity === "Critical").length);
  const warnCount    = Number(data.warningCount ?? exceptions.filter(e => e.severity === "Warning").length);

  const weeklyJobs  = ((charts.weeklyJobs  as number[]) ?? []).map((v, i) => ({ d: DOW[i] ?? String(i + 1), v: Number(v) }));
  const costData    = ((charts.costLeakage as number[]) ?? []).map((v, i) => ({ d: `D${i + 1}`, v: Number(v) }));
  const safetyTrend = ((charts.safetyScore as number[]) ?? []).map((v, i) => ({ d: `P${i + 1}`, v: Number(v) }));

  const donut = FLEET_CFG.map(f => ({ name: f.label, value: Number(fleetStatus[f.key] ?? 0), color: f.color }));
  const postureTone = POSTURE[posture] ?? POSTURE.Stable;

  return (
    <div className="control-tower space-y-4">
      {/* ── Dashboard banner — light glass, brand-tinted ─────── */}
      <header className="relative overflow-hidden rounded-[20px] border border-white/70 px-6 py-4 shadow-sm ring-1 ring-slate-200/70 backdrop-blur-xl"
        style={{ background: "linear-gradient(120deg,#ffffff 0%,#f0fdfa 36%,#eff6ff 68%,#f5f3ff 100%)" }}>
        {/* soft glass blobs for depth */}
        <span className="pointer-events-none absolute -right-10 -top-16 h-48 w-48 rounded-full bg-teal-300/20 blur-3xl" />
        <span className="pointer-events-none absolute -bottom-20 left-1/3 h-44 w-44 rounded-full bg-blue-300/15 blur-3xl" />
        <span className="absolute inset-x-0 top-0 h-[3px]" style={{ background: `linear-gradient(90deg,${chart.teal600},${chart.blue600},${chart.violet600})` }} />
        <div className="relative flex flex-wrap items-center justify-between gap-4">
          <div className="min-w-0">
            <div className="flex items-center gap-2.5">
              <span className="inline-flex items-center gap-1.5 rounded-md bg-white/80 px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.22em] text-teal-700 ring-1 ring-teal-200/70">
                <Gauge className="h-3 w-3" /> Operations
              </span>
              <span className="inline-flex items-center gap-1.5 rounded-md border border-teal-200 bg-teal-50 px-2 py-0.5 text-[10px] font-black uppercase tracking-[0.22em] text-teal-700">
                Local build active
              </span>
              <span className="relative flex h-2 w-2">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
              </span>
              <span className="text-[11px] font-semibold text-slate-500">Live · refreshes every 15s</span>
              {isFetching && <RefreshCw className="h-3 w-3 animate-spin text-teal-500" />}
            </div>
            <h1 className="mt-2 text-2xl font-black tracking-tight text-slate-900 sm:text-[26px]">
              Dashboard
              <span className="ml-2 text-base font-medium text-slate-400">· Fleet Operations</span>
            </h1>
            <p className="mt-1 inline-flex items-center rounded-full border border-slate-200 bg-white/80 px-2.5 py-1 text-[11px] font-semibold text-slate-600 shadow-sm">
              Updated locally for <span className="ml-1 font-black text-teal-700">localhost:10000</span>
            </p>
            <p className="mt-1.5 max-w-2xl text-sm text-slate-600">
              {critCount > 0
                ? <><span className="font-bold text-red-600">{critCount} critical</span> and {warnCount} warning signal{warnCount === 1 ? "" : "s"} on the board — act on the queue before the next dispatch window.</>
                : warnCount > 0
                  ? <><span className="font-bold text-amber-600">{warnCount} warning{warnCount === 1 ? "" : "s"}</span> in play — fleet is holding but watch the exception queue.</>
                  : "Fleet operating within normal parameters across every monitored signal."}
            </p>
          </div>

          <div className="flex flex-col items-end gap-3">
            <span className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-[11px] font-bold uppercase tracking-wide ${postureTone.chip}`}>
              <span className="h-1.5 w-1.5 rounded-full" style={{ background: postureTone.dot }} />
              {posture} posture
            </span>
            <div className="flex items-center gap-2">
              <button type="button" onClick={() => navigate("/alerts")} className="btn-primary h-9 gap-1.5 px-3.5 text-xs">
                <ShieldCheck className="h-3.5 w-3.5" /> Acknowledge Risks
              </button>
              <button type="button" onClick={() => exportCsv("dashboard", kpis)} className="btn-ghost h-9 gap-1.5 px-3.5 text-xs">
                <Download className="h-3.5 w-3.5" /> Export
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* ── KPI strip ──────────────────────────────────────── */}
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
        {kpis.slice(0, 5).map((kpi, i) => {
          const meta = KPI_META[i] ?? KPI_META[0];
          const Icon = meta.icon;
          const status = String(kpi.status ?? "");
          const tone = /risk|critical|warn/i.test(status) ? "text-rose-600 bg-rose-50 border-rose-200"
            : /clear|on track|active/i.test(status) ? "text-emerald-600 bg-emerald-50 border-emerald-200"
            : "text-slate-500 bg-slate-50 border-slate-200";
          return (
            <button key={i} type="button" onClick={() => navigate(meta.route)}
              className="group relative overflow-hidden rounded-2xl border border-slate-200 bg-white px-4 py-3 text-left shadow-sm transition hover:-translate-y-0.5 hover:shadow-md">
              <span className="absolute inset-x-0 top-0 h-1" style={{ background: meta.accent }} />
              <div className="flex items-center justify-between">
                <div className={`flex h-8 w-8 items-center justify-center rounded-lg ${meta.ring}`}>
                  <Icon className={`h-4 w-4 ${meta.tint}`} />
                </div>
                {status && <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${tone}`}>{status}</span>}
              </div>
              <p className="mt-2 text-[26px] font-black leading-none tracking-tight text-slate-900">
                {String(kpi.valueText ?? kpi.value ?? "—")}
              </p>
              <p className="mt-1 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
                {String(kpi.label ?? "")}
              </p>
            </button>
          );
        })}
      </div>

      <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-500">Operational shortcuts</p>
            <p className="mt-1 text-sm text-slate-600">Jump straight into live workflow surfaces without leaving the dashboard.</p>
          </div>
          <span className="text-xs font-semibold text-slate-400">Trips is now a visible first-class route.</span>
        </div>
        <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
          {[
            { label: "Jobs", route: "/jobs" },
            { label: "Trips", route: "/trips" },
            { label: "Dispatch", route: "/dispatch" },
            { label: "Proof Center", route: "/operations/proof-center" },
            { label: "Live Map", route: "/map-view" },
            { label: "Alerts", route: "/alerts" },
          ].map((item) => (
            <button
              key={item.route}
              type="button"
              onClick={() => navigate(item.route)}
              className="flex items-center justify-between rounded-xl border border-slate-200 bg-slate-50/80 px-3 py-2.5 text-left text-sm font-semibold text-slate-700 transition hover:border-teal-200 hover:bg-teal-50"
            >
              <span>{item.label}</span>
              <ArrowRight className="h-4 w-4 text-slate-400" />
            </button>
          ))}
        </div>
      </section>

      {/* ── Main: Exception queue | Fleet+Brief | Priority actions ── */}
      <div className="grid items-stretch gap-4 xl:grid-cols-[1.5fr_1.05fr_1fr]">
        {/* Exception queue (internal scroll, never pushes the page) */}
        <section className="flex max-h-[460px] flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
          <div className="flex items-center gap-2 border-b border-slate-100 px-4 py-3">
            <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-red-50">
              <AlertOctagon className="h-4 w-4 text-red-500" />
            </span>
            <div className="min-w-0">
              <p className="text-sm font-black text-slate-900">Live Exception Queue</p>
              <p className="text-[11px] text-slate-400">Prioritized by severity · act top-down</p>
            </div>
            <div className="ml-auto flex items-center gap-1.5">
              {critCount > 0 && <span className="rounded-full border border-red-200 bg-red-50 px-2 py-0.5 text-[10px] font-bold text-red-600">{critCount} Crit</span>}
              {warnCount > 0 && <span className="rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[10px] font-bold text-amber-600">{warnCount} Warn</span>}
              <button type="button" onClick={() => navigate("/alerts")} className="ml-1 inline-flex items-center gap-0.5 text-[11px] font-bold text-teal-600 hover:underline">
                All <ArrowRight className="h-3 w-3" />
              </button>
            </div>
          </div>

          {exceptions.length === 0 ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-2 py-12">
              <CheckCircle2 className="h-9 w-9 text-emerald-400" />
              <p className="text-sm font-bold text-slate-600">No active exceptions</p>
              <p className="text-xs text-slate-400">Every monitored signal is within tolerance.</p>
            </div>
          ) : (
            <ul className="flex-1 divide-y divide-slate-50 overflow-y-auto">
              {exceptions.slice(0, 12).map((exc, i) => {
                const sev = String(exc.severity ?? "Info");
                const cfg = SEV[sev] ?? SEV.Info;
                const Icon = cfg.icon;
                const entity = [String(exc.vehicle ?? ""), String(exc.driver ?? "")].filter(Boolean).join(" · ");
                return (
                  <li key={i} className="group relative flex items-center gap-2.5 px-4 py-2.5 transition hover:bg-slate-50/70">
                    <span className="absolute left-0 top-0 h-full w-[3px]" style={{ background: cfg.dot }} />
                    <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg" style={{ background: `${cfg.dot}14` }}>
                      <Icon className="h-3.5 w-3.5" style={{ color: cfg.dot }} />
                    </span>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-1.5">
                        <span className="truncate text-[13px] font-bold text-slate-900">{String(exc.event ?? exc.title ?? "Exception")}</span>
                        <span className={`shrink-0 rounded-full border px-1.5 py-px text-[9px] font-bold uppercase ${cfg.chip}`}>{sev}</span>
                      </div>
                      <p className="truncate text-[11px] text-slate-500">
                        {entity || "Unassigned"}<span className="text-slate-300"> · </span>{String(exc.timestamp ?? exc.time ?? "")}
                      </p>
                    </div>
                    <button type="button" onClick={() => navigate(String(exc.actionRoute ?? "/alerts"))}
                      className="shrink-0 rounded-lg border border-slate-200 px-2.5 py-1 text-[11px] font-bold text-slate-600 transition hover:border-teal-300 hover:bg-teal-50 hover:text-teal-700">
                      {String(exc.actionLabel ?? "View")}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </section>

        {/* Fleet status donut + Operations brief */}
        <div className="flex flex-col gap-4">
          <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="flex items-center justify-between">
              <p className="section-title flex items-center gap-1.5 text-slate-500"><Truck className="h-3.5 w-3.5" /> Fleet Status</p>
              <span className="text-[11px] font-bold text-slate-400">{fleetTotal} units</span>
            </div>
            <div className="mt-1 flex items-center gap-3">
              <div className="relative h-[104px] w-[104px] shrink-0">
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie data={donut} dataKey="value" innerRadius={36} outerRadius={50} paddingAngle={2} stroke="none">
                      {donut.map((d, i) => <Cell key={i} fill={d.color} />)}
                    </Pie>
                    <Tooltip contentStyle={tipStyle} itemStyle={{ color: chart.slate700 }} />
                  </PieChart>
                </ResponsiveContainer>
                <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                  <span className="text-xl font-black leading-none text-slate-900">{readinessPct}%</span>
                  <span className="text-[9px] font-bold uppercase tracking-wide text-slate-400">ready</span>
                </div>
              </div>
              <div className="grid flex-1 grid-cols-2 gap-1.5">
                {FLEET_CFG.map(f => {
                  const c = Number(fleetStatus[f.key] ?? 0);
                  return (
                    <button key={f.key} type="button" onClick={() => navigate(f.route)}
                      className="flex items-center gap-2 rounded-lg border border-slate-100 bg-slate-50/60 px-2 py-1.5 text-left transition hover:border-slate-300">
                      <span className="h-2 w-2 rounded-full" style={{ background: f.color }} />
                      <div className="min-w-0">
                        <p className="text-base font-black leading-none text-slate-900">{c}</p>
                        <p className="text-[10px] font-semibold uppercase tracking-wide text-slate-400">{f.label}</p>
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          </section>

          <section className="flex-1 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <p className="section-title flex items-center gap-1.5 text-teal-600"><Sparkles className="h-3.5 w-3.5" /> Operations Brief</p>
            <ul className="mt-2.5 space-y-2">
              {(briefItems.length ? briefItems : ["Fleet operating within normal parameters."]).slice(0, 5).map((item, i) => (
                <li key={i} className="flex items-start gap-2 text-[12.5px] leading-snug text-slate-600">
                  <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-gradient-to-br from-teal-400 to-blue-500" />
                  {item}
                </li>
              ))}
            </ul>
          </section>
        </div>

        {/* Priority actions */}
        <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <p className="section-title flex items-center gap-1.5 text-rose-600"><Wrench className="h-3.5 w-3.5" /> Priority Actions</p>
          <div className="mt-2.5 space-y-2">
            {priorityActions.slice(0, 4).map((a, i) => (
              <button key={i} type="button" onClick={() => navigate(String(a.entityRoute ?? a.route ?? "/alerts"))}
                className="flex w-full items-center gap-2.5 rounded-xl border border-slate-100 bg-slate-50/60 px-3 py-2 text-left transition hover:border-teal-300 hover:bg-teal-50/50">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-lg bg-white text-[11px] font-black text-slate-400 ring-1 ring-slate-200">{i + 1}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[12.5px] font-bold text-slate-900">{String(a.title ?? "Action")}</p>
                  {a.detail ? <p className="truncate text-[10.5px] text-slate-500">{String(a.detail)}</p> : null}
                </div>
                <ArrowRight className="h-4 w-4 shrink-0 text-slate-300" />
              </button>
            ))}
          </div>
        </section>
      </div>

      {/* ── Bridge strip: telemetry → safety / maintenance / fleet health ───── */}
      <section className="grid gap-4 lg:grid-cols-3">
        <BridgeCard
          title="Safety Bridge"
          icon={ShieldCheck}
          tone="emerald"
          loading={safetyBridge.isLoading}
          error={safetyBridge.isError}
          actionLabel="Open Safety"
          onAction={() => navigate("/safety")}
          lines={[
            metricLine("Fleet safety score", safetyBridge.data?.fleetSafetyScore, "%"),
            metricLine("Open safety events", safetyBridge.data?.openEvents),
            metricLine("Open coaching tasks", safetyBridge.data?.openCoachingTasks),
            metricLine("Overdue coaching", safetyBridge.data?.overdueCoachingTasks),
          ]}
        />
        <BridgeCard
          title="Maintenance Bridge"
          icon={Wrench}
          tone="amber"
          loading={maintenanceBridge.isLoading}
          error={maintenanceBridge.isError}
          actionLabel="Open Maintenance"
          onAction={() => navigate("/maintenance")}
          lines={[
            metricLine("Fleet availability", maintenanceKpis.fleetAvailabilityPct, "%"),
            metricLine("Open work orders", maintenanceKpis.openWorkOrders),
            metricLine("Critical open defects", maintenanceKpis.criticalOpenDefects),
            metricLine("Overdue PM", maintenanceKpis.overduePm),
          ]}
        />
        <BridgeCard
          title="Fleet Health Bridge"
          icon={Truck}
          tone="blue"
          loading={fleetHealthBridge.isLoading}
          error={fleetHealthBridge.isError}
          actionLabel="Open Fleet Health"
          onAction={() => navigate("/fleet-health")}
          lines={[
            metricLine("Fleet health score", fleetHealthBridge.data?.fleetHealthScore, "%"),
            metricLine("Dispatch-ready vehicles", fleetHealthBridge.data?.dispatchReadyVehicles),
            metricLine("Out of service", fleetHealthBridge.data?.oosVehicles),
            metricLine("Critical blockers", fleetHealthBridge.data?.criticalDefectVehicles),
          ]}
        />
      </section>

      {/* ── Trends strip ───────────────────────────────────── */}
      <div className="grid gap-4 sm:grid-cols-3">
        <TrendCard title="Throughput" unit="jobs · this week" color={chart.teal600} type="bar"  agg="sum"  data={weeklyJobs} />
        <TrendCard title="Cost Leakage" unit="spend · this week" color={chart.amber500} type="area" agg="sum"  data={costData} prefix="$" />
        <TrendCard title="Safety Score" unit="fleet composite" color={chart.indigo500} type="area" agg="last" data={safetyTrend} />
      </div>
    </div>
  );
}

/* ── Trend card (current value + delta + chart) ──────────── */
function TrendCard({ title, unit, color, type, agg, data, prefix = "" }: {
  title: string; unit: string; color: string; type: "area" | "bar";
  agg: "sum" | "last"; data: { d: string; v: number }[]; prefix?: string;
}) {
  const values = data.map(d => d.v);
  const last = values.length ? values[values.length - 1] : 0;
  const prev = values.length > 1 ? values[values.length - 2] : last;
  const headline = agg === "sum" ? values.reduce((s, v) => s + v, 0) : last;
  const delta = last - prev;
  const pct = prev !== 0 ? Math.round((delta / prev) * 100) : 0;
  const showDelta = agg === "last" && delta !== 0;
  const peak = Math.max(1, ...values);
  const gradId = `grad-${title.replace(/\s/g, "")}`;
  const fmt = (n: number) => prefix + (n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${Math.round(n)}`);

  return (
    <div className="flex flex-col rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between">
        <div>
          <p className="text-[11px] font-black uppercase tracking-wide text-slate-500">{title}</p>
          <p className="text-[10px] text-slate-400">{unit}</p>
        </div>
        {showDelta && (
          <span className={`inline-flex items-center gap-0.5 rounded-full px-1.5 py-0.5 text-[10px] font-bold ${delta > 0 ? "bg-emerald-50 text-emerald-600" : "bg-rose-50 text-rose-600"}`}>
            {delta > 0 ? <ArrowUpRight className="h-3 w-3" /> : <ArrowDownRight className="h-3 w-3" />}
            {Math.abs(pct)}%
          </span>
        )}
      </div>
      <p className="mt-1.5 text-2xl font-black leading-none text-slate-900">{fmt(headline)}</p>
      <div className="mt-2 h-14">
        <ResponsiveContainer width="100%" height="100%">
          {type === "bar" ? (
            <BarChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
              <Tooltip contentStyle={tipStyle} itemStyle={{ color: chart.slate700 }} cursor={{ fill: "rgba(0,0,0,0.03)" }} labelStyle={{ color: chart.slate500, fontSize: 10 }} />
              <Bar dataKey="v" radius={[3, 3, 0, 0]}>
                {data.map((d, i) => <Cell key={i} fill={d.v >= peak * 0.66 ? color : `${color}66`} />)}
              </Bar>
            </BarChart>
          ) : (
            <AreaChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={color} stopOpacity={0.28} />
                  <stop offset="100%" stopColor={color} stopOpacity={0} />
                </linearGradient>
              </defs>
              <Tooltip contentStyle={tipStyle} itemStyle={{ color: chart.slate700 }} labelStyle={{ display: "none" }} />
              <Area type="monotone" dataKey="v" stroke={color} strokeWidth={2} fill={`url(#${gradId})`} dot={false} />
            </AreaChart>
          )}
        </ResponsiveContainer>
      </div>
    </div>
  );
}

function metricLine(label: string, value: unknown, suffix = "") {
  if (value === null || value === undefined || value === "") {
    return { label, value: "No data yet" };
  }
  return { label, value: `${value}${suffix}` };
}

function BridgeCard({
  title,
  icon: Icon,
  tone,
  loading,
  error,
  actionLabel,
  onAction,
  lines,
}: {
  title: string;
  icon: LucideIcon;
  tone: "emerald" | "amber" | "blue";
  loading: boolean;
  error: boolean;
  actionLabel: string;
  onAction: () => void;
  lines: { label: string; value: string }[];
}) {
  const toneClasses = {
    emerald: "border-emerald-200 bg-emerald-50/60 text-emerald-700",
    amber: "border-amber-200 bg-amber-50/60 text-amber-700",
    blue: "border-blue-200 bg-blue-50/60 text-blue-700",
  }[tone];
  const chipClasses = {
    emerald: "bg-emerald-100 text-emerald-700",
    amber: "bg-amber-100 text-amber-700",
    blue: "bg-blue-100 text-blue-700",
  }[tone];

  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className={`inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.2em] ${chipClasses}`}>
            <Icon className="h-3 w-3" />
            {title}
          </p>
          <p className="mt-2 text-[12px] text-slate-500">
            Live data from the connected telemetry, safety, maintenance, and fleet-health services.
          </p>
        </div>
        <button type="button" onClick={onAction} className={`rounded-full border px-3 py-1 text-[11px] font-bold transition hover:shadow-sm ${toneClasses}`}>
          {actionLabel}
        </button>
      </div>

      <div className="mt-4 space-y-2">
        {loading ? (
          <p className="text-sm text-slate-400">Loading live data…</p>
        ) : error ? (
          <p className="text-sm text-rose-600">Live data unavailable. Check backend connectivity.</p>
        ) : (
          lines.map((line) => (
            <div key={line.label} className="flex items-center justify-between rounded-xl border border-slate-100 bg-slate-50/70 px-3 py-2">
              <span className="text-xs font-medium text-slate-500">{line.label}</span>
              <span className="text-sm font-black text-slate-900">{line.value}</span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}

const tipStyle = { background: tokens.surface, border: `1px solid ${tokens.border}`, borderRadius: 8, fontSize: 11, padding: "4px 10px", boxShadow: "0 4px 12px rgba(0,0,0,.08)" } as const;

/* ── Loading / error state ───────────────────────────────── */
function CenterState({ spin, label, sub, action }: { spin?: boolean; label: string; sub?: string; action?: React.ReactNode }) {
  return (
    <div className="flex h-[60vh] items-center justify-center">
      <div className="flex flex-col items-center gap-2 text-center">
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
