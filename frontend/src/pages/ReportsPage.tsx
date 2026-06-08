import { useState } from "react";
import {
  Bot, Calendar, CheckCircle2, Clock, Download,
  FileText, Play, Plus, RefreshCw, X,
} from "lucide-react";
import { EmptyState, LoadingState } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import {
  useReportCatalog, useReportRuns, useReportsSummary, useReportsAiRecs,
  useRunReport, useScheduledReports, useCreateScheduledReport,
  usePauseScheduledReport, useResumeScheduledReport,
  useReportExports, useCreateReportExport,
} from "@/hooks/useBatch7";

type AnyRecord = Record<string, unknown>;

const TABS = ["Catalog", "Run History", "Scheduled", "Exports", "AI Advisor"] as const;
type Tab = typeof TABS[number];

const CATEGORY_COLOR: Record<string, string> = {
  "Fleet":       "bg-sky-400/10 text-sky-300 border-sky-400/20",
  "Safety":      "bg-red-400/10 text-red-300 border-red-400/20",
  "Compliance":  "bg-emerald-400/10 text-emerald-300 border-emerald-400/20",
  "Finance":     "bg-yellow-400/10 text-yellow-300 border-yellow-400/20",
  "Operations":  "bg-blue-400/10 text-blue-300 border-blue-400/20",
  "Executive":   "bg-violet-400/10 text-violet-300 border-violet-400/20",
};

function CategoryBadge({ cat }: { cat: string }) {
  const cls = CATEGORY_COLOR[cat] ?? "bg-slate-400/10 text-slate-300 border-slate-400/20";
  return <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>{cat}</span>;
}

function StatusDot({ status }: { status: string }) {
  const c = status === "Completed" ? "bg-emerald-400" : status === "Running" ? "bg-amber-400 animate-pulse" : status === "Failed" ? "bg-red-400" : "bg-slate-500";
  return <span className={`inline-block h-2 w-2 rounded-full ${c}`} />;
}

function ExportFormatBadge({ fmt }: { fmt: string }) {
  const map: Record<string, string> = { CSV: "text-emerald-300", PDF: "text-red-300", Excel: "text-green-300", JSON: "text-amber-300" };
  return <span className={`font-mono text-xs font-bold ${map[fmt] ?? "text-slate-400"}`}>{fmt}</span>;
}

function SummaryCard({ label, value, sub, color }: { label: string; value: string | number; sub?: string; color: string }) {
  return (
    <div className="panel p-4">
      <p className="text-xs font-bold uppercase tracking-widest text-slate-500">{label}</p>
      <p className={`mt-1 text-2xl font-extrabold ${color}`}>{value}</p>
      {sub && <p className="mt-0.5 text-xs text-slate-500">{sub}</p>}
    </div>
  );
}

export function ReportsPage() {
  const hasPermission = useHasPermission();
  const canExport = hasPermission("reports:export");
  const [tab, setTab] = useState<Tab>("Catalog");
  const [runningKey, setRunningKey] = useState<string | null>(null);
  const [schedOpen, setSchedOpen] = useState(false);
  const [filterCat, setFilterCat] = useState("");

  const catalogQ = useReportCatalog();
  const summaryQ = useReportsSummary();
  const runsQ = useReportRuns();
  const scheduledQ = useScheduledReports();
  const exportsQ = useReportExports();
  const aiQ = useReportsAiRecs();

  const catalog   = (catalogQ.data ?? []) as AnyRecord[];
  const summary   = summaryQ.data   as AnyRecord | undefined;
  const runs      = (runsQ.data ?? []) as AnyRecord[];
  const scheduled = (scheduledQ.data ?? []) as AnyRecord[];
  const exports_  = (exportsQ.data ?? []) as AnyRecord[];
  const aiRecs    = (aiQ.data ?? []) as AnyRecord[];

  const isLoading = catalogQ.isLoading || summaryQ.isLoading || runsQ.isLoading || scheduledQ.isLoading || exportsQ.isLoading || aiQ.isLoading;
  const hasError = catalogQ.isError || summaryQ.isError || runsQ.isError || scheduledQ.isError || exportsQ.isError || aiQ.isError;

  const runReport         = useRunReport();
  const pauseScheduled    = usePauseScheduledReport();
  const resumeScheduled   = useResumeScheduledReport();
  const createExport      = useCreateReportExport();
  const createScheduled   = useCreateScheduledReport();

  const categories = [...new Set(catalog.map((r) => String(r.report_category ?? "")))].filter(Boolean);
  const filtered   = filterCat ? catalog.filter((r) => String(r.report_category) === filterCat) : catalog;

  if (isLoading) return <LoadingState />;
  if (hasError) return <EmptyState title="Reports unavailable" subtitle="Unable to load the report catalog right now. Refresh to try again." />;

  function handleRun(key: string) {
    setRunningKey(key);
    runReport.mutate({ key }, { onSettled: () => setRunningKey(null) });
  }

  function handleExport(r: AnyRecord, fmt: string) {
    createExport.mutate({ reportKey: String(r.report_key ?? ""), reportName: String(r.report_name ?? ""), exportFormat: fmt });
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-white">Reports &amp; Analytics</h1>
          <p className="mt-0.5 text-sm text-slate-400">Run, schedule, and export operational reports across all modules</p>
        </div>
        <button className="btn-primary flex items-center gap-2" onClick={() => setSchedOpen(true)}>
          <Plus className="h-4 w-4" /> Schedule Report
        </button>
      </div>

      {/* Summary Cards */}
      {summary && (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
          <SummaryCard label="Active Reports" value={Number(summary.catalogCount ?? 0)} color="text-teal-300" />
          <SummaryCard label="Runs Today"     value={Number(summary.runsToday ?? 0)}    color="text-sky-300" />
          <SummaryCard label="Scheduled"      value={Number(summary.scheduled ?? 0)}    color="text-violet-300" />
          <SummaryCard label="Pending Exports" value={Number(summary.pendingExports ?? 0)} color="text-amber-300" />
        </div>
      )}

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

      {/* Catalog */}
      {tab === "Catalog" && (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <button onClick={() => setFilterCat("")} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${!filterCat ? "border-teal-400/30 bg-teal-400/10 text-teal-300" : "border-white/10 text-slate-400 hover:text-slate-200"}`}>All</button>
            {categories.map((c) => (
              <button key={c} onClick={() => setFilterCat(c === filterCat ? "" : c)} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${filterCat === c ? "border-teal-400/30 bg-teal-400/10 text-teal-300" : "border-white/10 text-slate-400 hover:text-slate-200"}`}>{c}</button>
            ))}
          </div>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {!filtered.length && <EmptyState title="No reports match this filter" subtitle="Clear the category filter to see all operational reports." />}
            {filtered.map((r) => {
              const key = String(r.report_key ?? "");
              const isRunning = runningKey === key;
              return (
                <div key={key} className="panel flex flex-col gap-3 p-4">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <FileText className="h-4 w-4 flex-shrink-0 text-teal-400" />
                        <p className="truncate font-semibold text-white">{String(r.report_name ?? "")}</p>
                      </div>
                      <p className="mt-1 line-clamp-2 text-xs text-slate-400">{String(r.description ?? "")}</p>
                    </div>
                    <CategoryBadge cat={String(r.report_category ?? "")} />
                  </div>
                  <div className="flex gap-2">
                    <button
                      className="btn-primary flex flex-1 items-center justify-center gap-1.5 py-1.5 text-xs"
                      disabled={isRunning}
                      onClick={() => handleRun(key)}
                    >
                      {isRunning ? <RefreshCw className="h-3 w-3 animate-spin" /> : <Play className="h-3 w-3" />}
                      {isRunning ? "Running…" : "Run"}
                    </button>
                    <button className="btn-ghost flex items-center gap-1 py-1.5 px-2 text-xs" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : "Export this report as CSV."} onClick={() => handleExport(r, "CSV")}>
                      <Download className="h-3 w-3" /> CSV
                    </button>
                    <button className="btn-ghost flex items-center gap-1 py-1.5 px-2 text-xs" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : "Export this report as PDF."} onClick={() => handleExport(r, "PDF")}>
                      <Download className="h-3 w-3" /> PDF
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Run History */}
      {tab === "Run History" && (
        <div className="panel overflow-x-auto">
          {!runs.length && <EmptyState title="No report runs yet" subtitle="Run a report from the catalog to populate history." />}
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-white/[0.07]">
                {["Report", "Run By", "Status", "Rows", "Started", "Duration"].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/[0.04]">
              {runs.map((r, i) => {
                const started = r.started_at ? new Date(String(r.started_at)) : null;
                const completed = r.completed_at ? new Date(String(r.completed_at)) : null;
                const duration = started && completed ? Math.round((completed.getTime() - started.getTime()) / 1000) : null;
                return (
                  <tr key={i} className="transition hover:bg-white/[0.02]">
                    <td className="px-4 py-3">
                      <p className="font-medium text-white">{String(r.report_name ?? "")}</p>
                      <p className="text-xs text-slate-500 font-mono">{String(r.report_key ?? "")}</p>
                    </td>
                    <td className="px-4 py-3 text-slate-300">{String(r.run_by_name ?? "")}</td>
                    <td className="px-4 py-3"><div className="flex items-center gap-1.5"><StatusDot status={String(r.status ?? "")} /><span className="text-slate-300">{String(r.status ?? "")}</span></div></td>
                    <td className="px-4 py-3 font-mono text-slate-300">{Number(r.row_count ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-slate-400 text-xs">{started ? started.toLocaleString() : "—"}</td>
                    <td className="px-4 py-3 text-slate-400 text-xs">{duration != null ? `${duration}s` : "—"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Scheduled */}
      {tab === "Scheduled" && (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {!scheduled.length && <EmptyState title="No scheduled reports" subtitle="Schedule a report to automate delivery." />}
          {scheduled.map((s, i) => {
            const isActive = String(s.status) === "Active";
            return (
              <div key={i} className="panel p-4 space-y-3">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <p className="font-semibold text-white">{String(s.schedule_name ?? "")}</p>
                    <p className="text-xs text-slate-400 mt-0.5">{String(s.report_name ?? "")}</p>
                  </div>
                  <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${isActive ? "border-emerald-400/20 bg-emerald-400/10 text-emerald-300" : "border-slate-500/20 bg-slate-500/10 text-slate-400"}`}>
                    {String(s.status ?? "")}
                  </span>
                </div>
                <div className="flex items-center gap-3 text-xs text-slate-400">
                  <span className="flex items-center gap-1"><Calendar className="h-3 w-3" /> {String(s.frequency ?? "")}</span>
                  {!!s.next_run_at && <span className="flex items-center gap-1"><Clock className="h-3 w-3" /> Next: {new Date(String(s.next_run_at)).toLocaleDateString()}</span>}
                </div>
                <div className="flex gap-2">
                  {isActive
                    ? <button className="btn-ghost w-full py-1.5 text-xs" onClick={() => pauseScheduled.mutate(Number(s.id))}>Pause</button>
                    : <button className="btn-primary w-full py-1.5 text-xs" onClick={() => resumeScheduled.mutate(Number(s.id))}>Resume</button>
                  }
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Exports */}
      {tab === "Exports" && (
        <div className="panel overflow-x-auto">
          {!exports_.length && <EmptyState title="No exports yet" subtitle="Export a report to create a downloadable record." />}
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-white/[0.07]">
                {["Report", "Format", "Requested By", "Status", "Requested At"].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/[0.04]">
              {exports_.map((e, i) => (
                <tr key={i} className="transition hover:bg-white/[0.02]">
                  <td className="px-4 py-3 font-medium text-white">{String(e.report_name ?? "")}</td>
                  <td className="px-4 py-3"><ExportFormatBadge fmt={String(e.export_format ?? "")} /></td>
                  <td className="px-4 py-3 text-slate-300">{String(e.run_by_name ?? "")}</td>
                  <td className="px-4 py-3"><div className="flex items-center gap-1.5"><StatusDot status={String(e.status ?? "")} /><span className="text-slate-300">{String(e.status ?? "")}</span></div></td>
                  <td className="px-4 py-3 text-slate-400 text-xs">{e.requested_at ? new Date(String(e.requested_at)).toLocaleString() : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
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
              <CheckCircle2 className="h-4 w-4 flex-shrink-0 text-slate-600 hover:text-teal-400 cursor-pointer transition" />
            </div>
          ))}
        </div>
      )}

      {/* Schedule Modal */}
      {schedOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="panel w-full max-w-md p-6 space-y-4">
            <div className="flex items-center justify-between">
              <h2 className="font-bold text-white">Schedule New Report</h2>
              <button className="icon-btn" onClick={() => setSchedOpen(false)}><X className="h-4 w-4" /></button>
            </div>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                const fd = new FormData(e.currentTarget);
                createScheduled.mutate({
                  reportKey: String(fd.get("reportKey") ?? ""),
                  reportName: String(fd.get("reportName") ?? ""),
                  scheduleName: String(fd.get("scheduleName") ?? ""),
                  frequency: String(fd.get("frequency") ?? "Weekly"),
                  recipients: [String(fd.get("recipients") ?? "")],
                });
                setSchedOpen(false);
              }}
              className="space-y-3"
            >
              <div><label className="label">Schedule Name</label><input name="scheduleName" className="field w-full" placeholder="Weekly Fleet Summary" required /></div>
              <div><label className="label">Report</label>
                <select name="reportKey" className="field w-full">
                  {catalog.map((r) => <option key={String(r.report_key)} value={String(r.report_key)}>{String(r.report_name)}</option>)}
                </select>
                <input name="reportName" type="hidden" value="" />
              </div>
              <div><label className="label">Frequency</label>
                <select name="frequency" className="field w-full">
                  {["Daily", "Weekly", "Monthly", "Quarterly"].map((f) => <option key={f}>{f}</option>)}
                </select>
              </div>
              <div><label className="label">Recipients (email)</label><input name="recipients" type="email" className="field w-full" placeholder="ops@company.com" /></div>
              <div className="flex gap-2 pt-2">
                <button type="button" className="btn-ghost flex-1" onClick={() => setSchedOpen(false)}>Cancel</button>
                <button type="submit" className="btn-primary flex-1">Create Schedule</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
