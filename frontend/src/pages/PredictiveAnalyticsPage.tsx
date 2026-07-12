import { useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useQuery } from "@tanstack/react-query";
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid,
  Tooltip, ResponsiveContainer, ReferenceLine, Cell,
} from "recharts";
import {
  AlertTriangle, Bot, BrainCircuit, Download, Shield, Truck, TrendingDown,
  Wrench, Zap, Clock, DollarSign, Activity,
} from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, ErrorState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── API ────────────────────────────────────────────────────────────────────────

const predictiveApi = {
  maintenance: () => unwrap<AnyRecord[]>(apiClient.get("/api/predictions/maintenance")).then((rows) =>
      rows.map((r) => ({ ...r, vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "", confidencePct: Number(r.confidencePct ?? r.confidence_pct ?? 0) }))
    ),
  driverRisk: () => unwrap<AnyRecord[]>(apiClient.get("/api/predictions/driver-risk")).then((rows) =>
      rows.map((r) => ({ ...r, driverName: r.driverName ?? r.driver_name ?? "", currentScore: Number(r.currentScore ?? r.current_score ?? 0) }))
    ),
  slaRisk: () => unwrap<AnyRecord[]>(apiClient.get("/api/predictions/sla-risk")).then((rows) =>
      rows.map((r) => ({ ...r, jobNumber: r.jobNumber ?? r.job_number ?? "", delayProbability: Number(r.delayProbability ?? r.delay_probability ?? 0) }))
    ),
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function PriorityBadge({ p }: { p: string }) {
  const cls =
    p === "Critical" ? "bg-red-50 border-red-200 text-red-700" :
    p === "High"     ? "bg-amber-50 border-amber-200 text-amber-700" :
    p === "Urgent"   ? "bg-red-50 border-red-200 text-red-700" :
    "bg-slate-50 border-slate-200 text-slate-500";
  return <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${cls}`}>{p}</span>;
}

function ConfidenceBar({ pct }: { pct: number }) {
  const color = pct >= 85 ? "bg-red-500" : pct >= 70 ? "bg-amber-500" : "bg-slate-300";
  return (
    <div className="flex items-center gap-2 min-w-28">
      <div className="flex-1 h-1.5 bg-slate-100 rounded-full overflow-hidden">
        <div className={`h-full ${color} rounded-full`} style={{ width: `${pct}%` }} />
      </div>
      <span className={`text-xs font-bold tabular-nums ${pct >= 85 ? "text-red-600" : pct >= 70 ? "text-amber-600" : "text-slate-500"}`}>{pct}%</span>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type FeedTab = "maintenance" | "driver-risk" | "sla-risk";

export function PredictiveAnalyticsPage() {
  const [feed, setFeed] = useState<FeedTab>("maintenance");
  const [expandedId, setExpandedId] = useState<number | null>(null);

  const maintQ  = useQuery({ queryKey: ["predictions-maintenance"], queryFn: predictiveApi.maintenance, staleTime: 60_000 });
  const driverQ = useQuery({ queryKey: ["predictions-driver-risk"],  queryFn: predictiveApi.driverRisk,  staleTime: 60_000 });
  const slaQ    = useQuery({ queryKey: ["predictions-sla-risk"],     queryFn: predictiveApi.slaRisk,     staleTime: 60_000 });

  const maintenance = (maintQ.data  ?? []) as AnyRecord[];
  const driverRisk  = (driverQ.data ?? []) as AnyRecord[];
  const slaRisk     = (slaQ.data   ?? []) as AnyRecord[];

  const isLoading = maintQ.isLoading || driverQ.isLoading || slaQ.isLoading;

  const totalRevenueAtRisk =
    maintenance.reduce((s, r) => s + Number(r.revenueAtRisk ?? 0), 0) +
    driverRisk.reduce((s, r)  => s + Number(r.revenueImpact ?? 0), 0) +
    slaRisk.reduce((s, r)    => s + Number(r.revenueAtRisk ?? 0), 0);

  const criticalMaint  = maintenance.filter((r) => String(r.priority) === "Critical").length;
  const urgentDrivers  = driverRisk.filter((r) => ["Urgent","Critical"].includes(String(r.coachingStatus))).length;
  const highSlaRisk    = slaRisk.filter((r) => Number(r.delayProbability) >= 70).length;
  const avgConfidence  = maintenance.length > 0
    ? Math.round(maintenance.reduce((s, r) => s + Number(r.confidencePct ?? 0), 0) / maintenance.length)
    : 0;

  function handleExport() {
    const rows = [
      ...maintenance.map((r) => ({ type: "Maintenance", entity: String(r.vehicleCode), risk: String(r.failureType), confidence: `${Number(r.confidencePct)}%`, daysUntil: Number(r.daysUntilRisk), revenueAtRisk: Number(r.revenueAtRisk) })),
      ...driverRisk.map((r)  => ({ type: "Driver Safety", entity: String(r.driverName), risk: String(r.riskFactor), confidence: "—", daysUntil: 7, revenueAtRisk: Number(r.revenueImpact) })),
      ...slaRisk.map((r)    => ({ type: "SLA Risk", entity: String(r.jobNumber), risk: String(r.delayReason), confidence: `${Number(r.delayProbability)}%`, daysUntil: 0, revenueAtRisk: Number(r.revenueAtRisk) })),
    ];
    exportCsv("predictive-analytics", rows);
  }

  if (isLoading) return <LoadingState />;
  if (maintQ.isError) return <ErrorState message={(maintQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">

      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <BrainCircuit className="h-5 w-5 text-violet-600" />
            <span className="text-xs font-bold uppercase tracking-widest text-violet-600">AI-Powered Intelligence</span>
          </div>
          <h1 className="text-xl font-bold text-slate-900">Fleet Intelligence Center</h1>
          <p className="text-sm text-slate-500 mt-0.5">Predictive risk scoring for maintenance, driver safety, and SLA performance</p>
        </div>
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-1.5 rounded-full border border-violet-200 bg-violet-50 px-3 py-1.5">
            <Bot className="h-3.5 w-3.5 text-violet-600" />
            <span className="text-xs font-semibold text-violet-700">ML Models Active</span>
          </div>
          <button type="button" className="btn-secondary flex items-center gap-2 text-sm" onClick={handleExport}>
            <Download className="h-4 w-4" />Export All Risks
          </button>
        </div>
      </div>

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 xl:grid-cols-5">
        {[
          { label: "Revenue at Risk",     value: `AED ${(totalRevenueAtRisk / 1000).toFixed(1)}K`, icon: <DollarSign className="h-5 w-5" />, accent: "text-red-700",    bg: "bg-red-50" },
          { label: "Maintenance Alerts",  value: `${criticalMaint} Critical`,                        icon: <Wrench className="h-5 w-5" />,     accent: "text-amber-700", bg: "bg-amber-50" },
          { label: "Drivers at Risk",     value: `${urgentDrivers} Urgent`,                          icon: <Shield className="h-5 w-5" />,     accent: "text-amber-700", bg: "bg-amber-50" },
          { label: "SLA Breach Risk",     value: `${highSlaRisk} High Risk`,                         icon: <Clock className="h-5 w-5" />,      accent: "text-red-700",   bg: "bg-red-50" },
          { label: "Avg ML Confidence",   value: `${avgConfidence}%`,                                icon: <Activity className="h-5 w-5" />,   accent: "text-violet-700",bg: "bg-violet-50" },
        ].map((k) => (
          <div key={k.label} className="panel flex items-start gap-3 p-4">
            <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${k.bg} ${k.accent}`}>
              {k.icon}
            </div>
            <div>
              <p className={`text-lg font-bold ${k.accent}`}>{k.value}</p>
              <p className="text-xs text-slate-500 font-medium">{k.label}</p>
            </div>
          </div>
        ))}
      </div>

      {/* Charts row */}
      <div className="grid gap-4 xl:grid-cols-[1.6fr_1fr]">

        {/* Open risk items right now, by category (live snapshot — no historical trend data is collected yet) */}
        <div className="panel p-5">
          <p className="section-title mb-0.5">Open Risk Items by Category</p>
          <p className="text-xs text-slate-400 mb-4">Current predictive risk items — maintenance, safety and SLA</p>
          <ResponsiveContainer width="100%" height={200}>
            <BarChart
              data={[
                { cat: "Maintenance", value: maintenance.length, color: chart.amber500 },
                { cat: "Safety",      value: driverRisk.length,  color: chart.red600 },
                { cat: "SLA",         value: slaRisk.length,     color: chart.teal600 },
              ]}
              margin={{ top: 4, right: 8, left: -16, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke={tokens.border} />
              <XAxis dataKey="cat" tick={{ fontSize: 10, fill: chart.slate400 }} />
              <YAxis tick={{ fontSize: 10, fill: chart.slate400 }} allowDecimals={false} />
              <Tooltip contentStyle={{ background: tokens.surface, border: `1px solid ${tokens.border}`, borderRadius: 8 }} />
              <Bar dataKey="value" name="Open items" radius={[4, 4, 0, 0]}>
                {[chart.amber500, chart.red600, chart.teal600].map((c, i) => <Cell key={i} fill={c} />)}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>

        {/* Revenue at risk by category */}
        <div className="panel p-5">
          <p className="section-title mb-0.5">Revenue at Risk by Category</p>
          <p className="text-xs text-slate-400 mb-4">AED × estimated operational exposure</p>
          <ResponsiveContainer width="100%" height={200}>
            <BarChart
              data={[
                { cat: "SLA Breach",   value: Math.round(slaRisk.reduce((s, r) => s + Number(r.revenueAtRisk ?? 0), 0) / 1000) },
                { cat: "Maintenance",  value: Math.round(maintenance.reduce((s, r) => s + Number(r.revenueAtRisk ?? 0), 0) / 1000) },
                { cat: "Driver Risk",  value: Math.round(driverRisk.reduce((s, r) => s + Number(r.revenueImpact ?? 0), 0) / 1000) },
              ]}
              margin={{ top: 4, right: 8, left: -16, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke={tokens.border} />
              <XAxis dataKey="cat" tick={{ fontSize: 10, fill: chart.slate400 }} />
              <YAxis tick={{ fontSize: 10, fill: chart.slate400 }} unit="K" />
              <Tooltip formatter={(v: unknown) => [`AED ${Number(v ?? 0)}K`, "At Risk"]} contentStyle={{ background: tokens.surface, border: `1px solid ${tokens.border}`, borderRadius: 8 }} />
              <ReferenceLine y={0} stroke={tokens.border} />
              <Bar dataKey="value" name="AED K" fill={chart.red600} radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Feed tabs */}
      <div className="panel flex gap-1 p-1.5">
        {([
          { key: "maintenance", label: "Maintenance Predictions", icon: <Wrench className="h-3.5 w-3.5" />, count: maintenance.length },
          { key: "driver-risk", label: "Driver Safety Risk",      icon: <Shield className="h-3.5 w-3.5" />,  count: driverRisk.length },
          { key: "sla-risk",    label: "SLA Breach Risk",         icon: <Clock className="h-3.5 w-3.5" />,  count: slaRisk.length },
        ] as const).map(({ key, label, icon, count }) => (
          <button key={key} type="button" onClick={() => { setFeed(key); setExpandedId(null); }}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${feed === key ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"}`}>
            {icon}{label}
            <span className={`rounded-full px-1.5 py-0.5 text-[10px] font-bold ${feed === key ? "bg-white/20 text-white" : "bg-slate-100 text-slate-500"}`}>{count}</span>
          </button>
        ))}
      </div>

      {/* ── Maintenance Predictions ── */}
      {feed === "maintenance" && (
        <div className="flex flex-col gap-3">
          {maintenance.map((r) => {
            const id = Number(r.id);
            const open = expandedId === id;
            return (
              <div key={id} className="panel overflow-hidden">
                <button type="button" className="w-full text-left p-4" onClick={() => setExpandedId(open ? null : id)}>
                  <div className="flex items-center gap-4 flex-wrap">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-amber-50">
                      <Truck className="h-4 w-4 text-amber-600" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-slate-900">{String(r.vehicleCode)}</span>
                        <PriorityBadge p={String(r.priority)} />
                        <span className="text-xs text-slate-500">{String(r.vehicleType)}</span>
                      </div>
                      <p className="text-sm text-slate-600 mt-0.5">{String(r.failureType)} · {Number(r.daysUntilRisk)} days until predicted risk</p>
                    </div>
                    <div className="flex items-center gap-6 shrink-0">
                      <ConfidenceBar pct={Number(r.confidencePct)} />
                      <div className="text-right">
                        <p className="text-sm font-bold text-red-600">AED {Number(r.estimatedCost).toLocaleString()}</p>
                        <p className="text-[10px] text-slate-400">Est. repair cost</p>
                      </div>
                    </div>
                  </div>
                </button>
                {open && (
                  <div className="px-4 pb-4 border-t border-slate-100 pt-3">
                    <div className="flex items-start gap-2">
                      <Bot className="h-4 w-4 text-violet-500 shrink-0 mt-0.5" />
                      <p className="text-sm text-slate-700 leading-relaxed">{String(r.aiRecommendation)}</p>
                    </div>
                    <div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-500">
                      <span>Last service: <strong>{String(r.lastService)}</strong></span>
                      <span>Revenue at risk: <strong className="text-amber-600">AED {Number(r.revenueAtRisk).toLocaleString()}</strong></span>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ── Driver Safety Risk ── */}
      {feed === "driver-risk" && (
        <div className="flex flex-col gap-3">
          {driverRisk.map((r) => {
            const id = Number(r.id);
            const open = expandedId === id;
            const delta = Number(r.scoreDelta);
            return (
              <div key={id} className="panel overflow-hidden">
                <button type="button" className="w-full text-left p-4" onClick={() => setExpandedId(open ? null : id)}>
                  <div className="flex items-center gap-4 flex-wrap">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-red-50">
                      <Shield className="h-4 w-4 text-red-500" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-slate-900">{String(r.driverName)}</span>
                        <PriorityBadge p={String(r.coachingStatus)} />
                        <span className="text-xs text-slate-400">{String(r.driverCode)}</span>
                      </div>
                      <p className="text-sm text-slate-600 mt-0.5">{String(r.riskFactor)}</p>
                    </div>
                    <div className="flex items-center gap-6 shrink-0">
                      <div className="text-right">
                        <p className="text-sm font-bold text-slate-900">{Number(r.currentScore)} → <span className="text-red-600">{Number(r.predictedScore)}</span></p>
                        <p className={`text-[10px] font-semibold ${delta < -8 ? "text-red-600" : delta < -4 ? "text-amber-600" : "text-slate-400"}`}>
                          <TrendingDown className="inline h-3 w-3 mr-0.5" />{delta}pt predicted
                        </p>
                      </div>
                      <div className="text-right">
                        <p className="text-sm font-bold text-amber-700">{Number(r.harshEvents)} events</p>
                        <p className="text-[10px] text-slate-400">this period</p>
                      </div>
                    </div>
                  </div>
                </button>
                {open && (
                  <div className="px-4 pb-4 border-t border-slate-100 pt-3">
                    <div className="flex items-start gap-2">
                      <Bot className="h-4 w-4 text-violet-500 shrink-0 mt-0.5" />
                      <p className="text-sm text-slate-700 leading-relaxed">{String(r.aiRecommendation)}</p>
                    </div>
                    {Number(r.revenueImpact) > 0 && (
                      <p className="mt-2 text-xs text-slate-500">Revenue impact if unaddressed: <strong className="text-amber-600">AED {Number(r.revenueImpact).toLocaleString()}</strong></p>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ── SLA Breach Risk ── */}
      {feed === "sla-risk" && (
        <div className="flex flex-col gap-3">
          {slaRisk.map((r) => {
            const id = Number(r.id);
            const open = expandedId === id;
            const prob = Number(r.delayProbability);
            return (
              <div key={id} className="panel overflow-hidden">
                <button type="button" className="w-full text-left p-4" onClick={() => setExpandedId(open ? null : id)}>
                  <div className="flex items-center gap-4 flex-wrap">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-teal-50">
                      <Zap className="h-4 w-4 text-teal-600" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-slate-900">{String(r.jobNumber)}</span>
                        <span className="text-xs text-slate-500">{String(r.customerName)}</span>
                        <span className="text-xs text-slate-400">{String(r.route)}</span>
                      </div>
                      <p className="text-sm text-slate-600 mt-0.5">{String(r.delayReason)}</p>
                    </div>
                    <div className="flex items-center gap-6 shrink-0">
                      <ConfidenceBar pct={prob} />
                      <div className="text-right">
                        <p className="text-sm font-bold text-red-600">AED {Number(r.revenueAtRisk).toLocaleString()}</p>
                        <p className="text-[10px] text-slate-400">at risk</p>
                      </div>
                      <div className="text-right min-w-20">
                        <p className="text-xs text-slate-500">ETA: <strong className="text-slate-700">{String(r.currentEta)}</strong></p>
                        <p className="text-xs text-slate-500">SLA: <strong className="text-red-600">{String(r.slaDeadline)}</strong></p>
                      </div>
                    </div>
                  </div>
                </button>
                {open && (
                  <div className="px-4 pb-4 border-t border-slate-100 pt-3">
                    <div className="flex items-start gap-2">
                      <Bot className="h-4 w-4 text-violet-500 shrink-0 mt-0.5" />
                      <p className="text-sm text-slate-700 leading-relaxed">{String(r.aiRecommendation)}</p>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Empty alert */}
      {(feed === "maintenance" && maintenance.length === 0) ||
       (feed === "driver-risk" && driverRisk.length === 0)  ||
       (feed === "sla-risk"    && slaRisk.length === 0) ? (
        <div className="panel p-10 flex flex-col items-center gap-3 text-center">
          <AlertTriangle className="h-8 w-8 text-slate-300" />
          <p className="text-slate-500 text-sm">No predictions available at this time.</p>
        </div>
      ) : null}

    </div>
  );
}
