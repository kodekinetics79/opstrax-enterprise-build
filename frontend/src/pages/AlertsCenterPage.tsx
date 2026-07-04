import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import {
  AlertTriangle,
  ArrowRight,
  BadgeCheck,
  BellRing,
  Clock3,
  RefreshCw,
  Search,
  Shield,
  ShieldAlert,
  Sparkles,
  Wrench,
  Zap,
} from "lucide-react";
import { alertsApi } from "@/services/alertsApi";
import { useHasPermission } from "@/hooks/usePermission";
import { EmptyState, ErrorState, exportCsv, LoadingState } from "@/components/ui";
import type { AnyRecord } from "@/types";

type Alert = {
  id: string | number;
  alertId?: string;
  title?: string;
  body?: string;
  severity: "Critical" | "High" | "Warning" | "Info";
  status: string;
  category: string;
  alertType?: string;
  entity?: string;
  entityType?: string;
  entityRoute?: string;
  customer?: string;
  owner?: string;
  location?: string;
  age?: string;
  recommendedAction?: string;
  acknowledgedAt?: string;
  closedAt?: string;
  acknowledgedBy?: string;
  createdAt?: string;
};

type AlertsSummary = {
  total: number;
  critical: number;
  high: number;
  open: number;
  acknowledged: number;
  closed: number;
};

type AlertTask = {
  id: string | number;
  title: string;
  description?: string;
  priority?: string;
  status?: string;
  owner?: string;
  dueAt?: string;
  createdAt?: string;
};

type AlertAuditEntry = {
  id: string | number;
  actionName?: string;
  actorName?: string;
  createdAt?: string;
};

type AlertDetailRecord = {
  alert: Alert | null;
  tasks: AlertTask[];
  auditTrail: AlertAuditEntry[];
};

type ActionType = "acknowledge" | "close" | "task" | null;

const SEVERITY_ORDER: Record<string, number> = { Critical: 0, High: 1, Warning: 2, Info: 3 };
const CATEGORIES = ["All", "Safety", "Maintenance", "Customer", "Compliance", "Telematics", "Operations"] as const;
const STATUS_FILTERS = ["All", "Open", "Acknowledged", "Closed"] as const;
const SEVERITY_FILTERS = ["All", "Critical", "High", "Warning", "Info"] as const;

function normalizeAlert(raw: AnyRecord): Alert {
  const severity = String(raw.severity ?? "Info") as Alert["severity"];
  return {
    id: (raw.id ?? raw.alertId) as string | number,
    alertId: String(raw.alertId ?? raw.id ?? ""),
    title: String(raw.title ?? raw.type ?? "Alert"),
    body: String(raw.body ?? ""),
    severity,
    status: String(raw.status ?? "Open"),
    category: String(raw.category ?? "Operations"),
    alertType: String(raw.alertType ?? raw.alert_type ?? raw.type ?? ""),
    entity: raw.entity != null ? String(raw.entity) : undefined,
    entityType: raw.entityType != null ? String(raw.entityType) : raw.entity_type != null ? String(raw.entity_type) : undefined,
    entityRoute: raw.entityRoute != null ? String(raw.entityRoute) : raw.entity_route != null ? String(raw.entity_route) : undefined,
    customer: raw.customer != null ? String(raw.customer) : undefined,
    owner: raw.owner != null ? String(raw.owner) : undefined,
    location: raw.location != null ? String(raw.location) : undefined,
    age: raw.age != null ? String(raw.age) : undefined,
    recommendedAction: raw.recommendedAction != null ? String(raw.recommendedAction) : raw.recommended_action != null ? String(raw.recommended_action) : undefined,
    acknowledgedAt: raw.acknowledgedAt != null ? String(raw.acknowledgedAt) : undefined,
    closedAt: raw.closedAt != null ? String(raw.closedAt) : undefined,
    acknowledgedBy: raw.acknowledgedBy != null ? String(raw.acknowledgedBy) : undefined,
    createdAt: raw.createdAt != null ? String(raw.createdAt) : undefined,
  };
}

function normalizeTask(raw: AnyRecord): AlertTask {
  return {
    id: (raw.id ?? "") as string | number,
    title: String(raw.title ?? "Follow-up task"),
    description: raw.description != null ? String(raw.description) : undefined,
    priority: raw.priority != null ? String(raw.priority) : undefined,
    status: raw.status != null ? String(raw.status) : undefined,
    owner: raw.ownerName != null ? String(raw.ownerName) : raw.assignedToName != null ? String(raw.assignedToName) : raw.owner_name != null ? String(raw.owner_name) : undefined,
    dueAt: raw.dueAt != null ? String(raw.dueAt) : raw.due_at != null ? String(raw.due_at) : undefined,
    createdAt: raw.createdAt != null ? String(raw.createdAt) : raw.created_at != null ? String(raw.created_at) : undefined,
  };
}

function normalizeAuditEntry(raw: AnyRecord): AlertAuditEntry {
  return {
    id: (raw.id ?? "") as string | number,
    actionName: raw.actionName != null ? String(raw.actionName) : raw.action_name != null ? String(raw.action_name) : undefined,
    actorName: raw.actorName != null ? String(raw.actorName) : raw.actor_name != null ? String(raw.actor_name) : undefined,
    createdAt: raw.createdAt != null ? String(raw.createdAt) : raw.created_at != null ? String(raw.created_at) : undefined,
  };
}

function normalizeAlertDetail(raw: AnyRecord | null | undefined): AlertDetailRecord {
  if (!raw) return { alert: null, tasks: [], auditTrail: [] };
  const alertRaw = raw.alert && typeof raw.alert === "object" ? raw.alert as AnyRecord : raw;
  return {
    alert: normalizeAlert(alertRaw),
    tasks: Array.isArray(raw.tasks) ? raw.tasks.map((entry) => normalizeTask(entry as AnyRecord)) : [],
    auditTrail: Array.isArray(raw.auditTrail) ? raw.auditTrail.map((entry) => normalizeAuditEntry(entry as AnyRecord)) : [],
  };
}

function statusClass(status: string) {
  if (/open/i.test(status)) return "bg-red-50 border-red-200 text-red-700";
  if (/ack/i.test(status)) return "bg-violet-50 border-violet-200 text-violet-700";
  if (/progress/i.test(status)) return "bg-blue-50 border-blue-200 text-blue-700";
  if (/closed/i.test(status)) return "bg-slate-100 border-slate-200 text-slate-600";
  return "bg-slate-100 border-slate-200 text-slate-600";
}

function severityTone(severity: string) {
  if (/critical/i.test(severity)) return "border-red-200 bg-red-50/90";
  if (/high/i.test(severity)) return "border-orange-200 bg-orange-50/90";
  if (/warning/i.test(severity)) return "border-amber-200 bg-amber-50/90";
  return "border-sky-200 bg-sky-50/90";
}

function ageHours(createdAt?: string) {
  if (!createdAt) return 0;
  const created = new Date(createdAt).getTime();
  if (Number.isNaN(created)) return 0;
  return Math.max(0, (Date.now() - created) / 3_600_000);
}

function routeForCategory(category: string) {
  if (category === "Safety") return "/safety";
  if (category === "Maintenance") return "/maintenance";
  if (category === "Customer") return "/customers";
  if (category === "Compliance") return "/compliance";
  if (category === "Telematics") return "/iot-devices";
  return "/alerts";
}

function ActionModal({
  type,
  alert,
  onClose,
  onConfirm,
}: {
  type: ActionType;
  alert: Alert | null;
  onClose: () => void;
  onConfirm: (payload: AnyRecord) => void;
}) {
  const [note, setNote] = useState("");

  if (!type || !alert) return null;

  const title =
    type === "acknowledge" ? "Acknowledge alert" :
    type === "close" ? "Close alert" :
    "Create follow-up task";

  const label =
    type === "acknowledge" ? "Ops note" :
    type === "close" ? "Resolution summary" :
    "Task title";

  const buttonClass =
    type === "acknowledge" ? "bg-violet-600 hover:bg-violet-700" :
    type === "close" ? "bg-slate-800 hover:bg-slate-700" :
    "bg-teal-600 hover:bg-teal-700";

  return (
    <div className="ac-modal-overlay" onClick={onClose}>
      <div className="ac-modal" onClick={(event) => event.stopPropagation()} role="dialog" aria-modal="true">
        <div className="flex items-center justify-between">
          <h3 className="ac-modal-title">{title}</h3>
          <button type="button" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>
        <p className="ac-modal-sub">
          <span className="font-semibold text-slate-900">{alert.title}</span> · {alert.severity} · {alert.category}
        </p>
        <div>
          <label className="ac-modal-label">{label}</label>
          <textarea
            className="ac-modal-textarea"
            value={note}
            onChange={(event) => setNote(event.target.value)}
            placeholder={type === "task" ? `Follow-up for ${alert.title}` : "Add context for the team"}
          />
        </div>
        <div className="mt-4 flex justify-end gap-2">
          <button type="button" className="fh-btn-ghost h-10" onClick={onClose}>Cancel</button>
          <button
            type="button"
            className={`rounded-xl px-4 py-2 text-sm font-semibold text-white transition ${buttonClass}`}
            onClick={() => {
              const payload =
                type === "acknowledge" ? { note } :
                type === "close" ? { resolution: note } :
                { title: note || `Follow-up: ${alert.title}` };
              onConfirm(payload);
            }}
          >
            {type === "acknowledge" ? "Acknowledge" : type === "close" ? "Close alert" : "Create task"}
          </button>
        </div>
      </div>
    </div>
  );
}

function AlertCard({
  alert,
  active,
  onSelect,
  canAcknowledge,
  canClose,
  onAction,
}: {
  alert: Alert;
  active: boolean;
  onSelect: () => void;
  canAcknowledge: boolean;
  canClose: boolean;
  onAction: (type: ActionType, alert: Alert) => void;
}) {
  const sevKey = alert.severity.toLowerCase();
  const toneClass =
    sevKey === "critical" ? "ac-alert-card-critical" :
    sevKey === "high" ? "ac-alert-card-high" :
    sevKey === "warning" ? "ac-alert-card-warning" : "ac-alert-card-info";

  return (
    <article className={`ac-alert-card ${toneClass} ${active ? "ac-alert-card-active" : ""}`}>
      <button type="button" onClick={onSelect} className="w-full text-left">
        <div className="flex items-start justify-between gap-3">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${statusClass(alert.severity)}`}>{alert.severity}</span>
              <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${statusClass(alert.status)}`}>{alert.status}</span>
            </div>
            <h3 className="ac-alert-title">{alert.title}</h3>
          </div>
          <span className="text-xs font-semibold text-slate-400">{alert.age ?? "Live"}</span>
        </div>
        <p className="ac-alert-entity">{alert.entity ?? alert.entityType ?? "Unmapped entity"} · {alert.category}</p>
        <p className="ac-alert-action">{alert.recommendedAction || alert.body || "No recommended action recorded."}</p>
      </button>
      <div className="ac-alert-actions">
        {canAcknowledge && /open/i.test(alert.status) && (
          <button type="button" className="fh-btn-ghost h-9 border-violet-200 bg-violet-50 text-violet-700 hover:bg-violet-100" onClick={() => onAction("acknowledge", alert)}>
            Acknowledge
          </button>
        )}
        {canAcknowledge && (
          <button type="button" className="fh-btn-ghost h-9" onClick={() => onAction("task", alert)}>
            Create task
          </button>
        )}
        {canClose && !/closed/i.test(alert.status) && (
          <button type="button" className="fh-btn-ghost h-9" onClick={() => onAction("close", alert)}>
            Close
          </button>
        )}
      </div>
    </article>
  );
}

function DetailPanel({
  alert,
  liveDetail,
  tasks,
  auditTrail,
  loading,
  onNavigate,
  canAcknowledge,
  canClose,
  onAction,
}: {
  alert: Alert | null;
  liveDetail: Alert | null;
  tasks: AlertTask[];
  auditTrail: AlertAuditEntry[];
  loading: boolean;
  onNavigate: (route: string) => void;
  canAcknowledge: boolean;
  canClose: boolean;
  onAction: (type: ActionType, alert: Alert) => void;
}) {
  const record = liveDetail ?? alert;

  if (!record) {
    return (
      <div className="ac-detail">
        <EmptyState title="No alert selected" subtitle="Choose an alert from the live queue to inspect it in context." />
      </div>
    );
  }

  const actionRoute = record.entityRoute || routeForCategory(record.category);
  const rationale =
    record.severity === "Critical"
      ? "Immediate action is warranted because this signal can turn into a safety, compliance or service event if it sits in the queue."
      : record.severity === "High"
        ? "High severity means the issue is not catastrophic yet, but delay increases the chance of cascading dispatch or customer impact."
        : record.severity === "Warning"
          ? "This is an early-warning signal. The best teams reduce critical volume by clearing these before shift handoff."
          : "Informational signals should still stay linked to operational context so they can be audited later.";

  return (
    <aside className="ac-detail">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="ac-detail-eyebrow">Selected alert</p>
          <h2 className="ac-detail-title">{record.title}</h2>
          <p className="ac-detail-sub">{record.alertId} · {record.category}</p>
        </div>
        {loading ? <RefreshCw className="h-4 w-4 animate-spin text-slate-400" /> : null}
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${statusClass(record.severity)}`}>{record.severity}</span>
        <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${statusClass(record.status)}`}>{record.status}</span>
        {record.entity ? <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold text-slate-600">{record.entity}</span> : null}
      </div>

      <div className="mt-4 grid grid-cols-2 gap-3">
        <MetaCard label="Entity type" value={record.entityType || "Not tagged"} />
        <MetaCard label="Age" value={record.age || "Live"} />
        <MetaCard label="Acknowledged by" value={record.acknowledgedBy || "Unowned"} />
        <MetaCard label="Created" value={record.createdAt ? new Date(record.createdAt).toLocaleString() : "Unknown"} />
      </div>

      <div className="ac-info-block ac-info-block-teal">
        <p className="ac-info-block-label ac-info-block-label-teal">Recommended action</p>
        <p className="ac-info-block-body">{record.recommendedAction || record.body || "No action guidance recorded on this alert."}</p>
      </div>

      <div className="ac-info-block ac-info-block-violet">
        <p className="ac-info-block-label ac-info-block-label-violet">Priority rationale</p>
        <p className="ac-info-block-body">{rationale}</p>
      </div>

      <div className="ac-info-block" style={{ background: 'rgba(248,250,252,.6)' }}>
        <div className="flex items-center justify-between gap-3">
          <p className="ac-info-block-label ac-info-block-label-slate">Follow-up tasks</p>
          <span className="text-xs font-medium text-slate-400">{tasks.length} linked</span>
        </div>
        <div className="mt-3 space-y-3">
          {tasks.length ? tasks.map((task) => (
            <div key={String(task.id)} className="ac-task-entry">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="ac-task-title">{task.title}</p>
                  <p className="ac-task-meta">{task.owner || "Unassigned"} · {task.priority || "Priority not set"}</p>
                </div>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${statusClass(task.status || "Open")}`}>{task.status || "Open"}</span>
              </div>
              {task.description ? <p className="ac-task-desc">{task.description}</p> : null}
            </div>
          )) : (
            <p className="text-sm text-slate-500">No follow-up task has been created from this alert yet.</p>
          )}
        </div>
      </div>

      <div className="ac-info-block" style={{ background: 'rgba(248,250,252,.6)' }}>
        <div className="flex items-center justify-between gap-3">
          <p className="ac-info-block-label ac-info-block-label-slate">Audit trail</p>
          <span className="text-xs font-medium text-slate-400">{auditTrail.length} events</span>
        </div>
        <div className="mt-3 space-y-3">
          {auditTrail.length ? auditTrail.map((entry) => (
            <div key={String(entry.id)} className="ac-task-entry">
              <p className="ac-task-title">{entry.actionName || "Alert event"}</p>
              <p className="ac-task-meta">{entry.actorName || "system"} · {entry.createdAt ? new Date(entry.createdAt).toLocaleString() : "Unknown time"}</p>
            </div>
          )) : (
            <p className="text-sm text-slate-500">No audit entries are available for this alert yet.</p>
          )}
        </div>
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <button type="button" className="fh-btn-ghost h-9" onClick={() => onNavigate(actionRoute)}>Open related module</button>
        {canAcknowledge && /open/i.test(record.status) && (
          <button type="button" className="fh-btn-ghost h-9 border-violet-200 bg-violet-50 text-violet-700 hover:bg-violet-100" onClick={() => onAction("acknowledge", record)}>
            Acknowledge
          </button>
        )}
        {canClose && !/closed/i.test(record.status) && (
          <button type="button" className="fh-btn-ghost h-9" onClick={() => onAction("close", record)}>Close</button>
        )}
      </div>
    </aside>
  );
}

function MetaCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="ac-meta-card">
      <p className="ac-meta-label">{label}</p>
      <p className="ac-meta-value">{value}</p>
    </div>
  );
}

export function AlertsCenterPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const canAcknowledge = hasPermission("alerts:acknowledge");
  const canClose = hasPermission("alerts:close");

  const [categoryFilter, setCategoryFilter] = useState<(typeof CATEGORIES)[number]>("All");
  const [statusFilter, setStatusFilter] = useState<(typeof STATUS_FILTERS)[number]>("All");
  const [severityFilter, setSeverityFilter] = useState<(typeof SEVERITY_FILTERS)[number]>("All");
  const [search, setSearch] = useState("");
  const [selectedAlert, setSelectedAlert] = useState<Alert | null>(null);
  const [actionType, setActionType] = useState<ActionType>(null);
  const [actionAlert, setActionAlert] = useState<Alert | null>(null);
  const [toastMsg, setToastMsg] = useState<string | null>(null);

  const alertsQuery = useQuery({
    queryKey: ["alerts"],
    queryFn: () => alertsApi.list(),
    refetchInterval: 15_000,
  });

  const summaryQuery = useQuery({
    queryKey: ["alerts", "summary"],
    queryFn: () => alertsApi.summary(),
    refetchInterval: 15_000,
  });

  const detailQuery = useQuery({
    queryKey: ["alerts", "detail", selectedAlert?.id],
    queryFn: () => alertsApi.detail(String(selectedAlert?.id)),
    enabled: selectedAlert != null,
  });

  const acknowledgeMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => alertsApi.acknowledge(id, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["alerts"] });
      void queryClient.invalidateQueries({ queryKey: ["alerts", "summary"] });
      if (selectedAlert) void queryClient.invalidateQueries({ queryKey: ["alerts", "detail", selectedAlert.id] });
      showToast("Alert acknowledged");
    },
  });

  const closeMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => alertsApi.close(id, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["alerts"] });
      void queryClient.invalidateQueries({ queryKey: ["alerts", "summary"] });
      if (selectedAlert) void queryClient.invalidateQueries({ queryKey: ["alerts", "detail", selectedAlert.id] });
      showToast("Alert closed");
    },
  });

  const taskMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => alertsApi.createTask(id, payload),
    onSuccess: (res) => {
      void queryClient.invalidateQueries({ queryKey: ["alerts"] });
      if (selectedAlert) void queryClient.invalidateQueries({ queryKey: ["alerts", "detail", selectedAlert.id] });
      showToast(`Task created: ${String((res as AnyRecord).taskId ?? "")}`);
    },
  });

  function showToast(msg: string) {
    setToastMsg(msg);
    window.setTimeout(() => setToastMsg(null), 3200);
  }

  const alerts = useMemo(
    () => (Array.isArray(alertsQuery.data) ? (alertsQuery.data as AnyRecord[]).map(normalizeAlert) : []),
    [alertsQuery.data],
  );

  const summary = useMemo<AlertsSummary>(() => {
    const live = summaryQuery.data as AnyRecord | undefined;
    if (live) {
      return {
        total: Number(live.total ?? alerts.length),
        critical: Number(live.critical ?? alerts.filter((alert) => alert.severity === "Critical").length),
        high: Number(live.high ?? alerts.filter((alert) => alert.severity === "High").length),
        open: Number(live.open ?? alerts.filter((alert) => /open/i.test(alert.status)).length),
        acknowledged: Number(live.acknowledged ?? alerts.filter((alert) => /ack/i.test(alert.status)).length),
        closed: Number(live.closed ?? alerts.filter((alert) => /closed/i.test(alert.status)).length),
      };
    }
    return {
      total: alerts.length,
      critical: alerts.filter((alert) => alert.severity === "Critical").length,
      high: alerts.filter((alert) => alert.severity === "High").length,
      open: alerts.filter((alert) => /open/i.test(alert.status)).length,
      acknowledged: alerts.filter((alert) => /ack/i.test(alert.status)).length,
      closed: alerts.filter((alert) => /closed/i.test(alert.status)).length,
    };
  }, [alerts, summaryQuery.data]);

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    return alerts
      .filter((alert) => categoryFilter === "All" || alert.category === categoryFilter)
      .filter((alert) => statusFilter === "All" || alert.status === statusFilter)
      .filter((alert) => severityFilter === "All" || alert.severity === severityFilter)
      .filter((alert) => {
        if (!query) return true;
        return [
          alert.title,
          alert.alertId,
          alert.entity,
          alert.entityType,
          alert.category,
          alert.recommendedAction,
        ].some((value) => String(value ?? "").toLowerCase().includes(query));
      })
      .sort((a, b) => (SEVERITY_ORDER[a.severity] ?? 9) - (SEVERITY_ORDER[b.severity] ?? 9) || ageHours(b.createdAt) - ageHours(a.createdAt));
  }, [alerts, categoryFilter, statusFilter, severityFilter, search]);

  const openAlerts = filtered.filter((alert) => !/closed/i.test(alert.status));
  const agingOpen = alerts.filter((alert) => /open|ack/i.test(alert.status) && ageHours(alert.createdAt) >= 24).length;
  const unownedOpen = alerts.filter((alert) => /open/i.test(alert.status) && !alert.acknowledgedBy).length;
  const categoryBuckets = CATEGORIES.filter((category) => category !== "All").map((category) => ({
    category,
    count: alerts.filter((alert) => alert.category === category && !/closed/i.test(alert.status)).length,
    route: routeForCategory(category),
  })).filter((row) => row.count > 0).sort((a, b) => b.count - a.count);
  const detailRecord = normalizeAlertDetail(detailQuery.data as AnyRecord | undefined);
  const liveDetail = detailRecord.alert;

  function handleAction(type: ActionType, alert: Alert) {
    setActionType(type);
    setActionAlert(alert);
  }

  function handleActionConfirm(payload: AnyRecord) {
    if (!actionType || !actionAlert) return;
    const id = actionAlert.id;
    if (actionType === "acknowledge") acknowledgeMutation.mutate({ id, payload });
    else if (actionType === "close") closeMutation.mutate({ id, payload });
    else taskMutation.mutate({ id, payload });
    setActionType(null);
    setActionAlert(null);
  }

  if (alertsQuery.isLoading) return <LoadingState />;
  if (alertsQuery.isError) return <ErrorState message={alertsQuery.error instanceof Error ? alertsQuery.error.message : "Unable to load alerts."} />;

  return (
    <div className="space-y-6 pb-10">
      {toastMsg ? (
        <div className="ac-toast">{toastMsg}</div>
      ) : null}

      {/* ── Hero banner ──────────────────────────────────────────── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />

        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <ShieldAlert className="h-3 w-3" /> Exception Command
                </span>
                <span className="relative flex h-2.5 w-2.5">
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60" />
                  <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-emerald-500" />
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Live · 15s refresh</span>
              </div>

              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Alerts Center
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                {summary.total} alerts tracked · {summary.open} open · {summary.critical} critical — action-first triage surface
              </p>
            </div>

            <div className="flex items-center gap-2">
              <button type="button" onClick={() => exportCsv("alerts", filtered)} className="fh-btn-ghost">
                Export live queue
              </button>
              <button
                type="button"
                onClick={() => {
                  void queryClient.invalidateQueries({ queryKey: ["alerts"] });
                  void queryClient.invalidateQueries({ queryKey: ["alerts", "summary"] });
                }}
                className="fh-btn-primary"
              >
                Refresh alerts <RefreshCw className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* ── KPI strip ────────────────────────────────────────────── */}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <div className="fo-kpi-card fo-kpi-active">
          <div className="flex items-start gap-3">
            <div className="fo-kpi-icon fo-kpi-icon-inactive"><BellRing className="h-5 w-5 text-teal-600" /></div>
            <div className="min-w-0 flex-1">
              <div className="fo-kpi-count"><span className="fo-kpi-dot bg-teal-500" /><span>{summary.open}</span></div>
              <p className="fo-kpi-label text-slate-500">Open queue</p>
              <p className="mt-0.5 text-[11px] text-slate-400">{summary.critical} critical</p>
            </div>
          </div>
        </div>
        <div className="fo-kpi-card">
          <div className="flex items-start gap-3">
            <div className="fo-kpi-icon fo-kpi-icon-inactive"><Clock3 className="h-5 w-5 text-teal-600" /></div>
            <div className="min-w-0 flex-1">
              <div className="fo-kpi-count"><span className="fo-kpi-dot bg-amber-400" /><span>{agingOpen}</span></div>
              <p className="fo-kpi-label text-slate-500">Aging open alerts</p>
              <p className="mt-0.5 text-[11px] text-slate-400">24h+ still active</p>
            </div>
          </div>
        </div>
        <div className="fo-kpi-card">
          <div className="flex items-start gap-3">
            <div className="fo-kpi-icon fo-kpi-icon-inactive"><AlertTriangle className="h-5 w-5 text-teal-600" /></div>
            <div className="min-w-0 flex-1">
              <div className="fo-kpi-count"><span className="fo-kpi-dot bg-rose-500" /><span>{unownedOpen}</span></div>
              <p className="fo-kpi-label text-slate-500">Unowned alerts</p>
              <p className="mt-0.5 text-[11px] text-slate-400">Not yet acknowledged</p>
            </div>
          </div>
        </div>
        <div className="fo-kpi-card">
          <div className="flex items-start gap-3">
            <div className="fo-kpi-icon fo-kpi-icon-inactive"><BadgeCheck className="h-5 w-5 text-teal-600" /></div>
            <div className="min-w-0 flex-1">
              <div className="fo-kpi-count"><span className="fo-kpi-dot bg-slate-400" /><span>{summary.closed}/{summary.total || 1}</span></div>
              <p className="fo-kpi-label text-slate-500">Closed today posture</p>
              <p className="mt-0.5 text-[11px] text-slate-400">Closed versus total visible</p>
            </div>
          </div>
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[1.25fr_0.95fr]">
        <section className="ac-panel">
          <div className="ac-panel-header">
            <div>
              <h2 className="ac-panel-title">Response lanes</h2>
              <p className="ac-panel-subtitle">Where alert pressure is concentrating right now across operations domains.</p>
            </div>
            <span className="ac-panel-badge">
              <Zap className="h-3 w-3" /> Live workload map
            </span>
          </div>
          <div className="ac-lane-grid">
            {categoryBuckets.slice(0, 6).map((bucket) => (
              <button
                key={bucket.category}
                type="button"
                onClick={() => navigate(bucket.route)}
                className="ac-lane-card"
              >
                <div className="flex items-center justify-between">
                  <p className="ac-lane-name">{bucket.category}</p>
                  <ArrowRight className="h-4 w-4 text-slate-300" />
                </div>
                <p className="ac-lane-count">{bucket.count}</p>
                <p className="ac-lane-desc">Open alert{bucket.count === 1 ? "" : "s"} linked to this lane.</p>
              </button>
            ))}
            {!categoryBuckets.length && (
              <div className="ac-lane-empty md:col-span-2 xl:col-span-3">
                No active alert pressure is being returned by the live backend at the moment.
              </div>
            )}
          </div>
        </section>

        <section className="ac-panel">
          <div>
            <h2 className="ac-panel-title">Triage guidance</h2>
            <p className="ac-panel-subtitle">What a strong ops team should do next with the current queue shape.</p>
          </div>
          <div className="ac-guidance-list">
            <GuidanceCard
              icon={<ShieldAlert className="h-4 w-4" />}
              title="Clear criticals first"
              body={summary.critical > 0 ? `${summary.critical} critical alert${summary.critical === 1 ? "" : "s"} are still open. These should stay at the top of the shift board.` : "No critical alerts are currently open."}
            />
            <GuidanceCard
              icon={<Clock3 className="h-4 w-4" />}
              title="Stop queue aging"
              body={agingOpen > 0 ? `${agingOpen} open or acknowledged alert${agingOpen === 1 ? "" : "s"} have been sitting for 24h or more.` : "No aging alert backlog is visible right now."}
            />
            <GuidanceCard
              icon={<Wrench className="h-4 w-4" />}
              title="Hand off by domain"
              body={categoryBuckets[0] ? `${categoryBuckets[0].category} currently has the heaviest live alert concentration.` : "No single lane is overloaded from the returned dataset."}
            />
            <GuidanceCard
              icon={<Sparkles className="h-4 w-4" />}
              title="Make ownership visible"
              body={unownedOpen > 0 ? `${unownedOpen} open alert${unownedOpen === 1 ? "" : "s"} still have no acknowledged owner.` : "Every open alert appears to have an owner or the queue is empty."}
            />
          </div>
        </section>
      </div>

      <section className="ac-filter-panel">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div className="ac-search-wrap">
            <Search className="ac-search-icon" />
            <input
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search alerts, entities, categories\u2026"
              className="ac-search-input"
            />
          </div>
          <div className="flex flex-wrap gap-2">
            {CATEGORIES.map((category) => (
              <button
                key={category}
                type="button"
                onClick={() => setCategoryFilter(category)}
                className={`ac-chip ${categoryFilter === category ? "ac-chip-active" : ""}`}
              >
                {category}
              </button>
            ))}
          </div>
        </div>
      
        <div className="mt-4 flex flex-wrap gap-2">
          {SEVERITY_FILTERS.map((severity) => (
            <button
              key={severity}
              type="button"
              onClick={() => setSeverityFilter(severity)}
              className={`ac-chip text-xs uppercase tracking-[0.16em] ${severityFilter === severity ? "ac-chip-sev-active" : ""}`}
            >
              {severity}
            </button>
          ))}
          {STATUS_FILTERS.map((status) => (
            <button
              key={status}
              type="button"
              onClick={() => setStatusFilter(status)}
              className={`ac-chip text-xs uppercase tracking-[0.16em] ${statusFilter === status ? "ac-chip-status-active" : ""}`}
            >
              {status}
            </button>
          ))}
        </div>
      </section>

      <div className="grid gap-4 xl:grid-cols-[1.35fr_0.95fr]">
        <section className="space-y-3">
          {openAlerts.length ? openAlerts.map((alert) => (
            <AlertCard
              key={String(alert.id)}
              alert={alert}
              active={selectedAlert?.id === alert.id}
              onSelect={() => setSelectedAlert(alert)}
              canAcknowledge={canAcknowledge}
              canClose={canClose}
              onAction={handleAction}
            />
          )) : (
            <EmptyState title="No alerts match your filters" subtitle="Adjust the search or filter chips to broaden the live queue." />
          )}
        </section>

        <DetailPanel
          alert={selectedAlert}
          liveDetail={liveDetail}
          tasks={detailRecord.tasks}
          auditTrail={detailRecord.auditTrail}
          loading={detailQuery.isLoading}
          onNavigate={navigate}
          canAcknowledge={canAcknowledge}
          canClose={canClose}
          onAction={handleAction}
        />
      </div>

      <div className="ac-footer-strip">
        <Clock3 className="h-3.5 w-3.5" />
        Refreshed every 15 seconds from the live backend. No demo fallback is used here.
      </div>

      <ActionModal
        type={actionType}
        alert={actionAlert}
        onClose={() => {
          setActionType(null);
          setActionAlert(null);
        }}
        onConfirm={handleActionConfirm}
      />
    </div>
  );
}

function GuidanceCard({ icon, title, body }: { icon: React.ReactNode; title: string; body: string }) {
  return (
    <div className="ac-guidance-card">
      <div className="flex items-center gap-3">
        <div className="ac-guidance-icon-wrap">{icon}</div>
        <p className="ac-guidance-title">{title}</p>
      </div>
      <p className="ac-guidance-body">{body}</p>
    </div>
  );
}
