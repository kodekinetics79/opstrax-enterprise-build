import type { ReactNode } from "react";
import { Bot, BarChart2, TrendingUp, TrendingDown, Minus, Shield, Zap, Download,
         Truck, AlertTriangle, CheckCircle2, DollarSign, Activity, Users, Clock } from "lucide-react";
import {
  useExecutiveSummary, useExecutiveSnapshots, useExecutiveAiRecs,
} from "@/hooks/useBatch7";
import {
  LineChart, Line, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip,
  Legend, ResponsiveContainer, ReferenceLine,
} from "recharts";
import { useNavigate } from "react-router-dom";
import { exportCsv } from "@/components/ui";
import type { AnyRecord } from "@/types";

const emptySummary = {
  overall_score: 0,
  fleet_health_score: 0,
  safety_score: 0,
  compliance_score: 0,
  financial_score: 0,
  ai_brief: "No executive summary available yet.",
  snapshot_date: "",
};

// Navigation shortcuts to the live, backend-driven detail pages. These are UI
// structure only (label/icon/route) — they intentionally carry no metric value,
// because the executive summary endpoint does not expose these per-domain KPIs.
// Each card links to the page that owns the real, tenant-scoped data.
const KPI_NAV_TILES = [
  { label: "Active Vehicles", accent: "text-sky-700", icon: "truck", route: "/vehicles" },
  { label: "Jobs Today", accent: "text-teal-700", icon: "activity", route: "/jobs" },
  { label: "Revenue (MTD)", accent: "text-emerald-700", icon: "dollar", route: "/profitability" },
  { label: "Open Alerts", accent: "text-red-700", icon: "alert", route: "/alerts" },
  { label: "Fleet Utilization", accent: "text-amber-700", icon: "clock", route: "/fleet-utilization" },
  { label: "Driver Score", accent: "text-violet-700", icon: "users", route: "/driver-scorecards" },
  { label: "SLA Compliance", accent: "text-teal-700", icon: "shield", route: "/sla-kpi" },
  { label: "Cost / km", accent: "text-amber-700", icon: "dollar", route: "/fuel-idling" },
];

// ── Helpers ────────────────────────────────────────────────────────────────────

const ICON_MAP: Record<string, ReactNode> = {
  truck:    <Truck className="h-5 w-5" />,
  activity: <Activity className="h-5 w-5" />,
  dollar:   <DollarSign className="h-5 w-5" />,
  alert:    <AlertTriangle className="h-5 w-5" />,
  clock:    <Clock className="h-5 w-5" />,
  users:    <Users className="h-5 w-5" />,
  shield:   <Shield className="h-5 w-5" />,
};

function ScoreRing({ value, label, color }: { value: number; label: string; color: string }) {
  const pct = Math.min(100, Math.max(0, value));
  const r = 32;
  const circ = 2 * Math.PI * r;
  const dash = (pct / 100) * circ;
  return (
    <div className="panel flex flex-col items-center p-4 gap-2">
      <svg width="80" height="80" viewBox="0 0 80 80">
        <circle cx="40" cy="40" r={r} fill="none" stroke="#e2e8f0" strokeWidth="7" />
        <circle cx="40" cy="40" r={r} fill="none" stroke={color} strokeWidth="7"
          strokeLinecap="round" strokeDasharray={`${dash} ${circ}`}
          strokeDashoffset={circ / 4} transform="rotate(-90 40 40)" />
        <text x="40" y="44" textAnchor="middle" fill="#0f172a" fontSize="16" fontWeight="800">{pct}</text>
      </svg>
      <p className="text-xs font-bold uppercase tracking-widest text-slate-500 text-center">{label}</p>
    </div>
  );
}

function TrendChip({ val }: { val: number }) {
  if (val > 0) return <span className="flex items-center gap-0.5 text-emerald-700 text-xs font-semibold"><TrendingUp className="h-3 w-3" />+{val}</span>;
  if (val < 0) return <span className="flex items-center gap-0.5 text-red-700 text-xs font-semibold"><TrendingDown className="h-3 w-3" />{val}</span>;
  return <span className="flex items-center gap-0.5 text-slate-500 text-xs"><Minus className="h-3 w-3" />0</span>;
}

function PriorityBadge({ p }: { p: string }) {
  const cls =
    p === "Critical" ? "bg-red-50 border-red-200 text-red-700" :
    p === "High"     ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-500";
  return <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${cls}`}>{p}</span>;
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function ExecutivePage() {
  const { data: summaryRaw }        = useExecutiveSummary();
  const { data: snapshotsRaw = [] } = useExecutiveSnapshots();
  const { data: aiRecsRaw = [] }    = useExecutiveAiRecs();
  const navigate = useNavigate();

  const summary   = summaryRaw   as AnyRecord | undefined;
  const snapshots = snapshotsRaw as AnyRecord[];
  const aiRecs    = aiRecsRaw    as AnyRecord[];

  const latest = summary?.latest as AnyRecord[] | undefined;
  const snap   = (latest?.[0] ?? (snapshots[0] ?? null)) as AnyRecord | null;
  const trend  = summary?.trend as AnyRecord[] | undefined;

  const displaySnap = snap ?? emptySummary;
  const displayRecs = aiRecs;

  const chartData = ((trend && trend.length > 0) ? trend : (snapshots.length > 0 ? snapshots.slice().reverse() : []))
    .map((s) => {
      const row = s as AnyRecord;
      return {
      date:       row.snapshot_date ? new Date(String(row.snapshot_date)).toLocaleDateString("en-US", { month: "short", day: "numeric" }) : String(row.date ?? "—"),
      Fleet:      Number(row.fleet_health_score ?? row.Fleet ?? 0),
      Safety:     Number(row.safety_score ?? row.Safety ?? 0),
      Compliance: Number(row.compliance_score ?? row.Compliance ?? 0),
      Financial:  Number(row.financial_score ?? row.Financial ?? 0),
      Overall:    Number(row.overall_score ?? row.Overall ?? 0),
      };
    });

  const kpiCritical  = Number(summary?.kpiCritical ?? 0);
  const slaBreaches  = Number(summary?.openSlaBreaches ?? 0);
  const auditActions = Number(summary?.auditActionsToday ?? 0);

  function handleExport() {
    exportCsv("executive-dashboard", [
      { metric: "Overall Score",     value: Number(displaySnap.overall_score ?? 0) },
      { metric: "Fleet Health",      value: Number(displaySnap.fleet_health_score ?? 0) },
      { metric: "Safety Score",      value: Number(displaySnap.safety_score ?? 0) },
      { metric: "Compliance Score",  value: Number(displaySnap.compliance_score ?? 0) },
      { metric: "Financial Score",   value: Number(displaySnap.financial_score ?? 0) },
      { metric: "Critical KPIs",     value: kpiCritical },
      { metric: "Open SLA Breaches", value: slaBreaches },
      { metric: "Audit Actions Today", value: auditActions },
    ]);
  }

  return (
    <div className="space-y-6">

      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900">Executive Dashboard</h1>
          <p className="mt-0.5 text-sm text-slate-500">Enterprise health scores, revenue posture, safety, compliance and top risks</p>
        </div>
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2 rounded-full border border-violet-200 bg-violet-50 px-3 py-1.5">
            <Bot className="h-3.5 w-3.5 text-violet-600" />
            <span className="text-xs font-semibold text-violet-700">AI Live Monitoring</span>
          </div>
          <button type="button" className="btn-secondary flex items-center gap-2 text-sm" onClick={handleExport}>
            <Download className="h-4 w-4" />Export Brief
          </button>
        </div>
      </div>

      {/* AI Brief */}
      <div className="panel p-5 border-l-2 border-teal-400">
        <div className="flex items-center gap-2 mb-2">
          <Zap className="h-4 w-4 text-teal-600" />
          <span className="text-xs font-bold uppercase tracking-widest text-teal-700">Executive Summary</span>
          {displaySnap.snapshot_date ? (
            <span className="text-xs text-slate-400">{new Date(String(displaySnap.snapshot_date)).toLocaleDateString()}</span>
          ) : null}
        </div>
        <p className="text-sm text-slate-700 leading-relaxed">{String(displaySnap.ai_brief ?? "")}</p>
      </div>

      {/* KPI navigation grid — links to the live detail pages (no fabricated values) */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 xl:grid-cols-8">
        {KPI_NAV_TILES.map((kpi) => (
          <button key={kpi.label} type="button" onClick={() => navigate(kpi.route)}
            className="panel flex flex-col gap-1 p-3 text-left transition hover:border-slate-300 hover:shadow-sm">
            <div className={`flex h-8 w-8 items-center justify-center rounded-lg bg-slate-50 ${kpi.accent}`}>
              {ICON_MAP[kpi.icon]}
            </div>
            <span className="mt-1 text-[10px] font-semibold text-slate-500 uppercase tracking-wide leading-tight">{kpi.label}</span>
            <span className="text-[10px] font-medium text-teal-600">View details →</span>
          </button>
        ))}
      </div>

      {/* Score Rings */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
        <ScoreRing value={Number(displaySnap.overall_score     ?? 0)} label="Overall"    color="#2dd4bf" />
        <ScoreRing value={Number(displaySnap.fleet_health_score ?? 0)} label="Fleet"     color="#38bdf8" />
        <ScoreRing value={Number(displaySnap.safety_score      ?? 0)} label="Safety"     color="#f87171" />
        <ScoreRing value={Number(displaySnap.compliance_score  ?? 0)} label="Compliance" color="#34d399" />
        <ScoreRing value={Number(displaySnap.financial_score   ?? 0)} label="Financial"  color="#f59e0b" />
      </div>

      {/* Alert strip */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <button type="button" className="panel flex items-center gap-3 p-4 text-left transition hover:border-slate-300" onClick={() => navigate("/alerts")}>
          <BarChart2 className="h-8 w-8 text-red-600 shrink-0" />
          <div>
            <p className="text-2xl font-extrabold text-red-700">{kpiCritical}</p>
            <p className="text-xs text-slate-500">Critical KPIs</p>
          </div>
        </button>
        <button type="button" className="panel flex items-center gap-3 p-4 text-left transition hover:border-slate-300" onClick={() => navigate("/sla-kpi")}>
          <Shield className="h-8 w-8 text-amber-600 shrink-0" />
          <div>
            <p className="text-2xl font-extrabold text-amber-700">{slaBreaches}</p>
            <p className="text-xs text-slate-500">Open SLA Breaches</p>
          </div>
        </button>
        <button type="button" className="panel flex items-center gap-3 p-4 text-left transition hover:border-slate-300" onClick={() => navigate("/audit-logs")}>
          <CheckCircle2 className="h-8 w-8 text-sky-600 shrink-0" />
          <div>
            <p className="text-2xl font-extrabold text-sky-700">{auditActions}</p>
            <p className="text-xs text-slate-500">Audit Actions Today</p>
          </div>
        </button>
      </div>

      {/* Charts row */}
      <div className="grid gap-6 xl:grid-cols-[1.4fr_1fr]">

        {/* Score trend */}
        <div className="panel p-5">
          <p className="section-title mb-0.5">Health Score Trend (Last 14 Days)</p>
          <p className="text-xs text-slate-400 mb-4">Composite scores across fleet, safety, compliance and financial dimensions</p>
          <ResponsiveContainer width="100%" height={240}>
            <LineChart data={chartData} margin={{ top: 4, right: 16, left: -16, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" />
              <XAxis dataKey="date" tick={{ fill: "#94a3b8", fontSize: 11 }} />
              <YAxis domain={[60, 100]} tick={{ fill: "#94a3b8", fontSize: 11 }} />
              <Tooltip contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8 }}
                labelStyle={{ color: "#475569" }} formatter={(v: unknown) => [`${Number(v ?? 0)}`, ""]} />
              <Legend wrapperStyle={{ color: "#64748b", fontSize: 12 }} />
              <ReferenceLine y={80} stroke="#e2e8f0" strokeDasharray="4 2" />
              <Line type="monotone" dataKey="Overall"    stroke="#0d9488" strokeWidth={2.5} dot={false} />
              <Line type="monotone" dataKey="Fleet"      stroke="#0284c7" strokeWidth={1.5} dot={false} />
              <Line type="monotone" dataKey="Safety"     stroke="#dc2626" strokeWidth={1.5} dot={false} />
              <Line type="monotone" dataKey="Compliance" stroke="#059669" strokeWidth={1.5} dot={false} />
              <Line type="monotone" dataKey="Financial"  stroke="#d97706" strokeWidth={1.5} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>

        {/* Revenue vs Cost */}
        <div className="panel p-5">
          <p className="section-title mb-0.5">Revenue vs Cost (AED '000)</p>
          <p className="text-xs text-slate-400 mb-4">6-month P&L trend with gross margin %</p>
          <ResponsiveContainer width="100%" height={240}>
            <BarChart data={[]} margin={{ top: 4, right: 8, left: -12, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="month" tick={{ fontSize: 11, fill: "#94a3b8" }} />
              <YAxis tick={{ fontSize: 11, fill: "#94a3b8" }} />
              <Tooltip contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8 }}
                formatter={(v: unknown, name: unknown) => [`AED ${Number(v ?? 0)}K`, String(name)]} />
              <Legend wrapperStyle={{ color: "#64748b", fontSize: 12 }} />
              <Bar dataKey="revenue" name="Revenue" fill="#0d9488" radius={[3, 3, 0, 0]} />
              <Bar dataKey="cost"    name="Cost"    fill="#e2e8f0" radius={[3, 3, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* AI Recommendations */}
      <div>
        <div className="flex items-center justify-between mb-3">
          <p className="section-title">Top Operational Risks</p>
          <button type="button" className="text-xs font-medium text-teal-600 hover:underline" onClick={() => navigate("/alerts")}>View all alerts →</button>
        </div>
        <div className="flex flex-col gap-3">
          {displayRecs.slice(0, 5).map((rec, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-violet-50 border border-violet-200">
                <Bot className="h-4 w-4 text-violet-600" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 flex-wrap mb-1">
                  <p className="font-semibold text-slate-900 text-sm">{String(rec.title ?? "")}</p>
                  {rec.priority ? <PriorityBadge p={String(rec.priority)} /> : null}
                  {rec.score ? <span className="rounded-full border border-violet-200 bg-violet-50 px-2 py-0.5 text-[10px] font-bold text-violet-700">Score {Number(rec.score)}</span> : null}
                </div>
                <p className="text-sm text-slate-600 leading-relaxed">{String(rec.body ?? rec.description ?? "")}</p>
                {rec.action_label ? (
                  <button type="button" className="mt-2 text-xs font-semibold text-teal-600 hover:text-teal-700 transition">
                    {String(rec.action_label)} →
                  </button>
                ) : null}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Snapshot History */}
      {snapshots.length > 0 && (
        <div className="panel overflow-x-auto">
          <p className="section-title px-4 pt-4 pb-3">Snapshot History</p>
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50">
                {["Date", "Overall", "Fleet", "Safety", "Compliance", "Financial", "7-Day Δ"].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {snapshots.slice(0, 10).map((s, i) => (
                <tr key={i} className="transition hover:bg-slate-50">
                  <td className="px-4 py-3 text-slate-500 text-xs whitespace-nowrap">{s.snapshot_date ? new Date(String(s.snapshot_date)).toLocaleDateString() : "—"}</td>
                  <td className="px-4 py-3 font-bold text-slate-900">{Number(s.overall_score ?? 0)}</td>
                  <td className="px-4 py-3 text-sky-700">{Number(s.fleet_health_score ?? 0)}</td>
                  <td className="px-4 py-3 text-red-700">{Number(s.safety_score ?? 0)}</td>
                  <td className="px-4 py-3 text-emerald-700">{Number(s.compliance_score ?? 0)}</td>
                  <td className="px-4 py-3 text-amber-700">{Number(s.financial_score ?? 0)}</td>
                  <td className="px-4 py-3"><TrendChip val={Number(s.week_over_week_delta ?? 0)} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
