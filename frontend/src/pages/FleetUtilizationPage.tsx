import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import {
  ArrowRight,
  Clock3,
  Fuel,
  Gauge,
  Satellite,
  ShieldAlert,
  Sparkles,
  Truck,
  Wrench,
} from "lucide-react";
import {
  BarChart,
  Bar,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { EmptyState, ErrorState, exportCsv, KpiCard, LoadingState, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";

type UtilSection = "overview" | "capacity" | "efficiency" | "opportunities";

const SECTIONS: Array<{ key: UtilSection; label: string; description: string }> = [
  { key: "overview", label: "Overview", description: "Live fleet posture and quick actions" },
  { key: "capacity", label: "Capacity", description: "Deployable reserve and load coverage" },
  { key: "efficiency", label: "Efficiency", description: "Idle leakage, fuel drag and output" },
  { key: "opportunities", label: "Action Queue", description: "Operational moves worth making now" },
];

const RELATED_ENTITIES = [
  { label: "Vehicles", route: "/vehicles/roster", note: "Jump to the fleet master roster" },
  { label: "Drivers", route: "/drivers/readiness", note: "Check staffing readiness and HOS pressure" },
  { label: "Dispatch", route: "/dispatch", note: "Cover open work with the best available units" },
  { label: "Maintenance", route: "/maintenance", note: "Resolve blockers keeping units off road" },
];

const fleetApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/fleet/utilization")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/fleet/utilization/summary")),
};

function readSection(pathname: string): UtilSection {
  const section = pathname.split("/").filter(Boolean)[1];
  if (section === "capacity" || section === "efficiency" || section === "opportunities") return section;
  return "overview";
}

const g = (row: AnyRecord, ...keys: string[]) => {
  for (const key of keys) if (row?.[key] != null && row[key] !== "") return row[key];
  return undefined;
};

const num = (value: unknown) => (Number.isFinite(Number(value)) ? Number(value) : 0);

function average(rows: AnyRecord[], key: string) {
  if (!rows.length) return 0;
  return rows.reduce((sum, row) => sum + num(g(row, key, key.replace(/([A-Z])/g, "_$1").toLowerCase())), 0) / rows.length;
}

function riskTier(row: AnyRecord): "High" | "Medium" | "Low" {
  const risk = num(g(row, "riskScore", "risk_score"));
  if (risk >= 70) return "High";
  if (risk >= 40) return "Medium";
  return "Low";
}

function deployabilityScore(row: AnyRecord) {
  const readiness = num(g(row, "readinessScore", "fleetReadinessScore"));
  const utilization = num(g(row, "utilizationPct"));
  const risk = num(g(row, "riskScore", "risk_score"));
  const idle = num(g(row, "idleMinutesToday"));
  const status = String(g(row, "status") ?? "");
  const statusLift =
    /available/i.test(status) ? 18 :
    /idle/i.test(status) ? 12 :
    /active|on route|at stop/i.test(status) ? 4 :
    /maintenance|out of service/i.test(status) ? -20 : 0;
  return Math.max(0, Math.min(100, Math.round(readiness * 0.5 + (100 - risk) * 0.25 + (100 - Math.min(utilization, 100)) * 0.15 + statusLift - Math.min(idle / 8, 12))));
}

type Opportunity = {
  id: string;
  vehicleId: string;
  vehicleCode: string;
  severity: "Critical" | "High" | "Medium";
  title: string;
  detail: string;
  actionLabel: string;
  actionRoute: string;
  impact: number;
};

function buildOpportunities(rows: AnyRecord[]) {
  const fuelAverage = average(rows, "fuelCostMonth");
  return rows.flatMap<Opportunity>((row) => {
    const vehicleId = String(row.id ?? "");
    const vehicleCode = String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${vehicleId}`);
    const readiness = num(g(row, "readinessScore", "fleetReadinessScore"));
    const utilization = num(g(row, "utilizationPct"));
    const idleMinutes = num(g(row, "idleMinutesToday"));
    const risk = num(g(row, "riskScore", "risk_score"));
    const jobs = num(g(row, "activeJobs"));
    const fuelCost = num(g(row, "fuelCostMonth"));
    const status = String(g(row, "status") ?? "");
    const results: Opportunity[] = [];

    if (idleMinutes >= 45 && readiness >= 75 && /available|idle|at stop/i.test(status)) {
      results.push({
        id: `${vehicleId}-idle`,
        vehicleId,
        vehicleCode,
        severity: idleMinutes >= 90 ? "Critical" : "High",
        title: "Redeploy idle-ready asset",
        detail: `${vehicleCode} has been idle ${idleMinutes} minutes today with ${readiness}% readiness and can cover pending work faster than leaving it parked.`,
        actionLabel: "Open dispatch coverage",
        actionRoute: "/dispatch",
        impact: idleMinutes + readiness,
      });
    }

    if (/maintenance|out of service/i.test(status)) {
      results.push({
        id: `${vehicleId}-maintenance`,
        vehicleId,
        vehicleCode,
        severity: "Critical",
        title: "Maintenance blocker hurting capacity",
        detail: `${vehicleCode} is unavailable because of maintenance posture, which compresses reserve capacity and increases pressure on the active fleet.`,
        actionLabel: "Open maintenance queue",
        actionRoute: "/maintenance",
        impact: 180 + (100 - readiness),
      });
    }

    if (jobs > 0 && risk >= 70) {
      results.push({
        id: `${vehicleId}-risk`,
        vehicleId,
        vehicleCode,
        severity: "High",
        title: "Active asset carrying elevated operational risk",
        detail: `${vehicleCode} is supporting ${jobs} live job(s) with a ${risk} risk score. Dispatch and fleet health should inspect it before the next handoff.`,
        actionLabel: "Inspect vehicle health",
        actionRoute: "/vehicles/health",
        impact: 140 + risk,
      });
    }

    if (fuelCost > 0 && fuelCost >= fuelAverage * 1.25 && utilization < 70) {
      results.push({
        id: `${vehicleId}-fuel`,
        vehicleId,
        vehicleCode,
        severity: "Medium",
        title: "Fuel spend is outpacing output",
        detail: `${vehicleCode} is burning more monthly fuel than fleet peers while only delivering ${Math.round(utilization)}% utilization.`,
        actionLabel: "Open efficiency view",
        actionRoute: "/fleet-utilization/efficiency",
        impact: 70 + fuelCost / Math.max(fuelAverage || 1, 1),
      });
    }

    if (utilization <= 35 && readiness >= 85 && !/maintenance|out of service/i.test(status)) {
      results.push({
        id: `${vehicleId}-reserve`,
        vehicleId,
        vehicleCode,
        severity: "Medium",
        title: "Healthy asset is under-used",
        detail: `${vehicleCode} is only at ${Math.round(utilization)}% utilization despite strong readiness, which makes it a candidate for rebalancing or route redesign.`,
        actionLabel: "Open capacity board",
        actionRoute: "/fleet-utilization/capacity",
        impact: 60 + readiness - utilization,
      });
    }

    return results;
  }).sort((a, b) => b.impact - a.impact);
}

function toneClass(severity: Opportunity["severity"]) {
  if (severity === "Critical") return "border-red-200 bg-red-50/80";
  if (severity === "High") return "border-amber-200 bg-amber-50/80";
  return "border-sky-200 bg-sky-50/80";
}

function routeForVehicle(section: UtilSection) {
  if (section === "capacity") return "/vehicles/roster";
  if (section === "efficiency") return "/vehicles/health";
  return "/vehicles/overview";
}

export function FleetUtilizationPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const section = readSection(location.pathname);
  const [statusFilter, setStatusFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const listQ = useQuery({
    queryKey: ["fleet", "utilization"],
    queryFn: fleetApi.list,
    refetchInterval: 30_000,
  });
  const summaryQ = useQuery({
    queryKey: ["fleet", "utilization", "summary"],
    queryFn: fleetApi.summary,
    refetchInterval: 60_000,
  });

  const rows = (listQ.data ?? []) as AnyRecord[];
  const summary = (summaryQ.data ?? {}) as AnyRecord;

  const selected = rows.find((row) => String(row.id) === selectedId) ?? rows[0] ?? null;
  const opportunities = useMemo(() => buildOpportunities(rows), [rows]);
  const statusOptions = useMemo(
    () => ["All", ...Array.from(new Set(rows.map((row) => String(g(row, "status") ?? "")).filter(Boolean)))],
    [rows],
  );

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    return rows.filter((row) => {
      const status = String(g(row, "status") ?? "");
      const matchesStatus = statusFilter === "All" || status === statusFilter;
      const matchesQuery =
        !query ||
        [g(row, "vehicleCode", "vehicle_code"), g(row, "driverName", "driver_name"), g(row, "type"), status]
          .some((value) => String(value ?? "").toLowerCase().includes(query));
      return matchesStatus && matchesQuery;
    });
  }, [rows, search, statusFilter]);

  const deployableRows = useMemo(
    () => [...filtered].sort((a, b) => deployabilityScore(b) - deployabilityScore(a)),
    [filtered],
  );
  const efficiencyRows = useMemo(
    () => [...filtered].sort((a, b) => num(g(b, "idleMinutesToday")) - num(g(a, "idleMinutesToday")) || num(g(b, "fuelCostMonth")) - num(g(a, "fuelCostMonth"))),
    [filtered],
  );
  const chartData = useMemo(
    () =>
      [...rows]
        .sort((a, b) => num(g(b, "utilizationPct")) - num(g(a, "utilizationPct")))
        .slice(0, 8)
        .map((row) => ({
          name: String(g(row, "vehicleCode", "vehicle_code") ?? ""),
          utilization: Math.round(num(g(row, "utilizationPct"))),
          readiness: Math.round(num(g(row, "readinessScore", "fleetReadinessScore"))),
        })),
    [rows],
  );

  if (listQ.isLoading || summaryQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={listQ.error instanceof Error ? listQ.error.message : "Unable to load fleet utilization."} />;
  if (summaryQ.isError) return <ErrorState message={summaryQ.error instanceof Error ? summaryQ.error.message : "Unable to load utilization summary."} />;

  const total = num(summary.totalVehicles) || rows.length;
  const active = num(summary.activeVehicles) || rows.filter((row) => /active|on route/i.test(String(g(row, "status") ?? ""))).length;
  const available = num(summary.availableVehicles) || rows.filter((row) => /available|idle/i.test(String(g(row, "status") ?? ""))).length;
  const maintenance = num(summary.maintenanceVehicles) || rows.filter((row) => /maintenance|out of service/i.test(String(g(row, "status") ?? ""))).length;
  const readiness = Math.round(num(summary.avgReadiness) || average(rows, "readinessScore"));
  const utilization = Math.round(num(summary.avgUtilizationPct) || average(rows, "utilizationPct"));
  const idleHours = num(summary.idleHoursToday);
  const idleCost = num(summary.idleCostToday);
  const fuelSpend = num(summary.fuelSpendMonth);
  const atRisk = rows.filter((row) => riskTier(row) === "High").length;

  const exportRows =
    section === "efficiency" ? efficiencyRows :
    section === "capacity" ? deployableRows :
    section === "opportunities" ? opportunities :
    rows;

  return (
    <div className="space-y-6 pb-10">
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Gauge className="h-3 w-3" /> Capacity Intelligence
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Utilization and deployability</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Fleet Utilization
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Operations command surface for capacity, idle leakage and redeployment readiness
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button type="button" onClick={() => exportCsv("fleet-utilization", exportRows as AnyRecord[])} className="fh-btn-ghost">Export live view</button>
              <button type="button" onClick={() => navigate("/dispatch")} className="fh-btn-primary">Open dispatch coverage <ArrowRight className="h-3.5 w-3.5" /></button>
            </div>
          </div>
        </div>
      </header>

      <nav className="sticky top-4 z-20 rounded-2xl border border-slate-200 bg-white/95 p-2 shadow-sm backdrop-blur">
        <div className="grid gap-1 sm:grid-cols-4">
          {SECTIONS.map((item) => (
            <button
              key={item.key}
              type="button"
              onClick={() => navigate(`/fleet-utilization/${item.key}`)}
              className={`cursor-pointer rounded-xl px-3 py-2.5 text-left transition ${
                section === item.key ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60" : "bg-slate-50/40 hover:bg-slate-100"
              }`}
            >
              <div className="text-xs font-bold uppercase tracking-[0.14em]">{item.label}</div>
              <div className={`mt-0.5 text-[11px] ${section === item.key ? "text-teal-500" : "text-slate-500"}`}>{item.description}</div>
            </button>
          ))}
        </div>
      </nav>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Average utilization" value={`${utilization}%`} trend={`${active}/${Math.max(total, 1)} active now`} />
        <KpiCard label="Ready reserve" value={String(available)} trend={`${readiness}% average readiness`} />
        <KpiCard label="At risk units" value={String(atRisk)} status="Review" trend={`${maintenance} blocked by maintenance`} />
        <KpiCard label="Idle drag today" value={idleCost ? `$${idleCost.toLocaleString()}` : `${idleHours}h`} status="Review" trend={idleHours ? `${idleHours} idle hours logged` : "No idle drag detected"} />
      </div>

      {section === "overview" && (
        <div className="space-y-6">
          <div className="grid gap-4 lg:grid-cols-3">
            <ModuleCard
              title="Capacity board"
              body="Rank units by deployability so dispatch can cover demand without hunting through the roster."
              action="Open capacity"
              onClick={() => navigate("/fleet-utilization/capacity")}
              icon={<Truck className="h-5 w-5" />}
            />
            <ModuleCard
              title="Efficiency view"
              body="Spot idle leakage, fuel drag and under-performing assets before costs become normalized."
              action="Open efficiency"
              onClick={() => navigate("/fleet-utilization/efficiency")}
              icon={<Fuel className="h-5 w-5" />}
            />
            <ModuleCard
              title="Action queue"
              body="Prioritized recommendations built from real utilization, readiness, maintenance and risk signals."
              action="Open action queue"
              onClick={() => navigate("/fleet-utilization/opportunities")}
              icon={<Sparkles className="h-5 w-5" />}
            />
          </div>

          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Where capacity is getting trapped</h2>
                <p className="text-sm text-slate-500">These cues are derived from live status, readiness, utilization, idle time and maintenance posture.</p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                Operations pressure radar
              </span>
            </div>
            <div className="mt-4 grid gap-3 lg:grid-cols-3">
              <InsightTile
                icon={<Gauge className="h-4 w-4" />}
                label="Available coverage"
                value={`${available} units`}
                body={`${rows.filter((row) => deployabilityScore(row) >= 70).length} units are immediately deployable based on readiness, risk and current utilization.`}
              />
              <InsightTile
                icon={<Clock3 className="h-4 w-4" />}
                label="Idle leakage"
                value={idleHours ? `${idleHours}h` : "0h"}
                body={idleCost ? `$${idleCost.toLocaleString()} of today’s idle drag is visible right now.` : "No major idle drag recorded in the current data set."}
              />
              <InsightTile
                icon={<Wrench className="h-4 w-4" />}
                label="Maintenance blockers"
                value={String(maintenance)}
                body={`${maintenance} units are blocked from contributing capacity and should stay tied to work orders, defects and service readiness.`}
              />
            </div>
          </section>

          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Top action queue</h2>
                <p className="text-sm text-slate-500">The highest-value changes operators can make now without waiting for more reporting.</p>
              </div>
              <button type="button" className="fh-btn-ghost h-9" onClick={() => navigate("/fleet-utilization/opportunities")}>Open full queue</button>
            </div>
            <div className="mt-4 grid gap-3 xl:grid-cols-3">
              {opportunities.slice(0, 3).map((item) => (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => navigate(item.actionRoute)}
                  className={`cursor-pointer rounded-2xl border p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:shadow-md ${toneClass(item.severity)}`}
                >
                  <div className="flex items-center justify-between gap-3">
                    <StatusBadge status={item.severity} />
                    <ArrowRight className="h-4 w-4 text-slate-400" />
                  </div>
                  <h3 className="mt-3 text-sm font-semibold text-slate-900">{item.title}</h3>
                  <p className="mt-2 text-sm text-slate-600">{item.detail}</p>
                  <p className="mt-4 text-[11px] font-bold uppercase tracking-[0.14em] text-slate-500">{item.actionLabel}</p>
                </button>
              ))}
              {!opportunities.length && (
                <div className="rounded-2xl border border-emerald-200 bg-emerald-50/80 p-4 text-sm text-emerald-800 xl:col-span-3">
                  Live data is not surfacing a major utilization intervention right now. That is a good sign and should remain visible, not hidden.
                </div>
              )}
            </div>
          </section>

          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Entity links</h2>
                <p className="text-sm text-slate-500">Keep utilization decisions tied to the modules that can actually unblock them.</p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                Connected workflows
              </span>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {RELATED_ENTITIES.map((item) => (
                <button
                  key={item.label}
                  type="button"
                  onClick={() => navigate(item.route)}
                  className="group cursor-pointer rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-teal-200 hover:shadow-md"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-semibold text-slate-900">{item.label}</span>
                    <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-teal-500" />
                  </div>
                  <p className="mt-2 text-sm text-slate-500">{item.note}</p>
                </button>
              ))}
            </div>
          </section>
        </div>
      )}

      {section !== "overview" && (
        <section className="panel space-y-4 p-5">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <div className="relative w-full lg:max-w-sm">
              <input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search vehicle, driver or status…"
                className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none transition placeholder:text-slate-400 focus:border-teal-400 focus:ring-2 focus:ring-teal-100"
              />
            </div>
            <div className="flex flex-wrap gap-2">
              {statusOptions.map((option) => (
                <button
                  key={option}
                  type="button"
                  onClick={() => setStatusFilter(option)}
                  className={`cursor-pointer rounded-xl px-3 py-2 text-xs font-semibold transition ${
                    statusFilter === option ? "bg-teal-50 text-teal-700 ring-1 ring-teal-200/60" : "border border-slate-200 bg-white text-slate-600 hover:bg-slate-50"
                  }`}
                >
                  {option}
                </button>
              ))}
            </div>
          </div>

          {section === "capacity" && (
            <div className="grid gap-4 xl:grid-cols-[1.5fr_0.9fr]">
              <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
                <table className="w-full text-left text-sm">
                  <thead>
                    <tr className="border-b border-slate-200 text-[11px] uppercase tracking-[0.12em] text-slate-400">
                      <th className="px-5 py-3 font-semibold">Vehicle</th>
                      <th className="px-5 py-3 font-semibold">Status</th>
                      <th className="px-5 py-3 font-semibold">Deployability</th>
                      <th className="hidden px-5 py-3 font-semibold lg:table-cell">Readiness</th>
                      <th className="hidden px-5 py-3 font-semibold lg:table-cell">Idle</th>
                      <th className="hidden px-5 py-3 font-semibold xl:table-cell">Jobs</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {deployableRows.map((row) => {
                      const score = deployabilityScore(row);
                      return (
                        <tr
                          key={String(row.id)}
                          onClick={() => setSelectedId(String(row.id))}
                          className="cursor-pointer transition hover:bg-slate-50"
                        >
                          <td className="px-5 py-3.5">
                            <div className="font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</div>
                            <div className="text-xs text-slate-500">{String(g(row, "driverName", "driver_name") ?? "Unassigned driver")}</div>
                          </td>
                          <td className="px-5 py-3.5"><StatusBadge status={g(row, "status")} /></td>
                          <td className="px-5 py-3.5">
                            <div className="flex items-center gap-3">
                              <div className="h-2 flex-1 rounded-full bg-slate-100">
                                <div className="h-2 rounded-full bg-gradient-to-r from-teal-500 to-sky-500" style={{ width: `${score}%` }} />
                              </div>
                              <span className="w-10 text-xs font-semibold text-slate-700">{score}</span>
                            </div>
                          </td>
                          <td className="hidden px-5 py-3.5 text-slate-600 lg:table-cell">{Math.round(num(g(row, "readinessScore", "fleetReadinessScore")))}%</td>
                          <td className="hidden px-5 py-3.5 text-slate-600 lg:table-cell">{num(g(row, "idleMinutesToday"))}m</td>
                          <td className="hidden px-5 py-3.5 text-slate-600 xl:table-cell">{num(g(row, "activeJobs"))}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
                {!deployableRows.length && <EmptyState title="No vehicles match this capacity view" subtitle="Try another status or search term." />}
              </div>
              <VehicleInsightPanel row={selected} section={section} onNavigate={navigate} />
            </div>
          )}

          {section === "efficiency" && (
            <div className="space-y-4">
              <div className="grid gap-4 xl:grid-cols-[1.2fr_1fr]">
                <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-lg font-semibold text-slate-900">Top productive units</h2>
                      <p className="text-sm text-slate-500">Utilization versus readiness to highlight where output is coming from.</p>
                    </div>
                    <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                      Live chart
                    </span>
                  </div>
                  <div className="mt-4 h-72">
                    <ResponsiveContainer width="100%" height="100%">
                      <BarChart data={chartData} margin={{ top: 4, right: 10, left: -20, bottom: 4 }}>
                        <CartesianGrid stroke="rgba(15,23,42,0.08)" strokeDasharray="3 3" />
                        <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                        <YAxis tick={{ fontSize: 11 }} domain={[0, 100]} />
                        <Tooltip
                          contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 12, fontSize: 12 }}
                        />
                        <Bar dataKey="utilization" fill="#0f766e" radius={[6, 6, 0, 0]} />
                        <Bar dataKey="readiness" fill="#60a5fa" radius={[6, 6, 0, 0]} />
                      </BarChart>
                    </ResponsiveContainer>
                  </div>
                </div>
                <div className="space-y-3">
                  {efficiencyRows.slice(0, 3).map((row) => (
                    <button
                      key={String(row.id)}
                      type="button"
                      onClick={() => setSelectedId(String(row.id))}
                      className="cursor-pointer w-full rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md"
                    >
                      <div className="flex items-center justify-between gap-3">
                        <div>
                          <p className="text-sm font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</p>
                          <p className="text-xs text-slate-500">{String(g(row, "status") ?? "Unknown")}</p>
                        </div>
                        <StatusBadge status={riskTier(row)} />
                      </div>
                      <div className="mt-4 grid grid-cols-3 gap-3 text-sm">
                        <MetricMini label="Idle today" value={`${num(g(row, "idleMinutesToday"))}m`} />
                        <MetricMini label="Fuel month" value={`$${num(g(row, "fuelCostMonth")).toLocaleString()}`} />
                        <MetricMini label="Utilization" value={`${Math.round(num(g(row, "utilizationPct")))}%`} />
                      </div>
                    </button>
                  ))}
                </div>
              </div>

              <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
                <table className="w-full text-left text-sm">
                  <thead>
                    <tr className="border-b border-slate-200 text-[11px] uppercase tracking-[0.12em] text-slate-400">
                      <th className="px-5 py-3 font-semibold">Vehicle</th>
                      <th className="px-5 py-3 font-semibold">Idle drag</th>
                      <th className="px-5 py-3 font-semibold">Fuel spend</th>
                      <th className="hidden px-5 py-3 font-semibold lg:table-cell">Utilization</th>
                      <th className="hidden px-5 py-3 font-semibold xl:table-cell">Readiness</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {efficiencyRows.map((row) => (
                      <tr key={String(row.id)} onClick={() => setSelectedId(String(row.id))} className="cursor-pointer transition hover:bg-slate-50">
                        <td className="px-5 py-3.5">
                          <div className="font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</div>
                          <div className="text-xs text-slate-500">{String(g(row, "driverName", "driver_name") ?? "No driver linked")}</div>
                        </td>
                        <td className="px-5 py-3.5 text-slate-700">{num(g(row, "idleMinutesToday"))}m / {num(g(row, "idleEventsToday"))} events</td>
                        <td className="px-5 py-3.5 text-slate-700">${num(g(row, "fuelCostMonth")).toLocaleString()}</td>
                        <td className="hidden px-5 py-3.5 text-slate-700 lg:table-cell">{Math.round(num(g(row, "utilizationPct")))}%</td>
                        <td className="hidden px-5 py-3.5 text-slate-700 xl:table-cell">{Math.round(num(g(row, "readinessScore", "fleetReadinessScore")))}%</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {section === "opportunities" && (
            <div className="grid gap-4 xl:grid-cols-[1.4fr_0.95fr]">
              <div className="space-y-3">
                {opportunities.length ? opportunities.map((item) => (
                  <div key={item.id} className={`rounded-2xl border p-4 shadow-sm ${toneClass(item.severity)}`}>
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2">
                          <StatusBadge status={item.severity} />
                          <span className="text-sm font-semibold text-slate-900">{item.vehicleCode}</span>
                        </div>
                        <h3 className="mt-3 text-base font-semibold text-slate-900">{item.title}</h3>
                      </div>
                      <button type="button" className="fh-btn-ghost h-9" onClick={() => navigate(item.actionRoute)}>
                        {item.actionLabel}
                      </button>
                    </div>
                    <p className="mt-3 text-sm text-slate-600">{item.detail}</p>
                  </div>
                )) : (
                  <EmptyState title="No intervention queue right now" subtitle="The live dataset is not surfacing an obvious utilization issue at the moment." />
                )}
              </div>
              <VehicleInsightPanel row={selected} section={section} onNavigate={navigate} />
            </div>
          )}
        </section>
      )}
    </div>
  );
}

function ModuleCard({
  title,
  body,
  action,
  onClick,
  icon,
}: {
  title: string;
  body: string;
  action: string;
  onClick: () => void;
  icon: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="group cursor-pointer rounded-2xl border border-slate-200 bg-white p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md"
    >
      <div className="flex items-center justify-between">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-slate-50 text-slate-500">{icon}</div>
        <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5" />
      </div>
      <h3 className="mt-4 text-base font-semibold text-slate-900">{title}</h3>
      <p className="mt-2 text-sm text-slate-500">{body}</p>
      <p className="mt-4 text-xs font-bold uppercase tracking-[0.14em] text-teal-600">{action}</p>
    </button>
  );
}

function InsightTile({ icon, label, value, body }: { icon: React.ReactNode; label: string; value: string; body: string }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
      <div className="flex items-center justify-between">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-white text-slate-500 shadow-sm">{icon}</div>
        <span className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-400">{label}</span>
      </div>
      <p className="mt-4 text-2xl font-bold tracking-tight text-slate-900">{value}</p>
      <p className="mt-2 text-sm text-slate-500">{body}</p>
    </div>
  );
}

function MetricMini({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2">
      <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">{label}</p>
      <p className="mt-1 font-semibold text-slate-900">{value}</p>
    </div>
  );
}

function VehicleInsightPanel({
  row,
  section,
  onNavigate,
}: {
  row: AnyRecord | null;
  section: UtilSection;
  onNavigate: (route: string) => void;
}) {
  if (!row) {
    return (
      <div className="panel p-5">
        <EmptyState title="No vehicle selected" subtitle="Pick a row to inspect the unit in context." />
      </div>
    );
  }

  const readiness = Math.round(num(g(row, "readinessScore", "fleetReadinessScore")));
  const utilization = Math.round(num(g(row, "utilizationPct")));
  const fuelCost = num(g(row, "fuelCostMonth"));
  const idle = num(g(row, "idleMinutesToday"));
  const risk = num(g(row, "riskScore", "risk_score"));

  return (
    <aside className="panel p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Selected unit</p>
          <h3 className="mt-1 text-lg font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</h3>
          <p className="text-sm text-slate-500">{String(g(row, "driverName", "driver_name") ?? "Unassigned driver")}</p>
        </div>
        <StatusBadge status={g(row, "status")} />
      </div>
      <div className="mt-4 grid grid-cols-2 gap-3">
        <MetricMini label="Readiness" value={`${readiness}%`} />
        <MetricMini label="Utilization" value={`${utilization}%`} />
        <MetricMini label="Risk" value={`${risk}`} />
        <MetricMini label="Fuel month" value={`$${fuelCost.toLocaleString()}`} />
      </div>
      <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
        {section === "capacity" && `${String(g(row, "vehicleCode", "vehicle_code"))} is currently carrying ${num(g(row, "activeJobs"))} active job(s) with ${idle} idle minutes today. Use the roster to rebalance it or open dispatch to cover available demand.`}
        {section === "efficiency" && `${String(g(row, "vehicleCode", "vehicle_code"))} is showing ${idle} idle minutes and $${fuelCost.toLocaleString()} fuel spend this month. This is where operators decide whether the issue is routing, dispatch timing or asset health.`}
        {section === "opportunities" && `${String(g(row, "vehicleCode", "vehicle_code"))} is part of the current action queue because live backend metrics suggest it can either unlock capacity or reduce operating drag.`}
      </div>
      <div className="mt-4 flex flex-wrap gap-2">
        <button type="button" className="fh-btn-ghost h-9" onClick={() => onNavigate(routeForVehicle(section))}>Open vehicle module</button>
        <button type="button" className="fh-btn-ghost h-9" onClick={() => onNavigate("/dispatch")}>Open dispatch</button>
        <button type="button" className="fh-btn-ghost h-9" onClick={() => onNavigate("/maintenance")}>Open maintenance</button>
      </div>
    </aside>
  );
}
