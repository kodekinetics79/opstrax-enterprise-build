import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { tokens, chart } from "@/styles/tokens";
import {
  Activity, AlertOctagon, AlertTriangle, ArrowRight, CheckCircle2, Download,
  RefreshCw, ShieldCheck, Truck, Wrench, type LucideIcon,
} from "lucide-react";
import { useNavigate } from "react-router";
import {
  Area, AreaChart, Bar, BarChart, Cell, ComposedChart, Line, Pie, PieChart,
  ResponsiveContainer, Tooltip,
} from "recharts";
import { exportCsv } from "@/components/ui";
import { commandCenterApi } from "@/services/commandCenterApi";
import { fleetHealthApi } from "@/services/fleetHealthApi";
import { maintenanceApi } from "@/services/maintenanceApi";
import { safetyApi } from "@/services/safetyApi";
import type { AnyRecord } from "@/types";

/* ── Severity tokens (colour = state severity, nothing else) ── */
const SEV: Record<string, { dot: string; chip: string; icon: LucideIcon }> = {
  Critical: { dot: chart.red500,   chip: "bg-red-50 text-red-700 border-red-200",       icon: AlertOctagon },
  Warning:  { dot: chart.amber500, chip: "bg-amber-50 text-amber-700 border-amber-200", icon: AlertTriangle },
  Info:     { dot: chart.blue500,  chip: "bg-blue-50 text-blue-700 border-blue-200",    icon: Activity },
};

const POSTURE: Record<string, string> = {
  Elevated: "border-red-200 bg-red-50 text-red-700",
  Guarded:  "border-amber-200 bg-amber-50 text-amber-700",
  Stable:   "border-emerald-200 bg-emerald-50 text-emerald-700",
};

/* Hero KPI slots — routes into the owning workflow page per slot. */
const KPI_ROUTES = ["/jobs", "/alerts", "/dispatch", "/vehicles", "/incidents"];

const FLEET_CFG = [
  { key: "driving", label: "Driving", color: chart.teal600 },
  { key: "idling",  label: "Idling",  color: chart.amber500 },
  { key: "parked",  label: "Parked",  color: chart.slate500 },
  { key: "offline", label: "Offline", color: chart.red500 },
];

const DOW = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

/* Three-state doctrine for every metric line:
   measured value (incl. 0) → the number; absent → "—" + a named reason. Fetch
   failures are handled at column level. Never a default number. */
function metricLine(label: string, value: unknown, suffix = "", absentReason = "Not yet measured") {
  const missing = value === null || value === undefined || value === "";
  return { label, value: missing ? "—" : `${value}${suffix}`, note: missing ? absentReason : null };
}

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

  // API-measured values only: no client-side derivation, no fabricated denominators,
  // no default posture — absence stays absent.
  const fleetTotal   = Number(data.fleetTotal ?? 0);
  const readinessPct = typeof data.readinessPct === "number" && fleetTotal > 0 ? data.readinessPct : null;
  const posture      = typeof data.posture === "string" && POSTURE[data.posture] ? data.posture : null;
  const critCount    = Number(data.criticalCount ?? 0);
  const warnCount    = Number(data.warningCount ?? 0);

  const weeklyJobs = ((charts.weeklyJobs  as number[]) ?? []).map((v, i) => ({ d: DOW[i] ?? String(i + 1), v: Number(v) }));
  const costData   = ((charts.costLeakage as number[]) ?? []).map((v, i) => ({ d: `D${i + 1}`, v: Number(v) }));

  const donut = FLEET_CFG.map(f => ({ name: f.label, value: Number(fleetStatus[f.key] ?? 0), color: f.color }));

  // Real "as of" time from the payload. If the feed carries no parseable timestamp
  // we drop the label rather than imply a fresh sync.
  const generatedAt = data.generatedAt ? new Date(String(data.generatedAt)) : null;
  const asOf = generatedAt && !Number.isNaN(generatedAt.getTime())
    ? generatedAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
    : null;

  const vehiclesTileEmpty = (label: string) => label === "Vehicles in Fleet";

  return (
    <div className="control-tower space-y-3">
      {/* ── Status strip: the 5-second verdict ─────────────── */}
      <header className="flex flex-wrap items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-2.5 shadow-sm">
        <span className="text-sm font-bold text-slate-900">Operations</span>
        {posture && (
          <span className={`inline-flex items-center rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${POSTURE[posture]}`}>
            {posture} posture
          </span>
        )}
        {critCount > 0 && (
          <button type="button" onClick={() => navigate("/alerts")}
            className="inline-flex items-center gap-1 rounded-full border border-red-200 bg-red-50 px-2.5 py-0.5 text-[11px] font-semibold text-red-700 hover:border-red-300">
            <AlertOctagon className="h-3 w-3" /> {critCount} critical
          </button>
        )}
        {warnCount > 0 && (
          <button type="button" onClick={() => navigate("/alerts")}
            className="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-0.5 text-[11px] font-semibold text-amber-700 hover:border-amber-300">
            <AlertTriangle className="h-3 w-3" /> {warnCount} warning{warnCount === 1 ? "" : "s"}
          </button>
        )}
        {critCount === 0 && warnCount === 0 && (
          <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 text-[11px] font-semibold text-emerald-700">
            <CheckCircle2 className="h-3 w-3" /> Clear
          </span>
        )}
        <div className="ml-auto flex items-center gap-2">
          {isFetching && <RefreshCw className="h-3 w-3 animate-spin text-teal-600" />}
          {asOf && <span className="text-[11px] font-medium text-slate-500">as of {asOf} · refreshes every 15s</span>}
          <button type="button" onClick={() => navigate("/alerts")} className="btn-primary h-8 gap-1.5 px-3 text-xs">
            Open Alerts
          </button>
          <button type="button" onClick={() => exportCsv("dashboard", kpis)} title="Export KPIs as CSV"
            className="btn-ghost h-8 w-8 items-center justify-center px-0" aria-label="Export KPIs as CSV">
            <Download className="h-3.5 w-3.5" />
          </button>
        </div>
      </header>

      {/* ── Hero KPI strip ─────────────────────────────────── */}
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
        {kpis.slice(0, 5).map((kpi, i) => {
          const label = String(kpi.label ?? "");
          const raw = kpi.valueText ?? kpi.value;
          const measured = raw !== null && raw !== undefined && raw !== "";
          const numeric = Number(kpi.value ?? NaN);
          const status = String(kpi.status ?? "");
          const attention = /attention|review|risk|critical|warn/i.test(status) && numeric > 0;
          const onboarding = vehiclesTileEmpty(label) && measured && numeric === 0;
          return (
            <button key={label || i} type="button" onClick={() => navigate(KPI_ROUTES[i] ?? "/jobs")}
              className="min-w-0 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-left shadow-sm transition hover:border-slate-300">
              <div className="flex items-center justify-between gap-2">
                <p className="truncate text-[11px] font-semibold uppercase tracking-wide text-slate-500">{label}</p>
                {measured && status && attention && (
                  <span className="shrink-0 rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[10px] font-semibold text-amber-700">{status}</span>
                )}
              </div>
              <p className={`mt-1.5 text-2xl font-bold leading-none tracking-tight tabular-nums ${measured ? "text-slate-900" : "text-slate-900/60"}`}>
                {measured ? String(raw) : "—"}
              </p>
              <p className="mt-1 truncate text-[11px] font-medium text-slate-400">
                {onboarding ? "Add your first vehicle →" : measured ? " " : "Not yet measured"}
              </p>
            </button>
          );
        })}
      </div>

      {/* ── Triage grid: queue → actions → capacity ────────── */}
      <div className="grid items-stretch gap-3 xl:grid-cols-[1.6fr_1fr_0.9fr]">
        {/* Live Exception Queue — the decision layer */}
        <section className="flex min-w-0 max-h-[420px] flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
          <div className="flex items-center gap-2 border-b border-slate-100 px-4 py-2.5">
            <AlertOctagon className="h-4 w-4 shrink-0 text-red-500" />
            <p className="text-sm font-bold text-slate-900">Live Exception Queue</p>
            <p className="hidden text-[11px] text-slate-400 sm:block">severity-first · act top-down</p>
            <button type="button" onClick={() => navigate("/alerts")} className="ml-auto inline-flex items-center gap-0.5 text-[11px] font-semibold text-teal-700 hover:underline">
              All <ArrowRight className="h-3 w-3" />
            </button>
          </div>

          {exceptions.length === 0 ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-1.5 py-10">
              <CheckCircle2 className="h-8 w-8 text-emerald-500" />
              <p className="text-sm font-semibold text-slate-600">No active exceptions{asOf ? ` · as of ${asOf}` : ""}</p>
              <p className="text-xs text-slate-400">Job, fleet and safety feeds are clear.</p>
            </div>
          ) : (
            <ul className="min-h-0 flex-1 divide-y divide-slate-50 overflow-y-auto">
              {exceptions.slice(0, 12).map((exc, i) => {
                const sev = String(exc.severity ?? "Info");
                const cfg = SEV[sev] ?? SEV.Info;
                const Icon = cfg.icon;
                const entity = [String(exc.vehicle ?? ""), String(exc.driver ?? "")].filter(Boolean).join(" · ");
                return (
                  <li key={i} className="relative flex items-center gap-2.5 px-4 py-2.5 transition hover:bg-slate-50/70">
                    <span className="absolute left-0 top-0 h-full w-[3px]" style={{ background: cfg.dot }} />
                    <Icon className="h-4 w-4 shrink-0" style={{ color: cfg.dot }} />
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-1.5">
                        <span className="truncate text-[13px] font-semibold text-slate-900">{String(exc.event ?? exc.title ?? "Exception")}</span>
                        <span className={`shrink-0 rounded-full border px-1.5 py-px text-[10px] font-semibold uppercase ${cfg.chip}`}>{sev}</span>
                      </div>
                      <p className="truncate text-[11px] text-slate-500">
                        {entity || "Unassigned"}<span className="text-slate-300"> · </span>{String(exc.timestamp ?? exc.time ?? "")}
                      </p>
                    </div>
                    <button type="button" onClick={() => navigate(String(exc.actionRoute ?? "/alerts"))}
                      className="shrink-0 rounded-lg border border-slate-200 px-2.5 py-1 text-[11px] font-semibold text-slate-600 transition hover:border-teal-300 hover:text-teal-700">
                      {String(exc.actionLabel ?? "View")}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </section>

        {/* Priority Actions + operational notes */}
        <section className="flex min-w-0 flex-col rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <p className="flex items-center gap-1.5 text-sm font-bold text-slate-900"><Wrench className="h-3.5 w-3.5 text-slate-400" /> Priority Actions</p>
          {priorityActions.length === 0 ? (
            <p className="mt-3 text-xs leading-relaxed text-slate-400">
              Nothing queued — actions appear when exceptions, overdue PM, or coaching tasks need an owner.
            </p>
          ) : (
            <div className="mt-2.5 space-y-2">
              {priorityActions.slice(0, 4).map((a, i) => (
                <button key={i} type="button" onClick={() => navigate(String(a.entityRoute ?? a.route ?? "/alerts"))}
                  className="flex w-full items-center gap-2.5 rounded-xl border border-slate-100 bg-slate-50/60 px-3 py-2 text-left transition hover:border-teal-300">
                  <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-lg bg-white text-[11px] font-bold text-slate-400 ring-1 ring-slate-200 tabular-nums">{i + 1}</span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-[13px] font-semibold text-slate-900">{String(a.title ?? "Action")}</p>
                    {a.detail ? <p className="truncate text-[11px] text-slate-500">{String(a.detail)}</p> : null}
                  </div>
                  <ArrowRight className="h-4 w-4 shrink-0 text-slate-300" />
                </button>
              ))}
            </div>
          )}
          {briefItems.length > 0 && (
            <div className="mt-auto border-t border-slate-100 pt-3">
              <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-400">Notes</p>
              <ul className="mt-1.5 space-y-1.5">
                {briefItems.slice(0, 3).map((item, i) => (
                  <li key={i} className="flex items-start gap-2 text-xs leading-snug text-slate-600">
                    <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-slate-300" />
                    {item}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </section>

        {/* Fleet Snapshot — response capacity */}
        <section className="min-w-0 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="flex items-center justify-between gap-2">
            <p className="flex items-center gap-1.5 text-sm font-bold text-slate-900"><Truck className="h-3.5 w-3.5 text-slate-400" /> Fleet Snapshot</p>
            <span className="text-[11px] font-semibold text-slate-400 tabular-nums">{fleetTotal} unit{fleetTotal === 1 ? "" : "s"}</span>
          </div>

          {fleetTotal === 0 ? (
            <button type="button" onClick={() => navigate("/vehicles")}
              className="mt-3 flex w-full flex-col items-center gap-1.5 rounded-xl border border-dashed border-slate-300 bg-slate-50/60 px-3 py-6 text-center transition hover:border-teal-300">
              <Truck className="h-6 w-6 text-slate-300" />
              <p className="text-xs font-semibold text-slate-600">No vehicles yet</p>
              <p className="text-[11px] text-slate-400">Add your first vehicle to see live fleet status.</p>
            </button>
          ) : (
            <>
              {fleetTotal >= 4 && (
                <div className="relative mx-auto mt-2 h-[104px] w-[104px]">
                  <ResponsiveContainer width="100%" height="100%">
                    <PieChart>
                      <Pie data={donut} dataKey="value" innerRadius={36} outerRadius={50} paddingAngle={2} stroke={tokens.surface} strokeWidth={2}>
                        {donut.map((d, i) => <Cell key={i} fill={d.color} />)}
                      </Pie>
                      <Tooltip contentStyle={tipStyle} itemStyle={{ color: chart.slate700 }} />
                    </PieChart>
                  </ResponsiveContainer>
                  {readinessPct != null && (
                    <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                      <span className="text-lg font-bold leading-none text-slate-900 tabular-nums">{readinessPct}%</span>
                      <span className="text-[9px] font-semibold uppercase tracking-wide text-slate-400">ready</span>
                    </div>
                  )}
                </div>
              )}
              {fleetTotal < 4 && readinessPct != null && (
                <p className="mt-2 text-xs font-medium text-slate-500"><span className="font-bold text-slate-900 tabular-nums">{readinessPct}%</span> ready to respond</p>
              )}
              <div className="mt-3 grid grid-cols-2 gap-1.5">
                {FLEET_CFG.map(f => {
                  const c = Number(fleetStatus[f.key] ?? 0);
                  return (
                    <button key={f.key} type="button" onClick={() => navigate(f.key === "offline" ? "/iot-devices" : "/vehicles")}
                      className="flex items-center gap-2 rounded-lg border border-slate-100 bg-slate-50/60 px-2 py-1.5 text-left transition hover:border-slate-300">
                      <span className="h-2 w-2 shrink-0 rounded-full" style={{ background: f.color }} />
                      <div className="min-w-0">
                        <p className="text-sm font-bold leading-none text-slate-900 tabular-nums">{c}</p>
                        <p className="text-[10px] font-semibold uppercase tracking-wide text-slate-400">{f.label}</p>
                      </div>
                    </button>
                  );
                })}
              </div>
            </>
          )}
        </section>
      </div>

      {/* ── Domain Health: safety | maintenance | fleet health ── */}
      {/* Each column leads with a chart over measured data (the safety feed already
          ships a 30-day daily trend; counts render as scaled bars) so the band reads
          at a glance instead of as label:value rows. */}
      <section className="grid min-w-0 gap-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:grid-cols-3 sm:gap-0 sm:divide-x sm:divide-slate-100">
        <DomainColumn
          title="Safety"
          icon={ShieldCheck}
          loading={safetyBridge.isLoading}
          error={safetyBridge.isError}
          onOpen={() => navigate("/safety")}
          headline={metricLine("Fleet safety score", safetyBridge.data?.fleetSafetyScore, "%")}
        >
          <SafetyTrendChart rows={safetyBridge.data?.trend as AnyRecord[] | undefined} />
          {(() => {
            const openEvents = asNum(safetyBridge.data?.openEvents);
            const openCoaching = asNum(safetyBridge.data?.openCoachingTasks);
            const overdueCoaching = asNum(safetyBridge.data?.overdueCoachingTasks);
            const max = Math.max(openEvents ?? 0, openCoaching ?? 0, overdueCoaching ?? 0, 1);
            return (
              <div className="mt-3 space-y-2">
                <MiniBar label="Open safety events" value={openEvents} max={max} color={chart.sky600} />
                <MiniBar label="Open coaching tasks" value={openCoaching} max={max} color={chart.sky600} />
                <MiniBar label="Overdue coaching" value={overdueCoaching} max={max} color={chart.amber600} />
              </div>
            );
          })()}
        </DomainColumn>
        <DomainColumn
          title="Maintenance"
          icon={Wrench}
          loading={maintenanceBridge.isLoading}
          error={maintenanceBridge.isError}
          onOpen={() => navigate("/maintenance")}
          headline={metricLine("Fleet availability", maintenanceKpis.fleetAvailabilityPct, "%", fleetTotal === 0 ? "No vehicles yet" : "Not yet measured")}
        >
          {(() => {
            const availability = asNum(maintenanceKpis.fleetAvailabilityPct);
            const openWo = asNum(maintenanceKpis.openWorkOrders);
            const critical = asNum(maintenanceKpis.criticalOpenDefects);
            const overduePm = asNum(maintenanceKpis.overduePm);
            const max = Math.max(openWo ?? 0, critical ?? 0, overduePm ?? 0, 1);
            return (
              <>
                {availability != null && (
                  <div className="mt-2 h-2 overflow-hidden rounded-full bg-slate-100" title={`Fleet availability: ${availability}%`}>
                    <div className="h-full rounded-full" style={{ width: `${Math.min(100, Math.max(availability, 0))}%`, background: chart.teal600 }} />
                  </div>
                )}
                <div className="mt-3 space-y-2">
                  <MiniBar label="Open work orders" value={openWo} max={max} color={chart.sky600} />
                  <MiniBar label="Critical open defects" value={critical} max={max} color={chart.red600} />
                  <MiniBar label="Overdue PM" value={overduePm} max={max} color={chart.amber600} />
                </div>
              </>
            );
          })()}
        </DomainColumn>
        <DomainColumn
          title="Fleet Health"
          icon={Truck}
          loading={fleetHealthBridge.isLoading}
          error={fleetHealthBridge.isError}
          onOpen={() => navigate("/fleet-health")}
          headline={metricLine("Fleet health score", fleetHealthBridge.data?.fleetHealthScore, "%")}
        >
          {(() => {
            const absent = fleetTotal === 0 ? "No vehicles yet" : "Not yet measured";
            const ready = asNum(fleetHealthBridge.data?.dispatchReadyVehicles);
            const oos = asNum(fleetHealthBridge.data?.oosVehicles);
            const blocked = asNum(fleetHealthBridge.data?.criticalDefectVehicles);
            // Bars scale against the whole fleet so ready-vs-blocked reads instantly.
            const max = Math.max(asNum(fleetHealthBridge.data?.totalVehicles) ?? fleetTotal, 1);
            return (
              <div className="mt-3 space-y-2">
                <MiniBar label={`Dispatch-ready of ${max}`} value={ready} max={max} color={chart.emerald600} absentReason={absent} />
                <MiniBar label="Out of service" value={oos} max={max} color={chart.amber600} absentReason={absent} />
                <MiniBar label="Critical blockers" value={blocked} max={max} color={chart.red600} absentReason={absent} />
              </div>
            );
          })()}
        </DomainColumn>
      </section>

      {/* ── Trends: review material, below the fold ────────── */}
      <div className="grid min-w-0 gap-3 sm:grid-cols-2">
        <TrendCard title="Throughput" unit="jobs · this week" color={chart.teal600} type="bar" data={weeklyJobs} />
        <TrendCard title="Cost Leakage" unit="logged expenses · last 7 days" color={chart.sky600} type="area" data={costData} prefix="$" />
      </div>
    </div>
  );
}

/* ── Trend card ──────────────────────────────────────────── */
function TrendCard({ title, unit, color, type, data, prefix = "" }: {
  title: string; unit: string; color: string; type: "area" | "bar";
  data: { d: string; v: number }[]; prefix?: string;
}) {
  const total = data.reduce((s, p) => s + p.v, 0);
  const gradId = `grad-${title.replace(/\s/g, "")}`;
  const fmt = (n: number) => prefix + (n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${Math.round(n)}`);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <div>
        <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">{title}</p>
        <p className="text-[10px] text-slate-400">{unit}</p>
      </div>
      {data.length === 0 ? (
        <p className="mt-4 text-xs text-slate-400">No history yet.</p>
      ) : (
        <>
          <p className="mt-1.5 text-2xl font-bold leading-none text-slate-900 tabular-nums">{fmt(total)}</p>
          <div className="mt-2 h-20 w-full min-w-0">
            <ResponsiveContainer width="100%" height="100%">
              {type === "bar" ? (
                <BarChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                  <Tooltip contentStyle={tipStyle} itemStyle={{ color: chart.slate700 }} cursor={{ fill: "rgba(0,0,0,0.03)" }} labelStyle={{ color: chart.slate500, fontSize: 10 }} />
                  <Bar dataKey="v" radius={[3, 3, 0, 0]} fill={color} />
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
        </>
      )}
    </div>
  );
}

/* ── Domain health column ────────────────────────────────── */
function DomainColumn({ title, icon: Icon, loading, error, onOpen, headline, children }: {
  title: string;
  icon: LucideIcon;
  loading: boolean;
  error: boolean;
  onOpen: () => void;
  headline: { label: string; value: string; note: string | null };
  children: ReactNode;
}) {
  return (
    <div className="min-w-0 sm:px-4 sm:first:pl-0 sm:last:pr-0">
      <div className="flex items-center justify-between gap-2">
        <p className="flex items-center gap-1.5 text-sm font-bold text-slate-900"><Icon className="h-3.5 w-3.5 text-slate-400" /> {title}</p>
        <button type="button" onClick={onOpen} className="inline-flex items-center gap-0.5 text-[11px] font-semibold text-teal-700 hover:underline">
          Open <ArrowRight className="h-3 w-3" />
        </button>
      </div>

      {loading ? (
        <p className="mt-3 text-xs text-slate-400">Loading live data…</p>
      ) : error ? (
        <p className="mt-3 text-xs text-red-600">Feed unavailable — check backend connectivity.</p>
      ) : (
        <>
          <div className="mt-2.5">
            <p className={`text-2xl font-bold leading-none tabular-nums ${headline.value === "—" ? "text-slate-900/60" : "text-slate-900"}`}>{headline.value}</p>
            <p className="mt-1 text-[11px] font-medium text-slate-400">{headline.note ?? headline.label}</p>
          </div>
          {children}
        </>
      )}
    </div>
  );
}

/* Coerce a JSON value to a measured number; absence stays null (never a default). */
function asNum(value: unknown): number | null {
  if (value === null || value === undefined || value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

/* ── Count bar: magnitude at a glance, honest about absence ── */
function MiniBar({ label, value, max, color, absentReason = "Not yet measured" }: {
  label: string; value: number | null; max: number; color: string; absentReason?: string;
}) {
  const measured = value != null;
  const pct = measured && max > 0 ? Math.min(100, (value / max) * 100) : 0;
  return (
    <div title={measured ? `${label}: ${value}` : `${label}: ${absentReason}`}>
      <div className="flex items-baseline justify-between gap-2">
        <span className="truncate text-xs font-medium text-slate-500">{label}</span>
        <span className={`shrink-0 text-sm font-bold tabular-nums ${measured ? "text-slate-900" : "text-slate-900/50"}`}>
          {measured ? value : "—"}
        </span>
      </div>
      <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
        {measured && value > 0 && (
          <div className="h-full rounded-full" style={{ width: `${Math.max(pct, 3)}%`, background: color }} />
        )}
      </div>
    </div>
  );
}

/* ── Safety 30-day trend: events/day area + critical line ── */
/* Gap days render as measured zeros: safety_events is the ledger, so a day with
   no rows is a day with no events — not missing data. */
function SafetyTrendChart({ rows }: { rows: AnyRecord[] | undefined }) {
  if (!Array.isArray(rows)) return null;
  const byDay = new Map<string, { events: number; critical: number }>();
  for (const r of rows) {
    const key = String(r.eventDate ?? "").slice(0, 10);
    if (key) byDay.set(key, { events: Number(r.eventCount ?? 0), critical: Number(r.criticalCount ?? 0) });
  }
  const series: { d: string; events: number; critical: number }[] = [];
  const now = new Date();
  for (let i = 29; i >= 0; i--) {
    const dt = new Date(now.getFullYear(), now.getMonth(), now.getDate() - i);
    const key = `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, "0")}-${String(dt.getDate()).padStart(2, "0")}`;
    const hit = byDay.get(key);
    series.push({ d: key.slice(5), events: hit?.events ?? 0, critical: hit?.critical ?? 0 });
  }
  return (
    <div className="mt-2">
      <div className="flex items-center gap-3 text-[10px] font-semibold text-slate-500">
        <span className="inline-flex items-center gap-1"><span className="h-1.5 w-1.5 rounded-full" style={{ background: chart.sky600 }} /> Events / day · 30d</span>
        <span className="inline-flex items-center gap-1"><span className="h-1.5 w-1.5 rounded-full" style={{ background: chart.red600 }} /> Critical</span>
      </div>
      <div className="mt-1 h-16 w-full min-w-0">
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart data={series} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
            <defs>
              <linearGradient id="grad-safety-events" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={chart.sky600} stopOpacity={0.25} />
                <stop offset="100%" stopColor={chart.sky600} stopOpacity={0} />
              </linearGradient>
            </defs>
            <Tooltip contentStyle={tipStyle} itemStyle={{ color: chart.slate700 }} labelStyle={{ color: chart.slate500, fontSize: 10 }} />
            <Area type="monotone" dataKey="events" name="Events" stroke={chart.sky600} strokeWidth={2} fill="url(#grad-safety-events)" dot={false} />
            <Line type="monotone" dataKey="critical" name="Critical" stroke={chart.red600} strokeWidth={1.5} dot={false} />
          </ComposedChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

const tipStyle = { background: tokens.surface, border: `1px solid ${tokens.border}`, borderRadius: 8, fontSize: 11, padding: "4px 10px", boxShadow: "0 4px 12px rgba(0,0,0,.08)" } as const;

/* ── Loading / error state ───────────────────────────────── */
function CenterState({ spin, label, sub, action }: { spin?: boolean; label: string; sub?: string; action?: ReactNode }) {
  return (
    <div className="flex h-[60vh] items-center justify-center">
      <div className="flex flex-col items-center gap-2 text-center">
        {spin
          ? <RefreshCw className="h-7 w-7 animate-spin text-teal-500" />
          : <AlertTriangle className="h-8 w-8 text-rose-400" />}
        <p className="text-sm font-semibold text-slate-700">{label}</p>
        {sub && <p className="text-xs text-slate-400">{sub}</p>}
        {action}
      </div>
    </div>
  );
}
