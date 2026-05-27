import { useState } from "react";
import {
  AlertTriangle, BarChart2, Bot, CheckCircle2, Target, TrendingDown, TrendingUp,
} from "lucide-react";
import {
  useKpiMetrics, useKpiSummary, useKpiTargets, useKpiAiRecs,
  useSlaSummary, useSlaRecords, useSlaBreaches,
  useAcknowledgeSlaBreache, useResolveSlaBreache,
} from "@/hooks/useBatch7";

type AnyRecord = Record<string, unknown>;

const TABS = ["KPI Dashboard", "SLA Records", "SLA Breaches", "AI Advisor"] as const;
type Tab = typeof TABS[number];

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    "On Target": "border-emerald-400/20 bg-emerald-400/10 text-emerald-300",
    "At Risk":   "border-amber-400/20 bg-amber-400/10 text-amber-300",
    "Critical":  "border-red-400/20 bg-red-400/10 text-red-300",
    "Met":       "border-emerald-400/20 bg-emerald-400/10 text-emerald-300",
    "Breached":  "border-red-400/20 bg-red-400/10 text-red-300",
    "Open":      "border-red-400/20 bg-red-400/10 text-red-300",
    "Acknowledged": "border-amber-400/20 bg-amber-400/10 text-amber-300",
    "Resolved":  "border-slate-400/20 bg-slate-400/10 text-slate-400",
  };
  const cls = map[status] ?? "border-slate-400/20 bg-slate-400/10 text-slate-400";
  return <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>{status}</span>;
}

function TrendIcon({ trend }: { trend: string }) {
  if (trend === "Up")   return <TrendingUp   className="h-4 w-4 text-emerald-400" />;
  if (trend === "Down") return <TrendingDown className="h-4 w-4 text-red-400" />;
  return <BarChart2 className="h-4 w-4 text-slate-500" />;
}

function KpiCard({ kpi }: { kpi: AnyRecord }) {
  const actual = Number(kpi.actual_value ?? 0);
  const target = Number(kpi.target_value ?? 1);
  const pct    = Math.min(100, Math.round((actual / target) * 100));
  const status = String(kpi.status ?? "");
  const barColor = status === "Critical" ? "bg-red-400" : status === "At Risk" ? "bg-amber-400" : "bg-emerald-400";

  return (
    <div className="panel p-4 space-y-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <p className="truncate font-semibold text-white">{String(kpi.kpi_name ?? "")}</p>
          <p className="text-xs text-slate-500">{String(kpi.category ?? "")}</p>
        </div>
        <div className="flex items-center gap-2">
          <TrendIcon trend={String(kpi.trend ?? "")} />
          <StatusBadge status={status} />
        </div>
      </div>
      <div className="flex items-end justify-between">
        <div>
          <p className="text-2xl font-extrabold text-white">
            {actual.toLocaleString(undefined, { maximumFractionDigits: 1 })}
            <span className="ml-1 text-sm font-normal text-slate-400">{String(kpi.unit ?? "")}</span>
          </p>
          <p className="text-xs text-slate-500">Target: {target.toLocaleString(undefined, { maximumFractionDigits: 1 })} {String(kpi.unit ?? "")}</p>
        </div>
        <p className="text-sm font-bold text-slate-400">{pct}%</p>
      </div>
      <div className="h-1.5 w-full rounded-full bg-slate-800">
        <div className={`h-full rounded-full transition-all ${barColor}`} style={{ width: `${pct}%` }} />
      </div>
      {!!kpi.recommendation && (
        <p className="text-xs text-slate-400 italic border-s-2 border-violet-400/30 ps-2">{String(kpi.recommendation)}</p>
      )}
    </div>
  );
}

export function SlaKpiPage() {
  const [tab, setTab] = useState<Tab>("KPI Dashboard");
  const [filterCat, setFilterCat]   = useState("");
  const [filterSlaType, setFilterSlaType] = useState("");

  const { data: kpiMetricsRaw = [] } = useKpiMetrics();
  const { data: kpiSummaryRaw }      = useKpiSummary();
  const { data: kpiTargetsRaw = [] } = useKpiTargets();
  const { data: kpiAiRecsRaw = [] }  = useKpiAiRecs();
  const { data: slaSummaryRaw }      = useSlaSummary();
  const { data: slaRecordsRaw = [] } = useSlaRecords();
  const { data: slaBreachesRaw = [] }= useSlaBreaches();

  const kpiMetrics  = kpiMetricsRaw  as AnyRecord[];
  const kpiSummary  = kpiSummaryRaw  as AnyRecord | undefined;
  const kpiTargets  = kpiTargetsRaw  as AnyRecord[];
  const kpiAiRecs   = kpiAiRecsRaw   as AnyRecord[];
  const slaSummary  = slaSummaryRaw  as AnyRecord | undefined;
  const slaRecords  = slaRecordsRaw  as AnyRecord[];
  const slaBreaches = slaBreachesRaw as AnyRecord[];

  const ackBreache  = useAcknowledgeSlaBreache();
  const resolveBreache = useResolveSlaBreache();

  const kpiCategories = [...new Set(kpiMetrics.map((k) => String(k.category ?? "")))].filter(Boolean);
  const slaTypes      = [...new Set(slaRecords.map((s) => String(s.sla_type ?? "")))].filter(Boolean);
  const filteredKpi   = filterCat     ? kpiMetrics.filter((k) => String(k.category) === filterCat) : kpiMetrics;
  const filteredSla   = filterSlaType ? slaRecords.filter((s) => String(s.sla_type) === filterSlaType) : slaRecords;

  void kpiTargets;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-xl font-extrabold text-white">SLA / KPI Center</h1>
        <p className="mt-0.5 text-sm text-slate-400">Track key performance indicators, SLA compliance, and breach exposure</p>
      </div>

      {/* Summary strip */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-6">
        {kpiSummary && (
          <>
            <div className="panel p-3 text-center"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Total KPIs</p><p className="text-2xl font-extrabold text-white">{Number(kpiSummary.total ?? 0)}</p></div>
            <div className="panel p-3 text-center"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">On Target</p><p className="text-2xl font-extrabold text-emerald-300">{Number(kpiSummary.onTarget ?? 0)}</p></div>
            <div className="panel p-3 text-center"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">At Risk</p><p className="text-2xl font-extrabold text-amber-300">{Number(kpiSummary.atRisk ?? 0)}</p></div>
            <div className="panel p-3 text-center"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Critical</p><p className="text-2xl font-extrabold text-red-300">{Number(kpiSummary.critical ?? 0)}</p></div>
          </>
        )}
        {slaSummary && (
          <>
            <div className="panel p-3 text-center"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">SLA Met</p><p className="text-2xl font-extrabold text-emerald-300">{Number(slaSummary.met ?? 0)}</p></div>
            <div className="panel p-3 text-center"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">SLA Breached</p><p className="text-2xl font-extrabold text-red-300">{Number(slaSummary.breached ?? 0)}</p></div>
          </>
        )}
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-white/[0.07] pb-px">
        {TABS.map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`rounded-t-lg px-4 py-2 text-sm font-semibold transition ${tab === t ? "bg-teal-400/10 text-teal-300 border border-b-0 border-teal-400/20" : "text-slate-400 hover:text-slate-200"}`}
          >
            {t}
          </button>
        ))}
      </div>

      {/* KPI Dashboard */}
      {tab === "KPI Dashboard" && (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <button onClick={() => setFilterCat("")} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${!filterCat ? "border-teal-400/30 bg-teal-400/10 text-teal-300" : "border-white/10 text-slate-400 hover:text-slate-200"}`}>All</button>
            {kpiCategories.map((c) => (
              <button key={c} onClick={() => setFilterCat(c === filterCat ? "" : c)} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${filterCat === c ? "border-teal-400/30 bg-teal-400/10 text-teal-300" : "border-white/10 text-slate-400 hover:text-slate-200"}`}>{c}</button>
            ))}
          </div>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {filteredKpi.map((kpi, i) => <KpiCard key={i} kpi={kpi} />)}
          </div>
        </div>
      )}

      {/* SLA Records */}
      {tab === "SLA Records" && (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <button onClick={() => setFilterSlaType("")} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${!filterSlaType ? "border-teal-400/30 bg-teal-400/10 text-teal-300" : "border-white/10 text-slate-400 hover:text-slate-200"}`}>All Types</button>
            {slaTypes.map((t) => (
              <button key={t} onClick={() => setFilterSlaType(t === filterSlaType ? "" : t)} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${filterSlaType === t ? "border-teal-400/30 bg-teal-400/10 text-teal-300" : "border-white/10 text-slate-400 hover:text-slate-200"}`}>{t}</button>
            ))}
          </div>
          <div className="panel overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-white/[0.07]">
                  {["SLA", "Customer", "Type", "Target", "Actual", "Status"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-white/[0.04]">
                {filteredSla.map((s, i) => {
                  const targetVal = Number(s.target_value ?? 0);
                  const actualVal = Number(s.actual_value ?? 0);
                  const unit = String(s.unit ?? "");
                  return (
                    <tr key={i} className="transition hover:bg-white/[0.02]">
                      <td className="px-4 py-3">
                        <p className="font-medium text-white">{String(s.sla_name ?? "")}</p>
                        {!!s.job_number && <p className="text-xs text-slate-500">Job: {String(s.job_number)}</p>}
                      </td>
                      <td className="px-4 py-3 text-slate-300">{String(s.customer_name ?? "—")}</td>
                      <td className="px-4 py-3 text-slate-400 text-xs">{String(s.sla_type ?? "")}</td>
                      <td className="px-4 py-3 font-mono text-slate-300">{targetVal.toLocaleString()} {unit}</td>
                      <td className="px-4 py-3 font-mono text-slate-300">{actualVal.toLocaleString()} {unit}</td>
                      <td className="px-4 py-3"><StatusBadge status={String(s.status ?? "")} /></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* SLA Breaches */}
      {tab === "SLA Breaches" && (
        <div className="space-y-3">
          {slaBreaches.length === 0 && (
            <div className="panel p-8 text-center">
              <CheckCircle2 className="mx-auto h-10 w-10 text-emerald-400" />
              <p className="mt-3 font-semibold text-white">No open SLA breaches</p>
              <p className="text-sm text-slate-400">All SLA commitments are within acceptable thresholds.</p>
            </div>
          )}
          {slaBreaches.map((b, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <AlertTriangle className="h-5 w-5 flex-shrink-0 text-red-400 mt-0.5" />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 flex-wrap">
                  <p className="font-semibold text-white">{String(b.sla_name ?? "")}</p>
                  <StatusBadge status={String(b.status ?? "")} />
                  {!!b.customer_name && <span className="text-xs text-slate-400">{String(b.customer_name)}</span>}
                </div>
                <p className="mt-1 text-sm text-slate-300">{String(b.breach_reason ?? "")}</p>
                <p className="mt-0.5 text-xs text-slate-500">Detected: {b.breach_detected_at ? new Date(String(b.breach_detected_at)).toLocaleString() : "—"}</p>
                {!!b.resolution_action && <p className="mt-1 text-xs text-teal-300 italic">{String(b.resolution_action)}</p>}
              </div>
              <div className="flex flex-col gap-2">
                {String(b.status) === "Open" && (
                  <button className="btn-ghost py-1.5 px-3 text-xs" onClick={() => ackBreache.mutate(Number(b.id))}>Acknowledge</button>
                )}
                {String(b.status) !== "Resolved" && (
                  <button className="btn-primary py-1.5 px-3 text-xs" onClick={() => resolveBreache.mutate(Number(b.id))}>Resolve</button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* AI Advisor */}
      {tab === "AI Advisor" && (
        <div className="space-y-3">
          {kpiAiRecs.map((rec, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-violet-400/10 border border-violet-400/20">
                <Bot className="h-4 w-4 text-violet-300" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="font-semibold text-white">{String(rec.title ?? "")}</p>
                  <span className="rounded-full border border-violet-400/20 bg-violet-400/10 px-2 py-0.5 text-[10px] font-bold text-violet-300">Score {Number(rec.score ?? 0)}</span>
                  {!!rec.priority && <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${String(rec.priority) === "High" ? "border-red-400/20 bg-red-400/10 text-red-300" : "border-amber-400/20 bg-amber-400/10 text-amber-300"}`}>{String(rec.priority)}</span>}
                </div>
                <p className="mt-1 text-sm text-slate-400">{String(rec.body ?? rec.description ?? "")}</p>
                {!!rec.action_label && (
                  <button className="mt-2 text-xs font-semibold text-teal-400 hover:text-teal-300 transition">{String(rec.action_label)}</button>
                )}
              </div>
              <Target className="h-4 w-4 flex-shrink-0 text-slate-600" />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
