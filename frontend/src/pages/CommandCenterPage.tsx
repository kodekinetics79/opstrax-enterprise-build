import { useQuery } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import "@/styles/command-center.css";
import { tokens, chart } from "@/styles/tokens";
import {
  Activity, AlertOctagon, AlertTriangle, ArrowRight, CheckCircle2, Download,
  RefreshCw, Search, ShieldCheck, Truck, Wrench, Zap, ClipboardList, Route, type LucideIcon,
} from "lucide-react";
import { useNavigate } from "react-router";
import {
  Area, AreaChart, Bar, BarChart, ComposedChart, Line, XAxis, CartesianGrid,
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
const KPI_ROUTES = ["/jobs", "/control-tower", "/dispatch", "/vehicles", "/incidents"];

const FLEET_CFG = [
  { key: "driving",   label: "On Road", color: chart.teal600 },
  { key: "idling",    label: "Idle / Stop", color: chart.amber500 },
  { key: "parked",    label: "Available / Parked", color: chart.slate500 },
  { key: "attention", label: "Needs Service", color: chart.red500 },
];

const DOW = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

function exceptionActionRoute(exception: AnyRecord) {
  const route = String(exception.actionRoute ?? "/alerts");
  if (route !== "/active-shipments") return route;
  const jobId = String(exception.jobId ?? "").trim();
  if (jobId) return `${route}?jobId=${encodeURIComponent(jobId)}`;
  const context = [exception.shipmentNumber, exception.vehicle, exception.driver]
    .map((value) => String(value ?? "").trim())
    .find(Boolean);
  return context ? `${route}?search=${encodeURIComponent(context)}` : route;
}

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
  const [search, setSearch] = useState("");
  const [severity, setSeverity] = useState("All");

  if (isError) return (
    <CenterState
      label="Dashboard feed unavailable"
      sub="The operations dashboard API did not respond."
      action={<button type="button" onClick={() => refetch()} className="btn-primary h-9 px-4 text-xs mt-3">Reconnect</button>}
    />
  );

  if (isLoading || !data) return <CenterState spin label="Synchronizing dashboard…" />;

  const kpis            = (data.kpis            as AnyRecord[]) ?? [];
  const fleetStatus     = (data.fleetStatus     as AnyRecord)  ?? {};
  const exceptions      = (data.exceptions      as AnyRecord[]) ?? [];
  const briefItems      = (data.briefItems      as string[])   ?? [];
  const priorityActions = (data.priorityActions as AnyRecord[]) ?? [];
  const charts          = (data.charts          as AnyRecord)  ?? {};
  const maintenanceKpis = (maintenanceBridge.data?.kpis as AnyRecord) ?? {};

  // API-measured values only: no client-side derivation, no fabricated denominators,
  // no default posture — absence stays absent.
  const fleetTotal   = asNum(data.fleetTotal);
  const readinessPct = typeof data.readinessPct === "number" && fleetTotal != null && fleetTotal > 0 ? data.readinessPct : null;
  const posture      = typeof data.posture === "string" && POSTURE[data.posture] ? data.posture : null;
  const critCount    = Number(data.criticalCount ?? 0);
  const warnCount    = Number(data.warningCount ?? 0);

  const weeklyJobs = ((charts.weeklyJobs  as number[]) ?? []).map((v, i) => ({ d: DOW[i] ?? String(i + 1), v: Number(v) }));
  const costData   = ((charts.costLeakage as number[]) ?? []).map((v, i) => ({ d: `D${i + 1}`, v: Number(v) }));

  const fleetStatusAvailable = FLEET_CFG.every((item) => asNum(fleetStatus[item.key]) != null);

  // Real "as of" time from the payload. If the feed carries no parseable timestamp
  // we drop the label rather than imply a fresh sync.
  const generatedAt = data.generatedAt ? new Date(String(data.generatedAt)) : null;
  const asOf = generatedAt && !Number.isNaN(generatedAt.getTime())
    ? generatedAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
    : null;

  const term = search.trim().toLowerCase();
  const visibleExceptions = exceptions.filter(exc =>
    (severity === "All" || String(exc.severity ?? "Info") === severity) &&
    (!term || [exc.event, exc.title, exc.vehicle, exc.driver, exc.shipmentNumber]
      .some(value => String(value ?? "").toLowerCase().includes(term))),
  );
  const metricIcons = [ClipboardList, Activity, Route, Truck, AlertTriangle];

  return (
    <div className="command-overview">
      <header className="command-overview-header">
        <div>
          <h1>Operations</h1>
          <p>{generatedAt && !Number.isNaN(generatedAt.getTime())
            ? generatedAt.toLocaleDateString([], { weekday: "short", day: "numeric", month: "short", year: "numeric" })
            : "Operating overview"}
            {posture && <span className={`command-posture ${POSTURE[posture]}`}>{posture} posture</span>}
          </p>
        </div>
        <div className="command-overview-tools">
          <label className="command-search">
            <Search size={16} aria-hidden="true" />
            <input type="search" aria-label="Search dashboard exceptions" placeholder="Search exceptions, vehicles, drivers…" value={search} onChange={event => setSearch(event.target.value)} />
          </label>
          <select className="command-filter" aria-label="Filter exceptions by severity" value={severity} onChange={event => setSeverity(event.target.value)}>
            <option value="All">All severities</option><option>Critical</option><option>Warning</option><option>Info</option>
          </select>
          <button type="button" className="command-control command-icon-control" onClick={() => refetch()} aria-label="Refresh dashboard" title={asOf ? `Snapshot ${asOf}; refreshes every 15 seconds` : "Refresh dashboard"}>
            <RefreshCw size={16} className={isFetching ? "animate-spin" : ""} />
          </button>
          <button type="button" className="command-control command-icon-control" onClick={() => exportCsv("dashboard", kpis)} aria-label="Export KPIs as CSV"><Download size={16} /></button>
        </div>
      </header>

      <div className="command-metrics command-surface" aria-label="Operations summary">
        {kpis.slice(0, 5).map((kpi, i) => {
          const label = String(kpi.label ?? "");
          const raw = kpi.valueText ?? kpi.value;
          const measured = raw !== null && raw !== undefined && raw !== "";
          const Icon = metricIcons[i] ?? Activity;
          return (
            <button key={label || i} type="button" onClick={() => navigate(KPI_ROUTES[i] ?? "/jobs")}>
              {i === 1 ? <img className="command-dimension-image" src="/images/dashboard/telemetry-3d.png" width="44" height="44" alt="" />
                : i === 3 ? <img className="command-dimension-image" src="/images/dashboard/truck-3d.png" width="44" height="44" alt="" />
                : <Icon className={`command-dimension-icon ${i === 4 ? "command-danger-icon" : ""}`} size={30} aria-hidden="true" />}
              <span className="command-metric-copy"><strong>{measured ? String(raw) : "—"}</strong><span>{label}</span>{!measured && <small>Not measured</small>}{measured && kpi.status && String(kpi.status) !== "Active" ? <small>{String(kpi.status)}</small> : null}</span>
              <ArrowRight size={14} className="command-metric-arrow" aria-hidden="true" />
            </button>
          );
        })}
      </div>

      <div className="command-workspace">
        <section className="command-surface command-exceptions" aria-labelledby="command-exceptions-heading">
          <div className="command-section-heading">
            <AlertOctagon size={22} className="command-dimension-icon command-danger-icon" aria-hidden="true" />
            <h2 id="command-exceptions-heading">Current Exception Queue</h2>
            <span className="command-section-count">{visibleExceptions.length} {visibleExceptions.length === 1 ? "item" : "items"}</span>
            <button type="button" className="command-control" aria-label="Open Control Tower" onClick={() => navigate("/control-tower")}>View all</button>
          </div>
          <div className="command-exception-summary">
            <span className="text-red-700">{critCount} critical</span><span className="text-amber-700">{warnCount} warnings</span>
            {asOf && <span className="ml-auto">Snapshot {asOf}</span>}
          </div>
          {visibleExceptions.length === 0 ? (
            <div className="command-empty">
              <CheckCircle2 size={28} className="text-teal-600" />
              <strong>{exceptions.length ? "No exceptions match your filters" : "No active exceptions"}</strong>
              <p>{exceptions.length ? "Try another vehicle, driver, or severity." : "No current job, vehicle-service, or safety exception is recorded in this view."}</p>
              {(search || severity !== "All") && <button type="button" className="command-control" onClick={() => { setSearch(""); setSeverity("All"); }}>Clear filters</button>}
            </div>
          ) : (
            <div className="command-table-scroll" tabIndex={0} role="region" aria-label="Current exceptions table">
              <table className="command-table">
                <thead><tr><th scope="col">Severity</th><th scope="col">Exception</th><th scope="col">Vehicle / Driver</th><th scope="col">Age</th><th scope="col">Action</th></tr></thead>
                <tbody>{visibleExceptions.map((exc, i) => {
                  const sev = String(exc.severity ?? "Info");
                  const cfg = SEV[sev] ?? SEV.Info;
                  const Icon = cfg.icon;
                  return <tr key={String(exc.id ?? `${exc.event}-${exc.vehicle}-${i}`)}>
                    <td><span data-severity={sev.toLowerCase()} className={`command-severity ${cfg.chip}`}><Icon size={13} aria-hidden="true" />{sev}</span></td>
                    <td><strong>{String(exc.event ?? exc.title ?? "Exception")}</strong>{Boolean(exc.shipmentNumber) && <small>{String(exc.shipmentNumber)}</small>}</td>
                    <td><strong>{String(exc.vehicle || "Unassigned")}</strong><small>{String(exc.driver || "Unassigned")}</small></td>
                    <td className="command-age">{String(exc.timestamp ?? exc.time ?? "—")}</td>
                    <td><button type="button" className="command-control" onClick={() => navigate(exceptionActionRoute(exc))}>{String(exc.actionLabel ?? "View")}</button></td>
                  </tr>;
                })}</tbody>
              </table>
            </div>
          )}
        </section>

        <section className="command-surface command-agenda" aria-labelledby="command-agenda-heading">
          <div className="command-section-heading"><Zap size={23} className="command-dimension-icon" aria-hidden="true" /><h2 id="command-agenda-heading">Action Agenda</h2><span className="command-section-count">{Math.min(priorityActions.length, 4)} priorities</span></div>
          {priorityActions.length === 0 ? <p className="command-empty">Nothing queued — actions appear when exceptions, overdue PM, or coaching tasks need an owner.</p> :
            <ol>{priorityActions.slice(0, 4).map((action, i) => <li key={i}>
              <span className="command-priority-number" aria-hidden="true">{i + 1}</span>
              <button type="button" onClick={() => navigate(String(action.entityRoute ?? action.route ?? "/alerts"))}>
                <span><strong>{String(action.title ?? "Action")}</strong>{Boolean(action.detail) && <small>{String(action.detail)}</small>}</span><ArrowRight size={16} aria-hidden="true" />
              </button>
            </li>)}</ol>}
          {briefItems.length > 0 && <details className="command-notes"><summary>Operational notes</summary><ul>{briefItems.slice(0, 3).map((item, i) => <li key={i}>{item}</li>)}</ul></details>}
        </section>
      </div>

      {/* ── Domain Health: safety | maintenance | fleet health ── */}
      {/* Supporting domains stay compact; empty histories do not reserve chart space. */}
      <section className="command-health">
        <section className="command-surface command-fleet-distribution">
          <div className="command-section-heading"><img className="command-dimension-image" src="/images/dashboard/truck-3d.png" width="28" height="28" alt="" /><h2>Fleet distribution</h2><button type="button" className="command-open" aria-label="Open fleet registry" onClick={() => navigate("/vehicles")}><ArrowRight size={16} /></button></div>
          {fleetTotal == null || !fleetStatusAvailable ? <p className="command-absence">Fleet status evidence unavailable</p>
            : fleetTotal === 0 ? <button type="button" className="command-control m-3" onClick={() => navigate("/vehicles")}>Add first vehicle</button>
            : <><div className="command-fleet-counts">{FLEET_CFG.map(f => <button key={f.key} type="button" onClick={() => navigate(f.key === "attention" ? "/work-orders" : "/vehicles")}><strong>{String(fleetStatus[f.key])}</strong><span><i style={{ background: f.color }} />{f.label}</span></button>)}</div>
              {readinessPct != null && <p className="command-fleet-note">{readinessPct}% not marked for service</p>}</>}
        </section>
        <DomainColumn
          title="Safety"
          icon={ShieldCheck}
          loading={safetyBridge.isLoading}
          error={safetyBridge.isError}
          onOpen={() => navigate("/safety")}
          headline={metricLine("Fleet safety score", safetyBridge.data?.fleetSafetyScore, "%")}
        >
          <details className="command-history"><summary>30-day history</summary><SafetyTrendChart rows={safetyBridge.data?.trend as AnyRecord[] | undefined} /></details>
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
            const coveredFleet = asNum(fleetHealthBridge.data?.totalVehicles);
            const max = Math.max(coveredFleet ?? 0, 1);
            return (
              <div className="mt-3 space-y-2">
                <MiniBar label={coveredFleet != null ? `Dispatch-ready of ${coveredFleet}` : "Dispatch-ready"} value={ready} max={max} color={chart.emerald600} absentReason={absent} />
                <MiniBar label="Out of service" value={oos} max={max} color={chart.amber600} absentReason={absent} />
                <MiniBar label="Critical blockers" value={blocked} max={max} color={chart.red600} absentReason={absent} />
              </div>
            );
          })()}
        </DomainColumn>
      </section>

      {/* ── Trends: review material, below the fold ────────── */}
      <details className="command-trends"><summary>Operating trends <span>Throughput &amp; recorded expenses</span></summary><div className="grid min-w-0 gap-3 sm:grid-cols-2">
        <TrendCard title="Throughput" unit="jobs · this week" color={chart.teal600} type="bar" data={weeklyJobs} />
        <TrendCard title="Cost Leakage" unit="logged expenses · last 7 days" color={chart.sky600} type="area" data={costData} prefix="$" />
      </div></details>
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
        <p className="text-[10px] text-slate-500">{unit}</p>
      </div>
      {data.length === 0 ? (
        <p className="mt-4 text-xs text-slate-500">No history yet.</p>
      ) : (
        <>
          <p className="mt-1.5 text-2xl font-bold leading-none text-slate-900 tabular-nums">{fmt(total)}</p>
          <div className="mt-3 h-28 w-full min-w-0">
            <ResponsiveContainer width="100%" height="100%">
              {type === "bar" ? (
                <BarChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                  <CartesianGrid vertical={false} stroke={tokens.border} />
                  <XAxis dataKey="d" tickLine={false} axisLine={false} tick={{ fill: chart.slate500, fontSize: 11 }} />
                  <Tooltip contentStyle={tipStyle} itemStyle={{ color: chart.slate700 }} cursor={{ fill: "rgba(0,0,0,0.03)" }} labelStyle={{ color: chart.slate500, fontSize: 10 }} />
                  <Bar dataKey="v" radius={[3, 3, 0, 0]} fill={color} />
                </BarChart>
              ) : (
                <AreaChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                  <CartesianGrid vertical={false} stroke={tokens.border} />
                  <XAxis dataKey="d" tickLine={false} axisLine={false} tick={{ fill: chart.slate500, fontSize: 11 }} />
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
    <div className="command-surface command-domain">
      <div className="command-section-heading">
        <Icon className="command-dimension-icon" size={23} aria-hidden="true" /><h2>{title}</h2>
        <button type="button" onClick={onOpen} className="command-open" aria-label={`Open ${title}`}><ArrowRight size={16} /></button>
      </div>

      {loading ? (
        <p className="mt-3 text-xs text-slate-500">Loading live data…</p>
      ) : error ? (
        <p className="mt-3 text-xs text-red-600">Feed unavailable — check backend connectivity.</p>
      ) : (
        <>
          <div className="command-domain-headline">
            <p className={`text-2xl font-bold leading-none tabular-nums ${headline.value === "—" ? "text-slate-900/60" : "text-slate-900"}`}>{headline.value}</p>
            <p className="mt-1 text-[11px] font-medium text-slate-500">{headline.note ?? headline.label}</p>
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
      {measured && value > 0 && (
        <div className="mt-1 h-1 overflow-hidden rounded-full bg-slate-100">
          <div className="h-full rounded-full" style={{ width: `${Math.max(pct, 3)}%`, background: color }} />
        </div>
      )}
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
  if (!series.some(point => point.events > 0 || point.critical > 0)) return (
    <p className="mt-2 text-xs text-slate-500">No safety events recorded in the last 30 days.</p>
  );
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
        {sub && <p className="text-xs text-slate-500">{sub}</p>}
        {action}
      </div>
    </div>
  );
}
