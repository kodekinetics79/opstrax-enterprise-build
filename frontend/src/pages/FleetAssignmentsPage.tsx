import { useMemo, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  AlertTriangle,
  ArrowRight,
  Route,
  Satellite,
  ShieldAlert,
  ShieldCheck,
  Sparkles,
  Truck,
  Users,
} from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { dispatchApi } from "@/services/dispatchApi";
import { EmptyState, ErrorState, exportCsv, KpiCard, LoadingState, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";

type AssignmentSection = "overview" | "board" | "exceptions" | "owners";

const SECTIONS: Array<{ key: AssignmentSection; label: string; description: string }> = [
  { key: "overview", label: "Overview", description: "Coverage posture and quick moves" },
  { key: "board", label: "Assignment board", description: "Live driver and vehicle pairing flow" },
  { key: "exceptions", label: "Exception radar", description: "Work the issues before they cascade" },
  { key: "owners", label: "Owner ops", description: "Partner capacity and owner-operator records" },
];

const RELATED_ENTITIES = [
  { label: "Dispatch", route: "/dispatch", note: "Open the broader job and route cockpit" },
  { label: "Vehicles", route: "/vehicles/roster", note: "Inspect unit readiness and availability" },
  { label: "Drivers", route: "/drivers/roster", note: "Inspect fit, HOS and safety posture" },
  { label: "Proof of Delivery", route: "/proof-of-delivery", note: "Review pickup and delivery evidence" },
];

function readSection(pathname: string): AssignmentSection {
  if (pathname === "/owners") return "owners";
  const section = pathname.split("/").filter(Boolean)[1];
  if (section === "board" || section === "exceptions" || section === "owners") return section;
  return "overview";
}

const g = (row: AnyRecord, ...keys: string[]) => {
  for (const key of keys) if (row?.[key] != null && row[key] !== "") return row[key];
  return undefined;
};

const num = (value: unknown) => (Number.isFinite(Number(value)) ? Number(value) : 0);

function normalizeOwner(row: AnyRecord): AnyRecord {
  return {
    ...row,
    ownerCode: row.ownerCode ?? row.owner_code ?? String(row.id ?? ""),
    ownerName: row.ownerName ?? row.owner_name ?? row.title ?? "",
    contactName: row.contactName ?? row.contact_name ?? row.assignedToName ?? "",
    vehicleCount: num(row.vehicleCount ?? row.vehicle_count ?? row.numericValue ?? row.numeric_value),
    revenueSharePct: num(row.revenueSharePct ?? row.revenue_share_pct ?? row.secondaryValue ?? row.secondary_value),
    totalLoads: num(row.totalLoads ?? row.total_loads ?? row.metricValue ?? row.metric_value),
    contractExpiry: row.contractExpiry ?? row.contract_expiry ?? row.dueDate ?? row.due_date ?? "",
    status: row.status ?? "Active",
  };
}

function ownersApi() {
  return unwrap<AnyRecord[]>(apiClient.get("/api/owners")).then((rows) => rows.map(normalizeOwner));
}

function assignmentStatus(row: AnyRecord) {
  return String(g(row, "assignmentStatus", "assignment_status", "status") ?? "assigned").toLowerCase().replace(/\s+/g, "_");
}

function statusGroup(status: string) {
  if (/assigned/.test(status)) return "Assigned";
  if (/accepted/.test(status)) return "Accepted";
  if (/pickup|transit|delivery/.test(status)) return "In transit";
  if (/exception/.test(status)) return "Exception";
  if (/cancel/.test(status)) return "Cancelled";
  if (/deliver|complete/.test(status)) return "Delivered";
  return "Assigned";
}

function groupAssignments(rows: AnyRecord[]) {
  const groups: Record<string, AnyRecord[]> = {
    Assigned: [],
    Accepted: [],
    "In transit": [],
    Exception: [],
    Delivered: [],
  };
  for (const row of rows) {
    const key = statusGroup(assignmentStatus(row));
    if (!groups[key]) groups[key] = [];
    groups[key].push(row);
  }
  return groups;
}

function priorityClass(priority: string) {
  if (/critical|urgent|high/i.test(priority)) return "text-red-600";
  if (/medium|warning/i.test(priority)) return "text-amber-700";
  return "text-slate-500";
}

export function FleetAssignmentsPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const section = readSection(location.pathname);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const assignmentsQ = useQuery({
    queryKey: ["dispatch", "assignments"],
    queryFn: () => dispatchApi.assignments({ limit: 100 }),
    refetchInterval: 30_000,
  });
  const detailQ = useQuery({
    queryKey: ["dispatch", "assignments", "detail", selectedId],
    queryFn: () => dispatchApi.assignmentDetail(String(selectedId)),
    enabled: selectedId != null,
  });
  const recommendationsQ = useQuery({
    queryKey: ["dispatch", "recommendations"],
    queryFn: dispatchApi.recommendations,
    refetchInterval: 60_000,
  });
  const exceptionsQ = useQuery({
    queryKey: ["dispatch", "exceptions"],
    queryFn: () => dispatchApi.exceptions(),
    refetchInterval: 30_000,
  });
  const availableDriversQ = useQuery({
    queryKey: ["dispatch", "available-drivers"],
    queryFn: dispatchApi.availableDrivers,
    refetchInterval: 60_000,
  });
  const availableVehiclesQ = useQuery({
    queryKey: ["dispatch", "available-vehicles"],
    queryFn: dispatchApi.availableVehicles,
    refetchInterval: 60_000,
  });
  const ownersQ = useQuery({
    queryKey: ["owners"],
    queryFn: ownersApi,
    refetchInterval: 120_000,
  });

  const assignments = (assignmentsQ.data ?? []) as AnyRecord[];
  const recommendations = (recommendationsQ.data ?? []) as AnyRecord[];
  const exceptions = (exceptionsQ.data ?? []) as AnyRecord[];
  const availableDrivers = (availableDriversQ.data ?? []) as AnyRecord[];
  const availableVehicles = (availableVehiclesQ.data ?? []) as AnyRecord[];
  const owners = (ownersQ.data ?? []) as AnyRecord[];

  const selected = useMemo(
    () => assignments.find((row) => String(row.id) === selectedId) ?? assignments[0] ?? null,
    [assignments, selectedId],
  );
  const detail = (detailQ.data ?? {}) as AnyRecord;
  const grouped = useMemo(() => groupAssignments(assignments), [assignments]);

  if (assignmentsQ.isLoading) return <LoadingState />;
  if (assignmentsQ.isError) {
    return <ErrorState message={assignmentsQ.error instanceof Error ? assignmentsQ.error.message : "Unable to load assignments."} />;
  }

  const activeAssignments = assignments.filter((row) => !/deliver|cancel/.test(assignmentStatus(row))).length;
  const inTransit = assignments.filter((row) => /pickup|transit|delivery/.test(assignmentStatus(row))).length;
  const exceptionCount = exceptions.filter((row) => String(g(row, "status") ?? "open").toLowerCase() !== "resolved").length;
  const avgMatch = assignments.length
    ? Math.round(assignments.reduce((sum, row) => sum + num(g(row, "matchScore", "match_score")), 0) / assignments.length)
    : 0;
  const readyDrivers = availableDrivers.filter((row) => !num(g(row, "safetyBlocked", "safety_blocked")) && !num(g(row, "statusBlocked", "status_blocked"))).length;
  const readyVehicles = availableVehicles.filter((row) => !num(g(row, "criticalDefectCount", "critical_defect_count")) && !num(g(row, "blockingWoCount", "blocking_wo_count"))).length;
  const exportRows =
    section === "exceptions" ? exceptions :
    section === "owners" ? owners :
    assignments;

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
                  <Route className="h-3 w-3" /> Dispatch Pairing
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Live pairing command surface</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Assignments
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Coverage, eligibility pressure, exceptions and partner capacity in one place
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button type="button" onClick={() => exportCsv("assignments", exportRows as AnyRecord[])} className="fh-btn-ghost">Export live view</button>
              <button type="button" onClick={() => navigate("/dispatch")} className="fh-btn-primary">Open dispatch cockpit <ArrowRight className="h-3.5 w-3.5" /></button>
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
              onClick={() => navigate(`/assignments/${item.key}`)}
              className={`rounded-xl px-3 py-2.5 text-left transition ${
                section === item.key ? "bg-slate-900 text-white shadow-sm" : "bg-slate-50/40 hover:bg-slate-100"
              }`}
            >
              <div className="text-xs font-bold uppercase tracking-[0.14em]">{item.label}</div>
              <div className={`mt-0.5 text-[11px] ${section === item.key ? "text-slate-300" : "text-slate-500"}`}>{item.description}</div>
            </button>
          ))}
        </div>
      </nav>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Active assignments" value={String(activeAssignments)} trend={`${inTransit} in motion`} />
        <KpiCard label="Exception pressure" value={String(exceptionCount)} status="Review" trend={exceptionCount ? "Needs dispatcher attention" : "No open dispatch issues"} />
        <KpiCard label="Average match score" value={`${avgMatch}%`} trend={`${readyDrivers} ready drivers / ${readyVehicles} ready units`} />
        <KpiCard label="Partner capacity" value={String(owners.length)} trend={owners.length ? "Owner-operator records connected" : "No owner records connected"} />
      </div>

      {section === "overview" && (
        <div className="space-y-6">
          <div className="grid gap-4 lg:grid-cols-3">
            <ModuleCard
              title="Assignment board"
              body="See the pairing lifecycle by stage, with one-click access to proof, exceptions and audit."
              action="Open board"
              onClick={() => navigate("/assignments/board")}
              icon={<Route className="h-5 w-5" />}
            />
            <ModuleCard
              title="Exception radar"
              body="Bring delays, proof gaps and assignment issues to the surface before customers feel them."
              action="Open exception radar"
              onClick={() => navigate("/assignments/exceptions")}
              icon={<AlertTriangle className="h-5 w-5" />}
            />
            <ModuleCard
              title="Owner ops"
              body="Treat owner-operators as strategic reserve capacity, not a hidden table in the back office."
              action="Open owner ops"
              onClick={() => navigate("/assignments/owners")}
              icon={<Users className="h-5 w-5" />}
            />
          </div>

          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Coverage posture</h2>
                <p className="text-sm text-slate-500">A buyer should be able to understand assignment health in seconds, not dig through tabs and exports.</p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                Live command summary
              </span>
            </div>
            <div className="mt-4 grid gap-3 lg:grid-cols-3">
              <InsightTile
                icon={<ShieldCheck className="h-4 w-4" />}
                label="Ready bench"
                value={`${readyDrivers} drivers / ${readyVehicles} units`}
                body="Immediately eligible resources that can absorb new or recovering work without starting from a cold search."
              />
              <InsightTile
                icon={<AlertTriangle className="h-4 w-4" />}
                label="Open exceptions"
                value={String(exceptionCount)}
                body={exceptionCount ? "Assignment issues are active and should stay visible in the dispatch heartbeat." : "No open dispatch exceptions in the current tenant data."}
              />
              <InsightTile
                icon={<Sparkles className="h-4 w-4" />}
                label="Top recommendation"
                value={recommendations[0] ? `${Math.round(num(g(recommendations[0], "score")))}% fit` : "No recommendation"}
                body={recommendations[0] ? String(g(recommendations[0], "customerName", "customer_name", "title") ?? "Recommendation ready for dispatcher review.") : "No dispatch recommendations returned right now."}
              />
            </div>
          </section>

          <section className="panel p-5">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Suggested pairings</h2>
                <p className="text-sm text-slate-500">These are coming from the live dispatch recommendations endpoint, not seeded examples.</p>
              </div>
              <button type="button" className="fh-btn-ghost h-9" onClick={() => navigate("/assignments/board")}>Open board</button>
            </div>
            <div className="mt-4 grid gap-3 xl:grid-cols-3">
              {recommendations.slice(0, 3).map((row, index) => (
                <div key={String(row.id ?? index)} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                  <div className="flex items-center justify-between">
                    <StatusBadge status={g(row, "priority", "slaStatus", "sla_status") ?? "Suggested"} />
                    <span className="text-sm font-semibold text-teal-700">{Math.round(num(g(row, "score")))}% fit</span>
                  </div>
                  <h3 className="mt-3 text-sm font-semibold text-slate-900">{String(g(row, "jobNumber", "job_number", "jobCode", "job_code") ?? "Open load")}</h3>
                  <p className="mt-2 text-sm text-slate-600">{String(g(row, "customerName", "customer_name") ?? "Customer")} · {String(g(row, "driverName", "driver_name") ?? "Driver TBD")} · {String(g(row, "vehicleCode", "vehicle_code") ?? "Vehicle TBD")}</p>
                  <p className={`mt-3 text-xs font-semibold uppercase tracking-[0.14em] ${priorityClass(String(g(row, "priority") ?? ""))}`}>
                    {String(g(row, "priority") ?? "Standard")}
                  </p>
                </div>
              ))}
              {!recommendations.length && (
                <EmptyState title="No live recommendations" subtitle="The recommendation table is connected, but this tenant is not returning rows right now." />
              )}
            </div>
          </section>

          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Entity links</h2>
                <p className="text-sm text-slate-500">Assignments should stay connected to the records that let operators resolve issues immediately.</p>
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
                  className="group rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-teal-200 hover:shadow-md"
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

      {section === "board" && (
        <div className="grid gap-4 xl:grid-cols-[1.6fr_0.95fr]">
          <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-5">
            {Object.entries(grouped).map(([label, rows]) => (
              <section key={label} className="panel min-h-[420px] p-4">
                <div className="mb-4 flex items-center justify-between">
                  <h2 className="text-sm font-bold uppercase tracking-[0.16em] text-slate-400">{label}</h2>
                  <span className="rounded-full bg-slate-100 px-2 py-1 text-xs text-slate-600">{rows.length}</span>
                </div>
                <div className="space-y-3">
                  {rows.slice(0, 8).map((row) => (
                    <button
                      key={String(row.id)}
                      type="button"
                      onClick={() => setSelectedId(String(row.id))}
                      className={`w-full rounded-2xl border p-3 text-left transition hover:-translate-y-0.5 hover:shadow-sm ${
                        selectedId === String(row.id) ? "border-teal-300 bg-teal-50" : "border-slate-200 bg-white hover:border-slate-300"
                      }`}
                    >
                      <div className="flex items-start justify-between gap-3">
                        <p className="font-semibold text-slate-900">{String(g(row, "jobNumber", "job_number") ?? `Assignment ${row.id}`)}</p>
                        <span className={`text-xs font-semibold uppercase tracking-[0.14em] ${priorityClass(String(g(row, "priority") ?? ""))}`}>
                          {String(g(row, "priority") ?? "Standard")}
                        </span>
                      </div>
                      <p className="mt-2 text-xs text-slate-500">{String(g(row, "customerName", "customer_name") ?? "Customer")} · {String(g(row, "trackingCode", "tracking_code") ?? "Tracking pending")}</p>
                      <p className="mt-2 text-xs text-slate-500">{String(g(row, "driverName", "driver_name") ?? "Driver TBD")} / {String(g(row, "vehicleCode", "vehicle_code") ?? "Vehicle TBD")}</p>
                      <div className="mt-3 flex items-center justify-between">
                        <StatusBadge status={g(row, "slaStatus", "sla_status", "status")} />
                        <span className="text-xs font-semibold text-teal-700">{Math.round(num(g(row, "matchScore", "match_score")))}% fit</span>
                      </div>
                      {!!num(g(row, "openExceptions", "open_exceptions")) && (
                        <p className="mt-2 text-xs font-semibold text-red-600">{num(g(row, "openExceptions", "open_exceptions"))} open exception(s)</p>
                      )}
                    </button>
                  ))}
                  {!rows.length && <p className="text-sm text-slate-400">No assignments in this stage.</p>}
                </div>
              </section>
            ))}
          </div>
          <AssignmentDetailPanel
            selected={selected}
            detail={detail}
            loading={detailQ.isLoading}
            onNavigate={navigate}
          />
        </div>
      )}

      {section === "exceptions" && (
        <div className="grid gap-4 xl:grid-cols-[1.3fr_1fr]">
          <section className="panel p-5">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Live exception queue</h2>
                <p className="text-sm text-slate-500">The queue is only as good as the backend links behind it, so this view is connected directly to dispatch exceptions.</p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                {exceptionCount} open
              </span>
            </div>
            <div className="mt-4 space-y-3">
              {exceptions.length ? exceptions.map((row) => (
                <div key={String(row.id)} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <div className="flex items-center gap-2">
                        <StatusBadge status={g(row, "severity") ?? "Open"} />
                        <p className="text-sm font-semibold text-slate-900">{String(g(row, "title") ?? g(row, "exceptionType", "exception_type") ?? "Dispatch exception")}</p>
                      </div>
                      <p className="mt-2 text-sm text-slate-600">{String(g(row, "jobNumber", "job_number") ?? "No job code")} · {String(g(row, "driverName", "driver_name") ?? "Driver unknown")} / {String(g(row, "vehicleCode", "vehicle_code") ?? "Vehicle unknown")}</p>
                    </div>
                    <StatusBadge status={g(row, "status") ?? "Open"} />
                  </div>
                  <p className="mt-3 text-sm text-slate-500">{String(g(row, "notes") ?? "No exception notes recorded.")}</p>
                </div>
              )) : (
                <EmptyState title="No dispatch exceptions" subtitle="This tenant currently has no exception records." />
              )}
            </div>
          </section>

          <section className="panel p-5">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">Recovery bench</h2>
              <p className="text-sm text-slate-500">Operators need to see the immediately available resources that can absorb problem work.</p>
            </div>
            <div className="mt-4 grid gap-3">
              <BenchList title="Drivers" rows={availableDrivers} empty="No available driver pool returned." renderLabel={(row) => String(g(row, "fullName", "full_name", "driverName", "driver_name") ?? `Driver ${row.id}`)} renderMeta={(row) => `${Math.round(num(g(row, "matchReadiness", "match_readiness")))}% readiness · ${num(g(row, "availableHosHours", "available_hos_hours"))}h HOS`} />
              <BenchList title="Vehicles" rows={availableVehicles} empty="No available vehicle pool returned." renderLabel={(row) => String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)} renderMeta={(row) => `${Math.round(num(g(row, "matchReadiness", "match_readiness")))}% readiness · ${num(g(row, "blockingWoCount", "blocking_wo_count"))} blocking WO`} />
            </div>
          </section>
        </div>
      )}

      {section === "owners" && (
        <div className="space-y-4">
          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Owner-operator reserve network</h2>
                <p className="text-sm text-slate-500">If partner capacity is part of the business model, it should feel integrated with dispatch readiness, not orphaned in a side table.</p>
              </div>
              <div className="flex flex-wrap gap-2">
                <StatusBadge status={`${owners.filter((row) => String(g(row, "status") ?? "").toLowerCase() === "active").length} active`} />
                <StatusBadge status={`${owners.reduce((sum, row) => sum + num(g(row, "vehicleCount", "vehicle_count")), 0)} vehicles`} />
              </div>
            </div>
          </section>

          {ownersQ.isLoading ? <LoadingState /> : owners.length ? (
            <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
              <table className="w-full text-left text-sm">
                <thead>
                  <tr className="border-b border-slate-200 text-[11px] uppercase tracking-[0.12em] text-slate-400">
                    <th className="px-5 py-3 font-semibold">Owner</th>
                    <th className="px-5 py-3 font-semibold">Contact</th>
                    <th className="px-5 py-3 font-semibold">Vehicles</th>
                    <th className="px-5 py-3 font-semibold">Revenue share</th>
                    <th className="hidden px-5 py-3 font-semibold lg:table-cell">Loads</th>
                    <th className="hidden px-5 py-3 font-semibold xl:table-cell">Contract expiry</th>
                    <th className="px-5 py-3 font-semibold">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {owners.map((row) => (
                    <tr key={String(row.id)} className="transition hover:bg-slate-50">
                      <td className="px-5 py-3.5">
                        <div className="font-semibold text-slate-900">{String(g(row, "ownerName", "owner_name") ?? "Owner")}</div>
                        <div className="text-xs text-slate-500">{String(g(row, "ownerCode", "owner_code") ?? "--")}</div>
                      </td>
                      <td className="px-5 py-3.5">
                        <div className="text-slate-700">{String(g(row, "contactName", "contact_name") ?? "No contact")}</div>
                        <div className="text-xs text-slate-500">{String(g(row, "phone") ?? "No phone")}</div>
                      </td>
                      <td className="px-5 py-3.5 text-slate-700">{num(g(row, "vehicleCount", "vehicle_count"))}</td>
                      <td className="px-5 py-3.5 text-slate-700">{num(g(row, "revenueSharePct", "revenue_share_pct"))}%</td>
                      <td className="hidden px-5 py-3.5 text-slate-700 lg:table-cell">{num(g(row, "totalLoads", "total_loads"))}</td>
                      <td className="hidden px-5 py-3.5 text-slate-700 xl:table-cell">{String(g(row, "contractExpiry", "contract_expiry") ?? "—")}</td>
                      <td className="px-5 py-3.5"><StatusBadge status={g(row, "status") ?? "Active"} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <EmptyState title="No owner-operator records connected" subtitle="The owner registry route is live, but this tenant is not returning rows yet." />
          )}
        </div>
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
      className="group rounded-2xl border border-slate-200 bg-white p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md"
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

function BenchList({
  title,
  rows,
  empty,
  renderLabel,
  renderMeta,
}: {
  title: string;
  rows: AnyRecord[];
  empty: string;
  renderLabel: (row: AnyRecord) => string;
  renderMeta: (row: AnyRecord) => string;
}) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-slate-900">{title}</h3>
        <span className="rounded-full bg-white px-2 py-1 text-xs text-slate-600 shadow-sm">{rows.length}</span>
      </div>
      <div className="mt-3 space-y-2">
        {rows.slice(0, 4).map((row) => (
          <div key={String(row.id)} className="rounded-xl border border-slate-200 bg-white px-3 py-2">
            <p className="text-sm font-semibold text-slate-900">{renderLabel(row)}</p>
            <p className="mt-1 text-xs text-slate-500">{renderMeta(row)}</p>
          </div>
        ))}
        {!rows.length && <p className="text-sm text-slate-400">{empty}</p>}
      </div>
    </div>
  );
}

function AssignmentDetailPanel({
  selected,
  detail,
  loading,
  onNavigate,
}: {
  selected: AnyRecord | null;
  detail: AnyRecord;
  loading: boolean;
  onNavigate: (route: string) => void;
}) {
  if (!selected) {
    return (
      <div className="panel p-5">
        <EmptyState title="No assignment selected" subtitle="Pick an assignment card to inspect proofs, exceptions and audit trail." />
      </div>
    );
  }

  const assignment = (detail.assignment as AnyRecord) || selected;
  const proofs = (detail.proofs as AnyRecord[]) || [];
  const exceptions = (detail.exceptions as AnyRecord[]) || [];
  const auditTrail = (detail.auditTrail as AnyRecord[]) || [];

  return (
    <aside className="panel p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Selected assignment</p>
          <h3 className="mt-1 text-lg font-semibold text-slate-900">{String(g(assignment, "jobNumber", "job_number") ?? `Assignment ${assignment.id}`)}</h3>
          <p className="text-sm text-slate-500">{String(g(assignment, "customerName", "customer_name") ?? "Customer")} · {String(g(assignment, "trackingCode", "tracking_code") ?? "Tracking pending")}</p>
        </div>
        <StatusBadge status={g(assignment, "assignmentStatus", "assignment_status", "status") ?? "Assigned"} />
      </div>

      <div className="mt-4 grid grid-cols-2 gap-3">
        <MetricMini label="Driver" value={String(g(assignment, "driverName", "driver_name") ?? "TBD")} />
        <MetricMini label="Vehicle" value={String(g(assignment, "vehicleCode", "vehicle_code") ?? "TBD")} />
        <MetricMini label="Match score" value={`${Math.round(num(g(assignment, "matchScore", "match_score")))}%`} />
        <MetricMini label="Trip compliance" value={`${Math.round(num(g(assignment, "tripCompliance", "trip_compliance")))}%`} />
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-4">
        <p className="text-sm font-semibold text-slate-900">Operational context</p>
        <p className="mt-2 text-sm text-slate-600">{String(g(assignment, "pickupAddress", "pickup_address") ?? "Pickup pending")} to {String(g(assignment, "dropoffAddress", "dropoff_address") ?? "Dropoff pending")}</p>
        <p className="mt-2 text-sm text-slate-500">{String(g(assignment, "driverPhone", "driver_phone") ?? "Driver phone unavailable")}</p>
      </div>

      <div className="mt-4 grid gap-3">
        <EvidenceSummary title="Proofs" rows={proofs} loading={loading} empty="No proof records returned yet." />
        <EvidenceSummary title="Exceptions" rows={exceptions} loading={loading} empty="No exception records on this assignment." />
        <EvidenceSummary title="Audit" rows={auditTrail} loading={loading} empty="No audit entries returned." />
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <button type="button" className="fh-btn-ghost h-9" onClick={() => onNavigate("/dispatch")}>Open dispatch</button>
        <button type="button" className="fh-btn-ghost h-9" onClick={() => onNavigate("/proof-of-delivery")}>Open proof</button>
        <button type="button" className="fh-btn-ghost h-9" onClick={() => onNavigate("/vehicles/roster")}>Open vehicle</button>
      </div>
    </aside>
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

function EvidenceSummary({
  title,
  rows,
  loading,
  empty,
}: {
  title: string;
  rows: AnyRecord[];
  loading: boolean;
  empty: string;
}) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold text-slate-900">{title}</h4>
        <span className="rounded-full bg-white px-2 py-1 text-xs text-slate-600 shadow-sm">{rows.length}</span>
      </div>
      <div className="mt-3 space-y-2">
        {loading && !rows.length ? <p className="text-sm text-slate-400">Loading…</p> : null}
        {!loading && !rows.length ? <p className="text-sm text-slate-400">{empty}</p> : null}
        {rows.slice(0, 3).map((row, index) => (
          <div key={String(row.id ?? index)} className="rounded-xl border border-slate-200 bg-white px-3 py-2">
            <p className="text-sm font-semibold text-slate-900">{String(g(row, "title", "proofType", "proof_type", "actionName", "action_name", "exceptionType", "exception_type") ?? `${title} item`)}</p>
            <p className="mt-1 text-xs text-slate-500">{String(g(row, "notes", "createdAt", "created_at", "confirmedAt", "confirmed_at", "severity") ?? "No extra detail")}</p>
          </div>
        ))}
      </div>
    </div>
  );
}
