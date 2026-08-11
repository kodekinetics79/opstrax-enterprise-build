import { FormEvent, ReactNode, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, FileVideo, Gavel, PackageCheck, PenTool, Plus, ShieldAlert, UserCheck, X } from "lucide-react";
import { AiInsightCard, DataTable, EmptyState, KpiCard, LoadingState, RiskBadge, StatusBadge, PageHeader, exportCsv, labelize } from "@/components/ui";
import { useCoachingSummary, useCoachingTaskDetail, useCoachingTasks, useDashcamEventDetail, useDashcamEvents, useDashcamSummary, useEvidencePackageDetail, useEvidencePackages, useEvidenceSummary, useIncidentDetail, useIncidents, useIncidentsSummary, useSafetyEventDetail, useSafetyEvents, useSafetySummary } from "@/hooks/useBatch4";
import { useHasDirectPermission, useHasPermission } from "@/hooks/usePermission";
import { useSingleFlight } from "@/hooks/useSingleFlight";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { coachingApi } from "@/services/coachingApi";
import { dashcamApi } from "@/services/dashcamApi";
import { evidenceApi } from "@/services/evidenceApi";
import { incidentsApi } from "@/services/incidentsApi";
import { safetyApi } from "@/services/safetyApi";
import type { AnyRecord } from "@/types";

type Kind = "safety" | "dashcam" | "coaching" | "incidents" | "evidence";

const configs = {
  safety: {
    queryKey: "safety", eyebrow: "Safety", title: "Fleet Safety Review Center", icon: <ShieldAlert />,
    description: "Review safety events, driver risk, vehicle risk, trends, coaching queues and incident predictors from connected OpsTrax data.",
    useRows: useSafetyEvents, useSummary: useSafetySummary, useDetail: useSafetyEventDetail, api: safetyApi, createLabel: "Create Safety Event",
    kpis: [["Fleet Safety Score","fleetSafetyScore"],["Events Today","safetyEventsToday"],["Critical Events","criticalEvents"],["Harsh Braking","harshBraking"],["Harsh Acceleration","harshAcceleration"],["Speeding","speedingEvents"],["Route Deviation","routeDeviation"],["Distracted Driving","distractedDrivingEvents"],["Coaching Needed","coachingNeeded"],["Open Incidents","openIncidents"],["Reviewed Events","reviewedEvents"],["Preventable Risk","preventableRiskScore"]],
    columns: ["eventNumber","eventType","severity","driverName","vehicleCode","jobNumber","routeCode","locationDescription","speed","occurredAt","reviewStatus","coachingStatus","incidentStatus","riskScore","recommendedAction"],
    fields: [["eventNumber","Event Number"],["eventType","Event Type"],["severity","Severity"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["routeId","Route ID"],["locationDescription","Location/Zone"],["speed","Speed"],["postedSpeedLimit","Posted Limit"],["reviewStatus","Review Status"],["riskScore","Risk Score"],["aiSummary","Event Summary"],["recommendedAction","Recommended Action"]],
    actions: ["review","dismiss","resolve","createCoaching","createIncident"],
    sections: [["Dashcam Events","dashcamEvents",["eventNumber","eventType","severity","reviewStatus","evidenceStatus"]],["Coaching Queue","coachingTasks",["taskNumber","coachingType","priority","status","dueAt"]],["Incident Watch","incidents",["incidentNumber","incidentType","severity","status"]]],
  },
  dashcam: {
    queryKey: "dashcam", eyebrow: "Dashcam / Incident Review", title: "Video Event Inbox", icon: <FileVideo />,
    description: "Review video event metadata, event summaries, road and driver clip slots, false positives, coaching, evidence packages and insurance exports.",
    useRows: useDashcamEvents, useSummary: useDashcamSummary, useDetail: useDashcamEventDetail, api: dashcamApi, createLabel: "Record Event Metadata",
    kpis: [["Dashcam Events Today","dashcamEventsToday"],["Critical Video Events","criticalVideoEvents"],["Pending Review","pendingReview"],["Reviewed Events","reviewedEvents"],["False Positives","falsePositives"],["Coaching Created","coachingCreated"],["Evidence Packages","evidencePackages"],["Collision/Near Miss","collisionNearMiss"],["Distracted Driving","distractedDrivingEvents"],["Tailgating","tailgatingEvents"],["Speeding Video","speedingVideoEvents"],["Driver Exoneration","driverExonerations"]],
    columns: ["eventNumber","eventType","severity","driverName","vehicleCode","jobNumber","routeCode","locationDescription","occurredAt","videoProvider","aiConfidence","reviewStatus","evidenceStatus","recommendedAction"],
    fields: [["eventNumber","Event Number"],["safetyEventId","Safety Event ID"],["eventType","Event Type"],["title","Title"],["severity","Severity"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["routeId","Route ID"],["locationDescription","Location"],["aiSummary","Event Summary"],["aiConfidence","Detection Confidence"],["reviewStatus","Review Status"],["evidenceStatus","Evidence Status"],["recommendedAction","Recommended Action"]],
    actions: ["review","falsePositive","createCoaching","createEvidencePackage","createIncidentReport"],
    sections: [["Coaching From Video", "coachingTasks", ["taskNumber","priority","status","aiScript"]],["Evidence Packages","evidencePackages",["packageNumber","status","locked","exportUrl"]]],
  },
  coaching: {
    queryKey: "coaching", eyebrow: "Driver Coaching", title: "Driver coaching queue", icon: <UserCheck />,
    description: "Assign, acknowledge, complete and document coaching with structured scripts, before/after safety trends and repeat behavior detection.",
    useRows: useCoachingTasks, useSummary: useCoachingSummary, useDetail: useCoachingTaskDetail, api: coachingApi, createLabel: "Create Coaching Task",
    kpis: [["Open Coaching","openCoachingTasks"],["Critical Coaching","criticalCoaching"],["Assigned Tasks","assignedTasks"],["Driver Acknowledged","driverAcknowledged"],["Completed This Month","completedThisMonth"],["Overdue Coaching","overdueCoaching"],["Repeat Drivers","repeatCoachingDrivers"],["Safety Improved","safetyScoreImproved"],["Escalated","escalatedCoaching"],["Avg Completion","averageCompletionTime"]],
    columns: ["taskNumber","driverName","coachingType","priority","status","assignedToName","driverAcknowledged","beforeSafetyScore","afterSafetyScore","effectivenessScore","dueAt"],
    fields: [["driverId","Driver ID"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["assignedToUserId","Assigned User ID"],["coachingType","Coaching Type"],["priority","Priority"],["title","Title"],["description","Description"],["aiScript","Coaching Script"],["dueAt","Due At"]],
    actions: ["assign","complete","addNote"],
    sections: [["Coaching Notes","notes",["noteType","noteText","createdAt"]],["Related Safety Events","relatedSafetyEvents",["eventNumber","eventType","severity","reviewStatus"]],["Related Dashcam Events","relatedDashcamEvents",["eventNumber","eventType","severity","reviewStatus"]]],
  },
  incidents: {
    queryKey: "incidents", eyebrow: "Incidents", title: "Incident and legal review register", icon: <Gavel />,
    description: "Manage incidents, driver and customer statements, evidence, insurance report drafts and chain-of-custody audit trails.",
    useRows: useIncidents, useSummary: useIncidentsSummary, useDetail: useIncidentDetail, api: incidentsApi, createLabel: "Create Incident",
    kpis: [["Total Incidents","totalIncidents"],["Open Incidents","openIncidents"],["Closed Incidents","closedIncidents"],["Critical Incidents","criticalIncidents"],["Awaiting Statement","awaitingDriverStatement"],["Insurance Drafts","insuranceReports"],["Evidence Collected","evidenceCollected"]],
    columns: ["incidentNumber","incidentType","severity","status","driverName","vehicleCode","jobNumber","routeCode","locationDescription","occurredAt","insuranceReportStatus","recommendedAction"],
    fields: [["incidentNumber","Incident Number"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["routeId","Route ID"],["incidentType","Incident Type"],["severity","Severity"],["occurredAt","Occurred Date / Time"],["locationDescription","Location"],["aiSummary","Incident Summary"],["driverStatement","Driver Statement"],["witnessStatement","Witness Statement"],["customerStatement","Customer Statement"],["recommendedAction","Recommended Action"]],
    actions: ["status","attachEvidence","createInsuranceReport"],
    sections: [["Evidence List","evidence",["evidenceType","evidenceTitle","evidenceUrl","contentHash","verificationStatus","custodyStatus","retrievalStatus","createdAt"]],["Evidence Packages","packages",["packageNumber","status","locked","exportUrl"]],["Insurance Reports","insuranceReports",["reportNumber","status","exportUrl","createdAt"]],["Timeline","timeline",["title","eventType","eventTime"]]],
  },
  evidence: {
    queryKey: "evidence-packages", eyebrow: "Evidence Packages", title: "Insurance evidence package builder", icon: <PackageCheck />,
    description: "Bundle video, GPS, speed data, statements, job context, DVIR references, maintenance history and legal export packages.",
    useRows: useEvidencePackages, useSummary: useEvidenceSummary, useDetail: useEvidencePackageDetail, api: evidenceApi, createLabel: "Create Evidence Package",
    kpis: [["Total Packages","totalPackages"],["Draft Packages","draftPackages"],["Export Ready","exportReady"],["Locked Packages","lockedPackages"],["Insurance Packages","insurancePackages"],["Exports Generated","exportsGenerated"]],
    columns: ["packageNumber","incidentNumber","driverName","vehicleCode","safetyEventNumber","dashcamEventNumber","packageType","status","locked","exportUrl","summary"],
    fields: [["incidentId","Incident ID"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["status","Status"],["summary","Summary"]],
    // Server-side PDF packaging is not implemented; keep the real CSV export and
    // custody lock, but do not advertise a generated evidence file.
    actions: ["lock"],
    sections: [["Package Items","items",["itemType","itemTitle","sourceEntityType","sourceEntityId","createdAt"]]],
  },
} satisfies Record<Kind, { queryKey: string; eyebrow: string; title: string; icon: ReactNode; description: string; useRows: () => { data?: AnyRecord[]; isLoading: boolean }; useSummary: () => { data?: AnyRecord }; useDetail: (id?: string | number) => { data?: AnyRecord; isLoading: boolean }; api: AnyRecord; createLabel: string; kpis: string[][]; columns: string[]; fields: string[][]; actions: string[]; sections: [string,string,string[]][] }>;

const ACTION_PERMISSIONS: Record<Kind, Record<string, string>> = {
  safety: {
    create: "safety:create",
    update: "safety:update",
    export: "safety:view",
    review: "safety:review",
    dismiss: "safety:review",
    resolve: "safety:review",
    createCoaching: "safety:update",
    createIncident: "safety:create",
  },
  dashcam: {
    create: "safety:update",
    update: "safety:update",
    export: "safety:evidence:export",
    review: "safety:update",
    falsePositive: "safety:update",
    createCoaching: "safety:update",
    createEvidencePackage: "safety:evidence:view",
    createIncidentReport: "safety:update",
  },
  coaching: {
    create: "safety:update",
    update: "safety:update",
    export: "safety:evidence:export",
    assign: "safety:update",
    complete: "safety:update",
    addNote: "safety:update",
  },
  incidents: {
    create: "safety:create",
    update: "safety:update",
    export: "safety:view",
    status: "safety:update",
    attachEvidence: "safety:update",
    createInsuranceReport: "safety:review",
  },
  evidence: {
    create: "safety:update",
    update: "safety:update",
    export: "safety:evidence:export",
    exportEvidencePackage: "safety:evidence:export",
    lock: "safety:update",
  },
};

export function Batch4SafetyPage({ kind }: { kind: Kind }) {
  const config = configs[kind];
  const hasPermission = useHasPermission();
  const hasDirectPermission = useHasDirectPermission();
  const canMutate = (permission: string) => hasDirectPermission(permission);
  const createPermission = ACTION_PERMISSIONS[kind].create;
  const updatePermission = ACTION_PERMISSIONS[kind].update;
  const exportPermission = ACTION_PERMISSIONS[kind].export;
  const rowsQuery = config.useRows();
  const summary = config.useSummary();
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [incidentAction, setIncidentAction] = useState<{ type: "status" | "attachEvidence"; row: AnyRecord } | null>(null);
  const [coachingNoteAction, setCoachingNoteAction] = useState<AnyRecord | null>(null);
  const [coachingCompleteAction, setCoachingCompleteAction] = useState<AnyRecord | null>(null);
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("All");
  const detail = config.useDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();
  const saveSingleFlight = useSingleFlight();
  const actionSingleFlight = useSingleFlight();
  const invalidate = async () => { await qc.invalidateQueries({ queryKey: [config.queryKey] }); await qc.invalidateQueries({ queryKey: [config.queryKey, "summary"] }); if (selected?.id) await qc.invalidateQueries({ queryKey: [config.queryKey, "detail", selected.id] }); };
  const save = useMutation({ mutationFn: (payload: AnyRecord) => {
    if (!payload.id) return config.api.create(payload);
    if (kind === "coaching" || kind === "incidents") {
      const { status: _workflowStatus, ...safeUpdate } = payload;
      return config.api.update(payload.id as string | number, safeUpdate);
    }
    return config.api.update(payload.id as string | number, payload);
  }, onSuccess: async () => { setEditing(null); await invalidate(); } });
  const action = useMutation({ mutationFn: ({ type, row, payload }: { type: string; row: AnyRecord; payload?: AnyRecord }) => runAction(kind, type, row, payload), onSuccess: async () => { setIncidentAction(null); await invalidate(); } });
  const operationError = save.error || action.error;
  const rows = useMemo(() => (rowsQuery.data || []).filter((row) => {
    const searchLower = search.toLowerCase();
    const filterLower = filter.toLowerCase();
    const matchesSearch = !search || 
      String(row.eventNumber || row.taskNumber || row.incidentNumber || row.packageNumber || "").toLowerCase().includes(searchLower) ||
      String(row.driverName || row.vehicleCode || row.jobNumber || row.routeCode || "").toLowerCase().includes(searchLower) ||
      String(row.eventType || row.coachingType || row.incidentType || "").toLowerCase().includes(searchLower);

    const statusVal = String(row.status || row.reviewStatus || row.severity || "").toLowerCase();
    const matchesFilter = filter === "All" || statusVal.includes(filterLower);
    return matchesSearch && matchesFilter;
  }), [rowsQuery.data, search, filter]);
  if (rowsQuery.isLoading || summary.isLoading) return <LoadingState />;
  if (rowsQuery.isError || summary.isError) {
    return <EmptyState
      title={`${config.eyebrow} unavailable`}
      subtitle="Unable to load live records right now. No empty or healthy state has been inferred."
      action={<button type="button" className="btn-secondary" disabled={rowsQuery.isFetching || summary.isFetching} onClick={() => { void rowsQuery.refetch(); void summary.refetch(); }}>{rowsQuery.isFetching || summary.isFetching ? "Retrying…" : "Retry live data"}</button>}
    />;
  }
  const s = (summary.data || {}) as AnyRecord;
  return <div className="fleet-console space-y-3">
    <PageHeader
      eyebrow={config.eyebrow}
      title={config.title}
      description={config.description}
      actions={
        <>
          <button
            className="btn-primary"
            disabled={!canMutate(createPermission)}
            title={!canMutate(createPermission) ? "You do not have permission to perform this action." : `Create a new ${config.eyebrow.toLowerCase()} record.`}
            onClick={() => setEditing(defaultForm(kind))}
          >
            <Plus className="h-4 w-4" /> {config.createLabel}
          </button>
          <button
            className="btn-ghost"
            disabled={!hasPermission(exportPermission)}
            title={!hasPermission(exportPermission) ? "You do not have permission to perform this action." : "Export the current filtered records."}
            onClick={() => exportCsv(kind, rows)}
          >
            <Download className="h-4 w-4" /> Export Report
          </button>
        </>
      }
    />
    {operationError ? <div role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">{operationError instanceof Error ? operationError.message : "The incident action could not be completed."}</div> : null}
    <div className="grid gap-6 sm:grid-cols-3 xl:grid-cols-5">{config.kpis.slice(0, 5).map(([label,key]) => <KpiCard key={key} label={label} value={String(s[key] ?? 0)} status={/critical|overdue|missing|rejected/i.test(label) ? "Critical" : undefined} />)}</div>
    <div className="flex flex-col gap-3 xl:flex-row xl:items-center"><input className="field xl:max-w-md" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={`Search ${config.eyebrow.toLowerCase()} by driver, vehicle, route, event, status...`} /><select className="field xl:max-w-[180px]" value={filter} onChange={(e) => setFilter(e.target.value)}><option>All</option><option>Critical</option><option>High</option><option>Pending</option><option>Reviewed</option><option>Open</option><option>Closed</option><option>Locked</option></select></div>
    {!rows.length ? (
      <EmptyState title={`No ${config.eyebrow.toLowerCase()} records`} subtitle="Try another filter or create the first record." />
    ) : (
      <DataTable rows={rows} columns={config.columns} onSelect={setSelected} />
    )}
    <Drawer
      config={config}
      detail={detail.data}
      loading={detail.isLoading}
      canUpdate={canMutate(updatePermission) && !(kind === "coaching" && /completed|cancelled/i.test(String((detail.data?.record as AnyRecord | undefined)?.status || selected?.status || "")))}
      canExport={hasPermission(exportPermission)}
      actionPending={action.isPending}
      canRunAction={(type) => {
        const permission = ACTION_PERMISSIONS[kind][type] || updatePermission;
        if (!canMutate(permission)) return false;
        if (kind === "coaching") {
          const status = String((detail.data?.record as AnyRecord | undefined)?.status || selected?.status || "").trim().toLowerCase();
          if (type === "assign" && !["draft", "open", "escalated"].includes(status)) return false;
          if (type === "complete" && !["driver acknowledged", "escalated"].includes(status)) return false;
        }
        if (kind === "incidents" && type === "status") {
          const evidenceReady = ((detail.data?.evidence as AnyRecord[] | undefined) || []).some((item) => /^https:\/\//i.test(String(item.evidenceUrl || "")) && /^[0-9a-f]{64}$/i.test(String(item.contentHash || "")));
          return incidentNextStatuses(String((detail.data?.record as AnyRecord | undefined)?.status || selected?.status || ""), evidenceReady).length > 0;
        }
        return true;
      }}
      onClose={() => setSelected(null)}
      onEdit={(r) => setEditing(r)}
      onAction={(type, row) => {
        if (kind === "incidents" && (type === "status" || type === "attachEvidence")) {
          const evidenceReady = ((detail.data?.evidence as AnyRecord[] | undefined) || []).some((item) => /^https:\/\//i.test(String(item.evidenceUrl || "")) && /^[0-9a-f]{64}$/i.test(String(item.contentHash || "")));
          action.reset();
          setIncidentAction({ type, row: { ...row, evidenceReady } });
        }
        else if (kind === "coaching" && type === "addNote") setCoachingNoteAction(row);
        else if (kind === "coaching" && type === "complete") setCoachingCompleteAction(row);
        else void actionSingleFlight(() => action.mutateAsync({ type, row }));
      }}
    />
    {editing ? <Modal kind={kind} title={config.createLabel} fields={config.fields} initial={editing} saving={save.isPending} error={save.error} onClose={() => { save.reset(); setEditing(null); }} onSave={(payload) => { void saveSingleFlight(() => save.mutateAsync(payload)); }} /> : null}
    {incidentAction ? <IncidentActionModal action={incidentAction} saving={action.isPending} error={action.error} onClose={() => { action.reset(); setIncidentAction(null); }} onSubmit={(payload) => { void actionSingleFlight(() => action.mutateAsync({ type: incidentAction.type, row: incidentAction.row, payload })); }} /> : null}
    {coachingNoteAction ? <CoachingNoteModal saving={action.isPending} onClose={() => setCoachingNoteAction(null)} onSubmit={(noteText) => { void actionSingleFlight(async () => { await action.mutateAsync({ type: "addNote", row: coachingNoteAction, payload: { noteText } }); setCoachingNoteAction(null); }); }} /> : null}
    {coachingCompleteAction ? <CoachingCompleteModal saving={action.isPending} error={action.error} onClose={() => { action.reset(); setCoachingCompleteAction(null); }} onSubmit={(payload) => { void actionSingleFlight(async () => { await action.mutateAsync({ type: "complete", row: coachingCompleteAction, payload }); setCoachingCompleteAction(null); }); }} /> : null}
  </div>;
}

function VideoGrid({ rows, onSelect }: { rows: AnyRecord[]; onSelect: (row: AnyRecord) => void }) {
  return <div className="grid gap-4 lg:grid-cols-3">{rows.map((row) => <button key={String(row.id)} className="panel p-4 text-left transition hover:border-violet-400/40" onClick={() => onSelect(row)}><div className="flex aspect-video items-center justify-center rounded-xl border border-white/10 bg-slate-900 text-violet-200"><FileVideo className="h-10 w-10" /></div><p className="mt-3 font-semibold text-slate-900">{String(row.eventNumber)}</p><p className="text-sm text-slate-400">{String(row.aiSummary || row.eventType)}</p><div className="mt-3 flex gap-2"><StatusBadge status={row.reviewStatus} /><RiskBadge risk={row.severity} /></div></button>)}</div>;
}

function Drawer({
  config,
  detail,
  loading,
  canUpdate,
  canExport,
  actionPending,
  canRunAction,
  onClose,
  onEdit,
  onAction,
}: {
  config: (typeof configs)[Kind];
  detail?: AnyRecord;
  loading: boolean;
  canUpdate: boolean;
  canExport: boolean;
  actionPending: boolean;
  canRunAction: (type: string) => boolean;
  onClose: () => void;
  onEdit: (record: AnyRecord) => void;
  onAction: (type: string, row: AnyRecord) => void;
}) {
  const record = detail?.record as AnyRecord | undefined;
  const detailDialogRef = useDialogFocus<HTMLDivElement>(Boolean(record), onClose);
  if (!record && !loading) return null;
  if (!record) return null;
  return <div ref={detailDialogRef} className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm" role="dialog" aria-modal="true" aria-label={`${config.eyebrow} detail`}><aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6"><button className="float-right icon-btn" aria-label="Close detail" onClick={onClose}><X className="h-5 w-5" /></button><p className="section-title text-teal-300">{config.eyebrow} Detail</p><h2 className="mt-3 text-2xl font-semibold text-white">{String(record.eventNumber || record.taskNumber || record.incidentNumber || record.packageNumber || `Record ${record.id}`)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status || record.reviewStatus} /><RiskBadge risk={record.severity || record.priority || record.riskScore} /></div><div className="mt-5 flex flex-wrap gap-3"><button className="btn-primary" disabled={!canUpdate || actionPending} title={!canUpdate ? "You do not have permission to perform this action." : "Edit this record."} onClick={() => onEdit(record)}><PenTool className="h-4 w-4" /> Edit</button>{config.actions.map((type) => { const canAction = canRunAction(type); return <button key={type} className="btn-ghost" disabled={!canAction || actionPending} aria-busy={actionPending} title={!canAction ? "You do not have permission to perform this action." : `Run ${labelize(type)}.`} onClick={() => onAction(type, record)}>{labelize(type)}</button>; })}<button className="btn-ghost" disabled={!canExport || actionPending} title={!canExport ? "You do not have permission to perform this action." : "Export this record."} onClick={() => exportCsv(config.eyebrow, record ? [record] : [])}><Download className="h-4 w-4" /> Export Report</button></div><div className="mt-6 grid gap-4 lg:grid-cols-3"><Info title="Primary Context" record={record} keys={Object.keys(record).slice(0,12)} /><Info title="Event Summary / Action" record={record} keys={["aiSummary","aiScript","summary","recommendedAction","reportSummary"]} /><Info title="Evidence / Legal Readiness" record={record} keys={["evidenceStatus","insuranceReportStatus","locked","exportUrl","falsePositive"]} /></div>{config.sections.map(([title,key,columns]) => <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) || []} columns={columns} />)}<Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /><div className="mt-6 grid gap-4 lg:grid-cols-2">{((detail?.recommendations as AnyRecord[]) || []).slice(0,4).map((insight,i) => <AiInsightCard key={String(insight.id || i)} insight={insight} />)}</div></aside></div>;
}

function Modal({ kind, title, fields, initial, saving, error, onClose, onSave }: { kind: Kind; title: string; fields: string[][]; initial: AnyRecord; saving: boolean; error?: unknown; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const recordDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [form, setForm] = useState<AnyRecord>(initial);
  const isIncidentCreate = kind === "incidents" && !initial.id;
  const requiredFields = kind === "coaching"
    ? ["driverId", "coachingType", "title", "description"]
    : isIncidentCreate ? ["incidentType", "severity", "occurredAt", "locationDescription", "aiSummary"] : [];
  const incidentLinkKeys = ["driverId", "vehicleId", "safetyEventId", "dashcamEventId"];
  const hasIncidentLink = incidentLinkKeys.some((key) => /^\d+$/.test(String(form[key] ?? "").trim()) && Number(form[key]) > 0);
  const missingRequired = requiredFields.some((key) => {
    const value = String(form[key] ?? "").trim();
    return !value || (key === "driverId" && (!/^\d+$/.test(value) || Number(value) <= 0));
  }) || (isIncidentCreate && !hasIncidentLink);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (missingRequired) return;
    if (isIncidentCreate) {
      const occurredAt = new Date(String(form.occurredAt));
      if (Number.isNaN(occurredAt.getTime())) return;
      onSave({ ...form, occurredAt: occurredAt.toISOString() });
      return;
    }
    onSave(form);
  };
  const titleId = "safety-record-dialog-title";
  const visibleError = error instanceof Error ? error.message : error ? "The record could not be saved." : "";
  return <div ref={recordDialogRef} className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby={titleId}><form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><div><h2 id={titleId} className="text-2xl font-semibold text-slate-900">{title}</h2>{kind === "coaching" ? <p className="mt-1 text-sm text-slate-600">Driver, coaching type, title, and description are required.</p> : isIncidentCreate ? <p className="mt-1 text-sm text-slate-600">Type, severity, occurrence time, location, summary, and at least one driver, vehicle, safety event, or dashcam event are required.</p> : null}</div><button type="button" className="icon-btn" aria-label="Close record dialog" onClick={onClose}><X /></button></div>{visibleError ? <div role="alert" className="mt-4 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">{visibleError}</div> : null}<div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label]) => { const required = requiredFields.includes(key); const inputId = `safety-record-${key}`; const immutableDriver = kind === "coaching" && key === "driverId" && Boolean(form.id); const numeric = ["driverId", "vehicleId", "safetyEventId", "dashcamEventId", "jobId", "routeId"].includes(key); const textarea = ["description", "aiScript", "aiSummary"].includes(key); return <label key={key} htmlFor={inputId}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}{required ? " *" : ""}</span>{textarea ? <textarea id={inputId} className="field min-h-24" required={required} maxLength={key === "description" || key === "aiSummary" ? 4000 : undefined} value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} /> : <input id={inputId} className="field" required={required} disabled={immutableDriver} min={numeric ? 1 : undefined} type={key === "occurredAt" ? "datetime-local" : numeric ? "number" : "text"} maxLength={key === "title" ? 220 : key === "coachingType" ? 120 : undefined} value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} />}{immutableDriver ? <span className="mt-1 block text-xs text-slate-500">Driver ownership cannot be changed after creation.</span> : null}</label>; })}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || missingRequired} aria-disabled={saving || missingRequired}>{saving ? "Saving…" : "Save"}</button></div></form></div>;
}

function incidentNextStatuses(status: string, evidenceReady = false): string[] {
  const transitions: Record<string, string[]> = {
    New: ["Under Review", "Awaiting Driver Statement"],
    "Awaiting Driver Statement": ["Under Review"],
    "Under Review": evidenceReady ? ["Evidence Collected", "Closed"] : ["Closed"],
    "Evidence Collected": ["Closed"],
    // Legacy records may close, but this retired state is never offered as a target.
    "Insurance Report Ready": ["Closed"],
  };
  return transitions[status] || [];
}

function IncidentActionModal({ action, saving, error, onClose, onSubmit }: {
  action: { type: "status" | "attachEvidence"; row: AnyRecord };
  saving: boolean;
  error: unknown;
  onClose: () => void;
  onSubmit: (payload: AnyRecord) => void;
}) {
  const incidentActionDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const statuses = incidentNextStatuses(String(action.row.status || ""), Boolean(action.row.evidenceReady));
  const [form, setForm] = useState<AnyRecord>(() => action.type === "status"
    ? { status: statuses[0] || "" }
    : { evidenceType: "Document", evidenceTitle: "", evidenceUrl: "", contentHash: "" });
  const titleId = "incident-action-title";
  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({ ...form, rowVersion: Number(action.row.rowVersion ?? action.row.row_version) });
  };
  return <div ref={incidentActionDialogRef} className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby={titleId}>
    <form className="panel w-full max-w-xl p-6" onSubmit={submit}>
      <div className="flex items-start justify-between gap-4"><div><h2 id={titleId} className="text-2xl font-semibold text-slate-900">{action.type === "status" ? "Change incident status" : "Attach evidence reference"}</h2><p className="mt-2 text-sm text-slate-600">{action.type === "status" ? `Current status: ${String(action.row.status)}` : "Record an existing HTTPS evidence reference and its caller-supplied SHA-256 hash. OpsTrax does not upload or verify the referenced file."}</p></div><button type="button" className="icon-btn" aria-label="Close incident action" onClick={onClose}><X /></button></div>
      {error ? <div role="alert" className="mt-4 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">{error instanceof Error ? error.message : "The incident action could not be completed."}</div> : null}
      <div className="mt-6 space-y-4">
        {action.type === "status" ? <label htmlFor="incident-target-status"><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Target status</span><select id="incident-target-status" className="field" required value={String(form.status)} onChange={(e) => setForm({ status: e.target.value })}>{statuses.map((status) => <option key={status} value={status}>{status}</option>)}</select></label> : <>
          <label htmlFor="incident-evidence-type"><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Evidence type</span><select id="incident-evidence-type" className="field" required value={String(form.evidenceType)} onChange={(e) => setForm((value) => ({ ...value, evidenceType: e.target.value }))}><option>Document</option><option>Photo</option><option>Video</option><option>Statement</option><option>Other</option></select></label>
          <label htmlFor="incident-evidence-title"><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Evidence title</span><input id="incident-evidence-title" className="field" required maxLength={220} value={String(form.evidenceTitle)} onChange={(e) => setForm((value) => ({ ...value, evidenceTitle: e.target.value }))} /></label>
          <label htmlFor="incident-evidence-url"><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">HTTPS evidence URL</span><input id="incident-evidence-url" className="field" required type="url" pattern="https://.*" maxLength={400} aria-describedby="incident-evidence-url-help" value={String(form.evidenceUrl)} onChange={(e) => setForm((value) => ({ ...value, evidenceUrl: e.target.value }))} /><span id="incident-evidence-url-help" className="mt-1 block text-xs text-slate-500">Link to an existing evidence object. This form does not upload or validate the file.</span></label>
          <label htmlFor="incident-content-hash"><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">SHA-256 content hash</span><input id="incident-content-hash" className="field font-mono" required pattern="[0-9a-fA-F]{64}" minLength={64} maxLength={64} spellCheck={false} autoCapitalize="none" aria-describedby="incident-content-hash-help" value={String(form.contentHash)} onChange={(e) => setForm((value) => ({ ...value, contentHash: e.target.value.trim().toLowerCase() }))} /><span id="incident-content-hash-help" className="mt-1 block text-xs text-slate-500">Enter the 64-character hexadecimal hash supplied by the evidence source.</span></label>
        </>}
      </div>
      <div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || (action.type === "status" && statuses.length === 0)} aria-busy={saving}>{saving ? "Saving…" : action.type === "status" ? "Update status" : "Attach reference"}</button></div>
    </form>
  </div>;
}

function Info({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2">{keys.map((key) => <p key={key} className="text-sm text-slate-300"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[720px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-white/10">{rows.slice(0,10).map((row,i) => <tr key={String(row.id || i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-300">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}

function CoachingNoteModal({ saving, onClose, onSubmit }: { saving: boolean; onClose: () => void; onSubmit: (note: string) => void }) {
  const coachingNoteDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [note, setNote] = useState("");
  return <div ref={coachingNoteDialogRef} className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="coaching-note-title">
    <form className="panel w-full max-w-lg p-6" onSubmit={(event) => { event.preventDefault(); if (note.trim()) onSubmit(note.trim()); }}>
      <div className="flex items-center justify-between"><h2 id="coaching-note-title" className="text-xl font-semibold text-slate-900">Add coaching note</h2><button type="button" className="icon-btn" aria-label="Close coaching note dialog" onClick={onClose}><X /></button></div>
      <label className="mt-5 block"><span className="mb-2 block text-xs font-bold uppercase tracking-wide text-slate-500">Note</span><textarea autoFocus className="field min-h-32" required value={note} onChange={(event) => setNote(event.target.value)} /></label>
      <div className="mt-5 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || !note.trim()}>{saving ? "Saving…" : "Add note"}</button></div>
    </form>
  </div>;
}

function CoachingCompleteModal({ saving, error, onClose, onSubmit }: { saving: boolean; error: unknown; onClose: () => void; onSubmit: (payload: AnyRecord) => void }) {
  const coachingCompleteDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [completionNote, setCompletionNote] = useState("");
  const [afterSafetyScore, setAfterSafetyScore] = useState("");
  const score = Number(afterSafetyScore);
  const valid = completionNote.trim().length > 0 && completionNote.trim().length <= 4000 && afterSafetyScore.trim() !== "" && Number.isFinite(score) && score >= 0 && score <= 100;
  return <div ref={coachingCompleteDialogRef} className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="coaching-complete-title">
    <form className="panel w-full max-w-xl p-6" onSubmit={(event) => { event.preventDefault(); if (valid) onSubmit({ completionNote: completionNote.trim(), afterSafetyScore: score }); }}>
      <div className="flex items-start justify-between gap-4"><div><h2 id="coaching-complete-title" className="text-xl font-semibold text-slate-900">Complete coaching task</h2><p className="mt-2 text-sm text-slate-600">Document the observed outcome and current safety score. The comparison is observational and does not prove coaching caused the score change.</p></div><button type="button" className="icon-btn" aria-label="Close coaching completion dialog" onClick={onClose}><X /></button></div>
      {error ? <div role="alert" className="mt-4 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">{error instanceof Error ? error.message : "The coaching task could not be completed."}</div> : null}
      <div className="mt-5 space-y-4">
        <label htmlFor="coaching-completion-note"><span className="field-label">Completion outcome *</span><textarea id="coaching-completion-note" className="field mt-1 min-h-28" required maxLength={4000} value={completionNote} onChange={(event) => setCompletionNote(event.target.value)} placeholder="Summarize the discussion, corrective action, and follow-up commitment." /></label>
        <label htmlFor="coaching-after-score"><span className="field-label">Observed safety score (0–100) *</span><input id="coaching-after-score" className="field mt-1" required type="number" min={0} max={100} step="0.01" value={afterSafetyScore} onChange={(event) => setAfterSafetyScore(event.target.value)} /></label>
      </div>
      <div className="mt-5 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || !valid} aria-disabled={saving || !valid}>{saving ? "Completing…" : "Complete task"}</button></div>
    </form>
  </div>;
}

function defaultForm(kind: Kind): AnyRecord {
  if (kind === "safety") return { eventType: "Harsh Braking", severity: "High", reviewStatus: "New", riskScore: "" };
  if (kind === "dashcam") return { eventType: "Near Miss", title: "", severity: "High", reviewStatus: "Pending Review", aiConfidence: "" };
  if (kind === "coaching") return { coachingType: "Following Distance", priority: "High", title: "", dueAt: new Date(Date.now()+7*86400000).toISOString(), idempotencyKey: crypto.randomUUID() };
  if (kind === "incidents") return { incidentType: "", severity: "", status: "New", occurredAt: "", locationDescription: "", aiSummary: "", idempotencyKey: crypto.randomUUID() };
  return { status: "Draft", summary: "" };
}

async function runAction(kind: Kind, type: string, row: AnyRecord, payload?: AnyRecord) {
  const id = row.id as string | number;
  if (kind === "safety") {
    if (type === "review")         return safetyApi.review(id);
    if (type === "dismiss")        return safetyApi.dismiss(id);
    if (type === "resolve")        return safetyApi.resolve(id);
    if (type === "createCoaching") return safetyApi.createCoaching(id);
    return safetyApi.createIncident(id);
  }
  if (kind === "dashcam") return type === "review" ? dashcamApi.review(id) : type === "falsePositive" ? dashcamApi.falsePositive(id) : type === "createCoaching" ? dashcamApi.createCoaching(id) : type === "createEvidencePackage" ? dashcamApi.createEvidencePackage(id) : dashcamApi.createIncidentReport(id);
  if (kind === "coaching") {
    const rowVersion = (row.rowVersion ?? row.row_version) as string | number;
    if (type === "assign") return coachingApi.assign(id, { rowVersion });
    if (type === "complete") return coachingApi.complete(id, { rowVersion, completionNote: String(payload?.completionNote || "").trim(), afterSafetyScore: Number(payload?.afterSafetyScore) });
    return coachingApi.addNote(id, { noteText: String(payload?.noteText || "").trim() });
  }
  if (kind === "incidents") {
    const rowVersion = Number(row.rowVersion);
    if (type === "status") return incidentsApi.status(id, { status: String(payload?.status || ""), rowVersion });
    if (type === "attachEvidence") return incidentsApi.attachEvidence(id, {
      evidenceType: String(payload?.evidenceType || ""),
      evidenceTitle: String(payload?.evidenceTitle || ""),
      evidenceUrl: String(payload?.evidenceUrl || ""),
      contentHash: String(payload?.contentHash || ""),
      rowVersion,
    });
    return incidentsApi.createInsuranceReport(id, { rowVersion });
  }
  return type === "lock" ? evidenceApi.lock(id) : evidenceApi.exportPlaceholder(id);
}
