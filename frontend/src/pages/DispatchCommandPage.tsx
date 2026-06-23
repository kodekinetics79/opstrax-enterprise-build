import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, CheckCircle, ChevronRight, Clock,
  MapPin, ShieldAlert, Truck, User, XCircle, Zap,
} from "lucide-react";
import { DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv } from "@/components/ui";
import { dispatchApi } from "@/services/dispatchApi";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

// P4 Dispatch Execution Workflow — no static data, no fake buttons.
// All board state, eligibility, and actions come from real backend.

const TABS = ["Board", "Assignments", "Eligibility", "Exceptions", "Available Drivers", "Available Vehicles"] as const;
type Tab = (typeof TABS)[number];

const STATUS_ORDER = [
  "Unassigned", "Assigned", "Accepted",
  "En Route Pickup", "At Pickup", "In Transit", "At Delivery",
  "Exception",
];

const NEXT_STATUS: Record<string, string[]> = {
  assigned:         ["accepted", "cancelled"],
  accepted:         ["en_route_pickup", "cancelled"],
  en_route_pickup:  ["arrived_pickup", "exception"],
  arrived_pickup:   ["loaded", "exception"],
  loaded:           ["in_transit", "exception"],
  in_transit:       ["arrived_delivery", "exception"],
  arrived_delivery: ["delivered", "exception"],
  exception:        ["in_transit", "cancelled"],
};

export function DispatchCommandPage() {
  const [activeTab, setActiveTab] = useState<Tab>("Board");
  const [selectedAssignment, setSelectedAssignment] = useState<AnyRecord | null>(null);
  const [eligVehicleId, setEligVehicleId] = useState("");
  const [eligDriverId,  setEligDriverId]  = useState("");
  const [exceptionAssignId, setExceptionAssignId] = useState<number | null>(null);
  const [exceptionType, setExceptionType] = useState("late_delivery");
  const [exceptionNotes, setExceptionNotes] = useState("");
  const qc = useQueryClient();

  const hasPermission  = useHasPermission();
  const canAssign      = hasPermission("dispatch:assign");
  const canUpdate      = hasPermission("dispatch:update");
  const canCancel      = hasPermission("dispatch:cancel");
  const canOverride    = hasPermission("dispatch:override");

  const board = useQuery({
    queryKey: ["dispatch", "board"],
    queryFn: dispatchApi.board,
    refetchInterval: 30_000,
  });
  const assignments = useQuery<AnyRecord[]>({
    queryKey: ["dispatch", "assignments"],
    queryFn: () => dispatchApi.assignments({ limit: 100 }),
    staleTime: 15_000,
  });
  const exceptions = useQuery<AnyRecord[]>({
    queryKey: ["dispatch", "exceptions"],
    queryFn: () => dispatchApi.exceptions(),
    staleTime: 15_000,
  });
  const availDrivers = useQuery<AnyRecord[]>({
    queryKey: ["dispatch", "available-drivers"],
    queryFn: dispatchApi.availableDrivers,
    staleTime: 30_000,
  });
  const availVehicles = useQuery<AnyRecord[]>({
    queryKey: ["dispatch", "available-vehicles"],
    queryFn: dispatchApi.availableVehicles,
    staleTime: 30_000,
  });
  const eligQuery = useQuery({
    queryKey: ["dispatch", "eligibility", eligVehicleId, eligDriverId],
    queryFn: () => dispatchApi.eligibility(Number(eligVehicleId), Number(eligDriverId)),
    enabled: Number(eligVehicleId) > 0 && Number(eligDriverId) > 0,
    staleTime: 10_000,
  });

  const invalidateAll = () => {
    qc.invalidateQueries({ queryKey: ["dispatch"] });
  };

  const statusMutation = useMutation({
    mutationFn: ({ id, status }: { id: number; status: string }) =>
      dispatchApi.updateStatus(id, status),
    onSuccess: invalidateAll,
  });
  const cancelMutation = useMutation({
    mutationFn: (id: number) => dispatchApi.cancelAssignment(id),
    onSuccess: () => { invalidateAll(); setSelectedAssignment(null); },
  });
  const proofMutation = useMutation({
    mutationFn: ({ id, type }: { id: number; type: "pickup" | "delivery" }) =>
      dispatchApi.recordProof(id, { proofType: type }),
    onSuccess: invalidateAll,
  });
  const exceptionMutation = useMutation({
    mutationFn: ({ id, type, notes }: { id: number; type: string; notes: string }) =>
      dispatchApi.createException(id, { exceptionType: type, notes }),
    onSuccess: () => { invalidateAll(); setExceptionAssignId(null); setExceptionNotes(""); },
  });

  if (board.isLoading) return <LoadingState />;
  if (board.isError)
    return (
      <div className="p-8 text-red-600">
        Failed to load dispatch board. Check backend connectivity.
      </div>
    );

  const stageMap: Record<string, AnyRecord[]> = board.data?.stageMap ?? {};
  const insights: AnyRecord[] = board.data?.insights ?? [];
  const summary = {
    unassigned: (stageMap["Unassigned"] ?? []).length,
    active: Object.entries(stageMap)
      .filter(([k]) => k !== "Unassigned" && k !== "Exception")
      .reduce((acc, [, v]) => acc + v.length, 0),
    exceptions: (stageMap["Exception"] ?? []).length,
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Dispatch Operations"
        title="Dispatch Command Center"
        description="Backend-authoritative assignment, eligibility gates, status execution, and exception management."
        actions={
          <button
            type="button"
            className="btn-ghost"
            onClick={() => exportCsv("dispatch-assignments", assignments.data ?? [])}
          >
            Export Assignments
          </button>
        }
      />

      {/* KPI Strip */}
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard
          label="Unassigned Loads"
          value={String(summary.unassigned)}
          icon={<Truck />}
          status={summary.unassigned > 0 ? "Warning" : "Active"}
        />
        <KpiCard
          label="Active Assignments"
          value={String(summary.active)}
          icon={<MapPin />}
          status="Active"
        />
        <KpiCard
          label="Open Exceptions"
          value={String(summary.exceptions)}
          icon={<AlertTriangle />}
          status={summary.exceptions > 0 ? "Critical" : "Active"}
        />
        <KpiCard
          label="Available Drivers"
          value={String(availDrivers.data?.length ?? "--")}
          icon={<User />}
          status="Review"
        />
      </div>

      {/* System Dispatch Insights */}
      {insights.length > 0 && (
        <section className="panel p-5">
          <h2 className="section-title">System Dispatch Insights</h2>
          <div className="mt-4 space-y-3">
            {insights.map((ins, i) => (
              <DispatchInsightRow key={i} insight={ins} />
            ))}
          </div>
        </section>
      )}

      {/* Tabs */}
      <section className="panel p-5">
        <div className="flex flex-wrap gap-2 border-b border-slate-200 pb-4">
          {TABS.map((tab) => (
            <button
              key={tab}
              type="button"
              className={tab === activeTab ? "control-tab control-tab-active" : "control-tab"}
              onClick={() => setActiveTab(tab)}
            >
              {tab}
            </button>
          ))}
        </div>

        <div className="mt-5">
          {activeTab === "Board" && (
            <BoardTab
              stageMap={stageMap}
              onSelect={setSelectedAssignment}
              onException={(id) => { setExceptionAssignId(id); setActiveTab("Board"); }}
              canUpdate={canUpdate}
              canCancel={canCancel}
              onStatusChange={(id, status) => statusMutation.mutate({ id, status })}
              onProof={(id, type) => proofMutation.mutate({ id, type })}
            />
          )}
          {activeTab === "Assignments" && (
            <AssignmentsTab
              rows={assignments.data ?? []}
              isLoading={assignments.isLoading}
              canUpdate={canUpdate}
              canCancel={canCancel}
              onStatusChange={(id, status) => statusMutation.mutate({ id, status })}
              onCancel={(id) => cancelMutation.mutate(id)}
              onSelect={setSelectedAssignment}
            />
          )}
          {activeTab === "Eligibility" && (
            <EligibilityTab
              vehicleId={eligVehicleId}
              driverId={eligDriverId}
              onVehicleChange={setEligVehicleId}
              onDriverChange={setEligDriverId}
              result={eligQuery.data}
              isLoading={eligQuery.isLoading}
              canAssign={canAssign}
              canOverride={canOverride}
            />
          )}
          {activeTab === "Exceptions" && (
            <ExceptionsTab
              rows={exceptions.data ?? []}
              isLoading={exceptions.isLoading}
            />
          )}
          {activeTab === "Available Drivers" && (
            <AvailableDriversTab rows={availDrivers.data ?? []} isLoading={availDrivers.isLoading} />
          )}
          {activeTab === "Available Vehicles" && (
            <AvailableVehiclesTab rows={availVehicles.data ?? []} isLoading={availVehicles.isLoading} />
          )}
        </div>
      </section>

      {/* Assignment Detail Drawer */}
      {selectedAssignment && (
        <AssignmentDrawer
          assignment={selectedAssignment}
          canUpdate={canUpdate}
          canCancel={canCancel}
          onClose={() => setSelectedAssignment(null)}
          onStatusChange={(id, status) => statusMutation.mutate({ id, status })}
          onCancel={(id) => cancelMutation.mutate(id)}
          onProof={(id, type) => proofMutation.mutate({ id, type })}
          onException={(id) => setExceptionAssignId(id)}
        />
      )}

      {/* Exception Create Modal */}
      {exceptionAssignId !== null && (
        <ExceptionModal
          assignmentId={exceptionAssignId}
          exceptionType={exceptionType}
          notes={exceptionNotes}
          isLoading={exceptionMutation.isPending}
          onTypeChange={setExceptionType}
          onNotesChange={setExceptionNotes}
          onSubmit={() =>
            exceptionMutation.mutate({
              id: exceptionAssignId,
              type: exceptionType,
              notes: exceptionNotes,
            })
          }
          onClose={() => setExceptionAssignId(null)}
        />
      )}
    </div>
  );
}

// ── Board Tab ─────────────────────────────────────────────────────────────────
function BoardTab({
  stageMap, onSelect, onException, canUpdate, canCancel, onStatusChange, onProof,
}: {
  stageMap: Record<string, AnyRecord[]>;
  onSelect: (a: AnyRecord) => void;
  onException: (id: number) => void;
  canUpdate: boolean;
  canCancel: boolean;
  onStatusChange: (id: number, status: string) => void;
  onProof: (id: number, type: "pickup" | "delivery") => void;
}) {
  return (
    <div className="overflow-x-auto">
      <div className="flex gap-4 min-w-[900px] pb-4">
        {STATUS_ORDER.map((stage) => {
          const rows = stageMap[stage] ?? [];
          return (
            <div key={stage} className="w-52 flex-shrink-0">
              <div className="mb-3 flex items-center justify-between">
                <span className="text-xs font-bold uppercase tracking-wide text-slate-500">
                  {stage}
                </span>
                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-semibold text-slate-600">
                  {rows.length}
                </span>
              </div>
              <div className="space-y-2">
                {rows.length === 0 && (
                  <div className="rounded-xl border border-dashed border-slate-200 py-8 text-center text-xs text-slate-400">
                    Empty
                  </div>
                )}
                {rows.map((row) => (
                  <BoardCard
                    key={String(row["id"] ?? row["jobNumber"])}
                    row={row}
                    stage={stage}
                    canUpdate={canUpdate}
                    canCancel={canCancel}
                    onSelect={() => onSelect(row)}
                    onException={() => onException(Number(row["id"]))}
                    onStatusChange={(s) => onStatusChange(Number(row["id"]), s)}
                    onProof={(t) => onProof(Number(row["id"]), t)}
                  />
                ))}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ── Board Card ────────────────────────────────────────────────────────────────
function BoardCard({
  row, stage, canUpdate, canCancel, onSelect, onException, onStatusChange, onProof,
}: {
  row: AnyRecord;
  stage: string;
  canUpdate: boolean;
  canCancel: boolean;
  onSelect: () => void;
  onException: () => void;
  onStatusChange: (s: string) => void;
  onProof: (t: "pickup" | "delivery") => void;
}) {
  const risk    = String(row["riskHeat"] ?? row["riskHeatScore"] ?? "Low");
  const status  = String(row["assignmentStatus"] ?? row["status"] ?? "").toLowerCase();
  const nextStatuses = NEXT_STATUS[status] ?? [];
  const isException = stage === "Exception";

  return (
    <div
      className={`rounded-xl border bg-white p-3 shadow-sm cursor-pointer transition-shadow hover:shadow-md
        ${isException ? "border-red-200" : "border-slate-200"}`}
      onClick={onSelect}
    >
      <div className="flex items-start justify-between gap-1">
        <span className="truncate text-xs font-bold text-slate-800">
          {String(row["jobNumber"] ?? "--")}
        </span>
        <RiskBadge risk={risk} />
      </div>
      <p className="mt-1 truncate text-xs text-slate-500">
        {String(row["customerName"] ?? "--")}
      </p>
      {row["driverName"] != null ? (
        <p className="mt-0.5 text-xs text-slate-400 truncate">
          <User className="inline h-3 w-3 mr-0.5" />
          {String(row["driverName"])}
        </p>
      ) : null}
      {row["vehicleCode"] != null ? (
        <p className="text-xs text-slate-400 truncate">
          <Truck className="inline h-3 w-3 mr-0.5" />
          {String(row["vehicleCode"])}
        </p>
      ) : null}
      {row["tripCompliance"] !== undefined && row["tripCompliance"] !== null && (
        <p className={`mt-1 text-xs font-semibold ${Number(row["tripCompliance"]) >= 80 ? "text-teal-600" : "text-amber-600"}`}>
          Route: {String(row["tripCompliance"])}%
        </p>
      )}
      {Number(row["openExceptions"] ?? 0) > 0 ? (
        <p className="mt-1 text-xs font-bold text-red-600">
          {String(row["openExceptions"])} open exception(s)
        </p>
      ) : null}
      {/* Status action buttons (stop propagation so card click doesn't fire) */}
      {canUpdate && nextStatuses.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1" onClick={(e) => e.stopPropagation()}>
          {nextStatuses
            .filter((s) => s !== "exception" && s !== "cancelled")
            .slice(0, 1)
            .map((s) => (
              <button
                key={s}
                type="button"
                className="rounded bg-teal-600 px-2 py-0.5 text-xs font-semibold text-white hover:bg-teal-700"
                onClick={() => onStatusChange(s)}
              >
                → {s.replace("_", " ")}
              </button>
            ))}
          <button
            type="button"
            className="rounded bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-800 hover:bg-amber-200"
            onClick={onException}
          >
            Exception
          </button>
        </div>
      )}
      {canUpdate && (stage === "At Pickup" || status === "arrived_pickup") && (
        <div className="mt-1" onClick={(e) => e.stopPropagation()}>
          <button
            type="button"
            className="rounded bg-blue-100 px-2 py-0.5 text-xs font-semibold text-blue-800 hover:bg-blue-200"
            onClick={() => onProof("pickup")}
          >
            Record Pickup
          </button>
        </div>
      )}
      {canUpdate && (stage === "At Delivery" || status === "arrived_delivery") && (
        <div className="mt-1" onClick={(e) => e.stopPropagation()}>
          <button
            type="button"
            className="rounded bg-teal-100 px-2 py-0.5 text-xs font-semibold text-teal-800 hover:bg-teal-200"
            onClick={() => onProof("delivery")}
          >
            Confirm Delivery
          </button>
        </div>
      )}
    </div>
  );
}

// ── Assignments Tab ───────────────────────────────────────────────────────────
function AssignmentsTab({
  rows, isLoading, canUpdate, canCancel, onStatusChange, onCancel, onSelect,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canUpdate: boolean;
  canCancel: boolean;
  onStatusChange: (id: number, status: string) => void;
  onCancel: (id: number) => void;
  onSelect: (a: AnyRecord) => void;
}) {
  if (isLoading) return <LoadingState />;
  if (!rows.length)
    return (
      <Empty
        icon={<Truck className="h-8 w-8 text-slate-300" />}
        message="No assignments found"
      />
    );

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th className="px-3 py-2">Job #</th>
            <th className="px-3 py-2">Driver</th>
            <th className="px-3 py-2">Vehicle</th>
            <th className="px-3 py-2">Status</th>
            <th className="px-3 py-2">Safety</th>
            <th className="px-3 py-2">Route Compl.</th>
            <th className="px-3 py-2">Exceptions</th>
            <th className="px-3 py-2">Assigned</th>
            <th className="px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {rows.map((r) => {
            const status     = String(r["assignmentStatus"] ?? "").toLowerCase();
            const nextOpts   = (NEXT_STATUS[status] ?? []).filter((s) => s !== "cancelled");
            const showCancel = canCancel && !["delivered", "cancelled"].includes(status);

            return (
              <tr
                key={String(r["id"])}
                className="cursor-pointer hover:bg-slate-50"
                onClick={() => onSelect(r)}
              >
                <td className="px-3 py-2 font-mono text-xs font-semibold">{String(r["jobNumber"] ?? "--")}</td>
                <td className="px-3 py-2">{String(r["driverName"] ?? "--")}</td>
                <td className="px-3 py-2">{String(r["vehicleCode"] ?? "--")}</td>
                <td className="px-3 py-2"><StatusBadge status={r["assignmentStatus"]} /></td>
                <td className="px-3 py-2">
                  <SafetyScorePill score={r["driverSafetyScore"]} />
                </td>
                <td className="px-3 py-2 text-xs">
                  {r["tripCompliance"] !== null && r["tripCompliance"] !== undefined
                    ? `${r["tripCompliance"]}%`
                    : "--"}
                </td>
                <td className="px-3 py-2">
                  {Number(r["openExceptions"] ?? 0) > 0
                    ? <span className="font-bold text-red-600">{String(r["openExceptions"])}</span>
                    : <span className="text-slate-400">0</span>}
                </td>
                <td className="px-3 py-2 text-xs text-slate-500">{fmtDate(r["assignedAt"] ?? r["createdAt"])}</td>
                <td className="px-3 py-2" onClick={(e) => e.stopPropagation()}>
                  <div className="flex gap-1">
                    {canUpdate && nextOpts.length > 0 && (
                      <button
                        type="button"
                        className="btn-ghost text-xs py-0.5 px-2"
                        onClick={() => onStatusChange(Number(r["id"]), nextOpts[0])}
                      >
                        <ChevronRight className="h-3 w-3" />
                        {nextOpts[0].replace("_", " ")}
                      </button>
                    )}
                    {showCancel && (
                      <button
                        type="button"
                        className="btn-ghost text-xs py-0.5 px-2 text-red-600"
                        onClick={() => onCancel(Number(r["id"]))}
                      >
                        Cancel
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

// ── Eligibility Tab ───────────────────────────────────────────────────────────
function EligibilityTab({
  vehicleId, driverId, onVehicleChange, onDriverChange, result, isLoading, canAssign, canOverride,
}: {
  vehicleId: string;
  driverId: string;
  onVehicleChange: (v: string) => void;
  onDriverChange:  (v: string) => void;
  result: AnyRecord | undefined;
  isLoading: boolean;
  canAssign: boolean;
  canOverride: boolean;
}) {
  return (
    <div className="space-y-6">
      <section>
        <h3 className="section-title mb-3">Eligibility Check</h3>
        <p className="text-sm text-slate-500 mb-4">
          Enter a Vehicle ID and Driver ID to run the eligibility engine before creating an assignment.
          The engine checks out-of-service status, open defects, work orders, safety score, and HOS data.
        </p>
        <div className="flex gap-3 flex-wrap items-end">
          <label className="block">
            <span className="text-xs font-semibold text-slate-600 uppercase tracking-wide">Vehicle ID</span>
            <input
              type="number"
              className="mt-1 block w-32 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none"
              value={vehicleId}
              onChange={(e) => onVehicleChange(e.target.value)}
              placeholder="e.g. 42"
            />
          </label>
          <label className="block">
            <span className="text-xs font-semibold text-slate-600 uppercase tracking-wide">Driver ID</span>
            <input
              type="number"
              className="mt-1 block w-32 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none"
              value={driverId}
              onChange={(e) => onDriverChange(e.target.value)}
              placeholder="e.g. 17"
            />
          </label>
        </div>
      </section>

      {isLoading && <LoadingState />}

      {result && !isLoading && (
        <section className={`rounded-xl border p-5 ${result["eligible"] ? "border-teal-200 bg-teal-50" : "border-red-200 bg-red-50"}`}>
          <div className="flex items-center gap-3 mb-4">
            {result["eligible"]
              ? <CheckCircle className="h-6 w-6 text-teal-600" />
              : <XCircle    className="h-6 w-6 text-red-600" />}
            <div>
              <p className="font-bold text-slate-900">
                {result["eligible"] ? "Eligible for dispatch" : "Not eligible — see blockers below"}
              </p>
              <p className="text-xs text-slate-500">
                {`Match score: ${String(result["matchScore"] ?? "--")} / 99`}
                {result["availableHosHours"] != null
                  ? ` · HOS available: ${String(result["availableHosHours"])}h`
                  : " · HOS: data unavailable"}
                {result["safetyScore"] != null
                  ? ` · Safety score: ${String(result["safetyScore"])}`
                  : ""}
              </p>
            </div>
          </div>

          {(result["blockingReasons"] as string[] | undefined)?.length ? (
            <div className="mb-3">
              <p className="text-xs font-bold uppercase text-red-700 mb-1">Blocking Reasons</p>
              <ul className="space-y-1">
                {(result["blockingReasons"] as string[]).map((r, i) => (
                  <li key={i} className="flex items-start gap-2 text-sm text-red-800">
                    <ShieldAlert className="mt-0.5 h-4 w-4 flex-shrink-0" />
                    {r}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {(result["warnings"] as string[] | undefined)?.length ? (
            <div>
              <p className="text-xs font-bold uppercase text-amber-700 mb-1">Warnings</p>
              <ul className="space-y-1">
                {(result["warnings"] as string[]).map((w, i) => (
                  <li key={i} className="flex items-start gap-2 text-sm text-amber-800">
                    <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" />
                    {w}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {result["overrideRequired"] ? (
            <div className="mt-4 rounded-lg bg-amber-100 p-3 text-sm text-amber-800">
              <strong>Override required.</strong>{" "}
              {canOverride
                ? "You have dispatch:override permission. Set override=true when creating assignment."
                : "You do not have permission to override eligibility blocks."}
            </div>
          ) : null}

          {result["vehicleOutOfService"] ? (
            <div className="mt-3 rounded-lg bg-red-100 p-3 text-sm font-bold text-red-800">
              Vehicle is out of service. This block cannot be overridden — resolve critical defects first.
            </div>
          ) : null}
        </section>
      )}

      {!result && !isLoading && Number(vehicleId) > 0 && Number(driverId) > 0 && (
        <p className="text-sm text-slate-400">Loading eligibility check…</p>
      )}
    </div>
  );
}

// ── Exceptions Tab ────────────────────────────────────────────────────────────
function ExceptionsTab({ rows, isLoading }: { rows: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!rows.length)
    return <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No open exceptions" />;
  return (
    <DataTable
      rows={rows}
      columns={["jobNumber", "exceptionType", "severity", "status", "driverName", "vehicleCode", "notes", "createdByName", "createdAt"]}
    />
  );
}

// ── Available Drivers Tab ─────────────────────────────────────────────────────
function AvailableDriversTab({ rows, isLoading }: { rows: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!rows.length)
    return <Empty icon={<User className="h-8 w-8 text-slate-300" />} message="No available drivers" />;
  return (
    <DataTable
      rows={rows}
      columns={["fullName", "status", "safetyScore", "availableHosHours", "activeAssignmentCount", "safetyBlocked", "matchReadiness"]}
    />
  );
}

// ── Available Vehicles Tab ────────────────────────────────────────────────────
function AvailableVehiclesTab({ rows, isLoading }: { rows: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!rows.length)
    return <Empty icon={<Truck className="h-8 w-8 text-slate-300" />} message="No available vehicles" />;
  return (
    <DataTable
      rows={rows}
      columns={["vehicleCode", "type", "status", "availabilityStatus", "criticalDefectCount", "blockingWoCount", "activeAssignmentCount", "matchReadiness"]}
    />
  );
}

// ── Assignment Detail Drawer ──────────────────────────────────────────────────
function AssignmentDrawer({
  assignment, canUpdate, canCancel, onClose, onStatusChange, onCancel, onProof, onException,
}: {
  assignment: AnyRecord;
  canUpdate: boolean;
  canCancel: boolean;
  onClose: () => void;
  onStatusChange: (id: number, s: string) => void;
  onCancel: (id: number) => void;
  onProof: (id: number, t: "pickup" | "delivery") => void;
  onException: (id: number) => void;
}) {
  const id     = Number(assignment["id"]);
  const status = String(assignment["assignmentStatus"] ?? "").toLowerCase();
  const next   = NEXT_STATUS[status] ?? [];

  return (
    <div className="fixed inset-y-0 right-0 w-96 shadow-2xl bg-white border-l border-slate-200 z-50 overflow-y-auto">
      <div className="sticky top-0 bg-white border-b border-slate-200 px-5 py-4 flex items-center justify-between">
        <h2 className="font-bold text-slate-900">Assignment Detail</h2>
        <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-700">
          <XCircle className="h-5 w-5" />
        </button>
      </div>
      <div className="p-5 space-y-4">
        <InfoRow label="Job #"      value={assignment["jobNumber"]} />
        <InfoRow label="Customer"   value={assignment["customerName"]} />
        <InfoRow label="Driver"     value={assignment["driverName"]} />
        <InfoRow label="Vehicle"    value={assignment["vehicleCode"]} />
        <InfoRow label="Status">
          <StatusBadge status={assignment["assignmentStatus"]} />
        </InfoRow>
        <InfoRow label="Safety Score">
          <SafetyScorePill score={assignment["driverSafetyScore"]} />
        </InfoRow>
        {assignment["tripCompliance"] !== null && assignment["tripCompliance"] !== undefined && (
          <InfoRow label="Route Compliance" value={`${assignment["tripCompliance"]}%`} />
        )}
        <InfoRow label="Planned Pickup"  value={fmtDate(assignment["plannedPickupAt"])} />
        <InfoRow label="Planned Delivery" value={fmtDate(assignment["plannedDeliveryAt"])} />
        <InfoRow label="Actual Pickup"   value={fmtDate(assignment["actualPickupAt"])} />
        <InfoRow label="Actual Delivery" value={fmtDate(assignment["actualDeliveryAt"])} />
        {Number(assignment["openExceptions"] ?? 0) > 0 ? (
          <p className="text-sm font-bold text-red-600">
            {String(assignment["openExceptions"])} open exception(s)
          </p>
        ) : null}

        {/* State transition actions */}
        {canUpdate && next.length > 0 && (
          <div>
            <p className="text-xs font-bold uppercase text-slate-500 mb-2">Advance Status</p>
            <div className="flex flex-wrap gap-2">
              {next.filter((s) => s !== "exception" && s !== "cancelled").map((s) => (
                <button
                  key={s}
                  type="button"
                  className="rounded-lg bg-teal-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-teal-700"
                  onClick={() => onStatusChange(id, s)}
                >
                  → {s.replace(/_/g, " ")}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Proof actions */}
        {canUpdate && (
          <div className="flex gap-2">
            {(status === "arrived_pickup" || status === "loaded") && (
              <button
                type="button"
                className="rounded-lg bg-blue-100 px-3 py-1.5 text-sm font-semibold text-blue-800 hover:bg-blue-200"
                onClick={() => onProof(id, "pickup")}
              >
                Record Pickup
              </button>
            )}
            {(status === "arrived_delivery" || status === "in_transit") && (
              <button
                type="button"
                className="rounded-lg bg-teal-100 px-3 py-1.5 text-sm font-semibold text-teal-800 hover:bg-teal-200"
                onClick={() => onProof(id, "delivery")}
              >
                Confirm Delivery
              </button>
            )}
          </div>
        )}

        {/* Exception + Cancel */}
        <div className="flex gap-2 pt-2 border-t border-slate-100">
          {canUpdate && !["delivered", "cancelled"].includes(status) && (
            <button
              type="button"
              className="rounded-lg bg-amber-100 px-3 py-1.5 text-sm font-semibold text-amber-800 hover:bg-amber-200"
              onClick={() => onException(id)}
            >
              Flag Exception
            </button>
          )}
          {canCancel && !["delivered", "cancelled"].includes(status) && (
            <button
              type="button"
              className="rounded-lg bg-red-100 px-3 py-1.5 text-sm font-semibold text-red-700 hover:bg-red-200"
              onClick={() => { onCancel(id); onClose(); }}
            >
              Cancel Assignment
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Exception Modal ───────────────────────────────────────────────────────────
const EXCEPTION_TYPES = [
  "late_pickup", "late_delivery", "vehicle_breakdown", "driver_unavailable",
  "customer_hold", "route_blocked", "compliance_hold", "maintenance_hold", "safety_hold",
];

function ExceptionModal({
  assignmentId, exceptionType, notes, isLoading,
  onTypeChange, onNotesChange, onSubmit, onClose,
}: {
  assignmentId: number;
  exceptionType: string;
  notes: string;
  isLoading: boolean;
  onTypeChange: (v: string) => void;
  onNotesChange: (v: string) => void;
  onSubmit: () => void;
  onClose: () => void;
}) {
  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl">
        <h3 className="font-bold text-slate-900 mb-4">Create Dispatch Exception</h3>
        <p className="text-xs text-slate-500 mb-4">Assignment #{assignmentId}</p>
        <label className="block mb-3">
          <span className="text-xs font-semibold text-slate-600 uppercase tracking-wide">Exception Type</span>
          <select
            className="mt-1 block w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            value={exceptionType}
            onChange={(e) => onTypeChange(e.target.value)}
          >
            {EXCEPTION_TYPES.map((t) => (
              <option key={t} value={t}>{t.replace(/_/g, " ")}</option>
            ))}
          </select>
        </label>
        <label className="block mb-4">
          <span className="text-xs font-semibold text-slate-600 uppercase tracking-wide">Notes</span>
          <textarea
            className="mt-1 block w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            rows={3}
            value={notes}
            onChange={(e) => onNotesChange(e.target.value)}
            placeholder="Describe the exception…"
          />
        </label>
        <div className="flex gap-3 justify-end">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button
            type="button"
            className="rounded-lg bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-50"
            disabled={isLoading}
            onClick={onSubmit}
          >
            {isLoading ? "Creating…" : "Create Exception"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Dispatch Insight Row ──────────────────────────────────────────────────────
function DispatchInsightRow({ insight }: { insight: AnyRecord }) {
  const level   = String(insight["level"] ?? "info");
  const message = String(insight["message"] ?? "");
  const styles: Record<string, string> = {
    critical: "border-red-200 bg-red-50 text-red-800",
    warning:  "border-amber-200 bg-amber-50 text-amber-800",
    ok:       "border-teal-200 bg-teal-50 text-teal-800",
    info:     "border-blue-200 bg-blue-50 text-blue-800",
  };
  const Icon = level === "ok" ? CheckCircle : level === "critical" ? AlertTriangle : Zap;
  return (
    <div className={`rounded-xl border p-4 ${styles[level] ?? styles.info}`}>
      <div className="flex items-start gap-3">
        <Icon className="mt-0.5 h-4 w-4 shrink-0" />
        <div>
          <p className="text-xs font-bold uppercase tracking-wide opacity-70">System Dispatch Insight</p>
          <p className="mt-0.5 text-sm">{message}</p>
        </div>
      </div>
    </div>
  );
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function InfoRow({ label, value, children }: { label: string; value?: unknown; children?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-slate-500 font-medium">{label}</span>
      <span className="text-slate-900 text-right">{children ?? (value !== null && value !== undefined ? String(value) : "--")}</span>
    </div>
  );
}

function SafetyScorePill({ score }: { score: unknown }) {
  if (score === null || score === undefined || score instanceof Object) return <span className="text-slate-400 text-xs">--</span>;
  const n = Number(score);
  const cls = n >= 80 ? "bg-teal-100 text-teal-700" : n >= 65 ? "bg-amber-100 text-amber-700" : "bg-red-100 text-red-700";
  return <span className={`rounded-full px-2 py-0.5 text-xs font-bold ${cls}`}>{n}</span>;
}

function Empty({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-10 text-center text-slate-400">
      <div className="mb-3">{icon}</div>
      <p className="text-sm">{message}</p>
    </div>
  );
}

function fmtDate(val: unknown): string {
  if (!val) return "--";
  try { return new Date(String(val)).toLocaleDateString(); } catch { return String(val); }
}
