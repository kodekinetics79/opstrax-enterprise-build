import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import {
  Clock3,
  RefreshCw,
  Search,
  X,
} from "lucide-react";
import { alertsApi } from "@/services/alertsApi";
import { useHasPermission } from "@/hooks/usePermission";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { EmptyState, ErrorState, exportCsv, LoadingState, PageHeader, StatusBadge } from "@/components/ui";
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
  const dialogRef = useDialogFocus<HTMLDivElement>(Boolean(type && alert), onClose);

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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm" onClick={onClose}>
      <div ref={dialogRef} className="panel mx-4 w-full max-w-md" onClick={(event) => event.stopPropagation()} role="dialog" aria-modal="true" aria-labelledby="alert-action-title">
        <div className="flex items-center justify-between">
          <h3 id="alert-action-title" className="text-base font-semibold text-slate-900">{title}</h3>
          <button type="button" className="rounded-lg p-1 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700" onClick={onClose} aria-label="Close alert action"><X className="h-4 w-4" /></button>
        </div>
        <p className="mt-3 text-sm text-slate-600">
          <span className="font-medium text-slate-900">{alert.title}</span> · {alert.severity} · {alert.category}
        </p>
        <div className="mt-4">
          <label htmlFor="alert-action-note" className="mb-2 block text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">{label}</label>
          <textarea
            id="alert-action-note"
            autoFocus
            className="min-h-[110px] w-full rounded-xl border border-slate-200 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100"
            value={note}
            onChange={(event) => setNote(event.target.value)}
            placeholder={type === "task" ? `Follow-up for ${alert.title}` : "Add context for the team"}
          />
        </div>
        <div className="mt-4 flex justify-end gap-2">
          <button type="button" className="btn-ghost h-10" onClick={onClose}>Cancel</button>
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
  return (
    <article
      className={`rounded-xl border p-3 shadow-sm transition hover:border-sky-300 hover:shadow-md ${active ? "border-sky-300 bg-sky-50/70" : severityTone(alert.severity)}`}
    >
      <button type="button" onClick={onSelect} className="w-full text-left">
        <div className="flex items-start justify-between gap-3">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={alert.severity} />
              <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${statusClass(alert.status)}`}>{alert.status}</span>
            </div>
            <h3 className="mt-2 text-sm font-semibold text-slate-900">{alert.title}</h3>
          </div>
          <span className="text-xs font-semibold text-slate-400">{alert.age ?? "Age unavailable"}</span>
        </div>
        <p className="mt-1.5 text-sm text-slate-600">{alert.entity ?? alert.entityType ?? "Unmapped entity"} · {alert.category}</p>
        <p className="mt-1.5 line-clamp-2 text-sm leading-5 text-slate-500">{alert.recommendedAction || alert.body || "No recommended action recorded."}</p>
      </button>
      <div className="mt-3 flex flex-wrap gap-2 border-t border-black/5 pt-2">
        {canAcknowledge && /open/i.test(alert.status) && (
          <button type="button" className="btn-ghost btn-compact border-sky-200 bg-sky-50 text-sky-700 hover:bg-sky-100" onClick={() => onAction("acknowledge", alert)}>
            Acknowledge
          </button>
        )}
        {canAcknowledge && (
          <button type="button" className="btn-ghost btn-compact border-teal-200 bg-teal-50 text-teal-700 hover:bg-teal-100" onClick={() => onAction("task", alert)}>
            Create task
          </button>
        )}
        {canClose && !/closed/i.test(alert.status) && (
          <button type="button" className="btn-ghost btn-compact" onClick={() => onAction("close", alert)}>
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
      <div className="panel p-5">
        <EmptyState title="No alert selected" subtitle="Choose an alert from the current queue to inspect its recorded context." />
      </div>
    );
  }

  const actionRoute = record.entityRoute || routeForCategory(record.category);
  const evidenceNote = `Severity is recorded as ${record.severity}. Review the source event and current asset context before deciding the operational response.`;

  return (
    <aside className="panel p-4 lg:p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Selected alert</p>
          <h2 className="mt-1 text-lg font-semibold text-slate-900">{record.title}</h2>
          <p className="mt-1 text-sm text-slate-500">{record.alertId} · {record.category}</p>
        </div>
        {loading ? <RefreshCw className="h-4 w-4 animate-spin text-slate-400" /> : null}
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <StatusBadge status={record.severity} />
        <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${statusClass(record.status)}`}>{record.status}</span>
        {record.entity ? <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold text-slate-600">{record.entity}</span> : null}
      </div>

      <div className="mt-4 grid grid-cols-2 gap-3">
        <MetaCard label="Entity type" value={record.entityType || "Not tagged"} />
        <MetaCard label="Age" value={record.age || "Unavailable"} />
        <MetaCard label="Acknowledged by" value={record.acknowledgedBy || "Unowned"} />
        <MetaCard label="Created" value={record.createdAt ? new Date(record.createdAt).toLocaleString() : "Unknown"} />
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-[linear-gradient(180deg,rgba(255,255,255,.98),rgba(245,249,253,.94))] p-4 shadow-[0_10px_22px_rgba(15,23,42,.05)]">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-teal-600">Recommended action</p>
        <p className="mt-2 text-sm text-slate-700">{record.recommendedAction || record.body || "No action guidance recorded on this alert."}</p>
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-[0_8px_18px_rgba(15,23,42,.04)]">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-violet-600">Evidence note</p>
        <p className="mt-2 text-sm text-slate-600">{evidenceNote}</p>
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-[0_8px_18px_rgba(15,23,42,.04)]">
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Follow-up tasks</p>
          <span className="text-xs font-medium text-slate-400">{tasks.length} linked</span>
        </div>
        <div className="mt-3 space-y-3">
          {tasks.length ? tasks.map((task) => (
            <div key={String(task.id)} className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-3">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-semibold text-slate-900">{task.title}</p>
                  <p className="mt-1 text-xs text-slate-500">{task.owner || "Unassigned"} · {task.priority || "Priority not set"}</p>
                </div>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${statusClass(task.status || "Open")}`}>{task.status || "Open"}</span>
              </div>
              {task.description ? <p className="mt-2 text-sm text-slate-600">{task.description}</p> : null}
            </div>
          )) : (
            <p className="text-sm text-slate-500">No follow-up task has been created from this alert yet.</p>
          )}
        </div>
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-[0_8px_18px_rgba(15,23,42,.04)]">
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Audit trail</p>
          <span className="text-xs font-medium text-slate-400">{auditTrail.length} events</span>
        </div>
        <div className="mt-3 space-y-3">
          {auditTrail.length ? auditTrail.map((entry) => (
            <div key={String(entry.id)} className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-3">
              <p className="text-sm font-semibold text-slate-900">{entry.actionName || "Alert event"}</p>
              <p className="mt-1 text-xs text-slate-500">{entry.actorName || "system"} · {entry.createdAt ? new Date(entry.createdAt).toLocaleString() : "Unknown time"}</p>
            </div>
          )) : (
            <p className="text-sm text-slate-500">No audit entries are available for this alert yet.</p>
          )}
        </div>
      </div>

      <div className="mt-4 flex flex-wrap gap-2 border-t border-black/5 pt-3">
        <button type="button" className="btn-ghost h-9" onClick={() => onNavigate(actionRoute)}>Open related module</button>
        {canAcknowledge && /open/i.test(record.status) && (
          <button type="button" className="btn-ghost h-9 border-violet-200 bg-violet-50 text-violet-700 hover:bg-violet-100" onClick={() => onAction("acknowledge", record)}>
            Acknowledge
          </button>
        )}
        {canClose && !/closed/i.test(record.status) && (
          <button type="button" className="btn-ghost h-9" onClick={() => onAction("close", record)}>Close</button>
        )}
      </div>
    </aside>
  );
}

function MetaCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-[linear-gradient(180deg,rgba(255,255,255,.98),rgba(245,249,253,.94))] px-3 py-2 shadow-[0_6px_14px_rgba(15,23,42,.04)]">
      <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">{label}</p>
      <p className="mt-1 text-sm font-semibold text-slate-900">{value}</p>
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
    const persistedSummary = summaryQuery.data as AnyRecord | undefined;
    if (persistedSummary) {
      return {
        total: Number(persistedSummary.total ?? alerts.length),
        critical: Number(persistedSummary.critical ?? alerts.filter((alert) => alert.severity === "Critical").length),
        high: Number(persistedSummary.high ?? alerts.filter((alert) => alert.severity === "High").length),
        open: Number(persistedSummary.open ?? alerts.filter((alert) => /open/i.test(alert.status)).length),
        acknowledged: Number(persistedSummary.acknowledged ?? alerts.filter((alert) => /ack/i.test(alert.status)).length),
        closed: Number(persistedSummary.closed ?? alerts.filter((alert) => /closed/i.test(alert.status)).length),
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

  const unresolvedCount = alerts.filter((alert) => !/closed/i.test(alert.status)).length;
  const agingUnresolved = alerts.filter((alert) => !/closed/i.test(alert.status) && ageHours(alert.createdAt) >= 24).length;
  const unownedOpen = alerts.filter((alert) => /open/i.test(alert.status) && !alert.acknowledgedBy).length;
  const criticalUnresolved = alerts.filter((alert) => !/closed/i.test(alert.status) && alert.severity === "Critical").length;
  const highUnresolved = alerts.filter((alert) => !/closed/i.test(alert.status) && alert.severity === "High").length;
  const hasActiveFilters = categoryFilter !== "All" || statusFilter !== "All" || severityFilter !== "All" || search.trim().length > 0;
  const detailRecord = normalizeAlertDetail(detailQuery.data as AnyRecord | undefined);
  const liveDetail = detailRecord.alert;

  useEffect(() => {
    if (!filtered.length) return;
    if (!selectedAlert || !filtered.some((alert) => alert.id === selectedAlert.id)) {
      setSelectedAlert(filtered[0]);
    }
  }, [filtered, selectedAlert]);

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
  if (alertsQuery.isError) {
    return (
      <ErrorState
        message={alertsQuery.error instanceof Error ? alertsQuery.error.message : "Unable to load alerts."}
        onRetry={() => void alertsQuery.refetch()}
      />
    );
  }

  return (
    <div className="control-tower page-stack pb-4">
      {toastMsg ? (
        <div className="fixed right-4 top-4 z-50 rounded-2xl border border-emerald-500/20 bg-emerald-600 px-4 py-3 text-sm font-medium text-white shadow-2xl shadow-emerald-900/20">
          {toastMsg}
        </div>
      ) : null}

      <PageHeader
        eyebrow="Operations"
        title="Alerts Center"
        description="Persisted telemetry alerts generated by the available ingest and detection services, limited to your authorized tenant and branch scope."
        actions={
          <>
            <button type="button" onClick={() => exportCsv("alerts", filtered)} className="btn-secondary btn-compact">
              Export current records
            </button>
            <button
              type="button"
              onClick={() => {
                void queryClient.invalidateQueries({ queryKey: ["alerts"] });
                void queryClient.invalidateQueries({ queryKey: ["alerts", "summary"] });
              }}
              className="btn-primary btn-compact"
            >
              <RefreshCw className="h-4 w-4" /> Refresh alerts
            </button>
          </>
        }
      />

      <section className="panel p-3" aria-label="Alert queue summary and filters">
        <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(20rem,.8fr)] xl:items-center">
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label="Alert queue summary">
            <CompactMetric label="Unresolved" value={unresolvedCount} detail={`${criticalUnresolved} critical · ${highUnresolved} high`} tone={criticalUnresolved > 0 ? "danger" : "neutral"} />
            <CompactMetric label="Aging unresolved" value={agingUnresolved} detail="24h+ and not closed" tone={agingUnresolved > 0 ? "warning" : "neutral"} />
            <CompactMetric label="Unowned" value={unownedOpen} detail="Awaiting acknowledgement" tone={unownedOpen > 0 ? "info" : "neutral"} />
            <CompactMetric label="Resolved" value={summary.closed} detail={`${summary.total || alerts.length} total`} tone="success" />
          </dl>

          <div className="relative min-w-0">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search alerts, entities, categories…"
                className="field w-full pl-9"
              />
          </div>
        </div>

        <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-slate-100 pt-3">
          <label className="min-w-0">
            <span className="sr-only">Filter alerts by category</span>
            <select aria-label="Alert categories" className="field min-w-36" value={categoryFilter} onChange={(event) => setCategoryFilter(event.target.value as (typeof CATEGORIES)[number])}>
              {CATEGORIES.map((category) => <option key={category} value={category}>{category === "All" ? "All categories" : category}</option>)}
            </select>
          </label>
          <label className="min-w-0">
            <span className="sr-only">Filter alerts by severity</span>
            <select aria-label="Alert severity" className="field min-w-32" value={severityFilter} onChange={(event) => setSeverityFilter(event.target.value as (typeof SEVERITY_FILTERS)[number])}>
              {SEVERITY_FILTERS.map((severity) => <option key={severity} value={severity}>{severity === "All" ? "All severities" : severity}</option>)}
            </select>
          </label>
          <label className="min-w-0">
            <span className="sr-only">Filter alerts by status</span>
            <select aria-label="Alert status" className="field min-w-36" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as (typeof STATUS_FILTERS)[number])}>
              {STATUS_FILTERS.map((status) => <option key={status} value={status}>{status === "All" ? "All statuses" : status}</option>)}
            </select>
          </label>
          <button
            type="button"
            aria-pressed={statusFilter === "Open"}
            className={`${statusFilter === "Open" ? "btn-primary" : "btn-ghost"} btn-compact`}
            onClick={() => setStatusFilter((current) => current === "Open" ? "All" : "Open")}
          >
            Open only
          </button>
          {hasActiveFilters ? (
            <button type="button" className="btn-ghost btn-compact" onClick={() => { setCategoryFilter("All"); setSeverityFilter("All"); setStatusFilter("All"); setSearch(""); }}>
              Clear filters
            </button>
          ) : null}
          <span className="ml-auto text-xs font-medium text-slate-500" role="status" aria-live="polite">
            <strong className="font-semibold text-slate-700">{filtered.length}</strong> visible · persisted records · auto refresh 15s
          </span>
        </div>
      </section>

      <div className="grid gap-3 xl:grid-cols-[1.35fr_0.95fr]">
        <section className="grid gap-3 md:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3">
          {filtered.length ? filtered.map((alert) => (
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
            <div className="md:col-span-2">
              <EmptyState title="No alerts match your filters" subtitle="Adjust the search or filters to broaden the current record set." />
            </div>
          )}
        </section>

        <div className="xl:sticky xl:top-4 xl:self-start">
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
      </div>

      <div className="flex items-center gap-3 text-xs font-medium text-slate-500">
        <Clock3 className="h-3.5 w-3.5" />
        Refreshed every 15 seconds from persisted telemetry alert records. Missing records remain unavailable.
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

function CompactMetric({
  label,
  value,
  detail,
  tone,
}: {
  label: string;
  value: number | string;
  detail: string;
  tone: "danger" | "warning" | "info" | "success" | "neutral";
}) {
  const toneClass = {
    danger: "border-red-200 bg-red-50/70 text-red-700",
    warning: "border-amber-200 bg-amber-50/70 text-amber-700",
    info: "border-sky-200 bg-sky-50/70 text-sky-700",
    success: "border-emerald-200 bg-emerald-50/70 text-emerald-700",
    neutral: "border-slate-200 bg-slate-50/70 text-slate-700",
  }[tone];

  return (
    <div className={`min-w-0 rounded-xl border px-3 py-2 ${toneClass}`}>
      <dt className="text-[10px] font-bold uppercase tracking-[0.12em] opacity-75">{label}</dt>
      <dd className="mt-0.5 flex min-w-0 items-baseline gap-2">
        <strong className="text-lg font-bold leading-none tabular-nums">{value}</strong>
        <span className="min-w-0 truncate text-[11px] font-medium opacity-75" title={detail}>{detail}</span>
      </dd>
    </div>
  );
}
