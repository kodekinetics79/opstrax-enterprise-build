import { useState, useMemo } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { alertsApi } from "@/services/alertsApi";
import { useHasPermission } from "@/hooks/usePermission";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

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
    <div className="panel flex flex-col gap-1 min-w-30">
      <span className={`text-2xl font-bold ${accent ?? "text-slate-900"}`}>{value}</span>
      <span className="text-xs text-slate-500 font-medium">{label}</span>
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

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["alerts"],
    queryFn: () => alertsApi.list(),
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

  const rawAlerts = Array.isArray(data) ? (data as AnyRecord[]) : [];
  const alerts = useMemo(() => rawAlerts.map(normalizeAlert), [rawAlerts]);

  // KPI counts
  const criticalCount = alerts.filter((a) => a.severity === "Critical").length;
  const highCount = alerts.filter((a) => a.severity === "High").length;
  const openCount = alerts.filter((a) => a.status === "Open").length;
  const ackedCount = alerts.filter((a) => a.status === "Acknowledged").length;

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

  if (isLoading) return <LoadingState />;
  if (isError) return <ErrorState message={(error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {/* Toast */}
      {toastMsg && (
        <div className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">
          {toastMsg}
        </div>
      )}

      {/* Page header */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Alerts Center</h1>
          <p className="text-sm text-slate-500 mt-0.5">Real-time exception management — Safety, Maintenance, Compliance &amp; Customer</p>
        </div>
        <button
          type="button"
          className="btn-secondary text-sm"
          onClick={() => exportCsv("alerts-center", filtered as unknown as AnyRecord[])}
        >
          Export CSV
        </button>
      </div>

      {/* KPI Strip */}
      <div className="flex flex-wrap gap-3">
        <KpiCard label="Total Alerts" value={alerts.length} />
        <KpiCard label="Critical" value={criticalCount} accent="text-red-600" />
        <KpiCard label="High" value={highCount} accent="text-orange-600" />
        <KpiCard label="Open" value={openCount} accent="text-blue-600" />
        <KpiCard label="Acknowledged" value={ackedCount} accent="text-violet-600" />
      </div>

      {/* Filters */}
      <div className="panel flex flex-col gap-4">
        {/* Category tabs */}
        <div className="flex flex-wrap gap-1.5">
          {CATEGORIES.map((cat) => (
            <button
              key={cat}
              type="button"
              onClick={() => setCategoryFilter(cat)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                categoryFilter === cat
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {cat}
            </button>
          ))}
        </div>

        {/* Row 2: severity + status + search */}
        <div className="flex flex-wrap gap-3 items-center">
          <div className="flex gap-1">
            {SEVERITY_FILTERS.map((sev) => (
              <button
                key={sev}
                type="button"
                onClick={() => setSeverityFilter(sev)}
                className={`px-2.5 py-1 rounded-md text-xs font-medium border transition-colors ${
                  severityFilter === sev
                    ? "bg-slate-800 border-slate-600 text-white"
                    : "bg-white border-slate-200 text-slate-600 hover:bg-slate-50"
                }`}
              >
                {sev}
              </button>
            ))}
          </div>

          <div className="flex gap-1">
            {STATUS_FILTERS.map((st) => (
              <button
                key={st}
                type="button"
                onClick={() => setStatusFilter(st)}
                className={`px-2.5 py-1 rounded-md text-xs font-medium border transition-colors ${
                  statusFilter === st
                    ? "bg-slate-800 border-slate-600 text-white"
                    : "bg-white border-slate-200 text-slate-600 hover:bg-slate-50"
                }`}
              >
                {st}
              </button>
            ))}
          </div>

          <input
            type="search"
            placeholder="Search alerts…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56"
          />
        </div>
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? (
          <EmptyState title="No alerts match your filters" subtitle="Adjust category, severity, or status filters above" />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Severity</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Alert</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Category</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Entity</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Status</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Recommended Action</th>
                  <th className="text-right px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((alert) => (
                  <tr
                    key={String(alert.id ?? alert.alertId)}
                    className="hover:bg-slate-50 cursor-pointer"
                    onClick={() => setSelectedAlert(alert)}
                  >
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <span className={`inline-block w-2 h-2 rounded-full shrink-0 ${SEVERITY_DOT[alert.severity] ?? "bg-slate-400"}`} />
                        <span className={`text-xs font-semibold px-2 py-0.5 rounded-full border ${SEVERITY_STYLES[alert.severity] ?? ""}`}>
                          {alert.severity}
                        </span>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900 leading-snug">{alert.title}</p>
                      {alert.alertId && <p className="text-xs text-slate-400">{alert.alertId}</p>}
                    </td>
                    <td className="px-4 py-3 text-slate-700">{alert.category}</td>
                    <td className="px-4 py-3 text-slate-700">{alert.entity || alert.customer || "—"}</td>
                    <td className="px-4 py-3">
                      <span className={`text-xs font-semibold px-2 py-0.5 rounded-full border ${STATUS_STYLES[alert.status] ?? "bg-slate-100 border-slate-200 text-slate-600"}`}>
                        {alert.status}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-600 max-w-50 truncate">{alert.recommendedAction || "—"}</td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-1.5" onClick={(e) => e.stopPropagation()}>
                        {canAcknowledge && alert.status === "Open" && (
                          <button
                            type="button"
                            className="text-xs px-2.5 py-1 rounded-md bg-violet-50 border border-violet-200 text-violet-700 hover:bg-violet-100 transition-colors"
                            onClick={() => handleAction("acknowledge", alert)}
                          >
                            Ack
                          </button>
                        )}
                        {canAcknowledge && (
                          <button
                            type="button"
                            className="text-xs px-2.5 py-1 rounded-md bg-teal-50 border border-teal-200 text-teal-700 hover:bg-teal-100 transition-colors"
                            onClick={() => handleAction("task", alert)}
                          >
                            Task
                          </button>
                        )}
                        {canClose && alert.status !== "Closed" && (
                          <button
                            type="button"
                            className="text-xs px-2.5 py-1 rounded-md bg-slate-100 border border-slate-200 text-slate-600 hover:bg-slate-200 transition-colors"
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

      {/* Detail Drawer */}
      <DetailDrawer
        alert={selectedAlert}
        onClose={() => setSelectedAlert(null)}
        canAcknowledge={canAcknowledge}
        canClose={canClose}
        onAction={handleAction}
      />

      {/* Action Modal */}
      <ActionModal
        type={actionType}
        alert={actionAlert}
        onClose={() => { setActionType(null); setActionAlert(null); }}
        onConfirm={handleActionConfirm}
      />
    </div>
  );
}
