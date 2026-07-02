import { useState, useMemo } from "react";
import {
  Download, Play, Plus, RefreshCw, Trash2, BookOpen,
  Eye, EyeOff, Users, Clock, Filter, SortAsc, Save, Calendar,
  X, ChevronDown, ChevronRight,
} from "lucide-react";
import { LoadingState, EmptyState } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import {
  useDatasets,
  useSavedReports,
  useCreateSavedReport,
  useUpdateSavedReport,
  useDeleteSavedReport,
  useRunQuery,
  useExportCsv,
  useExportSavedReportCsv,
  useCreateScheduledReport,
} from "@/hooks/useReporting";
import type {
  ReportDatasetMeta,
  P8Filter,
  P8Sort,
  P8QueryBody,
  SavedReport,
  SavedReportBody,
  ReportVisibility,
} from "@/services/reportingApi";

type Tab = "builder" | "saved";

const OPERATOR_LABELS: Record<string, string> = {
  equals: "=", not_equals: "≠", contains: "contains", starts_with: "starts with",
  greater_than: ">", less_than: "<", in: "in (list)", date_range: "between",
  number_range: "between", boolean: "is", is_empty: "is empty", is_not_empty: "is not empty",
};

function VisibilityIcon({ v }: { v: ReportVisibility }) {
  if (v === "private") return <EyeOff className="h-3.5 w-3.5 text-slate-400" />;
  if (v === "role_shared") return <Users className="h-3.5 w-3.5 text-blue-500" />;
  return <Eye className="h-3.5 w-3.5 text-emerald-500" />;
}

function VisibilityLabel({ v }: { v: ReportVisibility }) {
  const map: Record<ReportVisibility, string> = {
    private: "Private", role_shared: "Role", tenant_shared: "Org",
  };
  return <span className="text-xs text-slate-500">{map[v]}</span>;
}

// ── Filter Builder Row ────────────────────────────────────────────────────────

function FilterRow({
  filter, dataset, onUpdate, onRemove,
}: {
  filter: P8Filter;
  dataset: ReportDatasetMeta;
  onUpdate: (f: P8Filter) => void;
  onRemove: () => void;
}) {
  const field = dataset.fields.find((f) => f.key === filter.field);
  const operators = field?.allowedOperators ?? [];
  return (
    <div className="flex items-center gap-2 flex-wrap">
      <select
        title="Filter field"
        value={filter.field}
        onChange={(e) => onUpdate({ ...filter, field: e.target.value, operator: "equals", value: "" })}
        className="input-sm"
      >
        <option value="">— field —</option>
        {dataset.fields.map((f) => (
          <option key={f.key} value={f.key}>{f.label}</option>
        ))}
      </select>
      <select
        title="Filter operator"
        value={filter.operator}
        onChange={(e) => onUpdate({ ...filter, operator: e.target.value })}
        className="input-sm"
        disabled={!filter.field}
      >
        {operators.map((op) => (
          <option key={op} value={op}>{OPERATOR_LABELS[op] ?? op}</option>
        ))}
      </select>
      {filter.operator !== "is_empty" && filter.operator !== "is_not_empty" && (
        <input
          className="input-sm flex-1 min-w-24"
          placeholder="value"
          value={filter.value}
          onChange={(e) => onUpdate({ ...filter, value: e.target.value })}
        />
      )}
      <button type="button" title="Remove filter" onClick={onRemove} className="p-1 text-slate-400 hover:text-red-500">
        <X className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}

// ── Save Report Modal ─────────────────────────────────────────────────────────

function SaveModal({
  dataset, fields, filters, sort,
  initial, onSave, onClose,
}: {
  dataset: ReportDatasetMeta;
  fields: string[];
  filters: P8Filter[];
  sort: P8Sort | null;
  initial?: SavedReport;
  onSave: (body: SavedReportBody) => void;
  onClose: () => void;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [visibility, setVisibility] = useState<ReportVisibility>(initial?.visibility ?? "private");
  const [sharedRole, setSharedRole] = useState(initial?.sharedRole ?? "");

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-2xl">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-base font-semibold text-slate-800">Save Report</h3>
          <button type="button" title="Close" onClick={onClose}><X className="h-4 w-4 text-slate-400" /></button>
        </div>
        <div className="space-y-3">
          <div>
            <label className="label-xs">Report Name *</label>
            <input className="input w-full" value={name} onChange={(e) => setName(e.target.value)} placeholder="My Fleet Report" />
          </div>
          <div>
            <label className="label-xs">Description</label>
            <input className="input w-full" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Optional description" />
          </div>
          <div>
            <label className="label-xs">Visibility</label>
            <select title="Visibility" className="input w-full" value={visibility} onChange={(e) => setVisibility(e.target.value as ReportVisibility)}>
              <option value="private">Private — only me</option>
              <option value="role_shared">Role — users with same role</option>
              <option value="tenant_shared">Organization — all users with reports:view</option>
            </select>
          </div>
          {visibility === "role_shared" && (
            <div>
              <label className="label-xs">Shared With Role</label>
              <input className="input w-full" value={sharedRole} onChange={(e) => setSharedRole(e.target.value)} placeholder="e.g. Fleet Manager" />
            </div>
          )}
          <p className="text-[11px] text-slate-500">
            Dataset: <strong>{dataset.label}</strong> · {fields.length} field{fields.length !== 1 ? "s" : ""} · {filters.length} filter{filters.length !== 1 ? "s" : ""}
          </p>
        </div>
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-sm btn-ghost" onClick={onClose}>Cancel</button>
          <button
            type="button"
            className="btn-sm btn-primary"
            disabled={!name.trim()}
            onClick={() => onSave({ name, description, datasetKey: dataset.key, fields, filters, sort: sort ?? undefined, visibility, sharedRole: sharedRole || undefined })}
          >
            <Save className="h-3.5 w-3.5 mr-1" />Save
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Schedule Modal ────────────────────────────────────────────────────────────

function ScheduleModal({ savedReportId, onClose }: { savedReportId: number; onClose: () => void }) {
  const [schedule, setSchedule] = useState<"daily" | "weekly" | "monthly">("weekly");
  const [format, setFormat] = useState<"csv" | "xlsx" | "pdf">("csv");
  const [recipientType, setRecipientType] = useState<"roles" | "users">("roles");
  const [recipients, setRecipients] = useState("");
  const create = useCreateScheduledReport();

  function submit() {
    if (!recipients.trim()) return;
    create.mutate(
      { savedReportId, schedule, format, recipientType, recipients: recipients.trim() },
      { onSuccess: onClose },
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-2xl">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-base font-semibold text-slate-800">Schedule Report</h3>
          <button type="button" title="Close" onClick={onClose}><X className="h-4 w-4 text-slate-400" /></button>
        </div>
        <div className="space-y-3">
          <div>
            <label className="label-xs">Frequency</label>
            <select title="Schedule frequency" className="input w-full" value={schedule} onChange={(e) => setSchedule(e.target.value as typeof schedule)}>
              <option value="daily">Daily</option>
              <option value="weekly">Weekly</option>
              <option value="monthly">Monthly</option>
            </select>
          </div>
          <div>
            <label className="label-xs">Format</label>
            <select title="Export format" className="input w-full" value={format} onChange={(e) => setFormat(e.target.value as typeof format)}>
              <option value="csv">CSV</option>
              <option value="xlsx">XLSX</option>
              <option value="pdf">PDF</option>
            </select>
          </div>
          <div>
            <label className="label-xs">Recipient Type</label>
            <select title="Recipient type" className="input w-full" value={recipientType} onChange={(e) => setRecipientType(e.target.value as typeof recipientType)}>
              <option value="roles">Roles</option>
              <option value="users">Users</option>
            </select>
          </div>
          <div>
            <label className="label-xs">{recipientType === "roles" ? "Roles" : "Usernames"} (comma-separated)</label>
            <input className="input w-full" value={recipients} onChange={(e) => setRecipients(e.target.value)} placeholder={recipientType === "roles" ? "Fleet Manager, Safety Manager" : "alice, bob"} />
          </div>
          <p className="text-[11px] text-slate-400">Delivery via in-app notification. Recipients are resolved server-side.</p>
        </div>
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-sm btn-ghost" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-sm btn-primary" disabled={!recipients.trim() || create.isPending} onClick={submit}>
            <Calendar className="h-3.5 w-3.5 mr-1" />
            {create.isPending ? "Scheduling…" : "Schedule"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Results Table ─────────────────────────────────────────────────────────────

function ResultsTable({ rows, fields, dataset }: { rows: Record<string, unknown>[]; fields: string[]; dataset: ReportDatasetMeta }) {
  if (rows.length === 0)
    return <p className="py-8 text-center text-sm text-slate-500">No rows returned.</p>;

  const headers = fields.map((k) => dataset.fields.find((f) => f.key === k)?.label ?? k);
  const keys = fields.map((k) => {
    // Convert snake_case field key to camelCase to match API response
    return k.replace(/_([a-z])/g, (_, c) => c.toUpperCase());
  });

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b border-slate-200">
            {headers.map((h, i) => (
              <th key={i} className="px-3 py-2 text-left text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, ri) => (
            <tr key={ri} className="border-b border-slate-100 hover:bg-slate-50">
              {keys.map((k, ki) => (
                <td key={ki} className="px-3 py-2 text-slate-700">{String(row[k] ?? "")}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Dataset Builder (main tab) ─────────────────────────────────────────────────

function DatasetBuilder({ datasets }: { datasets: ReportDatasetMeta[] }) {
  const hasPermission = useHasPermission();
  const canExport     = hasPermission("reports:export");

  const [selectedDataset, setSelectedDataset] = useState<string>("");
  const [selectedFields,  setSelectedFields]  = useState<string[]>([]);
  const [filters,         setFilters]         = useState<P8Filter[]>([]);
  const [sort,            setSort]            = useState<P8Sort | null>(null);
  const [page,            setPage]            = useState(1);
  const [saveOpen,        setSaveOpen]        = useState(false);
  const [autoRun,         setAutoRun]         = useState(false);

  const dataset = datasets.find((d) => d.key === selectedDataset);

  const queryBody: P8QueryBody | null = useMemo(() => {
    if (!dataset || selectedFields.length === 0) return null;
    return {
      datasetKey: selectedDataset,
      fields: selectedFields,
      filters: filters.filter((f) => f.field && f.operator),
      sort: sort ?? undefined,
      page,
      pageSize: 50,
    };
  }, [selectedDataset, selectedFields, filters, sort, page, dataset]);

  const [runBody, setRunBody] = useState<P8QueryBody | null>(null);
  const queryResult = useRunQuery(runBody);
  const exportCsv   = useExportCsv();
  const createSaved = useCreateSavedReport();

  function toggleField(key: string) {
    setSelectedFields((prev) =>
      prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key],
    );
  }

  function addFilter() {
    if (!dataset) return;
    const firstField = dataset.fields[0];
    setFilters((prev) => [...prev, { field: firstField.key, operator: firstField.allowedOperators[0] ?? "equals", value: "" }]);
  }

  function handleDatasetChange(key: string) {
    setSelectedDataset(key);
    setSelectedFields([]);
    setFilters([]);
    setSort(null);
    setRunBody(null);
    setPage(1);
  }

  const rows    = (queryResult.data?.rows ?? []) as Record<string, unknown>[];
  const meta    = queryResult.data?.meta;
  const totalPages = meta ? Math.ceil(meta.total / 50) : 1;

  return (
    <div className="space-y-4">
      {/* Dataset selector */}
      <div className="panel p-4">
        <label className="label-xs mb-1">Dataset</label>
        <select
          title="Select dataset"
          className="input w-full max-w-sm"
          value={selectedDataset}
          onChange={(e) => handleDatasetChange(e.target.value)}
        >
          <option value="">— choose a dataset —</option>
          {datasets.map((d) => (
            <option key={d.key} value={d.key}>{d.label}</option>
          ))}
        </select>
      </div>

      {dataset && (
        <>
          {/* Field picker */}
          <div className="panel p-4">
            <div className="flex items-center justify-between mb-3">
              <h3 className="text-sm font-semibold text-slate-700">Fields ({selectedFields.length} selected)</h3>
              <div className="flex gap-2">
                <button type="button" className="btn-xs btn-ghost" onClick={() => setSelectedFields(dataset.fields.map((f) => f.key))}>All</button>
                <button type="button" className="btn-xs btn-ghost" onClick={() => setSelectedFields([])}>None</button>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              {dataset.fields.map((f) => (
                <button
                  type="button"
                  key={f.key}
                  onClick={() => toggleField(f.key)}
                  className={`inline-flex items-center gap-1 rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                    selectedFields.includes(f.key)
                      ? "border-blue-500 bg-blue-50 text-blue-700"
                      : "border-slate-200 bg-slate-50 text-slate-600 hover:border-blue-300"
                  }`}
                >
                  {f.sensitive && <EyeOff className="h-2.5 w-2.5 text-amber-500" />}
                  {f.label}
                </button>
              ))}
            </div>
          </div>

          {/* Filters */}
          <div className="panel p-4">
            <div className="flex items-center justify-between mb-3">
              <h3 className="text-sm font-semibold text-slate-700 flex items-center gap-1.5">
                <Filter className="h-3.5 w-3.5" />Filters
              </h3>
              <button type="button" className="btn-xs btn-ghost" onClick={addFilter} disabled={filters.length >= 10}>
                <Plus className="h-3.5 w-3.5 mr-0.5" />Add
              </button>
            </div>
            {filters.length === 0 && (
              <p className="text-xs text-slate-400">No filters — all rows will be returned (up to page limit).</p>
            )}
            <div className="space-y-2">
              {filters.map((f, i) => (
                <FilterRow
                  key={i}
                  filter={f}
                  dataset={dataset}
                  onUpdate={(updated) => setFilters((prev) => prev.map((x, j) => j === i ? updated : x))}
                  onRemove={() => setFilters((prev) => prev.filter((_, j) => j !== i))}
                />
              ))}
            </div>
          </div>

          {/* Sort */}
          <div className="panel p-4">
            <h3 className="text-sm font-semibold text-slate-700 flex items-center gap-1.5 mb-3">
              <SortAsc className="h-3.5 w-3.5" />Sort
            </h3>
            <div className="flex gap-2 flex-wrap">
              <select
                title="Sort field"
                className="input-sm"
                value={sort?.field ?? ""}
                onChange={(e) => setSort(e.target.value ? { field: e.target.value, direction: sort?.direction ?? "asc" } : null)}
              >
                <option value="">— no sort —</option>
                {dataset.fields.filter((f) => f.sortable !== false).map((f) => (
                  <option key={f.key} value={f.key}>{f.label}</option>
                ))}
              </select>
              {sort && (
                <select title="Sort direction" className="input-sm" value={sort.direction} onChange={(e) => setSort({ ...sort, direction: e.target.value as "asc" | "desc" })}>
                  <option value="asc">Ascending</option>
                  <option value="desc">Descending</option>
                </select>
              )}
            </div>
          </div>

          {/* Actions */}
          <div className="flex items-center gap-2 flex-wrap">
            <button
              type="button"
              className="btn-sm btn-primary"
              disabled={selectedFields.length === 0 || queryResult.isFetching}
              onClick={() => { setPage(1); setRunBody({ ...queryBody!, page: 1 }); }}
            >
              <Play className="h-3.5 w-3.5 mr-1" />
              {queryResult.isFetching ? "Running…" : "Run Report"}
            </button>
            {canExport && (
              <button
                type="button"
                className="btn-sm btn-ghost"
                disabled={selectedFields.length === 0 || exportCsv.isPending}
                onClick={() => queryBody && exportCsv.mutate(queryBody)}
              >
                <Download className="h-3.5 w-3.5 mr-1" />
                {exportCsv.isPending ? "Exporting…" : "Export CSV"}
              </button>
            )}
            <button
              type="button"
              className="btn-sm btn-ghost"
              disabled={selectedFields.length === 0}
              onClick={() => setSaveOpen(true)}
            >
              <Save className="h-3.5 w-3.5 mr-1" />Save
            </button>
            <button type="button" className="btn-sm btn-ghost" onClick={() => { setRunBody(null); setSelectedFields([]); setFilters([]); setSort(null); }}>
              <RefreshCw className="h-3.5 w-3.5 mr-1" />Reset
            </button>
          </div>

          {/* Error */}
          {queryResult.isError && (
            <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              {(queryResult.error as Error)?.message ?? "Query failed. Check fields and filters."}
            </div>
          )}

          {/* Results */}
          {runBody && !queryResult.isError && (
            <div className="panel p-4">
              <div className="flex items-center justify-between mb-3">
                <h3 className="text-sm font-semibold text-slate-700">
                  Results
                  {meta && <span className="ml-2 text-xs text-slate-500 font-normal">({meta.total.toLocaleString()} total · {meta.executionMs}ms)</span>}
                </h3>
                {meta && totalPages > 1 && (
                  <div className="flex items-center gap-2 text-xs">
                    <button type="button" className="btn-xs btn-ghost" disabled={page <= 1} onClick={() => { setPage((p) => p - 1); setRunBody({ ...runBody, page: page - 1 }); }}>Prev</button>
                    <span className="text-slate-500">{page}/{totalPages}</span>
                    <button type="button" className="btn-xs btn-ghost" disabled={page >= totalPages} onClick={() => { setPage((p) => p + 1); setRunBody({ ...runBody, page: page + 1 }); }}>Next</button>
                  </div>
                )}
              </div>
              {queryResult.isFetching
                ? <LoadingState />
                : <ResultsTable rows={rows} fields={selectedFields} dataset={dataset} />}
            </div>
          )}
        </>
      )}

      {saveOpen && dataset && (
        <SaveModal
          dataset={dataset}
          fields={selectedFields}
          filters={filters}
          sort={sort}
          onClose={() => setSaveOpen(false)}
          onSave={(body) => {
            createSaved.mutate(body, { onSuccess: () => setSaveOpen(false) });
          }}
        />
      )}
    </div>
  );
}

// ── Saved Reports List ────────────────────────────────────────────────────────

function SavedReportsList({ datasets }: { datasets: ReportDatasetMeta[] }) {
  const hasPermission = useHasPermission();
  const canExport     = hasPermission("reports:export");
  const savedQ        = useSavedReports();
  const deleteMut     = useDeleteSavedReport();
  const exportMut     = useExportSavedReportCsv();
  const [schedId, setSchedId] = useState<number | null>(null);
  const [expanded, setExpanded] = useState<number | null>(null);

  const reports = (savedQ.data ?? []) as SavedReport[];

  if (savedQ.isLoading) return <LoadingState />;
  if (reports.length === 0)
    return <EmptyState title="No saved reports" subtitle="Use the builder to create and save a report." />;

  return (
    <div className="space-y-2">
      {reports.map((r) => {
        const ds = datasets.find((d) => d.key === r.datasetKey);
        const fields = JSON.parse(r.selectedFieldsJson ?? "[]") as string[];
        const isOpen = expanded === r.id;
        return (
          <div key={r.id} className="panel p-4">
            <div className="flex items-start justify-between gap-3">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-0.5 flex-wrap">
                  <VisibilityIcon v={r.visibility} />
                  <span className="font-semibold text-sm text-slate-800 truncate">{r.name}</span>
                  <VisibilityLabel v={r.visibility} />
                  {ds && <span className="text-[10px] text-slate-400 border border-slate-200 rounded px-1.5 py-0.5">{ds.label}</span>}
                </div>
                {r.description && <p className="text-xs text-slate-500 mb-1">{r.description}</p>}
                <div className="flex items-center gap-3 text-[11px] text-slate-400">
                  <span>{fields.length} field{fields.length !== 1 ? "s" : ""}</span>
                  {r.lastRunAt && <span className="flex items-center gap-0.5"><Clock className="h-3 w-3" />{new Date(r.lastRunAt).toLocaleDateString()}</span>}
                </div>
              </div>
              <div className="flex items-center gap-1 shrink-0">
                <button
                  type="button"
                  className="btn-xs btn-ghost"
                  title="Expand details"
                  onClick={() => setExpanded(isOpen ? null : r.id)}
                >
                  {isOpen ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
                </button>
                {canExport && (
                  <button
                    type="button"
                    className="btn-xs btn-ghost"
                    title="Export CSV"
                    disabled={exportMut.isPending}
                    onClick={() => exportMut.mutate(r.id)}
                  >
                    <Download className="h-3.5 w-3.5" />
                  </button>
                )}
                <button
                  type="button"
                  className="btn-xs btn-ghost"
                  title="Schedule"
                  onClick={() => setSchedId(r.id)}
                >
                  <Calendar className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  className="btn-xs btn-ghost text-red-400 hover:text-red-600"
                  title="Delete"
                  onClick={() => { if (confirm("Delete this saved report?")) deleteMut.mutate(r.id); }}
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
            {isOpen && ds && (
              <div className="mt-3 pt-3 border-t border-slate-100">
                <p className="text-xs font-semibold text-slate-600 mb-1">Fields:</p>
                <div className="flex flex-wrap gap-1">
                  {fields.map((k) => {
                    const fd = ds.fields.find((f) => f.key === k);
                    return <span key={k} className="text-[10px] bg-slate-100 text-slate-600 rounded px-2 py-0.5">{fd?.label ?? k}</span>;
                  })}
                </div>
              </div>
            )}
          </div>
        );
      })}
      {schedId && <ScheduleModal savedReportId={schedId} onClose={() => setSchedId(null)} />}
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function ReportsPage() {
  const [tab, setTab] = useState<Tab>("builder");
  const datasetsQ = useDatasets();
  const datasets  = datasetsQ.data ?? [];

  if (datasetsQ.isLoading) return <LoadingState />;

  return (
    <div className="flex h-full flex-col gap-4 overflow-y-auto">
      {/* Header */}
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-800">Reports</h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Build, save, and export reports from live fleet data. All queries are validated server-side against a whitelisted dataset registry.
          </p>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-slate-200">
        {(["builder", "saved"] as Tab[]).map((t) => (
          <button
            type="button"
            key={t}
            onClick={() => setTab(t)}
            className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
              tab === t ? "border-blue-500 text-blue-600" : "border-transparent text-slate-500 hover:text-slate-700"
            }`}
          >
            {t === "builder" ? (
              <><BookOpen className="inline h-3.5 w-3.5 mr-1.5" />Report Builder</>
            ) : (
              <><Save className="inline h-3.5 w-3.5 mr-1.5" />Saved Reports</>
            )}
          </button>
        ))}
      </div>

      {datasetsQ.isError && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          Unable to load datasets. You may not have the required permissions.
        </div>
      )}

      {tab === "builder" && <DatasetBuilder datasets={datasets} />}
      {tab === "saved"   && <SavedReportsList datasets={datasets} />}
    </div>
  );
}
