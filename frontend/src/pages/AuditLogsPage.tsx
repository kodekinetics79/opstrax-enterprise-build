import { useState } from "react";
import {
  Bot, CheckCircle2, Download, Filter, Search, Shield, Sparkles, X,
} from "lucide-react";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasPermission } from "@/hooks/usePermission";
import { Select } from "@/components/ui";
import {
  useAuditLogs, useAuditExportRequests, useCreateAuditExport, useAuditAiRecs,
} from "@/hooks/useBatch7";

type AnyRecord = Record<string, unknown>;

const TABS = ["Audit Trail", "Export Requests", "Operations Advisor"] as const;
type Tab = typeof TABS[number];

const MODULE_OPTIONS = [
  "command-center", "control-tower", "dispatch", "fleet", "compliance", "hos-eld",
  "safety", "maintenance", "finance", "reports-analytics", "sla-kpi", "executive",
];

const SEVERITY_OPTIONS = ["Info", "Warning", "High", "Critical"];

const SEVERITY_COLOR: Record<string, string> = {
  "Info":     "border-sky-400/30 bg-sky-50 text-sky-700",
  "Warning":  "border-amber-400/30 bg-amber-50 text-amber-700",
  "High":     "border-orange-400/30 bg-orange-50 text-orange-700",
  "Critical": "border-red-400/30 bg-red-50 text-red-700",
};

function SeverityBadge({ severity }: { severity: string }) {
  const cls = SEVERITY_COLOR[severity] ?? "border-slate-400/20 bg-slate-400/10 text-slate-400";
  return <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>{severity}</span>;
}

function ExportStatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Pending:   "border-amber-400/30 bg-amber-50 text-amber-700",
    Processing:"border-sky-400/30 bg-sky-50 text-sky-700",
    Completed: "border-emerald-400/30 bg-emerald-50 text-emerald-700",
    Failed:    "border-red-400/30 bg-red-50 text-red-700",
  };
  const cls = map[status] ?? "border-slate-200 bg-slate-50 text-slate-600";
  return <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>{status}</span>;
}

export function AuditLogsPage() {
  const hasPermission = useHasPermission();
  const canExportReports = hasPermission(PERMISSIONS.REPORTS_EXPORT);
  const [tab, setTab]                   = useState<Tab>("Audit Trail");
  const [search, setSearch]             = useState("");
  const [filterModule, setFilterModule] = useState("");
  const [filterSev, setFilterSev]       = useState("");
  const [exportOpen, setExportOpen]     = useState(false);
  const [drawerLog, setDrawerLog]       = useState<AnyRecord | null>(null);

  const params: Record<string, string> = {};
  if (filterModule) params.module   = filterModule;
  if (filterSev)    params.severity = filterSev;
  if (search)       params.search   = search;

  const { data: logsRaw = [] }         = useAuditLogs(Object.keys(params).length ? params : undefined);
  const { data: exportsRaw = [] }      = useAuditExportRequests();
  const { data: aiRecsRaw = [] }       = useAuditAiRecs();

  const logs    = logsRaw    as AnyRecord[];
  const exports_= exportsRaw as AnyRecord[];
  const aiRecs  = aiRecsRaw  as AnyRecord[];

  const createExport = useCreateAuditExport();

  return (
    <div className="space-y-6 pb-10">
      {/* ── Hero ── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Shield className="h-3 w-3" /> Compliance
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Immutable system activity record</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">Audit Logs</h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">Immutable record of all system actions across all modules</p>
            </div>
            <div className="flex items-center gap-2">
              <button
                className="fh-btn-primary cursor-pointer flex items-center gap-2"
                onClick={() => setExportOpen(true)}
                disabled={!canExportReports}
                title={!canExportReports ? "You do not have permission to perform this action." : undefined}
              >
                <Download className="h-4 w-4" /> Export Audit Log
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* Compliance disclaimer */}
      <div className="rounded-xl border border-amber-400/15 bg-amber-400/[0.04] p-3">
        <div className="flex items-start gap-2">
          <Shield className="h-4 w-4 shrink-0 text-amber-500 mt-0.5" />
          <p className="text-xs text-amber-700">
            OpsTrax audit logs provide an operational record of system activity for internal compliance and review purposes.
            They do not constitute a legally certified audit trail. For regulatory submissions, consult your compliance officer.
          </p>
        </div>
      </div>

      {/* ── Ops intelligence bar ── */}
      <div className="anim-fade-up relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
        <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-teal-500/10 blur-2xl" />
        <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-indigo-500/8 blur-2xl" />
        <div className="relative flex items-center gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-teal-400/20 to-teal-600/10 ring-1 ring-teal-400/20">
            <Sparkles className="h-5 w-5 text-teal-300" />
          </span>
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/80">Audit signal</p>
            <p className="mt-1 text-sm font-medium leading-relaxed text-slate-600">
              {logs.length > 0 ? `${logs.length} audit entries captured · ${exports_.length} export requests` : "No audit entries match current filters."}
            </p>
          </div>
        </div>
        <button className="cursor-pointer inline-flex items-center gap-2 self-start rounded-xl bg-gradient-to-r from-teal-500 to-teal-600 px-4 py-2.5 text-xs font-bold text-white shadow-lg shadow-teal-500/20 transition hover:from-teal-400 hover:to-teal-500 hover:shadow-teal-400/30 sm:self-auto" onClick={() => setExportOpen(true)} disabled={!canExportReports}>
          Request export <Download className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* ── Tab navigation ── */}
      <div className="panel p-2">
        <div className="grid grid-cols-3 gap-1.5">
          {TABS.map((t) => (
            <button
              key={t}
              onClick={() => setTab(t)}
              className={`flex flex-col items-start rounded-xl border px-3 py-2.5 text-left transition cursor-pointer ${tab === t ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60" : "border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50"}`}
            >
              <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">{t}</span>
              <span className={`mt-0.5 text-sm font-bold ${tab === t ? "text-teal-700" : "text-slate-900"}`}>{t === "Audit Trail" ? logs.length : t === "Export Requests" ? exports_.length : aiRecs.length}</span>
            </button>
          ))}
        </div>
      </div>

      {/* Audit Trail */}
      {tab === "Audit Trail" && (
        <div className="space-y-4">
          {/* Filters */}
          <div className="flex flex-wrap gap-2">
            <div className="relative flex-1 min-w-[180px]">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-500" />
              <input
                className="field w-full pl-9 text-sm"
                placeholder="Search actor, entity, action…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <div className="flex items-center gap-1.5">
              <Filter className="h-3.5 w-3.5 text-slate-500" />
              <Select className="text-sm w-44" value={filterModule} onChange={(e) => setFilterModule(e.target.value)}>
                <option value="">All modules</option>
                {MODULE_OPTIONS.map((m) => <option key={m} value={m}>{m}</option>)}
              </Select>
            </div>
            <Select className="text-sm w-44" value={filterSev} onChange={(e) => setFilterSev(e.target.value)}>
              <option value="">All severities</option>
              {SEVERITY_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
            </Select>
            {(search || filterModule || filterSev) && (
              <button className="icon-btn" onClick={() => { setSearch(""); setFilterModule(""); setFilterSev(""); }}>
                <X className="h-3.5 w-3.5" />
              </button>
            )}
          </div>

          <div className="overflow-x-auto rounded-xl border border-slate-200">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
                  {["Timestamp", "Actor", "Action", "Entity", "Module", "Severity"].map((h) => (
                    <th key={h} className="px-4 py-2.5 text-left">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {logs.map((log, i) => (
                  <tr
                    key={i}
                    className="cursor-pointer transition-colors hover:bg-slate-50"
                    onClick={() => setDrawerLog(log)}
                  >
                    <td className="px-4 py-3 text-xs text-slate-500 whitespace-nowrap">
                      {log.created_at ? new Date(String(log.created_at)).toLocaleString() : "—"}
                    </td>
                    <td className="px-4 py-3 text-slate-700">{String(log.actor_name ?? "system")}</td>
                    <td className="px-4 py-3">
                      <code className="rounded bg-teal-50 px-1.5 py-0.5 text-xs text-teal-700">{String(log.action_name ?? "")}</code>
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      {String(log.entity_name ?? "")}
                      {!!log.entity_id && <span className="ml-1 text-slate-500 text-xs">#{String(log.entity_id)}</span>}
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(log.module_key ?? "—")}</td>
                    <td className="px-4 py-3"><SeverityBadge severity={String(log.severity ?? "Info")} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
            {logs.length === 0 && (
              <div className="py-12 text-center">
                <CheckCircle2 className="mx-auto h-8 w-8 text-slate-600" />
                <p className="mt-2 text-sm text-slate-500">No audit log entries match the current filters.</p>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Export Requests */}
      {tab === "Export Requests" && (
        <div className="space-y-3">
          <div className="overflow-x-auto rounded-xl border border-slate-200">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
                  {["Requested By", "Date Range", "Format", "Status", "Requested At"].map((h) => (
                    <th key={h} className="px-4 py-2.5 text-left">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {exports_.map((e, i) => (
                  <tr key={i} className="transition-colors hover:bg-slate-50">
                    <td className="px-4 py-3 text-slate-700">{String(e.requested_by_name ?? "")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">
                      {e.date_range_start ? new Date(String(e.date_range_start)).toLocaleDateString() : "—"}
                      {" – "}
                      {e.date_range_end ? new Date(String(e.date_range_end)).toLocaleDateString() : "—"}
                    </td>
                    <td className="px-4 py-3 font-mono text-xs font-bold text-emerald-700">{String(e.export_format ?? "")}</td>
                    <td className="px-4 py-3"><ExportStatusBadge status={String(e.status ?? "")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-400">{e.requested_at ? new Date(String(e.requested_at)).toLocaleString() : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* AI Advisor */}
      {tab === "Operations Advisor" && (
        <div className="space-y-3">
          {aiRecs.map((rec, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-violet-50 border border-violet-200">
                <Bot className="h-4 w-4 text-violet-600" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="font-semibold text-slate-900">{String(rec.title ?? "")}</p>
                  <span className="rounded-full border border-violet-200 bg-violet-50 px-2 py-0.5 text-[10px] font-bold text-violet-700">Score {Number(rec.score ?? 0)}</span>
                </div>
                <p className="mt-1 text-sm text-slate-600">{String(rec.body ?? rec.description ?? "")}</p>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Log detail drawer */}
      {drawerLog && (
        <div className="fixed inset-0 z-50 flex items-center justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in" onClick={() => setDrawerLog(null)}>
          <aside className="anim-slide-right flex h-full w-full max-w-md flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur">
              <div className="flex items-center justify-between">
                <p className="section-title text-teal-700">Audit Log Detail</p>
                <button className="icon-btn cursor-pointer" onClick={() => setDrawerLog(null)}><X className="h-4 w-4" /></button>
              </div>
            </div>
            <div className="space-y-4 px-6 py-6">
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4 space-y-4 text-sm">
              {Object.entries(drawerLog).map(([k, v]) => (
                <div key={k} className="flex items-start justify-between gap-3 rounded-xl border border-slate-100 bg-white px-4 py-2.5">
                  <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500 mt-0.5">{k.replace(/_/g, " ")}</p>
                  <p className="text-right text-sm font-medium text-slate-800 break-all">{v == null ? "—" : typeof v === "object" ? JSON.stringify(v, null, 2) : String(v)}</p>
                </div>
              ))}
              </div>
            </div>
          </aside>
        </div>
      )}

      {/* Export request modal */}
      {exportOpen && (
        <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in">
          <div className="panel max-h-[90vh] w-full max-w-md overflow-y-auto p-6 shadow-2xl">
            <div className="flex items-center justify-between border-b border-slate-200 pb-4">
              <h2 className="text-2xl font-bold text-slate-900">Export Audit Log</h2>
              <button className="icon-btn cursor-pointer" onClick={() => setExportOpen(false)}><X className="h-4 w-4" /></button>
            </div>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                const fd = new FormData(e.currentTarget);
                createExport.mutate({
                  requestedByName: "Admin",
                  dateRangeStart: String(fd.get("start") ?? ""),
                  dateRangeEnd: String(fd.get("end") ?? ""),
                  exportFormat: String(fd.get("format") ?? "CSV"),
                });
                setExportOpen(false);
              }}
              className="space-y-3"
            >
              <div className="grid grid-cols-2 gap-3">
                <div><label className="label">From</label><input name="start" type="date" className="field w-full" /></div>
                <div><label className="label">To</label><input name="end" type="date" className="field w-full" /></div>
              </div>
              <div><label className="label">Format</label>
                <Select name="format" className="w-full">
                  {["CSV", "PDF", "Excel", "JSON"].map((f) => <option key={f}>{f}</option>)}
                </Select>
              </div>
              <div className="mt-6 flex gap-2 border-t border-slate-200 pt-4">
                <button type="button" className="fh-btn-ghost flex-1 cursor-pointer" onClick={() => setExportOpen(false)}>Cancel</button>
                <button type="submit" className="fh-btn-primary flex-1 cursor-pointer" disabled={!canExportReports}>
                  Request Export
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
