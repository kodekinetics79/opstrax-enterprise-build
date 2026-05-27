import { useState } from "react";
import {
  Bot, CheckCircle2, Download, Filter, Search, Shield, X,
} from "lucide-react";
import {
  useAuditLogs, useAuditExportRequests, useCreateAuditExport, useAuditAiRecs,
} from "@/hooks/useBatch7";

type AnyRecord = Record<string, unknown>;

const TABS = ["Audit Trail", "Export Requests", "AI Advisor"] as const;
type Tab = typeof TABS[number];

const MODULE_OPTIONS = [
  "command-center", "control-tower", "dispatch", "fleet", "compliance", "hos-eld",
  "safety", "maintenance", "finance", "reports-analytics", "sla-kpi", "executive",
];

const SEVERITY_OPTIONS = ["Info", "Warning", "High", "Critical"];

const SEVERITY_COLOR: Record<string, string> = {
  "Info":     "border-sky-400/20 bg-sky-400/10 text-sky-300",
  "Warning":  "border-amber-400/20 bg-amber-400/10 text-amber-300",
  "High":     "border-orange-400/20 bg-orange-400/10 text-orange-300",
  "Critical": "border-red-400/20 bg-red-400/10 text-red-300",
};

function SeverityBadge({ severity }: { severity: string }) {
  const cls = SEVERITY_COLOR[severity] ?? "border-slate-400/20 bg-slate-400/10 text-slate-400";
  return <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>{severity}</span>;
}

function ExportStatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Pending:   "border-amber-400/20 bg-amber-400/10 text-amber-300",
    Processing:"border-sky-400/20 bg-sky-400/10 text-sky-300",
    Completed: "border-emerald-400/20 bg-emerald-400/10 text-emerald-300",
    Failed:    "border-red-400/20 bg-red-400/10 text-red-300",
  };
  const cls = map[status] ?? "border-slate-400/20 bg-slate-400/10 text-slate-400";
  return <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>{status}</span>;
}

export function AuditLogsPage() {
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
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-white">Audit Logs</h1>
          <p className="mt-0.5 text-sm text-slate-400">Immutable record of all system actions across all modules</p>
        </div>
        <button className="btn-primary flex items-center gap-2" onClick={() => setExportOpen(true)}>
          <Download className="h-4 w-4" /> Export Audit Log
        </button>
      </div>

      {/* Compliance disclaimer */}
      <div className="rounded-xl border border-amber-400/15 bg-amber-400/[0.04] p-3">
        <div className="flex items-start gap-2">
          <Shield className="h-4 w-4 flex-shrink-0 text-amber-300 mt-0.5" />
          <p className="text-xs text-amber-200/80">
            OpsTrax audit logs provide an operational record of system activity for internal compliance and review purposes.
            They do not constitute a legally certified audit trail. For regulatory submissions, consult your compliance officer.
          </p>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-white/[0.07] pb-px">
        {TABS.map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`rounded-t-lg px-4 py-2 text-sm font-semibold transition ${tab === t ? "bg-teal-400/10 text-teal-300 border border-b-0 border-teal-400/20" : "text-slate-400 hover:text-slate-200"}`}
          >
            {t}
          </button>
        ))}
      </div>

      {/* Audit Trail */}
      {tab === "Audit Trail" && (
        <div className="space-y-4">
          {/* Filters */}
          <div className="flex flex-wrap gap-2">
            <div className="relative flex-1 min-w-[180px]">
              <Search className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-500" />
              <input
                className="field w-full pl-9 text-sm"
                placeholder="Search actor, entity, action…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <div className="flex items-center gap-1.5">
              <Filter className="h-3.5 w-3.5 text-slate-500" />
              <select className="field text-sm" value={filterModule} onChange={(e) => setFilterModule(e.target.value)}>
                <option value="">All modules</option>
                {MODULE_OPTIONS.map((m) => <option key={m} value={m}>{m}</option>)}
              </select>
            </div>
            <select className="field text-sm" value={filterSev} onChange={(e) => setFilterSev(e.target.value)}>
              <option value="">All severities</option>
              {SEVERITY_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
            {(search || filterModule || filterSev) && (
              <button className="icon-btn" onClick={() => { setSearch(""); setFilterModule(""); setFilterSev(""); }}>
                <X className="h-3.5 w-3.5" />
              </button>
            )}
          </div>

          <div className="panel overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-white/[0.07]">
                  {["Timestamp", "Actor", "Action", "Entity", "Module", "Severity"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-white/[0.04]">
                {logs.map((log, i) => (
                  <tr
                    key={i}
                    className="cursor-pointer transition hover:bg-white/[0.02]"
                    onClick={() => setDrawerLog(log)}
                  >
                    <td className="px-4 py-3 text-xs text-slate-400 whitespace-nowrap">
                      {log.created_at ? new Date(String(log.created_at)).toLocaleString() : "—"}
                    </td>
                    <td className="px-4 py-3 text-slate-300">{String(log.actor_name ?? "system")}</td>
                    <td className="px-4 py-3">
                      <code className="rounded bg-white/[0.04] px-1.5 py-0.5 text-xs text-teal-300">{String(log.action_name ?? "")}</code>
                    </td>
                    <td className="px-4 py-3 text-slate-300">
                      {String(log.entity_name ?? "")}
                      {!!log.entity_id && <span className="ml-1 text-slate-500 text-xs">#{String(log.entity_id)}</span>}
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-400">{String(log.module_key ?? "—")}</td>
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
          <div className="panel overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-white/[0.07]">
                  {["Requested By", "Date Range", "Format", "Status", "Requested At"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-white/[0.04]">
                {exports_.map((e, i) => (
                  <tr key={i} className="transition hover:bg-white/[0.02]">
                    <td className="px-4 py-3 text-slate-300">{String(e.requested_by_name ?? "")}</td>
                    <td className="px-4 py-3 text-xs text-slate-400">
                      {e.date_range_start ? new Date(String(e.date_range_start)).toLocaleDateString() : "—"}
                      {" – "}
                      {e.date_range_end ? new Date(String(e.date_range_end)).toLocaleDateString() : "—"}
                    </td>
                    <td className="px-4 py-3 font-mono text-xs font-bold text-emerald-300">{String(e.export_format ?? "")}</td>
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
      {tab === "AI Advisor" && (
        <div className="space-y-3">
          {aiRecs.map((rec, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-violet-400/10 border border-violet-400/20">
                <Bot className="h-4 w-4 text-violet-300" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="font-semibold text-white">{String(rec.title ?? "")}</p>
                  <span className="rounded-full border border-violet-400/20 bg-violet-400/10 px-2 py-0.5 text-[10px] font-bold text-violet-300">Score {Number(rec.score ?? 0)}</span>
                </div>
                <p className="mt-1 text-sm text-slate-400">{String(rec.body ?? rec.description ?? "")}</p>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Log detail drawer */}
      {drawerLog && (
        <div className="fixed inset-0 z-50 flex items-center justify-end bg-black/50 backdrop-blur-sm" onClick={() => setDrawerLog(null)}>
          <div className="h-full w-full max-w-md overflow-y-auto border-s border-white/[0.09] bg-slate-950 p-6 shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between mb-6">
              <h2 className="font-bold text-white">Audit Log Detail</h2>
              <button className="icon-btn" onClick={() => setDrawerLog(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-4 text-sm">
              {Object.entries(drawerLog).map(([k, v]) => (
                <div key={k}>
                  <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">{k.replace(/_/g, " ")}</p>
                  <p className="mt-0.5 break-all text-slate-200">{v == null ? "—" : typeof v === "object" ? JSON.stringify(v, null, 2) : String(v)}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* Export request modal */}
      {exportOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="panel w-full max-w-md p-6 space-y-4">
            <div className="flex items-center justify-between">
              <h2 className="font-bold text-white">Export Audit Log</h2>
              <button className="icon-btn" onClick={() => setExportOpen(false)}><X className="h-4 w-4" /></button>
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
                <select name="format" className="field w-full">
                  {["CSV", "PDF", "Excel", "JSON"].map((f) => <option key={f}>{f}</option>)}
                </select>
              </div>
              <div className="flex gap-2 pt-2">
                <button type="button" className="btn-ghost flex-1" onClick={() => setExportOpen(false)}>Cancel</button>
                <button type="submit" className="btn-primary flex-1">Request Export</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
