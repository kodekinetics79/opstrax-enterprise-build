import { useMemo } from "react";
import { CalendarClock, Gauge, ShieldAlert, Sparkles } from "lucide-react";
import { CartesianGrid, Cell, ReferenceLine, ResponsiveContainer, Scatter, ScatterChart, Tooltip, XAxis, YAxis, ZAxis } from "recharts";
import type { AnyRecord } from "@/types";

// Decision-grade Drivers header. Every number here is computed from the live driver
// rows (no static marketing copy). It answers the only questions dispatch actually asks:
// who can I put on a load right now, whose credentials are about to lapse, and who needs
// coaching before the next safety event.

export type Triage = "ready" | "onduty" | "watch" | "blocked";

const TRIAGE: Record<Triage, { label: string; help: string; color: string; bg: string; border: string; text: string }> = {
  ready:   { label: "Ready to dispatch", help: "Available, compliant, credentials current", color: "#059669", bg: "bg-emerald-50", border: "border-emerald-200", text: "text-emerald-700" },
  onduty:  { label: "On duty / assigned",  help: "Currently moving a load",                  color: "#2563eb", bg: "bg-blue-50",    border: "border-blue-200",    text: "text-blue-700" },
  watch:   { label: "Watch",               help: "Compliance dip, rising risk or expiry < 30d", color: "#d97706", bg: "bg-amber-50",   border: "border-amber-200",   text: "text-amber-700" },
  blocked: { label: "Blocked",             help: "Suspended, non-compliant or credential lapsed", color: "#dc2626", bg: "bg-rose-50",    border: "border-rose-200",    text: "text-rose-700" },
};

const num = (row: AnyRecord, ...keys: string[]) => {
  for (const key of keys) {
    const value = row[key];
    if (value !== null && value !== undefined && value !== "" && !Number.isNaN(Number(value))) return Number(value);
  }
  return 0;
};

function daysUntil(value: unknown): number | null {
  if (!value) return null;
  const date = new Date(String(value));
  if (Number.isNaN(date.getTime())) return null;
  return Math.ceil((date.getTime() - Date.now()) / 86_400_000);
}

export function triageOf(row: AnyRecord): Triage {
  const status = String(row.status ?? "");
  const compliance = num(row, "complianceScore", "compliance_score");
  const risk = num(row, "riskScore", "risk_score");
  const expiry = daysUntil(row.licenseExpiry ?? row.license_expiry);
  const expired = expiry !== null && expiry < 0;
  const expiringSoon = expiry !== null && expiry <= 30;

  if (/suspend/i.test(status) || (compliance > 0 && compliance < 70) || expired || risk >= 70) return "blocked";
  if ((compliance > 0 && compliance < 85) || expiringSoon || risk >= 40) return "watch";
  if (/on route|at stop|delayed|en route|driving/i.test(status)) return "onduty";
  return "ready";
}

function driverName(row: AnyRecord) {
  return String(row.fullName ?? row.full_name ?? row.driverCode ?? row.driver_code ?? `Driver ${row.id}`);
}

export function DriverIntelligenceBoard({ rows, activeTriage, onTriageSelect }: {
  rows: AnyRecord[];
  activeTriage?: Triage | null;
  onTriageSelect?: (triage: Triage) => void;
}) {
  const model = useMemo(() => {
    const buckets: Record<Triage, AnyRecord[]> = { ready: [], onduty: [], watch: [], blocked: [] };
    for (const row of rows) buckets[triageOf(row)].push(row);

    const runway = rows
      .map((row) => ({ row, days: daysUntil(row.licenseExpiry ?? row.license_expiry) }))
      .filter((item): item is { row: AnyRecord; days: number } => item.days !== null)
      .sort((a, b) => a.days - b.days)
      .slice(0, 6);

    const scatter = rows.map((row) => ({
      name: driverName(row),
      safety: num(row, "safetyScore", "safety_score"),
      compliance: num(row, "complianceScore", "compliance_score"),
      triage: triageOf(row),
    })).filter((point) => point.safety > 0 || point.compliance > 0);

    const actionable = [...buckets.blocked, ...buckets.watch].slice(0, 6);
    return { buckets, runway, scatter, actionable, total: rows.length };
  }, [rows]);

  if (!model.total) return null;

  const lanes: { key: Triage; value: number }[] = (Object.keys(TRIAGE) as Triage[]).map((key) => ({ key, value: model.buckets[key].length }));
  const dispatchable = model.buckets.ready.length;
  const needsAction = model.buckets.blocked.length + model.buckets.watch.length;

  return (
    <section className="grid gap-5 xl:grid-cols-[minmax(0,1.05fr)_minmax(300px,.95fr)]">
      <div className="overflow-hidden rounded-[18px] border border-slate-200 bg-white shadow-sm">
        <div className="flex flex-wrap items-end justify-between gap-3 border-b border-slate-100 bg-gradient-to-r from-slate-50 via-blue-50 to-teal-50 p-5">
          <div>
            <p className="section-title flex items-center gap-2 text-blue-700"><Gauge className="h-4 w-4" /> Dispatch Readiness</p>
            <h2 className="mt-2 text-xl font-black text-slate-950">{dispatchable} of {model.total} drivers ready to roll now</h2>
            <p className="mt-1 text-sm text-slate-600">{needsAction ? `${needsAction} need attention before they can be assigned.` : "No blockers across the active roster."}</p>
          </div>
          <span className={`badge ${needsAction ? "border-amber-200 bg-amber-50 text-amber-700" : "border-emerald-200 bg-emerald-50 text-emerald-700"}`}>
            {Math.round((dispatchable / model.total) * 100)}% dispatchable
          </span>
        </div>

        <div className="grid gap-3 p-5 sm:grid-cols-2 lg:grid-cols-4">
          {lanes.map((lane) => {
            const meta = TRIAGE[lane.key];
            const pct = model.total ? Math.round((lane.value / model.total) * 100) : 0;
            const active = activeTriage === lane.key;
            const interactive = Boolean(onTriageSelect);
            return (
              <button
                key={lane.key}
                type="button"
                onClick={() => onTriageSelect?.(lane.key)}
                aria-pressed={active ? "true" : "false"}
                className={`rounded-2xl border p-4 text-left transition ${meta.border} ${meta.bg} ${interactive ? "cursor-pointer hover:shadow-md focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-400" : "cursor-default"} ${active ? "ring-2 ring-offset-1" : ""}`}
                style={active ? { boxShadow: `0 0 0 2px ${meta.color}` } : undefined}
              >
                <p className={`flex items-center justify-between text-sm font-black ${meta.text}`}>
                  {meta.label}
                  {active ? <span className="text-[10px] font-bold uppercase tracking-wide">Filtering</span> : null}
                </p>
                <p className="mt-2 text-3xl font-black text-slate-950">{lane.value}</p>
                <div className="mt-3 h-1.5 overflow-hidden rounded-full bg-white/70">
                  <div className="h-full rounded-full" style={{ width: `${pct}%`, background: meta.color }} />
                </div>
                <p className="mt-2 text-[11px] leading-tight text-slate-500">{meta.help}</p>
              </button>
            );
          })}
        </div>

        {model.actionable.length ? (
          <div className="border-t border-slate-100 p-5 pt-4">
            <p className="section-title flex items-center gap-2 text-rose-600"><ShieldAlert className="h-4 w-4" /> Act first</p>
            <div className="mt-3 flex flex-wrap gap-2">
              {model.actionable.map((row) => {
                const state = triageOf(row);
                const meta = TRIAGE[state];
                return (
                  <span key={String(row.id)} className={`inline-flex items-center gap-2 rounded-full border ${meta.border} ${meta.bg} px-3 py-1 text-xs font-bold ${meta.text}`}>
                    <span className="h-2 w-2 rounded-full" style={{ background: meta.color }} />
                    {driverName(row)}
                    <span className="font-semibold text-slate-500">· {String(row.recommendedAction ?? row.recommended_action ?? "Review")}</span>
                  </span>
                );
              })}
            </div>
          </div>
        ) : null}
      </div>

      <div className="space-y-5">
        <div className="overflow-hidden rounded-[18px] border border-slate-200 bg-white shadow-sm">
          <div className="flex items-center justify-between border-b border-slate-100 p-4">
            <p className="section-title flex items-center gap-2 text-amber-600"><CalendarClock className="h-4 w-4" /> Credential Runway</p>
            <span className="text-xs font-bold text-slate-400">Next to lapse</span>
          </div>
          <div className="divide-y divide-slate-100">
            {model.runway.length ? model.runway.map(({ row, days }) => {
              const tone = days < 0 ? "rose" : days <= 14 ? "rose" : days <= 30 ? "amber" : "slate";
              const toneClass = tone === "rose" ? "border-rose-200 bg-rose-50 text-rose-700" : tone === "amber" ? "border-amber-200 bg-amber-50 text-amber-700" : "border-slate-200 bg-slate-50 text-slate-600";
              return (
                <div key={String(row.id)} className="flex items-center justify-between gap-3 px-4 py-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-bold text-slate-900">{driverName(row)}</p>
                    <p className="text-xs text-slate-500">License {String(row.licenseNumber ?? row.license_number ?? "—")}</p>
                  </div>
                  <span className={`shrink-0 rounded-full border px-2.5 py-1 text-xs font-black ${toneClass}`}>
                    {days < 0 ? `Expired ${Math.abs(days)}d` : days === 0 ? "Expires today" : `${days}d left`}
                  </span>
                </div>
              );
            }) : <p className="px-4 py-5 text-sm text-slate-500">No license expiry dates on file.</p>}
          </div>
        </div>

        <div className="overflow-hidden rounded-[18px] border border-slate-200 bg-white shadow-sm">
          <div className="flex items-center justify-between border-b border-slate-100 p-4">
            <p className="section-title flex items-center gap-2 text-teal-600"><Sparkles className="h-4 w-4" /> Safety × Compliance</p>
            <span className="text-xs font-bold text-slate-400">Coach the bottom-left</span>
          </div>
          <div className="h-56 p-3">
            <ResponsiveContainer width="100%" height="100%">
              <ScatterChart margin={{ top: 8, right: 12, bottom: 8, left: -8 }}>
                <CartesianGrid stroke="#eef2f7" />
                <XAxis type="number" dataKey="safety" name="Safety" domain={[50, 100]} tick={{ fontSize: 10, fill: "#94a3b8" }} tickLine={false} axisLine={false} />
                <YAxis type="number" dataKey="compliance" name="Compliance" domain={[50, 100]} tick={{ fontSize: 10, fill: "#94a3b8" }} tickLine={false} axisLine={false} />
                <ZAxis range={[60, 60]} />
                <ReferenceLine x={85} stroke="#cbd5e1" strokeDasharray="4 4" />
                <ReferenceLine y={85} stroke="#cbd5e1" strokeDasharray="4 4" />
                <Tooltip cursor={{ strokeDasharray: "3 3" }} content={<ScatterTip />} />
                <Scatter data={model.scatter}>
                  {model.scatter.map((point, index) => <Cell key={index} fill={TRIAGE[point.triage].color} />)}
                </Scatter>
              </ScatterChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>
    </section>
  );
}

function ScatterTip({ active, payload }: { active?: boolean; payload?: { payload: { name: string; safety: number; compliance: number } }[] }) {
  if (!active || !payload?.length) return null;
  const point = payload[0].payload;
  return (
    <div className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs shadow-md">
      <p className="font-bold text-slate-900">{point.name}</p>
      <p className="text-slate-500">Safety {point.safety} · Compliance {point.compliance}</p>
    </div>
  );
}
