import { FormEvent, ReactNode, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, FileVideo, Gavel, PackageCheck, PenTool, Plus, ShieldAlert, Sparkles, UserCheck, X } from "lucide-react";
import { AiInsightCard, DataTable, KpiCard, LoadingState, RiskBadge, StatusBadge, PageHeader, labelize } from "@/components/ui";
import { useCoachingSummary, useCoachingTaskDetail, useCoachingTasks, useDashcamEventDetail, useDashcamEvents, useDashcamSummary, useEvidencePackageDetail, useEvidencePackages, useEvidenceSummary, useIncidentDetail, useIncidents, useIncidentsSummary, useSafetyEventDetail, useSafetyEvents, useSafetySummary } from "@/hooks/useBatch4";
import { coachingApi } from "@/services/coachingApi";
import { dashcamApi } from "@/services/dashcamApi";
import { evidenceApi } from "@/services/evidenceApi";
import { incidentsApi } from "@/services/incidentsApi";
import { safetyApi } from "@/services/safetyApi";
import type { AnyRecord } from "@/types";

type Kind = "safety" | "dashcam" | "coaching" | "incidents" | "evidence";

const configs = {
  safety: {
    queryKey: "safety", eyebrow: "Safety", title: "Fleet safety intelligence center", icon: <ShieldAlert />,
    description: "Review safety events, driver risk, vehicle risk, trends, coaching queues and incident predictors from connected OpsTrax data.",
    useRows: useSafetyEvents, useSummary: useSafetySummary, useDetail: useSafetyEventDetail, api: safetyApi, createLabel: "Create Safety Event",
    kpis: [["Fleet Safety Score","fleetSafetyScore"],["Events Today","safetyEventsToday"],["Critical Events","criticalEvents"],["Harsh Braking","harshBraking"],["Harsh Acceleration","harshAcceleration"],["Speeding","speedingEvents"],["Route Deviation","routeDeviation"],["Distracted Placeholder","distractedDrivingPlaceholder"],["Coaching Needed","coachingNeeded"],["Open Incidents","openIncidents"],["Reviewed Events","reviewedEvents"],["Preventable Risk","preventableRiskScore"]],
    columns: ["eventNumber","eventType","severity","driverName","vehicleCode","jobNumber","routeCode","locationDescription","speed","occurredAt","reviewStatus","coachingStatus","incidentStatus","riskScore","recommendedAction"],
    fields: [["eventNumber","Event Number"],["eventType","Event Type"],["severity","Severity"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["routeId","Route ID"],["locationDescription","Location/Zone"],["speed","Speed"],["postedSpeedLimit","Posted Limit"],["reviewStatus","Review Status"],["riskScore","Risk Score"],["aiSummary","AI Summary"],["recommendedAction","Recommended Action"]],
    actions: ["review","createCoaching","createIncident"],
    sections: [["Dashcam Events","dashcamEvents",["eventNumber","eventType","severity","reviewStatus","evidenceStatus"]],["Coaching Queue","coachingTasks",["taskNumber","coachingType","priority","status","dueAt"]],["Incident Watch","incidents",["incidentNumber","incidentType","severity","status"]]],
  },
  dashcam: {
    queryKey: "dashcam", eyebrow: "AI Dashcam / Incident Review", title: "AI video event inbox", icon: <FileVideo />,
    description: "Review video event metadata, AI summaries, placeholder road/driver clips, false positives, coaching, evidence packages and insurance exports.",
    useRows: useDashcamEvents, useSummary: useDashcamSummary, useDetail: useDashcamEventDetail, api: dashcamApi, createLabel: "Create Video Event",
    kpis: [["Dashcam Events Today","dashcamEventsToday"],["Critical Video Events","criticalVideoEvents"],["Pending Review","pendingReview"],["Reviewed Events","reviewedEvents"],["False Positives","falsePositives"],["Coaching Created","coachingCreated"],["Evidence Packages","evidencePackages"],["Collision/Near Miss","collisionNearMiss"],["Distracted Placeholder","distractedDrivingPlaceholder"],["Tailgating Placeholder","tailgatingPlaceholder"],["Speeding Video","speedingVideoEvents"],["Exoneration Placeholder","driverExonerationPlaceholder"]],
    columns: ["eventNumber","eventType","severity","driverName","vehicleCode","jobNumber","routeCode","locationDescription","occurredAt","videoProvider","aiConfidence","reviewStatus","evidenceStatus","recommendedAction"],
    fields: [["eventNumber","Event Number"],["safetyEventId","Safety Event ID"],["eventType","Event Type"],["title","Title"],["severity","Severity"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["routeId","Route ID"],["locationDescription","Location"],["aiSummary","AI Summary"],["aiConfidence","AI Confidence"],["reviewStatus","Review Status"],["evidenceStatus","Evidence Status"],["recommendedAction","Recommended Action"]],
    actions: ["review","falsePositive","createCoaching","createEvidencePackage","createIncidentReport"],
    sections: [["Coaching From Video", "coachingTasks", ["taskNumber","priority","status","aiScript"]],["Evidence Packages","evidencePackages",["packageNumber","status","locked","exportUrl"]]],
  },
  coaching: {
    queryKey: "coaching", eyebrow: "Driver Coaching", title: "Driver coaching queue", icon: <UserCheck />,
    description: "Assign, acknowledge, complete and document coaching with AI scripts, before/after safety trends and repeat behavior detection.",
    useRows: useCoachingTasks, useSummary: useCoachingSummary, useDetail: useCoachingTaskDetail, api: coachingApi, createLabel: "Create Coaching Task",
    kpis: [["Open Coaching","openCoachingTasks"],["Critical Coaching","criticalCoaching"],["Assigned Tasks","assignedTasks"],["Driver Acknowledged","driverAcknowledged"],["Completed This Month","completedThisMonth"],["Overdue Coaching","overdueCoaching"],["Repeat Drivers","repeatCoachingDrivers"],["Safety Improved","safetyScoreImproved"],["Escalated","escalatedCoaching"],["Avg Completion","averageCompletionTime"]],
    columns: ["taskNumber","driverName","coachingType","priority","status","assignedToName","driverAcknowledged","beforeSafetyScore","afterSafetyScore","effectivenessScore","dueAt"],
    fields: [["driverId","Driver ID"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["assignedToUserId","Assigned User ID"],["coachingType","Coaching Type"],["priority","Priority"],["status","Status"],["title","Title"],["description","Description"],["aiScript","AI Script"],["dueAt","Due At"]],
    actions: ["assign","acknowledge","complete","addNote"],
    sections: [["Coaching Notes","notes",["noteType","noteText","createdAt"]],["Related Safety Events","relatedSafetyEvents",["eventNumber","eventType","severity","reviewStatus"]],["Related Dashcam Events","relatedDashcamEvents",["eventNumber","eventType","severity","reviewStatus"]]],
  },
  incidents: {
    queryKey: "incidents", eyebrow: "Incidents", title: "Incident and legal review register", icon: <Gavel />,
    description: "Manage incidents, driver/customer statements, evidence, insurance report placeholders and chain-of-custody audit trails.",
    useRows: useIncidents, useSummary: useIncidentsSummary, useDetail: useIncidentDetail, api: incidentsApi, createLabel: "Create Incident",
    kpis: [["Total Incidents","totalIncidents"],["Open Incidents","openIncidents"],["Closed Incidents","closedIncidents"],["Critical Incidents","criticalIncidents"],["Insurance Ready","insuranceReady"],["Awaiting Statement","awaitingDriverStatement"],["Insurance Reports","insuranceReports"],["Evidence Collected","evidenceCollected"]],
    columns: ["incidentNumber","incidentType","severity","status","driverName","vehicleCode","jobNumber","routeCode","locationDescription","occurredAt","insuranceReportStatus","recommendedAction"],
    fields: [["incidentNumber","Incident Number"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["routeId","Route ID"],["incidentType","Incident Type"],["severity","Severity"],["status","Status"],["locationDescription","Location"],["driverStatement","Driver Statement"],["witnessStatement","Witness Statement"],["customerStatement","Customer Statement"],["recommendedAction","Recommended Action"]],
    actions: ["status","attachEvidence","createInsuranceReport"],
    sections: [["Evidence List","evidence",["evidenceType","evidenceTitle","sourceEntityType","createdAt"]],["Evidence Packages","packages",["packageNumber","status","locked","exportUrl"]],["Insurance Reports","insuranceReports",["reportNumber","status","exportUrl","createdAt"]],["Timeline","timeline",["title","eventType","eventTime"]]],
  },
  evidence: {
    queryKey: "evidence-packages", eyebrow: "Evidence Packages", title: "Insurance evidence package builder", icon: <PackageCheck />,
    description: "Bundle video, GPS, speed data, statements, job context, DVIR references, maintenance history and legal export placeholders.",
    useRows: useEvidencePackages, useSummary: useEvidenceSummary, useDetail: useEvidencePackageDetail, api: evidenceApi, createLabel: "Create Evidence Package",
    kpis: [["Total Packages","totalPackages"],["Draft Packages","draftPackages"],["Export Ready","exportReady"],["Locked Packages","lockedPackages"],["Insurance Packages","insurancePackages"],["Exports Generated","exportsGenerated"]],
    columns: ["packageNumber","incidentNumber","driverName","vehicleCode","safetyEventNumber","dashcamEventNumber","packageType","status","locked","exportUrl","summary"],
    fields: [["incidentId","Incident ID"],["safetyEventId","Safety Event ID"],["dashcamEventId","Dashcam Event ID"],["driverId","Driver ID"],["vehicleId","Vehicle ID"],["jobId","Job ID"],["status","Status"],["summary","Summary"]],
    actions: ["exportPlaceholder","lock"],
    sections: [["Package Items","items",["itemType","itemTitle","sourceEntityType","sourceEntityId","createdAt"]]],
  },
} satisfies Record<Kind, { queryKey: string; eyebrow: string; title: string; icon: ReactNode; description: string; useRows: () => { data?: AnyRecord[]; isLoading: boolean }; useSummary: () => { data?: AnyRecord }; useDetail: (id?: string | number) => { data?: AnyRecord; isLoading: boolean }; api: AnyRecord; createLabel: string; kpis: string[][]; columns: string[]; fields: string[][]; actions: string[]; sections: [string,string,string[]][] }>;

export function Batch4SafetyPage({ kind }: { kind: Kind }) {
  const config = configs[kind];
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
    const text = JSON.stringify(row).toLowerCase();
    const status = String(row.status || row.reviewStatus || row.severity || "").toLowerCase();
    return (!search || text.includes(search.toLowerCase())) && (filter === "All" || status.includes(filter.toLowerCase()) || text.includes(filter.toLowerCase()));
  }), [rowsQuery.data, search, filter]);
  if (rowsQuery.isLoading) return <LoadingState />;
  const s = summary.data || {};
  return <div className="space-y-6">
    <PageHeader eyebrow={config.eyebrow} title={config.title} description={config.description} actions={<><button className="btn-primary" onClick={() => setEditing(defaultForm(kind))}><Plus className="h-4 w-4" /> {config.createLabel}</button><button className="btn-ghost" onClick={() => exportCsv(kind, rows)}><Download className="h-4 w-4" /> Export Report</button></>} />
    <div className="grid gap-4 md:grid-cols-4 xl:grid-cols-6">{config.kpis.map(([label,key]) => <KpiCard key={key} label={label} value={String(s[key] ?? 0)} icon={config.icon} status={/critical|open|pending|risk|overdue|draft/i.test(label) ? "Review" : "Active"} />)}</div>
    <div className="panel flex flex-col gap-3 p-4 xl:flex-row xl:items-center"><input className="field xl:max-w-md" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={`Search ${config.eyebrow.toLowerCase()} by driver, vehicle, route, event, status...`} /><select className="field xl:max-w-[180px]" value={filter} onChange={(e) => setFilter(e.target.value)}><option>All</option><option>Critical</option><option>High</option><option>Pending</option><option>Reviewed</option><option>Open</option><option>Closed</option><option>Locked</option></select><span className="badge"><Sparkles className="h-3.5 w-3.5" /> AI workflow, report/export placeholders active</span></div>
    {kind === "dashcam" ? <VideoGrid rows={rows.slice(0, 6)} onSelect={setSelected} /> : null}
    <DataTable rows={rows} columns={config.columns} onSelect={setSelected} />
    <Drawer config={config} detail={detail.data} loading={detail.isLoading} onClose={() => setSelected(null)} onEdit={(r) => setEditing(r)} onAction={(type, row) => action.mutate({ type, row })} />
    {editing ? <Modal title={config.createLabel} fields={config.fields} initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(payload) => save.mutate(payload)} /> : null}
  </div>;
}

function VideoGrid({ rows, onSelect }: { rows: AnyRecord[]; onSelect: (row: AnyRecord) => void }) {
  return <div className="grid gap-4 lg:grid-cols-3">{rows.map((row) => <button key={String(row.id)} className="panel p-4 text-left transition hover:border-violet-400/40" onClick={() => onSelect(row)}><div className="flex aspect-video items-center justify-center rounded-xl border border-white/10 bg-slate-900 text-violet-200"><FileVideo className="h-10 w-10" /></div><p className="mt-3 font-semibold text-white">{String(row.eventNumber)}</p><p className="text-sm text-slate-400">{String(row.aiSummary || row.eventType)}</p><div className="mt-3 flex gap-2"><StatusBadge status={row.reviewStatus} /><RiskBadge risk={row.severity} /></div></button>)}</div>;
}

function Drawer({ config, detail, loading, onClose, onEdit, onAction }: { config: (typeof configs)[Kind]; detail?: AnyRecord; loading: boolean; onClose: () => void; onEdit: (record: AnyRecord) => void; onAction: (type: string, row: AnyRecord) => void }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;
  return <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm"><aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6"><button className="float-right icon-btn" onClick={onClose}><X className="h-5 w-5" /></button><p className="section-title text-teal-300">{config.eyebrow} Detail</p><h2 className="mt-3 text-2xl font-semibold text-white">{String(record.eventNumber || record.taskNumber || record.incidentNumber || record.packageNumber || `Record ${record.id}`)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status || record.reviewStatus} /><RiskBadge risk={record.severity || record.priority || record.riskScore} /><span className="badge">OpsTrax AI Active</span></div><div className="mt-5 flex flex-wrap gap-3"><button className="btn-primary" onClick={() => onEdit(record)}><PenTool className="h-4 w-4" /> Edit</button>{config.actions.map((type) => <button key={type} className="btn-ghost" onClick={() => onAction(type, record)}>{labelize(type)}</button>)}<button className="btn-ghost"><Download className="h-4 w-4" /> Report Placeholder</button></div><div className="mt-6 grid gap-4 lg:grid-cols-3"><Info title="Primary Context" record={record} keys={Object.keys(record).slice(0,12)} /><Info title="AI Summary / Recommended Action" record={record} keys={["aiSummary","aiScript","summary","recommendedAction","reportSummary"]} /><Info title="Evidence / Legal Readiness" record={record} keys={["evidenceStatus","insuranceReportStatus","locked","exportUrl","falsePositive"]} /></div>{config.sections.map(([title,key,columns]) => <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) || []} columns={columns} />)}<Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /><div className="mt-6 grid gap-4 lg:grid-cols-2">{((detail?.recommendations as AnyRecord[]) || []).slice(0,4).map((insight,i) => <AiInsightCard key={String(insight.id || i)} insight={insight} />)}</div></aside></div>;
}

function Modal({ title, fields, initial, saving, onClose, onSave }: { title: string; fields: string[][]; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4"><form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><h2 className="text-2xl font-semibold text-white">{title}</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div><div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label]) => <label key={key}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span><input className="field" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} /></label>)}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button className="btn-primary" disabled={saving}>Save</button></div></form></div>;
}

function Info({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2">{keys.map((key) => <p key={key} className="text-sm text-slate-300"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[720px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-white/10">{rows.slice(0,10).map((row,i) => <tr key={String(row.id || i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-300">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}

function defaultForm(kind: Kind): AnyRecord {
  if (kind === "safety") return { eventType: "Harsh Braking", severity: "High", driverId: 1, vehicleId: 1, reviewStatus: "New", riskScore: 72 };
  if (kind === "dashcam") return { eventType: "Near Miss", title: "AI video event", severity: "High", driverId: 1, vehicleId: 1, reviewStatus: "Pending Review", aiConfidence: 88 };
  if (kind === "coaching") return { driverId: 1, coachingType: "Following Distance", priority: "High", status: "Assigned", title: "Driver coaching", dueAt: new Date(Date.now()+7*86400000).toISOString() };
  if (kind === "incidents") return { incidentType: "Near Miss", severity: "High", status: "New", driverId: 1, vehicleId: 1, locationDescription: "Northern Virginia corridor" };
  return { incidentId: 1, safetyEventId: 1, dashcamEventId: 1, driverId: 1, vehicleId: 1, status: "Draft", summary: "Evidence package placeholder" };
}

async function runAction(kind: Kind, type: string, row: AnyRecord) {
  const id = row.id as string | number;
  if (kind === "safety") return type === "review" ? safetyApi.review(id) : type === "createCoaching" ? safetyApi.createCoaching(id) : safetyApi.createIncident(id);
  if (kind === "dashcam") return type === "review" ? dashcamApi.review(id) : type === "falsePositive" ? dashcamApi.falsePositive(id) : type === "createCoaching" ? dashcamApi.createCoaching(id) : type === "createEvidencePackage" ? dashcamApi.createEvidencePackage(id) : dashcamApi.createIncidentReport(id);
  if (kind === "coaching") return type === "assign" ? coachingApi.assign(id, { assignedToUserId: 1 }) : type === "acknowledge" ? coachingApi.acknowledge(id) : type === "complete" ? coachingApi.complete(id) : coachingApi.addNote(id, { noteText: "OpsTrax coaching note placeholder." });
  if (kind === "incidents") return type === "status" ? incidentsApi.status(id, { status: "Evidence Collected" }) : type === "attachEvidence" ? incidentsApi.attachEvidence(id, { evidenceType: "Photo Placeholder", evidenceTitle: "Attached evidence placeholder" }) : incidentsApi.createInsuranceReport(id);
  return type === "lock" ? evidenceApi.lock(id) : evidenceApi.exportPlaceholder(id);
}

function exportCsv(name: string, rows: AnyRecord[]) {
  const cols = Array.from(new Set(rows.flatMap((row) => Object.keys(row)))).slice(0,24);
  const csv = [cols.join(","), ...rows.map((row) => cols.map((c) => JSON.stringify(row[c] ?? "")).join(","))].join("\n");
  const a = document.createElement("a"); a.href = URL.createObjectURL(new Blob([csv], { type: "text/csv" })); a.download = `opstrax-${name}.csv`; a.click();
}
