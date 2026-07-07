import { FormEvent, ReactNode, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, Award, Download, FileVideo, Gavel, PackageCheck,
  PenTool, Plus, Search, Shield, ShieldAlert, Sparkles, TrendingUp,
  UserCheck, X,
} from "lucide-react";
import { AiInsightCard, DataTable, EmptyState, KpiCard, LoadingState, RiskBadge, Select, StatusBadge, exportCsv, labelize } from "@/components/ui";
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
  return <div className="space-y-6 pb-10">
    {/* ── fh-hero header ─────────────────────────────────────── */}
    <header className="fh-hero relative">
      <span className="fh-hero-bar" />
      <span className="fh-hero-glow-1" />
      <span className="fh-hero-glow-2" />
      <div className="relative px-7 py-6">
        <div className="flex flex-wrap items-start justify-between gap-6">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-3 mb-3">
              <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                {config.icon} {config.eyebrow}
              </span>
              <span className="text-[11px] font-semibold text-slate-500">Safety operations and compliance review</span>
            </div>
            <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
              {config.title}
            </h1>
            <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
              {config.description}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button type="button" className="fh-btn-primary cursor-pointer" disabled={!can(createPermission)} title={!can(createPermission) ? "You do not have permission to perform this action." : undefined} onClick={() => can(createPermission) && setEditing(defaultForm(kind))}><Plus className="h-4 w-4" /> {config.createLabel}</button>
            <button type="button" className="fh-btn-ghost cursor-pointer" disabled={!can(exportPermission)} title={!can(exportPermission) ? "You do not have permission to perform this action." : undefined} onClick={() => can(exportPermission) && exportCsv(kind, rows)}><Download className="h-4 w-4" /> Export Roster</button>
          </div>
        </div>
      </div>
    </header>

    {/* ── Ops intelligence bar ─────────────────────────────── */}
    <div className="anim-fade-up relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
      <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-teal-500/10 blur-2xl" />
      <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-indigo-500/8 blur-2xl" />
      <div className="relative flex items-center gap-4">
        <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-teal-400/20 to-teal-600/10 ring-1 ring-teal-400/20">
          <Sparkles className="h-5 w-5 text-teal-300" />
        </span>
        <div>
          <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/80">Live operations signal</p>
          <p className="mt-1 text-sm font-medium leading-relaxed text-slate-600">
            {rows.length === 0
              ? `No ${config.eyebrow.toLowerCase()} records match your current filters.`
              : `${rows.length} ${config.eyebrow.toLowerCase()} records loaded — AI-powered risk detection and coaching recommendations enabled.`}
          </p>
        </div>
      </div>
      <div className="relative flex items-center gap-6 text-xs">
        <div className="flex items-center gap-2">
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
          <span className="text-slate-300">{rows.length} records</span>
        </div>
        <div className="flex items-center gap-2">
          <Shield className="h-3.5 w-3.5 text-teal-400" />
          <span className="text-slate-300">CSRF Protected</span>
        </div>
      </div>
    </div>

    {/* ── KPI cards ─────────────────────────────────────────── */}
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
      {config.kpis.slice(0, 5).map(([label, key]) => (
        <KpiCard key={key} label={label} value={String(s[key] ?? 0)} status={/critical|overdue|missing|rejected/i.test(label) ? "Critical" : undefined} />
      ))}
    </div>

    {/* ── Search & Filter toolbar ─────────────────────────── */}
    <div className="panel flex flex-col gap-3 p-3.5 lg:flex-row lg:items-center">
      <div className="relative min-w-[220px] flex-1 lg:max-w-md">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 shrink-0 -translate-y-1/2 text-slate-400" />
        <input className="field h-10 pl-9" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={`Search ${config.eyebrow.toLowerCase()} by driver, vehicle, route, event, status...`} />
      </div>
      <Select className="lg:max-w-[180px]" aria-label="Filter by status" value={filter} onChange={(e) => setFilter(e.target.value)}>
        <option value="All">All statuses</option><option>Critical</option><option>High</option><option>Pending</option><option>Reviewed</option><option>Open</option><option>Closed</option><option>Locked</option>
      </Select>
      <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-semibold text-slate-500">{rows.length} records</span>
    </div>

    {/* ── Data Table ──────────────────────────────────────── */}
    {!rows.length ? (
      <EmptyState title={`No ${config.eyebrow.toLowerCase()} records`} subtitle="Try another filter or create the first record." />
    ) : (
      <DataTable rows={rows} columns={config.columns} onSelect={setSelected} />
    )}

    {/* ── Drawer & Modal ─────────────────────────────────── */}
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
  return <div className="grid gap-4 lg:grid-cols-3">{rows.map((row) => <button key={String(row.id)} className="rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:border-violet-300 hover:shadow-md cursor-pointer" onClick={() => onSelect(row)}><div className="flex aspect-video items-center justify-center rounded-xl border border-slate-200 bg-slate-50 text-violet-600"><FileVideo className="h-10 w-10" /></div><p className="mt-3 font-semibold text-slate-900">{String(row.eventNumber)}</p><p className="text-sm text-slate-500">{String(row.aiSummary || row.eventType)}</p><div className="mt-3 flex gap-2"><StatusBadge status={row.reviewStatus} /><RiskBadge risk={row.severity} /></div></button>)}</div>;
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
  return <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in"><aside className="anim-slide-right flex h-full w-full max-w-3xl flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-2xl"><div className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur"><div className="flex items-start justify-between gap-4"><div><p className="section-title text-teal-700">{config.eyebrow} Detail</p><h2 className="mt-1 text-2xl font-bold text-slate-900">{String(record.eventNumber || record.taskNumber || record.incidentNumber || record.packageNumber || `Record ${record.id}`)}</h2><div className="mt-3 flex flex-wrap items-center gap-2"><StatusBadge status={record.status || record.reviewStatus} /><RiskBadge risk={record.severity || record.priority || record.riskScore} /></div></div><button type="button" className="icon-btn cursor-pointer" onClick={onClose} aria-label="Close"><X className="h-5 w-5" /></button></div><div className="mt-4 flex flex-wrap gap-2"><button type="button" className="fh-btn-primary h-9 py-0 cursor-pointer" disabled={!canUpdate} title={!canUpdate ? "You do not have permission to perform this action." : undefined} onClick={() => canUpdate && onEdit(record)}><PenTool className="h-4 w-4" /> Edit</button>{config.actions.map((type) => { const canAction = canRunAction(type); return <button key={type} type="button" className="fh-btn-ghost h-9 py-0 cursor-pointer" disabled={!canAction} title={!canAction ? "You do not have permission to perform this action." : undefined} onClick={() => canAction && onAction(type, record)}>{labelize(type)}</button>; })}<button type="button" className="fh-btn-ghost h-9 py-0 cursor-pointer" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : undefined} onClick={() => canExport && exportCsv(config.eyebrow, record ? [record] : [])}><Download className="h-4 w-4" /> Export</button></div></div><div className="space-y-6 px-6 py-6"><div className="grid gap-4 lg:grid-cols-3"><Info title="Primary Context" record={record} keys={Object.keys(record).slice(0,12)} /><Info title="Event Summary / Action" record={record} keys={["aiSummary","aiScript","summary","recommendedAction","reportSummary"]} /><Info title="Evidence / Legal Readiness" record={record} keys={["evidenceStatus","insuranceReportStatus","locked","exportUrl","falsePositive"]} /></div>{config.sections.map(([title,key,columns]) => <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) || []} columns={columns} />)}<Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /><div className="grid gap-3 lg:grid-cols-2">{((detail?.recommendations as AnyRecord[]) || []).slice(0,4).map((insight,i) => <AiInsightCard key={String(insight.id || i)} insight={insight} />)}</div></div></aside></div>;
}

function Modal({ title, fields, initial, saving, onClose, onSave }: { title: string; fields: string[][]; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in"><form className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6 shadow-2xl" onSubmit={submit}><div className="flex items-center justify-between border-b border-slate-200 pb-4"><h2 className="text-2xl font-bold text-slate-900">{title}</h2><button type="button" className="icon-btn cursor-pointer" onClick={onClose} aria-label="Close"><X className="h-5 w-5" /></button></div><div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label]) => <label key={key} className="block"><span className="mb-1.5 block text-xs font-bold uppercase tracking-[0.14em] text-slate-500">{label}</span><input className="field" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} /></label>)}</div><div className="mt-6 flex justify-end gap-3 border-t border-slate-200 pt-4"><button type="button" className="fh-btn-ghost cursor-pointer" onClick={onClose}>Cancel</button><button type="submit" className="fh-btn-primary cursor-pointer" disabled={saving}>{saving ? "Saving..." : "Save"}</button></div></form></div>;
}

function Info({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2.5">{keys.map((key) => <div key={key} className="flex items-start justify-between gap-3"><span className="text-xs font-medium text-slate-500">{labelize(key)}</span><span className="text-right text-sm font-medium text-slate-800">{String(record[key] ?? "--")}</span></div>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-2 rounded-xl border border-dashed border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-400">No records yet.</p> : <div className="mt-2 overflow-x-auto rounded-xl border border-slate-200"><table className="w-full min-w-[620px] text-left text-sm"><thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-4 py-2.5">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-slate-100">{rows.slice(0, 8).map((row,i) => <tr key={String(row.id || i)} className="hover:bg-slate-50 cursor-pointer transition-colors">{columns.map((c) => <td key={c} className="px-4 py-2.5 text-slate-600">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
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

