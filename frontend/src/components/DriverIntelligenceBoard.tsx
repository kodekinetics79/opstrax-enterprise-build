import { useMemo } from "react";
import { CartesianGrid, Cell, ReferenceArea, ReferenceLine, ResponsiveContainer, Scatter, ScatterChart, Tooltip, XAxis, YAxis, ZAxis } from "recharts";
import type { AnyRecord } from "@/types";

// Driver Operations console. Every figure is derived from the live driver rows — it answers
// the three questions dispatch actually asks: who can I assign right now, whose credentials
// are about to lapse, and who is drifting toward a safety event. Restrained on purpose:
// neutral surface, colour used only as a signal, not decoration.

export type Triage = "ready" | "onduty" | "watch" | "blocked";

const TRIAGE: Record<Triage, { label: string; help: string; color: string }> = {
  ready:   { label: "Ready",   help: "Available, compliant, credentials current",        color: "#10b981" },
  onduty:  { label: "On duty", help: "Currently assigned to a load",                     color: "#3b82f6" },
  watch:   { label: "Watch",   help: "Compliance dip, rising risk or expiry within 30d", color: "#f59e0b" },
  blocked: { label: "Blocked", help: "Suspended, non-compliant or credential lapsed",    color: "#f43f5e" },
};
const ORDER: Triage[] = ["ready", "onduty", "watch", "blocked"];

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

const driverName = (row: AnyRecord) =>
  String(row.fullName ?? row.full_name ?? row.driverCode ?? row.driver_code ?? `Driver ${row.id}`);

export function DriverIntelligenceBoard({ rows, activeTriage, onTriageSelect }: {
  rows: AnyRecord[];
  activeTriage?: Triage | null;
  onTriageSelect?: (triage: Triage) => void;
}) {
  const model = useMemo(() => {
    const counts: Record<Triage, number> = { ready: 0, onduty: 0, watch: 0, blocked: 0 };
    for (const row of rows) counts[triageOf(row)] += 1;

    const runway = rows
      .map((row) => ({ row, days: daysUntil(row.licenseExpiry ?? row.license_expiry) }))
      .filter((item): item is { row: AnyRecord; days: number } => item.days !== null)
      .sort((a, b) => a.days - b.days)
      .slice(0, 5);

    const scatter = rows
      .map((row) => ({ name: driverName(row), safety: num(row, "safetyScore", "safety_score"), compliance: num(row, "complianceScore", "compliance_score"), triage: triageOf(row) }))
      .filter((point) => point.safety > 0 || point.compliance > 0);

    const priority = rows.filter((row) => triageOf(row) === "blocked" || triageOf(row) === "watch")
      .sort((a, b) => num(b, "riskScore", "risk_score") - num(a, "riskScore", "risk_score"))
      .slice(0, 4);

    return { counts, runway, scatter, priority, total: rows.length };
  }, [rows]);

  if (!model.total) return null;

  const ready = model.counts.ready;
  const needsAction = model.counts.blocked + model.counts.watch;
  const pct = Math.round((ready / model.total) * 100);

  return (
    <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-[0_1px_2px_rgba(15,23,42,0.04)]">
      {/* Header + availability rail */}
      <div className="border-b border-slate-100 p-6">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-400">Driver Operations</p>
            <div className="mt-2 flex items-baseline gap-2">
              <span className="text-4xl font-semibold tabular-nums leading-none text-slate-900">{ready}</span>
              <span className="text-sm font-medium text-slate-400">of {model.total} ready to dispatch</span>
            </div>
            <p className="mt-2 text-sm text-slate-500">{needsAction ? `${needsAction} driver${needsAction > 1 ? "s" : ""} need attention before assignment` : "Roster clear — no blockers"}</p>
          </div>
          <div className="text-right">
            <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-400">Dispatchable</p>
            <p className="mt-1 text-3xl font-semibold tabular-nums text-slate-900">{pct}<span className="text-lg text-slate-400">%</span></p>
          </div>
        </div>

        {/* proportional availability rail */}
        <div className="mt-5 flex h-2.5 w-full gap-px overflow-hidden rounded-full bg-slate-100">
          {ORDER.map((key) => {
            const width = model.total ? (model.counts[key] / model.total) * 100 : 0;
            if (width <= 0) return null;
            return <div key={key} title={`${TRIAGE[key].label}: ${model.counts[key]}`} style={{ width: `${width}%`, background: TRIAGE[key].color }} />;
          })}
        </div>

        {/* legend = click-to-filter */}
        <div className="mt-4 flex flex-wrap gap-x-6 gap-y-3">
          {ORDER.map((key) => {
            const meta = TRIAGE[key];
            const active = activeTriage === key;
            return (
              <button
                key={key}
                type="button"
                onClick={() => onTriageSelect?.(key)}
                title={meta.help}
                className={`group flex items-center gap-2 border-b-2 pb-1 transition ${active ? "border-current" : "border-transparent hover:border-slate-200"}`}
                style={active ? { color: meta.color } : undefined}
              >
                <span className="h-2 w-2 rounded-full" style={{ background: meta.color }} />
                <span className={`text-sm ${active ? "font-semibold text-slate-900" : "font-medium text-slate-500 group-hover:text-slate-700"}`}>{meta.label}</span>
                <span className="text-sm font-semibold tabular-nums text-slate-900">{model.counts[key]}</span>
              </button>
            );
          })}
          {activeTriage ? (
            <button type="button" onClick={() => onTriageSelect?.(activeTriage)} className="text-xs font-medium text-slate-400 underline-offset-2 hover:text-slate-600 hover:underline">
              Clear filter
            </button>
          ) : null}
        </div>

        {model.priority.length ? (
          <div className="mt-5 flex flex-wrap items-center gap-2">
            <span className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">Priority</span>
            {model.priority.map((row) => (
              <span key={String(row.id)} className="inline-flex items-center gap-1.5 rounded-md border border-slate-200 bg-slate-50 px-2 py-1 text-xs font-medium text-slate-700">
                <span className="h-1.5 w-1.5 rounded-full" style={{ background: TRIAGE[triageOf(row)].color }} />
                {driverName(row)}
              </span>
            ))}
          </div>
        ) : null}
      </div>

      {/* Lower: credential runway + risk quadrant */}
      <div className="grid gap-0 lg:grid-cols-2">
        <div className="border-b border-slate-100 p-6 lg:border-b-0 lg:border-r">
          <div className="flex items-center justify-between">
            <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">Credential Runway</p>
            <span className="text-[11px] font-medium text-slate-400">Soonest to lapse</span>
          </div>
          <div className="mt-4">
            {model.runway.length ? model.runway.map(({ row, days }) => {
              const urgent = days < 14;
              const soon = days < 30;
              const color = days < 0 || urgent ? "#f43f5e" : soon ? "#f59e0b" : "#94a3b8";
              const fill = Math.max(4, Math.min(100, Math.round((1 - days / 60) * 100)));
              return (
                <div key={String(row.id)} className="flex items-center gap-4 py-2.5">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-slate-800">{driverName(row)}</p>
                    <p className="mt-0.5 font-mono text-[11px] text-slate-400">{String(row.licenseNumber ?? row.license_number ?? "—")}</p>
                    <div className="mt-1.5 h-1 w-full overflow-hidden rounded-full bg-slate-100">
                      <div className="h-full rounded-full" style={{ width: `${fill}%`, background: color }} />
                    </div>
                  </div>
                  <span className="shrink-0 text-right text-sm font-semibold tabular-nums" style={{ color }}>
                    {days < 0 ? `${Math.abs(days)}d ago` : days === 0 ? "today" : `${days}d`}
                  </span>
                </div>
              );
            }) : <p className="mt-4 text-sm text-slate-400">No license expiry dates on file.</p>}
          </div>
        </div>

        <div className="p-6">
          <div className="flex items-center justify-between">
            <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">Safety × Compliance</p>
            <span className="text-[11px] font-medium text-rose-500">Lower-left = coach now</span>
          </div>
          <div className="mt-2 h-52">
            <ResponsiveContainer width="100%" height="100%">
              <ScatterChart margin={{ top: 10, right: 10, bottom: 4, left: -12 }}>
                <CartesianGrid stroke="#f1f5f9" />
                <ReferenceArea x1={50} x2={85} y1={50} y2={85} fill="#f43f5e" fillOpacity={0.05} />
                <XAxis type="number" dataKey="safety" name="Safety" domain={[50, 100]} ticks={[50, 70, 85, 100]} tick={{ fontSize: 10, fill: "#94a3b8" }} tickLine={false} axisLine={{ stroke: "#e2e8f0" }} />
                <YAxis type="number" dataKey="compliance" name="Compliance" domain={[50, 100]} ticks={[50, 70, 85, 100]} tick={{ fontSize: 10, fill: "#94a3b8" }} tickLine={false} axisLine={{ stroke: "#e2e8f0" }} />
                <ZAxis range={[55, 55]} />
                <ReferenceLine x={85} stroke="#cbd5e1" strokeDasharray="3 3" />
                <ReferenceLine y={85} stroke="#cbd5e1" strokeDasharray="3 3" />
                <Tooltip cursor={{ strokeDasharray: "3 3", stroke: "#cbd5e1" }} content={<ScatterTip />} />
                <Scatter data={model.scatter}>
                  {model.scatter.map((point, index) => <Cell key={index} fill={TRIAGE[point.triage].color} fillOpacity={0.85} />)}
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
    <div className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs shadow-lg">
      <p className="font-semibold text-slate-900">{point.name}</p>
      <p className="mt-0.5 text-slate-500">Safety <span className="font-semibold tabular-nums text-slate-700">{point.safety}</span> · Compliance <span className="font-semibold tabular-nums text-slate-700">{point.compliance}</span></p>
    </div>
  );
}
