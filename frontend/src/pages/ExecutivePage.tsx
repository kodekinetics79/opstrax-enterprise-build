import { Bot, BarChart2, TrendingUp, TrendingDown, Minus, Shield, Zap } from "lucide-react";
import {
  useExecutiveSummary, useExecutiveSnapshots, useExecutiveAiRecs,
} from "@/hooks/useBatch7";
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from "recharts";

type AnyRecord = Record<string, unknown>;

function ScoreRing({ value, label, color }: { value: number; label: string; color: string }) {
  const pct = Math.min(100, Math.max(0, value));
  const r = 32;
  const circ = 2 * Math.PI * r;
  const dash = (pct / 100) * circ;

  return (
    <div className="panel flex flex-col items-center p-4 gap-2">
      <svg width="80" height="80" viewBox="0 0 80 80">
        <circle cx="40" cy="40" r={r} fill="none" stroke="rgba(255,255,255,0.05)" strokeWidth="7" />
        <circle
          cx="40" cy="40" r={r}
          fill="none"
          stroke={color}
          strokeWidth="7"
          strokeLinecap="round"
          strokeDasharray={`${dash} ${circ}`}
          strokeDashoffset={circ / 4}
          transform="rotate(-90 40 40)"
        />
        <text x="40" y="44" textAnchor="middle" className="fill-white" style={{ fontSize: 16, fontWeight: 800 }}>
          {pct}
        </text>
      </svg>
      <p className="text-xs font-bold uppercase tracking-widest text-slate-400 text-center">{label}</p>
    </div>
  );
}

function TrendChip({ val }: { val: number }) {
  if (val > 0) return <span className="flex items-center gap-0.5 text-emerald-300 text-xs font-semibold"><TrendingUp className="h-3 w-3" />+{val}</span>;
  if (val < 0) return <span className="flex items-center gap-0.5 text-red-300 text-xs font-semibold"><TrendingDown className="h-3 w-3" />{val}</span>;
  return <span className="flex items-center gap-0.5 text-slate-400 text-xs"><Minus className="h-3 w-3" />0</span>;
}

export function ExecutivePage() {
  const { data: summaryRaw }        = useExecutiveSummary();
  const { data: snapshotsRaw = [] } = useExecutiveSnapshots();
  const { data: aiRecsRaw = [] }    = useExecutiveAiRecs();

  const summary   = summaryRaw   as AnyRecord | undefined;
  const snapshots = snapshotsRaw as AnyRecord[];
  const aiRecs    = aiRecsRaw    as AnyRecord[];

  const latest  = summary?.latest as AnyRecord[] | undefined;
  const snap    = latest?.[0] as AnyRecord | undefined;
  const trend   = summary?.trend as AnyRecord[] | undefined;

  const chartData = (trend ?? snapshots.slice().reverse()).map((s) => ({
    date: s.snapshot_date ? new Date(String(s.snapshot_date)).toLocaleDateString("en-US", { month: "short", day: "numeric" }) : "—",
    Fleet:      Number(s.fleet_health_score ?? 0),
    Safety:     Number(s.safety_score ?? 0),
    Compliance: Number(s.compliance_score ?? 0),
    Financial:  Number(s.financial_score ?? 0),
    Overall:    Number(s.overall_score ?? 0),
  }));

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-white">Executive Dashboard</h1>
          <p className="mt-0.5 text-sm text-slate-400">Enterprise-wide health, KPI drift, and AI-generated operational brief</p>
        </div>
        <div className="flex items-center gap-2 rounded-full border border-violet-400/20 bg-violet-400/7 px-3 py-1.5">
          <Bot className="h-3.5 w-3.5 text-violet-300" />
          <span className="text-xs font-semibold text-violet-300">AI Live Monitoring</span>
        </div>
      </div>

      {/* AI Brief */}
      {!!snap?.ai_brief && (
        <div className="panel p-5 border-l-2 border-teal-400/40">
          <div className="flex items-center gap-2 mb-2">
            <Zap className="h-4 w-4 text-teal-300" />
            <span className="text-xs font-bold uppercase tracking-widest text-teal-400">OpsTrax AI Executive Brief</span>
            {!!snap.snapshot_date && <span className="text-xs text-slate-500">{new Date(String(snap.snapshot_date)).toLocaleDateString()}</span>}
          </div>
          <p className="text-sm text-slate-300 leading-relaxed">{String(snap.ai_brief)}</p>
        </div>
      )}

      {/* Score Rings */}
      {snap && (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
          <ScoreRing value={Number(snap.overall_score    ?? 0)} label="Overall"    color="#2dd4bf" />
          <ScoreRing value={Number(snap.fleet_health_score ?? 0)} label="Fleet"    color="#38bdf8" />
          <ScoreRing value={Number(snap.safety_score     ?? 0)} label="Safety"     color="#f87171" />
          <ScoreRing value={Number(snap.compliance_score ?? 0)} label="Compliance" color="#34d399" />
          <ScoreRing value={Number(snap.financial_score  ?? 0)} label="Financial"  color="#facc15" />
        </div>
      )}

      {/* Alert strip */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div className="panel flex items-center gap-3 p-4">
          <BarChart2 className="h-8 w-8 text-red-300 flex-shrink-0" />
          <div>
            <p className="text-2xl font-extrabold text-red-300">{Number(summary?.kpiCritical ?? 0)}</p>
            <p className="text-xs text-slate-400">Critical KPIs</p>
          </div>
        </div>
        <div className="panel flex items-center gap-3 p-4">
          <Shield className="h-8 w-8 text-amber-300 flex-shrink-0" />
          <div>
            <p className="text-2xl font-extrabold text-amber-300">{Number(summary?.openSlaBreaches ?? 0)}</p>
            <p className="text-xs text-slate-400">Open SLA Breaches</p>
          </div>
        </div>
        <div className="panel flex items-center gap-3 p-4">
          <Zap className="h-8 w-8 text-sky-300 flex-shrink-0" />
          <div>
            <p className="text-2xl font-extrabold text-sky-300">{Number(summary?.auditActionsToday ?? 0)}</p>
            <p className="text-xs text-slate-400">Audit Actions Today</p>
          </div>
        </div>
      </div>

      {/* Trend Chart */}
      {chartData.length > 0 && (
        <div className="panel p-4">
          <p className="section-title mb-4">Score Trend (Last 14 Days)</p>
          <ResponsiveContainer width="100%" height={280}>
            <LineChart data={chartData} margin={{ top: 0, right: 16, left: -16, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.04)" />
              <XAxis dataKey="date" tick={{ fill: "#64748b", fontSize: 11 }} />
              <YAxis domain={[0, 100]} tick={{ fill: "#64748b", fontSize: 11 }} />
              <Tooltip
                contentStyle={{ background: "#0f172a", border: "1px solid rgba(255,255,255,0.08)", borderRadius: 8 }}
                labelStyle={{ color: "#94a3b8" }}
                formatter={(v: unknown) => [`${Number(v ?? 0)}`, ""]}
              />
              <Legend wrapperStyle={{ color: "#94a3b8", fontSize: 12 }} />
              <Line type="monotone" dataKey="Overall"    stroke="#2dd4bf" strokeWidth={2.5} dot={false} />
              <Line type="monotone" dataKey="Fleet"      stroke="#38bdf8" strokeWidth={1.5} dot={false} />
              <Line type="monotone" dataKey="Safety"     stroke="#f87171" strokeWidth={1.5} dot={false} />
              <Line type="monotone" dataKey="Compliance" stroke="#34d399" strokeWidth={1.5} dot={false} />
              <Line type="monotone" dataKey="Financial"  stroke="#facc15" strokeWidth={1.5} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* Snapshots table */}
      {snapshots.length > 0 && (
        <div className="panel overflow-x-auto">
          <p className="section-title px-4 pt-4">Snapshot History</p>
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-white/[0.07]">
                {["Date", "Overall", "Fleet", "Safety", "Compliance", "Financial", "7-Day Trend"].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/[0.04]">
              {snapshots.slice(0, 10).map((s, i) => (
                <tr key={i} className="transition hover:bg-white/[0.02]">
                  <td className="px-4 py-3 text-slate-400 text-xs whitespace-nowrap">{s.snapshot_date ? new Date(String(s.snapshot_date)).toLocaleDateString() : "—"}</td>
                  <td className="px-4 py-3 font-bold text-white">{Number(s.overall_score ?? 0)}</td>
                  <td className="px-4 py-3 text-sky-300">{Number(s.fleet_health_score ?? 0)}</td>
                  <td className="px-4 py-3 text-red-300">{Number(s.safety_score ?? 0)}</td>
                  <td className="px-4 py-3 text-emerald-300">{Number(s.compliance_score ?? 0)}</td>
                  <td className="px-4 py-3 text-yellow-300">{Number(s.financial_score ?? 0)}</td>
                  <td className="px-4 py-3"><TrendChip val={Number(s.week_over_week_delta ?? 0)} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* AI Recommendations */}
      {aiRecs.length > 0 && (
        <div className="space-y-3">
          <p className="section-title">AI Recommendations</p>
          {aiRecs.map((rec, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-violet-400/10 border border-violet-400/20">
                <Bot className="h-4 w-4 text-violet-300" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 flex-wrap">
                  <p className="font-semibold text-white">{String(rec.title ?? "")}</p>
                  <span className="rounded-full border border-violet-400/20 bg-violet-400/10 px-2 py-0.5 text-[10px] font-bold text-violet-300">Score {Number(rec.score ?? 0)}</span>
                  {!!rec.priority && (
                    <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${String(rec.priority) === "High" || String(rec.priority) === "Critical" ? "border-red-400/20 bg-red-400/10 text-red-300" : "border-amber-400/20 bg-amber-400/10 text-amber-300"}`}>
                      {String(rec.priority)}
                    </span>
                  )}
                </div>
                <p className="mt-1 text-sm text-slate-400">{String(rec.body ?? rec.description ?? "")}</p>
                {!!rec.action_label && (
                  <button className="mt-2 text-xs font-semibold text-teal-400 hover:text-teal-300 transition">{String(rec.action_label)}</button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
