import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, CheckCircle2, Download, Edit3, Pause, Play, Plus, ShieldAlert, Trash2, X, Zap } from "lucide-react";
import { ErrorState, KpiCard, LoadingState, PageHeader, Select, StatusBadge, exportCsv } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

const CATEGORIES = ["All", "Speed", "Idling", "Geofence", "HOS", "Maintenance", "Safety", "Fuel", "Temperature"] as const;
type Category = typeof CATEGORIES[number];

const CHANNELS = ["Email", "SMS", "In-App", "Webhook"] as const;

async function fetchRules(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/alert-rules"));
}
async function createRule(payload: AnyRecord) { return apiClient.post("/api/alert-rules", payload); }
async function updateRule(id: string, payload: AnyRecord) { return apiClient.put(`/api/alert-rules/${id}`, payload); }
async function deleteRule(id: string) { return apiClient.delete(`/api/alert-rules/${id}`); }
async function toggleRule(id: string, enabled: boolean) { return apiClient.put(`/api/alert-rules/${id}/toggle`, { enabled }); }

function formatLastTriggered(value: unknown) {
  if (!value) return "Never triggered";
  const date = new Date(String(value));
  if (Number.isNaN(date.getTime())) return String(value);
  return date.toLocaleString();
}

const FIELDS: [string, string, string?][] = [
  ["name",       "Rule Name"],
  ["category",   "Category"],
  ["threshold",  "Threshold / Condition"],
  ["action",     "Action to Take"],
  ["channels",   "Notification Channels"],
  ["priority",   "Priority"],
  ["recipients", "Additional Recipients (email/phone, comma-separated)", "optional"],
];

const PRIORITY_COLOR: Record<string, string> = {
  Critical: "bg-red-50 text-red-700 border-red-200",
  High:     "bg-orange-50 text-orange-700 border-orange-200",
  Medium:   "bg-amber-50 text-amber-700 border-amber-200",
  Low:      "bg-slate-50 text-slate-600 border-slate-200",
};

export function AlertRulesPage() {
  const hasPermission = useHasPermission();
  const canManage = hasPermission("alerts:manage") || hasPermission("users:manage");
  const [category, setCategory] = useState<Category>("All");
  const [statusFilter, setStatusFilter] = useState("All");
  const [query, setQuery] = useState("");
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const qc = useQueryClient();

  const rulesQ = useQuery({ queryKey: ["alert-rules"], queryFn: fetchRules });
  const rules = (rulesQ.data ?? []) as AnyRecord[];

  const save = useMutation({
    mutationFn: (p: AnyRecord) => p.id ? updateRule(String(p.id), p) : createRule(p),
    onSuccess: () => { setEditing(null); qc.invalidateQueries({ queryKey: ["alert-rules"] }); },
  });
  const remove = useMutation({
    mutationFn: (id: string) => deleteRule(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["alert-rules"] }),
  });
  const toggle = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => toggleRule(id, enabled),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["alert-rules"] }),
  });

  const filtered = rules.filter((r) => {
    const cat = category === "All" || String(r.category) === category;
    const st  = statusFilter === "All" || String(r.status) === statusFilter;
    const q   = !query || String(r.name ?? "").toLowerCase().includes(query.toLowerCase()) || String(r.category ?? "").toLowerCase().includes(query.toLowerCase());
    return cat && st && q;
  });

  const activeCount   = rules.filter((r) => String(r.status) === "Active").length;
  const criticalCount = rules.filter((r) => String(r.priority) === "Critical").length;
  const todayCount    = rules.reduce((s, r) => s + Number(r.triggeredToday ?? 0), 0);
  const channelSet    = [...new Set(rules.flatMap((r) => String(r.channels ?? "").split(/,\s*/)))].filter(Boolean).length;

  if (rulesQ.isLoading) return <LoadingState />;
  if (rulesQ.isError) return <ErrorState message={rulesQ.error instanceof Error ? rulesQ.error.message : "Unable to load alert rules."} />;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Alert Rules"
        title="Live alert thresholds and escalation channels"
        description="These rules are now backed by the alert rules table in the backend, not a seeded frontend fallback. Changes here update the live alert-control record for the active tenant."
        actions={
          <>
            <button type="button" className="btn-primary" onClick={() => setEditing({ status: "Active", priority: "Medium", category: "Speed" })} disabled={!canManage} title={!canManage ? "You do not have permission." : undefined}>
              <Plus className="h-4 w-4" /> Create Rule
            </button>
            <button type="button" className="btn-ghost" onClick={() => exportCsv("alert-rules", filtered)}>
              <Download className="h-4 w-4" /> Export
            </button>
          </>
        }
      />

      {/* KPIs */}
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard label="Active Rules"        value={String(activeCount)}   icon={<Bell />}        status="Active"  />
        <KpiCard label="Critical Thresholds" value={String(criticalCount)} icon={<ShieldAlert />} status="Review"  />
        <KpiCard label="Triggered Today"     value={String(todayCount)}    icon={<Zap />}         status={todayCount > 5 ? "Risk" : "Healthy"} />
        <KpiCard label="Channel Types"       value={String(channelSet)}    icon={<CheckCircle2 />}status="Healthy" />
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap items-center gap-3 p-4">
        <div className="flex flex-wrap gap-1.5">
          {CATEGORIES.map((c) => (
            <button key={c} type="button" onClick={() => setCategory(c)}
              className={`rounded-full px-3 py-1 text-xs font-semibold border transition ${category === c ? "bg-teal-50 border-teal-300 text-teal-700" : "border-slate-200 text-slate-500 hover:text-slate-700"}`}>
              {c}
            </button>
          ))}
        </div>
        <Select aria-label="Filter by status" className="ml-auto w-36 text-sm" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
          <option>All</option><option>Active</option><option>Paused</option>
        </Select>
        <input className="field w-52 text-sm" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search rules…" />
      </div>

      {/* Rules Table */}
      <div className="panel overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50">
                {["Rule", "Category", "Threshold", "Action", "Channels", "Priority", "Triggered Today", "Last Triggered", "Status", ""].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500 whitespace-nowrap">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {!filtered.length && (
                <tr><td colSpan={10} className="px-4 py-10 text-center text-sm text-slate-400">No live alert rules are available for this tenant yet.</td></tr>
              )}
              {filtered.map((r) => {
                const isActive = String(r.status) === "Active";
                const priColor = PRIORITY_COLOR[String(r.priority)] ?? PRIORITY_COLOR.Low;
                return (
                  <tr key={String(r.id)} className="transition hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900 whitespace-nowrap">{String(r.name)}</td>
                    <td className="px-4 py-3 text-slate-500">{String(r.category)}</td>
                    <td className="px-4 py-3 font-mono text-xs text-slate-700 whitespace-nowrap">{String(r.threshold)}</td>
                    <td className="px-4 py-3 text-slate-600 max-w-45 truncate">{String(r.action)}</td>
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap gap-1">
                        {String(r.channels ?? "").split(/,\s*/).map((ch) => (
                          <span key={ch} className="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] font-semibold text-slate-600">{ch}</span>
                        ))}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${priColor}`}>{String(r.priority)}</span>
                    </td>
                    <td className="px-4 py-3 text-center">
                      {Number(r.triggeredToday) > 0
                        ? <span className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-red-100 text-[10px] font-bold text-red-700">{Number(r.triggeredToday)}</span>
                        : <span className="text-slate-300">—</span>
                      }
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-400 whitespace-nowrap">{formatLastTriggered(r.lastTriggered)}</td>
                    <td className="px-4 py-3"><StatusBadge status={r.status} /></td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button type="button" aria-label="Edit rule" className="icon-btn text-slate-400" title="Edit rule" disabled={!canManage} onClick={() => setEditing(r)}><Edit3 className="h-3.5 w-3.5" /></button>
                        <button type="button" aria-label={isActive ? "Pause rule" : "Resume rule"} className="icon-btn text-slate-400" title={isActive ? "Pause rule" : "Resume rule"} disabled={!canManage}
                          onClick={() => toggle.mutate({ id: String(r.id), enabled: !isActive })}>
                          {isActive ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
                        </button>
                        <button type="button" aria-label="Delete rule" className="icon-btn text-red-400" title="Delete rule" disabled={!canManage} onClick={() => remove.mutate(String(r.id))}><Trash2 className="h-3.5 w-3.5" /></button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      {/* Channel Summary */}
      <div className="grid gap-4 md:grid-cols-4">
        {CHANNELS.map((ch) => {
          const count = rules.filter((r) => String(r.channels ?? "").includes(ch)).length;
          return (
            <div key={ch} className="panel flex items-center gap-3 p-4">
              <Bell className="h-8 w-8 shrink-0 text-teal-500" />
              <div>
                <p className="text-2xl font-extrabold text-slate-900">{count}</p>
                <p className="text-xs text-slate-500">{ch} rules</p>
              </div>
            </div>
          );
        })}
      </div>

      {editing && (
        <RuleModal initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(p) => save.mutate(p)} />
      )}
    </div>
  );
}

function RuleModal({ initial, saving, onClose, onSave }: { initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (p: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const [channels, setChannels] = useState<string[]>(String(initial.channels ?? "").split(/,\s*/).filter(Boolean));
  const submit = (e: FormEvent) => {
    e.preventDefault();
    onSave({ ...form, channels: channels.join(", ") });
  };
  const toggleChannel = (ch: string) =>
    setChannels((prev) => prev.includes(ch) ? prev.filter((c) => c !== ch) : [...prev, ch]);

  return (
    <div className="fixed inset-0 z-60 grid place-items-center bg-black/60 p-4">
      <form className="panel max-h-[90vh] w-full max-w-2xl overflow-y-auto p-6" onSubmit={submit}>
        <div className="flex items-center justify-between">
          <h2 className="text-2xl font-semibold text-slate-900">{initial.id ? "Edit Alert Rule" : "Create Alert Rule"}</h2>
          <button type="button" className="icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-6 grid gap-4 md:grid-cols-2">
          {FIELDS.map(([key, label, note]) => (
            <label key={key} className={key === "name" || key === "recipients" ? "md:col-span-2" : ""}>
              <span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}{note ? <span className="ml-1 normal-case text-slate-400">({note})</span> : ""}</span>
              {key === "category" ? (
                <Select className="w-full" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))}>
                  {CATEGORIES.filter((c) => c !== "All").map((c) => <option key={c}>{c}</option>)}
                </Select>
              ) : key === "priority" ? (
                <Select className="w-full" value={String(form[key] ?? "Medium")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))}>
                  {["Critical","High","Medium","Low"].map((p) => <option key={p}>{p}</option>)}
                </Select>
              ) : (
                <input className="field" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} required={key !== "recipients"} />
              )}
            </label>
          ))}
        </div>
        <div className="mt-5">
          <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Notification Channels</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {CHANNELS.map((ch) => (
              <button key={ch} type="button"
                className={`rounded-full border px-3 py-1 text-xs font-semibold transition ${channels.includes(ch) ? "bg-teal-50 border-teal-300 text-teal-700" : "border-slate-200 text-slate-500 hover:text-slate-700"}`}
                onClick={() => toggleChannel(ch)}>
                {ch}
              </button>
            ))}
          </div>
        </div>
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={saving}>Save Rule</button>
        </div>
      </form>
    </div>
  );
}
