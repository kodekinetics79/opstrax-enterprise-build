import { FormEvent, ReactNode, useMemo, useState, useRef } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, FileVideo, Gavel, PackageCheck, PenTool, Plus, ShieldAlert, UserCheck, X } from "lucide-react";
import { AiInsightCard, DataTable, EmptyState, KpiCard, LoadingState, RiskBadge, StatusBadge, PageHeader, exportCsv, labelize } from "@/components/ui";
import { useCoachingSummary, useCoachingTaskDetail, useCoachingTasks, useDashcamEventDetail, useDashcamEvents, useDashcamProviderEvents, useDashcamProviderStatus, useDashcamSummary, useEvidencePackageDetail, useEvidencePackages, useEvidenceSummary, useIncidentDetail, useIncidents, useIncidentsSummary, useSafetyEventDetail, useSafetyEvents, useSafetySummary } from "@/hooks/useBatch4";
import { useHasDirectPermission, useHasPermission } from "@/hooks/usePermission";
import { useSingleFlight } from "@/hooks/useSingleFlight";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useAuth } from "@/hooks/useAuth";
import { CameraMetadataDialog, useCameraMetadataWorkflow } from "@/components/CameraMetadataDialog";
import { coachingApi } from "@/services/coachingApi";
import { CAMERA_NOTICE, cameraProjection, cameraRecord, dashcamApi } from "@/services/dashcamApi";
import type { CameraProviderPendingEvent, CameraProviderStatus } from "@/services/dashcamApi";
import { evidenceApi } from "@/services/evidenceApi";
import { incidentsApi } from "@/services/incidentsApi";
import { safetyApi } from "@/services/safetyApi";
import type { SafetyCoachingReceipt } from "@/services/safetyApi";
import type { AnyRecord, UserSession } from "@/types";

type Kind = "safety" | "dashcam" | "coaching" | "incidents" | "evidence";
type SafetyCoachingOrigin = { session: number; attempt: number; eventId: string | number; detailGeneration: number };
type SafetyCoachingAcknowledgement = SafetyCoachingOrigin & { generation: number; task: SafetyCoachingReceipt; refreshing: boolean; refreshWarning: boolean };
type CoachingModalOwner = { session: number; attempt: number; open: boolean; pending: boolean; recordId?: string | number; rowVersion?: string | number; dataIdentity?: unknown; detailGeneration: number };

const SAFETY_DETAIL_FIELDS = ["id", "rowVersion", "eventType", "severity", "status", "eventTime", "driverId", "driverName", "vehicleId", "vehicleCode", "notes", "reviewedAt", "reviewedByName", "resolvedAt", "resolvedByName"] as const;
const SAFETY_COACHING_FIELDS = ["id", "status", "coachingType", "dueDate", "assignedToName", "assignedByName", "completedAt", "driverAcknowledgedAt"] as const;
const SAFETY_AUDIT_FIELDS = ["actionName", "actorName", "createdAt"] as const;
const COACHING_DETAIL_FIELDS = ["id", "rowVersion", "taskNumber", "driverId", "driverName", "coachingType", "priority", "status", "assignedToUserId", "assignedToName", "assignedByName", "title", "description", "aiScript", "dueAt", "driverAcknowledged", "acknowledgedAt", "beforeSafetyScore", "afterSafetyScore", "effectivenessScore", "completedAt"] as const;
const COACHING_PRIMARY_FIELDS = ["taskNumber", "driverId", "driverName", "coachingType", "priority", "status", "assignedToUserId", "assignedToName", "assignedByName", "dueAt"] as const;
const COACHING_CONTENT_FIELDS = ["title", "description", "aiScript"] as const;
const COACHING_OUTCOME_FIELDS = ["driverAcknowledged", "acknowledgedAt", "beforeSafetyScore", "afterSafetyScore", "effectivenessScore", "completedAt"] as const;
const COACHING_CSV_FIELDS = ["id", "rowVersion", "taskNumber", "driverId", "driverName", "coachingType", "priority", "status", "assignedToName", "dueAt", "driverAcknowledged", "acknowledgedAt", "beforeSafetyScore", "afterSafetyScore", "effectivenessScore", "completedAt"] as const;
const COACHING_NOTE_FIELDS = ["id", "noteType", "noteText", "createdAt", "createdByName"] as const;
const COACHING_RELATED_SAFETY_FIELDS = ["id", "eventNumber", "eventType", "severity", "reviewStatus", "occurredAt"] as const;
const COACHING_RELATED_DASHCAM_FIELDS = ["id", "eventNumber", "eventType", "severity", "reviewStatus", "occurredAt"] as const;
const COACHING_AUDIT_FIELDS = ["actionName", "actorName", "createdAt"] as const;
const COACHING_DETAIL_UNAVAILABLE = "Current coaching detail could not be confirmed. Keep this draft open and reopen the action from the current task before submitting.";

function safetyDetailId(value: unknown): string | undefined {
  if (typeof value === "number") return Number.isSafeInteger(value) && value > 0 ? String(value) : undefined;
  if (typeof value !== "string" || !/^[1-9]\d{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n) return undefined;
  return value;
}

function safetyScalarProjection(value: unknown, fields: readonly string[]): AnyRecord | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const source = value as AnyRecord;
  const projected: AnyRecord = {};
  for (const field of fields) {
    if (!Object.hasOwn(source, field) || source[field] === undefined) continue;
    const item = source[field];
    if (item !== null && typeof item !== "string" && typeof item !== "boolean" && !(typeof item === "number" && Number.isFinite(item))) return undefined;
    projected[field] = item;
  }
  return Object.keys(projected).length ? projected : undefined;
}

export function safetyDetailView(value: unknown, selectedId: unknown): { record: AnyRecord; coachingTasks: AnyRecord[]; auditTrail: AnyRecord[] } | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const source = value as AnyRecord;
  const selectedKey = safetyDetailId(selectedId);
  if (!selectedKey || safetyDetailId(source.id) !== selectedKey ||
      !Number.isSafeInteger(source.rowVersion) || Number(source.rowVersion) < 0 ||
      typeof source.eventType !== "string" || !source.eventType.trim() ||
      typeof source.status !== "string" || !source.status.trim() ||
      !Array.isArray(source.coachingTasks) || !Array.isArray(source.auditTrail)) return undefined;
  const record = safetyScalarProjection(source, SAFETY_DETAIL_FIELDS);
  if (!record) return undefined;
  const coachingTasks = source.coachingTasks.map(row => safetyScalarProjection(row, SAFETY_COACHING_FIELDS));
  const auditTrail = source.auditTrail.map(row => safetyScalarProjection(row, SAFETY_AUDIT_FIELDS));
  if (coachingTasks.some(row => !row) || auditTrail.some(row => !row)) return undefined;
  return { record, coachingTasks: coachingTasks as AnyRecord[], auditTrail: auditTrail as AnyRecord[] };
}

function coachingScalarProjection(value: unknown, fields: readonly string[]): AnyRecord | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const source = value as AnyRecord;
  const projected: AnyRecord = {};
  for (const field of fields) {
    if (!Object.hasOwn(source, field) || source[field] === undefined) continue;
    const item = source[field];
    if (item !== null && typeof item !== "string" && typeof item !== "boolean" && !(typeof item === "number" && Number.isFinite(item))) return undefined;
    projected[field] = item;
  }
  return Object.keys(projected).length ? projected : undefined;
}

function coachingRowsProjection(source: AnyRecord, key: string, fields: readonly string[]): AnyRecord[] | undefined {
  if (!Object.hasOwn(source, key) || source[key] == null) return [];
  if (!Array.isArray(source[key])) return undefined;
  const rows = (source[key] as unknown[]).map(row => coachingScalarProjection(row, fields));
  return rows.some(row => !row) ? undefined : rows as AnyRecord[];
}

export function coachingDetailView(value: unknown, selectedId: unknown): AnyRecord | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const source = value as AnyRecord;
  if (!Object.hasOwn(source, "record") || !source.record || typeof source.record !== "object" || Array.isArray(source.record)) return undefined;
  const rawRecord = source.record as AnyRecord;
  const selectedKey = safetyDetailId(selectedId);
  if (!selectedKey || !Object.hasOwn(rawRecord, "id") || safetyDetailId(rawRecord.id) !== selectedKey ||
      !Object.hasOwn(rawRecord, "rowVersion") || !Number.isSafeInteger(rawRecord.rowVersion) || Number(rawRecord.rowVersion) < 0 ||
      !Object.hasOwn(rawRecord, "status") || typeof rawRecord.status !== "string" || !rawRecord.status.trim()) return undefined;
  const record = coachingScalarProjection(rawRecord, COACHING_DETAIL_FIELDS);
  const notes = coachingRowsProjection(source, "notes", COACHING_NOTE_FIELDS);
  const relatedSafetyEvents = coachingRowsProjection(source, "relatedSafetyEvents", COACHING_RELATED_SAFETY_FIELDS);
  const relatedDashcamEvents = coachingRowsProjection(source, "relatedDashcamEvents", COACHING_RELATED_DASHCAM_FIELDS);
  const auditTrail = coachingRowsProjection(source, "auditTrail", COACHING_AUDIT_FIELDS);
  if (!record || !notes || !relatedSafetyEvents || !relatedDashcamEvents || !auditTrail) return undefined;
  return { record, notes, relatedSafetyEvents, relatedDashcamEvents, auditTrail, recommendations: [] };
}

export function coachingCsvProjection(value: unknown): AnyRecord | undefined {
  return coachingScalarProjection(value, COACHING_CSV_FIELDS);
}

function entitlementAllows(session: Pick<UserSession, "entitlements" | "entitlementPolicyMode"> | null, key: string): boolean {
  if (!session) return false;
  if (session.entitlements && Object.prototype.hasOwnProperty.call(session.entitlements, key))
    return session.entitlements[key] === true;
  return (session.entitlementPolicyMode ?? "legacy_allow") === "legacy_allow";
}

export function coachingCompletionAdmission(
  record: AnyRecord | undefined,
  detailState: { selectedId?: string | number; isLoading: boolean; isFetching: boolean; isError: boolean },
): { allowed: boolean; reason?: string } {
  if (detailState.isLoading || detailState.isFetching || detailState.isError || !record ||
      detailState.selectedId == null || String(record.id) !== String(detailState.selectedId))
    return { allowed: false, reason: "Coaching details must finish loading successfully before completion." };
  if (!["driver acknowledged", "escalated"].includes(String(record.status ?? "").trim().toLowerCase()))
    return { allowed: false, reason: "This coaching task is not in a state that can be completed." };
  if (record.driverAcknowledged !== true || typeof record.acknowledgedAt !== "string" ||
      record.acknowledgedAt.trim() === "" || !Number.isFinite(Date.parse(record.acknowledgedAt)))
    return { allowed: false, reason: "A recorded driver acknowledgement is required before completion." };
  return { allowed: true };
}

export function coachingCompletionModalAdmission(
  admission: { allowed: boolean; reason?: string }, modalId: unknown, selectedId: unknown,
): { allowed: boolean; reason?: string } {
  if (modalId == null || selectedId == null || String(modalId) !== String(selectedId))
    return { allowed: false, reason: "The selected coaching task changed. Reopen completion for the current task." };
  return admission;
}

export function runCoachingCompletionAction(admission: { allowed: boolean }, operation: () => void): void {
  if (admission.allowed) operation();
}

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
    queryKey: "dashcam", eyebrow: "Camera Metadata", title: "Stored camera metadata", icon: <FileVideo />,
    description: CAMERA_NOTICE,
    useRows: useDashcamEvents, useSummary: useDashcamSummary, useDetail: useDashcamEventDetail, api: dashcamApi, createLabel: "Record Event Metadata",
    kpis: [["Stored event records","storedEventRecords"]],
    columns: ["eventNumber","eventType","title","recordedLevel","driverName","vehicleCode","jobNumber","routeCode","locationDescription","occurredAt"],
    fields: [],
    actions: [],
    sections: [],
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
    create: "dashcam:manage",
    update: "dashcam:manage",
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

export function CameraProviderStatusPanel({ status }: { status: CameraProviderStatus }) {
  const title = status.status === "AwaitingProviderConnection" ? "Awaiting provider connection"
    : status.status === "AttentionRequired" ? "Provider intake needs attention"
      : "Provider data pending verification";
  const message = status.status === "AwaitingProviderConnection"
    ? "No provider intake records have been observed for this scope. This account currently has no provider-backed camera evidence."
    : status.status === "AttentionRequired"
      ? "Provider intake records require reconciliation. Quarantined records are excluded from customer event claims."
      : "Provider intake records exist, but provider authenticity, media access and certification remain unverified.";
  const lastReceipt = status.lastOpsTraxIntakeUtc === null ? "Never observed" : new Date(status.lastOpsTraxIntakeUtc).toLocaleString();
  return <section className="rounded-2xl border border-amber-300 bg-amber-50 p-5" aria-labelledby="camera-provider-status-title">
    <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
      <div>
        <p className="text-xs font-bold uppercase tracking-[0.16em] text-amber-800">Camera provider intake</p>
        <h2 id="camera-provider-status-title" className="mt-1 text-lg font-semibold text-slate-900">{title}</h2>
        <p className="mt-2 max-w-3xl text-sm text-slate-700">{message}</p>
      </div>
      <div className="text-sm text-slate-700">
        <p><strong>Verification:</strong> External hold</p>
        <p><strong>Certification:</strong> External hold</p>
        <p><strong>Last OpsTrax intake:</strong> {lastReceipt}</p>
      </div>
    </div>
    <dl className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
      {[
        ["Observed events", status.observedEventCount],
        ["Matched pending", status.matchedEventCount],
        ["Unmatched", status.unmatchedEventCount],
        ["Quarantined", status.quarantinedEventCount],
        ["Pending media", status.pendingMediaCount],
      ].map(([label, value]) => <div key={String(label)} className="rounded-xl border border-amber-200 bg-white px-4 py-3">
        <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</dt>
        <dd className="mt-1 text-xl font-semibold text-slate-900">{value}</dd>
      </div>)}
    </dl>
    <p className="mt-3 text-xs text-slate-600">Provider verified: No · Media available: No · Expired media references: {status.expiredMediaCount}</p>
  </section>;
}

export function CameraProviderPendingEventsPanel({ rows }: { rows: readonly CameraProviderPendingEvent[] }) {
  if (rows.length === 0) return null;
  return <section className="rounded-2xl border border-slate-200 bg-white p-5" aria-labelledby="camera-provider-events-title">
    <div>
      <p className="text-xs font-bold uppercase tracking-[0.16em] text-amber-700">Unverified provider records</p>
      <h2 id="camera-provider-events-title" className="mt-1 text-lg font-semibold text-slate-900">Camera safety intake queue</h2>
      <p className="mt-2 max-w-4xl text-sm text-slate-600">These records are separate from stored camera metadata. They cannot be reviewed, coached, exported, or used as certification evidence until provider, media, privacy, and device checks pass.</p>
    </div>
    <div className="mt-4 overflow-x-auto">
      <table className="min-w-full text-left text-sm">
        <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500"><tr>
          <th className="px-3 py-2">Event</th><th className="px-3 py-2">Occurred</th><th className="px-3 py-2">Vehicle</th><th className="px-3 py-2">Reconciliation</th><th className="px-3 py-2">Evidence</th>
        </tr></thead>
        <tbody>{rows.map((row) => <tr key={row.intakeReference} className="border-b border-slate-100 last:border-0">
          <td className="px-3 py-3 font-medium text-slate-900">{row.eventType}</td>
          <td className="px-3 py-3 text-slate-700">{new Date(row.occurredAtUtc).toLocaleString()}</td>
          <td className="px-3 py-3 text-slate-700">{row.vehicleCode ?? "Not matched"}</td>
          <td className="px-3 py-3 text-slate-700">{row.reconciliationStatus}</td>
          <td className="px-3 py-3"><span className="rounded-full border border-amber-300 bg-amber-50 px-2 py-1 text-xs font-semibold text-amber-800">External hold</span></td>
        </tr>)}</tbody>
      </table>
    </div>
  </section>;
}

export function Batch4SafetyPage({ kind }: { kind: Kind }) {
  const { session } = useAuth();
  const config = configs[kind];
  const hasPermission = useHasPermission();
  const hasDirectPermission = useHasDirectPermission();
  const canReadSafetyCoaching = () => hasPermission("safety:view") && entitlementAllows(session, "safety") && entitlementAllows(session, "fleet.driver_safety");
  const canMutate = (permission: string) => hasDirectPermission(permission);
  const createPermission = ACTION_PERMISSIONS[kind].create;
  const updatePermission = ACTION_PERMISSIONS[kind].update;
  const exportPermission = ACTION_PERMISSIONS[kind].export;
  const rowsQuery = config.useRows();
  const summary = config.useSummary();
  const providerStatus = useDashcamProviderStatus(kind === "dashcam");
  const providerEvents = useDashcamProviderEvents(kind === "dashcam");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const safetyDetailOwner = useRef({ generation: 0, id: undefined as string | number | undefined, retrying: false });
  const [safetyDetailRetrying, setSafetyDetailRetrying] = useState(false);
  const safetyCoachingOwner = useRef({ session: 0, attempt: 0, draftRevision: 0, open: false, pending: false, eventId: undefined as string | number | undefined, detailGeneration: 0 });
  const safetyCoachingDraftRevision = safetyCoachingOwner.current.draftRevision;
  const [safetyCoachingDialog, setSafetyCoachingDialog] = useState<{ session: number; eventId: string | number } | null>(null);
  const [safetyCoachingDescription, setSafetyCoachingDescription] = useState("");
  const [safetyCoachingPending, setSafetyCoachingPending] = useState(false);
  const [safetyCoachingError, setSafetyCoachingError] = useState(false);
  const safetyCoachingReceiptOwner = useRef({ generation: 0, reading: false });
  const [safetyCoachingAcknowledgement, setSafetyCoachingAcknowledgement] = useState<SafetyCoachingAcknowledgement | null>(null);
  const coachingDetailOwner = useRef({ generation: 0, id: undefined as string | number | undefined, retrying: false });
  const [coachingDetailRetrying, setCoachingDetailRetrying] = useState(false);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const coachingEditorOwner = useRef({ session: 0, attempt: 0, pending: false, open: false, recordId: undefined as string | number | undefined, rowVersion: undefined as string | number | undefined, detailGeneration: 0 });
  const [coachingEditorSession, setCoachingEditorSession] = useState(0);
  const [coachingEditorSaving, setCoachingEditorSaving] = useState(false);
  const [coachingEditorError, setCoachingEditorError] = useState<unknown>(null);
  const [incidentAction, setIncidentAction] = useState<{ type: "status" | "attachEvidence"; row: AnyRecord } | null>(null);
  const [coachingNoteAction, setCoachingNoteAction] = useState<AnyRecord | null>(null);
  const coachingNoteOwner = useRef<CoachingModalOwner>({ session: 0, attempt: 0, open: false, pending: false, detailGeneration: 0 });
  const [coachingNoteSession, setCoachingNoteSession] = useState(0);
  const [coachingNoteUnavailable, setCoachingNoteUnavailable] = useState(false);
  const coachingNoteFlight = useRef(false);
  const [coachingCompleteAction, setCoachingCompleteAction] = useState<AnyRecord | null>(null);
  const coachingCompleteOwner = useRef<CoachingModalOwner>({ session: 0, attempt: 0, open: false, pending: false, detailGeneration: 0 });
  const [coachingCompleteSession, setCoachingCompleteSession] = useState(0);
  const [coachingCompleteUnavailable, setCoachingCompleteUnavailable] = useState(false);
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("All");
  const detail = config.useDetail(kind === "safety" && !safetyDetailId(selected?.id) ? undefined : selected?.id as string | number | undefined);
  const qc = useQueryClient();
  const safetyDetailGeneration = safetyDetailOwner.current.generation;
  const coachingDetailGeneration = coachingDetailOwner.current.generation;
  const selectRecord = (row: AnyRecord | null) => {
    if (kind === "safety") {
      safetyDetailOwner.current = { generation: safetyDetailOwner.current.generation + 1, id: row?.id as string | number | undefined, retrying: false };
      setSafetyDetailRetrying(false);
    }
    if (kind === "coaching") {
      coachingDetailOwner.current = { generation: coachingDetailOwner.current.generation + 1, id: row?.id as string | number | undefined, retrying: false };
      setCoachingDetailRetrying(false);
      if (coachingEditorOwner.current.open && !coachingEditorOwner.current.pending) coachingEditorOwner.current.open = false;
    }
    setSelected(row);
  };
  const currentSafetyDetail = () => {
    const owner = safetyDetailOwner.current;
    if (owner.generation !== safetyDetailGeneration || !safetyDetailId(owner.id) || owner.retrying) return undefined;
    const query = qc.getQueryState(["safety", "detail", owner.id]);
    return query?.status === "success" && query.fetchStatus === "idle" ? safetyDetailView(query.data, owner.id) : undefined;
  };
  const currentSafetyActionRecord = (type: string) => {
    if (type !== "edit" && type !== "export" && !configs.safety.actions.includes(type)) return undefined;
    const record = currentSafetyDetail()?.record;
    if (!record || action.isPending) return undefined;
    if (type === "export") return hasPermission(exportPermission) ? record : undefined;
    return canMutate(type === "edit" ? updatePermission : ACTION_PERMISSIONS.safety[type] || updatePermission) ? record : undefined;
  };
  const retrySafetyDetail = () => {
    const owner = safetyDetailOwner.current;
    if (owner.generation !== safetyDetailGeneration || !safetyDetailId(owner.id) || owner.retrying) return;
    if (qc.getQueryState(["safety", "detail", owner.id])?.fetchStatus === "fetching") return;
    const generation = ++owner.generation;
    owner.retrying = true;
    setSafetyDetailRetrying(true);
    void detail.refetch().finally(() => {
      if (safetyDetailOwner.current.generation !== generation) return;
      safetyDetailOwner.current.retrying = false;
      setSafetyDetailRetrying(false);
    });
  };
  const safetyDetailData = kind === "safety" ? currentSafetyDetail() : undefined;
  const safetyDetailState = !selected ? "closed"
    : !safetyDetailId(selected.id) ? "unavailable"
    : safetyDetailRetrying || detail.isLoading || detail.isFetching ? "loading"
    : detail.isSuccess && safetyDetailData ? "ready" : "unavailable";
  const currentCoachingDetail = () => {
    const owner = coachingDetailOwner.current;
    if (owner.generation !== coachingDetailGeneration || !owner.id || !String(owner.id).trim() || owner.retrying) return undefined;
    const query = qc.getQueryState<AnyRecord>(["coaching", "detail", owner.id]);
    return query?.status === "success" && query.fetchStatus === "idle" ? coachingDetailView(query.data, owner.id) : undefined;
  };
  const currentCoachingActionRecord = (type: string) => {
    const record = currentCoachingDetail()?.record as AnyRecord | undefined;
    if (!record || action.isPending) return undefined;
    if (type === "export") return hasPermission(exportPermission) ? record : undefined;
    if (!canMutate(type === "edit" ? updatePermission : ACTION_PERMISSIONS.coaching[type] || updatePermission)) return undefined;
    const status = String(record.status || "").trim().toLowerCase();
    if (type === "edit" && (coachingEditorOwner.current.pending || /completed|cancelled/i.test(status))) return undefined;
    if (type === "assign" && !["draft", "open", "escalated"].includes(status)) return undefined;
    if (type === "complete" && !coachingCompletionAdmission(record, {
      selectedId: coachingDetailOwner.current.id, isLoading: false, isFetching: false, isError: false,
    }).allowed) return undefined;
    return record;
  };
  const currentCoachingModalRecord = (owner: CoachingModalOwner, sessionId: number, type: "addNote" | "complete") => {
    if (!owner.open || owner.pending || owner.session !== sessionId || owner.recordId == null || owner.detailGeneration !== coachingDetailOwner.current.generation) return undefined;
    const query = qc.getQueryState<AnyRecord>(["coaching", "detail", owner.recordId]);
    if (query?.data !== owner.dataIdentity) return undefined;
    const record = currentCoachingActionRecord(type);
    if (!record || String(record.id) !== String(owner.recordId) ||
        String(record.rowVersion) !== String(owner.rowVersion)) return undefined;
    return record;
  };
  const closeDetail = () => {
    if (kind === "safety" && safetyDetailOwner.current.generation !== safetyDetailGeneration) return;
    if (kind === "coaching" && coachingDetailOwner.current.generation !== coachingDetailGeneration) return;
    selectRecord(null);
  };
  const retryCoachingDetail = () => {
    const owner = coachingDetailOwner.current;
    if (owner.generation !== coachingDetailGeneration || !owner.id || owner.retrying) return;
    const query = qc.getQueryState(["coaching", "detail", owner.id]);
    if (query?.fetchStatus === "fetching") return;
    const generation = ++owner.generation;
    owner.retrying = true;
    setCoachingDetailRetrying(true);
    void detail.refetch().finally(() => {
      if (coachingDetailOwner.current.generation !== generation) return;
      coachingDetailOwner.current.retrying = false;
      setCoachingDetailRetrying(false);
    });
  };
  const coachingDetailData = kind === "coaching" ? currentCoachingDetail() : undefined;
  const coachingDetailState = !selected ? "closed"
    : coachingDetailRetrying || detail.isLoading || detail.isFetching ? "loading"
    : detail.isSuccess && coachingDetailData ? "ready" : "unavailable";
  const coachingCompletionAccess = coachingCompletionAdmission(coachingDetailData?.record as AnyRecord | undefined, {
    selectedId: selected?.id as string | number | undefined,
    isLoading: detail.isLoading, isFetching: detail.isFetching, isError: detail.isError,
  });
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
  }, onSuccess: async () => { if (kind !== "coaching") { setEditing(null); await invalidate(); } } });
  const action = useMutation({
    ...(kind === "safety" ? { retry: false } : {}),
    mutationFn: ({ type, row, payload }: { type: string; row: AnyRecord; payload?: AnyRecord; safetyCoachingOrigin?: SafetyCoachingOrigin }) => runAction(kind, type, row, payload),
    onSuccess: async (result, variables) => {
      if (kind === "safety" && variables.type === "createCoaching") {
        const origin = variables.safetyCoachingOrigin;
        if (!origin || !ownsSafetyCoaching(origin.session) || safetyCoachingOwner.current.attempt !== origin.attempt) return;
        safetyCoachingOwner.current.open = false;
        setSafetyCoachingDialog(null);
        setSafetyCoachingDescription("");
        setSafetyCoachingError(false);
        const generation = ++safetyCoachingReceiptOwner.current.generation;
        const acknowledgement = { ...origin, generation, task: result as SafetyCoachingReceipt, refreshing: false, refreshWarning: false };
        setSafetyCoachingAcknowledgement(acknowledgement);
        await refreshSafetyCoachingReceipt(acknowledgement, false);
        return;
      }
      setIncidentAction(null);
      await invalidate();
    },
  });
  const coachingNoteUnavailableReason = coachingNoteAction && !coachingNoteOwner.current.pending &&
    !currentCoachingModalRecord(coachingNoteOwner.current, coachingNoteSession, "addNote") ? COACHING_DETAIL_UNAVAILABLE : undefined;
  const coachingCompleteOwnerUnavailable = Boolean(coachingCompleteAction && !coachingCompleteOwner.current.pending &&
    !currentCoachingModalRecord(coachingCompleteOwner.current, coachingCompleteSession, "complete"));
  const coachingCompletionModalAccess = coachingCompleteUnavailable || coachingCompleteOwnerUnavailable
    ? { allowed: false, reason: COACHING_DETAIL_UNAVAILABLE }
    : coachingCompletionModalAdmission(coachingCompletionAccess, coachingCompleteAction?.id, selected?.id);
  const ownsSafetyCoaching = (sessionId: number) => safetyCoachingOwner.current.open && safetyCoachingOwner.current.session === sessionId;
  const safetyCoachingAdmission = (sessionId: number) => {
    const owner = safetyCoachingOwner.current;
    if (!ownsSafetyCoaching(sessionId) || owner.pending || owner.detailGeneration !== safetyDetailOwner.current.generation) return undefined;
    const row = currentSafetyActionRecord("createCoaching");
    return row && safetyDetailId(row.id) === safetyDetailId(owner.eventId) ? row : undefined;
  };
  const openSafetyCoaching = () => {
    if (safetyCoachingOwner.current.pending) return;
    void actionSingleFlight(async () => {
      const row = currentSafetyActionRecord("createCoaching");
      if (!row) return;
      const owner = safetyCoachingOwner.current;
      owner.session += 1;
      owner.attempt = 0;
      owner.draftRevision = 0;
      owner.open = true;
      owner.eventId = row.id as string | number;
      owner.detailGeneration = safetyDetailOwner.current.generation;
      safetyCoachingReceiptOwner.current = { generation: safetyCoachingReceiptOwner.current.generation + 1, reading: false };
      setSafetyCoachingAcknowledgement(null);
      setSafetyCoachingDialog({ session: owner.session, eventId: owner.eventId });
      setSafetyCoachingDescription("");
      setSafetyCoachingError(false);
      action.reset();
    });
  };
  const closeSafetyCoaching = (sessionId: number) => {
    if (!ownsSafetyCoaching(sessionId) || safetyCoachingOwner.current.pending) return;
    void actionSingleFlight(async () => {
      if (!ownsSafetyCoaching(sessionId) || safetyCoachingOwner.current.pending) return;
      safetyCoachingOwner.current.open = false;
      setSafetyCoachingDialog(null);
      setSafetyCoachingDescription("");
      setSafetyCoachingError(false);
      action.reset();
    });
  };
  const submitSafetyCoaching = (sessionId: number, draftRevision: number, description: string) => {
    if (!safetyCoachingAdmission(sessionId) || safetyCoachingOwner.current.draftRevision !== draftRevision || typeof description !== "string" || !description.trim() || description.trim().length > 4000) return;
    let admitted: SafetyCoachingOrigin | undefined;
    void actionSingleFlight(async () => {
      const row = safetyCoachingAdmission(sessionId);
      if (!row || safetyCoachingOwner.current.draftRevision !== draftRevision) return;
      const owner = safetyCoachingOwner.current;
      admitted = { session: sessionId, attempt: ++owner.attempt, eventId: owner.eventId!, detailGeneration: owner.detailGeneration };
      owner.pending = true;
      setSafetyCoachingPending(true);
      setSafetyCoachingError(false);
      try {
        await action.mutateAsync({ type: "createCoaching", row, payload: { notes: description.trim() }, safetyCoachingOrigin: admitted });
      } catch {
        if (ownsSafetyCoaching(sessionId) && owner.attempt === admitted.attempt) setSafetyCoachingError(true);
      }
    }).finally(() => {
      if (admitted && safetyCoachingOwner.current.session === admitted.session && safetyCoachingOwner.current.attempt === admitted.attempt) {
        safetyCoachingOwner.current.pending = false;
        setSafetyCoachingPending(false);
      }
    });
  };
  const refreshSafetyCoachingReceipt = async (receipt: SafetyCoachingAcknowledgement, explicit: boolean) => {
    const owner = safetyCoachingReceiptOwner.current;
    if (owner.generation !== receipt.generation || owner.reading) return;
    owner.reading = true;
    const update = (refreshing: boolean, refreshWarning: boolean) => {
      if (safetyCoachingReceiptOwner.current.generation === receipt.generation)
        setSafetyCoachingAcknowledgement(current => current?.generation === receipt.generation ? { ...current, refreshing, refreshWarning } : current);
    };
    update(true, receipt.refreshWarning);
    const keys = [["safety"], ["safety", "summary"], ["safety", "detail", receipt.eventId], ["coaching"], ["coaching", "summary"]];
    let stopWatching: (() => void) | undefined;
    try {
      if (!canReadSafetyCoaching()) {
        update(false, true);
        return;
      }
      const paused = new Promise<null>(resolve => {
        const observePause = () => { if (keys.some(queryKey => qc.getQueryState(queryKey)?.fetchStatus === "paused")) resolve(null); };
        stopWatching = qc.getQueryCache().subscribe(observePause);
        observePause();
      });
      if (keys.some(queryKey => qc.getQueryState(queryKey)?.fetchStatus === "paused")) {
        update(false, true);
        return;
      }
      const reads = Promise.allSettled(keys.map(queryKey => Promise.resolve().then<unknown>(() =>
        explicit && queryKey[1] === "detail"
          ? qc.fetchQuery({ queryKey, queryFn: () => safetyApi.detail(receipt.eventId), staleTime: 0 })
          : qc.invalidateQueries({ queryKey, exact: true }, { throwOnError: true }))));
      const results = await Promise.race([reads, paused]);
      const query = qc.getQueryState(["safety", "detail", receipt.eventId]);
      const unconfirmed = query?.status !== "success" || query.fetchStatus !== "idle" || query.isInvalidated || !safetyDetailView(query.data, receipt.eventId);
      update(false, results === null || results.some(result => result.status === "rejected") || Boolean(unconfirmed));
    } catch {
      update(false, true);
    } finally {
      stopWatching?.();
      if (safetyCoachingReceiptOwner.current.generation === receipt.generation) owner.reading = false;
    }
  };
  const ownsCoachingEditor = (sessionId: number) => coachingEditorOwner.current.open && coachingEditorOwner.current.session === sessionId;
  const coachingEditorRecordIsCurrent = (owner = coachingEditorOwner.current) => {
    if (owner.recordId == null) return true;
    if (owner.detailGeneration !== coachingDetailOwner.current.generation) return false;
    const current = currentCoachingDetail()?.record as AnyRecord | undefined;
    return Boolean(current && String(current.id) === String(owner.recordId) && String(current.rowVersion ?? current.row_version) === String(owner.rowVersion));
  };
  const canChangeCoachingDraft = (sessionId: number) => ownsCoachingEditor(sessionId) && !coachingEditorOwner.current.pending && coachingEditorRecordIsCurrent();
  const openRecordEditor = (record: AnyRecord) => {
    if (kind === "coaching") {
      const owner = coachingEditorOwner.current;
      if (owner.pending) return;
      owner.session += 1;
      owner.open = true;
      owner.recordId = record.id as string | number | undefined;
      owner.rowVersion = (record.rowVersion ?? record.row_version) as string | number | undefined;
      owner.detailGeneration = coachingDetailOwner.current.generation;
      setCoachingEditorSession(owner.session);
      setCoachingEditorSaving(false);
      setCoachingEditorError(null);
      save.reset();
    }
    setEditing(record);
  };
  const closeCoachingEditor = (sessionId: number) => {
    if (!canChangeCoachingDraft(sessionId)) return;
    coachingEditorOwner.current.open = false;
    save.reset();
    setCoachingEditorError(null);
    setEditing(null);
  };
  const saveCoachingEditor = (sessionId: number, payload: AnyRecord) => {
    if (!canChangeCoachingDraft(sessionId)) return;
    const owner = coachingEditorOwner.current;
    if (owner.recordId != null && (String(payload.id) !== String(owner.recordId) || String(payload.rowVersion ?? payload.row_version) !== String(owner.rowVersion))) return;
    const attempt = ++owner.attempt;
    owner.pending = true;
    setCoachingEditorSaving(true);
    setCoachingEditorError(null);
    void saveSingleFlight(async () => {
      try {
        await save.mutateAsync(payload);
        if (ownsCoachingEditor(sessionId) && owner.attempt === attempt && coachingEditorRecordIsCurrent(owner)) {
          owner.open = false;
          setEditing(null);
          await invalidate();
        }
      } catch (error) {
        if (ownsCoachingEditor(sessionId) && owner.attempt === attempt && coachingEditorRecordIsCurrent(owner)) setCoachingEditorError(error);
        throw error;
      }
    }).finally(() => {
      if (owner.session === sessionId && owner.attempt === attempt) {
        owner.pending = false;
        setCoachingEditorSaving(false);
      }
    });
  };
  const operationError = (kind === "coaching" ? coachingEditorError : save.error) || (kind === "safety" && action.variables?.type === "createCoaching" ? null : action.error);
  const rows = useMemo(() => (kind === "dashcam" ? (Array.isArray(rowsQuery.data) ? rowsQuery.data : []).map(cameraProjection).filter((row): row is AnyRecord => row !== null) : (rowsQuery.data || [])).filter((row) => {
    const searchLower = search.toLowerCase();
    const filterLower = filter.toLowerCase();
    const matchesSearch = !search || 
      String(row.eventNumber || row.taskNumber || row.incidentNumber || row.packageNumber || "").toLowerCase().includes(searchLower) ||
      String(row.driverName || row.vehicleCode || row.jobNumber || row.routeCode || "").toLowerCase().includes(searchLower) ||
      String(row.eventType || row.coachingType || row.incidentType || "").toLowerCase().includes(searchLower);

    const statusVal = String(row.status || row.reviewStatus || row.severity || "").toLowerCase();
    const matchesFilter = filter === "All" || statusVal.includes(filterLower);
    return matchesSearch && matchesFilter;
  }), [rowsQuery.data, search, filter, kind]);
  const camera = useCameraMetadataWorkflow({ enabled: kind === "dashcam", session, canManage: canMutate("dashcam:manage"), canExport: hasPermission(exportPermission), selectedId: selected?.id,
    visibleIds: rows.map((row) => String(row.id)), detail, rows: rowsQuery, queryClient: qc });
  const cameraNotice = kind === "dashcam" && camera.notice ? <div role="status" className="rounded-xl border border-slate-300 bg-slate-50 p-4 text-sm">
    Server returned a manual metadata acknowledgement for record {camera.notice.receipt.id}. This is not independent confirmation of durable commit or media verification.
    {camera.warning ? <p>Display refresh is not confirmed. Inspect current records before submitting again.</p> : <p>Metadata reads refreshed.</p>}
    <button type="button" className="btn-ghost mt-2" disabled={camera.refreshing || camera.pending} onClick={() => { if (camera.notice) void camera.refresh(camera.notice); }}>{camera.refreshing ? "Refreshing metadata…" : "Refresh metadata reads"}</button>
  </div> : null;
  const safetyCoachingReceipt = kind === "safety" && safetyCoachingAcknowledgement ? <SafetyCoachingReceiptNotice receipt={safetyCoachingAcknowledgement} canView={canReadSafetyCoaching()} onRetry={() => { void refreshSafetyCoachingReceipt(safetyCoachingAcknowledgement, true); }} /> : null;
  const safetyCoachingInput = kind === "safety" && safetyCoachingDialog ? <SafetyCoachingInputDialog key={safetyCoachingDialog.session} eventId={safetyCoachingDialog.eventId} description={safetyCoachingDescription} saving={safetyCoachingPending} error={safetyCoachingError} admitted={Boolean(safetyCoachingAdmission(safetyCoachingDialog.session))} onChange={(value) => {
    if (safetyCoachingAdmission(safetyCoachingDialog.session) && safetyCoachingOwner.current.draftRevision === safetyCoachingDraftRevision) {
      safetyCoachingOwner.current.draftRevision += 1;
      setSafetyCoachingDescription(value);
    }
  }} onClose={() => closeSafetyCoaching(safetyCoachingDialog.session)} onSubmit={() => submitSafetyCoaching(safetyCoachingDialog.session, safetyCoachingDraftRevision, safetyCoachingDescription)} /> : null;
  const safetyCoachingFeedback = <>{safetyCoachingReceipt}{safetyCoachingInput}</>;
  if (rowsQuery.isLoading || summary.isLoading || (kind === "dashcam" && (providerStatus.isLoading || providerEvents.isLoading))) return kind === "safety" ? <>{safetyCoachingFeedback}<LoadingState /></> : <LoadingState />;
  if (rowsQuery.isError || summary.isError || (kind === "dashcam" && (providerStatus.isError || providerEvents.isError || !providerStatus.data || !providerEvents.data || !Array.isArray(rowsQuery.data)))) {
    const unavailable = <div>{cameraNotice}<EmptyState
      title={`${config.eyebrow} unavailable`}
      subtitle="Unable to load live records right now. No empty or healthy state has been inferred."
      action={<button type="button" className="btn-secondary" disabled={rowsQuery.isFetching || summary.isFetching || providerStatus.isFetching || providerEvents.isFetching} onClick={() => { void rowsQuery.refetch(); void summary.refetch(); if (kind === "dashcam") { void providerStatus.refetch(); void providerEvents.refetch(); } }}>{rowsQuery.isFetching || summary.isFetching || providerStatus.isFetching || providerEvents.isFetching ? "Retrying…" : "Retry live data"}</button>}
    /></div>;
    return kind === "safety" ? <>{safetyCoachingFeedback}{unavailable}</> : unavailable;
  }
  const s = (summary.data || {}) as AnyRecord;
  return <div className="fleet-console space-y-3">
    {safetyCoachingReceipt}
    <PageHeader
      eyebrow={config.eyebrow}
      title={config.title}
      description={config.description}
      actions={
        <>
          <button
            className="btn-primary"
            disabled={!canMutate(createPermission) || (kind === "dashcam" && camera.pending) || (kind === "coaching" && coachingEditorSaving)}
            title={!canMutate(createPermission) ? "You do not have permission to perform this action." : `Create a new ${config.eyebrow.toLowerCase()} record.`}
            onClick={() => { if (kind === "dashcam") camera.open(); else openRecordEditor(defaultForm(kind)); }}
          >
            <Plus className="h-4 w-4" /> {config.createLabel}
          </button>
          <button
            className="btn-ghost"
            disabled={!hasPermission(exportPermission) || (kind === "dashcam" && (rowsQuery.isFetching || rowsQuery.fetchStatus !== "idle" || camera.pending))}
            title={!hasPermission(exportPermission) ? "You do not have permission to perform this action." : "Export the current filtered records."}
            onClick={() => { if (kind === "dashcam") camera.exportCurrent("list"); else exportCsv(kind, rows); }}
          >
            <Download className="h-4 w-4" /> Export Report
          </button>
        </>
      }
    />
    {kind === "dashcam" ? <CameraProviderStatusPanel status={providerStatus.data!} /> : null}
    {kind === "dashcam" ? <CameraProviderPendingEventsPanel rows={providerEvents.data!} /> : null}
    {cameraNotice}
    {operationError && kind !== "dashcam" ? <div role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">{operationError instanceof Error ? operationError.message : "The incident action could not be completed."}</div> : null}
    {kind === "dashcam" && Array.isArray(rowsQuery.data) && rowsQuery.data.some((row) => !cameraProjection(row)) ? <p role="alert">Some stored metadata is unavailable because its identity or fields cannot be interpreted safely. It cannot be edited or exported.</p> : null}
    <div className="grid gap-6 sm:grid-cols-3 xl:grid-cols-5">{config.kpis.slice(0, 5).map(([label,key]) => <KpiCard key={key} label={label} value={kind === "dashcam" ? (typeof s[key] === "number" && Number.isSafeInteger(s[key]) && Number(s[key]) >= 0 ? String(s[key]) : "Unavailable") : String(s[key] ?? 0)} status={/critical|overdue|missing|rejected/i.test(label) ? "Critical" : undefined} />)}</div>
    <div className="flex flex-col gap-3 xl:flex-row xl:items-center"><input className="field xl:max-w-md" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={`Search ${config.eyebrow.toLowerCase()} by driver, vehicle, route, event, status...`} /><select className="field xl:max-w-[180px]" value={filter} onChange={(e) => setFilter(e.target.value)}><option>All</option><option>Critical</option><option>High</option><option>Pending</option><option>Reviewed</option><option>Open</option><option>Closed</option><option>Locked</option></select></div>
    {!rows.length ? (
      <EmptyState title={`No ${config.eyebrow.toLowerCase()} records`} subtitle={kind === "dashcam" ? "No provider event has been projected into this view. Manual entries remain explicitly unverified." : "Try another filter or create the first record."} />
    ) : (
      <DataTable rows={kind === "dashcam" ? rows.map(({ severity, ...metadata }) => ({ ...metadata, recordedLevel: severity ?? "Unavailable" })) : rows} columns={config.columns} onSelect={(row) => { if (kind !== "dashcam" || camera.canLeave()) selectRecord(row); }} />
    )}
    <Drawer
      kind={kind}
      config={config}
      detail={kind === "safety" ? safetyDetailState === "ready" ? safetyDetailData : undefined : kind === "coaching" ? coachingDetailState === "ready" ? coachingDetailData : undefined : detail.data}
      loading={detail.isLoading}
      safetyRead={kind === "safety" ? { state: safetyDetailState, onRetry: retrySafetyDetail, onExport: () => {
        void actionSingleFlight(async () => {
          const current = currentSafetyActionRecord("export");
          if (current) exportCsv(config.eyebrow, [safetyScalarProjection(current, SAFETY_DETAIL_FIELDS.filter(field => field !== "notes"))!]);
        });
      } } : undefined}
      coachingRead={kind === "coaching" ? { state: coachingDetailState, onRetry: retryCoachingDetail, onExport: () => {
        void actionSingleFlight(async () => {
          const current = currentCoachingActionRecord("export");
          const projected = coachingCsvProjection(current);
          if (projected) exportCsv(config.eyebrow, [projected]);
        });
      } } : undefined}
      canUpdate={kind === "dashcam" ? camera.canEdit : canMutate(updatePermission) && (kind === "safety" ? Boolean(currentSafetyActionRecord("edit")) : kind !== "coaching" || (!coachingEditorSaving && Boolean(currentCoachingActionRecord("edit"))))}
      canExport={hasPermission(exportPermission) && (kind !== "dashcam" || camera.detailReady)}
      actionPending={kind === "dashcam" ? camera.pending : action.isPending}
      cameraReady={kind !== "dashcam" || camera.detailReady}
      cameraOpen={kind === "dashcam" && selected !== null}
      onCameraExport={() => camera.exportCurrent("detail")}
      onCameraRefresh={() => { void detail.refetch(); }}
      canRunAction={(type) => {
        if (kind === "dashcam") return false;
        const permission = ACTION_PERMISSIONS[kind][type] || updatePermission;
        if (!canMutate(permission)) return false;
        if (kind === "safety") return configs.safety.actions.includes(type) && Boolean(currentSafetyActionRecord(type));
        if (kind === "coaching") {
          const status = String((coachingDetailData?.record as AnyRecord | undefined)?.status || "").trim().toLowerCase();
          if (type === "assign" && !["draft", "open", "escalated"].includes(status)) return false;
          if (type === "complete" && !coachingCompletionAccess.allowed) return false;
          return Boolean(currentCoachingActionRecord(type));
        }
        if (kind === "incidents" && type === "status") {
          const evidenceReady = ((detail.data?.evidence as AnyRecord[] | undefined) || []).some((item) => /^https:\/\//i.test(String(item.evidenceUrl || "")) && /^[0-9a-f]{64}$/i.test(String(item.contentHash || "")));
          return incidentNextStatuses(String((detail.data?.record as AnyRecord | undefined)?.status || selected?.status || ""), evidenceReady).length > 0;
        }
        return true;
      }}
      onClose={() => { if (kind === "dashcam") { if (camera.canLeave()) selectRecord(null); } else if (kind === "safety" || kind === "coaching") closeDetail(); else selectRecord(null); }}
      onEdit={(row) => {
        if (kind === "dashcam") { camera.open(row); return; }
        if (kind === "safety") {
          void actionSingleFlight(async () => {
            const current = currentSafetyActionRecord("edit");
            if (current) openRecordEditor(current);
          });
          return;
        }
        if (kind !== "coaching") { openRecordEditor(row); return; }
        void actionSingleFlight(async () => {
          const current = currentCoachingActionRecord("edit");
          if (current) openRecordEditor(current);
        });
      }}
      onAction={(type, row) => {
        if (kind === "dashcam") return;
        if (kind === "safety") {
          if (!configs.safety.actions.includes(type)) return;
          const current = currentSafetyActionRecord(type);
          if (!current) return;
          row = current;
          if (type === "createCoaching") { openSafetyCoaching(); return; }
        }
        if (kind === "coaching") {
          const current = currentCoachingActionRecord(type);
          if (!current) return;
          row = current;
        }
        if (kind === "incidents" && (type === "status" || type === "attachEvidence")) {
          const evidenceReady = ((detail.data?.evidence as AnyRecord[] | undefined) || []).some((item) => /^https:\/\//i.test(String(item.evidenceUrl || "")) && /^[0-9a-f]{64}$/i.test(String(item.contentHash || "")));
          action.reset();
          setIncidentAction({ type, row: { ...row, evidenceReady } });
        }
        else if (kind === "coaching" && type === "addNote") {
          if (coachingNoteFlight.current || action.isPending) return;
          void actionSingleFlight(async () => {
            const owner = coachingNoteOwner.current;
            owner.session += 1;
            owner.attempt = 0;
            owner.open = true;
            owner.pending = false;
            owner.recordId = row.id as string | number;
            owner.rowVersion = row.rowVersion as string | number;
            owner.dataIdentity = qc.getQueryState(["coaching", "detail", row.id])?.data;
            owner.detailGeneration = coachingDetailOwner.current.generation;
            setCoachingNoteSession(owner.session);
            setCoachingNoteUnavailable(false);
            action.reset();
            setCoachingNoteAction(row);
          });
        }
        else if (kind === "coaching" && type === "complete") { void actionSingleFlight(async () => {
          runCoachingCompletionAction(coachingCompletionAdmission(row, { selectedId: coachingDetailOwner.current.id, isLoading: false, isFetching: false, isError: false }), () => {
            const owner = coachingCompleteOwner.current;
            owner.session += 1;
            owner.attempt = 0;
            owner.open = true;
            owner.pending = false;
            owner.recordId = row.id as string | number;
            owner.rowVersion = row.rowVersion as string | number;
            owner.dataIdentity = qc.getQueryState(["coaching", "detail", row.id])?.data;
            owner.detailGeneration = coachingDetailOwner.current.generation;
            setCoachingCompleteSession(owner.session);
            setCoachingCompleteUnavailable(false);
            setCoachingCompleteAction(row);
          });
        }); }
        else void actionSingleFlight(() => action.mutateAsync({ type, row }));
      }}
    />
    {safetyCoachingInput}
    {camera.editor ? <CameraMetadataDialog editor={camera.editor} pending={camera.pending} error={camera.error} onChange={camera.change} onClose={camera.close} onSubmit={(editor) => { void camera.submit(editor); }} /> : null}
    {editing && kind !== "dashcam" ? <Modal key={kind === "coaching" ? coachingEditorSession : undefined} kind={kind} title={config.createLabel} fields={config.fields} initial={editing} saving={kind === "coaching" ? coachingEditorSaving : save.isPending} error={kind === "coaching" ? coachingEditorError : save.error} draftAdmission={kind === "coaching" ? () => canChangeCoachingDraft(coachingEditorSession) : undefined} onClose={() => {
      if (kind === "coaching") { closeCoachingEditor(coachingEditorSession); return; }
      save.reset(); setEditing(null);
    }} onSave={(payload) => {
      if (kind === "coaching") { saveCoachingEditor(coachingEditorSession, payload); return; }
      void saveSingleFlight(() => save.mutateAsync(payload));
    }} /> : null}
    {incidentAction ? <IncidentActionModal action={incidentAction} saving={action.isPending} error={action.error} onClose={() => { action.reset(); setIncidentAction(null); }} onSubmit={(payload) => { void actionSingleFlight(() => action.mutateAsync({ type: incidentAction.type, row: incidentAction.row, payload })); }} /> : null}
    {coachingNoteAction ? <CoachingNoteModal key={coachingNoteSession} saving={action.isPending} error={action.error} unavailableReason={coachingNoteUnavailableReason} onClose={() => {
      const owner = coachingNoteOwner.current;
      if (!owner.open || owner.session !== coachingNoteSession || owner.pending || coachingNoteFlight.current || action.isPending) return;
      void actionSingleFlight(async () => {
        if (!owner.open || owner.session !== coachingNoteSession || owner.pending) return;
        owner.open = false;
        setCoachingNoteUnavailable(false);
        action.reset();
        setCoachingNoteAction(null);
      });
    }} onSubmit={(noteText) => {
      const owner = coachingNoteOwner.current;
      if (coachingNoteFlight.current || owner.pending || action.isPending) return;
      void actionSingleFlight(async () => {
        const current = currentCoachingModalRecord(owner, coachingNoteSession, "addNote");
        if (!current) {
          if (owner.open && owner.session === coachingNoteSession) setCoachingNoteUnavailable(true);
          return;
        }
        const attempt = ++owner.attempt;
        owner.pending = true;
        coachingNoteFlight.current = true;
        setCoachingNoteUnavailable(false);
        try {
          await action.mutateAsync({ type: "addNote", row: current, payload: { noteText } });
          if (owner.open && owner.session === coachingNoteSession && owner.attempt === attempt) {
            owner.open = false;
            setCoachingNoteAction(null);
          }
        } finally {
          if (owner.session === coachingNoteSession && owner.attempt === attempt) owner.pending = false;
          coachingNoteFlight.current = false;
        }
      });
    }} /> : null}
    {coachingCompleteAction ? <CoachingCompleteModal key={coachingCompleteSession} saving={action.isPending} error={action.error} unavailableReason={coachingCompletionModalAccess.reason} onClose={() => {
      const owner = coachingCompleteOwner.current;
      if (!owner.open || owner.session !== coachingCompleteSession || owner.pending || action.isPending) return;
      void actionSingleFlight(async () => {
        if (!owner.open || owner.session !== coachingCompleteSession || owner.pending) return;
        owner.open = false;
        setCoachingCompleteUnavailable(false);
        action.reset();
        setCoachingCompleteAction(null);
      });
    }} onSubmit={(payload) => {
      const owner = coachingCompleteOwner.current;
      if (owner.pending || action.isPending) return;
      void actionSingleFlight(async () => {
        const current = currentCoachingModalRecord(owner, coachingCompleteSession, "complete");
        if (!current) {
          if (owner.open && owner.session === coachingCompleteSession) setCoachingCompleteUnavailable(true);
          return;
        }
        const attempt = ++owner.attempt;
        owner.pending = true;
        setCoachingCompleteUnavailable(false);
        try {
          await action.mutateAsync({ type: "complete", row: current, payload });
          if (owner.open && owner.session === coachingCompleteSession && owner.attempt === attempt) {
            owner.open = false;
            setCoachingCompleteAction(null);
          }
        } finally {
          if (owner.session === coachingCompleteSession && owner.attempt === attempt) owner.pending = false;
        }
      });
    }} /> : null}
  </div>;
}

function VideoGrid({ rows, onSelect }: { rows: AnyRecord[]; onSelect: (row: AnyRecord) => void }) {
  return <div className="grid gap-4 lg:grid-cols-3">{rows.map((row) => <button key={String(row.id)} className="panel p-4 text-left transition hover:border-violet-400/40" onClick={() => onSelect(row)}><div className="flex aspect-video items-center justify-center rounded-xl border border-white/10 bg-slate-900 text-violet-200"><FileVideo className="h-10 w-10" /></div><p className="mt-3 font-semibold text-slate-900">{String(row.eventNumber)}</p><p className="text-sm text-slate-400">{String(row.aiSummary || row.eventType)}</p><div className="mt-3 flex gap-2"><StatusBadge status={row.reviewStatus} /><RiskBadge risk={row.severity} /></div></button>)}</div>;
}

function Drawer({
  kind: _kind,
  config,
  detail,
  loading,
  coachingRead,
  safetyRead,
  canUpdate,
  canExport,
  actionPending,
  canRunAction,
  onClose,
  onEdit,
  onAction,
  cameraReady = true,
  cameraOpen = false,
  onCameraExport,
  onCameraRefresh,
}: {
  kind: Kind;
  config: (typeof configs)[Kind];
  detail?: AnyRecord;
  loading: boolean;
  coachingRead?: { state: "closed" | "loading" | "unavailable" | "ready"; onRetry: () => void; onExport: () => void };
  safetyRead?: { state: "closed" | "loading" | "unavailable" | "ready"; onRetry: () => void; onExport: () => void };
  canUpdate: boolean;
  canExport: boolean;
  actionPending: boolean;
  canRunAction: (type: string) => boolean;
  onClose: () => void;
  onEdit: (record: AnyRecord) => void;
  onAction: (type: string, row: AnyRecord) => void;
  cameraReady?: boolean;
  cameraOpen?: boolean;
  onCameraExport?: () => void;
  onCameraRefresh?: () => void;
}) {
  const record = detail?.record as AnyRecord | undefined;
  const detailDialogRef = useDialogFocus<HTMLDivElement>(config.queryKey === "dashcam" ? cameraOpen : safetyRead ? safetyRead.state !== "closed" : coachingRead ? coachingRead.state !== "closed" : Boolean(record), onClose);
  if (config.queryKey === "dashcam") {
    if (!cameraOpen) return null;
    const metadata = cameraProjection(record);
    const source = cameraRecord(record)?.source ?? "Source authority unavailable";
    return <div ref={detailDialogRef} className="fixed inset-0 z-50 flex justify-end bg-black/50" role="dialog" aria-modal="true" aria-label="Camera metadata detail"><aside className="h-full w-full max-w-3xl overflow-y-auto bg-slate-950 p-6 text-slate-100">
      <button className="btn-ghost float-right" onClick={onClose}>Close detail</button><h2 className="text-xl font-semibold">Stored camera metadata</h2>
      <p className="mt-3 text-sm">{CAMERA_NOTICE}</p><p className="mt-2 text-sm">{cameraReady ? source : "Current source authority unavailable"}</p>
      <p className="mt-2 text-sm">Review, coaching, evidence and report workflows are not assessed in this view.</p>
      <div className="mt-4 flex gap-3"><button className="btn-primary" disabled={!canUpdate || actionPending} onClick={() => { if (record && canUpdate && !actionPending) onEdit(record); }}>Edit manual metadata</button><button className="btn-ghost" disabled={!canExport || actionPending} onClick={() => { if (canExport && !actionPending) onCameraExport?.(); }}>Export metadata</button></div>
      {!cameraReady || !metadata ? <div role="alert" className="mt-5"><p>Current metadata is unavailable. Cached data cannot authorize editing or export.</p><button className="btn-ghost" onClick={onCameraRefresh}>Retry metadata read</button></div>
        : <div className="mt-5"><Info title="Stored event context" record={metadata} keys={Object.keys(metadata).filter((key) => key !== "metadataNotice")} /></div>}
    </aside></div>;
  }
  if (safetyRead) {
    if (safetyRead.state === "closed") return null;
    const pending = safetyRead.state === "loading";
    const ready = safetyRead.state === "ready" && record;
    return <div ref={detailDialogRef} className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm" role="dialog" aria-modal="true" aria-label="Safety event detail">
      <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6">
        <button className="float-right icon-btn" aria-label="Close detail" onClick={onClose}><X className="h-5 w-5" /></button>
        <h2 className="text-2xl font-semibold text-white">Safety event metadata</h2>
        {!ready ? <>
          <p role={pending ? "status" : "alert"} className="mt-6 text-slate-200">{pending ? "Loading current safety event detail…" : "Current safety event detail could not be confirmed. Retry the read before using this event."}</p>
          {!pending ? <button className="btn-ghost mt-4" onClick={safetyRead.onRetry}>Retry detail</button> : null}
        </> : <>
          <p role="note" className="mt-4 text-sm text-slate-300">Stored event metadata and returned related records only. These fields do not verify footage, automated assessment, source provenance, driver acknowledgement, or reviewer attribution.</p>
          <div className="mt-5 flex flex-wrap gap-3">
            <button className="btn-primary" disabled={!canUpdate || actionPending} onClick={() => onEdit(record)}><PenTool className="h-4 w-4" /> Edit</button>
            {config.actions.map((type) => <button key={type} className="btn-ghost" disabled={!canRunAction(type) || actionPending} aria-busy={actionPending} onClick={() => onAction(type, record)}>{labelize(type)}</button>)}
            <button className="btn-ghost" disabled={!canExport || actionPending} onClick={safetyRead.onExport}><Download className="h-4 w-4" /> Export Report</button>
          </div>
          <div className="mt-6"><Info title="Stored Event Metadata" record={record} keys={Object.keys(record)} /></div>
          <Grid title="Returned Coaching Records" rows={detail!.coachingTasks as AnyRecord[]} columns={[...SAFETY_COACHING_FIELDS]} />
          <Grid title="Returned Audit Records" rows={detail!.auditTrail as AnyRecord[]} columns={[...SAFETY_AUDIT_FIELDS]} />
        </>}
      </aside>
    </div>;
  }
  if (coachingRead && coachingRead.state !== "ready") {
    if (coachingRead.state === "closed") return null;
    const pending = coachingRead.state === "loading";
    return <div ref={detailDialogRef} className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm" role="dialog" aria-modal="true" aria-label="Coaching detail">
      <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6">
        <button className="float-right icon-btn" aria-label="Close detail" onClick={onClose}><X className="h-5 w-5" /></button>
        <h2 className="text-2xl font-semibold text-white">Coaching detail</h2>
        <p role={pending ? "status" : "alert"} className="mt-6 text-slate-200">{pending ? "Loading current coaching detail…" : "Current coaching detail could not be confirmed. Retry the read before using this task."}</p>
        {!pending ? <button className="btn-ghost mt-4" onClick={coachingRead.onRetry}>Retry detail</button> : null}
      </aside>
    </div>;
  }
  if (!record && !loading) return null;
  if (!record) return null;
  const exportDetail = () => {
    if (coachingRead) { coachingRead.onExport(); return; }
    exportCsv(config.eyebrow, record ? [record] : []);
  };
  return <div ref={detailDialogRef} className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm" role="dialog" aria-modal="true" aria-label={`${config.eyebrow} detail`}><aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6"><button className="float-right icon-btn" aria-label="Close detail" onClick={onClose}><X className="h-5 w-5" /></button><p className="section-title text-teal-300">{config.eyebrow} Detail</p><h2 className="mt-3 text-2xl font-semibold text-white">{String(record.eventNumber || record.taskNumber || record.incidentNumber || record.packageNumber || `Record ${record.id}`)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status || record.reviewStatus} /><RiskBadge risk={record.severity || record.priority || record.riskScore} /></div><div className="mt-5 flex flex-wrap gap-3"><button className="btn-primary" disabled={!canUpdate || actionPending} title={!canUpdate ? "You do not have permission to perform this action." : "Edit this record."} onClick={() => onEdit(record)}><PenTool className="h-4 w-4" /> Edit</button>{config.actions.map((type) => { const canAction = canRunAction(type); return <button key={type} className="btn-ghost" disabled={!canAction || actionPending} aria-busy={actionPending} title={!canAction ? "You do not have permission to perform this action." : `Run ${labelize(type)}.`} onClick={() => onAction(type, record)}>{labelize(type)}</button>; })}<button className="btn-ghost" disabled={!canExport || actionPending} title={!canExport ? "You do not have permission to perform this action." : "Export this record."} onClick={exportDetail}><Download className="h-4 w-4" /> Export Report</button></div>{coachingRead ? <div className="mt-6 grid gap-4 lg:grid-cols-3"><Info title="Stored Task Context" record={record} keys={[...COACHING_PRIMARY_FIELDS]} /><Info title="Coaching Content" record={record} keys={[...COACHING_CONTENT_FIELDS]} /><Info title="Acknowledgement / Outcome" record={record} keys={[...COACHING_OUTCOME_FIELDS]} /></div> : <div className="mt-6 grid gap-4 lg:grid-cols-3"><Info title="Primary Context" record={record} keys={Object.keys(record).slice(0,12)} /><Info title="Event Summary / Action" record={record} keys={["aiSummary","aiScript","summary","recommendedAction","reportSummary"]} /><Info title="Evidence / Legal Readiness" record={record} keys={["evidenceStatus","insuranceReportStatus","locked","exportUrl","falsePositive"]} /></div>}{config.sections.map(([title,key,columns]) => <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) || []} columns={columns} />)}<Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={coachingRead ? [...COACHING_AUDIT_FIELDS] : ["actionName","actorName","createdAt"]} /><div className="mt-6 grid gap-4 lg:grid-cols-2">{((detail?.recommendations as AnyRecord[]) || []).slice(0,4).map((insight,i) => <AiInsightCard key={String(insight.id || i)} insight={insight} />)}</div></aside></div>;
}

function Modal({ kind, title, fields, initial, saving, error, draftAdmission, onClose, onSave }: { kind: Kind; title: string; fields: string[][]; initial: AnyRecord; saving: boolean; error?: unknown; draftAdmission?: () => boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const recordDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [form, setForm] = useState<AnyRecord>(initial);
  const changeDraft = (next: AnyRecord | ((current: AnyRecord) => AnyRecord)) => {
    if (draftAdmission && !draftAdmission()) return;
    setForm(next);
  };
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
    if (draftAdmission && !draftAdmission()) return;
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
  const visibleError = error ? kind === "coaching" ? "Coaching save could not be confirmed. Inspect the current task before manually retrying; the earlier request may already have been applied." : error instanceof Error ? error.message : "The record could not be saved." : "";
  const draftLocked = Boolean(draftAdmission && saving);
  return <div ref={recordDialogRef} className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby={titleId}><form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><div><h2 id={titleId} className="text-2xl font-semibold text-slate-900">{title}</h2>{kind === "coaching" ? <p className="mt-1 text-sm text-slate-600">Driver, coaching type, title, and description are required.</p> : isIncidentCreate ? <p className="mt-1 text-sm text-slate-600">Type, severity, occurrence time, location, summary, and at least one driver, vehicle, safety event, or dashcam event are required.</p> : null}</div><button type="button" className="icon-btn" aria-label="Close record dialog" disabled={draftLocked} onClick={onClose}><X /></button></div>{draftLocked ? <p role="status" className="mt-4 text-sm text-slate-600">Saving this draft. Editing and closing are paused until the request settles.</p> : null}{visibleError ? <div role="alert" className="mt-4 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">{visibleError}</div> : null}<div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label]) => { const required = requiredFields.includes(key); const inputId = `safety-record-${key}`; const immutableDriver = kind === "coaching" && key === "driverId" && Boolean(form.id); const numeric = ["driverId", "vehicleId", "safetyEventId", "dashcamEventId", "jobId", "routeId"].includes(key); const textarea = ["description", "aiScript", "aiSummary"].includes(key); return <label key={key} htmlFor={inputId}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}{required ? " *" : ""}</span>{textarea ? <textarea id={inputId} className="field min-h-24" required={required} disabled={draftLocked} maxLength={key === "description" || key === "aiSummary" ? 4000 : undefined} value={String(form[key] ?? "")} onChange={(e) => changeDraft((x) => ({ ...x, [key]: e.target.value }))} /> : <input id={inputId} className="field" required={required} disabled={immutableDriver || draftLocked} min={numeric ? 1 : undefined} type={key === "occurredAt" ? "datetime-local" : numeric ? "number" : "text"} maxLength={key === "title" ? 220 : key === "coachingType" ? 120 : undefined} value={String(form[key] ?? "")} onChange={(e) => changeDraft((x) => ({ ...x, [key]: e.target.value }))} />}{immutableDriver ? <span className="mt-1 block text-xs text-slate-500">Driver ownership cannot be changed after creation.</span> : null}</label>; })}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" disabled={draftLocked} onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || missingRequired} aria-disabled={saving || missingRequired}>{saving ? "Saving…" : "Save"}</button></div></form></div>;
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

function SafetyCoachingInputDialog({ eventId, description, saving, error, admitted, onChange, onClose, onSubmit }: {
  eventId: string | number; description: string; saving: boolean; error: boolean; admitted: boolean;
  onChange: (value: string) => void; onClose: () => void; onSubmit: () => void;
}) {
  const dialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const valid = description.trim().length > 0 && description.trim().length <= 4000;
  return <div ref={dialogRef} className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-busy={saving} aria-labelledby="safety-coaching-input-title">
    <form className="panel w-full max-w-lg p-6" onSubmit={event => { event.preventDefault(); if (admitted && valid && !saving) onSubmit(); }}>
      <div className="flex items-center justify-between"><h2 id="safety-coaching-input-title" className="text-xl font-semibold text-slate-900">Create coaching for safety event {String(eventId)}</h2><button type="button" className="icon-btn" aria-label="Close safety coaching dialog" disabled={saving} onClick={onClose}><X /></button></div>
      <p className="mt-3 text-sm text-slate-600">Enter your coaching description. Stored event notes and automated text are not copied.</p>
      {error ? <p role="alert" className="mt-4 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-950">Coaching creation could not be confirmed. Inspect existing coaching records before manually resubmitting. An earlier request may already have saved a task; changed text may conflict rather than update that task.</p> : null}
      {!admitted && !saving ? <p role="status" className="mt-3 text-sm text-amber-800">Current safety event access must be confirmed. Reopen coaching from the current event if the selection changed.</p> : null}
      <label className="mt-5 block"><span className="mb-2 block text-xs font-bold uppercase tracking-wide text-slate-500">Coaching description (1–4000 characters after trimming)</span><textarea autoFocus className="field min-h-32" required disabled={saving || !admitted} value={description} onChange={event => onChange(event.target.value)} /></label>
      <div className="mt-5 flex justify-end gap-3"><button type="button" className="btn-ghost" disabled={saving} onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || !admitted || !valid}>{saving ? "Submitting…" : "Create coaching"}</button></div>
    </form>
  </div>;
}

function SafetyCoachingReceiptNotice({ receipt, canView, onRetry }: { receipt: SafetyCoachingAcknowledgement; canView: boolean; onRetry: () => void }) {
  return <section className="rounded-xl border border-teal-300 bg-teal-50 px-4 py-3 text-sm text-teal-950" aria-label="Safety coaching acknowledgement">
    <p role="status">{receipt.task.replayed ? "Existing coaching task returned" : "Coaching task created"} for safety event {String(receipt.eventId)}. Task {String(receipt.task.id)}.</p>
    {canView ? <a className="font-semibold underline" href={`/coaching?taskId=${receipt.task.id}`}>Open coaching task</a> : null}
    {receipt.refreshWarning ? <><p role="alert" className="mt-2">Coaching acknowledgement received; related views could not be refreshed. This does not change the recorded acknowledgement.</p><button type="button" className="btn-ghost mt-2" disabled={receipt.refreshing || !canView} onClick={onRetry}>{receipt.refreshing ? "Refreshing related views…" : "Retry related reads"}</button></> : null}
  </section>;
}

function CoachingNoteModal({ saving, error, unavailableReason, onClose, onSubmit }: { saving: boolean; error: unknown; unavailableReason?: string; onClose: () => void; onSubmit: (note: string) => void }) {
  const coachingNoteDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [note, setNote] = useState("");
  return <div ref={coachingNoteDialogRef} className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-busy={saving} aria-labelledby="coaching-note-title">
    <form className="panel w-full max-w-lg p-6" onSubmit={(event) => { event.preventDefault(); if (!saving && !unavailableReason && note.trim()) onSubmit(note.trim()); }}>
      <div className="flex items-center justify-between"><h2 id="coaching-note-title" className="text-xl font-semibold text-slate-900">Add coaching note</h2><button type="button" className="icon-btn" aria-label="Close coaching note dialog" disabled={saving} onClick={onClose}><X /></button></div>
      {error ? <div role="alert" className="mt-4 rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950">Note save could not be confirmed. Inspect the existing notes for this coaching task before manually submitting again. The earlier request may already have saved the note.</div> : null}
      {unavailableReason ? <p role="status" className="mt-4 text-sm text-amber-800">{unavailableReason}</p> : null}
      <label className="mt-5 block"><span className="mb-2 block text-xs font-bold uppercase tracking-wide text-slate-500">Note</span><textarea autoFocus className="field min-h-32" required disabled={saving} value={note} onChange={(event) => setNote(event.target.value)} /></label>
      <div className="mt-5 flex justify-end gap-3"><button type="button" className="btn-ghost" disabled={saving} onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || Boolean(unavailableReason) || !note.trim()}>{saving ? "Saving…" : "Add note"}</button></div>
    </form>
  </div>;
}

function CoachingCompleteModal({ saving, error, unavailableReason, onClose, onSubmit }: { saving: boolean; error: unknown; unavailableReason?: string; onClose: () => void; onSubmit: (payload: AnyRecord) => void }) {
  const coachingCompleteDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [completionNote, setCompletionNote] = useState("");
  const [afterSafetyScore, setAfterSafetyScore] = useState("");
  const score = Number(afterSafetyScore);
  const valid = !unavailableReason && completionNote.trim().length > 0 && completionNote.trim().length <= 4000 && afterSafetyScore.trim() !== "" && Number.isFinite(score) && score >= 0 && score <= 100;
  return <div ref={coachingCompleteDialogRef} className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="coaching-complete-title">
    <form className="panel w-full max-w-xl p-6" onSubmit={(event) => { event.preventDefault(); if (valid) onSubmit({ completionNote: completionNote.trim(), afterSafetyScore: score }); }}>
      <div className="flex items-start justify-between gap-4"><div><h2 id="coaching-complete-title" className="text-xl font-semibold text-slate-900">Complete coaching task</h2><p className="mt-2 text-sm text-slate-600">Document the observed outcome and current safety score. The comparison is observational and does not prove coaching caused the score change.</p></div><button type="button" className="icon-btn" aria-label="Close coaching completion dialog" onClick={onClose}><X /></button></div>
      {error ? <div role="alert" className="mt-4 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">{error instanceof Error ? error.message : "The coaching task could not be completed."}</div> : null}
      {unavailableReason ? <p role="status" className="mt-4 text-sm text-amber-800">{unavailableReason}</p> : null}
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
  if (kind === "dashcam") return { eventType: "", title: "", severity: "" };
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
    if (type === "createCoaching") return safetyApi.createCoaching(id, { notes: String(payload?.notes ?? "").trim() });
    return safetyApi.createIncident(id);
  }
  if (kind === "dashcam") return;
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
