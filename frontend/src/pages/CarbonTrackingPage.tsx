import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  AreaChart, Area, BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend,
} from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, KpiCard } from "@/components/ui";
import { vehicles as seedVehicles } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";
import { Download, Leaf, TrendingDown, Sparkles, Activity, Target } from "lucide-react";

// ── Constants ─────────────────────────────────────────────────────────────────

const CO2_PER_GALLON = 10.21; // kg CO₂ per gallon diesel
const CO2_PER_KM_REEFER = 0.82; // higher due to refrigeration unit
const CO2_PER_KM_TRUCK = 0.62;
const CO2_PER_KM_VAN = 0.34;

// ── Seed ─────────────────────────────────────────────────────────────────────

const MONTHLY_TREND: AnyRecord[] = [
  { month: "Jan", emissions: 142, target: 155, intensity: 1.8 },
  { month: "Feb", emissions: 138, target: 152, intensity: 1.75 },
  { month: "Mar", emissions: 145, target: 150, intensity: 1.82 },
  { month: "Apr", emissions: 131, target: 148, intensity: 1.66 },
  { month: "May", emissions: 127, target: 145, intensity: 1.61 },
  { month: "Jun", emissions: 119, target: 143, intensity: 1.51 },
];

function buildVehicleSeed(): AnyRecord[] {
  const vTypes = ["Reefer", "Box Truck", "Van", "Flatbed", "Refrigerated Van"];
  return (seedVehicles as AnyRecord[]).map((v, i) => {
    const vType = vTypes[i % 5];
    const co2pkm = vType === "Reefer" ? CO2_PER_KM_REEFER : vType.includes("Van") ? CO2_PER_KM_VAN : CO2_PER_KM_TRUCK;
    const kmMonth = 4000 + i * 1800;
    const co2Month = Math.round(co2pkm * kmMonth);
    const gallonsMonth = Math.round(co2Month / CO2_PER_GALLON);
    return {
      id: i + 1,
      vehicleCode: String(v.vehicleId ?? ""),
      vehicleType: vType,
      kmThisMonth: kmMonth,
      co2ThisMonth: co2Month,
      co2PerKm: co2pkm,
      gallonsUsed: gallonsMonth,
      idlingCo2: Math.round(co2Month * 0.12),
      trend: (["Improving", "Stable", "Improving", "Deteriorating", "Stable"] as const)[i % 5],
    };
  });
}

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
  () => buildVehicleSeed()
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
    <div className="space-y-6 pb-10">

      {/* ── Hero header ─────────────────────────────────────────────── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Leaf className="h-3 w-3" /> Reports
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Fleet CO₂ emissions and sustainability KPIs</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Carbon Tracking
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Fleet CO₂ emissions, sustainability KPIs, Scope 1/2/3 breakdown and reduction targets
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button type="button" className="fh-btn-primary cursor-pointer" onClick={() => exportCsv("carbon-emissions", vehicles)}>
                <Download className="h-4 w-4" /> Export CSV
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* ── Ops intelligence bar ────────────────────────────────────── */}
      <div className="anim-fade-up relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
        <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-teal-500/10 blur-2xl" />
        <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-emerald-500/8 blur-2xl" />
        <div className="relative flex items-center gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-teal-400/20 to-teal-600/10 ring-1 ring-teal-400/20">
            <Sparkles className="h-5 w-5 text-teal-300" />
          </span>
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/80">Sustainability signal</p>
            <p className="mt-1 text-sm font-medium leading-relaxed text-slate-400">
              {totalCo2 < targetCo2 * 1000
                ? `Fleet emissions are ${((1 - totalCo2 / (targetCo2 * 1000)) * 100).toFixed(1)}% below target — on track for 2026 goals.`
                : `Fleet emissions exceed target by ${((totalCo2 / (targetCo2 * 1000) - 1) * 100).toFixed(1)}% — review idling and route optimisation.`}
            </p>
          </div>
        </div>
        {totalCo2 >= targetCo2 * 1000 && (
          <button type="button" onClick={() => setActiveView("targets")} className="cursor-pointer inline-flex items-center gap-2 self-start rounded-xl bg-gradient-to-r from-teal-500 to-teal-600 px-4 py-2.5 text-xs font-bold text-white shadow-lg shadow-teal-500/20 transition hover:from-teal-400 hover:to-teal-500 hover:shadow-teal-400/30 sm:self-auto">
            Review reduction targets <Target className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {/* ── KPI cards ───────────────────────────────────────────────── */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
        <KpiCard label="CO₂ This Month" value={`${totalCo2.toLocaleString()} kg`} icon={<Leaf className="h-4 w-4" />} status={totalCo2 < targetCo2 * 1000 ? undefined : "review"} />
        <KpiCard label="Intensity (kg/km)" value={avgIntensity.toFixed(2)} icon={<TrendingDown className="h-4 w-4" />} status={avgIntensity < 0.65 ? undefined : "review"} />
        <KpiCard label="Idling CO₂" value={`${idlingCo2.toLocaleString()} kg`} icon={<Activity className="h-4 w-4" />} status="review" />
        <KpiCard label="vs Target" value={`${((totalCo2 / (targetCo2 * 1000) - 1) * 100).toFixed(1)}%`} icon={<Target className="h-4 w-4" />} status={totalCo2 < targetCo2 * 1000 ? undefined : "review"} />
        <KpiCard label="Monthly Target" value={`${targetCo2}t CO₂`} icon={<Target className="h-4 w-4" />} />
      </div>

      {/* ── View tabs ───────────────────────────────────────────────── */}
      <div className="panel p-2">
        <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-3">
          {(["overview", "vehicles", "targets"] as const).map((v) => (
            <button
              key={v}
              type="button"
              onClick={() => setActiveView(v)}
              className={`flex flex-col items-start rounded-xl border px-3 py-2.5 text-left transition cursor-pointer ${
                activeView === v
                  ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60"
                  : "border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50"
              }`}
            >
              <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">
                {v === "overview" ? "View" : v === "vehicles" ? "Fleet" : "Goals"}
              </span>
              <span className={`mt-0.5 text-base font-bold ${activeView === v ? "text-teal-700" : "text-slate-900"}`}>
                {v === "overview" ? "Overview" : v === "vehicles" ? "By Vehicle" : "Targets"}
              </span>
            </button>
          ))}
        </div>
      </div>

      {/* ── Overview ── */}
      {activeView === "overview" && (
        <div className="flex flex-col gap-4">
          {/* Monthly trend chart */}
          <div className="panel p-5">
            <p className="section-title mb-0.5">Monthly CO₂ Emissions vs Target</p>
            <p className="text-xs text-slate-400 mb-4">Tonnes of CO₂ emitted compared to monthly reduction target</p>
            <ResponsiveContainer width="100%" height={220}>
              <AreaChart data={MONTHLY_TREND} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
                <defs>
                  <linearGradient id="co2grad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%"  stopColor="#0d9488" stopOpacity={0.2} />
                    <stop offset="95%" stopColor="#0d9488" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                <XAxis dataKey="month" tick={{ fontSize: 12 }} />
                <YAxis tick={{ fontSize: 12 }} />
                <Tooltip />
                <Legend />
                <Area type="monotone" dataKey="emissions" name="Actual (t CO₂)" stroke="#0d9488" fill="url(#co2grad)" strokeWidth={2} />
                <Area type="monotone" dataKey="target"    name="Target (t CO₂)"  stroke="#94a3b8" fill="none" strokeDasharray="5 3" strokeWidth={1.5} />
              </AreaChart>
            </ResponsiveContainer>
          </div>

          {/* Scope breakdown + intensity chart side by side */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="panel p-5">
              <p className="section-title mb-0.5">Scope Breakdown</p>
              <p className="text-xs text-slate-400 mb-4">GHG Protocol categorisation</p>
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

            <div className="panel p-5">
              <p className="section-title mb-0.5">CO₂ Intensity</p>
              <p className="text-xs text-slate-400 mb-4">kg CO₂ per km driven by month</p>
              <ResponsiveContainer width="100%" height={140}>
                <BarChart data={MONTHLY_TREND} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
                  <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} domain={[1.4, 2.0]} />
                  <Tooltip formatter={(v) => [`${String(v)} kg/km`, "Intensity"]} />
                  <Bar dataKey="intensity" name="kg CO₂/km" fill="#0d9488" radius={[3, 3, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
      )}

      {/* ── By Vehicle ── */}
      {activeView === "vehicles" && (
        <div className="overflow-x-auto rounded-xl border border-slate-200">
          <table className="w-full min-w-[800px] text-left text-sm">
            <thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2.5">Vehicle</th>
                <th className="px-4 py-2.5">Type</th>
                <th className="px-4 py-2.5">CO₂ This Month (kg)</th>
                <th className="px-4 py-2.5">km Driven</th>
                <th className="px-4 py-2.5">CO₂ Intensity (kg/km)</th>
                <th className="px-4 py-2.5">Idling CO₂ (kg)</th>
                <th className="px-4 py-2.5">Trend</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {vehicles.map((v, i) => (
                <tr key={String(v.id ?? i)} className="cursor-pointer transition-colors hover:bg-slate-50">
                  <td className="px-4 py-2.5 font-medium text-slate-900">{String(v.vehicleCode ?? "—")}</td>
                  <td className="px-4 py-2.5 text-xs text-slate-500">{String(v.vehicleType ?? "—")}</td>
                  <td className="px-4 py-2.5 font-semibold text-slate-800">{Number(v.co2ThisMonth ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-2.5 text-slate-600">{Number(v.kmThisMonth ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-2.5">
                    <span className={`text-xs font-medium ${Number(v.co2PerKm ?? 0) > 0.7 ? "text-red-600" : Number(v.co2PerKm ?? 0) > 0.5 ? "text-amber-600" : "text-teal-600"}`}>
                      {Number(v.co2PerKm ?? 0).toFixed(2)}
                    </span>
                  </td>
                  <td className="px-4 py-2.5 text-amber-600 text-xs font-medium">{Number(v.idlingCo2 ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-2.5"><TrendBadge trend={String(v.trend ?? "Stable")} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── Targets ── */}
      {activeView === "targets" && (
        <div className="flex flex-col gap-4">
          <div className="panel p-5">
            <p className="section-title mb-0.5">2026 Reduction Targets</p>
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

          <div className="panel p-5">
            <p className="section-title mb-0.5">Improvement Opportunities</p>
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
