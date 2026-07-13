import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Flag, Plus, Trash2, X } from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { ErrorState, LoadingState, EmptyState, PageHeader } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

// Feature flags — the REAL one. Every control here changes actual behaviour:
//   • enabled=false → the API returns 403 for that feature (Program.cs route gate)
//     and the UI hides it. A true kill switch.
//   • rollout %     → deterministic per-user bucketing, resolved server-side.
const flagsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/feature-flags")),
  create: (body: AnyRecord) => apiClient.post("/api/feature-flags", body),
  update: (key: string, body: AnyRecord) => apiClient.put(`/api/feature-flags/${encodeURIComponent(key)}`, body),
  remove: (key: string) => apiClient.delete(`/api/feature-flags/${encodeURIComponent(key)}`),
};

const EMPTY = { flagKey: "", name: "", description: "", enabled: false, rolloutPct: 100, environment: "production" };

export function FeatureFlagsPage() {
  const qc = useQueryClient();
  const hasPermission = useHasPermission();
  const canManage = hasPermission("users:manage");

  const q = useQuery({ queryKey: ["feature-flags"], queryFn: flagsApi.list });
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [creating, setCreating] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ["feature-flags"] });
    void qc.invalidateQueries({ queryKey: ["feature-flags", "evaluate"] }); // re-resolve the UI's own flags
  };

  const mut = useMutation({
    mutationFn: async (v: { action: "create" | "update" | "delete"; key?: string; body?: AnyRecord }) => {
      if (v.action === "create") return flagsApi.create(v.body!);
      if (v.action === "update") return flagsApi.update(v.key!, v.body!);
      return flagsApi.remove(v.key!);
    },
    onSuccess: () => { setErr(null); setEditing(null); setCreating(false); refresh(); },
    onError: (e) => setErr(e instanceof Error ? e.message : "Action failed"),
  });

  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message} />;

  const rows = (q.data ?? []) as AnyRecord[];

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      <PageHeader
        eyebrow="Governance"
        title="Feature flags"
        description="Kill switches and gradual rollouts. Turning a flag off blocks its API at the edge (403) and hides it in the UI — no deploy needed. Rollout % is a deterministic, stable slice of users."
        actions={canManage ? (
          <button type="button" className="btn-primary" onClick={() => { setCreating(true); setEditing({ ...EMPTY }); }}>
            <Plus className="h-4 w-4" /> New flag
          </button>
        ) : undefined}
      />

      {err && <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">{err}</div>}

      {rows.length === 0 ? (
        <EmptyState title="No feature flags" subtitle="Create a flag to gate a rollout or add a kill switch." />
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {rows.map((f) => {
            const enabled = Boolean(f.enabled);
            const pct = Number(f.rolloutPct ?? 100);
            return (
              <div key={String(f.id)} className="panel flex flex-col gap-3 p-5">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="font-semibold text-slate-900">{String(f.name)}</p>
                    <p className="font-mono text-xs text-slate-400">{String(f.flagKey)}</p>
                  </div>
                  <span className={`shrink-0 rounded-full border px-2 py-0.5 text-[10px] font-bold ${
                    enabled ? "border-teal-200 bg-teal-50 text-teal-700" : "border-red-200 bg-red-50 text-red-700"}`}>
                    {enabled ? "ON" : "OFF"}
                  </span>
                </div>

                {f.description ? <p className="text-xs leading-5 text-slate-500">{String(f.description)}</p> : null}

                <div>
                  <div className="mb-1 flex items-center justify-between text-xs text-slate-500">
                    <span>Rollout</span>
                    <span className="font-semibold text-slate-700">{enabled ? `${pct}%` : "0% (off)"}</span>
                  </div>
                  <div className="h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
                    <div className={`h-full rounded-full ${enabled ? "bg-teal-500" : "bg-slate-300"}`}
                         style={{ width: `${enabled ? Math.min(100, Math.max(0, pct)) : 0}%` }} />
                  </div>
                </div>

                <p className="text-[11px] text-slate-400">
                  {String(f.environment ?? "production")}
                  {f.updatedBy ? ` · last changed by ${String(f.updatedBy)}` : ""}
                </p>

                {canManage && (
                  <div className="mt-auto flex flex-wrap gap-2 border-t border-slate-100 pt-3">
                    <button type="button" className="btn-ghost text-xs" disabled={mut.isPending}
                      onClick={() => mut.mutate({ action: "update", key: String(f.flagKey), body: { enabled: !enabled } })}>
                      {enabled ? "Turn off" : "Turn on"}
                    </button>
                    <button type="button" className="btn-ghost text-xs" disabled={mut.isPending}
                      onClick={() => { setCreating(false); setEditing({ ...f }); }}>
                      Edit
                    </button>
                    <button type="button" className="btn-ghost text-xs text-red-600" disabled={mut.isPending}
                      onClick={() => { if (confirm(`Delete flag "${String(f.flagKey)}"?`)) mut.mutate({ action: "delete", key: String(f.flagKey) }); }}>
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {editing && (
        <FlagModal
          flag={editing}
          isNew={creating}
          busy={mut.isPending}
          onClose={() => { setEditing(null); setCreating(false); }}
          onSave={(body) =>
            creating
              ? mut.mutate({ action: "create", body })
              : mut.mutate({ action: "update", key: String(editing.flagKey), body })
          }
        />
      )}
    </div>
  );
}

function FlagModal({ flag, isNew, busy, onClose, onSave }: {
  flag: AnyRecord; isNew: boolean; busy: boolean; onClose: () => void; onSave: (body: AnyRecord) => void;
}) {
  const [form, setForm] = useState({
    flagKey: String(flag.flagKey ?? ""),
    name: String(flag.name ?? ""),
    description: String(flag.description ?? ""),
    enabled: Boolean(flag.enabled),
    rolloutPct: Number(flag.rolloutPct ?? 100),
    environment: String(flag.environment ?? "production"),
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="w-full max-w-md rounded-2xl bg-white shadow-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-slate-100 px-6 py-4">
          <p className="flex items-center gap-2 font-semibold text-slate-900">
            <Flag className="h-4 w-4 text-teal-600" /> {isNew ? "New flag" : `Edit — ${form.flagKey}`}
          </p>
          <button type="button" onClick={onClose}><X className="h-5 w-5 text-slate-400" /></button>
        </div>

        <div className="space-y-4 px-6 py-5">
          {isNew && (
            <label className="block">
              <span className="field-label">Key (used in code)</span>
              <input className="field mt-1 font-mono" placeholder="new_dispatch_board"
                value={form.flagKey}
                onChange={(e) => setForm({ ...form, flagKey: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, "_") })} />
            </label>
          )}
          <label className="block">
            <span className="field-label">Name</span>
            <input className="field mt-1" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </label>
          <label className="block">
            <span className="field-label">Description</span>
            <input className="field mt-1" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
          </label>

          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
            Enabled (unchecking is a hard kill switch — overrides rollout)
          </label>

          <label className="block">
            <span className="field-label">Rollout — {form.rolloutPct}% of users</span>
            <input type="range" min={0} max={100} step={1} className="mt-2 w-full"
              value={form.rolloutPct} disabled={!form.enabled}
              onChange={(e) => setForm({ ...form, rolloutPct: Number(e.target.value) })} />
            <span className="text-[11px] text-slate-400">
              Deterministic: the same users stay in the slice as you raise the percentage.
            </span>
          </label>

          <label className="block">
            <span className="field-label">Environment</span>
            <select className="field mt-1" value={form.environment} onChange={(e) => setForm({ ...form, environment: e.target.value })}>
              <option value="production">production</option>
              <option value="staging">staging</option>
              <option value="development">development</option>
            </select>
          </label>
        </div>

        <div className="flex justify-end gap-3 border-t border-slate-100 px-6 py-4">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-primary text-sm disabled:opacity-50"
            disabled={busy || !form.name.trim() || (isNew && !form.flagKey.trim())}
            onClick={() => onSave({ ...form })}>
            {busy ? "Saving…" : isNew ? "Create flag" : "Save changes"}
          </button>
        </div>
      </div>
    </div>
  );
}
