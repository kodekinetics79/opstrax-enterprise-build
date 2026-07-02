import { useState } from "react";
import { Bell, CheckCheck, Eye, Filter, RefreshCw, X } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { notificationsApi } from "@/services/notificationsApi";

type AnyRecord = Record<string, unknown>;

const SEVERITY_COLOR: Record<string, string> = {
  Critical: "border-red-400/30 bg-red-50 text-red-700",
  High:     "border-orange-400/30 bg-orange-50 text-orange-700",
  Medium:   "border-amber-400/30 bg-amber-50 text-amber-700",
  Low:      "border-sky-400/30 bg-sky-50 text-sky-700",
};

const STATUS_COLOR: Record<string, string> = {
  unread:       "border-blue-400/30 bg-blue-50 text-blue-700",
  read:         "border-slate-300/30 bg-slate-50 text-slate-500",
  acknowledged: "border-emerald-400/30 bg-emerald-50 text-emerald-700",
  escalated:    "border-purple-400/30 bg-purple-50 text-purple-700",
};

function SeverityBadge({ severity }: { severity: string }) {
  const cls = SEVERITY_COLOR[severity] ?? "border-slate-300/20 bg-slate-50 text-slate-500";
  return (
    <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>
      {severity}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const cls = STATUS_COLOR[status] ?? "border-slate-300/20 bg-slate-50 text-slate-500";
  return (
    <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>
      {status}
    </span>
  );
}

function formatTime(val: unknown): string {
  if (!val) return "—";
  try { return new Date(String(val)).toLocaleString(); } catch { return String(val); }
}

export function NotificationCenterPage() {
  const qc = useQueryClient();
  const [filterSeverity, setFilterSeverity] = useState("");
  const [filterStatus,   setFilterStatus]   = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [ackNote, setAckNote]   = useState("");

  const { data = [], isLoading, refetch } = useQuery({
    queryKey: ["notifications"],
    queryFn:  notificationsApi.list,
    refetchInterval: 30_000,
  });

  const markRead = useMutation({
    mutationFn: (id: number) => notificationsApi.markRead(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["notifications"] }),
  });

  const acknowledge = useMutation({
    mutationFn: ({ id, note }: { id: number; note?: string }) => notificationsApi.acknowledge(id, note),
    onSuccess:  () => { qc.invalidateQueries({ queryKey: ["notifications"] }); setSelected(null); setAckNote(""); },
  });

  const ackAll = useMutation({
    mutationFn: notificationsApi.acknowledgeAll,
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["notifications"] }),
  });

  const rows = (data as AnyRecord[]).filter((n) => {
    if (filterSeverity && String(n.severity ?? "") !== filterSeverity) return false;
    if (filterStatus   && String(n.status   ?? "") !== filterStatus)   return false;
    return true;
  });

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900 flex items-center gap-2">
            <Bell className="h-5 w-5 text-indigo-500" />
            Notification Center
          </h1>
          <p className="mt-0.5 text-sm text-slate-500">Real-time notifications, escalations and acknowledgements</p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => ackAll.mutate()}
            disabled={ackAll.isPending}
            className="flex items-center gap-1.5 rounded-lg border border-emerald-300 bg-emerald-50 px-3 py-1.5 text-xs font-semibold text-emerald-700 hover:bg-emerald-100"
          >
            <CheckCheck className="h-3.5 w-3.5" />
            Acknowledge All
          </button>
          <button
            onClick={() => refetch()}
            className="flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-1.5 text-xs font-semibold text-slate-600 hover:bg-slate-50"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </button>
        </div>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-3">
        <div className="flex items-center gap-2">
          <Filter className="h-4 w-4 text-slate-400" />
          <select
            value={filterSeverity}
            onChange={(e) => setFilterSeverity(e.target.value)}
            className="rounded border border-slate-200 px-2 py-1 text-xs text-slate-700"
          >
            <option value="">All Severities</option>
            {["Critical","High","Medium","Low"].map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
          <select
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value)}
            className="rounded border border-slate-200 px-2 py-1 text-xs text-slate-700"
          >
            <option value="">All Statuses</option>
            {["unread","read","acknowledged","escalated"].map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>
        <span className="ml-auto text-xs text-slate-400">{rows.length} notifications</span>
      </div>

      {/* Table */}
      <div className="rounded-xl border border-slate-200 bg-white shadow-sm overflow-hidden">
        {isLoading ? (
          <div className="p-8 text-center text-sm text-slate-400">Loading notifications...</div>
        ) : rows.length === 0 ? (
          <div className="p-8 text-center text-sm text-slate-400">No notifications found</div>
        ) : (
          <table className="w-full text-sm">
            <thead className="border-b border-slate-100 bg-slate-50 text-xs text-slate-500">
              <tr>
                <th className="px-4 py-3 text-left">Title</th>
                <th className="px-4 py-3 text-left">Event Type</th>
                <th className="px-4 py-3 text-left">Severity</th>
                <th className="px-4 py-3 text-left">Status</th>
                <th className="px-4 py-3 text-left">Created</th>
                <th className="px-4 py-3 text-left">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {rows.map((n) => (
                <tr
                  key={String(n.id)}
                  className={`hover:bg-slate-50 cursor-pointer ${String(n.status) === "unread" ? "bg-blue-50/20" : ""}`}
                  onClick={() => setSelected(n)}
                >
                  <td className="px-4 py-3 font-medium text-slate-900 max-w-xs truncate">{String(n.title ?? "")}</td>
                  <td className="px-4 py-3 text-slate-500">{String(n.eventType ?? "")}</td>
                  <td className="px-4 py-3"><SeverityBadge severity={String(n.severity ?? "")} /></td>
                  <td className="px-4 py-3"><StatusBadge status={String(n.status ?? "")} /></td>
                  <td className="px-4 py-3 text-slate-400 text-xs">{formatTime(n.createdAt)}</td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
                      {String(n.status) === "unread" && (
                        <button
                          onClick={() => markRead.mutate(Number(n.id))}
                          className="rounded bg-sky-50 px-2 py-0.5 text-[10px] font-semibold text-sky-700 hover:bg-sky-100"
                        >
                          <Eye className="h-3 w-3 inline mr-0.5" />
                          Read
                        </button>
                      )}
                      {String(n.status) !== "acknowledged" && (
                        <button
                          onClick={() => acknowledge.mutate({ id: Number(n.id) })}
                          className="rounded bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold text-emerald-700 hover:bg-emerald-100"
                        >
                          <CheckCheck className="h-3 w-3 inline mr-0.5" />
                          Ack
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Detail drawer */}
      {selected && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30">
          <div className="w-full max-w-lg rounded-2xl bg-white shadow-2xl p-6 space-y-4">
            <div className="flex items-start justify-between">
              <div>
                <h2 className="text-base font-extrabold text-slate-900">{String(selected.title ?? "")}</h2>
                <p className="text-xs text-slate-400 mt-0.5">{String(selected.eventType ?? "")} — {formatTime(selected.createdAt)}</p>
              </div>
              <button onClick={() => setSelected(null)} className="text-slate-400 hover:text-slate-600">
                <X className="h-4 w-4" />
              </button>
            </div>
            <div className="flex gap-2">
              <SeverityBadge severity={String(selected.severity ?? "")} />
              <StatusBadge status={String(selected.status ?? "")} />
            </div>
            <p className="text-sm text-slate-700">{String(selected.message ?? "")}</p>
            {selected.sourceType != null && (
              <p className="text-xs text-slate-400">Source: {String(selected.sourceType)} #{String(selected.sourceId ?? "")}</p>
            )}
            {String(selected.status) !== "acknowledged" && (
              <div className="space-y-2 pt-2 border-t border-slate-100">
                <textarea
                  placeholder="Acknowledgement note (optional)"
                  value={ackNote}
                  onChange={(e) => setAckNote(e.target.value)}
                  className="w-full rounded-lg border border-slate-200 p-2 text-sm resize-none"
                  rows={2}
                />
                <button
                  onClick={() => acknowledge.mutate({ id: Number(selected.id), note: ackNote || undefined })}
                  disabled={acknowledge.isPending}
                  className="btn-primary w-full"
                >
                  Acknowledge
                </button>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
