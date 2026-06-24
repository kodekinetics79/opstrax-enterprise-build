import { useQuery } from "@tanstack/react-query";
import {
  Activity, AlertTriangle, CheckCircle2, ChevronRight,
  Clock, Download, Package, RadioTower, RefreshCw,
  ShieldCheck, Truck, Zap,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import {
  Area, AreaChart, ResponsiveContainer, Tooltip,
} from "recharts";
import { exportCsv } from "@/components/ui";
import { commandCenterApi } from "@/services/commandCenterApi";
import type { AnyRecord } from "@/types";

/* ── Severity config ─────────────────────────────────────── */
const SEV: Record<string, { dot: string; badge: string; row: string }> = {
  Critical: {
    dot:   "h-1.5 w-1.5 rounded-full bg-red-500 animate-pulse shrink-0 mt-1",
    badge: "rounded-full bg-red-50 border border-red-200 text-red-600 px-1.5 py-0.5 text-[9px] font-bold uppercase",
    row:   "border-l-2 border-red-300 bg-red-50/30",
  },
  Warning: {
    dot:   "h-1.5 w-1.5 rounded-full bg-amber-400 shrink-0 mt-1",
    badge: "rounded-full bg-amber-50 border border-amber-200 text-amber-600 px-1.5 py-0.5 text-[9px] font-bold uppercase",
    row:   "border-l-2 border-amber-300 bg-amber-50/20",
  },
  Info: {
    dot:   "h-1.5 w-1.5 rounded-full bg-blue-400 shrink-0 mt-1",
    badge: "rounded-full bg-blue-50 border border-blue-200 text-blue-600 px-1.5 py-0.5 text-[9px] font-bold uppercase",
    row:   "",
  },
};

const KPI_ICONS = [
  <Package   className="h-3.5 w-3.5" />,
  <AlertTriangle className="h-3.5 w-3.5" />,
  <Clock     className="h-3.5 w-3.5" />,
  <Truck     className="h-3.5 w-3.5" />,
  <ShieldCheck className="h-3.5 w-3.5" />,
];

const FLEET_CFG = [
  { key: "driving", label: "Driving",  bar: "bg-teal-500",  icon: <Truck className="h-3 w-3 text-teal-600" />,      route: "/vehicles"    },
  { key: "idling",  label: "Idling",   bar: "bg-amber-400", icon: <Zap className="h-3 w-3 text-amber-500" />,       route: "/vehicles"    },
  { key: "parked",  label: "Parked",   bar: "bg-slate-400", icon: <Clock className="h-3 w-3 text-slate-500" />,     route: "/vehicles"    },
  { key: "offline", label: "Offline",  bar: "bg-red-400",   icon: <RadioTower className="h-3 w-3 text-red-500" />, route: "/iot-devices" },
];

/* ── Main page ───────────────────────────────────────────── */
export function CommandCenterPage() {
  const { data, isLoading, isError, isFetching, refetch } = useQuery({
    queryKey:       ["command-center"],
    queryFn:        commandCenterApi.summary,
    refetchInterval: 15_000,
  });
  const navigate = useNavigate();

  if (isLoading || !data) {
    return (
      <div className="flex h-[60vh] items-center justify-center">
        <div className="flex flex-col items-center gap-2 text-slate-400">
          <RefreshCw className="h-6 w-6 animate-spin" />
          <span className="text-sm font-medium">Loading command center…</span>
        </div>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex h-[60vh] items-center justify-center">
        <div className="text-center">
          <AlertTriangle className="mx-auto h-8 w-8 text-red-400 mb-2" />
          <p className="text-sm font-semibold text-slate-700">Failed to load dashboard</p>
          <button type="button" onClick={() => refetch()} className="mt-3 text-xs font-semibold text-teal-600 hover:underline">Retry</button>
        </div>
      </div>
    );
  }

  const kpis            = (data.kpis            as AnyRecord[]) ?? [];
  const fleetStatus     = (data.fleetStatus      as AnyRecord)  ?? {};
  const exceptions      = (data.exceptions       as AnyRecord[]) ?? [];
  const briefItems      = (data.briefItems       as string[])   ?? [];
  const priorityActions = (data.priorityActions  as AnyRecord[]) ?? [];
  const charts          = (data.charts           as AnyRecord)  ?? {};

  const weeklyJobs  = ((charts.weeklyJobs  as number[]) ?? []).map((v, i) => ({ d: ["M","T","W","T","F","S","S"][i], v }));
  const costData    = ((charts.costLeakage as number[]) ?? []).map((v, i) => ({ d: `W${i+1}`, v }));
  const safetyTrend = ((charts.safetyScore as number[]) ?? []).map((v, i) => ({ d: ["M","T","W","T","F","S","S"][i], v }));

  const critCount = exceptions.filter(e => String(e.severity) === "Critical").length;
  const warnCount = exceptions.filter(e => String(e.severity) === "Warning").length;

  const fleetTotal = (Object.values(fleetStatus) as unknown[]).reduce<number>((s, v) => s + Number(v), 0) || 1;

  return (
    <div className="space-y-3 h-full">

      {/* ── Header bar ──────────────────────────────────────── */}
      <div className="flex items-center gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <Activity className="h-4 w-4 text-teal-600 shrink-0" />
            <span className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Operations</span>
            <span className="live-dot" />
            {isFetching && <RefreshCw className="h-3 w-3 text-blue-400 animate-spin" />}
          </div>
          <h1 className="text-lg font-extrabold tracking-tight text-slate-900 leading-tight">
            Command Center
            <span className="ml-2 text-sm font-normal text-slate-400">· Fleet Operations</span>
          </h1>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          {critCount > 0 && (
            <span className="inline-flex items-center gap-1 rounded-full border border-red-200 bg-red-50 px-2.5 py-1 text-[11px] font-bold text-red-600">
              <span className="h-1.5 w-1.5 rounded-full bg-red-500 animate-pulse" />
              {critCount} Critical
            </span>
          )}
          <button type="button" className="btn-primary h-8 px-3 text-xs" onClick={() => navigate("/alerts")}>
            Acknowledge Risks
          </button>
          <button type="button" className="btn-ghost h-8 px-3 text-xs gap-1.5" onClick={() => exportCsv("command-center", kpis)}>
            <Download className="h-3.5 w-3.5" /> Export
          </button>
        </div>
      </div>

      {/* ── KPIs + Fleet Status (one row) ───────────────────── */}
      <div className="grid grid-cols-2 sm:grid-cols-4 xl:grid-cols-9 gap-2">
        {/* 5 KPIs */}
        {kpis.slice(0, 5).map((kpi, i) => {
          const target = resolveKpiRoute(String(kpi.label ?? ""));
          const el = (
            <div className={`panel p-3 flex items-center gap-2.5 ${target ? "cursor-pointer hover:border-teal-300 transition" : ""}`}
              onClick={target ? () => navigate(target) : undefined}>
              <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-slate-50 text-slate-500">
                {KPI_ICONS[i]}
              </div>
              <div className="min-w-0">
                <p className="text-[10px] font-semibold uppercase tracking-wide text-slate-400 truncate">{String(kpi.label ?? "")}</p>
                <p className="text-lg font-extrabold leading-tight text-slate-900">{String(kpi.valueText || kpi.value || "—")}</p>
              </div>
            </div>
          );
          return <div key={i}>{el}</div>;
        })}

        {/* 4 fleet status chips */}
        {FLEET_CFG.map(({ key, label, bar, icon, route }) => {
          const count = Number(fleetStatus[key] ?? 0);
          const pct   = Math.round((count / fleetTotal) * 100);
          return (
            <button key={key} type="button"
              className="panel p-3 flex items-center gap-2 text-left hover:border-slate-300 transition"
              onClick={() => navigate(route)}>
              <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-slate-50">{icon}</div>
              <div className="min-w-0 flex-1">
                <p className="text-[10px] font-semibold uppercase tracking-wide text-slate-400 truncate">{label}</p>
                <p className="text-lg font-extrabold leading-tight text-slate-900">{count}</p>
                <div className="mt-0.5 h-0.5 w-full rounded-full bg-slate-100">
                  <div className={`h-0.5 rounded-full ${bar}`} style={{ width: `${pct}%` }} />
                </div>
              </div>
            </button>
          );
        })}
      </div>

      {/* ── Middle row: Exceptions | Brief + Actions | Charts ── */}
      <div className="grid gap-3 xl:grid-cols-[1.6fr_1fr_1.4fr]">

        {/* Exception Queue */}
        <div className="panel overflow-hidden flex flex-col">
          <div className="flex items-center gap-2 border-b border-slate-100 px-4 py-2.5 shrink-0">
            <AlertTriangle className="h-3.5 w-3.5 text-red-500 shrink-0" />
            <span className="section-title text-[11px]">Exception Queue</span>
            {critCount > 0 && <span className="ml-1 text-[9px] font-bold text-red-600 bg-red-50 border border-red-200 rounded-full px-1.5 py-0.5">{critCount} Critical</span>}
            {warnCount > 0 && <span className="text-[9px] font-bold text-amber-600 bg-amber-50 border border-amber-200 rounded-full px-1.5 py-0.5">{warnCount} Warning</span>}
            <button type="button" className="ml-auto text-[10px] font-semibold text-teal-600 hover:underline shrink-0" onClick={() => navigate("/alerts")}>View all →</button>
          </div>

          {exceptions.length === 0 ? (
            <div className="flex flex-1 flex-col items-center justify-center py-6 gap-1">
              <CheckCircle2 className="h-6 w-6 text-emerald-400" />
              <p className="text-xs font-semibold text-slate-500">No active exceptions</p>
            </div>
          ) : (
            <ul className="divide-y divide-slate-100 overflow-y-auto max-h-55">
              {exceptions.slice(0, 8).map((exc, i) => {
                const sev = String(exc.severity || "Info");
                const cfg = SEV[sev] ?? SEV.Info;
                const entity = [String(exc.vehicle || ""), String(exc.driver || "")].filter(Boolean).join(" · ");
                return (
                  <li key={i} className={`flex items-start gap-2.5 px-4 py-2.5 ${cfg.row}`}>
                    <span className={cfg.dot} />
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-1">
                        <span className={cfg.badge}>{sev}</span>
                        {entity && <span className="text-xs font-semibold text-slate-800">{entity}</span>}
                        <span className="text-xs text-slate-600 truncate">{String(exc.event || exc.title || "")}</span>
                      </div>
                      <p className="text-[10px] text-slate-400 mt-0.5 truncate">{String(exc.timestamp || exc.time || "")}</p>
                    </div>
                    <button type="button" className="shrink-0 text-[10px] font-semibold text-teal-600 hover:underline whitespace-nowrap"
                      onClick={() => navigate(String(exc.actionRoute || "/alerts"))}>
                      {String(exc.actionLabel || "View")} →
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        {/* Brief + Actions */}
        <div className="flex flex-col gap-3">
          {/* Ops brief */}
          <div className="panel p-3 flex-1">
            <div className="flex items-center gap-1.5 mb-2">
              <Activity className="h-3.5 w-3.5 text-teal-600" />
              <span className="section-title text-[11px]">Operations Brief</span>
            </div>
            <ul className="space-y-1.5">
              {(briefItems.length > 0 ? briefItems : [
                "Fleet operating within normal parameters.",
                "9 vehicles on active routes now.",
                "No critical SLA breaches active.",
                "Next PM service due in 3 days.",
              ]).slice(0, 4).map((item, i) => (
                <li key={i} className="flex items-start gap-2 text-[11px] leading-snug text-slate-600">
                  <span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-teal-500" />{item}
                </li>
              ))}
            </ul>
          </div>

          {/* Priority actions */}
          <div className="panel p-3 flex-1">
            <span className="section-title text-[11px] block mb-2">Priority Actions</span>
            {priorityActions.length === 0 ? (
              <p className="text-[11px] text-slate-400">No actions required.</p>
            ) : (
              <ul className="space-y-1.5">
                {priorityActions.slice(0, 4).map((action, i) => (
                  <li key={i} className="flex items-center gap-2 rounded-lg border border-slate-100 bg-slate-50/60 px-2.5 py-1.5">
                    <div className="min-w-0 flex-1">
                      <p className="text-[11px] font-semibold text-slate-800 truncate">{String(action.title || "Action")}</p>
                    </div>
                    <button type="button" title={String(action.title || "View action")} className="shrink-0 text-[10px] font-semibold text-teal-600 hover:underline"
                      onClick={() => navigate(String(action.entityRoute || action.route || "/alerts"))}>
                      <ChevronRight className="h-3 w-3" aria-hidden="true" />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>

        {/* Mini sparkline charts */}
        <div className="flex flex-col gap-3">
          <MiniChart title="Throughput" sub="Jobs / day" color="#2dd4bf" data={weeklyJobs} gradId="g1" />
          <MiniChart title="Cost Leakage" sub="SAR (K) / week" color="#f59e0b" data={costData} gradId="g2" />
          <MiniChart title="Safety Score" sub="Fleet composite" color="#6366f1" data={safetyTrend} gradId="g3" />
        </div>
      </div>
    </div>
  );
}

/* ── Mini sparkline ──────────────────────────────────────── */
function MiniChart({ title, sub, color, data, gradId }: {
  title: string; sub: string; color: string;
  data: { d: string; v: number }[]; gradId: string;
}) {
  return (
    <div className="panel p-3 flex-1 flex flex-col">
      <div className="flex items-baseline justify-between mb-1">
        <span className="text-[11px] font-bold text-slate-700">{title}</span>
        <span className="text-[9px] text-slate-400">{sub}</span>
      </div>
      <div className="flex-1 min-h-15">
        <ResponsiveContainer width="100%" height={60}>
          <AreaChart data={data} margin={{ top: 2, right: 2, left: -32, bottom: 0 }}>
            <defs>
              <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%"  stopColor={color} stopOpacity={0.2} />
                <stop offset="95%" stopColor={color} stopOpacity={0}   />
              </linearGradient>
            </defs>
            <Tooltip
              contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 6, fontSize: 10, padding: "2px 8px" }}
              itemStyle={{ color: "#334155" }}
              labelStyle={{ display: "none" }}
            />
            <Area type="monotone" dataKey="v" stroke={color} strokeWidth={1.5} fill={`url(#${gradId})`} dot={false} />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

/* ── KPI route resolver ──────────────────────────────────── */
function resolveKpiRoute(label: string): string | null {
  const t = label.toLowerCase();
  if (/shipment|load|delivery|eta|pod/.test(t))         return "/active-shipments";
  if (/sla|exception|breach/.test(t))                   return "/alerts";
  if (/overdue|assignment|unassigned|dispatch/.test(t)) return "/dispatch-board";
  if (/fleet|on road|driving|vehicle/.test(t))          return "/vehicles";
  if (/safety|event|incident|score/.test(t))            return "/incidents";
  return null;
}
