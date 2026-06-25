import { useState, useMemo } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { alertsApi } from "@/services/alertsApi";
import { useHasPermission } from "@/hooks/usePermission";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";
import { AlertTriangle, BadgeCheck, BellRing, Clock3, Filter, RefreshCw, Search, ShieldAlert, Sparkles } from "lucide-react";

// ── Types ─────────────────────────────────────────────────────────────────────

type Alert = {
  id: string | number;
  alertId?: string;
  title?: string;
  type?: string;
  body?: string;
  severity: "Critical" | "High" | "Warning" | "Info";
  status: string;
  category: string;
  alertType?: string;
  entity?: string;
  entityType?: string;
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

// ── Constants ─────────────────────────────────────────────────────────────────

const SEVERITY_ORDER: Record<string, number> = { Critical: 0, High: 1, Warning: 2, Info: 3 };

const SEVERITY_STYLES: Record<string, string> = {
  Critical: "bg-red-50 border-red-200 text-red-700",
  High: "bg-orange-50 border-orange-200 text-orange-700",
  Warning: "bg-amber-50 border-amber-200 text-amber-700",
  Info: "bg-sky-50 border-sky-200 text-sky-700",
};

const SEVERITY_DOT: Record<string, string> = {
  Critical: "bg-red-500",
  High: "bg-orange-500",
  Warning: "bg-amber-500",
  Info: "bg-sky-500",
};

const STATUS_STYLES: Record<string, string> = {
  Open: "bg-red-50 border-red-200 text-red-700",
  "In Progress": "bg-blue-50 border-blue-200 text-blue-700",
  Acknowledged: "bg-violet-50 border-violet-200 text-violet-700",
  Closed: "bg-slate-100 border-slate-200 text-slate-500",
};

const CATEGORIES = ["All", "Safety", "Maintenance", "Customer", "Compliance", "Telematics", "Operations"] as const;
type CategoryFilter = (typeof CATEGORIES)[number];

const STATUS_FILTERS = ["All", "Open", "In Progress", "Acknowledged", "Closed"] as const;
type StatusFilter = (typeof STATUS_FILTERS)[number];

const SEVERITY_FILTERS = ["All", "Critical", "High", "Warning", "Info"] as const;
type SeverityFilter = (typeof SEVERITY_FILTERS)[number];

const CROSS_MODULE_ROUTES: Record<string, string> = {
  Safety: "/safety",
  Maintenance: "/maintenance",
  Customer: "/customers",
  Compliance: "/compliance",
  Telematics: "/iot-devices",
};

// ── Helper: normalize raw alert row to typed Alert ────────────────────────────

function normalizeAlert(raw: AnyRecord): Alert {
  const id = (raw.id ?? raw.alertId) as string | number;
  return {
    id,
    alertId: String(raw.alertId ?? raw.id ?? ""),
    title: String(raw.title ?? raw.type ?? "Alert"),
    body: String(raw.body ?? raw.recommendedAction ?? ""),
    severity: (raw.severity as Alert["severity"]) ?? "Info",
    status: String(raw.status ?? "Open"),
    category: String(raw.category ?? "Operations"),
    alertType: String(raw.alertType ?? raw.type ?? raw.alert_type ?? ""),
    entity: String(raw.entity ?? raw.entityType ?? ""),
    entityType: String(raw.entityType ?? raw.entity_type ?? ""),
    customer: String(raw.customer ?? ""),
    owner: String(raw.owner ?? raw.acknowledgedBy ?? ""),
    location: String(raw.location ?? ""),
    age: String(raw.age ?? ""),
    recommendedAction: String(raw.recommendedAction ?? raw.recommended_action ?? raw.body ?? ""),
    acknowledgedAt: raw.acknowledgedAt ? String(raw.acknowledgedAt) : undefined,
    closedAt: raw.closedAt ? String(raw.closedAt) : undefined,
    acknowledgedBy: raw.acknowledgedBy ? String(raw.acknowledgedBy) : undefined,
    createdAt: raw.createdAt ? String(raw.createdAt) : undefined,
  };
}

// ── KPI Card ─────────────────────────────────────────────────────────────────

function KpiCard({ label, value, accent }: { label: string; value: number | string; accent?: string }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white/85 p-4 shadow-sm backdrop-blur">
      <span className={`text-3xl font-semibold tracking-tight ${accent ?? "text-slate-900"}`}>{value}</span>
      <span className="mt-1 block text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{label}</span>
    </div>
  );
}

// ── Acknowledge / Close / Task modal ─────────────────────────────────────────

type ActionType = "acknowledge" | "close" | "task" | null;

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

  const configs = {
    acknowledge: { title: "Acknowledge Alert", label: "Note (optional)", cta: "Acknowledge", cta_cls: "bg-violet-600 hover:bg-violet-700" },
    close: { title: "Close Alert", label: "Resolution summary", cta: "Close Alert", cta_cls: "bg-slate-700 hover:bg-slate-800" },
    task: { title: "Create Follow-up Task", label: "Task description", cta: "Create Task", cta_cls: "bg-teal-600 hover:bg-teal-700" },
  };
  const cfg = configs[type];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm" onClick={onClose}>
      <div
        className="panel w-full max-w-md mx-4 flex flex-col gap-4"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
      >
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold text-slate-900">{cfg.title}</h3>
          <button type="button" className="text-slate-400 hover:text-slate-600" onClick={onClose}>
            ✕
          </button>
        </div>
        <p className="text-sm text-slate-600">
          <span className="font-medium">{alert.title}</span> &mdash; {alert.severity} / {alert.category}
        </p>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-slate-700">{cfg.label}</label>
          <textarea
            className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 resize-none focus:outline-none focus:ring-2 focus:ring-teal-400"
            rows={3}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Enter details..."
          />
        </div>
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className={`text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors ${cfg.cta_cls}`}
            onClick={() => {
              const payload: AnyRecord =
                type === "acknowledge" ? { note } : type === "close" ? { resolution: note } : { title: note || `Follow-up: ${alert.title}` };
              onConfirm(payload);
            }}
          >
            {cfg.cta}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Detail Drawer ─────────────────────────────────────────────────────────────

function DetailDrawer({
  alert,
  onClose,
  canAcknowledge,
  canClose,
  onAction,
}: {
  alert: Alert | null;
  onClose: () => void;
  canAcknowledge: boolean;
  canClose: boolean;
  onAction: (type: ActionType, alert: Alert) => void;
}) {
  if (!alert) return null;

  const crossRoute = CROSS_MODULE_ROUTES[alert.category];

  const priorityExplanation = (() => {
    if (alert.severity === "Critical") return "This alert requires immediate attention. Critical severity indicates a safety, compliance, or operational risk that can result in regulatory penalties, driver injury, or customer SLA breach if unresolved.";
    if (alert.severity === "High") return "High severity alerts represent elevated risk. These should be reviewed and actioned within 4 hours to prevent escalation.";
    if (alert.severity === "Warning") return "Warning-level alerts indicate potential issues that should be reviewed within 24 hours. Early action prevents escalation to Critical.";
    return "Informational alert. No immediate action required — review during normal operations cycle.";
  })();

  return (
    <div className="fixed inset-0 z-40 flex justify-end" onClick={onClose}>
      <div
        className="bg-slate-950 w-full max-w-sm h-full flex flex-col shadow-2xl overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
          <span className="text-sm font-semibold text-white">Alert Detail</span>
          <button type="button" className="text-slate-400 hover:text-white" onClick={onClose}>✕</button>
        </div>

        {/* Severity / Status badges */}
        <div className="px-5 pt-4 pb-2 flex flex-wrap gap-2">
          <span className={`text-xs font-semibold px-2.5 py-1 rounded-full border ${SEVERITY_STYLES[alert.severity] ?? ""}`}>
            {alert.severity}
          </span>
          <span className={`text-xs font-semibold px-2.5 py-1 rounded-full border ${STATUS_STYLES[alert.status] ?? "bg-slate-100 border-slate-200 text-slate-600"}`}>
            {alert.status}
          </span>
          <span className="text-xs font-semibold px-2.5 py-1 rounded-full border bg-slate-800 border-white/8 text-slate-300">
            {alert.category}
          </span>
        </div>

        {/* Title */}
        <div className="px-5 pb-4 border-b border-white/6">
          <p className="text-base font-semibold text-white leading-snug">{alert.title}</p>
          {alert.alertId && <p className="text-xs text-slate-400 mt-0.5">{alert.alertId}</p>}
        </div>

        {/* Info grid */}
        <div className="px-5 py-4 grid grid-cols-2 gap-x-4 gap-y-3 border-b border-white/6 text-sm">
          {alert.entity && (
            <>
              <span className="text-slate-400">Entity</span>
              <span className="text-white font-medium">{alert.entity}</span>
            </>
          )}
          {alert.customer && (
            <>
              <span className="text-slate-400">Customer</span>
              <span className="text-white font-medium">{alert.customer}</span>
            </>
          )}
          {alert.location && (
            <>
              <span className="text-slate-400">Location</span>
              <span className="text-white font-medium">{alert.location}</span>
            </>
          )}
          {alert.age && (
            <>
              <span className="text-slate-400">Age</span>
              <span className="text-white font-medium">{alert.age}</span>
            </>
          )}
          {alert.acknowledgedAt && (
            <>
              <span className="text-slate-400">Acknowledged</span>
              <span className="text-white font-medium">{new Date(alert.acknowledgedAt).toLocaleString()}</span>
            </>
          )}
          {alert.acknowledgedBy && (
            <>
              <span className="text-slate-400">By</span>
              <span className="text-white font-medium">{alert.acknowledgedBy}</span>
            </>
          )}
        </div>

        {/* Recommended action */}
        {alert.recommendedAction && (
          <div className="px-5 py-4 border-b border-white/6">
            <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Recommended Action</p>
            <p className="text-sm text-slate-200">{alert.recommendedAction}</p>
          </div>
        )}

        {/* AI priority explanation */}
        <div className="px-5 py-4 border-b border-white/6">
          <p className="text-xs font-semibold text-violet-400 uppercase tracking-wide mb-1.5">Priority Rationale</p>
          <p className="text-sm text-slate-300 leading-relaxed">{priorityExplanation}</p>
        </div>

        {/* Cross-module link */}
        {crossRoute && (
          <div className="px-5 py-3 border-b border-white/6">
            <a
              href={crossRoute}
              className="text-xs text-teal-400 hover:text-teal-300 underline underline-offset-2"
            >
              Open in {alert.category} module →
            </a>
          </div>
        )}

        {/* Actions */}
        <div className="px-5 py-4 flex flex-col gap-2 mt-auto">
          {canAcknowledge && alert.status === "Open" && (
            <button
              type="button"
              className="w-full bg-violet-600 hover:bg-violet-700 text-white text-sm font-medium py-2.5 rounded-lg transition-colors"
              onClick={() => onAction("acknowledge", alert)}
            >
              Acknowledge
            </button>
          )}
          {canAcknowledge && (
            <button
              type="button"
              className="w-full bg-teal-600 hover:bg-teal-700 text-white text-sm font-medium py-2.5 rounded-lg transition-colors"
              onClick={() => onAction("task", alert)}
            >
              Create Task
            </button>
          )}
          {canClose && alert.status !== "Closed" && (
            <button
              type="button"
              className="w-full bg-slate-700 hover:bg-slate-600 text-white text-sm font-medium py-2.5 rounded-lg transition-colors"
              onClick={() => onAction("close", alert)}
            >
              Close Alert
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function AlertsCenterPage() {
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const canAcknowledge = hasPermission("alerts:acknowledge");
  const canClose = hasPermission("alerts:close");

  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>("All");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("All");
  const [severityFilter, setSeverityFilter] = useState<SeverityFilter>("All");
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

  const acknowledgeMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => alertsApi.acknowledge(id, payload),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["alerts"] }); showToast("Alert acknowledged"); },
  });

  const closeMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => alertsApi.close(id, payload),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["alerts"] }); showToast("Alert closed"); },
  });

  const taskMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => alertsApi.createTask(id, payload),
    onSuccess: (res) => { showToast(`Task created: ${(res as AnyRecord).taskId ?? ""}`); },
  });

  function showToast(msg: string) {
    setToastMsg(msg);
    setTimeout(() => setToastMsg(null), 3500);
  }

  const rawAlerts = Array.isArray(alertsQuery.data) ? (alertsQuery.data as AnyRecord[]) : [];
  const alerts = useMemo(() => rawAlerts.map(normalizeAlert), [rawAlerts]);
  const summary = useMemo<AlertsSummary>(() => {
    const live = summaryQuery.data as AnyRecord | undefined;
    if (live) {
      return {
        total: Number(live.total ?? alerts.length),
        critical: Number(live.critical ?? alerts.filter((a) => a.severity === "Critical").length),
        high: Number(live.high ?? alerts.filter((a) => a.severity === "High").length),
        open: Number(live.open ?? alerts.filter((a) => a.status === "Open").length),
        acknowledged: Number(live.acknowledged ?? alerts.filter((a) => a.status === "Acknowledged").length),
        closed: Number(live.closed ?? alerts.filter((a) => a.status === "Closed").length),
      };
    }
    return {
      total: alerts.length,
      critical: alerts.filter((a) => a.severity === "Critical").length,
      high: alerts.filter((a) => a.severity === "High").length,
      open: alerts.filter((a) => a.status === "Open").length,
      acknowledged: alerts.filter((a) => a.status === "Acknowledged").length,
      closed: alerts.filter((a) => a.status === "Closed").length,
    };
  }, [alerts, summaryQuery.data]);

  // Filter + search
  const filtered = useMemo(() => {
    return alerts
      .filter((a) => categoryFilter === "All" || a.category === categoryFilter)
      .filter((a) => statusFilter === "All" || a.status === statusFilter)
      .filter((a) => severityFilter === "All" || a.severity === severityFilter)
      .filter((a) => {
        if (!search) return true;
        const q = search.toLowerCase();
        return (
          a.title?.toLowerCase().includes(q) ||
          a.alertId?.toLowerCase().includes(q) ||
          a.entity?.toLowerCase().includes(q) ||
          a.customer?.toLowerCase().includes(q) ||
          a.category?.toLowerCase().includes(q)
        );
      })
      .sort((a, b) => (SEVERITY_ORDER[a.severity] ?? 9) - (SEVERITY_ORDER[b.severity] ?? 9));
  }, [alerts, categoryFilter, statusFilter, severityFilter, search]);

  function handleAction(type: ActionType, alert: Alert) {
    setSelectedAlert(null);
    setActionType(type);
    setActionAlert(alert);
  }

  function handleActionConfirm(payload: AnyRecord) {
    if (!actionAlert || !actionType) return;
    const id = actionAlert.id ?? actionAlert.alertId!;
    if (actionType === "acknowledge") acknowledgeMutation.mutate({ id, payload });
    else if (actionType === "close") closeMutation.mutate({ id, payload });
    else if (actionType === "task") taskMutation.mutate({ id, payload });
    setActionType(null);
    setActionAlert(null);
  }

  if (alertsQuery.isLoading) return <LoadingState />;
  if (alertsQuery.isError) return <ErrorState message={(alertsQuery.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {toastMsg && (
        <div className="fixed right-4 top-4 z-50 rounded-2xl border border-emerald-500/20 bg-emerald-600 px-4 py-3 text-sm font-medium text-white shadow-2xl shadow-emerald-900/20">
          {toastMsg}
        </div>
      )}

      <section className="relative overflow-hidden rounded-[32px] border border-slate-800 bg-slate-950 px-6 py-7 text-white shadow-[0_28px_80px_rgba(15,23,42,0.28)]">
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_10%_0%,rgba(45,212,191,0.22),transparent_30%),radial-gradient(circle_at_90%_10%,rgba(99,102,241,0.18),transparent_28%),linear-gradient(135deg,rgba(15,23,42,0.98),rgba(15,23,42,0.92))]" />
        <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <div className="inline-flex items-center gap-2 rounded-full border border-emerald-400/20 bg-emerald-400/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.24em] text-emerald-200">
              <BellRing className="h-3.5 w-3.5" />
              Live exception command
            </div>
            <h1 className="mt-4 text-3xl font-semibold tracking-tight sm:text-4xl">Alerts Center</h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-300">
              Real-time exception management for safety, maintenance, compliance, and customer workflows.
              Backed by live API responses, tenant scoping, and server-side permissions.
            </p>
          </div>

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <div className="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 backdrop-blur">
              <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-slate-400">Live status</p>
              <div className="mt-2 flex items-center gap-2 text-sm font-semibold text-white">
                <BadgeCheck className="h-4 w-4 text-emerald-300" />
                Backend connected
              </div>
            </div>
            <div className="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 backdrop-blur">
              <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-slate-400">Open queue</p>
              <div className="mt-2 text-2xl font-semibold text-white">{summary.open}</div>
            </div>
            <div className="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 backdrop-blur">
              <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-slate-400">Critical</p>
              <div className="mt-2 flex items-center gap-2 text-2xl font-semibold text-white">
                {summary.critical}
                <ShieldAlert className="h-5 w-5 text-rose-300" />
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
        <KpiCard label="Total alerts" value={summary.total} accent="text-slate-900" />
        <KpiCard label="Critical" value={summary.critical} accent="text-rose-600" />
        <KpiCard label="High" value={summary.high} accent="text-amber-600" />
        <KpiCard label="Open" value={summary.open} accent="text-blue-600" />
        <KpiCard label="Acknowledged" value={summary.acknowledged} accent="text-violet-600" />
        <KpiCard label="Closed" value={summary.closed} accent="text-emerald-600" />
      </section>

      <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-white/90 shadow-[0_18px_60px_rgba(15,23,42,0.08)] backdrop-blur">
        <div className="border-b border-slate-200 px-5 py-4">
          <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-[0.24em] text-teal-600">Filters</p>
              <p className="mt-1 text-sm text-slate-500">Narrow the live alert queue without leaving the command surface.</p>
            </div>

            <button
              type="button"
              className="inline-flex items-center gap-2 self-start rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100"
              onClick={() => void queryClient.invalidateQueries({ queryKey: ["alerts"] })}
            >
              <RefreshCw className="h-4 w-4" />
              Refresh
            </button>
          </div>

          <div className="mt-4 flex flex-wrap gap-2">
            {CATEGORIES.map((cat) => (
              <button
                key={cat}
                type="button"
                onClick={() => setCategoryFilter(cat)}
                className={`rounded-full border px-3 py-1.5 text-sm font-medium transition ${
                  categoryFilter === cat
                    ? "border-teal-300 bg-teal-50 text-teal-700"
                    : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-slate-100"
                }`}
              >
                {cat}
              </button>
            ))}
          </div>

          <div className="mt-4 grid gap-3 xl:grid-cols-[1fr_auto_auto] xl:items-center">
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                type="search"
                placeholder="Search alerts, entities, customers..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="field w-full rounded-2xl border-slate-200 bg-white pl-10 text-sm shadow-sm"
              />
            </div>

            <div className="flex flex-wrap gap-2">
              {SEVERITY_FILTERS.map((sev) => (
                <button
                  key={sev}
                  type="button"
                  onClick={() => setSeverityFilter(sev)}
                  className={`rounded-full border px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.18em] transition ${
                    severityFilter === sev
                      ? "border-slate-900 bg-slate-900 text-white"
                      : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-slate-100"
                  }`}
                >
                  {sev}
                </button>
              ))}
            </div>

            <div className="flex flex-wrap gap-2">
              {STATUS_FILTERS.map((st) => (
                <button
                  key={st}
                  type="button"
                  onClick={() => setStatusFilter(st)}
                  className={`rounded-full border px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.18em] transition ${
                    statusFilter === st
                      ? "border-violet-300 bg-violet-50 text-violet-700"
                      : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-slate-100"
                  }`}
                >
                  {st}
                </button>
              ))}
            </div>
          </div>
        </div>

        <div className="overflow-hidden">
          {filtered.length === 0 ? (
            <EmptyState title="No alerts match your filters" subtitle="Adjust category, severity, or status filters above." />
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 bg-slate-50/80">
                    <th className="px-5 py-4 text-left text-[11px] font-bold uppercase tracking-[0.22em] text-slate-500">Severity</th>
                    <th className="px-5 py-4 text-left text-[11px] font-bold uppercase tracking-[0.22em] text-slate-500">Alert</th>
                    <th className="px-5 py-4 text-left text-[11px] font-bold uppercase tracking-[0.22em] text-slate-500">Category</th>
                    <th className="px-5 py-4 text-left text-[11px] font-bold uppercase tracking-[0.22em] text-slate-500">Entity</th>
                    <th className="px-5 py-4 text-left text-[11px] font-bold uppercase tracking-[0.22em] text-slate-500">Status</th>
                    <th className="px-5 py-4 text-left text-[11px] font-bold uppercase tracking-[0.22em] text-slate-500">Recommended action</th>
                    <th className="px-5 py-4 text-right text-[11px] font-bold uppercase tracking-[0.22em] text-slate-500">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filtered.map((alert) => (
                    <tr
                      key={String(alert.id ?? alert.alertId)}
                      className="cursor-pointer transition hover:bg-slate-50/80"
                      onClick={() => setSelectedAlert(alert)}
                    >
                      <td className="px-5 py-4">
                        <div className="flex items-center gap-2">
                          <span className={`inline-block h-2.5 w-2.5 rounded-full ${SEVERITY_DOT[alert.severity] ?? "bg-slate-400"}`} />
                          <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${SEVERITY_STYLES[alert.severity] ?? ""}`}>
                            {alert.severity}
                          </span>
                        </div>
                      </td>
                      <td className="px-5 py-4">
                        <p className="font-semibold text-slate-900">{alert.title}</p>
                        {alert.alertId && <p className="mt-1 text-xs text-slate-400">{alert.alertId}</p>}
                      </td>
                      <td className="px-5 py-4 text-slate-700">{alert.category}</td>
                      <td className="px-5 py-4 text-slate-700">{alert.entity || alert.customer || "—"}</td>
                      <td className="px-5 py-4">
                        <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${STATUS_STYLES[alert.status] ?? "bg-slate-100 border-slate-200 text-slate-600"}`}>
                          {alert.status}
                        </span>
                      </td>
                      <td className="px-5 py-4 text-slate-600">
                        <span className="line-clamp-2 block max-w-[28rem]">{alert.recommendedAction || "—"}</span>
                      </td>
                      <td className="px-5 py-4 text-right">
                        <div className="flex justify-end gap-2" onClick={(e) => e.stopPropagation()}>
                          {canAcknowledge && alert.status === "Open" && (
                            <button
                              type="button"
                              className="rounded-full border border-violet-200 bg-violet-50 px-3 py-1.5 text-xs font-semibold text-violet-700 transition hover:bg-violet-100"
                              onClick={() => handleAction("acknowledge", alert)}
                            >
                              Ack
                            </button>
                          )}
                          {canAcknowledge && (
                            <button
                              type="button"
                              className="rounded-full border border-teal-200 bg-teal-50 px-3 py-1.5 text-xs font-semibold text-teal-700 transition hover:bg-teal-100"
                              onClick={() => handleAction("task", alert)}
                            >
                              Task
                            </button>
                          )}
                          {canClose && alert.status !== "Closed" && (
                            <button
                              type="button"
                              className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-semibold text-slate-700 transition hover:bg-slate-100"
                              onClick={() => handleAction("close", alert)}
                            >
                              Close
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>

      <div className="flex items-center gap-3 text-xs font-medium text-slate-500">
        <Clock3 className="h-3.5 w-3.5" />
        Refreshed every 15 seconds from the live backend. No demo fallback is used here.
      </div>

      <DetailDrawer
        alert={selectedAlert}
        onClose={() => setSelectedAlert(null)}
        canAcknowledge={canAcknowledge}
        canClose={canClose}
        onAction={handleAction}
      />

      <ActionModal
        type={actionType}
        alert={actionAlert}
        onClose={() => { setActionType(null); setActionAlert(null); }}
        onConfirm={handleActionConfirm}
      />
    </div>
  );
}
