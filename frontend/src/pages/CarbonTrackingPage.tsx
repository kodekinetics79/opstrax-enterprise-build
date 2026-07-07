import { useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useQuery } from "@tanstack/react-query";
import {
  AreaChart, Area, BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend,
} from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, KpiCard, ClayCard } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Live data clients — every figure below is derived from real tenant data.
// Per-vehicle emissions are computed server-side from fuel_transactions.
const carbonApi = () =>
  unwrap<AnyRecord[]>(apiClient.get("/api/carbon-emissions")).then((rows) =>
    rows.map((r) => ({
      ...r,
      vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "",
      vehicleType: r.vehicleType ?? r.vehicle_type ?? "",
      co2ThisMonth: Number(r.co2ThisMonth ?? r.co2_this_month ?? 0),
      kmThisMonth: Number(r.kmThisMonth ?? r.km_this_month ?? 0),
      idlingCo2: Number(r.idlingCo2 ?? r.idling_co2 ?? 0),
      co2PerKm: Number(r.co2PerKm ?? r.co2_per_km ?? 0),
      trend: r.trend ?? "Stable",
    }))
  );

// Real monthly trend (tonnes) grouped from fuel_transactions server-side.
const trendApi = () =>
  unwrap<AnyRecord[]>(apiClient.get("/api/carbon-emissions/trend")).then((rows) =>
    rows.map((r) => ({
      month: String(r.month ?? ""),
      emissions: Number(r.emissions ?? 0),
      idling: Number(r.idling ?? 0),
    }))
  );

// ── Helpers ──────────────────────────────────────────────────────────────────

function TrendBadge({ trend }: { trend: string }) {
  const cls =
    trend === "Improving"    ? "bg-teal-50 border-teal-200 text-teal-700" :
    trend === "Deteriorating"? "bg-red-50 border-red-200 text-red-700" :
    "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{trend}</span>;
}

function ProgressBar({ pct, color }: { pct: number; color: string }) {
  return (
    <div className="h-2 bg-slate-100 rounded-full overflow-hidden">
      <div className={`h-full ${color} rounded-full transition-all`} style={{ width: `${Math.min(pct, 100)}%` }} />
    </div>
  );
}

function EmptyPanel({ title, note }: { title: string; note: string }) {
  return (
    <div className="clay-card p-5">
      <p className="text-sm font-semibold text-slate-700 mb-1">{title}</p>
      <p className="text-xs text-slate-400">{note}</p>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function CarbonTrackingPage() {
  const [activeView, setActiveView] = useState<"overview" | "vehicles" | "targets">("overview");

  const q = useQuery({ queryKey: ["carbon-emissions"], queryFn: carbonApi });
  const trendQ = useQuery({ queryKey: ["carbon-trend"], queryFn: trendApi });
  const vehicles = (q.data ?? []) as AnyRecord[];
  const trend = (trendQ.data ?? []) as AnyRecord[];

  const totalCo2 = vehicles.reduce((s, v) => s + Number(v.co2ThisMonth ?? 0), 0);
  const totalKm  = vehicles.reduce((s, v) => s + Number(v.kmThisMonth ?? 0), 0);
  const avgIntensity = totalKm > 0 ? totalCo2 / totalKm : 0;
  const idlingCo2 = vehicles.reduce((s, v) => s + Number(v.idlingCo2 ?? 0), 0);
  const idlingShare = totalCo2 > 0 ? (idlingCo2 / totalCo2) * 100 : 0;

  // Scope 1 (direct fuel combustion) is what we actually measure. Scope 2 (purchased
  // energy) and Scope 3 (supply chain) require sources we do not yet track — shown
  // honestly as "not tracked" rather than fabricated.
  const scope1Tonnes = totalCo2 / 1000;

  // Month-over-month delta for the trend KPI — derived from the same live trend
  // series, only shown when at least two months have actually accrued.
  const lastMonth = trend.length >= 2 ? Number(trend[trend.length - 1]?.emissions ?? 0) : null;
  const prevMonth = trend.length >= 2 ? Number(trend[trend.length - 2]?.emissions ?? 0) : null;
  const momPct =
    lastMonth !== null && prevMonth !== null && prevMonth > 0
      ? ((lastMonth - prevMonth) / prevMonth) * 100
      : null;
  const momDelta = momPct !== null ? `${momPct >= 0 ? "+" : ""}${momPct.toFixed(1)}% vs last month` : undefined;
  const momTrendWord = momPct === null ? undefined : momPct <= 0 ? "Improving" : "Rising";

  // Top emitters — ranked from the live per-vehicle rows already in scope.
  const topEmitters = [...vehicles]
    .sort((a, b) => Number(b.co2ThisMonth ?? 0) - Number(a.co2ThisMonth ?? 0))
    .slice(0, 6);
  const maxVehCo2 = topEmitters.reduce((m, v) => Math.max(m, Number(v.co2ThisMonth ?? 0)), 0);

  if (q.isLoading) return <LoadingState />;

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Carbon Tracking</h1>
          <p className="text-sm text-slate-500 mt-0.5">Fleet CO₂ emissions from fuel data · Scope 1 direct emissions · idling waste</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("carbon-emissions", vehicles)}>Export CSV</button>
      </div>

      {/* KPI grid — every figure derived from live per-vehicle + trend data */}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-3 xl:grid-cols-5">
        <KpiCard
          label="CO₂ This Month"
          value={`${totalCo2.toLocaleString()} kg`}
          delta={momDelta}
          trend={momTrendWord}
        />
        <KpiCard
          label="Intensity (kg/km)"
          value={totalKm > 0 ? avgIntensity.toFixed(2) : "—"}
          delta={totalKm > 0 ? `${totalKm.toLocaleString()} km driven` : undefined}
        />
        <KpiCard
          label="Idling CO₂"
          value={`${idlingCo2.toLocaleString()} kg`}
          status={idlingCo2 > 0 ? "Waste" : undefined}
          delta={totalCo2 > 0 ? `${idlingShare.toFixed(1)}% of fleet CO₂` : undefined}
        />
        <KpiCard
          label="Scope 1 (tonnes)"
          value={scope1Tonnes.toFixed(1)}
          delta="Direct fuel combustion"
        />
        <KpiCard
          label="Vehicles Reporting"
          value={String(vehicles.length)}
          delta={topEmitters.length > 0 ? `Top: ${String(topEmitters[0]?.vehicleCode ?? "—")}` : undefined}
        />
      </div>

      {/* View tabs */}
      <div className="clay-card flex gap-1 p-1.5">
        {(["overview", "vehicles", "targets"] as const).map((v) => (
          <button key={v} type="button" onClick={() => setActiveView(v)}
            className={`px-4 py-2 rounded-lg text-sm font-medium capitalize transition-colors ${
              activeView === v ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"
            }`}>{v === "overview" ? "Overview" : v === "vehicles" ? "By Vehicle" : "Targets"}</button>
        ))}
      </div>

      {/* ── Overview ── */}
      {activeView === "overview" && (
        <div className="flex flex-col gap-4">
          {/* Dense two-column band: monthly trend chart beside top-emitter leaderboard */}
          <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
            {/* Monthly trend chart — real fuel_transactions grouped by month */}
            <ClayCard className="p-5 xl:col-span-2">
              <div className="mb-4 flex items-center justify-between gap-3">
                <p className="text-sm font-semibold text-slate-700">Monthly CO₂ Emissions (tonnes)</p>
                {momDelta && (
                  <span className={`text-xs font-semibold ${momPct !== null && momPct <= 0 ? "text-teal-600" : "text-amber-600"}`}>
                    {momDelta}
                  </span>
                )}
              </div>
              {trend.length >= 2 ? (
                <ResponsiveContainer width="100%" height={260}>
                  <AreaChart data={trend} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
                    <defs>
                      <linearGradient id="co2grad" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="5%"  stopColor={chart.teal600} stopOpacity={0.2} />
                        <stop offset="95%" stopColor={chart.teal600} stopOpacity={0} />
                      </linearGradient>
                    </defs>
                    <CartesianGrid strokeDasharray="3 3" stroke={tokens.border} />
                    <XAxis dataKey="month" tick={{ fontSize: 12 }} />
                    <YAxis tick={{ fontSize: 12 }} />
                    <Tooltip />
                    <Legend />
                    <Area type="monotone" dataKey="emissions" name="Emissions (t CO₂)" stroke={chart.teal600} fill="url(#co2grad)" strokeWidth={2} />
                    <Area type="monotone" dataKey="idling"    name="Idling (t CO₂)"    stroke={chart.slate400} fill="none" strokeDasharray="5 3" strokeWidth={1.5} />
                  </AreaChart>
                </ResponsiveContainer>
              ) : (
                <p className="text-xs text-slate-400 py-8 text-center">
                  Monthly trend appears once at least two months of fuel data have accrued
                  {trend.length === 1 ? " (1 month recorded so far)." : "."}
                </p>
              )}
            </ClayCard>

            {/* Top emitters leaderboard — ranked from live per-vehicle rows */}
            <ClayCard className="p-5">
              <div className="mb-4 flex items-center justify-between gap-3">
                <p className="text-sm font-semibold text-slate-700">Top Emitters (this month)</p>
                <button type="button" className="text-xs font-semibold text-teal-600 hover:underline" onClick={() => setActiveView("vehicles")}>
                  View all
                </button>
              </div>
              {topEmitters.length > 0 ? (
                <ol className="flex flex-col gap-3">
                  {topEmitters.map((v, i) => {
                    const co2 = Number(v.co2ThisMonth ?? 0);
                    const pct = maxVehCo2 > 0 ? (co2 / maxVehCo2) * 100 : 0;
                    return (
                      <li key={String(v.id ?? i)} className="flex flex-col gap-1.5">
                        <div className="flex items-center justify-between gap-2 text-xs">
                          <span className="flex items-center gap-2 min-w-0">
                            <span className="inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-slate-100 text-[10px] font-bold text-slate-500">{i + 1}</span>
                            <span className="truncate font-semibold text-slate-800">{String(v.vehicleCode ?? "—")}</span>
                            <span className="truncate text-[11px] text-slate-400">{String(v.vehicleType ?? "")}</span>
                          </span>
                          <span className="shrink-0 font-semibold tabular-nums text-slate-700">{co2.toLocaleString()} kg</span>
                        </div>
                        <ProgressBar pct={pct} color="bg-teal-500" />
                      </li>
                    );
                  })}
                </ol>
              ) : (
                <p className="text-xs text-slate-400 py-8 text-center">No fuel activity recorded this month.</p>
              )}
            </ClayCard>
          </div>

          {/* Scope breakdown + idling-by-month — honest: only Scope 1 is measured today */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <ClayCard className="p-5">
              <p className="text-sm font-semibold text-slate-700 mb-4">Scope Breakdown (GHG Protocol)</p>
              <div className="flex flex-col gap-3">
                <div>
                  <div className="flex justify-between text-xs mb-1.5">
                    <span className="text-slate-600 font-medium">Scope 1 (Direct — fuel combustion)</span>
                    <span className="font-semibold text-slate-800">{scope1Tonnes.toFixed(1)}t</span>
                  </div>
                  <ProgressBar pct={100} color="bg-teal-500" />
                </div>
                <div className="grid grid-cols-2 gap-3 pt-1">
                  <div className="rounded-xl bg-slate-50 px-3 py-2.5">
                    <p className="text-[10px] font-bold uppercase tracking-wide text-slate-400">Idling share</p>
                    <p className="mt-1 text-lg font-bold text-amber-600">{totalCo2 > 0 ? `${idlingShare.toFixed(1)}%` : "—"}</p>
                  </div>
                  <div className="rounded-xl bg-slate-50 px-3 py-2.5">
                    <p className="text-[10px] font-bold uppercase tracking-wide text-slate-400">Fleet intensity</p>
                    <p className="mt-1 text-lg font-bold text-slate-800">{totalKm > 0 ? `${avgIntensity.toFixed(2)}` : "—"}<span className="text-xs font-medium text-slate-400"> kg/km</span></p>
                  </div>
                </div>
                <p className="text-xs text-slate-400 pt-1">
                  Scope 2 (purchased energy) and Scope 3 (supply chain) are not yet tracked —
                  connect an energy/procurement source to report them.
                </p>
              </div>
            </ClayCard>

            <ClayCard className="p-5">
              <p className="text-sm font-semibold text-slate-700 mb-4">Idling CO₂ by Month (tonnes)</p>
              {trend.length >= 2 ? (
                <ResponsiveContainer width="100%" height={160}>
                  <BarChart data={trend} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
                    <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                    <YAxis tick={{ fontSize: 11 }} />
                    <Tooltip formatter={(v) => [`${String(v)} t`, "Idling CO₂"]} />
                    <Bar dataKey="idling" name="Idling t CO₂" fill={chart.teal600} radius={[3, 3, 0, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              ) : (
                <p className="text-xs text-slate-400 py-8 text-center">Accrues with monthly fuel data.</p>
              )}
            </ClayCard>
          </div>
        </div>
      )}

      {/* ── By Vehicle ── (already real) */}
      {activeView === "vehicles" && (
        <div className="clay-card overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Vehicle", "Type", "CO₂ This Month (kg)", "km Driven", "CO₂ Intensity (kg/km)", "Idling CO₂ (kg)", "Trend"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {vehicles.map((v, i) => (
                  <tr key={String(v.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(v.vehicleCode ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(v.vehicleType ?? "—")}</td>
                    <td className="px-4 py-3 font-semibold text-slate-800">{Number(v.co2ThisMonth ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-slate-600">{Number(v.kmThisMonth ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3">
                      <span className={`text-xs font-medium ${Number(v.co2PerKm ?? 0) > 0.7 ? "text-red-600" : Number(v.co2PerKm ?? 0) > 0.5 ? "text-amber-600" : "text-teal-600"}`}>
                        {Number(v.co2PerKm ?? 0).toFixed(2)}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-amber-600 text-xs font-medium">{Number(v.idlingCo2 ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3"><TrendBadge trend={String(v.trend ?? "Stable")} /></td>
                  </tr>
                ))}
                {vehicles.length === 0 && (
                  <tr><td colSpan={7} className="px-4 py-10 text-center text-sm text-slate-400">No fuel activity recorded for any vehicle this month.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Targets ── honest: reduction targets need a baseline the platform doesn't store yet */}
      {activeView === "targets" && (
        <EmptyPanel
          title="Reduction Targets"
          note="Set science-based reduction targets against a baseline year to track progress here. Target management is not yet configured for this tenant."
        />
      )}
    </div>
  );
}
