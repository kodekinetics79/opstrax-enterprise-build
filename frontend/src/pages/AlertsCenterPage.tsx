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
  ShieldAlert,
  Sparkles,
  Wrench,
} from "lucide-react";
import { alertsApi } from "@/services/alertsApi";
import { useHasPermission } from "@/hooks/usePermission";
import { EmptyState, ErrorState, exportCsv, KpiCard, LoadingState, StatusBadge } from "@/components/ui";
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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm" onClick={onClose}>
      <div className="panel mx-4 w-full max-w-md" onClick={(event) => event.stopPropagation()} role="dialog" aria-modal="true">
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold text-slate-900">{title}</h3>
          <button type="button" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>
        <p className="mt-3 text-sm text-slate-600">
          <span className="font-medium text-slate-900">{alert.title}</span> · {alert.severity} · {alert.category}
        </p>
        <div className="mt-4">
          <label className="mb-2 block text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">{label}</label>
          <textarea
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
      className={`rounded-[22px] border p-4 shadow-[0_10px_28px_rgba(95,66,35,.08)] transition hover:-translate-y-0.5 hover:shadow-[0_16px_34px_rgba(95,66,35,.12)] ${active ? "border-teal-300 bg-[linear-gradient(180deg,rgba(245,252,251,.98),rgba(226,248,244,.92))]" : `bg-[linear-gradient(180deg,rgba(255,255,255,.96),rgba(250,243,233,.96))] ${severityTone(alert.severity)}`}`}
    >
      <button type="button" onClick={onSelect} className="w-full text-left">
        <div className="flex items-start justify-between gap-3">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={alert.severity} />
              <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${statusClass(alert.status)}`}>{alert.status}</span>
            </div>
            <h3 className="mt-3 text-sm font-semibold text-slate-900">{alert.title}</h3>
          </div>
          <span className="text-xs font-semibold text-slate-400">{alert.age ?? "Live"}</span>
        </div>
        <p className="mt-2 text-sm text-slate-600">{alert.entity ?? alert.entityType ?? "Unmapped entity"} · {alert.category}</p>
        <p className="mt-2 line-clamp-2 text-sm leading-6 text-slate-500">{alert.recommendedAction || alert.body || "No recommended action recorded."}</p>
      </button>
      <div className="mt-4 flex flex-wrap gap-2 border-t border-black/5 pt-3">
        {canAcknowledge && /open/i.test(alert.status) && (
          <button type="button" className="btn-ghost h-9 border-violet-200 bg-violet-50 text-violet-700 hover:bg-violet-100" onClick={() => onAction("acknowledge", alert)}>
            Acknowledge
          </button>
        )}
        {canAcknowledge && (
          <button type="button" className="btn-ghost h-9 border-teal-200 bg-teal-50 text-teal-700 hover:bg-teal-100" onClick={() => onAction("task", alert)}>
            Create task
          </button>
        )}
        {canClose && !/closed/i.test(alert.status) && (
          <button type="button" className="btn-ghost h-9" onClick={() => onAction("close", alert)}>
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
        <MetaCard label="Age" value={record.age || "Live"} />
        <MetaCard label="Acknowledged by" value={record.acknowledgedBy || "Unowned"} />
        <MetaCard label="Created" value={record.createdAt ? new Date(record.createdAt).toLocaleString() : "Unknown"} />
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-[linear-gradient(180deg,rgba(255,255,255,.96),rgba(247,239,227,.92))] p-4 shadow-[0_10px_22px_rgba(95,66,35,.06)]">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-teal-600">Recommended action</p>
        <p className="mt-2 text-sm text-slate-700">{record.recommendedAction || record.body || "No action guidance recorded on this alert."}</p>
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-[0_8px_18px_rgba(95,66,35,.04)]">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-violet-600">Priority rationale</p>
        <p className="mt-2 text-sm text-slate-600">{rationale}</p>
      </div>

      <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-[0_8px_18px_rgba(95,66,35,.04)]">
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

      <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-[0_8px_18px_rgba(95,66,35,.04)]">
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
    <div className="rounded-xl border border-slate-200 bg-[linear-gradient(180deg,rgba(255,255,255,.96),rgba(248,241,231,.92))] px-3 py-2 shadow-[0_6px_14px_rgba(95,66,35,.04)]">
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
  if (alertsQuery.isError) {
    return (
      <ErrorState
        message={alertsQuery.error instanceof Error ? alertsQuery.error.message : "Unable to load alerts."}
        onRetry={() => void alertsQuery.refetch()}
      />
    );
  }

  return (
    <div className="alerts-command-room alerts-center-workbench space-y-5 pb-10">
      {toastMsg ? (
        <div className="fixed right-4 top-4 z-50 rounded-2xl border border-emerald-500/20 bg-emerald-600 px-4 py-3 text-sm font-medium text-white shadow-2xl shadow-emerald-900/20">
          {toastMsg}
        </div>
      ) : null}

      <header className="panel relative overflow-hidden border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,252,247,.98),rgba(247,237,225,.96))] p-5 shadow-[0_24px_80px_-48px_rgba(95,66,35,0.46)]">
        <div className="pointer-events-none absolute inset-x-0 top-0 h-28 bg-[radial-gradient(circle_at_12%_8%,rgba(214,126,63,.20),transparent_24%),radial-gradient(circle_at_78%_0%,rgba(20,184,166,.14),transparent_20%),linear-gradient(90deg,rgba(255,255,255,.68),rgba(255,255,255,0))]" />
        <div className="relative grid gap-5 lg:grid-cols-[minmax(0,1.45fr)_0.95fr] lg:items-end">
          <div className="min-w-0">
            <div className="inline-flex items-center gap-2 rounded-full border border-amber-200 bg-white/85 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-amber-700 shadow-sm">
              <BellRing className="h-3.5 w-3.5" /> Live alerts command room
            </div>
            <h1 className="mt-3 text-[2rem] font-black tracking-tight text-slate-900 sm:text-[2.25rem]">
              Alerts Center
            </h1>
            <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">
              A live, backend-backed triage surface for open alerts, ownership, audit trail, and resolution actions. Tight layout, no demo queue, no fake feed, and no hidden fallback layer.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              <button type="button" onClick={() => exportCsv("alerts", filtered)} className="btn-ghost h-10 border-slate-200 bg-white/90 text-slate-700 hover:bg-white">
                Export live queue
              </button>
              <button
                type="button"
                onClick={() => {
                  void queryClient.invalidateQueries({ queryKey: ["alerts"] });
                  void queryClient.invalidateQueries({ queryKey: ["alerts", "summary"] });
                }}
                className="btn-primary h-10 bg-gradient-to-r from-amber-700 via-rose-600 to-teal-600 shadow-md shadow-amber-200/70 hover:from-amber-600 hover:via-rose-500 hover:to-teal-500"
              >
                Refresh alerts <RefreshCw className="h-4 w-4" />
              </button>
            </div>
          </div>

          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-1">
            <MiniStat label="Open queue" value={summary.open} sublabel={`${summary.critical} critical / ${summary.high} high`} />
            <MiniStat label="Aging open" value={agingOpen} sublabel="24h+ still active" />
            <MiniStat label="Unowned" value={unownedOpen} sublabel="Needs an acknowledged owner" />
            <MiniStat label="Closed today" value={`${summary.closed}/${summary.total || 1}`} sublabel="Visible set" />
          </div>
        </div>
        <div className="relative mt-4 flex flex-wrap gap-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
          <span className="rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-emerald-700">Connected to live backend</span>
          <span className="rounded-full border border-slate-200 bg-white/85 px-3 py-1">Auto refresh 15s</span>
          <span className="rounded-full border border-slate-200 bg-white/85 px-3 py-1">No demo fallback</span>
        </div>
      </header>

      <section className="panel p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold text-slate-900">Workspace controls</h2>
            <p className="text-sm text-slate-500">Compact filters for a dense queue view.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <div className="relative w-full min-w-[16rem] sm:w-[20rem]">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search alerts, entities, categories…"
                className="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-10 pr-3 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-teal-400 focus:ring-2 focus:ring-teal-100"
              />
            </div>
            <button type="button" className={`rounded-full border px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.16em] transition ${categoryFilter === "All" ? "border-amber-300 bg-amber-50 text-amber-800" : "border-slate-200 bg-slate-50 text-slate-600"}`} onClick={() => setCategoryFilter("All")}>
              All lanes
            </button>
          </div>
        </div>

        <div className="mt-3 flex flex-wrap gap-2">
          {CATEGORIES.filter((category) => category !== "All").map((category) => (
            <button
              key={category}
              type="button"
              onClick={() => setCategoryFilter(category)}
              className={`rounded-full border px-3 py-1.5 text-sm font-medium transition ${
                categoryFilter === category ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-white"
              }`}
            >
              {category}
            </button>
          ))}
        </div>

        <div className="mt-3 flex flex-wrap gap-2">
          {SEVERITY_FILTERS.map((severity) => (
            <button
              key={severity}
              type="button"
              onClick={() => setSeverityFilter(severity)}
              className={`rounded-full border px-3 py-1.5 text-[11px] font-semibold uppercase tracking-[0.16em] transition ${
                severityFilter === severity ? "border-slate-900 bg-slate-900 text-white" : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-white"
              }`}
            >
              {severity}
            </button>
          ))}
          {STATUS_FILTERS.map((status) => (
            <button
              key={status}
              type="button"
              onClick={() => setStatusFilter(status)}
              className={`rounded-full border px-3 py-1.5 text-[11px] font-semibold uppercase tracking-[0.16em] transition ${
                statusFilter === status ? "border-violet-300 bg-violet-50 text-violet-700" : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-white"
              }`}
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
        Refreshed every 15 seconds from the live backend. No fallback data layer is used here.
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
    <div className="rounded-2xl border border-slate-200 bg-[linear-gradient(180deg,rgba(255,255,255,.92),rgba(247,239,227,.86))] p-4 shadow-[0_10px_24px_rgba(95,66,35,.06)]">
      <div className="flex items-center gap-2">
        <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-white text-slate-500 shadow-sm">{icon}</div>
        <p className="text-sm font-semibold text-slate-900">{title}</p>
      </div>
      <p className="mt-3 text-sm text-slate-500">{body}</p>
    </div>
  );
}

function MiniStat({ label, value, sublabel }: { label: string; value: number | string; sublabel: string }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-[linear-gradient(180deg,rgba(255,255,255,.96),rgba(249,242,233,.92))] px-4 py-3 shadow-[inset_0_1px_0_rgba(255,255,255,.8),0_8px_20px_rgba(95,66,35,.06)]">
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">{label}</p>
      <p className="mt-1 text-2xl font-black tracking-tight text-slate-900">{value}</p>
      <p className="mt-1 text-xs font-medium text-slate-500">{sublabel}</p>
    </div>
  );
}
