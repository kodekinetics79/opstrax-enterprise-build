import { useEffect, useRef, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocation, useSearchParams } from "react-router";
import {
  AlertTriangle, ArrowLeft, CheckCircle, ChevronLeft, ChevronRight, Clock, ClipboardList,
  Plus, Search, Settings, ShieldAlert, Truck, Wrench, X, XCircle, Zap,
} from "lucide-react";
import { DataTable, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv } from "@/components/ui";
import { maintenanceApi } from "@/services/maintenanceApi";
import { vehiclesApi } from "@/services/vehiclesApi";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useHasPermission } from "@/hooks/usePermission";
import { WorkspaceGuidance } from "@/components/WorkspaceGuidance";
import type { AnyRecord } from "@/types";
import "./maintenance-workspace.css";

const TABS = ["Overview", "Defects", "Inspections", "Work Orders", "PM Rules", "Fault Codes", "Diagnostic Holds"] as const;
type Tab = (typeof TABS)[number];

export function MaintenanceCommandPage() {
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedVehicleId = searchParams.get("vehicleId") ?? "";
  const requestedTab = searchParams.get("tab");
  const routeTab: Tab = location.pathname === "/work-orders"
    ? "Work Orders"
    : location.pathname === "/inspections"
      ? "Inspections"
      : "Overview";
  const initialTab = TABS.find((tab) => tab === requestedTab) ?? (requestedVehicleId ? "Work Orders" : routeTab);
  const [activeTab, setActiveTab] = useState<Tab>(initialTab);
  const [createOpen, setCreateOpen] = useState(Boolean(requestedVehicleId));
  const handledVehicleIntent = useRef(requestedVehicleId);
  const [completionTarget, setCompletionTarget] = useState<AnyRecord | null>(null);
  const [resolveTarget, setResolveTarget] = useState<AnyRecord | null>(null);
  const [resolveHoldTarget, setResolveHoldTarget] = useState<AnyRecord | null>(null);
  const [notice, setNotice] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const qc = useQueryClient();

  useEffect(() => {
    const nextTab = TABS.find((tab) => tab === requestedTab) ?? (requestedVehicleId ? "Work Orders" : routeTab);
    setActiveTab(nextTab);
    if (!requestedVehicleId) {
      handledVehicleIntent.current = "";
      return;
    }
    if (handledVehicleIntent.current !== requestedVehicleId) {
      handledVehicleIntent.current = requestedVehicleId;
      setCreateOpen(true);
    }
  }, [requestedTab, requestedVehicleId, routeTab]);

  const selectTab = (tab: Tab) => {
    setActiveTab(tab);
    const next = new URLSearchParams(searchParams);
    if (tab === routeTab) next.delete("tab");
    else next.set("tab", tab);
    setSearchParams(next, { replace: true });
  };

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
  const diagnosticHolds = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "diagnostic-holds"],
    queryFn: () => maintenanceApi.diagnosticHolds(),
    staleTime: 15_000,
  });

  const hasPermission = useHasPermission();
  const canManage = hasPermission("maintenance:manage");
  const canClose  = hasPermission("maintenance:close");
  const canUpdateDiagnosticHold = canManage || hasPermission("maintenance:update") || hasPermission("telematics:manage");
  const canExport = hasPermission("reports:export");
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
  const acknowledgeDiagnosticHold = useMutation({
    mutationFn: (record: AnyRecord) => maintenanceApi.acknowledgeDiagnosticHold(Number(record.id)),
    onSuccess: () => {
      invalidateAll();
      setNotice({ kind: "success", message: "Diagnostic hold acknowledged. Vehicle availability is unchanged until verified resolution." });
    },
  });
  const resolveDiagnosticHold = useMutation({
    mutationFn: ({ id, resolutionNote, verificationType, evidenceReference }: {
      id: number;
      resolutionNote: string;
      verificationType: "technician_scan" | "provider_diagnostic" | "service_record";
      evidenceReference: string;
    }) => maintenanceApi.resolveDiagnosticHold(id, { resolutionNote, verificationType, evidenceReference }),
    onSuccess: () => {
      invalidateAll();
      setResolveHoldTarget(null);
      setNotice({ kind: "success", message: "Diagnostic hold resolved with verification evidence. Vehicle availability was re-evaluated against every remaining blocker." });
    },
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
  const maintenanceMetrics = [
    { label: "Fleet available", value: kpis["fleetAvailabilityPct"] == null ? "—" : `${String(kpis["fleetAvailabilityPct"])}%`, detail: "readiness", tone: kpis["fleetAvailabilityPct"] == null ? "text-slate-500" : Number(kpis["fleetAvailabilityPct"]) >= 80 ? "text-emerald-700" : "text-amber-700", icon: <Truck className="h-3.5 w-3.5" /> },
    { label: "Out of service", value: String(kpis["vehiclesOutOfService"] ?? 0), detail: "vehicles", tone: Number(kpis["vehiclesOutOfService"] ?? 0) > 0 ? "text-rose-700" : "text-slate-800", icon: <XCircle className="h-3.5 w-3.5" /> },
    { label: "Critical defects", value: String(kpis["criticalOpenDefects"] ?? 0), detail: "open", tone: Number(kpis["criticalOpenDefects"] ?? 0) > 0 ? "text-rose-700" : "text-slate-800", icon: <ShieldAlert className="h-3.5 w-3.5" /> },
    { label: "Work orders", value: String(kpis["openWorkOrders"] ?? 0), detail: "open", tone: "text-slate-800", icon: <Wrench className="h-3.5 w-3.5" /> },
    { label: "PM overdue", value: String(kpis["overduePm"] ?? 0), detail: "items", tone: Number(kpis["overduePm"] ?? 0) > 0 ? "text-amber-700" : "text-slate-800", icon: <Clock className="h-3.5 w-3.5" /> },
  ];

  return (
    <div className="maintenance-workspace page-stack">
      <PageHeader
        title={activeTab === "Work Orders" ? "Work Orders" : activeTab === "Defects" ? "Defect Queue" : "Maintenance Center"}
        description="Review vehicle blockers, prioritize service work, and record the outcome."
        actions={<div className="flex flex-wrap gap-2">
          {canManage && <button
            type="button"
            className="btn-primary"
            onClick={() => { createWo.reset(); setNotice(null); setCreateOpen(true); }}
          >
            <Plus className="h-4 w-4" /> Create work order
          </button>}
          {canExport && <button type="button" className="btn-ghost" onClick={() => exportCsv("maintenance-defects", defects.data ?? [])}>
            Export Defects
          </button>}
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

      {ackDefect.isError && <p role="alert" className="maintenance-error">{errorMessage(ackDefect.error, "Defect could not be acknowledged. Select the record and retry.")}</p>}

      <section className="panel p-2" aria-label="Maintenance operating summary">
        <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-5">
          {maintenanceMetrics.map((metric) => (
            <div key={metric.label} className="min-w-0 rounded-lg border border-slate-200 bg-slate-50/70 px-3 py-2">
              <dt className="flex items-center gap-1.5 text-[10px] font-bold uppercase tracking-[0.1em] text-slate-600">{metric.icon}{metric.label}</dt>
              <dd className="mt-1 flex items-baseline gap-1.5"><strong className={`text-base font-black leading-none tabular-nums ${metric.tone}`}>{metric.value}</strong><span className="text-[10px] font-medium text-slate-400">{metric.detail}</span></dd>
            </div>
          ))}
        </dl>
      </section>

      <WorkspaceGuidance
        nextStep={activeTab === "Work Orders" ? "Select a work order to review its context and record completion." : activeTab === "Defects" ? "Review out-of-service and critical defects first." : "Review blockers and overdue work, then open the relevant queue."}
        steps={["Filter the loaded queue by vehicle or status, then sort by recorded priority.", "Select a record to inspect its context and available actions.", "Record repair or service evidence before completing the work. Vehicle release remains a separate check."]}
      />

      {/* System Maintenance Insights */}
      {activeTab === "Overview" && insights.length > 0 && (
        <details className="panel px-3 py-2">
          <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2 sm:min-h-8">
            <Zap className="h-4 w-4 shrink-0 text-amber-600" />
            <span className="text-xs font-bold uppercase tracking-[0.12em] text-slate-600">Maintenance insights</span>
            <span className="min-w-0 flex-1 truncate text-xs text-slate-500">{String(insights[0]?.message ?? "Recorded maintenance guidance available")}</span>
            <span className="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-bold text-slate-600">{insights.length}</span>
          </summary>
          <div className="mt-2 space-y-2 border-t border-slate-100 pt-2">
            {insights.map((ins, i) => (
              <InsightRow key={i} insight={ins} />
            ))}
          </div>
        </details>
      )}

      {/* Tabs */}
      <section className="maintenance-tabs-section">
        <div className="flex flex-wrap gap-2 border-b border-slate-200 pb-2">
          {TABS.map((tab) => (
            <button
              key={tab}
              type="button"
              className={tab === activeTab ? "control-tab control-tab-active" : "control-tab"}
              onClick={() => selectTab(tab)}
            >
              {tab}
            </button>
          ))}
        </div>

        <div className="mt-3">
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
              ackPending={ackDefect.isPending}
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
              actionPending={ackDefect.isPending}
              error={defects.isError ? errorMessage(defects.error, "Defects could not be loaded.") : null}
              onRetry={() => void defects.refetch()}
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
              error={workOrders.isError ? errorMessage(workOrders.error, "Work orders could not be loaded.") : null}
              onRetry={() => void workOrders.refetch()}
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

          {activeTab === "Diagnostic Holds" && (
            <DiagnosticHoldsTab
              rows={diagnosticHolds.data ?? []}
              isLoading={diagnosticHolds.isLoading}
              canUpdate={canUpdateDiagnosticHold}
              isAcknowledging={acknowledgeDiagnosticHold.isPending}
              error={diagnosticHolds.isError
                ? errorMessage(diagnosticHolds.error, "Diagnostic holds could not be loaded.")
                : acknowledgeDiagnosticHold.isError
                  ? errorMessage(acknowledgeDiagnosticHold.error, "Diagnostic hold could not be acknowledged.")
                  : null}
              onRetry={() => void diagnosticHolds.refetch()}
              onAcknowledge={(record) => acknowledgeDiagnosticHold.mutate(record)}
              onResolve={setResolveHoldTarget}
            />
          )}
        </div>
      </section>

      {createOpen && canManage && <CreateWorkOrderDialog
        key={requestedVehicleId || "manual"}
        initialVehicleId={requestedVehicleId}
        vehicles={vehicles.data ?? []}
        vehiclesLoading={vehicles.isLoading}
        vehiclesError={vehicles.isError ? errorMessage(vehicles.error, "Vehicles could not be loaded.") : null}
        pending={createWo.isPending}
        error={createWo.isError ? errorMessage(createWo.error, "Work order could not be created.") : null}
        onRetryVehicles={() => void vehicles.refetch()}
        onClose={() => {
          if (createWo.isPending) return;
          handledVehicleIntent.current = requestedVehicleId;
          setCreateOpen(false);
          const next = new URLSearchParams(searchParams);
          next.delete("vehicleId");
          setSearchParams(next, { replace: true });
        }}
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
      {resolveHoldTarget && <ResolveDiagnosticHoldDialog
        hold={resolveHoldTarget}
        pending={resolveDiagnosticHold.isPending}
        error={resolveDiagnosticHold.isError
          ? errorMessage(resolveDiagnosticHold.error, "Diagnostic hold could not be resolved.")
          : null}
        onClose={() => { if (!resolveDiagnosticHold.isPending) setResolveHoldTarget(null); }}
        onSubmit={(resolutionNote, verificationType, evidenceReference) => resolveDiagnosticHold.mutate({
          id: Number(resolveHoldTarget.id), resolutionNote, verificationType, evidenceReference,
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
    <div className={`rounded-xl border p-3 ${styles[level] ?? styles.info}`}>
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
  canManage, canClose, onAck, onResolve, onCompleteWo, ackPending,
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
  ackPending: boolean;
}) {
  return (
    <div className="maintenance-overview">
      <section>
        <MaintenanceQueue kind="defect" heading="Open defects" rows={openDefects.slice(0, 8)} preview canManage={canManage} canClose={canClose} onAck={onAck} onResolve={onResolve} actionPending={ackPending} />
      </section>
      <section>
          <h3 className="maintenance-section-title">PM due / overdue</h3>
          {duePm.length === 0
            ? <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No PM items due in 14 days" />
            : <DataTable
                rows={duePm}
                columns={["vehicleCode", "serviceType", "status", "priority", "dueDate", "estimatedCost"]}
              />
          }
      </section>
      <section className="maintenance-overview-wide">
        <MaintenanceQueue kind="work-order" heading="Recent work orders" rows={recentWos.slice(0, 5)} preview canManage={canManage} canClose={canClose} onComplete={onCompleteWo} />
      </section>
    </div>
  );
}

// ── Defects Tab ───────────────────────────────────────────────────────────────
function DefectsTab({
  rows, isLoading, canManage, canClose, onAck, onResolve, actionPending, error, onRetry,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  canClose: boolean;
  onAck: (record: AnyRecord) => void;
  onResolve: (record: AnyRecord) => void;
  actionPending: boolean;
  error: string | null;
  onRetry: () => void;
}) {
  if (isLoading) return <LoadingState />;
  if (error) return <QueueError message={error} onRetry={onRetry} />;
  return (
    <MaintenanceQueue kind="defect" heading="Defect queue" rows={rows} canManage={canManage} canClose={canClose} onAck={onAck} onResolve={onResolve} actionPending={actionPending} />
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
  rows, isLoading, canManage, canClose, onComplete, error, onRetry,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  canClose: boolean;
  onComplete: (record: AnyRecord) => void;
  error: string | null;
  onRetry: () => void;
}) {
  if (isLoading) return <LoadingState />;
  if (error) return <QueueError message={error} onRetry={onRetry} />;
  return (
    <div className="space-y-3">
      {!canManage && (
        <p className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm text-slate-600">
          Creating work orders requires maintenance management permission. Completion has a separate permission.
        </p>
      )}
      <MaintenanceQueue kind="work-order" heading="Work order queue" rows={rows} canManage={canManage} canClose={canClose} onComplete={onComplete} />
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
      columns={["vehicleCode", "code", "protocol", "severity", "evidenceClassification", "safetyActionStatus", "diagnosticEvidenceReferences", "diagnosticEvidenceReferenceCount", "diagnosticEvidenceReferenceDigest", "sourceAddress", "bus", "occurrenceCount", "lastObservedAt", "status"]}
    />
  );
}

function DiagnosticHoldsTab({
  rows, isLoading, canUpdate, isAcknowledging, error, onRetry, onAcknowledge, onResolve,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canUpdate: boolean;
  isAcknowledging: boolean;
  error: string | null;
  onRetry: () => void;
  onAcknowledge: (record: AnyRecord) => void;
  onResolve: (record: AnyRecord) => void;
}) {
  if (isLoading) return <LoadingState />;
  if (error) return <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800">
    <p>{error}</p><button type="button" className="btn-ghost mt-3 text-xs" onClick={onRetry}>Retry</button>
  </div>;
  if (!rows.length) return <Empty icon={<ShieldAlert className="h-8 w-8 text-slate-300" />} message="No diagnostic holds recorded" />;
  return <div className="space-y-3">
    <p className="text-sm text-slate-600">These rows are persisted safety actions. Fault observations without a hold remain in the Fault Codes tab as observation-only evidence.</p>
    {rows.map((hold) => {
      const status = String(hold.status ?? "unknown");
      const canAcknowledge = status.toLowerCase() === "active";
      const canResolve = ["active", "acknowledged"].includes(status.toLowerCase());
      return <article key={String(hold.id)} className="rounded-xl border border-slate-200 bg-white p-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="font-semibold text-slate-950">{String(hold.vehicleCode ?? `Vehicle ${hold.vehicleId ?? "—"}`)}</h3>
              <StatusBadge status={status} />
              <RiskBadge risk={String(hold.severity ?? "Critical")} />
            </div>
            <p className="mt-1 font-mono text-sm text-slate-700">{String(hold.canonicalDtc ?? hold.code ?? "Diagnostic code unavailable")}</p>
            <p className="mt-2 text-sm text-slate-600">{String(hold.reason ?? "No hold reason recorded")}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            {canAcknowledge && <button type="button" className="btn-ghost text-xs" disabled={!canUpdate || isAcknowledging} onClick={() => onAcknowledge(hold)}>{isAcknowledging ? "Acknowledging…" : "Acknowledge"}</button>}
            {canResolve && <button type="button" className="btn-primary text-xs" disabled={!canUpdate} onClick={() => onResolve(hold)}>Resolve with evidence</button>}
          </div>
        </div>
        <dl className="mt-4 grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
          <div><dt className="text-slate-500">Device</dt><dd className="font-medium text-slate-800">{String(hold.deviceId ?? "—")}</dd></div>
          <div><dt className="text-slate-500">Source event</dt><dd className="break-all font-mono text-xs text-slate-800">{String(hold.sourceEventId ?? "—")}</dd></div>
          <div><dt className="text-slate-500">First observed</dt><dd className="font-medium text-slate-800">{fmtDateTime(hold.firstObservedAt)}</dd></div>
          <div><dt className="text-slate-500">Last observed</dt><dd className="font-medium text-slate-800">{fmtDateTime(hold.lastObservedAt)}</dd></div>
        </dl>
        {!canUpdate && canResolve && <p className="mt-3 text-xs text-slate-500">Maintenance update or telematics management permission is required to change this hold.</p>}
      </article>;
    })}
  </div>;
}

type MaintenanceQueueKind = "defect" | "work-order";

function queueTitle(record: AnyRecord, kind: MaintenanceQueueKind) {
  return String(kind === "defect" ? record.defectDescription ?? record.defect_description ?? "Defect" : record.title ?? record.issueType ?? "Work order");
}

function queueCode(record: AnyRecord, kind: MaintenanceQueueKind) {
  return String(kind === "defect" ? record.defectNumber ?? record.id ?? "—" : record.woNumber ?? record.workOrderNumber ?? record.workOrderCode ?? record.id ?? "—");
}

function queueOrigin(record: AnyRecord) {
  const origin = record.recordOrigin ?? record.record_origin;
  if (origin === "seeded_synthetic_database") return "Demo Data";
  if (origin === "unknown_database_record") return "Unverified DB Record";
  return origin == null ? "" : String(origin);
}

function queuePriority(record: AnyRecord, kind: MaintenanceQueueKind) {
  if (kind === "defect" && Boolean(record.outOfService ?? record.out_of_service)) return -1;
  const level = String(kind === "defect" ? record.severity : record.priority).toLowerCase();
  return ({ critical: 0, high: 1, major: 1, warning: 2, medium: 2, minor: 3, normal: 4, low: 4 } as Record<string, number>)[level] ?? 5;
}

function QueueError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return <div role="alert" className="maintenance-error"><p>{message}</p><button type="button" className="btn-ghost btn-compact mt-2" onClick={onRetry}>Retry loading</button></div>;
}

function MaintenanceQueue({
  kind, heading, rows, canManage, canClose, onAck, onResolve, onComplete, actionPending = false, preview = false,
}: {
  kind: MaintenanceQueueKind;
  heading: string;
  rows: AnyRecord[];
  canManage: boolean;
  canClose: boolean;
  onAck?: (record: AnyRecord) => void;
  onResolve?: (record: AnyRecord) => void;
  onComplete?: (record: AnyRecord) => void;
  actionPending?: boolean;
  preview?: boolean;
}) {
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");
  const [sort, setSort] = useState("priority");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [drawerRequested, setDrawerRequested] = useState(false);
  const [narrow, setNarrow] = useState(() => window.matchMedia("(max-width: 1199px)").matches);
  const query = search.trim().toLowerCase();
  const statuses = [...new Set(rows.map((record) => String(record.status ?? "Open")))];
  const filtered = rows.filter((record) => (statusFilter === "All" || String(record.status ?? "Open") === statusFilter) && (!query || [queueTitle(record, kind), queueCode(record, kind), record.vehicleCode, record.assignedToName, record.source].some((value) => String(value ?? "").toLowerCase().includes(query)))).sort((a, b) => {
    const dateA = new Date(String(a.createdAt ?? a.created_at ?? "")).getTime();
    const dateB = new Date(String(b.createdAt ?? b.created_at ?? "")).getTime();
    const dateOrder = !Number.isFinite(dateA) ? (Number.isFinite(dateB) ? 1 : 0) : !Number.isFinite(dateB) ? -1 : sort === "recent" ? dateB - dateA : dateA - dateB;
    return sort === "priority" ? queuePriority(a, kind) - queuePriority(b, kind) || dateOrder : dateOrder;
  });
  const pageSize = 15;
  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  const visible = preview ? filtered : filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const selected = filtered.find((record) => String(record.id) === selectedId) ?? (!preview ? visible[0] ?? null : null);
  const drawerOpen = Boolean(selected && drawerRequested && (preview || narrow));
  const closeDrawer = () => setDrawerRequested(false);
  const drawerRef = useDialogFocus<HTMLElement>(drawerOpen, closeDrawer);

  useEffect(() => {
    const media = window.matchMedia("(max-width: 1199px)");
    const update = () => setNarrow(media.matches);
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, []);

  useEffect(() => {
    if (selectedId && !filtered.some((record) => String(record.id) === selectedId)) {
      setSelectedId(null);
      setDrawerRequested(false);
    }
  }, [rows, search, statusFilter, selectedId]);

  const openRecord = (record: AnyRecord) => {
    setSelectedId(String(record.id));
    if (preview || narrow) setDrawerRequested(true);
  };
  const openAction = (action: ((record: AnyRecord) => void) | undefined, record: AnyRecord) => {
    closeDrawer();
    action?.(record);
  };
  const inspector = selected ? <MaintenanceInspector record={selected} kind={kind} canManage={canManage} canClose={canClose} actionPending={actionPending} onAck={onAck} onResolve={(record) => openAction(onResolve, record)} onComplete={(record) => openAction(onComplete, record)} /> : null;

  return <div className={`maintenance-queue-layout ${preview ? "is-preview" : ""}`}>
    <section className="maintenance-queue-frame" aria-label={heading}>
      <div className="maintenance-queue-heading"><h3>{heading}</h3><span>{preview ? `${rows.length} shown in overview` : `${rows.length} loaded records`}</span></div>
      {!preview && <div className="maintenance-toolbar">
        <label className="maintenance-search"><Search className="h-4 w-4" aria-hidden="true" /><span className="sr-only">Search {heading.toLowerCase()}</span><input type="search" className="field" placeholder="Search work, vehicle, assignment…" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /></label>
        <select aria-label={`Filter ${heading.toLowerCase()} by status`} className="field" value={statusFilter} onChange={(event) => { setStatusFilter(event.target.value); setPage(1); }}><option value="All">All statuses</option>{statuses.map((status) => <option key={status}>{status}</option>)}</select>
        <select aria-label={`Sort ${heading.toLowerCase()}`} className="field" value={sort} onChange={(event) => { setSort(event.target.value); setPage(1); setSelectedId(null); }}><option value="priority">Priority · oldest first</option><option value="recent">Newest recorded</option><option value="oldest">Oldest recorded</option></select>
        {(search || statusFilter !== "All") && <button type="button" className="btn-ghost btn-compact" onClick={() => { setSearch(""); setStatusFilter("All"); setPage(1); }}>Clear filters</button>}
      </div>}
      <div className="maintenance-list-scroll">
        <div className="maintenance-column-head" aria-hidden="true"><span>Priority</span><span>{kind === "defect" ? "Defect / recorded source" : "Work order / origin"}</span><span>Vehicle</span><span>Status</span><span>{kind === "defect" ? "Recorded" : "Assigned"}</span></div>
        <div role="list" aria-label={`${heading} records`}>
          {visible.map((record) => <div role="listitem" key={String(record.id)}><button type="button" className={`maintenance-row ${selected?.id === record.id ? "is-selected" : ""}`} onClick={() => openRecord(record)} aria-pressed={selected?.id === record.id} aria-haspopup={preview || narrow ? "dialog" : undefined} aria-label={`Inspect ${queueTitle(record, kind)}, vehicle ${String(record.vehicleCode ?? "not recorded")}, ${String(record.status ?? "Open")}`}>
            <span className="maintenance-row-priority"><RiskBadge risk={kind === "defect" ? record.severity ?? "Minor" : record.priority} />{kind === "defect" && Boolean(record.outOfService ?? record.out_of_service) && <span className="maintenance-oos">Out of service</span>}</span>
            <span className="maintenance-row-description"><strong>{queueTitle(record, kind)}</strong><span>{queueCode(record, kind)} · {kind === "defect" ? String(record.defectCategory ?? record.defect_category ?? "Category not recorded") + " · " + String(record.source ?? "dvir") : queueOrigin(record) || "Origin not recorded"}</span></span>
            <span className="maintenance-row-vehicle">{String(record.vehicleCode ?? "Not recorded")}</span>
            <span className="maintenance-row-status"><StatusBadge status={record.status ?? "Open"} /></span>
            <span className="maintenance-row-meta">{kind === "defect" ? fmtDate(record.createdAt ?? record.created_at) : String(record.assignedToName ?? "Unassigned")}</span>
          </button></div>)}
        </div>
        {!visible.length && <p className="maintenance-empty">{rows.length ? "No records match these filters. Clear filters to see the loaded queue." : kind === "defect" ? "No defect records loaded." : "No work orders loaded."}</p>}
      </div>
      {!preview && <div className="maintenance-pagination"><span role="status">{filtered.length} matching · {visible.length} shown{kind === "work-order" ? " · up to 50 loaded" : ""}</span><div><button type="button" className="btn-ghost btn-compact" aria-label="Previous maintenance page" disabled={currentPage <= 1} onClick={() => { setPage(currentPage - 1); setSelectedId(null); }}><ChevronLeft className="h-4 w-4" /></button><span>Page {currentPage} of {pageCount}</span><button type="button" className="btn-ghost btn-compact" aria-label="Next maintenance page" disabled={currentPage >= pageCount} onClick={() => { setPage(currentPage + 1); setSelectedId(null); }}><ChevronRight className="h-4 w-4" /></button></div></div>}
    </section>
    {!preview && <aside className="maintenance-desktop-inspector maintenance-queue-frame">{inspector || <p className="maintenance-empty">Choose a record to inspect its context and available actions.</p>}</aside>}
    {drawerOpen && <div className="maintenance-mobile-overlay" onClick={closeDrawer}><aside ref={drawerRef} className="maintenance-mobile-inspector" role="dialog" aria-modal="true" aria-label={`${kind === "defect" ? "Defect" : "Work order"} details`} onClick={(event) => event.stopPropagation()}><button type="button" className="btn-ghost maintenance-back" onClick={closeDrawer}><ArrowLeft className="h-4 w-4" /> Back to queue</button>{inspector}</aside></div>}
  </div>;
}

function MaintenanceInspector({ record, kind, canManage, canClose, actionPending, onAck, onResolve, onComplete }: {
  record: AnyRecord;
  kind: MaintenanceQueueKind;
  canManage: boolean;
  canClose: boolean;
  actionPending: boolean;
  onAck?: (record: AnyRecord) => void;
  onResolve: (record: AnyRecord) => void;
  onComplete: (record: AnyRecord) => void;
}) {
  const status = String(record.status ?? "Open").toLowerCase();
  const origin = queueOrigin(record);
  const cost = (value: unknown) => value == null || value === "" || !Number.isFinite(Number(value)) ? "Not recorded" : `${Number(value).toLocaleString()}${record.currency ? ` ${String(record.currency)}` : " · currency not recorded"}`;
  const notes = [["Description", record.description], ["Notes", record.notes], ["Service notes", record.serviceNotes]].filter(([, value]) => value != null && String(value).trim().length > 0);
  const metadata = kind === "defect" ? [
    ["Vehicle", record.vehicleCode ?? "Not recorded"], ["Category", record.defectCategory ?? record.defect_category ?? "Not recorded"], ["Source", record.source ?? "dvir"], ["Recorded", fmtDateTime(record.createdAt ?? record.created_at)], ["Out of service", Boolean(record.outOfService ?? record.out_of_service) ? "Yes" : "No"],
  ] : [
    ["Vehicle", record.vehicleCode ?? "Not recorded"], ["Assigned", record.assignedToName ?? "Unassigned"], ["Recorded", fmtDateTime(record.createdAt ?? record.created_at)], ["Scheduled", fmtDate(record.scheduledAt ?? record.dueDate ?? record.dueAt ?? record.due_date)], ["Estimated cost", cost(record.estimatedCost)], ["Actual cost", cost(record.actualCost)],
  ];
  return <div className="maintenance-inspector-content">
    <div className="maintenance-inspector-badges"><RiskBadge risk={kind === "defect" ? record.severity ?? "Minor" : record.priority} /><StatusBadge status={record.status ?? "Open"} />{origin && <span className="maintenance-origin">{origin}</span>}</div>
    <h3>{queueTitle(record, kind)}</h3><p className="maintenance-record-code">{queueCode(record, kind)}</p>
    <div className="maintenance-record-actions">
      {kind === "defect" && canManage && status === "open" && <button type="button" className="btn-primary btn-compact" disabled={actionPending} aria-busy={actionPending} onClick={() => onAck?.(record)}>{actionPending ? "Acknowledging…" : "Acknowledge"}</button>}
      {kind === "defect" && canClose && !["resolved", "rejected"].includes(status) && <button type="button" className="btn-ghost btn-compact" onClick={() => onResolve(record)}>Resolve defect</button>}
      {kind === "work-order" && canClose && !["completed", "cancelled"].includes(status) && <button type="button" className="btn-primary btn-compact" onClick={() => onComplete(record)}>Complete work order</button>}
    </div>
    <dl className="maintenance-metadata">{metadata.map(([label, value]) => <div key={String(label)}><dt>{String(label)}</dt><dd>{String(value)}</dd></div>)}</dl>
    {notes.length > 0 && <details className="maintenance-disclosure" open><summary>Recorded notes</summary>{notes.map(([label, value]) => <div key={String(label)}><strong>{String(label)}</strong><p>{String(value)}</p></div>)}</details>}
    <p className="maintenance-source-note">{kind === "defect" ? "Resolving a defect does not release the vehicle. Repair certification and driver acknowledgment remain required." : "Completion requires actual cost and service notes. Recorded origin remains distinct from verified repair evidence."}</p>
  </div>;
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

type DiagnosticVerificationType = "technician_scan" | "provider_diagnostic" | "service_record";

function ResolveDiagnosticHoldDialog({ hold, pending, error, onClose, onSubmit }: {
  hold: AnyRecord;
  pending: boolean;
  error: string | null;
  onClose: () => void;
  onSubmit: (resolutionNote: string, verificationType: DiagnosticVerificationType, evidenceReference: string) => void;
}) {
  const dialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [resolutionNote, setResolutionNote] = useState("");
  const [verificationType, setVerificationType] = useState<DiagnosticVerificationType>("technician_scan");
  const [evidenceReference, setEvidenceReference] = useState("");
  const [validation, setValidation] = useState<string | null>(null);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (resolutionNote.trim().length < 3)
      return setValidation("Add resolution notes describing the verified repair or diagnostic outcome.");
    if (evidenceReference.trim().length < 2)
      return setValidation("Add the technician scan, provider diagnostic, or service record reference.");
    setValidation(null);
    onSubmit(resolutionNote.trim(), verificationType, evidenceReference.trim());
  };
  return <div ref={dialogRef} className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" role="alertdialog" aria-modal="true" aria-labelledby="resolve-diagnostic-hold-title" aria-describedby="resolve-diagnostic-hold-description">
    <form className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl" onSubmit={submit} noValidate>
      <div className="flex items-start justify-between gap-4"><div>
        <h2 id="resolve-diagnostic-hold-title" className="text-xl font-semibold text-slate-950">Resolve diagnostic hold</h2>
        <p id="resolve-diagnostic-hold-description" className="mt-1 text-sm text-slate-600">Record verification evidence for {String(hold.vehicleCode ?? "this vehicle")}. Resolution re-evaluates availability against all remaining blockers.</p>
      </div><button type="button" className="icon-btn" onClick={onClose} disabled={pending} aria-label="Close diagnostic hold resolution dialog"><X className="h-5 w-5" /></button></div>
      {(validation || error) && <p role="alert" className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">{validation || error}</p>}
      <label className="field-label mt-5 block">Verification type <span aria-hidden="true">*</span>
        <select autoFocus className="input mt-1" value={verificationType} onChange={(event) => setVerificationType(event.target.value as DiagnosticVerificationType)} required>
          <option value="technician_scan">Technician scan</option>
          <option value="provider_diagnostic">Provider diagnostic</option>
          <option value="service_record">Service record</option>
        </select>
      </label>
      <label className="field-label mt-4 block">Evidence reference <span aria-hidden="true">*</span>
        <input className="input mt-1" value={evidenceReference} onChange={(event) => setEvidenceReference(event.target.value)} maxLength={500} placeholder="Scan report, provider case, or service record reference" required />
      </label>
      <label className="field-label mt-4 block">Resolution notes <span aria-hidden="true">*</span>
        <textarea className="input mt-1 min-h-28" value={resolutionNote} onChange={(event) => setResolutionNote(event.target.value)} maxLength={2000} required />
      </label>
      <div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose} disabled={pending}>Cancel</button><button type="submit" className="btn-primary" disabled={pending} aria-busy={pending}>{pending ? "Resolving…" : "Resolve with evidence"}</button></div>
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

function fmtDateTime(val: unknown): string {
  if (!val) return "—";
  const date = new Date(String(val));
  return Number.isNaN(date.getTime()) ? String(val) : date.toLocaleString();
}
