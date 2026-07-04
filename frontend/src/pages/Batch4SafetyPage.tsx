import { FormEvent, ReactNode, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, FileVideo, Gavel, PackageCheck, PenTool, Plus, ShieldAlert, UserCheck, X } from "lucide-react";
import { AiInsightCard, DataTable, EmptyState, KpiCard, LoadingState, RiskBadge, Select, StatusBadge, PageHeader, exportCsv, labelize } from "@/components/ui";
import { useCoachingSummary, useCoachingTaskDetail, useCoachingTasks, useDashcamEventDetail, useDashcamEvents, useDashcamSummary, useEvidencePackageDetail, useEvidencePackages, useEvidenceSummary, useIncidentDetail, useIncidents, useIncidentsSummary, useSafetyEventDetail, useSafetyEvents, useSafetySummary } from "@/hooks/useBatch4";
import { useHasPermission } from "@/hooks/usePermission";
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
    useRows: useDashcamEvents, useSummary: useDashcamSummary, useDetail: useDashcamEventDetail, api: dashcamApi, createLabel: "Create Video Event",
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
    fields: [["driverId","Driver ID"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["assignedToUserId","Assigned User ID"],["coachingType","Coaching Type"],["priority","Priority"],["status","Status"],["title","Title"],["description","Description"],["aiScript","Coaching Script"],["dueAt","Due At"]],
    actions: ["assign","acknowledge","complete","addNote"],
    sections: [["Coaching Notes","notes",["noteType","noteText","createdAt"]],["Related Safety Events","relatedSafetyEvents",["eventNumber","eventType","severity","reviewStatus"]],["Related Dashcam Events","relatedDashcamEvents",["eventNumber","eventType","severity","reviewStatus"]]],
  },
  incidents: {
    queryKey: "incidents", eyebrow: "Incidents", title: "Incident and legal review register", icon: <Gavel />,
    description: "Manage incidents, driver and customer statements, evidence, insurance report drafts and chain-of-custody audit trails.",
    useRows: useIncidents, useSummary: useIncidentsSummary, useDetail: useIncidentDetail, api: incidentsApi, createLabel: "Create Incident",
    kpis: [["Total Incidents","totalIncidents"],["Open Incidents","openIncidents"],["Closed Incidents","closedIncidents"],["Critical Incidents","criticalIncidents"],["Insurance Ready","insuranceReady"],["Awaiting Statement","awaitingDriverStatement"],["Insurance Reports","insuranceReports"],["Evidence Collected","evidenceCollected"]],
    columns: ["incidentNumber","incidentType","severity","status","driverName","vehicleCode","jobNumber","routeCode","locationDescription","occurredAt","insuranceReportStatus","recommendedAction"],
    fields: [["incidentNumber","Incident Number"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["routeId","Route ID"],["incidentType","Incident Type"],["severity","Severity"],["status","Status"],["locationDescription","Location"],["driverStatement","Driver Statement"],["witnessStatement","Witness Statement"],["customerStatement","Customer Statement"],["recommendedAction","Recommended Action"]],
    actions: ["status","attachEvidence","createInsuranceReport"],
    sections: [["Evidence List","evidence",["evidenceType","evidenceTitle","sourceEntityType","createdAt"]],["Evidence Packages","packages",["packageNumber","status","locked","exportUrl"]],["Insurance Reports","insuranceReports",["reportNumber","status","exportUrl","createdAt"]],["Timeline","timeline",["title","eventType","eventTime"]]],
  },
  evidence: {
    queryKey: "evidence-packages", eyebrow: "Evidence Packages", title: "Insurance evidence package builder", icon: <PackageCheck />,
    description: "Bundle video, GPS, speed data, statements, job context, DVIR references, maintenance history and legal export packages.",
    useRows: useEvidencePackages, useSummary: useEvidenceSummary, useDetail: useEvidencePackageDetail, api: evidenceApi, createLabel: "Create Evidence Package",
    kpis: [["Total Packages","totalPackages"],["Draft Packages","draftPackages"],["Export Ready","exportReady"],["Locked Packages","lockedPackages"],["Insurance Packages","insurancePackages"],["Exports Generated","exportsGenerated"]],
    columns: ["packageNumber","incidentNumber","driverName","vehicleCode","safetyEventNumber","dashcamEventNumber","packageType","status","locked","exportUrl","summary"],
    fields: [["incidentId","Incident ID"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["status","Status"],["summary","Summary"]],
    actions: ["exportEvidencePackage","lock"],
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
    export: "safety:view",
    assign: "safety:update",
    acknowledge: "safety:review",
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
  const can = (permission: string) => hasPermission(permission);
  const createPermission = ACTION_PERMISSIONS[kind].create;
  const updatePermission = ACTION_PERMISSIONS[kind].update;
  const exportPermission = ACTION_PERMISSIONS[kind].export;
  const rowsQuery = config.useRows();
  const summary = config.useSummary();
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("All");
  const detail = config.useDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();
  const invalidate = async () => { await qc.invalidateQueries({ queryKey: [config.queryKey] }); await qc.invalidateQueries({ queryKey: [config.queryKey, "summary"] }); if (selected?.id) await qc.invalidateQueries({ queryKey: [config.queryKey, "detail", selected.id] }); };
  const save = useMutation({ mutationFn: (payload: AnyRecord) => payload.id ? config.api.update(payload.id as string | number, payload) : config.api.create(payload), onSuccess: async () => { setEditing(null); await invalidate(); } });
  const action = useMutation({ mutationFn: ({ type, row }: { type: string; row: AnyRecord }) => runAction(kind, type, row), onSuccess: invalidate });
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
    return <EmptyState title={`${config.eyebrow} unavailable`} subtitle="Unable to load live records right now. Refresh to try again." />;
  }
  const s = (summary.data || {}) as AnyRecord;
  return <div className="space-y-8">
    <PageHeader
      eyebrow={config.eyebrow}
      title={config.title}
      description={config.description}
      actions={
        <>
          <button
            className="btn-primary"
            disabled={!can(createPermission)}
            title={!can(createPermission) ? "You do not have permission to perform this action." : `Create a new ${config.eyebrow.toLowerCase()} record.`}
            onClick={() => setEditing(defaultForm(kind))}
          >
            <Plus className="h-4 w-4" /> {config.createLabel}
          </button>
          <button
            className="btn-ghost"
            disabled={!can(exportPermission)}
            title={!can(exportPermission) ? "You do not have permission to perform this action." : "Export the current filtered records."}
            onClick={() => exportCsv(kind, rows)}
          >
            <Download className="h-4 w-4" /> Export Report
          </button>
        </>
      }
    />
    <div className="grid gap-6 sm:grid-cols-3 xl:grid-cols-5">{config.kpis.slice(0, 5).map(([label,key]) => <KpiCard key={key} label={label} value={String(s[key] ?? 0)} status={/critical|overdue|missing|rejected/i.test(label) ? "Critical" : undefined} />)}</div>
    <div className="flex flex-col gap-3 xl:flex-row xl:items-center"><input className="field xl:max-w-md" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={`Search ${config.eyebrow.toLowerCase()} by driver, vehicle, route, event, status...`} /><Select className="xl:max-w-[180px]" value={filter} onChange={(e) => setFilter(e.target.value)}><option>All</option><option>Critical</option><option>High</option><option>Pending</option><option>Reviewed</option><option>Open</option><option>Closed</option><option>Locked</option></Select></div>
    {!rows.length ? (
      <EmptyState title={`No ${config.eyebrow.toLowerCase()} records`} subtitle="Try another filter or create the first record." />
    ) : (
      <DataTable rows={rows} columns={config.columns} onSelect={setSelected} />
    )}
    <Drawer
      config={config}
      detail={detail.data}
      loading={detail.isLoading}
      canUpdate={can(updatePermission)}
      canExport={can(exportPermission)}
      canRunAction={(type) => {
        const permission = ACTION_PERMISSIONS[kind][type] || updatePermission;
        return can(permission);
      }}
      onClose={() => setSelected(null)}
      onEdit={(r) => setEditing(r)}
      onAction={(type, row) => action.mutate({ type, row })}
    />
    {editing ? <Modal title={config.createLabel} fields={config.fields} initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(payload) => save.mutate(payload)} /> : null}
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
  canRunAction: (type: string) => boolean;
  onClose: () => void;
  onEdit: (record: AnyRecord) => void;
  onAction: (type: string, row: AnyRecord) => void;
}) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;
  return <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm"><aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6"><button className="float-right icon-btn" onClick={onClose}><X className="h-5 w-5" /></button><p className="section-title text-teal-300">{config.eyebrow} Detail</p><h2 className="mt-3 text-2xl font-semibold text-white">{String(record.eventNumber || record.taskNumber || record.incidentNumber || record.packageNumber || `Record ${record.id}`)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status || record.reviewStatus} /><RiskBadge risk={record.severity || record.priority || record.riskScore} /></div><div className="mt-5 flex flex-wrap gap-3"><button className="btn-primary" disabled={!canUpdate} title={!canUpdate ? "You do not have permission to perform this action." : "Edit this record."} onClick={() => onEdit(record)}><PenTool className="h-4 w-4" /> Edit</button>{config.actions.map((type) => { const canAction = canRunAction(type); return <button key={type} className="btn-ghost" disabled={!canAction} title={!canAction ? "You do not have permission to perform this action." : `Run ${labelize(type)}.`} onClick={() => onAction(type, record)}>{labelize(type)}</button>; })}<button className="btn-ghost" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : "Export this record."} onClick={() => exportCsv(config.eyebrow, record ? [record] : [])}><Download className="h-4 w-4" /> Export Report</button></div><div className="mt-6 grid gap-4 lg:grid-cols-3"><Info title="Primary Context" record={record} keys={Object.keys(record).slice(0,12)} /><Info title="Event Summary / Action" record={record} keys={["aiSummary","aiScript","summary","recommendedAction","reportSummary"]} /><Info title="Evidence / Legal Readiness" record={record} keys={["evidenceStatus","insuranceReportStatus","locked","exportUrl","falsePositive"]} /></div>{config.sections.map(([title,key,columns]) => <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) || []} columns={columns} />)}<Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /><div className="mt-6 grid gap-4 lg:grid-cols-2">{((detail?.recommendations as AnyRecord[]) || []).slice(0,4).map((insight,i) => <AiInsightCard key={String(insight.id || i)} insight={insight} />)}</div></aside></div>;
}

function Modal({ title, fields, initial, saving, onClose, onSave }: { title: string; fields: string[][]; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4"><form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><h2 className="text-2xl font-semibold text-slate-900">{title}</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div><div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label]) => <label key={key}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span><input className="field" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} /></label>)}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving}>Save</button></div></form></div>;
}

function Info({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2">{keys.map((key) => <p key={key} className="text-sm text-slate-300"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[720px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-white/10">{rows.slice(0,10).map((row,i) => <tr key={String(row.id || i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-300">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}

function defaultForm(kind: Kind): AnyRecord {
  if (kind === "safety") return { eventType: "Harsh Braking", severity: "High", reviewStatus: "New", riskScore: "" };
  if (kind === "dashcam") return { eventType: "Near Miss", title: "", severity: "High", reviewStatus: "Pending Review", aiConfidence: "" };
  if (kind === "coaching") return { coachingType: "Following Distance", priority: "High", status: "Assigned", title: "", dueAt: new Date(Date.now()+7*86400000).toISOString() };
  if (kind === "incidents") return { incidentType: "Near Miss", severity: "High", status: "New", locationDescription: "" };
  return { status: "Draft", summary: "" };
}

async function runAction(kind: Kind, type: string, row: AnyRecord) {
  const id = row.id as string | number;
  if (kind === "safety") {
    if (type === "review")         return safetyApi.review(id);
    if (type === "dismiss")        return safetyApi.dismiss(id);
    if (type === "resolve")        return safetyApi.resolve(id);
    if (type === "createCoaching") return safetyApi.createCoaching(id);
    return safetyApi.createIncident(id);
  }
  if (kind === "dashcam") return type === "review" ? dashcamApi.review(id) : type === "falsePositive" ? dashcamApi.falsePositive(id) : type === "createCoaching" ? dashcamApi.createCoaching(id) : type === "createEvidencePackage" ? dashcamApi.createEvidencePackage(id) : dashcamApi.createIncidentReport(id);
  if (kind === "coaching") return type === "assign" ? coachingApi.assign(id, {}) : type === "acknowledge" ? coachingApi.acknowledge(id) : type === "complete" ? coachingApi.complete(id) : coachingApi.addNote(id, { noteText: "" });
  if (kind === "incidents") return type === "status" ? incidentsApi.status(id, { status: "Evidence Collected" }) : type === "attachEvidence" ? incidentsApi.attachEvidence(id, { evidenceType: "Photo", evidenceTitle: "Evidence item" }) : incidentsApi.createInsuranceReport(id);
  return type === "lock" ? evidenceApi.lock(id) : evidenceApi.exportPlaceholder(id);
}

