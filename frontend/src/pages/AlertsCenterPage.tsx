import { useEffect, useMemo, useState } from "react";
import axios from "axios";
import { WorkspaceGuidance } from "@/components/WorkspaceGuidance";
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
import { EmptyState, ErrorState, exportCsv, LoadingState, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";
import "./alerts-workspace.css";

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
  pending,
  error,
}: {
  type: ActionType;
  alert: Alert | null;
  onClose: () => void;
  onConfirm: (payload: AnyRecord) => void;
  pending: boolean;
  error: string | null;
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
        {error ? <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p> : null}
        <div className="mt-4 flex justify-end gap-2">
          <button type="button" className="btn-ghost h-10" disabled={pending} onClick={onClose}>Cancel</button>
          <button
            type="button"
            disabled={pending}
            className={`rounded-xl px-4 py-2 text-sm font-semibold text-white transition disabled:opacity-50 ${buttonClass}`}
            onClick={() => {
              const payload =
                type === "acknowledge" ? { note } :
                type === "close" ? { resolution: note } :
                { title: note || `Follow-up: ${alert.title}` };
              onConfirm(payload);
            }}
          >
            {pending ? "Saving…" : type === "acknowledge" ? "Acknowledge" : type === "close" ? "Close alert" : "Create task"}
          </button>
        </div>
      </div>
    </div>
  );
}

function AlertInspector({ alert, detail, loading, failed, retry, canAcknowledge, canClose, onAction, onNavigate }: {
  alert: Alert; detail: AlertDetailRecord; loading: boolean; failed: boolean; retry: () => void;
  canAcknowledge: boolean; canClose: boolean; onAction: (type: ActionType, alert: Alert) => void; onNavigate: (route: string) => void;
}) {
  const record = { ...detail.alert, ...alert };
  return <div className="alerts-inspector-content">
    <header>
      <div className="alerts-inline"><StatusBadge status={record.severity} /><span className={`alerts-status ${statusClass(record.status)}`}>{record.status}</span></div>
      <h2>{record.title}</h2>
      <p className="alerts-context">{record.entity || "Unmapped entity"} · {record.category} · #{record.alertId}</p>
    </header>
    <p className="alerts-message">{record.body || "No event description recorded."}</p>
    <div className="alerts-actions" aria-label="Selected alert actions">
      {canAcknowledge && /open/i.test(record.status) && <button type="button" className="btn-primary btn-compact" onClick={() => onAction("acknowledge", record)}>Acknowledge</button>}
      {canAcknowledge && <button type="button" className="btn-secondary btn-compact" onClick={() => onAction("task", record)}>Create task</button>}
      {canClose && !/closed/i.test(record.status) && <button type="button" className="btn-ghost btn-compact" onClick={() => onAction("close", record)}>Close alert</button>}
    </div>
    <dl className="alerts-metadata">
      <div><dt>Recorded</dt><dd>{record.createdAt ? new Date(record.createdAt).toLocaleString() : "Unavailable"}</dd></div>
      <div><dt>Age</dt><dd>{record.age || "Unavailable"}</dd></div>
      <div><dt>Acknowledged by</dt><dd>{record.acknowledgedBy || "Not recorded"}</dd></div>
      <div><dt>Entity type</dt><dd>{record.entityType || "Not tagged"}</dd></div>
    </dl>
    <button type="button" className="alerts-related" onClick={() => onNavigate(record.entityRoute || routeForCategory(record.category))}>Open related module →</button>
    {record.recommendedAction && <details className="alerts-disclosure"><summary>Action guidance</summary><p>{record.recommendedAction}</p></details>}
    {loading ? <p role="status" className="alerts-context">Loading linked tasks and history…</p> : failed ? <div role="alert" className="alerts-detail-error">Linked tasks and history could not be loaded. <button type="button" onClick={retry}>Retry details</button></div> : <>
      <details className="alerts-disclosure" open={detail.tasks.length > 0}>
        <summary>Follow-up tasks <span>{detail.tasks.length}</span></summary>
        {detail.tasks.length ? detail.tasks.map(task => <div className="alerts-history" key={String(task.id)}><strong>{task.title}</strong><p>{task.status || "Open"} · {task.owner || "Unassigned"} · {task.priority || "Priority not set"}</p>{task.description && <p>{task.description}</p>}</div>) : <p>No follow-up tasks recorded.</p>}
      </details>
      <details className="alerts-disclosure"><summary>Activity history <span>{detail.auditTrail.length}</span></summary>
        {detail.auditTrail.length ? detail.auditTrail.map(entry => <div className="alerts-history" key={String(entry.id)}><strong>{entry.actionName || "Alert event"}</strong><p>{entry.actorName || "system"} · {entry.createdAt ? new Date(entry.createdAt).toLocaleString() : "Unknown time"}</p></div>) : <p>No activity history recorded.</p>}
      </details>
    </>}
    <p className="alerts-source-note">Recorded alert severity. Confirm the source event and current asset context before acting.</p>
  </div>;
}

function MobileInspector({ onClose, children }: { onClose: () => void; children: React.ReactNode }) {
  const ref = useDialogFocus<HTMLDivElement>(true, onClose);
  return <div className="alerts-mobile-overlay" onClick={onClose}><div ref={ref} role="dialog" aria-modal="true" aria-label="Selected alert details" className="alerts-mobile-inspector" onClick={event => event.stopPropagation()}><button type="button" className="btn-ghost alerts-back" onClick={onClose}>← Back to alert queue</button>{children}</div></div>;
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
  const [actionError, setActionError] = useState<string | null>(null);
  const [mobileDetail, setMobileDetail] = useState(false);
  const [page, setPage] = useState(0);
  const [sortOrder, setSortOrder] = useState("priority");
  const [filtersExpanded, setFiltersExpanded] = useState(false);

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
    refetchInterval: 15_000,
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
          alert.body,
          alert.alertType,
          alert.alertId,
          alert.entity,
          alert.entityType,
          alert.category,
          alert.recommendedAction,
        ].some((value) => String(value ?? "").toLowerCase().includes(query));
      })
      .sort((a, b) => {
        const aKnown = Boolean(a.createdAt && Number.isFinite(Date.parse(a.createdAt)));
        const bKnown = Boolean(b.createdAt && Number.isFinite(Date.parse(b.createdAt)));
        if (sortOrder !== "priority" && aKnown !== bKnown) return aKnown ? -1 : 1;
        if (sortOrder === "newest") return ageHours(a.createdAt) - ageHours(b.createdAt);
        if (sortOrder === "oldest") return ageHours(b.createdAt) - ageHours(a.createdAt);
        return Number(/closed/i.test(a.status)) - Number(/closed/i.test(b.status)) || (SEVERITY_ORDER[a.severity] ?? 9) - (SEVERITY_ORDER[b.severity] ?? 9) || ageHours(b.createdAt) - ageHours(a.createdAt);
      });
  }, [alerts, categoryFilter, statusFilter, severityFilter, search, sortOrder]);

  const unresolvedCount = alerts.filter((alert) => !/closed/i.test(alert.status)).length;
  const agingUnresolved = alerts.filter((alert) => !/closed/i.test(alert.status) && ageHours(alert.createdAt) >= 24).length;
  const awaitingAcknowledgement = alerts.filter((alert) => /open/i.test(alert.status) && !alert.acknowledgedBy).length;
  const criticalUnresolved = alerts.filter((alert) => !/closed/i.test(alert.status) && alert.severity === "Critical").length;
  const highUnresolved = alerts.filter((alert) => !/closed/i.test(alert.status) && alert.severity === "High").length;
  const hasActiveFilters = categoryFilter !== "All" || statusFilter !== "All" || severityFilter !== "All" || search.trim().length > 0;
  const detailRecord = normalizeAlertDetail(detailQuery.data as AnyRecord | undefined);
  const currentPage = Math.min(page, Math.max(0, Math.ceil(filtered.length / 25) - 1));
  const visibleRows = filtered.slice(currentPage * 25, (currentPage + 1) * 25);
  const currentSelection = filtered.find(alert => alert.id === selectedAlert?.id) ?? null;
  const actionPending = acknowledgeMutation.isPending || closeMutation.isPending || taskMutation.isPending;

  useEffect(() => { setPage(0); }, [categoryFilter, statusFilter, severityFilter, search, sortOrder]);
  useEffect(() => {
    if (!filtered.length) { setSelectedAlert(null); setMobileDetail(false); return; }
    if (!selectedAlert || !filtered.some(alert => alert.id === selectedAlert.id)) setSelectedAlert(filtered[0]);
  }, [filtered, selectedAlert]);

  function handleAction(type: ActionType, alert: Alert) {
    setActionError(null);
    setActionType(type);
    setActionAlert(alert);
  }

  async function handleActionConfirm(payload: AnyRecord) {
    if (!actionType || !actionAlert || actionPending) return;
    setActionError(null);
    const id = actionAlert.id;
    try {
      if (actionType === "acknowledge") await acknowledgeMutation.mutateAsync({ id, payload });
      else if (actionType === "close") await closeMutation.mutateAsync({ id, payload });
      else await taskMutation.mutateAsync({ id, payload });
      setActionType(null);
      setActionAlert(null);
    } catch (error) {
      const reason = axios.isAxiosError(error) ? (error.response?.data?.message ?? error.response?.data?.error) : error instanceof Error ? error.message : null;
      setActionError(`${typeof reason === "string" ? reason + " " : "The action could not be saved. "}Your text is still here. Retry, or cancel and refresh the alert.`);
    }
  }

  const inspector = currentSelection ? <AlertInspector alert={currentSelection} detail={detailRecord} loading={detailQuery.isLoading} failed={detailQuery.isError} retry={() => void detailQuery.refetch()} canAcknowledge={canAcknowledge} canClose={canClose} onAction={handleAction} onNavigate={navigate} /> : null;

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
    <div className="alerts-workspace page-stack pb-4">
      {toastMsg ? (
        <div className="fixed right-4 top-4 z-50 rounded-2xl border border-emerald-500/20 bg-emerald-600 px-4 py-3 text-sm font-medium text-white shadow-2xl shadow-emerald-900/20">
          {toastMsg}
        </div>
      ) : null}

      <header className="alerts-page-header">
        <div><h1>Alerts Center</h1><p>Review exceptions, act on priorities, and track follow-up.</p></div>
        <div className="alerts-actions"><button type="button" onClick={() => exportCsv("alerts", filtered)} className="btn-secondary btn-compact">Export filtered</button><button type="button" onClick={() => void queryClient.invalidateQueries({ queryKey: ["alerts"] })} className="btn-primary btn-compact"><RefreshCw className="h-4 w-4" /> Refresh</button></div>
      </header>

      <section className="panel p-3" aria-label="Alert queue summary and filters">
        <p className="alerts-scope-label">Loaded queue · {alerts.length} records in your authorized scope</p>
        <div className="alerts-summary-bar">
          <dl className="alerts-summary" aria-label="Alert queue summary for loaded records">
            <CompactMetric label="Unresolved" value={unresolvedCount} detail={`${criticalUnresolved} critical · ${highUnresolved} high`} tone={criticalUnresolved > 0 ? "danger" : "neutral"} />
            <CompactMetric label="Aging unresolved" value={agingUnresolved} detail="24h+ and not closed" tone={agingUnresolved > 0 ? "warning" : "neutral"} />
            <CompactMetric label="Not acknowledged" value={awaitingAcknowledgement} detail="Open alerts" tone={awaitingAcknowledgement > 0 ? "info" : "neutral"} />
            <CompactMetric label="Closed" value={alerts.filter(alert => /closed/i.test(alert.status)).length} detail={`${alerts.length} loaded of ${summary.total || alerts.length}` } tone="success" />
          </dl>

          <div className="relative min-w-0">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                type="search"
                aria-label="Search alerts"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search alerts, entities, categories…"
                className="field w-full pl-9"
              />
          </div>
        </div>

        <button type="button" className="alerts-filter-toggle btn-ghost" aria-expanded={filtersExpanded} aria-controls="alerts-filters" onClick={() => setFiltersExpanded(value => !value)}>Filters and sort{hasActiveFilters ? " · active" : ""}</button>
        <div id="alerts-filters" className={`alerts-filter-controls mt-3 flex flex-wrap items-center gap-2 border-t border-slate-100 pt-3 ${filtersExpanded ? "is-expanded" : ""}`}>
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
          <select aria-label="Sort alerts" className="field alerts-sort" value={sortOrder} onChange={event => setSortOrder(event.target.value)}><option value="priority">Priority · unresolved first</option><option value="oldest">Oldest first</option><option value="newest">Newest first</option></select>
          {hasActiveFilters ? (
            <button type="button" className="btn-ghost btn-compact" onClick={() => { setCategoryFilter("All"); setSeverityFilter("All"); setStatusFilter("All"); setSearch(""); }}>
              Clear filters
            </button>
          ) : null}
          <span className="ml-auto text-xs font-medium text-slate-500" role="status" aria-live="polite">
            <strong className="font-semibold text-slate-700">{filtered.length}</strong> matching · {alerts.length} loaded
          </span>
        </div>
      </section>

      <WorkspaceGuidance
        nextStep={canAcknowledge ? "Select an alert, review its message, then acknowledge it or create follow-up work." : "Select an alert to review its message and history. Your role has view access to this queue."}
        steps={[
          "Use severity, status and search to narrow the queue. Select a row to read its full context; filters and sorting do not change records.",
          "Acknowledge records your review. Create task adds follow-up work; it does not assign or close the alert.",
          "Close ends the alert workflow after your review. It does not confirm that a vehicle or physical condition has recovered.",
        ]}
      />
      <div className="alerts-split">
        <section className="panel alerts-queue" aria-label="Alert work queue">
          <div className="alerts-queue-heading"><h2>Alert queue</h2><span>Choose a row to inspect and act</span></div>
          <div className="alerts-list-scroll">
            <div className="alerts-column-head" aria-hidden="true"><span>Severity</span><span>Alert / recorded message</span><span>Asset</span><span>Status</span><span>Age</span></div>
            {visibleRows.map(alert => <button type="button" key={String(alert.id)} className={`alerts-row ${currentSelection?.id === alert.id ? "is-selected" : ""}`} aria-pressed={currentSelection?.id === alert.id} onClick={() => { setSelectedAlert(alert); if (window.matchMedia("(max-width: 1199px)").matches) setMobileDetail(true); }}>
              <span className={`alerts-severity severity-${alert.severity.toLowerCase()}`}>{alert.severity}</span>
              <span className="alerts-row-description"><strong>{alert.title}</strong><span>{alert.body || "No event description recorded"}</span></span>
              <span className="alerts-row-asset">{alert.entity || "Unmapped"}</span>
              <span className={`alerts-status ${statusClass(alert.status)}`}>{alert.status}</span>
              <span className="alerts-row-age" title={alert.createdAt ? new Date(alert.createdAt).toLocaleString() : "Recorded time unavailable"}>{alert.age || "—"}</span>
            </button>)}
          </div>
          {!filtered.length && <EmptyState title="No alerts match your filters" subtitle="Adjust the search or filters to broaden the current record set." />}
          <footer className="alerts-pagination"><span>{filtered.length ? currentPage * 25 + 1 : 0}–{Math.min((currentPage + 1) * 25, filtered.length)} of {filtered.length} matching</span><div><button type="button" className="btn-ghost btn-compact" disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}>Previous</button><span>Page {currentPage + 1} / {Math.max(1, Math.ceil(filtered.length / 25))}</span><button type="button" className="btn-ghost btn-compact" disabled={(currentPage + 1) * 25 >= filtered.length} onClick={() => setPage(currentPage + 1)}>Next</button></div></footer>
        </section>
        <aside className="panel alerts-desktop-inspector" aria-label="Selected alert details">{inspector || <EmptyState title="No alert selected" subtitle="Choose an alert from the queue." />}</aside>
      </div>
      {mobileDetail && inspector && <MobileInspector onClose={() => setMobileDetail(false)}>{inspector}</MobileInspector>}

      <div className="flex items-center gap-3 text-xs font-medium text-slate-500">
        <Clock3 className="h-3.5 w-3.5" />
        Refreshed every 15 seconds from persisted telemetry alert records. Missing records remain unavailable.
      </div>

      <ActionModal
        key={`${actionType}:${actionAlert?.id}`}
        pending={actionPending}
        error={actionError}
        type={actionType}
        alert={actionAlert}
        onClose={() => {
          if (actionPending) return;
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
      <dd className="mt-1 flex min-w-0 items-baseline gap-2 flex-wrap">
        <strong className="text-lg font-bold leading-none tabular-nums">{value}</strong>
        <span className="min-w-0 text-[11px] font-medium" title={detail}>{detail}</span>
      </dd>
    </div>
  );
}
