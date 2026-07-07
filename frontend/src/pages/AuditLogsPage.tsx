import { useMemo, useState } from "react";
import {
  Activity, Bot, CheckCircle2, Download, FileDown, Filter, Layers,
  Search, Shield, Users, X,
} from "lucide-react";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasPermission } from "@/hooks/usePermission";
import { ClayCard, KpiCard } from "@/components/ui";
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

  // ---- Derived analytics (computed purely from data already fetched) ----
  const stats = useMemo(() => {
    const sevCounts: Record<string, number> = { Info: 0, Warning: 0, High: 0, Critical: 0 };
    const actors = new Set<string>();
    const modules = new Set<string>();
    for (const log of logs) {
      const sev = String(log.severity ?? "Info");
      sevCounts[sev] = (sevCounts[sev] ?? 0) + 1;
      const actor = String(log.actor_name ?? "system");
      if (actor) actors.add(actor);
      const mod = String(log.module_key ?? "");
      if (mod) modules.add(mod);
    }
    const criticalHigh = (sevCounts.Critical ?? 0) + (sevCounts.High ?? 0);
    const pendingExports = exports_.filter(
      (e) => /pending|processing/i.test(String(e.status ?? "")),
    ).length;
    // Module event tallies for the side rail (top modules by activity).
    const moduleTally: Record<string, number> = {};
    for (const log of logs) {
      const mod = String(log.module_key ?? "—");
      moduleTally[mod] = (moduleTally[mod] ?? 0) + 1;
    }
    const topModules = Object.entries(moduleTally)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 6);
    // Most recent entries by timestamp for the "recent activity" side panel.
    const recent = [...logs]
      .sort((a, b) => {
        const ta = a.created_at ? new Date(String(a.created_at)).getTime() : 0;
        const tb = b.created_at ? new Date(String(b.created_at)).getTime() : 0;
        return tb - ta;
      })
      .slice(0, 6);
    return {
      total: logs.length,
      sevCounts,
      criticalHigh,
      actors: actors.size,
      modules: modules.size,
      exportsTotal: exports_.length,
      pendingExports,
      topModules,
      recent,
    };
  }, [logs, exports_]);

  const maxModule = stats.topModules.length ? stats.topModules[0][1] : 0;

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900">Audit Logs</h1>
          <p className="mt-0.5 text-sm text-slate-500">Immutable record of all system actions across all modules</p>
        </div>
        <button
          className="btn-primary flex items-center gap-2"
          onClick={() => setExportOpen(true)}
          disabled={!canExportReports}
          title={!canExportReports ? "You do not have permission to perform this action." : undefined}
        >
          <Download className="h-4 w-4" /> Export Audit Log
        </button>
      </div>

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

      {/* KPI row — all values computed from data already in scope */}
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <KpiCard
          label="Total Events"
          value={stats.total.toLocaleString()}
          trend={`${stats.modules} module${stats.modules === 1 ? "" : "s"} active`}
          icon={<Activity className="h-5 w-5" />}
        />
        <KpiCard
          label="Critical & High"
          value={stats.criticalHigh.toLocaleString()}
          status={stats.criticalHigh > 0 ? "Critical" : undefined}
          trend={stats.criticalHigh > 0 ? `${stats.sevCounts.Critical ?? 0} critical` : "No high-severity events"}
          icon={<Shield className="h-5 w-5" />}
        />
        <KpiCard
          label="Export Requests"
          value={stats.exportsTotal.toLocaleString()}
          status={stats.pendingExports > 0 ? "Pending" : undefined}
          trend={stats.pendingExports > 0 ? `${stats.pendingExports} in progress` : "All resolved"}
          icon={<FileDown className="h-5 w-5" />}
        />
        <KpiCard
          label="Distinct Actors"
          value={stats.actors.toLocaleString()}
          trend={`${stats.modules} module${stats.modules === 1 ? "" : "s"} touched`}
          icon={<Users className="h-5 w-5" />}
        />
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-slate-200 pb-px">
        {TABS.map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`rounded-t-lg px-4 py-2 text-sm font-semibold transition ${tab === t ? "bg-teal-50 text-teal-700 border border-b-0 border-teal-300" : "text-slate-500 hover:text-slate-700"}`}
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

          {/* Two-column shell: log table (main) + analytics rail (side) */}
          <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
            <div className="panel overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200">
                    {["Timestamp", "Actor", "Action", "Entity", "Module", "Severity"].map((h) => (
                      <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {logs.map((log, i) => (
                    <tr
                      key={i}
                      className="cursor-pointer transition hover:bg-slate-50"
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

            {/* Analytics rail — derived entirely from in-scope data */}
            <div className="flex flex-col gap-4">
              {/* Severity breakdown */}
              <ClayCard className="p-4">
                <div className="flex items-center gap-2">
                  <Shield className="h-4 w-4 text-teal-600" />
                  <p className="text-[11px] font-black uppercase tracking-[0.16em] text-slate-500">Severity Breakdown</p>
                </div>
                <div className="mt-3 space-y-2.5">
                  {SEVERITY_OPTIONS.map((sev) => {
                    const count = stats.sevCounts[sev] ?? 0;
                    const pct = stats.total > 0 ? Math.round((count / stats.total) * 100) : 0;
                    return (
                      <div key={sev}>
                        <div className="flex items-center justify-between text-xs">
                          <span className="font-semibold text-slate-600">{sev}</span>
                          <span className="tabular-nums font-bold text-slate-800">{count}<span className="ml-1 font-medium text-slate-400">{pct}%</span></span>
                        </div>
                        <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
                          <div
                            className={`h-full rounded-full ${
                              sev === "Critical" ? "bg-red-500"
                              : sev === "High" ? "bg-orange-500"
                              : sev === "Warning" ? "bg-amber-500"
                              : "bg-sky-500"
                            }`}
                            style={{ width: `${pct}%` }}
                          />
                        </div>
                      </div>
                    );
                  })}
                </div>
              </ClayCard>

              {/* Activity by module */}
              <ClayCard className="p-4">
                <div className="flex items-center gap-2">
                  <Layers className="h-4 w-4 text-blue-600" />
                  <p className="text-[11px] font-black uppercase tracking-[0.16em] text-slate-500">Activity by Module</p>
                </div>
                {stats.topModules.length === 0 ? (
                  <p className="mt-3 text-xs text-slate-400">No module activity in view.</p>
                ) : (
                  <div className="mt-3 space-y-2.5">
                    {stats.topModules.map(([mod, count]) => {
                      const pct = maxModule > 0 ? Math.round((count / maxModule) * 100) : 0;
                      return (
                        <div key={mod}>
                          <div className="flex items-center justify-between text-xs">
                            <span className="truncate font-semibold text-slate-600">{mod}</span>
                            <span className="ml-2 shrink-0 tabular-nums font-bold text-slate-800">{count}</span>
                          </div>
                          <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
                            <div className="h-full rounded-full bg-gradient-to-r from-teal-500 to-blue-500" style={{ width: `${pct}%` }} />
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </ClayCard>

              {/* Recent activity */}
              <ClayCard className="p-4">
                <div className="flex items-center gap-2">
                  <Activity className="h-4 w-4 text-violet-600" />
                  <p className="text-[11px] font-black uppercase tracking-[0.16em] text-slate-500">Recent Activity</p>
                </div>
                {stats.recent.length === 0 ? (
                  <p className="mt-3 text-xs text-slate-400">No recent entries.</p>
                ) : (
                  <ul className="mt-3 space-y-2.5">
                    {stats.recent.map((log, i) => (
                      <li
                        key={i}
                        className="cursor-pointer rounded-lg border border-transparent p-1.5 transition hover:border-slate-200 hover:bg-slate-50"
                        onClick={() => setDrawerLog(log)}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <code className="truncate rounded bg-teal-50 px-1.5 py-0.5 text-[11px] text-teal-700">{String(log.action_name ?? "")}</code>
                          <SeverityBadge severity={String(log.severity ?? "Info")} />
                        </div>
                        <p className="mt-1 truncate text-[11px] text-slate-500">
                          {String(log.actor_name ?? "system")}
                          {" · "}
                          {log.created_at ? new Date(String(log.created_at)).toLocaleString() : "—"}
                        </p>
                      </li>
                    ))}
                  </ul>
                )}
              </ClayCard>
            </div>
          </div>
        </div>
      )}

      {/* Export Requests */}
      {tab === "Export Requests" && (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
          <div className="panel overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200">
                  {["Requested By", "Date Range", "Format", "Status", "Requested At"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {exports_.map((e, i) => (
                  <tr key={i} className="transition hover:bg-slate-50">
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
            {exports_.length === 0 && (
              <div className="py-12 text-center">
                <FileDown className="mx-auto h-8 w-8 text-slate-400" />
                <p className="mt-2 text-sm text-slate-500">No export requests yet.</p>
              </div>
            )}
          </div>

          {/* Export summary rail — derived from in-scope export data */}
          <div className="flex flex-col gap-4">
            <ClayCard className="p-4">
              <div className="flex items-center gap-2">
                <FileDown className="h-4 w-4 text-emerald-600" />
                <p className="text-[11px] font-black uppercase tracking-[0.16em] text-slate-500">Export Summary</p>
              </div>
              <div className="mt-3 grid grid-cols-2 gap-3">
                <div className="rounded-xl border border-slate-200 bg-white p-3">
                  <p className="text-[26px] font-black tabular-nums text-slate-900">{stats.exportsTotal}</p>
                  <p className="text-[11px] font-semibold text-slate-500">Total requests</p>
                </div>
                <div className="rounded-xl border border-slate-200 bg-white p-3">
                  <p className={`text-[26px] font-black tabular-nums ${stats.pendingExports > 0 ? "text-amber-600" : "text-slate-900"}`}>{stats.pendingExports}</p>
                  <p className="text-[11px] font-semibold text-slate-500">In progress</p>
                </div>
              </div>
              <button
                className="btn-primary mt-4 flex w-full items-center justify-center gap-2"
                onClick={() => setExportOpen(true)}
                disabled={!canExportReports}
                title={!canExportReports ? "You do not have permission to perform this action." : undefined}
              >
                <Download className="h-4 w-4" /> New Export Request
              </button>
            </ClayCard>

            <ClayCard className="p-4">
              <div className="flex items-center gap-2">
                <Layers className="h-4 w-4 text-blue-600" />
                <p className="text-[11px] font-black uppercase tracking-[0.16em] text-slate-500">Recent Requests</p>
              </div>
              {exports_.length === 0 ? (
                <p className="mt-3 text-xs text-slate-400">No requests to show.</p>
              ) : (
                <ul className="mt-3 space-y-2.5">
                  {exports_.slice(0, 6).map((e, i) => (
                    <li key={i} className="flex items-center justify-between gap-2">
                      <div className="min-w-0">
                        <p className="truncate text-xs font-semibold text-slate-700">{String(e.requested_by_name ?? "—")}</p>
                        <p className="truncate text-[11px] text-slate-400">
                          {String(e.export_format ?? "")}
                          {e.requested_at ? ` · ${new Date(String(e.requested_at)).toLocaleDateString()}` : ""}
                        </p>
                      </div>
                      <ExportStatusBadge status={String(e.status ?? "")} />
                    </li>
                  ))}
                </ul>
              )}
            </ClayCard>
          </div>
        </div>
      )}

      {/* AI Advisor */}
      {tab === "Operations Advisor" && (
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
          {aiRecs.map((rec, i) => (
            <ClayCard key={i} interactive className="flex items-start gap-4 p-4">
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
            </ClayCard>
          ))}
          {aiRecs.length === 0 && (
            <div className="lg:col-span-2 py-12 text-center">
              <Bot className="mx-auto h-8 w-8 text-slate-400" />
              <p className="mt-2 text-sm text-slate-500">No operations recommendations available.</p>
            </div>
          )}
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
              <h2 className="font-bold text-slate-900">Export Audit Log</h2>
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
                <button type="submit" className="btn-primary flex-1" disabled={!canExportReports}>
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
