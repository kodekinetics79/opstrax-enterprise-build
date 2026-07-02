import { useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useQuery } from "@tanstack/react-query";
import {
  AreaChart, Area, BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend,
} from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState } from "@/components/ui";
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
    <div className="panel">
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

  // Scope 1 (direct fuel combustion) is what we actually measure. Scope 2 (purchased
  // energy) and Scope 3 (supply chain) require sources we do not yet track — shown
  // honestly as "not tracked" rather than fabricated.
  const scope1Tonnes = totalCo2 / 1000;

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

      {/* KPI strip — all derived from live per-vehicle data */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "CO₂ This Month",     val: `${totalCo2.toLocaleString()} kg`,  accent: "text-slate-900" },
          { label: "Intensity (kg/km)",  val: totalKm > 0 ? avgIntensity.toFixed(2) : "—", accent: "text-slate-900" },
          { label: "Idling CO₂",         val: `${idlingCo2.toLocaleString()} kg`, accent: idlingCo2 > 0 ? "text-amber-600" : "text-slate-900" },
          { label: "Scope 1 (tonnes)",   val: scope1Tonnes.toFixed(1), accent: "text-slate-900" },
          { label: "Vehicles Reporting", val: String(vehicles.length), accent: "text-slate-900" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent}`}>{val}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* View tabs */}
      <div className="panel flex gap-1 p-1.5">
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
          {/* Monthly trend chart — real fuel_transactions grouped by month */}
          <div className="panel">
            <p className="text-sm font-semibold text-slate-700 mb-4">Monthly CO₂ Emissions (tonnes)</p>
            {trend.length >= 2 ? (
              <ResponsiveContainer width="100%" height={220}>
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
          </div>

          {/* Scope breakdown — honest: only Scope 1 is measured today */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="panel">
              <p className="text-sm font-semibold text-slate-700 mb-4">Scope Breakdown (GHG Protocol)</p>
              <div className="flex flex-col gap-3">
                <div>
                  <div className="flex justify-between text-xs mb-1.5">
                    <span className="text-slate-600 font-medium">Scope 1 (Direct — fuel combustion)</span>
                    <span className="font-semibold text-slate-800">{scope1Tonnes.toFixed(1)}t</span>
                  </div>
                  <ProgressBar pct={100} color="bg-teal-500" />
                </div>
                <p className="text-xs text-slate-400 pt-1">
                  Scope 2 (purchased energy) and Scope 3 (supply chain) are not yet tracked —
                  connect an energy/procurement source to report them.
                </p>
              </div>
            </div>

            <div className="panel">
              <p className="text-sm font-semibold text-slate-700 mb-4">Idling CO₂ by Month (tonnes)</p>
              {trend.length >= 2 ? (
                <ResponsiveContainer width="100%" height={140}>
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
            </div>
          </div>
        </div>
      )}

      {/* ── By Vehicle ── (already real) */}
      {activeView === "vehicles" && (
        <div className="panel overflow-hidden p-0">
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
