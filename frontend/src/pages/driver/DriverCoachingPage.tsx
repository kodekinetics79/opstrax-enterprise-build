import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { BookOpen, CheckCircle, XCircle } from "lucide-react";
import { driverApi } from "@/services/driverApi";
import type { AnyRecord } from "@/types";

const PRIORITY_COLOR: Record<string, string> = {
  Critical: "text-red-700 bg-red-50 border-red-200",
  High:     "text-orange-700 bg-orange-50 border-orange-200",
  Medium:   "text-amber-700 bg-amber-50 border-amber-200",
  Low:      "text-slate-600 bg-slate-50 border-slate-200",
};

export function DriverCoachingPage() {
  const qc = useQueryClient();
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [notes, setNotes] = useState<Record<number, string>>({});
  const [ackError, setAckError] = useState<string | null>(null);

  const { data, isLoading, isError, error } = useQuery<AnyRecord>({
    queryKey: ["driver", "coaching"],
    queryFn: driverApi.coaching,
  });

  const ackMut = useMutation({
    mutationFn: ({ id, note }: { id: number; note?: string }) =>
      driverApi.acknowledgeCoaching(id, note),
    onSuccess: (_result, { id }) => {
      setExpandedId(null);
      setNotes(prev => { const n = { ...prev }; delete n[id]; return n; });
      setAckError(null);
      void qc.invalidateQueries({ queryKey: ["driver", "coaching"] });
    },
    onError: (e: Error) => setAckError(e.message),
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <BookOpen className="h-12 w-12 animate-pulse text-slate-300" />
        <p className="text-sm text-slate-500">Loading coaching tasks…</p>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <XCircle className="h-10 w-10 text-red-400" />
        <p className="text-sm font-medium text-red-700">{(error as Error)?.message}</p>
      </div>
    );
  }

  const payload = data ?? {};
  const tasks   = (payload["tasks"] as AnyRecord[]) ?? [];
  const pending = Number(payload["pendingCount"] ?? 0);
  const insights = (payload["insights"] as AnyRecord[]) ?? [];

  return (
    <div className="space-y-4 p-4 pb-10">
      <div className="pt-2">
        <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Safety Coaching</p>
        <h1 className="mt-1 text-xl font-bold text-slate-900">Coaching Tasks</h1>
        {pending > 0 ? (
          <p className="text-sm text-amber-600 font-medium mt-0.5">{pending} pending acknowledgement</p>
        ) : (
          <p className="text-sm text-teal-600 font-medium mt-0.5">All caught up</p>
        )}
      </div>

      {/* System guidance */}
      {insights.map((ins, i) => (
        <div key={i} className={`flex items-start gap-3 rounded-2xl border p-4 ${
          String(ins["level"]) === "ok" ? "bg-green-50 border-green-200 text-green-800" :
          "bg-amber-50 border-amber-200 text-amber-800"
        }`}>
          {String(ins["level"]) === "ok"
            ? <CheckCircle className="h-4 w-4 mt-0.5 shrink-0" />
            : <BookOpen className="h-4 w-4 mt-0.5 shrink-0" />}
          <p className="text-sm font-medium">{String(ins["message"])}</p>
        </div>
      ))}

      {/* Task list */}
      {tasks.length === 0 ? (
        <div className="flex flex-col items-center py-12 text-center">
          <CheckCircle className="h-12 w-12 text-teal-400 mb-3" />
          <p className="text-base font-semibold text-slate-700">No coaching tasks</p>
          <p className="text-sm text-slate-400">You have no pending coaching items.</p>
        </div>
      ) : tasks.map(task => {
        const id       = Number(task["id"]);
        const priority = String(task["priority"] ?? "Low");
        const isAcked  = Boolean(task["driverAcknowledged"]);
        const expanded = expandedId === id;
        const colorClass = PRIORITY_COLOR[priority] ?? PRIORITY_COLOR["Low"];

        return (
          <div
            key={id}
            className={`rounded-2xl border bg-white overflow-hidden ${isAcked ? "opacity-60" : ""}`}
          >
            <button
              type="button"
              className="w-full text-left px-4 py-4"
              onClick={() => setExpandedId(expanded ? null : id)}
            >
              <div className="flex items-start justify-between gap-2">
                <div className="flex-1">
                  <p className="text-sm font-bold text-slate-900">{String(task["title"] ?? "Coaching Task")}</p>
                  <p className="text-xs text-slate-400 mt-0.5">{String(task["coachingType"] ?? "")}</p>
                </div>
                <div className="flex flex-col items-end gap-1">
                  <span className={`rounded-full border px-2 py-0.5 text-xs font-bold ${colorClass}`}>
                    {priority}
                  </span>
                  {isAcked && (
                    <span className="text-xs text-teal-600 font-medium flex items-center gap-1">
                      <CheckCircle className="h-3 w-3" /> Acked
                    </span>
                  )}
                </div>
              </div>
            </button>

            {expanded && (
              <div className="border-t border-slate-100 px-4 pb-4 space-y-3">
                {task["description"] != null && (
                  <p className="text-sm text-slate-600 pt-3">{String(task["description"])}</p>
                )}
                {task["safetyEventType"] != null && (
                  <div className="rounded-xl bg-slate-50 px-3 py-2 text-xs text-slate-500">
                    Related event: <span className="font-semibold">{String(task["safetyEventType"])}</span>
                    {task["safetySeverity"] != null ? ` (${String(task["safetySeverity"])})` : ""}
                  </div>
                )}
                {task["dueAt"] != null && (
                  <p className="text-xs text-slate-400">
                    Due: {new Date(String(task["dueAt"])).toLocaleDateString()}
                  </p>
                )}

                {!isAcked ? (
                  <>
                    <div>
                      <label className="text-xs font-bold text-slate-500 block mb-1">Acknowledgement Note (optional)</label>
                      <textarea
                        className="w-full rounded-xl border border-slate-200 px-3 py-2 text-sm"
                        rows={2}
                        placeholder="Add a note or action you will take…"
                        value={notes[id] ?? ""}
                        onChange={e => setNotes(prev => ({ ...prev, [id]: e.target.value }))}
                      />
                    </div>
                    {ackError && <p className="text-xs text-red-600">{ackError}</p>}
                    <button
                      type="button"
                      disabled={ackMut.isPending}
                      className="w-full rounded-2xl bg-teal-600 py-3 text-sm font-bold text-white disabled:opacity-40"
                      onClick={() => ackMut.mutate({ id, note: notes[id] })}
                    >
                      {ackMut.isPending ? "Acknowledging…" : "Acknowledge"}
                    </button>
                  </>
                ) : (
                  <div className="flex items-center gap-2 text-sm text-teal-700 font-medium">
                    <CheckCircle className="h-4 w-4" />
                    Acknowledged{task["acknowledgedAt"] != null
                      ? ` on ${new Date(String(task["acknowledgedAt"])).toLocaleDateString()}`
                      : ""}
                  </div>
                )}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
