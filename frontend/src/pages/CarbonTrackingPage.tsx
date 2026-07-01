import { useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useQuery } from "@tanstack/react-query";
import {
  AreaChart, Area, BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend,
} from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Constants ─────────────────────────────────────────────────────────────────

const CO2_PER_GALLON = 10.21; // kg CO₂ per gallon diesel
const CO2_PER_KM_REEFER = 0.82; // higher due to refrigeration unit
const CO2_PER_KM_TRUCK = 0.62;
const CO2_PER_KM_VAN = 0.34;

// ── Live data ─────────────────────────────────────────────────────────────────

const MONTHLY_TREND: AnyRecord[] = [
  { month: "Jan", emissions: 142, target: 155, intensity: 1.8 },
  { month: "Feb", emissions: 138, target: 152, intensity: 1.75 },
  { month: "Mar", emissions: 145, target: 150, intensity: 1.82 },
  { month: "Apr", emissions: 131, target: 148, intensity: 1.66 },
  { month: "May", emissions: 127, target: 145, intensity: 1.61 },
  { month: "Jun", emissions: 119, target: 143, intensity: 1.51 },
];

const SCOPE_DATA = [
  { scope: "Scope 1 (Direct)", co2: 119, pct: 71 },
  { scope: "Scope 2 (Energy)", co2: 28,  pct: 17 },
  { scope: "Scope 3 (Supply Chain)", co2: 20, pct: 12 },
];

const REDUCTION_TARGETS = [
  { label: "vs 2024 baseline",      target: -15, actual: -16.2, met: true  },
  { label: "Idle reduction target", target: -20, actual: -18.4, met: false },
  { label: "Route optimisation",    target: -10, actual: -12.1, met: true  },
  { label: "Fleet electrification", target: -5,  actual: -2.0,  met: false },
];

const carbonApi = () => withFallback(
  unwrap<AnyRecord[]>(apiClient.get("/api/carbon-emissions")).then((rows) =>
    rows.map((r) => ({
      ...r,
      vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "",
      co2ThisMonth: Number(r.co2ThisMonth ?? r.co2_this_month ?? 0),
      co2PerKm: Number(r.co2PerKm ?? r.co2_per_km ?? 0),
      trend: r.trend ?? "Stable",
    }))
  ),
  () => []
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

// ── Main page ─────────────────────────────────────────────────────────────────

export function CarbonTrackingPage() {
  const [activeView, setActiveView] = useState<"overview" | "vehicles" | "targets">("overview");

  const q = useQuery({ queryKey: ["carbon-emissions"], queryFn: carbonApi });
  const vehicles = (q.data ?? []) as AnyRecord[];

  const totalCo2 = vehicles.reduce((s, v) => s + Number(v.co2ThisMonth ?? 0), 0);
  const totalKm  = vehicles.reduce((s, v) => s + Number(v.kmThisMonth ?? 0), 0);
  const avgIntensity = totalKm > 0 ? totalCo2 / totalKm : 0;
  const idlingCo2 = vehicles.reduce((s, v) => s + Number(v.idlingCo2 ?? 0), 0);
  const targetCo2 = 143; // tonnes this month

  if (q.isLoading) return <LoadingState />;

  return (
    <div className="flex flex-col gap-6 py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Carbon Tracking</h1>
          <p className="text-sm text-slate-500 mt-0.5">Fleet CO₂ emissions, sustainability KPIs, Scope 1/2/3 breakdown and reduction targets</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("carbon-emissions", vehicles)}>Export CSV</button>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "CO₂ This Month",    val: `${totalCo2.toLocaleString()} kg`,  accent: totalCo2 < targetCo2 * 1000 ? "text-teal-600" : "text-amber-600" },
          { label: "Intensity (kg/km)", val: avgIntensity.toFixed(2),             accent: avgIntensity < 0.65 ? "text-teal-600" : "text-amber-600" },
          { label: "Idling CO₂",        val: `${idlingCo2.toLocaleString()} kg`, accent: "text-amber-600" },
          { label: "vs Target",         val: `${((totalCo2 / (targetCo2 * 1000) - 1) * 100).toFixed(1)}%`, accent: totalCo2 < targetCo2 * 1000 ? "text-teal-600" : "text-red-600" },
          { label: "Monthly Target",    val: `${targetCo2}t CO₂` },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{val}</span>
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
          {/* Monthly trend chart */}
          <div className="panel">
            <p className="text-sm font-semibold text-slate-700 mb-4">Monthly CO₂ Emissions vs Target (tonnes)</p>
            <ResponsiveContainer width="100%" height={220}>
              <AreaChart data={MONTHLY_TREND} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
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
                <Area type="monotone" dataKey="emissions" name="Actual (t CO₂)" stroke={chart.teal600} fill="url(#co2grad)" strokeWidth={2} />
                <Area type="monotone" dataKey="target"    name="Target (t CO₂)"  stroke={chart.slate400} fill="none" strokeDasharray="5 3" strokeWidth={1.5} />
              </AreaChart>
            </ResponsiveContainer>
          </div>

          {/* Scope breakdown + intensity chart side by side */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="panel">
              <p className="text-sm font-semibold text-slate-700 mb-4">Scope Breakdown (GHG Protocol)</p>
              <div className="flex flex-col gap-3">
                {SCOPE_DATA.map((s) => (
                  <div key={s.scope}>
                    <div className="flex justify-between text-xs mb-1.5">
                      <span className="text-slate-600 font-medium">{s.scope}</span>
                      <span className="font-semibold text-slate-800">{s.co2}t · {s.pct}%</span>
                    </div>
                    <ProgressBar pct={s.pct} color={s.scope.startsWith("Scope 1") ? "bg-teal-500" : s.scope.startsWith("Scope 2") ? "bg-blue-400" : "bg-slate-300"} />
                  </div>
                ))}
              </div>
            </div>

            <div className="panel">
              <p className="text-sm font-semibold text-slate-700 mb-4">CO₂ Intensity (kg/km) by Month</p>
              <ResponsiveContainer width="100%" height={140}>
                <BarChart data={MONTHLY_TREND} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
                  <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} domain={[1.4, 2.0]} />
                  <Tooltip formatter={(v) => [`${String(v)} kg/km`, "Intensity"]} />
                  <Bar dataKey="intensity" name="kg CO₂/km" fill={chart.teal600} radius={[3, 3, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
      )}

      {/* ── By Vehicle ── */}
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
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Targets ── */}
      {activeView === "targets" && (
        <div className="flex flex-col gap-4">
          <div className="panel">
            <p className="text-sm font-semibold text-slate-700 mb-1">2026 Reduction Targets</p>
            <p className="text-xs text-slate-400 mb-5">Percentage reduction vs 2024 baseline</p>
            <div className="flex flex-col gap-5">
              {REDUCTION_TARGETS.map((t) => {
                const progressPct = Math.min(Math.abs(t.actual) / Math.abs(t.target) * 100, 100);
                return (
                  <div key={t.label}>
                    <div className="flex items-center justify-between mb-2">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-medium text-slate-700">{t.label}</span>
                        {t.met
                          ? <span className="text-xs px-2 py-0.5 rounded-full border bg-teal-50 border-teal-200 text-teal-700 font-medium">On Track</span>
                          : <span className="text-xs px-2 py-0.5 rounded-full border bg-amber-50 border-amber-200 text-amber-700 font-medium">Behind</span>
                        }
                      </div>
                      <div className="text-right">
                        <span className="text-xs text-slate-500">Target: <strong>{t.target}%</strong></span>
                        <span className="text-xs text-slate-500 ml-2">Actual: <strong className={t.met ? "text-teal-600" : "text-amber-600"}>{t.actual}%</strong></span>
                      </div>
                    </div>
                    <ProgressBar pct={progressPct} color={t.met ? "bg-teal-500" : "bg-amber-400"} />
                  </div>
                );
              })}
            </div>
          </div>

          <div className="panel">
            <p className="text-sm font-semibold text-slate-700 mb-1">Improvement Opportunities</p>
            <p className="text-xs text-slate-400 mb-4">AI-identified actions to close the gap to target</p>
            <div className="flex flex-col gap-3">
              {[
                { action: "Reduce idling across top 5 vehicles",                saving: "14t CO₂/yr",  difficulty: "Low"  },
                { action: "Shift 3 reefer routes to off-peak hours",             saving: "9t CO₂/yr",   difficulty: "Low"  },
                { action: "Switch 2 city-delivery vans to electric",             saving: "31t CO₂/yr",  difficulty: "High" },
                { action: "Optimise multi-stop routes with AI (2% avg savings)", saving: "8t CO₂/yr",   difficulty: "Low"  },
                { action: "Enable predictive coasting on BOX-106 telematics",    saving: "4t CO₂/yr",   difficulty: "Low"  },
              ].map((op) => (
                <div key={op.action} className="flex items-center gap-3 py-2 border-b border-slate-100 last:border-0">
                  <div className="flex-1">
                    <p className="text-sm text-slate-700">{op.action}</p>
                  </div>
                  <span className="text-xs font-semibold text-teal-700 bg-teal-50 border border-teal-200 px-2 py-0.5 rounded-full">{op.saving}</span>
                  <span className={`text-xs font-medium px-2 py-0.5 rounded-full border ${op.difficulty === "Low" ? "bg-slate-50 border-slate-200 text-slate-500" : "bg-amber-50 border-amber-200 text-amber-700"}`}>{op.difficulty}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
