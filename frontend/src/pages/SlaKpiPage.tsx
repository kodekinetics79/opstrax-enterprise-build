import { useState } from "react";
import {
  AlertTriangle, BarChart2, Bot, CheckCircle2, Download, Target, TrendingDown, TrendingUp,
} from "lucide-react";
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, ReferenceLine, Cell,
} from "recharts";
import {
  useKpiMetrics, useKpiSummary, useKpiTargets, useKpiAiRecs,
  useSlaSummary, useSlaRecords, useSlaBreaches,
  useAcknowledgeSlaBreache, useResolveSlaBreache,
} from "@/hooks/useBatch7";
import { exportCsv, LoadingState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// Empty-state defaults — used only as fallbacks when the live backend returns no
// rows. No synthetic/demo values: the page renders honest empty states, never
// fabricated numbers.
const EMPTY_KPI_METRICS: AnyRecord[] = [];
const EMPTY_KPI_SUMMARY: AnyRecord = { total: 0, onTarget: 0, atRisk: 0, critical: 0 };
const EMPTY_SLA_RECORDS: AnyRecord[] = [];
const EMPTY_SLA_SUMMARY: AnyRecord = { met: 0, breached: 0 };
const EMPTY_SLA_BREACHES: AnyRecord[] = [];
const EMPTY_AI_RECS: AnyRecord[] = [];

// ── Helpers ───────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    "On Target":    "border-emerald-300 bg-emerald-50 text-emerald-700",
    "At Risk":      "border-amber-300 bg-amber-50 text-amber-700",
    "Critical":     "border-red-300 bg-red-50 text-red-700",
    "Met":          "border-emerald-300 bg-emerald-50 text-emerald-700",
    "Breached":     "border-red-300 bg-red-50 text-red-700",
    "Open":         "border-red-300 bg-red-50 text-red-700",
    "Acknowledged": "border-amber-300 bg-amber-50 text-amber-700",
    "Resolved":     "border-slate-300 bg-slate-100 text-slate-600",
  };
  const cls = map[status] ?? "border-slate-300 bg-slate-100 text-slate-600";
  return <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>{status}</span>;
}

function TrendIcon({ trend }: { trend: string }) {
  if (trend === "Up")   return <TrendingUp   className="h-4 w-4 text-emerald-600" />;
  if (trend === "Down") return <TrendingDown className="h-4 w-4 text-red-600" />;
  return <BarChart2 className="h-4 w-4 text-slate-500" />;
}

function KpiCard({ kpi }: { kpi: AnyRecord }) {
  const actual = Number(kpi.actual_value ?? 0);
  const target = Number(kpi.target_value ?? 1);
  const pct    = Math.min(110, Math.round((actual / target) * 100));
  const status = String(kpi.status ?? "");
  const barColor = status === "Critical" ? "bg-red-500" : status === "At Risk" ? "bg-amber-500" : "bg-emerald-500";
  const displayPct = Math.min(100, pct);

  return (
    <div className="panel p-4 space-y-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <p className="truncate font-semibold text-slate-900">{String(kpi.kpi_name ?? "")}</p>
          <p className="text-xs text-slate-500">{String(kpi.category ?? "")}</p>
        </div>
        <div className="flex items-center gap-2">
          <TrendIcon trend={String(kpi.trend ?? "")} />
          <StatusBadge status={status} />
        </div>
      </div>
      <div className="flex items-end justify-between">
        <div>
          <p className="text-2xl font-extrabold text-slate-900">
            {actual.toLocaleString(undefined, { maximumFractionDigits: 1 })}
            <span className="ml-1 text-sm font-normal text-slate-500">{String(kpi.unit ?? "")}</span>
          </p>
          <p className="text-xs text-slate-500">Target: {target.toLocaleString(undefined, { maximumFractionDigits: 1 })} {String(kpi.unit ?? "")}</p>
        </div>
        <p className={`text-sm font-bold ${pct >= 100 ? "text-emerald-700" : pct >= 90 ? "text-amber-600" : "text-red-600"}`}>{pct}%</p>
      </div>
      <div className="h-1.5 w-full rounded-full bg-slate-200">
        <div className={`h-full rounded-full transition-all ${barColor}`} style={{ width: `${displayPct}%` }} />
      </div>
      {kpi.recommendation ? (
        <p className="text-xs text-slate-500 italic border-s-2 border-violet-300 ps-2">{String(kpi.recommendation)}</p>
      ) : null}
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

const TABS = ["KPI Dashboard", "SLA Records", "SLA Breaches", "Operations Advisor"] as const;
type Tab = typeof TABS[number];

export function SlaKpiPage() {
  const [tab, setTab] = useState<Tab>("KPI Dashboard");
  const [filterCat, setFilterCat]         = useState("");
  const [filterSlaType, setFilterSlaType] = useState("");

  const { data: kpiMetricsRaw = [], isLoading: kpiMetricsLoading } = useKpiMetrics();
  const { data: kpiSummaryRaw, isLoading: kpiSummaryLoading }      = useKpiSummary();
  const { data: kpiTargetsRaw = [] } = useKpiTargets();
  const { data: kpiAiRecsRaw = [] }  = useKpiAiRecs();
  const { data: slaSummaryRaw, isLoading: slaSummaryLoading }      = useSlaSummary();
  const { data: slaRecordsRaw = [] } = useSlaRecords();
  const { data: slaBreachesRaw = [] }= useSlaBreaches();

  const isLoading = kpiMetricsLoading || kpiSummaryLoading || slaSummaryLoading;

  const kpiMetrics  = (kpiMetricsRaw  as AnyRecord[]) ?? [];
  const kpiSummary  = (kpiSummaryRaw  ?? EMPTY_KPI_SUMMARY) as AnyRecord;
  const kpiAiRecs   = (kpiAiRecsRaw    as AnyRecord[]) ?? [];
  const slaSummary  = (slaSummaryRaw   ?? EMPTY_SLA_SUMMARY) as AnyRecord;
  const slaRecords  = (slaRecordsRaw   as AnyRecord[]) ?? [];
  const slaBreaches = (slaBreachesRaw  as AnyRecord[]) ?? [];

  void kpiTargetsRaw;

  const ackBreache     = useAcknowledgeSlaBreache();
  const resolveBreache = useResolveSlaBreache();

  const kpiCategories = [...new Set(kpiMetrics.map((k) => String(k.category ?? "")))].filter(Boolean);
  const slaTypes      = [...new Set(slaRecords.map((s) => String(s.sla_type ?? "")))].filter(Boolean);
  const filteredKpi   = filterCat     ? kpiMetrics.filter((k) => String(k.category) === filterCat) : kpiMetrics;
  const filteredSla   = filterSlaType ? slaRecords.filter((s) => String(s.sla_type) === filterSlaType) : slaRecords;

  // Attainment vs target, derived from the same live kpiMetrics the KPI cards use
  // (pct = actual / target, capped at 110 to mirror KpiCard). Bars colour by pct.
  const kpiAttainmentData = kpiMetrics.map((k) => {
    const actual = Number(k.actual_value ?? 0);
    const target = Number(k.target_value ?? 0);
    const pct = target > 0 ? Math.min(110, Math.round((actual / target) * 100)) : 0;
    return { name: String(k.kpi_name ?? ""), pct, status: String(k.status ?? "") };
  });

  function handleExport() {
    if (tab === "KPI Dashboard") exportCsv("kpi-metrics", kpiMetrics);
    else if (tab === "SLA Records") exportCsv("sla-records", slaRecords);
    else if (tab === "SLA Breaches") exportCsv("sla-breaches", slaBreaches);
    else exportCsv("kpi-ai-recommendations", kpiAiRecs);
  }

  if (isLoading) {
    return (
      <div className="flex h-full flex-col gap-6 overflow-y-auto">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900">SLA / KPI Center</h1>
          <p className="mt-0.5 text-sm text-slate-500">Key performance indicators, SLA compliance, breach exposure and AI recommendations</p>
        </div>
        <LoadingState />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto">

      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900">SLA / KPI Center</h1>
          <p className="mt-0.5 text-sm text-slate-500">Key performance indicators, SLA compliance, breach exposure and AI recommendations</p>
        </div>
        <button type="button" className="btn-secondary flex items-center gap-2 text-sm" onClick={handleExport}>
          <Download className="h-4 w-4" />Export
        </button>
      </div>

      {/* Summary strip */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-6">
        <div className="panel p-3 text-center col-span-1"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Total KPIs</p><p className="text-2xl font-extrabold text-slate-900">{Number(kpiSummary.total ?? 0)}</p></div>
        <div className="panel p-3 text-center col-span-1"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">On Target</p><p className="text-2xl font-extrabold text-emerald-700">{Number(kpiSummary.onTarget ?? 0)}</p></div>
        <div className="panel p-3 text-center col-span-1"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">At Risk</p><p className="text-2xl font-extrabold text-amber-700">{Number(kpiSummary.atRisk ?? 0)}</p></div>
        <div className="panel p-3 text-center col-span-1"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Critical</p><p className="text-2xl font-extrabold text-red-700">{Number(kpiSummary.critical ?? 0)}</p></div>
        <div className="panel p-3 text-center col-span-1"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">SLA Met</p><p className="text-2xl font-extrabold text-emerald-700">{Number(slaSummary.met ?? 0)}</p></div>
        <div className="panel p-3 text-center col-span-1"><p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">SLA Breached</p><p className="text-2xl font-extrabold text-red-700">{Number(slaSummary.breached ?? 0)}</p></div>
      </div>

      {/* KPI Attainment Bar Chart — always visible */}
      <div className="panel p-5">
        <p className="section-title mb-0.5">KPI Attainment vs Target (%)</p>
        <p className="text-xs text-slate-400 mb-4">Values above 100% indicate target exceeded; below 90% are highlighted critical</p>
        <ResponsiveContainer width="100%" height={200}>
          <BarChart data={kpiAttainmentData} margin={{ top: 4, right: 8, left: -16, bottom: 24 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
            <XAxis dataKey="name" tick={{ fontSize: 10, fill: "#94a3b8" }} angle={-30} textAnchor="end" interval={0} />
            <YAxis domain={[60, 115]} tick={{ fontSize: 10, fill: "#94a3b8" }} unit="%" />
            <Tooltip formatter={(v: unknown) => [`${Number(v ?? 0)}%`, "Attainment"]} contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8 }} />
            <ReferenceLine y={100} stroke="#0d9488" strokeDasharray="4 2" label={{ value: "Target", position: "right", fontSize: 10, fill: "#0d9488" }} />
            <ReferenceLine y={90}  stroke="#f59e0b" strokeDasharray="4 2" />
            <Bar dataKey="pct" radius={[4, 4, 0, 0]}>
              {kpiAttainmentData.map((d, i) => (
                <Cell key={i} fill={d.pct >= 100 ? "#0d9488" : d.pct >= 90 ? "#f59e0b" : "#dc2626"} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-slate-200 pb-px">
        {TABS.map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setTab(t)}
            className={`rounded-t-lg px-4 py-2 text-sm font-semibold transition ${tab === t ? "bg-teal-50 text-teal-700 border border-b-0 border-teal-300" : "text-slate-500 hover:text-slate-700"}`}
          >
            {t}
          </button>
        ))}
      </div>

      {/* KPI Dashboard */}
      {tab === "KPI Dashboard" && (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={() => setFilterCat("")} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${!filterCat ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 text-slate-500 hover:text-slate-700"}`}>All</button>
            {kpiCategories.map((c) => (
              <button key={c} type="button" onClick={() => setFilterCat(c === filterCat ? "" : c)} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${filterCat === c ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 text-slate-500 hover:text-slate-700"}`}>{c}</button>
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
            <button type="button" onClick={() => setFilterSlaType("")} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${!filterSlaType ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 text-slate-500 hover:text-slate-700"}`}>All Types</button>
            {slaTypes.map((t) => (
              <button key={t} type="button" onClick={() => setFilterSlaType(t === filterSlaType ? "" : t)} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${filterSlaType === t ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 text-slate-500 hover:text-slate-700"}`}>{t}</button>
            ))}
          </div>
          <div className="panel overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["SLA", "Customer", "Type", "Target", "Actual", "Attainment", "Status"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filteredSla.map((s, i) => {
                  const targetVal = Number(s.target_value ?? 0);
                  const actualVal = Number(s.actual_value ?? 0);
                  const unit      = String(s.unit ?? "");
                  const attain    = targetVal > 0 ? Math.round((actualVal / targetVal) * 100) : 0;
                  return (
                    <tr key={i} className="transition hover:bg-slate-50">
                      <td className="px-4 py-3">
                        <p className="font-medium text-slate-900">{String(s.sla_name ?? "")}</p>
                        {s.job_number ? <p className="text-xs text-slate-500">Job: {String(s.job_number)}</p> : null}
                      </td>
                      <td className="px-4 py-3 text-slate-700">{String(s.customer_name ?? "—")}</td>
                      <td className="px-4 py-3 text-slate-500 text-xs">{String(s.sla_type ?? "")}</td>
                      <td className="px-4 py-3 font-mono text-slate-700">{targetVal.toLocaleString(undefined, { maximumFractionDigits: 1 })} {unit}</td>
                      <td className="px-4 py-3 font-mono text-slate-700">{actualVal.toLocaleString(undefined, { maximumFractionDigits: 1 })} {unit}</td>
                      <td className="px-4 py-3">
                        <span className={`text-xs font-bold ${attain >= 100 ? "text-emerald-700" : attain >= 90 ? "text-amber-600" : "text-red-600"}`}>{attain}%</span>
                      </td>
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
              <CheckCircle2 className="mx-auto h-10 w-10 text-emerald-600" />
              <p className="mt-3 font-semibold text-slate-900">No open SLA breaches</p>
              <p className="text-sm text-slate-500">All SLA commitments are within acceptable thresholds.</p>
            </div>
          )}
          {slaBreaches.map((b, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <AlertTriangle className="h-5 w-5 shrink-0 text-red-600 mt-0.5" />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 flex-wrap">
                  <p className="font-semibold text-slate-900">{String(b.sla_name ?? "")}</p>
                  <StatusBadge status={String(b.status ?? "")} />
                  {b.customer_name ? <span className="text-xs text-slate-500">{String(b.customer_name)}</span> : null}
                </div>
                <p className="mt-1 text-sm text-slate-700 leading-relaxed">{String(b.breach_reason ?? "")}</p>
                <p className="mt-0.5 text-xs text-slate-500">Detected: {b.breach_detected_at ? new Date(String(b.breach_detected_at)).toLocaleString() : "—"}</p>
                {b.resolution_action ? <p className="mt-1 text-xs text-teal-600 italic">{String(b.resolution_action)}</p> : null}
              </div>
              <div className="flex flex-col gap-2 shrink-0">
                {String(b.status) === "Open" && (
                  <button type="button" className="btn-ghost py-1.5 px-3 text-xs" onClick={() => ackBreache.mutate(Number(b.id))}>Acknowledge</button>
                )}
                {String(b.status) !== "Resolved" && (
                  <button type="button" className="btn-primary py-1.5 px-3 text-xs" onClick={() => resolveBreache.mutate(Number(b.id))}>Resolve</button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* AI Advisor */}
      {tab === "Operations Advisor" && (
        <div className="space-y-3">
          {kpiAiRecs.map((rec, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-violet-50 border border-violet-200">
                <Bot className="h-4 w-4 text-violet-600" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 flex-wrap">
                  <p className="font-semibold text-slate-900">{String(rec.title ?? "")}</p>
                  <span className="rounded-full border border-violet-200 bg-violet-50 px-2 py-0.5 text-[10px] font-bold text-violet-700">Score {Number(rec.score ?? 0)}</span>
                  {rec.priority ? <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${String(rec.priority) === "High" ? "border-red-200 bg-red-50 text-red-700" : "border-amber-200 bg-amber-50 text-amber-700"}`}>{String(rec.priority)}</span> : null}
                </div>
                <p className="mt-1 text-sm text-slate-600 leading-relaxed">{String(rec.body ?? rec.description ?? "")}</p>
                {rec.action_label ? (
                  <button
                    type="button"
                    onClick={() => setTab(String(rec.action_type) === "sla_action" ? "SLA Breaches" : "KPI Dashboard")}
                    className="mt-2 text-xs font-semibold text-teal-600 hover:text-teal-700 transition"
                  >{String(rec.action_label)} →</button>
                ) : null}
              </div>
              <Target className="h-4 w-4 shrink-0 text-slate-400" />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
