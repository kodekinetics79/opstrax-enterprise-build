import { FormEvent, ReactNode, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ClipboardCheck, Download, FilePlus2, Hammer, PenTool, Plus, Sparkles, Wrench, X } from "lucide-react";
import { AiInsightCard, DataTable, EmptyState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv, labelize } from "@/components/ui";
import { useAuth } from "@/hooks/useAuth";
import {
  useDocumentDetail,
  useDocuments,
  useDocumentSummary,
  useDvirDetail,
  useDvirReports,
  useDvirSummary,
  useMaintenance,
  useMaintenanceDetail,
  useMaintenanceSummary,
  useWorkOrderDetail,
  useWorkOrders,
  useWorkOrderSummary,
} from "@/hooks/useBatch3";
import { documentsApi } from "@/services/documentsApi";
import { dvirApi } from "@/services/dvirApi";
import { maintenanceApi } from "@/services/maintenanceApi";
import { workOrdersApi } from "@/services/workOrdersApi";
import type { AnyRecord } from "@/types";

type Batch3Kind = "maintenance" | "work-orders" | "dvir" | "documents";

const configs = {
  maintenance: {
    queryKey: "maintenance",
    eyebrow: "Maintenance",
    title: "Maintenance reliability center",
    description: "Preventive maintenance, due/overdue service, downtime exposure, service-cost leakage and AI work-order conversion for OpsTrax fleet reliability.",
    icon: <Wrench />,
    useRows: useMaintenance,
    useSummary: useMaintenanceSummary,
    useDetail: useMaintenanceDetail,
    api: maintenanceApi,
    idLabel: "Maintenance ID",
    createLabel: "Create Schedule",
    kpis: [
      ["Maintenance Due", "maintenanceDue"], ["Overdue Services", "overdueServices"], ["Critical Maintenance", "criticalMaintenance"], ["Vehicles Out of Service", "vehiclesOutOfService"],
      ["Average Downtime", "averageDowntime"], ["PM Compliance %", "pmCompliance"], ["Open Work Orders", "openWorkOrders"], ["Estimated Cost", "estimatedMaintenanceCost"],
      ["Repeat Issues", "repeatIssues"], ["Service Due This Week", "serviceDueThisWeek"], ["Asset Maintenance Due", "assetMaintenanceDue"], ["Readiness Score", "maintenanceReadinessScore"],
    ],
    columns: ["id", "vehicleCode", "assetName", "serviceType", "dueDate", "dueOdometer", "dueEngineHours", "priority", "status", "estimatedCost", "maintenanceRiskHeatScore", "recommendedAction"],
    fields: [["vehicleId", "Vehicle ID"], ["assetId", "Asset ID"], ["serviceType", "Service Type"], ["description", "Description"], ["priority", "Priority"], ["status", "Status"], ["dueDate", "Due Date"], ["dueOdometer", "Odometer Due"], ["dueEngineHours", "Engine Hours Due"], ["estimatedCost", "Estimated Cost"], ["riskScore", "Risk Score"], ["recommendedAction", "Recommended Action"]],
    actions: ["schedule", "defer", "createWorkOrder"],
    detailSections: [["Linked Work Orders", "workOrders", ["workOrderNumber", "issueType", "priority", "status", "estimatedCost"]], ["Service Schedules", "schedules", ["serviceType", "triggerType", "nextDueDate", "nextDueOdometer", "status"]]],
  },
  "work-orders": {
    queryKey: "workorders",
    eyebrow: "Work Orders",
    title: "Repair execution board",
    description: "Control work order priority, labor, parts, vendor SLA, downtime impact, cost approval and AI repair summaries across the maintenance operation.",
    icon: <Hammer />,
    useRows: useWorkOrders,
    useSummary: useWorkOrderSummary,
    useDetail: useWorkOrderDetail,
    api: workOrdersApi,
    idLabel: "Work Order #",
    createLabel: "Create Work Order",
    kpis: [
      ["Open Work Orders", "openWorkOrders"], ["Critical Work Orders", "criticalWorkOrders"], ["In Progress", "inProgress"], ["Waiting Parts", "waitingParts"],
      ["Waiting Approval", "waitingApproval"], ["Completed This Week", "completedThisWeek"], ["Avg Resolution", "averageResolutionTime"], ["Estimated Cost", "totalEstimatedCost"],
      ["Approved Cost", "totalApprovedCost"], ["Repeat Repairs", "repeatRepairs"], ["Vehicles Down", "vehiclesDown"], ["Vendor SLA Risk", "vendorSlaRisk"],
    ],
    columns: ["workOrderNumber", "vehicleCode", "assetName", "issueType", "priority", "status", "assignedToName", "vendorName", "createdDate", "dueDate", "estimatedCost", "approvedCost", "downtimeHours", "repairPriorityScore", "recommendedAction"],
    fields: [["workOrderNumber", "Work Order Number"], ["vehicleId", "Vehicle ID"], ["assetId", "Asset ID"], ["maintenanceItemId", "Maintenance Item ID"], ["dvirReportId", "DVIR Report ID"], ["issueType", "Issue Type"], ["title", "Title"], ["description", "Description"], ["priority", "Priority"], ["status", "Status"], ["assignedToUserId", "Assigned User ID"], ["vendorName", "Vendor / Shop"], ["dueDate", "Due Date"], ["estimatedCost", "Estimated Cost"], ["approvedCost", "Approved Cost"], ["downtimeHours", "Downtime Hours"]],
    actions: ["assign", "status", "labor", "part", "approveCost", "complete"],
    detailSections: [["Labor", "labor", ["technicianName", "laborHours", "laborRate", "totalCost", "notes"]], ["Parts", "parts", ["partName", "partNumber", "quantity", "unitCost", "status"]], ["Photos / Documents", "documents", ["documentNumber", "documentType", "status", "expiresAt"]]],
  },
  dvir: {
    queryKey: "dvir",
    eyebrow: "DVIR / Inspections",
    title: "Inspection compliance workflow",
    description: "Pre-trip and post-trip inspections, defect severity, mechanic review, repair certification, signature status and unsafe-vehicle recommendations.",
    icon: <ClipboardCheck />,
    useRows: useDvirReports,
    useSummary: useDvirSummary,
    useDetail: useDvirDetail,
    api: dvirApi,
    idLabel: "Report #",
    createLabel: "Create Inspection",
    kpis: [
      ["Inspections Today", "inspectionsToday"], ["Pre-Trip Completed", "preTripCompleted"], ["Post-Trip Completed", "postTripCompleted"], ["Defects Found", "defectsFound"],
      ["Unsafe Vehicles", "unsafeVehicles"], ["Mechanic Review", "pendingMechanicReview"], ["Repair Certification", "pendingRepairCertification"], ["Missing Signatures", "missingDriverSignatures"],
      ["DVIR Compliance", "dvirCompliance"], ["Repeat Defects", "repeatDefects"], ["Critical Defects", "criticalDefects"], ["Work Orders Created", "workOrdersCreated"],
    ],
    columns: ["reportNumber", "vehicleCode", "driverName", "inspectionType", "submittedAt", "inspectionStatus", "defectsFound", "safeToOperate", "mechanicReviewStatus", "repairCertificationStatus", "driverSignatureStatus", "countryCode", "defectSeverityScore", "recommendedAction"],
    fields: [["reportNumber", "Report Number"], ["driverId", "Driver ID"], ["vehicleId", "Vehicle ID"], ["countryCode", "Country/Profile"], ["inspectionType", "Inspection Type"], ["inspectionStatus", "Status"], ["defectsFound", "Defects Found"], ["safeToOperate", "Safe To Operate"], ["driverSignatureStatus", "Driver Signature"], ["mechanicReviewStatus", "Mechanic Review"], ["repairCertificationStatus", "Repair Certification"], ["riskScore", "Risk Score"], ["recommendedAction", "Recommended Action"], ["notes", "Notes"]],
    actions: ["mechanicReview", "certifyRepair", "driverSign"],
    detailSections: [["Defects Found", "defects", ["defectCategory", "defectDescription", "severity", "status", "linkedWorkOrderId"]], ["Inspection Checklist", "checklist", ["itemLabel", "itemCategory", "required", "status"]], ["Linked Work Orders", "workOrders", ["workOrderNumber", "status", "priority", "estimatedCost"]]],
  },
  documents: {
    queryKey: "documents",
    eyebrow: "Documents",
    title: "Compliance document vault",
    description: "Document lifecycle control for vehicles, drivers, assets, contracts, work orders, inspections and compliance packages with expiry intelligence.",
    icon: <FilePlus2 />,
    useRows: useDocuments,
    useSummary: useDocumentSummary,
    useDetail: useDocumentDetail,
    api: documentsApi,
    idLabel: "Document #",
    createLabel: "Upload Document",
    kpis: [
      ["Total Documents", "totalDocuments"], ["Expiring Soon", "expiringSoon"], ["Expired", "expired"], ["Missing Critical", "missingCriticalDocuments"],
      ["Vehicle Documents", "vehicleDocuments"], ["Driver Documents", "driverDocuments"], ["Compliance Docs", "complianceDocuments"], ["Pending Renewal", "pendingRenewal"],
      ["Uploaded This Month", "uploadedThisMonth"], ["Audit Package Docs", "auditPackageDocuments"], ["Cross-Border Gaps", "crossBorderMissingDocs"], ["Completeness", "dataCompletenessScore"],
    ],
    columns: ["documentNumber", "documentType", "category", "entityType", "entityName", "countryCode", "issuingAuthority", "issuedAt", "expiresAt", "status", "renewalStatus", "documentExpiryRiskScore", "recommendedAction"],
    fields: [["title", "Title"], ["documentNumber", "Document Number"], ["entityType", "Entity Type"], ["entityId", "Entity ID"], ["documentType", "Document Type"], ["category", "Category"], ["countryCode", "Country"], ["issuingAuthority", "Issuing Authority"], ["issuedAt", "Issued At"], ["expiresAt", "Expires At"], ["status", "Status"], ["renewalStatus", "Renewal Status"], ["riskScore", "Risk Score"], ["recommendedAction", "Recommended Action"], ["notes", "Notes"]],
    actions: ["renew", "upload"],
    detailSections: [["Timeline", "timeline", ["eventTitle", "eventDescription", "occurredAt"]]],
  },
} satisfies Record<Batch3Kind, {
  queryKey: string;
  eyebrow: string;
  title: string;
  description: string;
  icon: ReactNode;
  useRows: () => { data?: AnyRecord[]; isLoading: boolean };
  useSummary: () => { data?: AnyRecord };
  useDetail: (id?: string | number) => { data?: AnyRecord; isLoading: boolean };
  api: AnyRecord;
  idLabel: string;
  createLabel: string;
  kpis: string[][];
  columns: string[];
  fields: string[][];
  actions: string[];
  detailSections: [string, string, string[]][];
}>;

function getBatch3Permission(kind: Batch3Kind, action: string) {
  if (kind === "maintenance") {
    if (action === "schedule" || action === "createWorkOrder") return "maintenance:create";
    if (action === "defer") return "maintenance:update";
    if (action === "complete") return "maintenance:close";
    return "maintenance:update";
  }
  if (kind === "work-orders") {
    if (action === "complete") return "maintenance:close";
    if (action === "assign" || action === "status" || action === "labor" || action === "part" || action === "approveCost") return "maintenance:update";
    return "maintenance:view";
  }
  if (kind === "dvir") {
    if (action === "mechanicReview" || action === "certifyRepair") return "maintenance:update";
    return "maintenance:view";
  }
  if (action === "renew" || action === "upload") return "maintenance:update";
  return "maintenance:view";
}

export function Batch3OperationsPage({ kind }: { kind: Batch3Kind }) {
  const config = configs[kind];
  const { session } = useAuth();
  const exportPermission = kind === "documents" ? "maintenance:update" : "maintenance:view";
  
  const hasPermission = (perm: string) => {
    if (!session?.permissions) return false;
    return session.permissions.includes("*") || session.permissions.includes(perm);
  };

  const rowsQuery = config.useRows();
  const summary = config.useSummary();
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("All");
  const detail = config.useDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();
  const invalidate = async () => {
    await qc.invalidateQueries({ queryKey: [config.queryKey] });
    await qc.invalidateQueries({ queryKey: [config.queryKey, "summary"] });
    if (selected?.id) await qc.invalidateQueries({ queryKey: [config.queryKey, "detail", selected.id] });
  };

  const save = useMutation({
    mutationFn: (payload: AnyRecord) => payload.id ? config.api.update(payload.id as string | number, payload) : config.api.create(payload),
    onSuccess: async () => { setEditing(null); await invalidate(); },
  });

  const action = useMutation({
    mutationFn: ({ type, row }: { type: string; row: AnyRecord }) => runAction(kind, type, row),
    onSuccess: invalidate,
  });

  const statusLower = status.toLowerCase();

  const rows = useMemo(() => (rowsQuery.data || []).filter((row) => {
    // Expanded search fields to ensure searching by driver, customer or document entity feels responsive
    const searchLower = search.toLowerCase();
    const matchesSearch = !search || 
      String(row.vehicleCode || "").toLowerCase().includes(searchLower) ||
      String(row.driverName || "").toLowerCase().includes(searchLower) ||
      String(row.customerName || row.entityName || "").toLowerCase().includes(searchLower) ||
      String(row.assetName || "").toLowerCase().includes(searchLower) ||
      String(row.serviceType || row.category || row.issueType || row.inspectionType || "").toLowerCase().includes(searchLower) ||
      String(row.vendorName || row.assignedToName || "").toLowerCase().includes(searchLower) ||
      String(row.workOrderNumber || row.reportNumber || row.documentNumber || "").toLowerCase().includes(searchLower) ||
      String(row.title || row.description || "").toLowerCase().includes(searchLower);

    // Intelligent status filtering: "Active" should surface items needing attention (not closed/completed)
    const rowStatus = String(row.status || row.inspectionStatus || row.renewalStatus || "").toLowerCase();
    const matchesStatus = status === "All" || 
      (status === "Active" ? !/closed|completed|expired|resolved/i.test(rowStatus) : rowStatus.includes(statusLower));

    return matchesSearch && matchesStatus;
  }), [rowsQuery.data, search, status, statusLower]);

  if (rowsQuery.isLoading || summary.isLoading) return <LoadingState />;
  if (rowsQuery.isError || summary.isError) {
    return <EmptyState title={`${config.eyebrow} unavailable`} subtitle="Unable to load maintenance records right now. Refresh to try again." />;
  }
  const s = (summary.data || {}) as AnyRecord;

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto">
      <PageHeader
        eyebrow={config.eyebrow}
        title={config.title}
        description={config.description}
        actions={
          <>
            <button
              className="btn-primary"
              disabled={!hasPermission(kind === "work-orders" || kind === "maintenance" ? "maintenance:create" : "maintenance:update")}
              title={!hasPermission(kind === "work-orders" || kind === "maintenance" ? "maintenance:create" : "maintenance:update") ? "You do not have permission to perform this action." : `Create a new ${config.eyebrow.toLowerCase()} record.`}
              onClick={() => setEditing(defaultForm(kind))}
            >
              <Plus className="h-4 w-4" /> {config.createLabel}
            </button>
            <button
              className="btn-ghost"
              disabled={!hasPermission(exportPermission)}
              title={!hasPermission(exportPermission) ? "You do not have permission to perform this action." : "Export the current filtered records."}
              onClick={() => hasPermission(exportPermission) && exportCsv(kind, rows)}
            >
              <Download className="h-4 w-4" /> Export Report
            </button>
          </>
        }
      />
      <div className="grid gap-4 md:grid-cols-4 xl:grid-cols-6">
        {config.kpis.map(([label, key]) => <KpiCard key={key} label={label} value={String(s[key] ?? 0)} icon={config.icon} status={/overdue|critical|unsafe|expired|missing|risk/i.test(label) ? "Review" : "Active"} />)}
      </div>
      <div className="panel flex flex-col gap-3 p-4 xl:flex-row xl:items-center">
        <input className="field xl:max-w-md" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={`Search ${config.eyebrow.toLowerCase()} by entity, status, country, vendor, risk...`} />
        <select className="field xl:max-w-[180px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option>All</option><option>Open</option><option>Active</option><option>Scheduled</option><option>In Progress</option><option>Pending</option><option>Overdue</option><option>Expired</option><option>Completed</option>
        </select>
        <span className="badge"><Sparkles className="h-3.5 w-3.5" /> AI recommendations active</span>
      </div>
      {!rows.length ? (
        <EmptyState title={`No ${config.eyebrow.toLowerCase()} records`} subtitle="Try another filter or create the first record." />
      ) : (
        <DataTable rows={rows} columns={config.columns} onSelect={setSelected} />
      )}
      <DetailDrawer
        kind={kind}
        config={config}
        detail={detail.data}
        permissions={session?.permissions || []}
        canExport={hasPermission(exportPermission)}
        loading={detail.isLoading}
        onClose={() => setSelected(null)}
        onEdit={(record) => setEditing(record)}
        onAction={(type, row) => action.mutate({ type, row })}
      />
      {editing ? <RecordModal title={config.createLabel} fields={config.fields} initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(payload) => save.mutate(payload)} /> : null}
    </div>
  );
}

function DetailDrawer({ kind, config, detail, loading, onClose, onEdit, onAction, permissions, canExport }: { kind: Batch3Kind; config: (typeof configs)[Batch3Kind]; detail?: AnyRecord; loading: boolean; onClose: () => void; onEdit: (record: AnyRecord) => void; onAction: (type: string, row: AnyRecord) => void; permissions: string[]; canExport: boolean }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;

  const hasPermission = (perm: string) => permissions.includes("*") || permissions.includes(perm);
  const recommendations = ((detail?.recommendations as AnyRecord[]) || []).slice(0, 4);
  
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm">
      <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6">
        <button className="float-right icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        <p className="section-title text-teal-300">{config.eyebrow} Detail</p>
        <h2 className="mt-3 text-2xl font-semibold text-white">{String(record.documentNumber || record.reportNumber || record.workOrderNumber || record.serviceType || record.title || `Record ${record.id}`)}</h2>
        <div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status || record.inspectionStatus || record.renewalStatus} /><RiskBadge risk={record.priority || record.riskScore || record.documentExpiryRiskScore || record.defectSeverityScore} /><span className="badge">{config.idLabel}: {String(record.id)}</span></div>
        <div className="mt-5 flex flex-wrap gap-3">
          <button
            className="btn-primary"
            disabled={!hasPermission(getBatch3Permission(kind, "update"))}
            title={!hasPermission(getBatch3Permission(kind, "update")) ? "You do not have permission to perform this action." : "Edit this record."}
            onClick={() => onEdit(record)}
          >
            <PenTool className="h-4 w-4" /> Edit
          </button>
          {config.actions.map((type) => {
            const allowed = hasPermission(getBatch3Permission(kind, type));
            return (
              <button
                key={type}
                className="btn-ghost"
                disabled={!allowed}
                title={!allowed ? "You do not have permission to perform this action." : `Run ${labelize(type)}.`}
                onClick={() => onAction(type, record)}
              >
                {labelize(type)}
              </button>
            );
          })}
          <button
            className="btn-ghost"
            disabled={!canExport}
            title={!canExport ? "You do not have permission to perform this action." : "Export this record."}
            onClick={() => canExport && exportCsv(config.eyebrow, record ? [record] : [])}
          >
            <Download className="h-4 w-4" /> Export Record
          </button>
        </div>
        <div className="mt-6 grid gap-4 lg:grid-cols-3">
          <InfoPanel title="Primary Evidence" record={record} keys={Object.keys(record).slice(0, 12)} />
          <InfoPanel title="Risk & Recommended Action" record={record} keys={["riskScore", "maintenanceRiskHeatScore", "repairPriorityScore", "defectSeverityScore", "documentExpiryRiskScore", "recommendedAction"]} />
          <InfoPanel title="Compliance / Country Ready" record={record} keys={["countryCode", "safeToOperate", "renewalStatus", "costApprovalStatus", "vendorName", "issuingAuthority"]} />
        </div>
        {config.detailSections.map(([title, key, columns]) => <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) || []} columns={columns} />)}
        <Grid title="Timeline / Audit Trail" rows={((detail?.timeline as AnyRecord[]) || (detail?.auditTrail as AnyRecord[]) || []) as AnyRecord[]} columns={["eventTitle", "eventDescription", "actionName", "actorName", "occurredAt", "createdAt"]} />
        <div className="mt-6 grid gap-4 lg:grid-cols-2">{recommendations.map((insight, i) => <AiInsightCard key={String(insight.id || i)} insight={insight} />)}</div>
      </aside>
    </div>
  );
}

function RecordModal({ title, fields, initial, saving, onClose, onSave }: { title: string; fields: string[][]; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (event: FormEvent) => { event.preventDefault(); onSave(form); };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4">
      <form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}>
        <div className="flex justify-between"><h2 className="text-2xl font-semibold text-slate-900">{form.id ? `Edit ${title}` : title}</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div>
        <div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key, label]) => <label key={key}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span><input className="field" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} /></label>)}</div>
        <div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving}>Save</button></div>
      </form>
    </div>
  );
}

function InfoPanel({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2">{keys.map((key) => <p key={key} className="text-sm text-slate-300"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[720px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-white/10">{rows.slice(0, 10).map((row, i) => <tr key={String(row.id || i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-300">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}

function defaultForm(kind: Batch3Kind): AnyRecord {
  if (kind === "maintenance") return { serviceType: "PM-A Service", priority: "High", status: "Open", dueDate: new Date().toISOString().slice(0, 10), estimatedCost: "" };
  if (kind === "work-orders") return { workOrderNumber: `WO-NEW-${Date.now()}`, issueType: "Preventive", title: "", priority: "High", status: "Open", vendorName: "", estimatedCost: "" };
  if (kind === "dvir") return { reportNumber: `DVIR-NEW-${Date.now()}`, countryCode: "US", inspectionType: "Pre-Trip", inspectionStatus: "Submitted", defectsFound: 0, safeToOperate: true, driverSignatureStatus: "Pending", mechanicReviewStatus: "Pending", repairCertificationStatus: "Pending" };
  return { title: "", documentNumber: `DOC-NEW-${Date.now()}`, entityType: "vehicle", documentType: "Insurance", category: "Insurance", countryCode: "US", status: "Active", renewalStatus: "Current", issuedAt: new Date().toISOString().slice(0, 10), expiresAt: new Date(Date.now() + 45 * 86400000).toISOString().slice(0, 10) };
}

async function runAction(kind: Batch3Kind, type: string, row: AnyRecord) {
  const id = row.id as string | number;
  if (kind === "maintenance") {
    if (type === "schedule") return maintenanceApi.schedule(id);
    if (type === "defer") return maintenanceApi.defer(id);
    return maintenanceApi.createWorkOrder({ vehicleId: Number(id) });
  }
  if (kind === "work-orders") {
    if (type === "assign") return workOrdersApi.assign(id, {});
    if (type === "status") return workOrdersApi.status(id, { status: "In Progress" });
    if (type === "labor") return workOrdersApi.addLabor(id, { technicianName: "Technician", laborHours: 1.0, laborRate: 95 });
    if (type === "part") return workOrdersApi.addPart(id, { partName: "Repair part", quantity: 1, unitCost: 75 });
    if (type === "approveCost") return workOrdersApi.approveCost(id, { approvedCost: row.estimatedCost });
    return workOrdersApi.complete(id);
  }
  if (kind === "dvir") {
    if (type === "mechanicReview") return dvirApi.mechanicReview(id);
    if (type === "certifyRepair") return dvirApi.certifyRepair(id);
    return dvirApi.driverSign(id);
  }
  if (type === "renew") return documentsApi.renewPlaceholder(id);
  return documentsApi.uploadPlaceholder(defaultForm("documents"));
}

