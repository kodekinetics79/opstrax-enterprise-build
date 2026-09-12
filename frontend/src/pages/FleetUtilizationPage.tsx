import { useMemo, useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router";
import {
  ArrowRight,
  Clock3,
  Gauge,
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
import { EmptyState, ErrorState, exportCsv, LoadingState, StatusBadge } from "@/components/ui";
import { ConsoleNav, ConsoleRail } from "@/components/console";
import type { AnyRecord } from "@/types";

type UtilSection = "overview" | "capacity" | "efficiency" | "opportunities";

const SECTIONS: Array<{ key: UtilSection; label: string; description: string }> = [
  { key: "overview", label: "Overview", description: "Recorded fleet posture and evidence-qualified cues" },
  { key: "capacity", label: "Capacity", description: "Recorded status and qualified trip utilization" },
  { key: "efficiency", label: "Efficiency", description: "Qualified idle, fuel and trip evidence" },
  { key: "opportunities", label: "Action Queue", description: "Bounded cues supported by available evidence" },
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

function optionalNumber(value: unknown): number | null {
  if (value == null || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function hasUtilizationEvidence(row: AnyRecord) {
  return String(g(row, "utilizationBasis", "utilization_basis") ?? "").startsWith("trip_hours_30d");
}

function hasQualifiedEvidence(row: AnyRecord, key: "idle" | "fuel") {
  return String(g(row, `${key}EvidenceStatus`, `${key}_evidence_status`) ?? "") === "qualified";
}

function percent(value: unknown) {
  const parsed = optionalNumber(value);
  return parsed === null ? "—" : `${Math.round(parsed)}%`;
}

function minutes(value: unknown) {
  const parsed = optionalNumber(value);
  return parsed === null ? "—" : `${Math.round(parsed)}m`;
}

function money(value: unknown) {
  const parsed = optionalNumber(value);
  return parsed === null ? "—" : `$${parsed.toLocaleString()}`;
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
  const qualifiedFuel = rows
    .filter((row) => hasQualifiedEvidence(row, "fuel"))
    .map((row) => optionalNumber(g(row, "fuelCostMonth", "fuel_cost_month")))
    .filter((value): value is number => value !== null);
  const fuelAverage = qualifiedFuel.length
    ? qualifiedFuel.reduce((sum, value) => sum + value, 0) / qualifiedFuel.length
    : null;
  return rows.flatMap<Opportunity>((row) => {
    const vehicleId = String(row.id ?? "");
    const vehicleCode = String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${vehicleId}`);
    const utilization = optionalNumber(g(row, "utilizationPct", "utilization_pct"));
    const idleMinutes = optionalNumber(g(row, "idleMinutesToday", "idle_minutes_today"));
    const fuelCost = optionalNumber(g(row, "fuelCostMonth", "fuel_cost_month"));
    const status = String(g(row, "status") ?? "");
    const qualifiedTrips = hasUtilizationEvidence(row);
    const qualifiedIdle = hasQualifiedEvidence(row, "idle");
    const qualifiedFuelEvidence = hasQualifiedEvidence(row, "fuel");
    const results: Opportunity[] = [];

    if (qualifiedIdle && idleMinutes !== null && idleMinutes >= 45) {
      results.push({
        id: `${vehicleId}-idle`,
        vehicleId,
        vehicleCode,
        severity: idleMinutes >= 90 ? "Critical" : "High",
        title: "Review recorded idle time",
        detail: `${vehicleCode} has ${Math.round(idleMinutes)} qualified idle minutes recorded today. Review routing, dispatch timing and asset health before taking action.`,
        actionLabel: "Open efficiency view",
        actionRoute: "/fleet-utilization/efficiency",
        impact: idleMinutes,
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
        impact: 180,
      });
    }

    if (qualifiedTrips && qualifiedFuelEvidence && fuelCost !== null && fuelAverage !== null && utilization !== null && fuelCost > 0 && fuelCost >= fuelAverage * 1.25 && utilization < 70) {
      results.push({
        id: `${vehicleId}-fuel`,
        vehicleId,
        vehicleCode,
        severity: "Medium",
        title: "Fuel spend is outpacing output",
        detail: `${vehicleCode} is burning more monthly fuel than fleet peers while only delivering ${Math.round(utilization)}% utilization.`,
        actionLabel: "Open efficiency view",
        actionRoute: "/fleet-utilization/efficiency",
        impact: 70 + fuelCost / Math.max(fuelAverage, 1),
      });
    }

    if (qualifiedTrips && utilization !== null && utilization <= 35 && /available|idle/i.test(status)) {
      results.push({
        id: `${vehicleId}-reserve`,
        vehicleId,
        vehicleCode,
        severity: "Medium",
        title: "Available asset has low recorded use",
        detail: `${vehicleCode} has ${Math.round(utilization)}% qualified trip utilization and an ${status} status. Confirm readiness and demand before rebalancing it.`,
        actionLabel: "Open capacity board",
        actionRoute: "/fleet-utilization/capacity",
        impact: 60 + (100 - utilization),
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

  const capacityRows = useMemo(
    () => [...filtered].sort((a, b) => {
      const evidenceOrder = Number(hasUtilizationEvidence(b)) - Number(hasUtilizationEvidence(a));
      if (evidenceOrder) return evidenceOrder;
      return (optionalNumber(g(a, "utilizationPct", "utilization_pct")) ?? Number.MAX_SAFE_INTEGER)
        - (optionalNumber(g(b, "utilizationPct", "utilization_pct")) ?? Number.MAX_SAFE_INTEGER);
    }),
    [filtered],
  );
  const efficiencyRows = useMemo(
    () => [...filtered].sort((a, b) => (optionalNumber(g(b, "idleMinutesToday", "idle_minutes_today")) ?? -1)
      - (optionalNumber(g(a, "idleMinutesToday", "idle_minutes_today")) ?? -1)
      || (optionalNumber(g(b, "fuelCostMonth", "fuel_cost_month")) ?? -1)
      - (optionalNumber(g(a, "fuelCostMonth", "fuel_cost_month")) ?? -1)),
    [filtered],
  );
  const chartData = useMemo(
    () =>
      [...rows]
        .filter(hasUtilizationEvidence)
        .sort((a, b) => num(g(b, "utilizationPct")) - num(g(a, "utilizationPct")))
        .slice(0, 8)
        .map((row) => ({
          name: String(g(row, "vehicleCode", "vehicle_code") ?? ""),
          utilization: Math.round(num(g(row, "utilizationPct"))),
        })),
    [rows],
  );

  if (listQ.isLoading || summaryQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={listQ.error instanceof Error ? listQ.error.message : "Unable to load fleet utilization."} />;
  if (summaryQ.isError) return <ErrorState message={summaryQ.error instanceof Error ? summaryQ.error.message : "Unable to load utilization summary."} />;

  const total = optionalNumber(g(summary, "totalVehicles", "total_vehicles")) ?? rows.length;
  const active = optionalNumber(g(summary, "activeVehicles", "active_vehicles")) ?? rows.filter((row) => /active|on route/i.test(String(g(row, "status") ?? ""))).length;
  const available = optionalNumber(g(summary, "availableVehicles", "available_vehicles")) ?? rows.filter((row) => /available/i.test(String(g(row, "status") ?? ""))).length;
  const maintenance = optionalNumber(g(summary, "maintenanceVehicles", "maintenance_vehicles")) ?? rows.filter((row) => /maintenance|out of service/i.test(String(g(row, "status") ?? ""))).length;
  const utilization = optionalNumber(g(summary, "avgUtilizationPct", "avg_utilization_pct"));
  const utilizationEvidenceVehicles = optionalNumber(g(summary, "utilizationEvidenceVehicles", "utilization_evidence_vehicles"))
    ?? rows.filter(hasUtilizationEvidence).length;
  const idleHours = optionalNumber(g(summary, "idleHoursToday", "idle_hours_today"));
  const idleCost = optionalNumber(g(summary, "idleCostToday", "idle_cost_today"));
  const evidenceGaps = Math.max(0, total - utilizationEvidenceVehicles);
  const estimatedOpenTrips = rows.reduce((sum, row) => sum + num(g(row, "openTripEstimateCount", "open_trip_estimate_count")), 0);

  const exportRows =
    section === "efficiency" ? efficiencyRows :
    section === "capacity" ? capacityRows :
    section === "opportunities" ? opportunities :
    rows;

  return (
    <div className="fleet-console page-stack min-w-0">
      <ConsoleRail
        eyebrow="Fleet · Capacity"
        icon={<Gauge className="h-3.5 w-3.5 text-teal-700" />}
        title="Fleet Utilization"
        meta={<>
          <span className="font-bold text-slate-700 tabular-nums">{total}</span> units tracked ·{" "}
          <span className="font-bold text-emerald-600 tabular-nums">{active}</span> active ·{" "}
          <span className="font-bold text-sky-600 tabular-nums">{available}</span> in reserve ·{" "}
          <span className="font-bold text-rose-600 tabular-nums">{maintenance}</span> maintenance status
        </>}
        actions={<>
          <button type="button" onClick={() => exportCsv("fleet-utilization", exportRows as AnyRecord[])} className="btn-ghost h-10">
            Export current view
          </button>
          <button type="button" onClick={() => navigate("/dispatch")} className="btn-primary h-10">
            Open dispatch coverage <ArrowRight className="h-4 w-4" />
          </button>
        </>}
      />

      <ConsoleNav sections={SECTIONS} active={section} onSelect={(key) => navigate(`/fleet-utilization/${key}`)} />

      <section aria-label="Fleet utilization indicators" className="panel grid grid-cols-2 gap-px overflow-hidden bg-slate-200 p-px xl:grid-cols-4">
        <UtilMetric Icon={Gauge} iconClass="text-teal-700" label="30-day utilization" value={utilization === null ? "—" : `${Math.round(utilization)}%`} caption={evidenceGaps ? `${utilizationEvidenceVehicles} of ${total} units have qualified trip-hour evidence` : estimatedOpenTrips ? `${estimatedOpenTrips} open trip(s) estimated and capped at 24h` : "Qualified active trip hours ÷ 240-hour baseline"} />
        <UtilMetric Icon={Truck} iconClass="text-emerald-700" label="Available status" value={String(available)} caption="Persisted vehicle status; readiness evidence unavailable" />
        <UtilMetric Icon={Wrench} iconClass="text-rose-700" label="Maintenance status" value={String(maintenance)} caption="Persisted Maintenance or Out of Service status" attention={maintenance > 0} />
        <UtilMetric Icon={Clock3} iconClass="text-amber-700" label="Qualified idle evidence" value={idleCost !== null ? `$${idleCost.toLocaleString()}` : idleHours !== null ? `${idleHours}h` : "—"} caption={idleHours === null ? "No qualified idling evidence is available" : idleCost === null ? `${idleHours} recorded hours; cost evidence unavailable` : `${idleHours} qualified idle hours recorded`} attention={idleHours !== null && idleHours > 0} />
      </section>

      {section === "overview" && (
        <div className="space-y-3">
          <section className="panel p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Top action queue</h2>
                <p className="text-sm text-slate-500">Items supported by qualified source evidence or persisted maintenance status.</p>
              </div>
              <button type="button" className="btn-ghost min-h-11 sm:min-h-9" onClick={() => navigate("/fleet-utilization/opportunities")}>Open full queue</button>
            </div>
            <div className="mt-3 grid gap-2 xl:grid-cols-3">
              {opportunities.slice(0, 3).map((item) => (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => navigate(item.actionRoute)}
                  className={`min-h-11 rounded-xl border p-3 text-left shadow-sm transition hover:-translate-y-0.5 hover:shadow-md ${toneClass(item.severity)}`}
                >
                  <div className="flex items-center justify-between gap-3">
                    <StatusBadge status={item.severity} />
                    <ArrowRight className="h-4 w-4 text-slate-400" />
                  </div>
                  <h3 className="mt-2 text-sm font-semibold text-slate-900">{item.title}</h3>
                  <p className="mt-1 text-sm text-slate-600">{item.detail}</p>
                  <p className="mt-2 text-[11px] font-bold uppercase tracking-[0.14em] text-slate-500">{item.actionLabel}</p>
                </button>
              ))}
              {!opportunities.length && (
                <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600 xl:col-span-3">
                  No evidence-qualified utilization action is available. An empty queue does not prove that capacity, readiness, fuel, or idle performance is within range.
                </div>
              )}
            </div>
          </section>

          <details className="panel group">
            <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between gap-3 px-4 py-2 text-sm font-semibold text-slate-800 marker:content-none sm:min-h-9">
              <span>Capacity evidence breakdown</span>
              <span className="text-xs font-medium text-slate-500 group-open:hidden">Available, idle and maintenance details</span>
              <span className="hidden text-xs font-medium text-slate-500 group-open:inline">Hide details</span>
            </summary>
            <div className="grid gap-2 border-t border-slate-200 p-3 lg:grid-cols-3">
              <InsightTile
                icon={<Gauge className="h-4 w-4" />}
                label="Available coverage"
                value={`${available} units`}
                body={`${available} units carry a persisted Available status. Dispatch eligibility and readiness require separate qualified evidence.`}
              />
              <InsightTile
                icon={<Clock3 className="h-4 w-4" />}
                label="Idle leakage"
                value={idleHours === null ? "—" : `${idleHours}h`}
                body={idleHours === null ? "Qualified idling evidence is unavailable." : idleCost === null ? "Qualified idle duration is available; recorded cost evidence is unavailable." : `$${idleCost.toLocaleString()} of recorded idle cost accompanies the qualified duration evidence.`}
              />
              <InsightTile
                icon={<Wrench className="h-4 w-4" />}
                label="Maintenance blockers"
                value={String(maintenance)}
                body={`${maintenance} units are blocked from contributing capacity and should stay tied to work orders, defects and service readiness.`}
              />
            </div>
          </details>

          <section className="panel p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Entity links</h2>
                <p className="text-sm text-slate-500">Keep utilization decisions tied to the modules that can actually unblock them.</p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                Connected workflows
              </span>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-2 xl:grid-cols-4">
              {RELATED_ENTITIES.map((item) => (
                <button
                  key={item.label}
                  type="button"
                  onClick={() => navigate(item.route)}
                  className="group min-h-11 rounded-xl border border-slate-200 bg-white p-3 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-teal-200 hover:shadow-md"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-semibold text-slate-900">{item.label}</span>
                    <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-teal-500" />
                  </div>
                  <p className="mt-1 text-sm text-slate-500">{item.note}</p>
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
                  className={`rounded-xl px-3 py-2 text-xs font-semibold transition ${
                    statusFilter === option ? "bg-slate-900 text-white" : "border border-slate-200 bg-white text-slate-600 hover:bg-slate-50"
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
                      <th className="px-5 py-3 font-semibold">Trip utilization</th>
                      <th className="hidden px-5 py-3 font-semibold lg:table-cell">Evidence</th>
                      <th className="hidden px-5 py-3 font-semibold lg:table-cell">Idle</th>
                      <th className="hidden px-5 py-3 font-semibold xl:table-cell">Jobs</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {capacityRows.map((row) => {
                      const utilizationValue = optionalNumber(g(row, "utilizationPct", "utilization_pct"));
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
                            {utilizationValue === null ? <span className="text-slate-400">—</span> : (
                              <div className="flex items-center gap-3">
                                <div className="h-2 flex-1 rounded-full bg-slate-100">
                                  <div className="h-2 rounded-full bg-gradient-to-r from-teal-500 to-sky-500" style={{ width: `${Math.min(100, utilizationValue)}%` }} />
                                </div>
                                <span className="w-10 text-xs font-semibold text-slate-700">{Math.round(utilizationValue)}%</span>
                              </div>
                            )}
                          </td>
                          <td className="hidden px-5 py-3.5 text-slate-600 lg:table-cell">{hasUtilizationEvidence(row) ? "Qualified trip hours" : "No qualified trip evidence"}</td>
                          <td className="hidden px-5 py-3.5 text-slate-600 lg:table-cell">{hasQualifiedEvidence(row, "idle") ? minutes(g(row, "idleMinutesToday", "idle_minutes_today")) : "—"}</td>
                          <td className="hidden px-5 py-3.5 text-slate-600 xl:table-cell">{num(g(row, "activeJobs"))}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
                {!capacityRows.length && <EmptyState title="No vehicles match this capacity view" subtitle="Try another status or search term." />}
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
                      <h2 className="text-lg font-semibold text-slate-900">Recorded trip utilization</h2>
                      <p className="text-sm text-slate-500">Only vehicles with qualified trip-hour evidence appear in this chart.</p>
                    </div>
                    <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                      Qualified trip evidence
                    </span>
                  </div>
                  <div className="mt-4 h-72">
                    <ResponsiveContainer width="100%" height="100%">
                      <BarChart data={chartData} margin={{ top: 4, right: 10, left: -20, bottom: 4 }}>
                        <CartesianGrid stroke="rgba(15,23,42,0.08)" strokeDasharray="3 3" />
                        <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                        <YAxis tick={{ fontSize: 11 }} domain={[0, 100]} />
                        <Tooltip
                          contentStyle={{ background: tokens.surface, border: `1px solid ${tokens.border}`, borderRadius: 12, fontSize: 12 }}
                        />
                        <Bar dataKey="utilization" fill={chart.teal700} radius={[6, 6, 0, 0]} />
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
                      className="w-full rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md"
                    >
                      <div className="flex items-center justify-between gap-3">
                        <div>
                          <p className="text-sm font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</p>
                          <p className="text-xs text-slate-500">{String(g(row, "status") ?? "Unknown")}</p>
                        </div>
                        <StatusBadge status={g(row, "status")} />
                      </div>
                      <div className="mt-4 grid grid-cols-3 gap-3 text-sm">
                        <MetricMini label="Idle today" value={hasQualifiedEvidence(row, "idle") ? minutes(g(row, "idleMinutesToday", "idle_minutes_today")) : "—"} />
                        <MetricMini label="Fuel month" value={hasQualifiedEvidence(row, "fuel") ? money(g(row, "fuelCostMonth", "fuel_cost_month")) : "—"} />
                        <MetricMini label="Utilization" value={hasUtilizationEvidence(row) ? percent(g(row, "utilizationPct", "utilization_pct")) : "—"} />
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
                      <th className="hidden px-5 py-3 font-semibold xl:table-cell">Evidence basis</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {efficiencyRows.map((row) => (
                      <tr key={String(row.id)} onClick={() => setSelectedId(String(row.id))} className="cursor-pointer transition hover:bg-slate-50">
                        <td className="px-5 py-3.5">
                          <div className="font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</div>
                          <div className="text-xs text-slate-500">{String(g(row, "driverName", "driver_name") ?? "No driver linked")}</div>
                        </td>
                        <td className="px-5 py-3.5 text-slate-700">{hasQualifiedEvidence(row, "idle") ? `${minutes(g(row, "idleMinutesToday", "idle_minutes_today"))} / ${num(g(row, "idleEventsToday", "idle_events_today"))} events` : "—"}</td>
                        <td className="px-5 py-3.5 text-slate-700">{hasQualifiedEvidence(row, "fuel") ? money(g(row, "fuelCostMonth", "fuel_cost_month")) : "—"}</td>
                        <td className="hidden px-5 py-3.5 text-slate-700 lg:table-cell">{hasUtilizationEvidence(row) ? percent(g(row, "utilizationPct", "utilization_pct")) : "—"}</td>
                        <td className="hidden px-5 py-3.5 text-slate-700 xl:table-cell">{hasUtilizationEvidence(row) ? "Qualified trip hours" : "Unavailable"}</td>
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
                      <button type="button" className="btn-ghost h-9" onClick={() => navigate(item.actionRoute)}>
                        {item.actionLabel}
                      </button>
                    </div>
                    <p className="mt-3 text-sm text-slate-600">{item.detail}</p>
                  </div>
                )) : (
                  <EmptyState title="No evidence-qualified intervention" subtitle="An empty queue does not establish that utilization, readiness, fuel, or idle performance is within range." />
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

function UtilMetric({
  Icon,
  iconClass,
  label,
  value,
  caption,
  attention = false,
}: {
  Icon: React.ComponentType<{ className?: string }>;
  iconClass: string;
  label: string;
  value: string;
  caption: string;
  attention?: boolean;
}) {
  return (
    <div className="min-w-0 bg-white px-3 py-2">
      <div className="flex items-center gap-2">
        <Icon className={`h-4 w-4 shrink-0 ${iconClass}`} />
        <p className="min-w-0 text-xs font-medium text-slate-600">{label}</p>
        <p className={`ml-auto shrink-0 text-lg font-bold tabular-nums ${attention ? "text-rose-700" : "text-slate-950"}`}>{value}</p>
      </div>
      <p className="mt-1 text-[11px] leading-4 text-slate-500">{caption}</p>
    </div>
  );
}

function InsightTile({ icon, label, value, body }: { icon: React.ReactNode; label: string; value: string; body: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50/70 p-3">
      <div className="flex items-center gap-2">
        <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-white text-slate-500 shadow-sm">{icon}</div>
        <span className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-500">{label}</span>
        <span className="ml-auto text-lg font-bold tracking-tight text-slate-900">{value}</span>
      </div>
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

  const vehicleCode = String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`);
  const qualifiedTrips = hasUtilizationEvidence(row);
  const qualifiedFuel = hasQualifiedEvidence(row, "fuel");
  const qualifiedIdle = hasQualifiedEvidence(row, "idle");
  const utilization = qualifiedTrips ? percent(g(row, "utilizationPct", "utilization_pct")) : "—";
  const fuelCost = qualifiedFuel ? money(g(row, "fuelCostMonth", "fuel_cost_month")) : "—";
  const idle = qualifiedIdle ? minutes(g(row, "idleMinutesToday", "idle_minutes_today")) : "—";
  const activeHours = qualifiedTrips ? optionalNumber(g(row, "activeHours30d", "active_hours_30d")) : null;

  return (
    <aside className="panel p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Selected unit</p>
          <h3 className="mt-1 text-lg font-semibold text-slate-900">{vehicleCode}</h3>
          <p className="text-sm text-slate-500">{String(g(row, "driverName", "driver_name") ?? "Unassigned driver")}</p>
        </div>
        <StatusBadge status={g(row, "status")} />
      </div>
      <div className="mt-4 grid grid-cols-2 gap-3">
        <MetricMini label="Recorded status" value={String(g(row, "status") ?? "Unavailable")} />
        <MetricMini label="30-day utilization" value={utilization} />
        <MetricMini label="Qualified idle today" value={idle} />
        <MetricMini label="Qualified fuel month" value={fuelCost} />
      </div>
      <p className="mt-2 text-xs text-slate-500">{activeHours === null ? "No qualified trip-hour evidence is available for this unit; utilization remains unavailable." : `${activeHours.toFixed(1)} qualified active trip hours in the last 30 days, measured against a 240-hour operating baseline.`}</p>
      <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
        {section === "capacity" && `${vehicleCode} has ${num(g(row, "activeJobs", "active_jobs"))} persisted active job record(s). Status and trip utilization do not establish dispatch readiness; confirm that in the source workflow.`}
        {section === "efficiency" && `${vehicleCode} has ${idle === "—" ? "no qualified idle duration" : idle} and ${fuelCost === "—" ? "no qualified fuel spend" : `${fuelCost} qualified fuel spend`} in the current periods.`}
        {section === "opportunities" && `${vehicleCode} appears only when a bounded rule is supported by qualified trip, fuel, or idle evidence, or by persisted maintenance status.`}
      </div>
      <div className="mt-4 flex flex-wrap gap-2">
        <button type="button" className="btn-ghost h-9" onClick={() => onNavigate(routeForVehicle(section))}>Open vehicle module</button>
        <button type="button" className="btn-ghost h-9" onClick={() => onNavigate("/dispatch")}>Open dispatch</button>
        <button type="button" className="btn-ghost h-9" onClick={() => onNavigate("/maintenance")}>Open maintenance</button>
      </div>
    </aside>
  );
}
