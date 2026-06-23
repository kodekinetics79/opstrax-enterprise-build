import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import {
  Activity, AlertTriangle, CheckCircle2, ChevronRight, Clock,
  Download, Package, RadioTower, ShieldCheck, Truck, Zap,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import { ErrorState, KpiCard, LoadingState, PageHeader, exportCsv } from "@/components/ui";
import { commandCenterApi } from "@/services/commandCenterApi";
import type { AnyRecord } from "@/types";

const FLEET_STATUS_CONFIG = [
  { key: "driving",  label: "Driving",  color: "bg-teal-500",  route: "/vehicles",    icon: <Truck className="h-5 w-5 text-teal-600" /> },
  { key: "idling",   label: "Idling",   color: "bg-amber-400", route: "/vehicles",    icon: <Zap className="h-5 w-5 text-amber-500" /> },
  { key: "parked",   label: "Parked",   color: "bg-slate-400", route: "/vehicles",    icon: <Clock className="h-5 w-5 text-slate-500" /> },
  { key: "offline",  label: "Offline",  color: "bg-red-400",   route: "/iot-devices", icon: <RadioTower className="h-5 w-5 text-red-500" /> },
];

const KPI_ICONS = [
  <Package className="h-4 w-4" />,
  <AlertTriangle className="h-4 w-4" />,
  <Clock className="h-4 w-4" />,
  <Truck className="h-4 w-4" />,
  <ShieldCheck className="h-4 w-4" />,
];

const SEVERITY: Record<string, { badge: string; dot: string; row: string }> = {
  Critical: {
    badge: "badge badge-danger",
    dot: "mt-1 h-2 w-2 shrink-0 rounded-full bg-red-500 animate-pulse",
    row: "border-l-[3px] border-red-300 bg-red-50/40",
  },
  Warning: {
    badge: "badge badge-warning",
    dot: "mt-1 h-2 w-2 shrink-0 rounded-full bg-amber-400",
    row: "border-l-[3px] border-amber-300 bg-amber-50/30",
  },
  Info: {
    badge: "badge badge-info",
    dot: "mt-1 h-2 w-2 shrink-0 rounded-full bg-blue-400",
    row: "",
  },
};

export function CommandCenterPage() {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["command-center"],
    queryFn: commandCenterApi.summary,
    refetchInterval: 15_000,
  });
  const navigate = useNavigate();

  if (isLoading || !data) return <LoadingState />;
  if (isError) return <ErrorState message={error instanceof Error ? error.message : "Unable to load dashboard data."} />;

  const kpis            = (data.kpis as AnyRecord[]) || [];
  const fleetStatus     = (data.fleetStatus as AnyRecord) || {};
  const exceptions      = (data.exceptions as AnyRecord[]) || [];
  const briefItems      = (data.briefItems as string[]) || [];
  const priorityActions = (data.priorityActions as AnyRecord[]) || [];
  const charts          = (data.charts as AnyRecord) || {};

  const weeklyJobs  = ((charts.weeklyJobs  as number[]) || []).map((v, i) => ({ day: ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"][i] ?? `D${i+1}`, value: v }));
  const costData    = ((charts.costLeakage as number[]) || []).map((v, i) => ({ week: `W${i+1}`, value: v }));
  const safetyTrend = ((charts.safetyScore as number[]) || []).map((v, i) => ({ day: ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"][i] ?? `D${i+1}`, value: v }));

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Command Center"
        title="Fleet Operations"
        description="Exception queue, dispatch status, safety posture and cost exposure — refreshed every 15 seconds."
        actions={
          <>
            <button type="button" className="btn-primary" onClick={() => navigate("/alerts")}>
              Acknowledge Risks
            </button>
            <button type="button" className="btn-ghost" onClick={() => exportCsv("command-center-brief", kpis)}>
              <Download className="h-4 w-4" /> Export Brief
            </button>
          </>
        }
      />

      {/* 5-KPI Row */}
      <div className="grid gap-4 grid-cols-2 md:grid-cols-3 xl:grid-cols-5">
        {kpis.slice(0, 5).map((kpi, i) => {
          const target = resolveKpiTarget(String(kpi.label ?? ""));
          const card = (
            <KpiCard
              label={String(kpi.label)}
              value={String(kpi.valueText || kpi.value || "--")}
              trend={String(kpi.trend || "Updated now")}
              status={String(kpi.status || "Active")}
              icon={KPI_ICONS[i]}
            />
          );
          return target ? (
            <button key={String(kpi.id || i)} type="button" className="block w-full text-left" onClick={() => navigate(target.route)} title={target.title}>
              {card}
            </button>
          ) : (
            <div key={String(kpi.id || i)}>{card}</div>
          );
        })}
      </div>

      {/* Fleet Status Bar */}
      <div className="grid gap-4 md:grid-cols-4">
        {FLEET_STATUS_CONFIG.map(({ key, label, color, route, icon }) => {
          const count = Number(fleetStatus[key] ?? 0);
          const total = (Object.values(fleetStatus) as unknown[]).reduce<number>((s, v) => s + Number(v), 0) || 1;
          const pct   = Math.round((count / total) * 100);
          return (
            <button key={key} type="button" className="panel flex items-center gap-4 p-4 text-left transition hover:border-slate-300" onClick={() => navigate(route)}>
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-slate-50">{icon}</div>
              <div className="min-w-0 flex-1">
                <p className="text-xs font-semibold uppercase tracking-widest text-slate-500">{label}</p>
                <p className="mt-0.5 text-2xl font-bold text-slate-900">{count}</p>
                <div className="mt-1.5 h-1 w-full rounded-full bg-slate-100">
                  <div className={`h-1 rounded-full ${color}`} style={{ width: `${pct}%` }} />
                </div>
              </div>
              <span className="text-xs font-medium text-slate-400">{pct}%</span>
            </button>
          );
        })}
      </div>

      {/* Exception Queue + Brief + Actions */}
      <div className="grid gap-6 xl:grid-cols-[1.5fr_1fr]">
        <ExceptionQueue exceptions={exceptions} navigate={navigate} />
        <div className="flex flex-col gap-4">
          <OperationsBrief items={briefItems} />
          <ActionPanel actions={priorityActions} navigate={navigate} />
        </div>
      </div>

      {/* Charts Row */}
      <div className="grid gap-6 xl:grid-cols-3">
        <ChartPanel title="Operations Throughput" subtitle="Jobs completed per day — this week">
          <ResponsiveContainer width="100%" height={200}>
            <AreaChart data={weeklyJobs}>
              <defs>
                <linearGradient id="opsThroughput" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%"  stopColor="#2dd4bf" stopOpacity={0.25} />
                  <stop offset="95%" stopColor="#2dd4bf" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="day"   stroke="#94a3b8" tick={{ fontSize: 11 }} />
              <YAxis                 stroke="#94a3b8" tick={{ fontSize: 11 }} />
              <Tooltip contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8, fontSize: 12 }} />
              <Area type="monotone" dataKey="value" stroke="#2dd4bf" strokeWidth={2} fill="url(#opsThroughput)" />
            </AreaChart>
          </ResponsiveContainer>
        </ChartPanel>

        <ChartPanel title="Cost Leakage by Week" subtitle="SAR (K) — unrecovered operational spend">
          <ResponsiveContainer width="100%" height={200}>
            <BarChart data={costData}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="week"  stroke="#94a3b8" tick={{ fontSize: 11 }} />
              <YAxis                 stroke="#94a3b8" tick={{ fontSize: 11 }} />
              <Tooltip contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8, fontSize: 12 }} />
              <Bar dataKey="value" fill="#f59e0b" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartPanel>

        <ChartPanel title="Safety Score Trend" subtitle="Fleet composite score (0–100)">
          <ResponsiveContainer width="100%" height={200}>
            <AreaChart data={safetyTrend}>
              <defs>
                <linearGradient id="safetyGrad" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%"  stopColor="#6366f1" stopOpacity={0.2} />
                  <stop offset="95%" stopColor="#6366f1" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="day"        stroke="#94a3b8" tick={{ fontSize: 11 }} />
              <YAxis domain={[50, 100]}   stroke="#94a3b8" tick={{ fontSize: 11 }} />
              <Tooltip contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8, fontSize: 12 }} />
              <Area type="monotone" dataKey="value" stroke="#6366f1" strokeWidth={2} fill="url(#safetyGrad)" />
            </AreaChart>
          </ResponsiveContainer>
        </ChartPanel>
      </div>
    </div>
  );
}

/* ── Exception Queue ─────────────────────────────────────── */
function ExceptionQueue({
  exceptions,
  navigate,
}: {
  exceptions: AnyRecord[];
  navigate: (path: string) => void;
}) {
  const criticalCount = exceptions.filter((e) => String(e.severity) === "Critical").length;
  const warningCount  = exceptions.filter((e) => String(e.severity) === "Warning").length;

  return (
    <div className="panel overflow-hidden">
      <div className="flex items-center justify-between border-b border-slate-100 px-5 py-4">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="section-title">Exception Queue</h2>
            {criticalCount > 0 && (
              <span className="inline-flex items-center gap-1 rounded-full border border-red-300 bg-red-50 px-2 py-0.5 text-[10px] font-bold text-red-600">
                <span className="h-1.5 w-1.5 rounded-full bg-red-500 animate-pulse" />
                {criticalCount} Critical
              </span>
            )}
            {warningCount > 0 && (
              <span className="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-2 py-0.5 text-[10px] font-bold text-amber-600">
                {warningCount} Warning
              </span>
            )}
          </div>
          <p className="mt-0.5 text-xs text-slate-400">Requires operator action · sorted by severity</p>
        </div>
        <button
          type="button"
          className="text-xs font-medium text-teal-600 hover:underline"
          onClick={() => navigate("/alerts")}
        >
          View all →
        </button>
      </div>

      {exceptions.length === 0 ? (
        <div className="py-10 text-center">
          <CheckCircle2 className="mx-auto h-8 w-8 text-emerald-400" />
          <p className="mt-2 text-sm font-semibold text-slate-600">No active exceptions</p>
          <p className="text-xs text-slate-400">Fleet is operating within normal parameters</p>
        </div>
      ) : (
        <ul className="divide-y divide-slate-100">
          {exceptions.map((exc, i) => {
            const sev = String(exc.severity || "Info");
            const cfg = SEVERITY[sev] ?? SEVERITY.Info;
            const entity = [String(exc.vehicle || ""), String(exc.driver || "")]
              .filter(Boolean)
              .join(" · ");

            return (
              <li key={String(exc.id || i)} className={`flex items-start gap-3 px-5 py-3.5 ${cfg.row}`}>
                <span className={cfg.dot} />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className={cfg.badge}>{sev}</span>
                    {entity && (
                      <span className="text-sm font-semibold text-slate-900">{entity}</span>
                    )}
                    <span className="text-xs text-slate-300">·</span>
                    <span className="text-sm text-slate-700">{String(exc.event || exc.title || "")}</span>
                  </div>
                  <p className="mt-1 text-xs leading-relaxed text-slate-500">
                    {String(exc.slaImpact || exc.body || "")}
                  </p>
                  <p className="mt-0.5 text-[11px] text-slate-400">{String(exc.timestamp || exc.time || "")}</p>
                </div>
                <button
                  type="button"
                  className="btn-ghost h-7 shrink-0 gap-1 px-3 text-xs"
                  onClick={() => navigate(String(exc.actionRoute || "/alerts"))}
                >
                  {String(exc.actionLabel || "View")}
                  <ChevronRight className="h-3 w-3" />
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

/* ── Operations Brief ────────────────────────────────────── */
function OperationsBrief({ items }: { items: string[] }) {
  const bullets = items.length > 0 ? items : [
    "Fleet is operating within normal parameters — 9 vehicles on active routes.",
    "No critical SLA breaches active. 2 shipments approaching ETA threshold.",
    "Maintenance queue clear — next PM service due in 3 days on TRK-106.",
  ];

  return (
    <div className="panel p-5">
      <div className="mb-3 flex items-center gap-2">
        <Activity className="h-4 w-4 text-teal-600" />
        <h2 className="section-title">Operations Brief</h2>
        <span className="ml-auto text-[10px] text-slate-400">Updated 38 sec ago</span>
      </div>
      <ul className="space-y-2.5">
        {bullets.slice(0, 5).map((item, i) => (
          <li key={i} className="flex items-start gap-2.5 text-sm leading-relaxed text-slate-700">
            <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-teal-500" />
            {item}
          </li>
        ))}
      </ul>
    </div>
  );
}

/* ── Recommended Actions ─────────────────────────────────── */
function ActionPanel({
  actions,
  navigate,
}: {
  actions: AnyRecord[];
  navigate: (path: string) => void;
}) {
  return (
    <div className="panel flex-1 p-5">
      <h2 className="section-title mb-3">Recommended Actions</h2>
      {actions.length === 0 ? (
        <p className="text-sm text-slate-400">No actions required at this time.</p>
      ) : (
        <ul className="space-y-2">
          {actions.slice(0, 5).map((action, i) => (
            <li
              key={String(action.id || i)}
              className="flex items-start justify-between gap-3 rounded-xl border border-slate-100 bg-slate-50/60 p-3"
            >
              <div className="min-w-0 flex-1">
                <p className="text-sm font-semibold text-slate-900">{String(action.title || "Action")}</p>
                {!!action.body && (
                  <p className="mt-0.5 text-xs leading-relaxed text-slate-500">{String(action.body)}</p>
                )}
              </div>
              <button
                type="button"
                className="btn-ghost h-7 shrink-0 gap-1 px-3 text-xs"
                onClick={() => navigate(String(action.entityRoute || action.route || "/alerts"))}
              >
                {String(action.actionLabel || "View")}
                <ChevronRight className="h-3 w-3" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/* ── Chart Panel ─────────────────────────────────────────── */
function ChartPanel({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <div className="panel p-5">
      <h2 className="section-title">{title}</h2>
      <p className="mt-0.5 text-xs text-slate-400">{subtitle}</p>
      <div className="mt-4">{children}</div>
    </div>
  );
}

/* ── KPI click targets ───────────────────────────────────── */
function resolveKpiTarget(label: string) {
  const t = label.toLowerCase();
  if (/shipment|load|delivery|eta|pod/.test(t))         return { route: "/active-shipments", title: "Open shipments" };
  if (/sla|exception|breach/.test(t))                   return { route: "/alerts",            title: "Open alerts" };
  if (/overdue|assignment|unassigned|dispatch/.test(t)) return { route: "/dispatch",          title: "Open dispatch" };
  if (/fleet|on road|driving|vehicle/.test(t))          return { route: "/vehicles",          title: "Open vehicles" };
  if (/safety|event|incident|score/.test(t))            return { route: "/incidents",         title: "Open safety" };
  return null;
}
