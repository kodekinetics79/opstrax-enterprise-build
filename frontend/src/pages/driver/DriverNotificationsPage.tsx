import { useState } from "react";
import { Bell, CheckCheck, Eye } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { notificationsApi } from "@/services/notificationsApi";
import { ErrorState } from "@/components/ui";

type AnyRecord = Record<string, unknown>;

const SEVERITY_COLOR: Record<string, string> = {
  Critical: "bg-red-100 text-red-700 border-red-200",
  High:     "bg-orange-100 text-orange-700 border-orange-200",
  Medium:   "bg-amber-100 text-amber-700 border-amber-200",
  Low:      "bg-sky-100 text-sky-700 border-sky-200",
};

function formatTime(val: unknown): string {
  if (!val) return "";
  try {
    const d = new Date(String(val));
    const now = Date.now();
    const diff = now - d.getTime();
    if (diff < 60_000) return "just now";
    if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`;
    if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`;
    return d.toLocaleDateString();
  } catch { return String(val); }
}

export function DriverNotificationsPage() {
  const qc = useQueryClient();
  const [expanded, setExpanded] = useState<number | null>(null);

  const { data = [], isLoading, isError, error } = useQuery({
    queryKey: ["notifications"],
    queryFn:  notificationsApi.list,
    refetchInterval: 30_000,
  });

  const markRead = useMutation({
    mutationFn: (id: number) => notificationsApi.markRead(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["notifications"] }),
  });

  const acknowledge = useMutation({
    mutationFn: (id: number) => notificationsApi.acknowledge(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["notifications"] }),
  });

  const rows = data as AnyRecord[];
  const unreadCount = rows.filter((n) => String(n.status) === "unread").length;

  if (isError) return <ErrorState message={(error as Error)?.message} />;

  return (
    <div className="min-h-screen bg-slate-50 pb-24">
      {/* Header */}
      <div className="sticky top-0 z-10 bg-white border-b border-slate-200 px-4 py-3">
        <div className="flex items-center gap-2">
          <Bell className="h-5 w-5 text-indigo-500" />
          <h1 className="text-base font-extrabold text-slate-900">Notifications</h1>
          {unreadCount > 0 && (
            <span className="ml-1 rounded-full bg-indigo-500 px-2 py-0.5 text-[10px] font-bold text-white">
              {unreadCount}
            </span>
          )}
        </div>
      </div>

      {/* List */}
      <div className="px-4 py-3 space-y-2">
        {isLoading ? (
          <div className="py-8 text-center text-sm text-slate-400">Loading...</div>
        ) : rows.length === 0 ? (
          <div className="py-8 text-center text-sm text-slate-400">
            <Bell className="h-10 w-10 text-slate-300 mx-auto mb-2" />
            No notifications
          </div>
        ) : (
          rows.map((n) => {
            const id    = Number(n.id);
            const isNew = String(n.status) === "unread";
            const open  = expanded === id;
            const sevCls = SEVERITY_COLOR[String(n.severity ?? "")] ?? "bg-slate-100 text-slate-600 border-slate-200";

            return (
              <div
                key={String(n.id)}
                className={`rounded-xl border bg-white shadow-sm overflow-hidden ${isNew ? "border-indigo-200" : "border-slate-200"}`}
              >
                <button
                  className="w-full text-left p-4"
                  onClick={() => {
                    setExpanded(open ? null : id);
                    if (isNew) markRead.mutate(id);
                  }}
                >
                  <div className="flex items-start gap-3">
                    {isNew && (
                      <span className="mt-1 h-2 w-2 rounded-full bg-indigo-500 flex-shrink-0" />
                    )}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-bold ${sevCls}`}>
                          {String(n.severity ?? "")}
                        </span>
                        <span className="text-xs text-slate-400">{formatTime(n.createdAt)}</span>
                      </div>
                      <p className="mt-1 text-sm font-semibold text-slate-900">{String(n.title ?? "")}</p>
                      {!open && (
                        <p className="mt-0.5 text-xs text-slate-500 truncate">{String(n.message ?? "")}</p>
                      )}
                    </div>
                  </div>
                </button>

                {open && (
                  <div className="px-4 pb-4 space-y-3">
                    <p className="text-sm text-slate-700">{String(n.message ?? "")}</p>
                    {String(n.status) !== "acknowledged" && (
                      <button
                        onClick={() => acknowledge.mutate(id)}
                        disabled={acknowledge.isPending}
                        className="flex w-full items-center justify-center gap-2 rounded-xl bg-emerald-500 py-2.5 text-sm font-semibold text-white hover:bg-emerald-600"
                      >
                        <CheckCheck className="h-4 w-4" />
                        Acknowledge
                      </button>
                    )}
                    {String(n.status) === "acknowledged" && (
                      <div className="flex items-center gap-1.5 text-xs text-emerald-600 font-semibold">
                        <CheckCheck className="h-3.5 w-3.5" />
                        Acknowledged
                      </div>
                    )}
                    {String(n.status) === "read" && (
                      <div className="flex items-center gap-1.5 text-xs text-slate-400">
                        <Eye className="h-3.5 w-3.5" />
                        Read
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
