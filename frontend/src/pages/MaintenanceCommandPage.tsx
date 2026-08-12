import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import {
  AlertTriangle, CheckCircle, Clock, ClipboardList,
  Plus, Settings, ShieldAlert, Truck, Wrench, X, XCircle, Zap,
} from "lucide-react";
import { DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv } from "@/components/ui";
import { maintenanceApi } from "@/services/maintenanceApi";
import { vehiclesApi } from "@/services/vehiclesApi";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

const TABS = ["Overview", "Defects", "Inspections", "Work Orders", "PM Rules", "Fault Codes"] as const;
type Tab = (typeof TABS)[number];

export function MaintenanceCommandPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedVehicleId = searchParams.get("vehicleId") ?? "";
  const requestedTab = searchParams.get("tab");
  const initialTab = TABS.find((tab) => tab === requestedTab) ?? (requestedVehicleId ? "Work Orders" : "Overview");
  const [activeTab, setActiveTab] = useState<Tab>(initialTab);
  const [createOpen, setCreateOpen] = useState(Boolean(requestedVehicleId));
  const [completionTarget, setCompletionTarget] = useState<AnyRecord | null>(null);
  const [resolveTarget, setResolveTarget] = useState<AnyRecord | null>(null);
  const [notice, setNotice] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const qc = useQueryClient();

  const dashboard = useQuery<AnyRecord>({
    queryKey: ["maintenance", "dashboard"],
    queryFn: maintenanceApi.dashboard,
    refetchInterval: 60_000,
  });
  const defects = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "defects"],
    queryFn: () => maintenanceApi.defects(),
    staleTime: 15_000,
  });
  const inspections = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "inspections"],
    queryFn: () => maintenanceApi.inspections({ limit: 50 }),
    staleTime: 15_000,
  });
  const workOrders = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "work-orders"],
    queryFn: () => maintenanceApi.workOrders({ limit: 50 }),
    staleTime: 15_000,
  });
  const pmRules = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "pm-rules"],
    queryFn: maintenanceApi.pmRules,
    staleTime: 60_000,
  });
  const faultCodes = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "fault-codes"],
    queryFn: () => maintenanceApi.faultCodes("active"),
    staleTime: 30_000,
  });

  const hasPermission = useHasPermission();
  const canManage = hasPermission("maintenance:manage");
  const canClose  = hasPermission("maintenance:close");
  const vehicles = useQuery<AnyRecord[]>({
    queryKey: ["vehicles", "maintenance-selector"],
    queryFn: vehiclesApi.list,
    enabled: canManage,
    staleTime: 60_000,
  });

  const invalidateAll = () => {
    qc.invalidateQueries({ queryKey: ["maintenance"] });
  };

  const ackDefect = useMutation({
    mutationFn: (record: AnyRecord) => maintenanceApi.acknowledgeDefect(Number(record.id), Number(record["rowVersion"] ?? record["row_version"])),
    onSuccess: invalidateAll,
  });
  const resolveDefect = useMutation({
    mutationFn: ({ id, rowVersion, notes }: { id: number; rowVersion: number; notes: string }) => maintenanceApi.resolveDefect(id, rowVersion, notes),
    onSuccess: () => {
      invalidateAll();
      setResolveTarget(null);
      setNotice({ kind: "success", message: "Defect resolved. Repair certification and driver acknowledgment are still required before vehicle release." });
    },
  });
  const reviewInspection = useMutation({
    mutationFn: (record: AnyRecord) => maintenanceApi.reviewInspection(Number(record.id), Number(record["rowVersion"] ?? record["row_version"])),
    onSuccess: invalidateAll,
  });
  const completeWo = useMutation({
    mutationFn: ({ id, actualCost, notes }: { id: number; actualCost: number; notes: string }) =>
      maintenanceApi.completeWorkOrder(id, actualCost, notes),
    onSuccess: () => {
      invalidateAll();
      setCompletionTarget(null);
      setNotice({ kind: "success", message: "Work order completed with actual cost and service notes recorded." });
    },
  });
  const createWo = useMutation({
    mutationFn: maintenanceApi.createWorkOrder,
    onSuccess: () => {
      invalidateAll();
      setCreateOpen(false);
      setSearchParams({}, { replace: true });
      setActiveTab("Work Orders");
      setNotice({ kind: "success", message: "Work order created." });
    },
  });

  if (dashboard.isLoading) return <LoadingState />;
  if (dashboard.isError)
    return <div className="p-8 text-red-600">Failed to load maintenance dashboard. Check backend connectivity.</div>;

  const d = dashboard.data as AnyRecord;
  const kpis = (d?.kpis as AnyRecord) ?? {};
  const openDefectsList  = (d?.openDefects  as AnyRecord[]) ?? [];
  const duePmList        = (d?.duePm        as AnyRecord[]) ?? [];
  const recentWos        = (d?.recentWorkOrders as AnyRecord[]) ?? [];
  const insights         = (d?.insights     as AnyRecord[]) ?? [];

  return (
    <div className="fleet-console flex h-full flex-col gap-3 overflow-y-auto">
      <PageHeader
        eyebrow="Fleet Maintenance"
        title="Work Orders"
        description="DVIR inspections, defect management, work orders, fault codes, and preventive maintenance — all persisted and RBAC-enforced."
        actions={<div className="flex flex-wrap gap-2">
          {canManage && <button
            type="button"
            className="btn-primary"
            onClick={() => { createWo.reset(); setNotice(null); setCreateOpen(true); }}
          >
            <Plus className="h-4 w-4" /> Create work order
          </button>}
          <button type="button" className="btn-ghost" onClick={() => exportCsv("maintenance-defects", defects.data ?? [])}>
            Export Defects
          </button>
        </div>}
      />

      {notice && (
        <div
          role={notice.kind === "error" ? "alert" : "status"}
          className={notice.kind === "error" ? "rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800" : "rounded-xl border border-teal-200 bg-teal-50 p-3 text-sm text-teal-800"}
        >
          {notice.message}
        </div>
      )}

      {/* KPI Strip */}
      <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-5">
        <KpiCard
          label="Fleet Available"
          value={`${String(kpis["fleetAvailabilityPct"] ?? "--")}%`}
          icon={<Truck />}
          status={Number(kpis["fleetAvailabilityPct"] ?? 100) >= 80 ? "Active" : "Warning"}
        />
        <KpiCard
          label="Vehicles Out of Service"
          value={String(kpis["vehiclesOutOfService"] ?? 0)}
          icon={<XCircle />}
          status={Number(kpis["vehiclesOutOfService"] ?? 0) > 0 ? "Critical" : "Active"}
        />
        <KpiCard
          label="Critical Open Defects"
          value={String(kpis["criticalOpenDefects"] ?? 0)}
          icon={<ShieldAlert />}
          status={Number(kpis["criticalOpenDefects"] ?? 0) > 0 ? "Critical" : "Active"}
        />
        <KpiCard
          label="Open Work Orders"
          value={String(kpis["openWorkOrders"] ?? 0)}
          icon={<Wrench />}
          status="Review"
        />
        <KpiCard
          label="PM Overdue"
          value={String(kpis["overduePm"] ?? 0)}
          icon={<Clock />}
          status={Number(kpis["overduePm"] ?? 0) > 0 ? "Warning" : "Active"}
        />
      </div>

      {/* System Maintenance Insights */}
      {insights.length > 0 && (
        <section className="panel p-5">
          <h2 className="section-title">System Maintenance Insights</h2>
          <div className="mt-4 space-y-3">
            {insights.map((ins, i) => (
              <InsightRow key={i} insight={ins} />
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
          {activeTab === "Overview" && (
            <OverviewTab
              openDefects={openDefectsList}
              duePm={duePmList}
              recentWos={recentWos}
              kpis={kpis}
              canManage={canManage}
              canClose={canClose}
              onAck={(record) => ackDefect.mutate(record)}
              onResolve={setResolveTarget}
              onCompleteWo={setCompletionTarget}
            />
          )}

          {activeTab === "Defects" && (
            <DefectsTab
              rows={defects.data ?? []}
              isLoading={defects.isLoading}
              canManage={canManage}
              canClose={canClose}
              onAck={(record) => ackDefect.mutate(record)}
              onResolve={setResolveTarget}
            />
          )}

          {activeTab === "Inspections" && (
            <InspectionsTab
              rows={inspections.data ?? []}
              isLoading={inspections.isLoading}
              canManage={canManage}
              onReview={(record) => reviewInspection.mutate(record)}
            />
          )}

          {activeTab === "Work Orders" && (
            <WorkOrdersTab
              rows={workOrders.data ?? []}
              isLoading={workOrders.isLoading}
              canManage={canManage}
              canClose={canClose}
              onComplete={setCompletionTarget}
            />
          )}

          {activeTab === "PM Rules" && (
            <PmRulesTab
              rows={pmRules.data ?? []}
              isLoading={pmRules.isLoading}
            />
          )}

          {activeTab === "Fault Codes" && (
            <FaultCodesTab
              rows={faultCodes.data ?? []}
              isLoading={faultCodes.isLoading}
            />
          )}
        </div>
      </section>

      {createOpen && canManage && <CreateWorkOrderDialog
        initialVehicleId={requestedVehicleId}
        vehicles={vehicles.data ?? []}
        vehiclesLoading={vehicles.isLoading}
        vehiclesError={vehicles.isError ? errorMessage(vehicles.error, "Vehicles could not be loaded.") : null}
        pending={createWo.isPending}
        error={createWo.isError ? errorMessage(createWo.error, "Work order could not be created.") : null}
        onRetryVehicles={() => void vehicles.refetch()}
        onClose={() => { if (!createWo.isPending) { setCreateOpen(false); setSearchParams({}, { replace: true }); } }}
        onSubmit={(payload) => createWo.mutate(payload)}
      />}
      {completionTarget && <CompleteWorkOrderDialog
        workOrder={completionTarget}
        pending={completeWo.isPending}
        error={completeWo.isError ? errorMessage(completeWo.error, "Work order could not be completed.") : null}
        onClose={() => { if (!completeWo.isPending) setCompletionTarget(null); }}
        onSubmit={(actualCost, notes) => completeWo.mutate({ id: Number(completionTarget.id), actualCost, notes })}
      />}
      {resolveTarget && <ResolveDefectDialog
        defect={resolveTarget}
        pending={resolveDefect.isPending}
        error={resolveDefect.isError ? errorMessage(resolveDefect.error, "Defect could not be resolved.") : null}
        onClose={() => { if (!resolveDefect.isPending) setResolveTarget(null); }}
        onSubmit={(notes) => resolveDefect.mutate({
          id: Number(resolveTarget.id),
          rowVersion: Number(resolveTarget.rowVersion ?? resolveTarget.row_version),
          notes,
        })}
      />}
    </div>
  );
}

// ── Insight Row ───────────────────────────────────────────────────────────────
function InsightRow({ insight }: { insight: AnyRecord }) {
  const level   = String(insight["level"] ?? "info");
  const message = String(insight["message"] ?? "");
  const type    = String(insight["type"] ?? "System Maintenance Insight");
  const styles: Record<string, string> = {
    critical: "border-red-200 bg-red-50 text-red-800",
    warning:  "border-amber-200 bg-amber-50 text-amber-800",
    ok:       "border-teal-200 bg-teal-50 text-teal-800",
    info:     "border-blue-200 bg-blue-50 text-blue-800",
  };
  const icons: Record<string, typeof AlertTriangle> = {
    critical: AlertTriangle,
    warning:  AlertTriangle,
    ok:       CheckCircle,
    info:     Zap,
  };
  const Icon = icons[level] ?? Zap;
  return (
    <div className={`rounded-xl border p-4 ${styles[level] ?? styles.info}`}>
      <div className="flex items-start gap-3">
        <Icon className="mt-0.5 h-4 w-4 shrink-0" />
        <div>
          <p className="text-xs font-bold uppercase tracking-wide opacity-70">{type}</p>
          <p className="mt-0.5 text-sm">{message}</p>
        </div>
      </div>
    </div>
  );
}

// ── Overview Tab ──────────────────────────────────────────────────────────────
function OverviewTab({
  openDefects, duePm, recentWos, kpis,
  canManage, canClose, onAck, onResolve, onCompleteWo,
}: {
  openDefects: AnyRecord[];
  duePm: AnyRecord[];
  recentWos: AnyRecord[];
  kpis: AnyRecord;
  canManage: boolean;
  canClose: boolean;
  onAck: (record: AnyRecord) => void;
  onResolve: (record: AnyRecord) => void;
  onCompleteWo: (record: AnyRecord) => void;
}) {
  return (
    <div className="grid gap-6 lg:grid-cols-2">
      <section>
        <h3 className="section-title mb-3">Open Defects Queue</h3>
        {openDefects.length === 0
          ? <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No open defects" />
          : openDefects.slice(0, 8).map((d) => (
              <DefectCard
                key={String(d["id"])}
                defect={d}
                canManage={canManage}
                canClose={canClose}
                onAck={() => onAck(d)}
                onResolve={() => onResolve(d)}
              />
            ))
        }
      </section>

      <div className="space-y-6">
        <section>
          <h3 className="section-title mb-3">PM Due / Overdue</h3>
          {duePm.length === 0
            ? <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No PM items due in 14 days" />
            : <DataTable
                rows={duePm}
                columns={["vehicleCode", "serviceType", "status", "priority", "dueDate", "estimatedCost"]}
              />
          }
        </section>

        <section>
          <h3 className="section-title mb-3">Recent Work Orders</h3>
          {recentWos.length === 0
            ? <Empty icon={<Wrench className="h-8 w-8 text-slate-300" />} message="No open work orders" />
            : recentWos.slice(0, 5).map((wo) => (
                <WorkOrderCard
                  key={String(wo["id"])}
                  wo={wo}
                  canClose={canClose}
                  onComplete={() => onCompleteWo(wo)}
                />
              ))
          }
        </section>
      </div>
    </div>
  );
}

// ── Defects Tab ───────────────────────────────────────────────────────────────
function DefectsTab({
  rows, isLoading, canManage, canClose, onAck, onResolve,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  canClose: boolean;
  onAck: (record: AnyRecord) => void;
  onResolve: (record: AnyRecord) => void;
}) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No defects found" />;
  return (
    <div className="space-y-3">
      {rows.map((d) => (
        <DefectCard
          key={String(d["id"])}
          defect={d}
          canManage={canManage}
          canClose={canClose}
          onAck={() => onAck(d)}
          onResolve={() => onResolve(d)}
        />
      ))}
    </div>
  );
}

// ── Inspections Tab ───────────────────────────────────────────────────────────
function InspectionsTab({
  rows, isLoading, canManage, onReview,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  onReview: (record: AnyRecord) => void;
}) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<ClipboardList className="h-8 w-8 text-slate-300" />} message="No inspections yet" />;
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th className="px-3 py-2">Report #</th>
            <th className="px-3 py-2">Vehicle</th>
            <th className="px-3 py-2">Driver</th>
            <th className="px-3 py-2">Type</th>
            <th className="px-3 py-2">Status</th>
            <th className="px-3 py-2">Defects</th>
            <th className="px-3 py-2">Submitted</th>
            <th className="px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {rows.map((r) => (
            <tr key={String(r["id"])} className="hover:bg-slate-50">
              <td className="px-3 py-2 font-mono text-xs">{String(r["reportNumber"] ?? "--")}</td>
              <td className="px-3 py-2 font-medium">{String(r["vehicleCode"] ?? "--")}</td>
              <td className="px-3 py-2">{String(r["driverName"] ?? "--")}</td>
              <td className="px-3 py-2">{String(r["inspectionType"] ?? "--")}</td>
              <td className="px-3 py-2"><StatusBadge status={r["inspectionStatus"]} /></td>
              <td className="px-3 py-2">
                <span className={Number(r["criticalDefects"] ?? 0) > 0 ? "font-bold text-red-600" : "text-slate-600"}>
                  {String(r["totalDefects"] ?? 0)}
                  {Number(r["criticalDefects"] ?? 0) > 0 ? ` (${r["criticalDefects"]} critical)` : ""}
                </span>
              </td>
              <td className="px-3 py-2 text-xs text-slate-500">{fmtDate(r["submittedAt"])}</td>
              <td className="px-3 py-2">
                {canManage && String(r["inspectionStatus"]) !== "reviewed" && (
                  <button
                    type="button"
                    className="btn-ghost text-xs py-1 px-2"
                    onClick={() => onReview(r)}
                  >
                    Review
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Work Orders Tab ───────────────────────────────────────────────────────────
function WorkOrdersTab({
  rows, isLoading, canManage, canClose, onComplete,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  canClose: boolean;
  onComplete: (record: AnyRecord) => void;
}) {
  if (isLoading) return <LoadingState />;
  return (
    <div className="space-y-3">
      {!canManage && (
        <p className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm text-slate-600">
          You have read-only access. Creating work orders requires maintenance management permission.
        </p>
      )}
      {!rows.length && <Empty icon={<Wrench className="h-8 w-8 text-slate-300" />} message="No work orders" />}
      {rows.map((wo) => (
        <WorkOrderCard key={String(wo["id"])} wo={wo} canClose={canClose} onComplete={() => onComplete(wo)} />
      ))}
    </div>
  );
}

// ── PM Rules Tab ──────────────────────────────────────────────────────────────
function PmRulesTab({ rows, isLoading }: { rows: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<Settings className="h-8 w-8 text-slate-300" />} message="No PM rules configured" />;
  return (
    <DataTable
      rows={rows}
      columns={["ruleName", "serviceType", "triggerType", "intervalMiles", "intervalEngineHours", "intervalDays", "priority", "estimatedCost", "enabled"]}
    />
  );
}

// ── Fault Codes Tab ───────────────────────────────────────────────────────────
function FaultCodesTab({ rows, isLoading }: { rows: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<Zap className="h-8 w-8 text-slate-300" />} message="No active fault codes" />;
  return (
    <DataTable
      rows={rows}
      columns={["vehicleCode", "code", "codeType", "severity", "description", "occurrenceCount", "firstSeenAt", "lastSeenAt", "status"]}
    />
  );
}

// ── Defect Card ───────────────────────────────────────────────────────────────
const SEV_STYLES: Record<string, string> = {
  Critical: "border-red-300 bg-red-50",
  Major:    "border-amber-200 bg-amber-50",
  Minor:    "border-slate-200 bg-slate-50",
};

function DefectCard({
  defect, canManage, canClose, onAck, onResolve,
}: {
  defect: AnyRecord;
  canManage: boolean;
  canClose: boolean;
  onAck: () => void;
  onResolve: () => void;
}) {
  const sev   = String(defect["severity"] ?? "Minor");
  const oos   = Boolean(defect["outOfService"] ?? defect["out_of_service"]);
  const style = SEV_STYLES[sev] ?? SEV_STYLES.Minor;
  const status = String(defect["status"] ?? "Open");

  return (
    <div className={`mb-3 rounded-xl border p-4 ${style}`}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            {oos && <span className="rounded-full bg-red-600 px-2 py-0.5 text-xs font-bold text-white">OUT OF SERVICE</span>}
            <RiskBadge risk={sev} />
            <StatusBadge status={status} />
            <span className="text-xs text-slate-500">{String(defect["vehicleCode"] ?? "--")}</span>
          </div>
          <p className="mt-1.5 text-sm font-semibold text-slate-900">
            {String(defect["defectDescription"] ?? defect["defect_description"] ?? "Defect")}
          </p>
          <p className="mt-0.5 text-xs text-slate-500">
            {String(defect["defectCategory"] ?? defect["defect_category"] ?? "--")} · {String(defect["source"] ?? "dvir")} · {fmtDate(defect["createdAt"] ?? defect["created_at"])}
          </p>
        </div>
      </div>
      {status !== "resolved" && (
        <div className="mt-3 flex gap-2">
          {canManage && status === "Open" && (
            <button type="button" className="btn-ghost text-xs py-1 px-2" onClick={onAck}>Acknowledge</button>
          )}
          {canClose && status !== "rejected" && (
          <button type="button" className="btn-ghost text-xs py-1 px-2 text-teal-700" onClick={onResolve}>Resolve defect</button>
          )}
        </div>
      )}
    </div>
  );
}

// ── Work Order Card ───────────────────────────────────────────────────────────
function WorkOrderCard({
  wo, canClose, onComplete,
}: {
  wo: AnyRecord;
  canClose: boolean;
  onComplete: () => void;
}) {
  const status = String(wo["status"] ?? "Open");
  const isOpen = !["Completed","completed","Cancelled","cancelled"].includes(status);

  return (
    <div className="mb-3 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <RiskBadge risk={wo["priority"]} />
            <StatusBadge status={wo["status"]} />
            {wo["recordOrigin"] === "seeded_synthetic_database" ? <span className="rounded-full border border-violet-200 bg-violet-50 px-2 py-0.5 text-[10px] font-bold text-violet-700">Demo Data</span> : null}
            {wo["recordOrigin"] === "unknown_database_record" ? <span className="rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-[10px] font-bold text-slate-600">Unverified DB Record</span> : null}
            <span className="font-mono text-xs text-slate-500">{String(wo["woNumber"] ?? wo["workOrderNumber"] ?? wo["workOrderCode"] ?? "--")}</span>
          </div>
          <p className="mt-1.5 text-sm font-semibold text-slate-900">{String(wo["title"] ?? wo["issueType"] ?? "Work Order")}</p>
          <p className="mt-0.5 text-xs text-slate-500">
            {String(wo["vehicleCode"] ?? "--")}
            {wo["assignedToName"] ? ` · Assigned: ${String(wo["assignedToName"])}` : ""}
            {wo["estimatedCost"] ? ` · Est: $${Number(wo["estimatedCost"]).toLocaleString()}` : ""}
          </p>
        </div>
        {canClose && isOpen && (
          <button type="button" className="btn-ghost text-xs py-1 px-2" onClick={onComplete}>Complete work order</button>
        )}
      </div>
    </div>
  );
}

type WorkOrderPayload = Parameters<typeof maintenanceApi.createWorkOrder>[0];

function CreateWorkOrderDialog({
  initialVehicleId, vehicles, vehiclesLoading, vehiclesError, pending, error, onRetryVehicles, onClose, onSubmit,
}: {
  initialVehicleId: string;
  vehicles: AnyRecord[];
  vehiclesLoading: boolean;
  vehiclesError: string | null;
  pending: boolean;
  error: string | null;
  onRetryVehicles: () => void;
  onClose: () => void;
  onSubmit: (payload: WorkOrderPayload) => void;
}) {
  const dialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [vehicleId, setVehicleId] = useState(initialVehicleId);
  const [title, setTitle] = useState("");
  const [serviceType, setServiceType] = useState("");
  const [priority, setPriority] = useState("Medium");
  const [estimatedCost, setEstimatedCost] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [description, setDescription] = useState("");
  const [validation, setValidation] = useState<string | null>(null);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const cost = Number(estimatedCost);
    if (!vehicleId || !title.trim() || !serviceType.trim() || !dueDate || estimatedCost === "") {
      setValidation("Vehicle, title, service, estimated cost, and due date are required.");
      return;
    }
    if (!Number.isFinite(cost) || cost < 0) {
      setValidation("Estimated cost must be zero or greater.");
      return;
    }
    setValidation(null);
    onSubmit({
      vehicleId: Number(vehicleId),
      title: title.trim(),
      serviceType: serviceType.trim(),
      description: description.trim() || undefined,
      priority,
      estimatedCost: cost,
      scheduledAt: dueDate,
    });
  };

  return (
    <div ref={dialogRef} className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" role="dialog" aria-modal="true" aria-labelledby="create-work-order-title">
      <form className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl" onSubmit={submit} noValidate>
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="section-title">Maintenance</p>
            <h2 id="create-work-order-title" className="mt-1 text-xl font-semibold text-slate-950">Create work order</h2>
            <p className="mt-1 text-sm text-slate-600">Creates a tenant-scoped work order for a vehicle from the live fleet registry.</p>
          </div>
          <button type="button" className="icon-btn" onClick={onClose} disabled={pending} aria-label="Close create work order dialog"><X className="h-5 w-5" /></button>
        </div>

        {(validation || error) && <p role="alert" className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">{validation || error}</p>}
        {vehiclesError && <div role="alert" className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <p>{vehiclesError}</p>
          <button type="button" className="btn-ghost mt-2 text-xs" onClick={onRetryVehicles}>Retry vehicle list</button>
        </div>}

        <div className="mt-5 grid gap-4 sm:grid-cols-2">
          <label className="field-label sm:col-span-2">Vehicle <span aria-hidden="true">*</span>
            <select className="input mt-1" value={vehicleId} onChange={(e) => setVehicleId(e.target.value)} disabled={vehiclesLoading || Boolean(vehiclesError)} required autoFocus>
              <option value="">{vehiclesLoading ? "Loading vehicles…" : "Select a vehicle"}</option>
              {vehicles.map((vehicle) => <option key={String(vehicle.id)} value={String(vehicle.id)}>{vehicleLabel(vehicle)}</option>)}
            </select>
          </label>
          <label className="field-label">Title <span aria-hidden="true">*</span>
            <input className="input mt-1" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={160} required />
          </label>
          <label className="field-label">Service <span aria-hidden="true">*</span>
            <input className="input mt-1" value={serviceType} onChange={(e) => setServiceType(e.target.value)} placeholder="e.g. Brake service" maxLength={100} required />
          </label>
          <label className="field-label">Priority <span aria-hidden="true">*</span>
            <select className="input mt-1" value={priority} onChange={(e) => setPriority(e.target.value)} required>
              {['Low', 'Medium', 'High', 'Critical'].map((value) => <option key={value}>{value}</option>)}
            </select>
          </label>
          <label className="field-label">Estimated cost <span aria-hidden="true">*</span>
            <input className="input mt-1" type="number" min="0" step="0.01" inputMode="decimal" value={estimatedCost} onChange={(e) => setEstimatedCost(e.target.value)} required />
          </label>
          <label className="field-label">Due date <span aria-hidden="true">*</span>
            <input className="input mt-1" type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} required />
          </label>
          <label className="field-label sm:col-span-2">Description
            <textarea className="input mt-1 min-h-24" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={2000} />
          </label>
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-ghost" onClick={onClose} disabled={pending}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={pending || vehiclesLoading || Boolean(vehiclesError) || vehicles.length === 0} aria-busy={pending}>
            {pending ? "Creating…" : "Create work order"}
          </button>
        </div>
      </form>
    </div>
  );
}

function CompleteWorkOrderDialog({ workOrder, pending, error, onClose, onSubmit }: {
  workOrder: AnyRecord;
  pending: boolean;
  error: string | null;
  onClose: () => void;
  onSubmit: (actualCost: number, notes: string) => void;
}) {
  const dialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [actualCost, setActualCost] = useState("");
  const [notes, setNotes] = useState("");
  const [validation, setValidation] = useState<string | null>(null);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const cost = Number(actualCost);
    if (actualCost === "" || !Number.isFinite(cost) || cost < 0) return setValidation("Actual cost must be zero or greater.");
    if (notes.trim().length < 3) return setValidation("Add service notes before completing the work order.");
    setValidation(null);
    onSubmit(cost, notes.trim());
  };
  return <div ref={dialogRef} className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" role="alertdialog" aria-modal="true" aria-labelledby="complete-work-order-title" aria-describedby="complete-work-order-description">
    <form className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl" onSubmit={submit} noValidate>
      <div className="flex items-start justify-between gap-4"><div><h2 id="complete-work-order-title" className="text-xl font-semibold text-slate-950">Complete work order</h2><p id="complete-work-order-description" className="mt-1 text-sm text-slate-600">Record the actual service cost and notes for {String(workOrder.woNumber ?? workOrder.workOrderNumber ?? "this work order")}.</p></div><button type="button" className="icon-btn" onClick={onClose} disabled={pending} aria-label="Close completion dialog"><X className="h-5 w-5" /></button></div>
      {(validation || error) && <p role="alert" className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">{validation || error}</p>}
      <label className="field-label mt-5 block">Actual cost <span aria-hidden="true">*</span><input autoFocus className="input mt-1" type="number" min="0" step="0.01" inputMode="decimal" value={actualCost} onChange={(e) => setActualCost(e.target.value)} required /></label>
      <label className="field-label mt-4 block">Service notes <span aria-hidden="true">*</span><textarea className="input mt-1 min-h-28" value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} required /></label>
      <div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose} disabled={pending}>Cancel</button><button type="submit" className="btn-primary" disabled={pending} aria-busy={pending}>{pending ? "Completing…" : "Complete work order"}</button></div>
    </form>
  </div>;
}

function ResolveDefectDialog({ defect, pending, error, onClose, onSubmit }: {
  defect: AnyRecord;
  pending: boolean;
  error: string | null;
  onClose: () => void;
  onSubmit: (notes: string) => void;
}) {
  const dialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [notes, setNotes] = useState("");
  const [validation, setValidation] = useState<string | null>(null);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (notes.trim().length < 3) return setValidation("Add resolution notes before resolving the defect.");
    setValidation(null);
    onSubmit(notes.trim());
  };
  return <div ref={dialogRef} className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" role="alertdialog" aria-modal="true" aria-labelledby="resolve-defect-title" aria-describedby="resolve-defect-description">
    <form className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl" onSubmit={submit} noValidate>
      <div className="flex items-start justify-between gap-4"><div><h2 id="resolve-defect-title" className="text-xl font-semibold text-slate-950">Resolve defect</h2><p id="resolve-defect-description" className="mt-1 text-sm text-slate-600">Confirm the repair outcome for {String(defect.vehicleCode ?? "this vehicle")}. This may change vehicle availability.</p></div><button type="button" className="icon-btn" onClick={onClose} disabled={pending} aria-label="Close resolution dialog"><X className="h-5 w-5" /></button></div>
      {(validation || error) && <p role="alert" className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">{validation || error}</p>}
      <label className="field-label mt-5 block">Resolution notes <span aria-hidden="true">*</span><textarea autoFocus className="input mt-1 min-h-28" value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} required /></label>
      <div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose} disabled={pending}>Cancel</button><button type="submit" className="btn-primary" disabled={pending} aria-busy={pending}>{pending ? "Resolving…" : "Resolve defect"}</button></div>
    </form>
  </div>;
}

function vehicleLabel(vehicle: AnyRecord): string {
  const code = String(vehicle.vehicleCode ?? vehicle.code ?? vehicle.unitNumber ?? `Vehicle ${vehicle.id}`);
  const plate = vehicle.plateNumber ?? vehicle.plate_number ?? vehicle.licensePlate ?? vehicle.registrationNumber;
  return plate ? `${code} · ${String(plate)}` : code;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}

// ── Helpers ───────────────────────────────────────────────────────────────────
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
