import { useMemo, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle,
  ArrowRight,
  Route,
  ShieldCheck,
  Sparkles,
  Users,
} from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { jobsApi } from "@/services/jobsApi";
import { driversApi } from "@/services/driversApi";
import { vehiclesApi } from "@/services/vehiclesApi";
import {
  useHasDirectPermission,
  useHasPermission,
} from "@/hooks/usePermission";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import {
  assignmentStage,
  currentPairings,
  nextAssignmentStatuses,
  isTerminalAssignment,
} from "@/utils/assignmentPresentation";
import "@/styles/assignments.css";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import { dispatchApi } from "@/services/dispatchApi";
import {
  EmptyState,
  ErrorState,
  exportCsv,
  LoadingState,
  StatusBadge,
} from "@/components/ui";
import type { AnyRecord } from "@/types";

type AssignmentSection = "overview" | "board" | "exceptions" | "owners";

const SECTIONS: Array<{
  key: AssignmentSection;
  label: string;
  description: string;
}> = [
  {
    key: "overview",
    label: "Fleet history",
    description: "Recorded fleet pairings",
  },
  {
    key: "board",
    label: "Assignment list",
    description: "Live driver and vehicle pairing flow",
  },
  {
    key: "exceptions",
    label: "Exception radar",
    description: "Work the issues before they cascade",
  },
  {
    key: "owners",
    label: "Owner ops",
    description: "Partner capacity and owner-operator records",
  },
];

const RELATED_ENTITIES = [
  {
    label: "Dispatch",
    route: "/dispatch",
    note: "Open the broader job and route cockpit",
  },
  {
    label: "Vehicles",
    route: "/vehicles/roster",
    note: "Inspect unit readiness and availability",
  },
  {
    label: "Drivers",
    route: "/drivers/roster",
    note: "Inspect fit, HOS and safety posture",
  },
  {
    label: "Proof of Delivery",
    route: "/proof-of-delivery",
    note: "Review pickup and delivery evidence",
  },
];

function readSection(pathname: string): AssignmentSection {
  if (pathname === "/owners") return "owners";
  const section = pathname.split("/").filter(Boolean)[1];
  if (
    section === "overview" ||
    section === "exceptions" ||
    section === "owners"
  )
    return section;
  return "board";
}

const g = (row: AnyRecord, ...keys: string[]) => {
  for (const key of keys)
    if (row?.[key] != null && row[key] !== "") return row[key];
  return undefined;
};

const num = (value: unknown) =>
  Number.isFinite(Number(value)) ? Number(value) : 0;

function normalizeOwner(row: AnyRecord): AnyRecord {
  return {
    ...row,
    ownerCode: row.ownerCode ?? row.owner_code ?? String(row.id ?? ""),
    ownerName: row.ownerName ?? row.owner_name ?? row.title ?? "",
    contactName:
      row.contactName ?? row.contact_name ?? row.assignedToName ?? "",
    vehicleCount: num(
      row.vehicleCount ??
        row.vehicle_count ??
        row.numericValue ??
        row.numeric_value,
    ),
    revenueSharePct: num(
      row.revenueSharePct ??
        row.revenue_share_pct ??
        row.secondaryValue ??
        row.secondary_value,
    ),
    totalLoads: num(
      row.totalLoads ?? row.total_loads ?? row.metricValue ?? row.metric_value,
    ),
    contractExpiry:
      row.contractExpiry ??
      row.contract_expiry ??
      row.dueDate ??
      row.due_date ??
      "",
    status: row.status ?? "Unknown",
  };
}

function ownersApi() {
  return unwrap<AnyRecord[]>(apiClient.get("/api/owners")).then((rows) =>
    rows.map(normalizeOwner),
  );
}

function assignmentStatus(row: AnyRecord) {
  return String(
    g(row, "assignmentStatus", "assignment_status", "status") ?? "assigned",
  )
    .toLowerCase()
    .replace(/\s+/g, "_");
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
  const queryClient = useQueryClient();
  const hasDirect = useHasDirectPermission();
  const has = useHasPermission();
  const canAssign =
    hasDirect("dispatch:assign") || hasDirect("dispatch:manage");
  const canUpdate =
    hasDirect("dispatch:update") || hasDirect("dispatch:manage");
  const canCancel =
    hasDirect("dispatch:cancel") || hasDirect("dispatch:manage");
  const [search, setSearch] = useState("");
  const [stage, setStage] = useState("Active");
  const [page, setPage] = useState(0);
  const [editor, setEditor] = useState<{
    row: AnyRecord;
    mode: "pairing" | "status";
    trigger: HTMLElement | null;
  } | null>(null);
  const [driverId, setDriverId] = useState("");
  const [vehicleId, setVehicleId] = useState("");
  const [targetStatus, setTargetStatus] = useState("");
  const [notice, setNotice] = useState("");
  const candidateDriversQ = useQuery({
    queryKey: ["assignments", "driver-options"],
    queryFn: driversApi.list,
    enabled: editor?.mode === "pairing",
  });
  const candidateVehiclesQ = useQuery({
    queryKey: ["assignments", "vehicle-options"],
    queryFn: vehiclesApi.list,
    enabled: editor?.mode === "pairing",
  });
  const save = useMutation({
    mutationFn: async () => {
      if (!editor) throw new Error("Select an assignment first.");
      if (editor.mode === "pairing") {
        if (!driverId || !vehicleId)
          throw new Error("Choose both a driver and vehicle.");
        if (
          driverId === String(g(editor.row, "driverId", "driver_id")) &&
          vehicleId === String(g(editor.row, "vehicleId", "vehicle_id"))
        )
          throw new Error("Choose a different pairing before saving.");
        await jobsApi.assign(String(g(editor.row, "jobId", "job_id")), {
          driverId: Number(driverId),
          vehicleId: Number(vehicleId),
        });
      } else if (targetStatus === "cancelled") {
        await dispatchApi.cancelAssignment(String(editor.row.id));
      } else {
        if (!targetStatus) throw new Error("Choose the next status.");
        await dispatchApi.updateStatus(String(editor.row.id), targetStatus);
      }
    },
    onSuccess: async () => {
      setNotice(
        editor?.mode === "pairing"
          ? "Pairing saved. The previous assignment is retained in history."
          : "Assignment status saved.",
      );
      setSelectedId(null);
      setEditor(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["dispatch"] }),
        queryClient.invalidateQueries({ queryKey: ["fleet"] }),
        queryClient.invalidateQueries({ queryKey: ["jobs"] }),
      ]);
    },
  });
  const openEditor = (
    row: AnyRecord,
    mode: "pairing" | "status",
    trigger: HTMLElement,
  ) => {
    save.reset();
    setNotice("");
    setDriverId(String(g(row, "driverId", "driver_id") ?? ""));
    setVehicleId(String(g(row, "vehicleId", "vehicle_id") ?? ""));
    setTargetStatus("");
    setEditor({ row, mode, trigger });
  };

  const assignmentsQ = useQuery({
    queryKey: ["dispatch", "assignments"],
    queryFn: () => dispatchApi.assignments({ limit: 100 }),
    refetchInterval: 30_000,
  });
  const fleetHistoryQ = useQuery({
    queryKey: ["fleet", "vehicle-assignments"],
    queryFn: () =>
      unwrap<AnyRecord[]>(apiClient.get("/api/vehicle-assignments")),
    refetchInterval: 30_000,
    enabled: has("vehicles:view"),
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
  const fleetHistory = (fleetHistoryQ.data ?? []) as AnyRecord[];
  const recommendations = (recommendationsQ.data ?? []) as AnyRecord[];
  const exceptions = (exceptionsQ.data ?? []) as AnyRecord[];
  const availableDrivers = (availableDriversQ.data ?? []) as AnyRecord[];
  const availableVehicles = (availableVehiclesQ.data ?? []) as AnyRecord[];
  const owners = (ownersQ.data ?? []) as AnyRecord[];

  const selected = useMemo(
    () => assignments.find((row) => String(row.id) === selectedId) ?? null,
    [assignments, selectedId],
  );
  const detail = (detailQ.data ?? {}) as AnyRecord;
  const filtered = assignments.filter(
    (row) =>
      (stage === "All" ||
        (stage === "Active"
          ? !isTerminalAssignment(assignmentStatus(row))
          : assignmentStage(assignmentStatus(row)) === stage)) &&
      (!search.trim() ||
        [
          "jobNumber",
          "job_number",
          "customerName",
          "customer_name",
          "driverName",
          "driver_name",
          "vehicleCode",
          "vehicle_code",
          "trackingCode",
          "tracking_code",
        ].some((key) =>
          String(row[key] ?? "")
            .toLowerCase()
            .includes(search.trim().toLowerCase()),
        )),
  );
  const currentPage = Math.min(
    page,
    Math.max(0, Math.ceil(filtered.length / 25) - 1),
  );

  if (assignmentsQ.isLoading) return <LoadingState />;
  if (assignmentsQ.isError) {
    return (
      <ErrorState
        message={
          assignmentsQ.error instanceof Error
            ? assignmentsQ.error.message
            : "Unable to load assignments."
        }
      />
    );
  }

  const supportingError = [
    fleetHistoryQ,
    recommendationsQ,
    exceptionsQ,
    availableDriversQ,
    availableVehiclesQ,
    ownersQ,
  ].find((query) => query.isError)?.error;

  const activeAssignments = assignments.filter(
    (row) => !isTerminalAssignment(assignmentStatus(row)),
  ).length;
  const activeFleetPairings = currentPairings(fleetHistory).length;
  const inTransit = assignments.filter(
    (row) => assignmentStage(assignmentStatus(row)) === "In transit",
  ).length;
  const exceptionCount = exceptions.filter(
    (row) => String(g(row, "status") ?? "open").toLowerCase() !== "resolved",
  ).length;
  const avgMatch = assignments.length
    ? Math.round(
        assignments.reduce(
          (sum, row) => sum + num(g(row, "matchScore", "match_score")),
          0,
        ) / assignments.length,
      )
    : 0;
  const readyDrivers = availableDrivers.filter(
    (row) =>
      !num(g(row, "safetyBlocked", "safety_blocked")) &&
      !num(g(row, "statusBlocked", "status_blocked")),
  ).length;
  const readyVehicles = availableVehicles.filter(
    (row) =>
      !num(g(row, "criticalDefectCount", "critical_defect_count")) &&
      !num(g(row, "blockingWoCount", "blocking_wo_count")),
  ).length;
  const exportRows =
    section === "exceptions"
      ? exceptions
      : section === "owners"
        ? owners
        : section === "board"
          ? filtered
          : fleetHistory;

  return (
    <div className="fleet-console assignments-page flex flex-col gap-2 pb-3">
      {supportingError ? (
        <div
          role="alert"
          className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900"
        >
          Some assignment supporting data could not be loaded:{" "}
          {supportingError instanceof Error
            ? supportingError.message
            : "Please retry."}
        </div>
      ) : null}
      <header className="assignment-header fc-rail">
        <div>
          <h1>Assignments</h1>
          <p>
            {activeFleetPairings} current fleet pairings · {activeAssignments}{" "}
            active jobs · {inTransit} in motion
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            className="btn-ghost btn-compact"
            onClick={() => exportCsv("assignments", exportRows)}
          >
            Export view
          </button>
          <button
            type="button"
            className="btn-ghost btn-compact"
            onClick={() =>
              void Promise.all([
                queryClient.invalidateQueries({ queryKey: ["dispatch"] }),
                queryClient.invalidateQueries({ queryKey: ["fleet"] }),
              ])
            }
          >
            Refresh
          </button>
          <button
            type="button"
            className="btn-primary btn-compact"
            onClick={() => navigate("/dispatch")}
          >
            Open dispatch
          </button>
        </div>
      </header>
      <nav className="assignment-tabs" aria-label="Assignment sections">
        {SECTIONS.map((item) => (
          <button
            type="button"
            key={item.key}
            aria-current={section === item.key ? "page" : undefined}
            onClick={() => {
              setSelectedId(null);
              navigate(`/assignments/${item.key}`);
            }}
          >
            {item.label}
          </button>
        ))}
      </nav>
      <div className="assignment-metrics">
        <span>
          <strong>{activeFleetPairings}</strong> Current fleet pairings
        </span>
        <span>
          <strong>{exceptionCount}</strong> Open exceptions
        </span>
        <span>
          <strong>{avgMatch}%</strong> Average match
        </span>
        <span>
          <strong>{owners.length}</strong> Partner operators
        </span>
      </div>
      {notice && (
        <p
          role="status"
          className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-800"
        >
          {notice}
        </p>
      )}
      {section === "overview" && (
        <div className="space-y-6">
          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Fleet assignment history
                </h2>
                <p className="text-sm text-slate-500">
                  Effective-dated driver and vehicle pairings from the fleet
                  master. Reassignment closes the previous row and preserves it
                  here.
                </p>
              </div>
              <button
                type="button"
                className="btn-ghost h-9"
                onClick={() =>
                  exportCsv("fleet-assignment-history", fleetHistory)
                }
              >
                Export history
              </button>
            </div>
            {fleetHistoryQ.isLoading ? (
              <div className="mt-4">
                <LoadingState />
              </div>
            ) : fleetHistory.length ? (
              <div className="mt-4 overflow-x-auto rounded-xl border border-slate-200">
                <table className="min-w-full text-left text-sm">
                  <thead className="bg-slate-50 text-[11px] uppercase tracking-[0.12em] text-slate-500">
                    <tr>
                      <th className="px-4 py-3">Vehicle</th>
                      <th className="px-4 py-3">Driver</th>
                      <th className="px-4 py-3">Type</th>
                      <th className="px-4 py-3">Status</th>
                      <th className="px-4 py-3">Effective from</th>
                      <th className="px-4 py-3">Effective to</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {fleetHistory.slice(0, 100).map((row) => (
                      <tr key={String(row.id)}>
                        <td className="px-4 py-3 font-semibold text-slate-900">
                          {String(g(row, "vehicleCode", "vehicle_code") ?? "—")}
                        </td>
                        <td className="px-4 py-3 text-slate-700">
                          {String(g(row, "driverCode", "driver_code") ?? "—")} ·{" "}
                          {String(g(row, "driverName", "driver_name") ?? "—")}
                        </td>
                        <td className="px-4 py-3 text-slate-600">
                          {String(
                            g(row, "assignmentType", "assignment_type") ??
                              "Primary Driver",
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <StatusBadge status={g(row, "status") ?? "Unknown"} />
                        </td>
                        <td className="px-4 py-3 text-slate-600">
                          {g(row, "assignmentDate", "assignment_date")
                            ? new Date(
                                String(
                                  g(row, "assignmentDate", "assignment_date"),
                                ),
                              ).toLocaleString()
                            : "—"}
                        </td>
                        <td className="px-4 py-3 text-slate-600">
                          {g(row, "releaseDate", "release_date")
                            ? new Date(
                                String(g(row, "releaseDate", "release_date")),
                              ).toLocaleString()
                            : g(row, "isCurrent", "is_current") === true
                              ? "Current"
                              : "Not recorded"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {fleetHistory.length > 100 ? (
                  <p className="border-t border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-500">
                    Showing the latest 100 of{" "}
                    {fleetHistory.length.toLocaleString()} history rows. Export
                    includes the complete result.
                  </p>
                ) : null}
              </div>
            ) : (
              <div className="mt-4">
                <EmptyState
                  title="No fleet pairings"
                  subtitle="Assign a driver from the vehicle roster to create the first governed history row."
                />
              </div>
            )}
          </section>
          <div className="grid gap-4 lg:grid-cols-3">
            <ModuleCard
              title="Assignment list"
              body="See the pairing lifecycle in a searchable list, with one-click access to proof, exceptions and audit."
              action="Open list"
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
              body="Reserve capacity from partner owner-operators."
              action="Open owner ops"
              onClick={() => navigate("/assignments/owners")}
              icon={<Users className="h-5 w-5" />}
            />
          </div>

          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Coverage posture
                </h2>
                <p className="text-sm text-slate-500">
                  Coverage, match quality and exception pressure across current
                  pairings.
                </p>
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
                body={
                  exceptionCount
                    ? "Assignment issues are active and should stay visible in the dispatch heartbeat."
                    : "No open dispatch exceptions."
                }
              />
              <InsightTile
                icon={<Sparkles className="h-4 w-4" />}
                label="Top recommendation"
                value={
                  recommendations[0]
                    ? `${Math.round(num(g(recommendations[0], "score")))}% fit`
                    : "No recommendation"
                }
                body={
                  recommendations[0]
                    ? String(
                        g(
                          recommendations[0],
                          "customerName",
                          "customer_name",
                          "title",
                        ) ?? "Recommendation ready for dispatcher review.",
                      )
                    : "No dispatch recommendations returned right now."
                }
              />
            </div>
          </section>

          <section className="panel p-5">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Suggested pairings
                </h2>
                <p className="text-sm text-slate-500">
                  Suggested driver-vehicle pairings ranked by match score.
                </p>
              </div>
              <button
                type="button"
                className="btn-ghost h-9"
                onClick={() => navigate("/assignments/board")}
              >
                Open list
              </button>
            </div>
            <div className="mt-4 grid gap-3 xl:grid-cols-3">
              {recommendations.slice(0, 3).map((row, index) => (
                <div
                  key={String(row.id ?? index)}
                  className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4"
                >
                  <div className="flex items-center justify-between">
                    <StatusBadge
                      status={
                        g(row, "priority", "slaStatus", "sla_status") ??
                        "Suggested"
                      }
                    />
                    <span className="text-sm font-semibold text-teal-700">
                      {Math.round(num(g(row, "score")))}% fit
                    </span>
                  </div>
                  <h3 className="mt-3 text-sm font-semibold text-slate-900">
                    {String(
                      g(
                        row,
                        "jobNumber",
                        "job_number",
                        "jobCode",
                        "job_code",
                      ) ?? "Open load",
                    )}
                  </h3>
                  <p className="mt-2 text-sm text-slate-600">
                    {String(
                      g(row, "customerName", "customer_name") ?? "Customer",
                    )}{" "}
                    ·{" "}
                    {String(
                      g(row, "driverName", "driver_name") ?? "Driver TBD",
                    )}{" "}
                    ·{" "}
                    {String(
                      g(row, "vehicleCode", "vehicle_code") ?? "Vehicle TBD",
                    )}
                  </p>
                  <p
                    className={`mt-3 text-xs font-semibold uppercase tracking-[0.14em] ${priorityClass(String(g(row, "priority") ?? ""))}`}
                  >
                    {String(g(row, "priority") ?? "Standard")}
                  </p>
                </div>
              ))}
              {!recommendations.length && (
                <EmptyState
                  title="No live recommendations"
                  subtitle="No pairing recommendations right now — they appear as soon as dispatch generates matches."
                />
              )}
            </div>
          </section>

          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Entity links
                </h2>
                <p className="text-sm text-slate-500">
                  Assignments should stay connected to the records that let
                  operators resolve issues immediately.
                </p>
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
                  className="group rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-violet-200 hover:shadow-md"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-semibold text-slate-900">
                      {item.label}
                    </span>
                    <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-violet-500" />
                  </div>
                  <p className="mt-2 text-sm text-slate-500">{item.note}</p>
                </button>
              ))}
            </div>
          </section>
        </div>
      )}

      {section === "board" && (
        <section className="panel assignment-list">
          <div className="assignment-toolbar">
            <label className="min-w-0 flex-1">
              <span className="sr-only">Search assignments</span>
              <input
                className="input w-full"
                placeholder="Search jobs, customers, drivers or vehicles…"
                value={search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  setPage(0);
                }}
              />
            </label>
            <label>
              <span className="sr-only">Filter assignment stage</span>
              <select
                className="input"
                value={stage}
                onChange={(event) => {
                  setStage(event.target.value);
                  setPage(0);
                }}
              >
                {[
                  "Active",
                  "All",
                  "Assigned",
                  "Accepted",
                  "In transit",
                  "Exception",
                  "Delivered",
                  "Cancelled",
                ].map((value) => (
                  <option key={value}>{value}</option>
                ))}
              </select>
            </label>
            <span className="text-xs text-slate-500">
              {filtered.length} matching · {assignments.length} loaded
            </span>
          </div>
          <div className="assignment-table-scroll">
            <table className="assignment-table">
              <thead>
                <tr>
                  <th>Job / Customer</th>
                  <th>Driver / Vehicle</th>
                  <th>Stage</th>
                  <th>Priority / SLA</th>
                  <th>Match</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filtered
                  .slice(currentPage * 25, (currentPage + 1) * 25)
                  .map((row) => {
                    const status = assignmentStatus(row);
                    const canReassign =
                      canAssign &&
                      !!g(row, "jobId", "job_id") &&
                      ["assigned", "accepted"].includes(status);
                    const options = nextAssignmentStatuses(row).filter(
                      (value) =>
                        value === "cancelled" ? canCancel : canUpdate,
                    );
                    return (
                      <tr key={String(row.id)}>
                        <td>
                          <button
                            type="button"
                            className="font-semibold text-teal-800 underline-offset-2 hover:underline"
                            onClick={() => setSelectedId(String(row.id))}
                          >
                            {String(
                              g(row, "jobNumber", "job_number") ??
                                `Assignment ${row.id}`,
                            )}
                          </button>
                          <small>
                            {String(
                              g(row, "customerName", "customer_name") ??
                                "No customer",
                            )}
                          </small>
                        </td>
                        <td>
                          <span className="font-medium">
                            {String(
                              g(row, "driverName", "driver_name") ??
                                "Unassigned",
                            )}
                          </span>
                          <small>
                            {String(
                              g(row, "vehicleCode", "vehicle_code") ??
                                "No vehicle",
                            )}
                          </small>
                        </td>
                        <td>
                          <StatusBadge status={status.replaceAll("_", " ")} />
                        </td>
                        <td>
                          <span
                            className={priorityClass(
                              String(row.priority ?? ""),
                            )}
                          >
                            {String(row.priority ?? "Standard")}
                          </span>
                          <small>
                            {String(g(row, "slaStatus", "sla_status") ?? "—")}
                          </small>
                        </td>
                        <td className="font-semibold text-teal-700">
                          {Math.round(num(g(row, "matchScore", "match_score")))}
                          %
                        </td>
                        <td>
                          <div className="flex flex-wrap gap-1">
                            <button
                              type="button"
                              className="btn-ghost btn-compact"
                              onClick={() => setSelectedId(String(row.id))}
                            >
                              Details
                            </button>
                            {canReassign && (
                              <button
                                type="button"
                                className="btn-ghost btn-compact"
                                onClick={(event) =>
                                  openEditor(
                                    row,
                                    "pairing",
                                    event.currentTarget,
                                  )
                                }
                              >
                                Change pairing
                              </button>
                            )}
                            {options.length > 0 && (
                              <button
                                type="button"
                                className="btn-primary btn-compact"
                                onClick={(event) =>
                                  openEditor(row, "status", event.currentTarget)
                                }
                              >
                                Update status
                              </button>
                            )}
                            {status === "arrived_delivery" && canUpdate && (
                              <button
                                type="button"
                                className="btn-ghost btn-compact"
                                onClick={() => navigate("/proof-of-delivery")}
                              >
                                Record delivery proof
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
              </tbody>
            </table>
            {!filtered.length && (
              <div className="p-6">
                <EmptyState
                  title="No matching assignments"
                  subtitle="Try another search or stage."
                />
              </div>
            )}
          </div>
          <footer className="assignment-pagination">
            <span>
              {filtered.length ? currentPage * 25 + 1 : 0}–
              {Math.min((currentPage + 1) * 25, filtered.length)} of{" "}
              {filtered.length}
              {assignments.length === 100
                ? " · Latest 100 assignments loaded"
                : ""}
            </span>
            <div className="flex items-center gap-2">
              <button
                type="button"
                className="btn-ghost btn-compact"
                disabled={currentPage === 0}
                onClick={() => setPage(currentPage - 1)}
              >
                Previous
              </button>
              <span>Page {currentPage + 1}</span>
              <button
                type="button"
                className="btn-ghost btn-compact"
                disabled={(currentPage + 1) * 25 >= filtered.length}
                onClick={() => setPage(currentPage + 1)}
              >
                Next
              </button>
            </div>
          </footer>
        </section>
      )}
      {section === "board" && selected && (
        <ConfirmDialog
          title="Assignment details"
          message={
            detailQ.isError ? (
              <ErrorState
                message={
                  detailQ.error instanceof Error
                    ? detailQ.error.message
                    : "Could not load assignment details."
                }
              />
            ) : (
              <AssignmentDetailPanel
                selected={selected}
                detail={detail}
                loading={detailQ.isLoading}
                onNavigate={(route) => {
                  setSelectedId(null);
                  navigate(route);
                }}
              />
            )
          }
          confirmLabel="Done"
          onConfirm={() => setSelectedId(null)}
          onCancel={() => setSelectedId(null)}
        />
      )}
      {editor && (
        <ConfirmDialog
          title={`${editor.mode === "pairing" ? "Change pairing" : "Update status"} · ${String(g(editor.row, "jobNumber", "job_number") ?? editor.row.id)}`}
          confirmLabel="Save changes"
          busy={save.isPending}
          error={
            save.error
              ? apiErrorMessage(
                  save.error,
                  "Could not save the assignment. Refresh and try again.",
                )
              : null
          }
          returnFocusTo={editor.trigger}
          onCancel={() => {
            if (!save.isPending) setEditor(null);
          }}
          onConfirm={() => save.mutate()}
          message={
            <div className="space-y-3">
              {editor.mode === "pairing" ? (
                <>
                  <p>
                    Choose a driver and vehicle. Readiness and branch checks run
                    before the change is saved.
                  </p>
                  {candidateDriversQ.isLoading ||
                  candidateVehiclesQ.isLoading ? (
                    <LoadingState />
                  ) : null}
                  {candidateDriversQ.isError || candidateVehiclesQ.isError ? (
                    <p role="alert" className="text-red-700">
                      Could not load pairing options. Close and try again.
                    </p>
                  ) : null}
                  <label className="block">
                    Driver
                    <select
                      className="input mt-1 w-full"
                      value={driverId}
                      disabled={save.isPending}
                      onChange={(event) => setDriverId(event.target.value)}
                    >
                      <option value="">Choose driver</option>
                      {(candidateDriversQ.data ?? []).map((row) => (
                        <option key={String(row.id)} value={String(row.id)}>
                          {String(g(row, "driverCode", "driver_code") ?? "")} ·{" "}
                          {String(
                            g(
                              row,
                              "fullName",
                              "full_name",
                              "driverName",
                              "driver_name",
                            ) ?? row.id,
                          )}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="block">
                    Vehicle
                    <select
                      className="input mt-1 w-full"
                      value={vehicleId}
                      disabled={save.isPending}
                      onChange={(event) => setVehicleId(event.target.value)}
                    >
                      <option value="">Choose vehicle</option>
                      {(candidateVehiclesQ.data ?? []).map((row) => (
                        <option key={String(row.id)} value={String(row.id)}>
                          {String(
                            g(row, "vehicleCode", "vehicle_code") ?? row.id,
                          )}
                        </option>
                      ))}
                    </select>
                  </label>
                </>
              ) : (
                <>
                  <p>
                    Current stage:{" "}
                    <strong>
                      {assignmentStatus(editor.row).replaceAll("_", " ")}
                    </strong>
                    . Select the next stage.
                  </p>
                  <label className="block">
                    Next stage
                    <select
                      className="input mt-1 w-full"
                      disabled={save.isPending}
                      value={targetStatus}
                      onChange={(event) => setTargetStatus(event.target.value)}
                    >
                      <option value="">Choose next stage</option>
                      {nextAssignmentStatuses(editor.row)
                        .filter((value) =>
                          value === "cancelled" ? canCancel : canUpdate,
                        )
                        .map((value) => (
                          <option key={value} value={value}>
                            {value.replaceAll("_", " ")}
                          </option>
                        ))}
                    </select>
                  </label>
                  <p className="text-xs">
                    Delivery completion requires recorded proof.
                  </p>
                </>
              )}
            </div>
          }
        />
      )}
      {section === "exceptions" && (
        <div className="grid gap-4 xl:grid-cols-[1.3fr_1fr]">
          <section className="panel p-5">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Live exception queue
                </h2>
                <p className="text-sm text-slate-500">
                  Open dispatch exceptions with the bench available to recover
                  them.
                </p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                {exceptionCount} open
              </span>
            </div>
            <div className="mt-4 space-y-3">
              {exceptions.length ? (
                exceptions.map((row) => (
                  <div
                    key={String(row.id)}
                    className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2">
                          <StatusBadge status={g(row, "severity") ?? "Open"} />
                          <p className="text-sm font-semibold text-slate-900">
                            {String(
                              g(row, "title") ??
                                g(row, "exceptionType", "exception_type") ??
                                "Dispatch exception",
                            )}
                          </p>
                        </div>
                        <p className="mt-2 text-sm text-slate-600">
                          {String(
                            g(row, "jobNumber", "job_number") ?? "No job code",
                          )}{" "}
                          ·{" "}
                          {String(
                            g(row, "driverName", "driver_name") ??
                              "Driver unknown",
                          )}{" "}
                          /{" "}
                          {String(
                            g(row, "vehicleCode", "vehicle_code") ??
                              "Vehicle unknown",
                          )}
                        </p>
                      </div>
                      <StatusBadge status={g(row, "status") ?? "Open"} />
                    </div>
                    <p className="mt-3 text-sm text-slate-500">
                      {String(
                        g(row, "notes") ?? "No exception notes recorded.",
                      )}
                    </p>
                  </div>
                ))
              ) : (
                <EmptyState
                  title="No dispatch exceptions"
                  subtitle="No exception records."
                />
              )}
            </div>
          </section>

          <section className="panel p-5">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Recovery bench
              </h2>
              <p className="text-sm text-slate-500">
                Operators need to see the immediately available resources that
                can absorb problem work.
              </p>
            </div>
            <div className="mt-4 grid gap-3">
              <BenchList
                title="Drivers"
                rows={availableDrivers}
                empty="No available driver pool returned."
                renderLabel={(row) =>
                  String(
                    g(
                      row,
                      "fullName",
                      "full_name",
                      "driverName",
                      "driver_name",
                    ) ?? `Driver ${row.id}`,
                  )
                }
                renderMeta={(row) =>
                  `${Math.round(num(g(row, "matchReadiness", "match_readiness")))}% readiness · ${num(g(row, "availableHosHours", "available_hos_hours"))}h HOS`
                }
              />
              <BenchList
                title="Vehicles"
                rows={availableVehicles}
                empty="No available vehicle pool returned."
                renderLabel={(row) =>
                  String(
                    g(row, "vehicleCode", "vehicle_code") ??
                      `Vehicle ${row.id}`,
                  )
                }
                renderMeta={(row) =>
                  `${Math.round(num(g(row, "matchReadiness", "match_readiness")))}% readiness · ${num(g(row, "blockingWoCount", "blocking_wo_count"))} blocking WO`
                }
              />
            </div>
          </section>
        </div>
      )}

      {section === "owners" && (
        <div className="space-y-4">
          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Owner-operator reserve network
                </h2>
                <p className="text-sm text-slate-500">
                  Owner-operator capacity, revenue share and contract expiry.
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <StatusBadge
                  status={`${owners.filter((row) => String(g(row, "status") ?? "").toLowerCase() === "active").length} active`}
                />
                <StatusBadge
                  status={`${owners.reduce((sum, row) => sum + num(g(row, "vehicleCount", "vehicle_count")), 0)} vehicles`}
                />
              </div>
            </div>
          </section>

          {ownersQ.isLoading ? (
            <LoadingState />
          ) : owners.length ? (
            <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
              <table className="w-full text-left text-sm">
                <thead>
                  <tr className="border-b border-slate-200 text-[11px] uppercase tracking-[0.12em] text-slate-400">
                    <th className="px-5 py-3 font-semibold">Owner</th>
                    <th className="px-5 py-3 font-semibold">Contact</th>
                    <th className="px-5 py-3 font-semibold">Vehicles</th>
                    <th className="px-5 py-3 font-semibold">Revenue share</th>
                    <th className="hidden px-5 py-3 font-semibold lg:table-cell">
                      Loads
                    </th>
                    <th className="hidden px-5 py-3 font-semibold xl:table-cell">
                      Contract expiry
                    </th>
                    <th className="px-5 py-3 font-semibold">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {owners.map((row) => (
                    <tr
                      key={String(row.id)}
                      className="transition hover:bg-slate-50"
                    >
                      <td className="px-5 py-3.5">
                        <div className="font-semibold text-slate-900">
                          {String(g(row, "ownerName", "owner_name") ?? "Owner")}
                        </div>
                        <div className="text-xs text-slate-500">
                          {String(g(row, "ownerCode", "owner_code") ?? "--")}
                        </div>
                      </td>
                      <td className="px-5 py-3.5">
                        <div className="text-slate-700">
                          {String(
                            g(row, "contactName", "contact_name") ??
                              "No contact",
                          )}
                        </div>
                        <div className="text-xs text-slate-500">
                          {String(g(row, "phone") ?? "No phone")}
                        </div>
                      </td>
                      <td className="px-5 py-3.5 text-slate-700">
                        {num(g(row, "vehicleCount", "vehicle_count"))}
                      </td>
                      <td className="px-5 py-3.5 text-slate-700">
                        {num(g(row, "revenueSharePct", "revenue_share_pct"))}%
                      </td>
                      <td className="hidden px-5 py-3.5 text-slate-700 lg:table-cell">
                        {num(g(row, "totalLoads", "total_loads"))}
                      </td>
                      <td className="hidden px-5 py-3.5 text-slate-700 xl:table-cell">
                        {String(
                          g(row, "contractExpiry", "contract_expiry") ?? "—",
                        )}
                      </td>
                      <td className="px-5 py-3.5">
                        <StatusBadge status={g(row, "status") ?? "Active"} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <EmptyState
              title="No owner-operator records connected"
              subtitle="No owner-operator records yet — add partners to track their capacity here."
            />
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
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-slate-50 text-slate-500">
          {icon}
        </div>
        <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5" />
      </div>
      <h3 className="mt-4 text-base font-semibold text-slate-900">{title}</h3>
      <p className="mt-2 text-sm text-slate-500">{body}</p>
      <p className="mt-4 text-xs font-bold uppercase tracking-[0.14em] text-violet-600">
        {action}
      </p>
    </button>
  );
}

function InsightTile({
  icon,
  label,
  value,
  body,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  body: string;
}) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
      <div className="flex items-center justify-between">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-white text-slate-500 shadow-sm">
          {icon}
        </div>
        <span className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-400">
          {label}
        </span>
      </div>
      <p className="mt-4 text-2xl font-bold tracking-tight text-slate-900">
        {value}
      </p>
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
        <span className="rounded-full bg-white px-2 py-1 text-xs text-slate-600 shadow-sm">
          {rows.length}
        </span>
      </div>
      <div className="mt-3 space-y-2">
        {rows.slice(0, 4).map((row) => (
          <div
            key={String(row.id)}
            className="rounded-xl border border-slate-200 bg-white px-3 py-2"
          >
            <p className="text-sm font-semibold text-slate-900">
              {renderLabel(row)}
            </p>
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
        <EmptyState
          title="No assignment selected"
          subtitle="Pick an assignment row to inspect proofs, exceptions and audit trail."
        />
      </div>
    );
  }

  const assignment = (detail.assignment as AnyRecord) || selected;
  const proofs = (detail.proofs as AnyRecord[]) || [];
  const proofArtifacts = (detail.proofArtifacts as AnyRecord[]) || [];
  const exceptions = (detail.exceptions as AnyRecord[]) || [];
  const auditTrail = (detail.auditTrail as AnyRecord[]) || [];

  return (
    <aside className="panel p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">
            Selected assignment
          </p>
          <h3 className="mt-1 text-lg font-semibold text-slate-900">
            {String(
              g(assignment, "jobNumber", "job_number") ??
                `Assignment ${assignment.id}`,
            )}
          </h3>
          <p className="text-sm text-slate-500">
            {String(
              g(assignment, "customerName", "customer_name") ?? "Customer",
            )}{" "}
            ·{" "}
            {String(
              g(assignment, "trackingCode", "tracking_code") ??
                "Tracking pending",
            )}
          </p>
        </div>
        <StatusBadge
          status={
            g(assignment, "assignmentStatus", "assignment_status", "status") ??
            "Assigned"
          }
        />
      </div>

      <div className="mt-4 grid grid-cols-2 gap-3">
        <MetricMini
          label="Driver"
          value={String(g(assignment, "driverName", "driver_name") ?? "TBD")}
        />
        <MetricMini
          label="Vehicle"
          value={String(g(assignment, "vehicleCode", "vehicle_code") ?? "TBD")}
        />
        <MetricMini
          label="Match score"
          value={`${Math.round(num(g(assignment, "matchScore", "match_score")))}%`}
        />
        <MetricMini
          label="Trip compliance"
          value={`${Math.round(num(g(assignment, "tripCompliance", "trip_compliance")))}%`}
        />
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-4">
        <p className="text-sm font-semibold text-slate-900">
          Operational context
        </p>
        <p className="mt-2 text-sm text-slate-600">
          {String(
            g(assignment, "pickupAddress", "pickup_address") ??
              "Pickup pending",
          )}{" "}
          to{" "}
          {String(
            g(assignment, "dropoffAddress", "dropoff_address") ??
              "Dropoff pending",
          )}
        </p>
        <p className="mt-2 text-sm text-slate-500">
          {String(
            g(assignment, "driverPhone", "driver_phone") ??
              "Driver phone unavailable",
          )}
        </p>
      </div>

      <div className="mt-4 grid gap-3">
        <EvidenceSummary
          title="Proofs"
          rows={proofs}
          loading={loading}
          empty="No proof records returned yet."
        />
        <EvidenceSummary
          title="Proof artifacts"
          rows={proofArtifacts}
          loading={loading}
          empty="No tenant-scoped proof artifacts returned for this assignment."
        />
        <EvidenceSummary
          title="Exceptions"
          rows={exceptions}
          loading={loading}
          empty="No exception records on this assignment."
        />
        <EvidenceSummary
          title="Audit"
          rows={auditTrail}
          loading={loading}
          empty="No audit entries returned."
        />
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          className="btn-ghost h-9"
          onClick={() => onNavigate("/dispatch")}
        >
          Open dispatch
        </button>
        <button
          type="button"
          className="btn-ghost h-9"
          onClick={() => onNavigate("/proof-of-delivery")}
        >
          Open proof
        </button>
        <button
          type="button"
          className="btn-ghost h-9"
          onClick={() => onNavigate("/vehicles/roster")}
        >
          Open vehicle
        </button>
      </div>
    </aside>
  );
}

function MetricMini({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2">
      <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">
        {label}
      </p>
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
        <span className="rounded-full bg-white px-2 py-1 text-xs text-slate-600 shadow-sm">
          {rows.length}
        </span>
      </div>
      <div className="mt-3 space-y-2">
        {loading && !rows.length ? (
          <p className="text-sm text-slate-400">Loading…</p>
        ) : null}
        {!loading && !rows.length ? (
          <p className="text-sm text-slate-400">{empty}</p>
        ) : null}
        {rows.slice(0, 3).map((row, index) => (
          <div
            key={String(row.id ?? index)}
            className="rounded-xl border border-slate-200 bg-white px-3 py-2"
          >
            <p className="text-sm font-semibold text-slate-900">
              {String(
                g(
                  row,
                  "title",
                  "proofType",
                  "proof_type",
                  "kind",
                  "actionName",
                  "action_name",
                  "exceptionType",
                  "exception_type",
                ) ?? `${title} item`,
              )}
            </p>
            <p className="mt-1 break-all text-xs text-slate-500">
              {String(
                g(
                  row,
                  "notes",
                  "reference",
                  "createdAt",
                  "created_at",
                  "confirmedAt",
                  "confirmed_at",
                  "severity",
                ) ?? "No extra detail",
              )}
            </p>
          </div>
        ))}
      </div>
    </div>
  );
}
