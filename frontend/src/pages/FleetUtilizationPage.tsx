import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import type { AnyRecord } from "@/types";

// ── Seed fallback ─────────────────────────────────────────────────────────────

function buildSeed(): AnyRecord[] {
  const vehicles = developmentFleetSeedData.vehicles as AnyRecord[];
  const drivers = developmentFleetSeedData.drivers as AnyRecord[];
  return vehicles.map((v, i) => ({
    id: v.id ?? i + 1,
    vehicleCode: v.vehicleCode ?? `VH-${String(i + 1).padStart(3, "0")}`,
    type: v.type ?? "Box Truck",
    status: (["Active", "Active", "Active", "Available", "Maintenance", "Active"] as const)[i % 6],
    odometerMiles: 45000 + i * 3200,
    readinessScore: 88 - (i % 8) * 3,
    riskScore: 12 + (i % 10) * 5,
    driverName: (drivers[i % drivers.length] as AnyRecord)?.fullName ?? `Driver ${i + 1}`,
    driverCode: `DRV-${String((i % 12) + 1).padStart(3, "0")}`,
    idleMinutesToday: [0, 18, 45, 0, 72, 12, 30, 5, 60, 0, 24, 90][i % 12],
    idleEventsToday: [0, 2, 4, 0, 6, 1, 3, 1, 5, 0, 2, 7][i % 12],
    fuelCostMonth: 380 + (i * 87),
    gallonsMonth: 95 + (i * 22),
    activeJobs: [2, 1, 3, 0, 0, 1, 2, 1, 1, 0, 2, 0][i % 12],
    completedToday: [3, 2, 4, 0, 0, 2, 3, 1, 2, 0, 3, 0][i % 12],
    utilizationPct: [92, 85, 88, 22, 5, 78, 81, 70, 75, 18, 83, 3][i % 12],
    activeHoursPct: [90, 80, 85, 20, 8, 75, 79, 68, 72, 15, 80, 5][i % 12],
  }));
}

const SEED_SUMMARY: AnyRecord = {
  totalVehicles: 12, activeVehicles: 8, availableVehicles: 2, maintenanceVehicles: 2,
  avgReadiness: 84.3, avgUtilizationPct: 68.5, idleHoursToday: 5.8, idleCostToday: "$42", fuelSpendMonth: "$12,400",
};

const fleetApi = {
  list: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/fleet/utilization")), () => buildSeed()),
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/fleet/utilization/summary")), () => SEED_SUMMARY),
};

// ── Utilization bar ───────────────────────────────────────────────────────────

function UtilBar({ pct, label }: { pct: number; label?: string }) {
  const color =
    pct >= 75 ? "bg-teal-500" :
    pct >= 50 ? "bg-amber-400" :
    "bg-red-400";
  return (
    <div className="flex items-center gap-2 text-xs">
      {label && <span className="w-20 text-slate-500 shrink-0 truncate">{label}</span>}
      <div className="flex-1 h-2 rounded-full bg-slate-100 overflow-hidden">
        <div className={`h-full rounded-full ${color} transition-all`} style={{ width: `${Math.max(2, pct)}%` }} />
      </div>
      <span className="w-8 text-right font-medium text-slate-700">{Math.round(pct)}%</span>
    </div>
  );
}

// ── Status badge ──────────────────────────────────────────────────────────────

function VehicleStatusBadge({ status }: { status: string }) {
  const cls =
    status === "Active" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Available" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "Maintenance" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return (
    <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type SortKey = "utilizationPct" | "idleMinutesToday" | "fuelCostMonth" | "riskScore";

export function FleetUtilizationPage() {
  const [statusFilter, setStatusFilter] = useState<"All" | "Active" | "Available" | "Maintenance">("All");
  const [sortBy, setSortBy] = useState<SortKey>("utilizationPct");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);

  const listQ = useQuery({ queryKey: ["fleet", "utilization"], queryFn: fleetApi.list, refetchInterval: 30_000 });
  const sumQ = useQuery({ queryKey: ["fleet", "utilization", "summary"], queryFn: fleetApi.summary });

  const vehicles = (listQ.data ?? []) as AnyRecord[];
  const s = (sumQ.data ?? {}) as AnyRecord;

  const filtered = vehicles
    .filter((v) => {
      if (statusFilter !== "All" && v.status !== statusFilter) return false;
      if (search) {
        const q = search.toLowerCase();
        return (
          String(v.vehicleCode ?? "").toLowerCase().includes(q) ||
          String(v.driverName ?? "").toLowerCase().includes(q) ||
          String(v.type ?? "").toLowerCase().includes(q)
        );
      }
      return true;
    })
    .sort((a, b) => Number(b[sortBy] ?? 0) - Number(a[sortBy] ?? 0));

  const chartData = vehicles
    .sort((a, b) => Number(b.utilizationPct ?? 0) - Number(a.utilizationPct ?? 0))
    .slice(0, 10)
    .map((v) => ({
      name: String(v.vehicleCode ?? ""),
      utilization: Math.round(Number(v.utilizationPct ?? 0)),
      idle: Math.round(Number(v.idleMinutesToday ?? 0) / 60 * 100) / 100,
    }));

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Fleet Utilization</h1>
          <p className="text-sm text-slate-500 mt-0.5">Vehicle utilization %, idle time, fuel efficiency and active hour tracking across the fleet</p>
        </div>
        <button
          type="button"
          className="btn-secondary text-sm"
          onClick={() => exportCsv("fleet-utilization", filtered)}
        >
          Export CSV
        </button>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Vehicles",      val: s.totalVehicles ?? vehicles.length },
          { label: "Active Now",          val: s.activeVehicles ?? vehicles.filter((v) => v.status === "Active").length, accent: "text-teal-600" },
          { label: "Available",           val: s.availableVehicles ?? vehicles.filter((v) => v.status === "Available").length, accent: "text-blue-600" },
          { label: "In Maintenance",      val: s.maintenanceVehicles ?? vehicles.filter((v) => v.status === "Maintenance").length, accent: "text-amber-600" },
          { label: "Avg Utilization",     val: `${s.avgUtilizationPct ?? "--"}%`, accent: "text-violet-600" },
          { label: "Avg Readiness",       val: `${s.avgReadiness ?? "--"}%`, accent: "text-teal-600" },
          { label: "Idle Hours Today",    val: s.idleHoursToday ?? "--", accent: "text-amber-600" },
          { label: "Idle Cost Today",     val: s.idleCostToday ?? "--", accent: "text-red-600" },
          { label: "Fuel Spend (Month)",  val: s.fuelSpendMonth ?? "--", accent: "text-slate-700" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-30">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Chart */}
      <div className="panel p-5">
        <h2 className="text-sm font-semibold text-slate-900 mb-4">Top 10 Vehicles by Utilization %</h2>
        <ResponsiveContainer width="100%" height={200}>
          <BarChart data={chartData} margin={{ top: 4, right: 8, left: -20, bottom: 4 }}>
            <CartesianGrid stroke="rgba(0,0,0,0.05)" strokeDasharray="3 3" />
            <XAxis dataKey="name" tick={{ fontSize: 10 }} />
            <YAxis tick={{ fontSize: 10 }} domain={[0, 100]} />
            <Tooltip
              contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8, fontSize: 12 }}
              formatter={(val, name) => [
                name === "utilization" ? `${String(val)}%` : `${String(val)}h`,
                name === "utilization" ? "Utilization" : "Idle (hrs)",
              ]}
            />
            <Bar dataKey="utilization" fill="#14b8a6" radius={[3, 3, 0, 0]} name="Utilization" />
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Filters + sort */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Active", "Available", "Maintenance"] as const).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {f}
            </button>
          ))}
        </div>
        <select
          title="Sort by"
          value={sortBy}
          onChange={(e) => setSortBy(e.target.value as SortKey)}
          className="border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
        >
          <option value="utilizationPct">Sort: Utilization</option>
          <option value="idleMinutesToday">Sort: Idle Time</option>
          <option value="fuelCostMonth">Sort: Fuel Cost</option>
          <option value="riskScore">Sort: Risk Score</option>
        </select>
        <input
          type="search"
          placeholder="Search vehicles, drivers…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-52"
        />
      </div>

      {/* Vehicle table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? (
          <EmptyState title="No vehicles match your filters" />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Vehicle", "Driver", "Status", "Utilization", "Active Hours", "Idle Today", "Active Jobs", "Fuel / Mo", "Readiness"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((v, i) => (
                  <tr
                    key={String(v.id ?? i)}
                    className={`hover:bg-slate-50 cursor-pointer ${selected && Number(selected.id) === Number(v.id) ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === v.id ? null : v)}
                  >
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(v.vehicleCode ?? "--")}</p>
                      <p className="text-xs text-slate-400">{String(v.type ?? "")}</p>
                    </td>
                    <td className="px-4 py-3">
                      <p className="text-slate-700">{String(v.driverName ?? "Unassigned")}</p>
                      <p className="text-xs text-slate-400">{String(v.driverCode ?? "")}</p>
                    </td>
                    <td className="px-4 py-3"><VehicleStatusBadge status={String(v.status ?? "Available")} /></td>
                    <td className="px-4 py-3 w-40">
                      <UtilBar pct={Number(v.utilizationPct ?? 0)} />
                    </td>
                    <td className="px-4 py-3 w-36">
                      <UtilBar pct={Number(v.activeHoursPct ?? 0)} />
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      {Number(v.idleMinutesToday ?? 0) > 0 ? (
                        <span className={`text-xs font-medium ${Number(v.idleMinutesToday) > 30 ? "text-amber-700" : "text-slate-600"}`}>
                          {String(v.idleMinutesToday)}m / {String(v.idleEventsToday ?? 0)} events
                        </span>
                      ) : (
                        <span className="text-slate-400 text-xs">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {Number(v.activeJobs ?? 0) > 0 ? (
                        <span className="text-xs font-semibold text-teal-700">{String(v.activeJobs)} active</span>
                      ) : (
                        <span className="text-slate-400 text-xs">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-slate-700 text-xs">${Number(v.fuelCostMonth ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 w-32">
                      <UtilBar pct={Number(v.readinessScore ?? 0)} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Detail drawer */}
      {selected && (
        <div className="fixed inset-0 z-40 flex justify-end" onClick={() => setSelected(null)}>
          <div className="bg-slate-950 w-full max-w-sm h-full flex flex-col overflow-y-auto shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
              <span className="text-sm font-semibold text-white">{String(selected.vehicleCode)} — Utilization Detail</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6">
              <VehicleStatusBadge status={String(selected.status ?? "Available")} />
              <p className="text-sm text-slate-300 mt-2">{String(selected.type ?? "")}</p>
              <p className="text-xs text-slate-400 mt-0.5">{String(selected.driverName ?? "Unassigned")} · {String(selected.driverCode ?? "")}</p>
            </div>
            <div className="px-5 py-4 flex flex-col gap-3 border-b border-white/6">
              <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide">Utilization</p>
              <UtilBar pct={Number(selected.utilizationPct ?? 0)} label="Utilization" />
              <UtilBar pct={Number(selected.activeHoursPct ?? 0)} label="Active Hours" />
              <UtilBar pct={Number(selected.readinessScore ?? 0)} label="Readiness" />
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Odometer", `${Number(selected.odometerMiles ?? 0).toLocaleString()} mi`],
                ["Idle Today", `${String(selected.idleMinutesToday ?? 0)}m`],
                ["Idle Events", String(selected.idleEventsToday ?? 0)],
                ["Active Jobs", String(selected.activeJobs ?? 0)],
                ["Completed Today", String(selected.completedToday ?? 0)],
                ["Fuel (Month)", `$${Number(selected.fuelCostMonth ?? 0).toLocaleString()}`],
                ["Fuel (Gallons)", `${String(selected.gallonsMonth ?? 0)} gal`],
                ["Risk Score", String(selected.riskScore ?? 0)],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">AI Insight</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {Number(selected.utilizationPct ?? 0) >= 80
                  ? "Vehicle is well-utilized. Monitor idle events to prevent fuel cost creep."
                  : Number(selected.utilizationPct ?? 0) >= 50
                  ? "Moderate utilization. Consider assigning additional jobs or reviewing route allocation."
                  : String(selected.status) === "Maintenance"
                  ? "Vehicle is under maintenance. Ensure timely completion to restore fleet capacity."
                  : "Low utilization detected. Review assignment schedule and consider redeployment."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
