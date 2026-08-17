import { ChangeEvent, FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowRight, CheckCircle2, Download, Edit3, FileCheck2, Info, MapPin, Package, Plus,
  Search, Send, Sparkles, Trash2, TriangleAlert, Truck, Upload, UserCheck, X,
} from "lucide-react";
import { useLocation, useNavigate } from "react-router";
import {
  AiInsightCard, DataTable, EmptyState, ErrorState, KpiCard, LoadingState, PageHeader,
  RiskBadge, StatusBadge, exportCsv, labelize,
} from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { useAuth } from "@/hooks/useAuth";
import { isCustomerPortalRole, isDriverPortalRole, scopeRowsForSession } from "@/auth/accessScope";
import { useJobDetail, useJobSummary } from "@/hooks/useBatch2";
import { jobsApi } from "@/services/jobsApi";
import { downloadServerExport } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

const fields = [
  ["jobNumber", "Job Number"], ["customerId", "Customer ID"], ["jobType", "Job Type"], ["priority", "Priority"],
  ["pickupAddress", "Pickup Address"], ["dropoffAddress", "Drop-off Address"], ["scheduledStart", "Scheduled Start"],
  ["scheduledEnd", "Scheduled End"], ["slaWindowStart", "SLA Start"], ["slaWindowEnd", "SLA End"],
  ["requiredVehicleType", "Required Vehicle Type"], ["requiredDriverCertification", "Required Driver Certification"],
  ["routeId", "Route ID"], ["notes", "Notes"],
];

// Lifecycle pipeline — drives both the status filter strip and KPI distribution.
const PIPELINE: { label: string; summaryKey: string; statusValue: string }[] = [
  { label: "Unassigned", summaryKey: "unassignedJobs", statusValue: "Unassigned" },
  { label: "Assigned", summaryKey: "assignedJobs", statusValue: "Assigned" },
  { label: "En Route", summaryKey: "enRoute", statusValue: "En Route" },
  { label: "At Stop", summaryKey: "atStop", statusValue: "At Stop" },
  { label: "Completed", summaryKey: "completed", statusValue: "Completed" },
  { label: "Delayed", summaryKey: "delayedJobs", statusValue: "Delayed" },
  { label: "SLA Risk", summaryKey: "slaAtRisk", statusValue: "SLA At Risk" },
];

type Toast = { kind: "success" | "error" | "info"; message: string };
type JobImportSession = { rows: AnyRecord[]; preview: AnyRecord; idempotencyKey: string };

type ShipmentSurface =
  | "jobs"
  | "active-shipments"
  | "shipments";

const SURFACE_CONFIG: Record<ShipmentSurface, {
  eyebrow: string;
  title: string;
  description: string;
  exportName: string;
  createLabel: string;
  tableColumns: string[];
  filterRows: (row: AnyRecord) => boolean;
}> = {
  jobs: {
    eyebrow: "Jobs & Orders",
    title: "Jobs & Orders",
    description: "Create, assign, track, prove and audit every job with live SLA, proof and customer updates.",
    exportName: "jobs",
    createLabel: "Create Job",
    tableColumns: ["jobNumber", "customerName", "timeWindow", "driverName", "vehicleCode", "status", "slaStatus", "priority", "proofStatus", "recommendedAction"],
    filterRows: () => true,
  },
  "active-shipments": {
    eyebrow: "Active Shipments",
    title: "Active Shipments",
    description: "In-flight shipments only: dispatch state, ETA, proof readiness and customer updates.",
    exportName: "active-shipments",
    createLabel: "Create Shipment Job",
    tableColumns: ["jobNumber", "customerName", "timeWindow", "driverName", "vehicleCode", "status", "slaStatus", "proofStatus", "customerUpdateStatus", "recommendedAction"],
    filterRows: (row) => !/completed|delivered/i.test(String(row.status ?? "")),
  },
  shipments: {
    eyebrow: "Shipments",
    title: "Shipments",
    description: "Every shipment from assignment through delivered proof, with stops, communication and audit history.",
    exportName: "shipments",
    createLabel: "Create Shipment Job",
    tableColumns: ["jobNumber", "customerName", "trackingCode", "driverName", "vehicleCode", "status", "slaStatus", "proofStatus", "customerUpdateStatus", "recommendedAction"],
    filterRows: () => true,
  },
};

export function JobsPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [assigning, setAssigning] = useState<AnyRecord | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("All");
  const [priority, setPriority] = useState("All");
  const [toast, setToast] = useState<Toast | null>(null);
  const [importSession, setImportSession] = useState<JobImportSession | null>(null);
  const importInput = useRef<HTMLInputElement>(null);
  const focusedJobOpened = useRef<string | null>(null);
  const notify = (kind: Toast["kind"], message: string) => setToast({ kind, message });
  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 3200);
    return () => clearTimeout(t);
  }, [toast]);
  const JOBS_PAGE_SIZE = 50;
  const [jobsOffset, setJobsOffset] = useState(0);
  const location = useLocation();
  const navigate = useNavigate();
  const focusedJobId = new URLSearchParams(location.search).get("jobId");
  // Server-side paginated + searched — never fetches all 4000+ jobs at once.
  const jobsPaged = useQuery({
    queryKey: ["jobs", "paged", query.trim(), status, priority, jobsOffset, focusedJobId],
    queryFn: () => jobsApi.listPaged({ limit: JOBS_PAGE_SIZE, offset: jobsOffset, search: query, status, priority, jobId: focusedJobId ?? undefined }),
  });
  const jobs = { data: jobsPaged.data?.rows ?? [], isLoading: jobsPaged.isLoading, isError: jobsPaged.isError, isFetching: jobsPaged.isFetching, error: jobsPaged.error };
  const jobsTotal = jobsPaged.data?.total ?? (jobsPaged.data?.rows?.length ?? 0);
  const summary = useJobSummary();
  const detail = useJobDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();
  const hasPermission = useHasPermission();
  const { session } = useAuth();
  const surface = ((): ShipmentSurface => {
    if (location.pathname === "/active-shipments") return "active-shipments";
    if (location.pathname === "/shipments") return "shipments";
    return "jobs";
  })();
  const surfaceConfig = SURFACE_CONFIG[surface];
  const canManageDispatch = hasPermission("dispatch:manage");
  const canCreate = canManageDispatch || hasPermission("shipments:create") || hasPermission("dispatch:create");
  const canEdit = canManageDispatch || hasPermission("shipments:update") || hasPermission("dispatch:update");
  const canCancel = canManageDispatch || hasPermission("dispatch:cancel");
  const canDelete = canManageDispatch || hasPermission("shipments:delete") || canCancel;
  const canDispatch = canManageDispatch || hasPermission("dispatch:update");
  const canAssign = canManageDispatch || hasPermission("dispatch:assign");
  const canExport = hasPermission("shipments:export");
  const scopedRows = useMemo(() => scopeRowsForSession("jobs", jobs.data || [], session), [jobs.data, session]);
  const visibleSummary = useMemo(() => buildJobSummary(scopedRows, summary.data as AnyRecord | undefined, session), [scopedRows, session, summary.data]);

  const save = useMutation({
    mutationFn: (payload: AnyRecord) => payload.id ? jobsApi.update(String(payload.id), payload) : jobsApi.create(payload),
    onSuccess: async (_d, payload) => { setEditing(null); await qc.invalidateQueries({ queryKey: ["jobs"] }); notify("success", payload.id ? "Job updated" : "Job created"); },
    onError: (error) => notify("error", requestError(error, "Could not save the job. Please try again.")),
  });
  const remove = useMutation({
    mutationFn: (id: string | number) => jobsApi.remove(id),
    onSuccess: async () => { setSelected(null); await qc.invalidateQueries({ queryKey: ["jobs"] }); notify("success", "Job archived"); },
    onError: (error) => notify("error", requestError(error, "Could not archive the job.")),
  });
  const action = useMutation({
    mutationFn: ({ type, id }: { type: string; id: string | number }) => type === "eta" ? jobsApi.sendEta(id) : jobsApi.proofPlaceholder(id),
    onSuccess: async (result, vars) => {
      await qc.invalidateQueries({ queryKey: ["jobs"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "summary"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "detail", selected?.id] });
      await qc.invalidateQueries({ queryKey: ["pod"] });
      await qc.invalidateQueries({ queryKey: ["pod", "summary"] });
      notify("success", vars.type === "eta"
        ? String(result.deliveryStatus) === "Sent" ? "Customer ETA recorded in-app" : "Customer ETA queued for provider delivery"
        : "POD workflow queued");
      if (vars.type === "proof") {
        navigate(`/proof-of-delivery?jobId=${vars.id}`);
      }
    },
    onError: (error) => notify("error", requestError(error, "The action could not be completed.")),
  });
  const changeStatus = useMutation({
    mutationFn: ({ id, status: next }: { id: string | number; status: string }) => jobsApi.changeStatus(id, next),
    onSuccess: async (_d, vars) => {
      await qc.invalidateQueries({ queryKey: ["jobs"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "summary"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "detail", selected?.id] });
      notify("success", `Job advanced to ${vars.status}`);
    },
    onError: (error) => notify("error", requestError(error, "Status change was rejected.")),
  });
  const assign = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => jobsApi.assign(id, payload),
    onSuccess: async () => {
      setAssigning(null);
      await qc.invalidateQueries({ queryKey: ["jobs"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "summary"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "detail", selected?.id] });
      notify("success", "Driver and vehicle assigned");
    },
    onError: (error) => notify("error", requestError(error, "Assignment was rejected. Check availability, HOS, and branch ownership.")),
  });
  const previewImport = useMutation({
    mutationFn: (rows: AnyRecord[]) => jobsApi.importPreview(rows),
    onSuccess: (preview, rows) => setImportSession({ rows, preview, idempotencyKey: globalThis.crypto.randomUUID() }),
    onError: (error) => notify("error", requestError(error, "The shipment CSV could not be validated.")),
  });
  const commitImport = useMutation({
    mutationFn: ({ rows, idempotencyKey }: { rows: AnyRecord[]; idempotencyKey: string }) => jobsApi.importCommit(rows, idempotencyKey),
    onSuccess: async (result) => {
      setImportSession(null);
      await qc.invalidateQueries({ queryKey: ["jobs"] });
      const created = Number(result.created ?? 0);
      const updated = Number(result.updated ?? 0);
      const skipped = Array.isArray(result.skipped) ? result.skipped.length : Number(result.skipped ?? 0);
      notify(skipped ? "info" : "success", `Import complete: ${created} created, ${updated} updated, ${skipped} skipped`);
    },
    onError: (error) => notify("error", requestError(error, "The shipment import could not be committed.")),
  });

  useEffect(() => setJobsOffset(0), [status, priority]);

  const exportRoster = async () => {
    if (!canExport) return;
    try {
      await downloadServerExport(jobsApi.exportPath({ search: query, status, priority }), `${surfaceConfig.exportName}.csv`);
      notify("success", "Full job roster exported");
    } catch {
      notify("error", "Could not export the full job roster.");
    }
  };

  const chooseImport = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) {
      notify("error", "CSV files are limited to 2 MB.");
      return;
    }
    try {
      const rows = parseJobCsv(await file.text());
      if (rows.length > 500) throw new Error("Import at most 500 shipment rows at a time.");
      previewImport.mutate(rows);
    } catch (error) {
      notify("error", error instanceof Error ? error.message : "The CSV could not be parsed.");
    }
  };

  const baseRows = useMemo(() => scopedRows.filter(surfaceConfig.filterRows), [scopedRows, surfaceConfig]);

  const rows = useMemo(() => baseRows.filter((row) => {
    const qLower = query.toLowerCase();
    const matchesText = !query ||
      String(row.jobNumber || row.jobCode || "").toLowerCase().includes(qLower) ||
      String(row.customerName || "").toLowerCase().includes(qLower) ||
      String(row.driverName || row.vehicleCode || row.trackingCode || "").toLowerCase().includes(qLower) ||
      String(row.pickupAddress || row.dropoffAddress || "").toLowerCase().includes(qLower);

    const matchesStatus = status === "All"
      || String(row.status) === status
      || (status === "En Route" && /In Progress/i.test(String(row.status)))
      || (status === "Completed" && /Delivered/i.test(String(row.status)))
      || (status === "SLA At Risk" && (row.slaStatus === "At Risk" || /At Risk/i.test(String(row.status))));
    const matchesPriority = priority === "All" || String(row.priority) === priority;
    return matchesText && matchesStatus && matchesPriority;
  }), [baseRows, priority, query, status]);

  useEffect(() => {
    if (!focusedJobId || focusedJobOpened.current === focusedJobId) return;
    const focused = rows.find((row) => String(row.id) === focusedJobId);
    if (!focused) return;
    focusedJobOpened.current = focusedJobId;
    setSelected(focused);
  }, [focusedJobId, rows]);

  useEffect(() => {
    if (selected && !rows.some((row) => String(row.id) === String(selected.id))) {
      setSelected(null);
    }
  }, [rows, selected]);

  if (jobs.isLoading) return <LoadingState />;
  if (jobs.isError) return <ErrorState message={jobs.error instanceof Error ? jobs.error.message : "Unable to load jobs."} onRetry={() => void jobsPaged.refetch()} />;

  const headline = [
    { label: surface === "active-shipments" ? "Active Now" : surface === "shipments" ? "Shipment Rows" : "Jobs Today", value: surface === "active-shipments" ? rows.length : visibleSummary.totalJobsToday, icon: <Package className="h-4 w-4" /> },
    { label: "SLA At Risk", value: visibleSummary.slaAtRisk, status: "Review", icon: <TriangleAlert className="h-4 w-4" /> },
    { label: "Proof Pending", value: visibleSummary.proofPending, status: "Review", icon: <FileCheck2 className="h-4 w-4" /> },
    { label: "On-Time ETA", value: visibleSummary.averageEtaAccuracy, icon: <MapPin className="h-4 w-4" /> },
    { label: "Updates Sent", value: visibleSummary.customerUpdatesSent, icon: <Send className="h-4 w-4" /> },
    { label: "Revenue Margin", value: visibleSummary.revenueMargin, icon: <Truck className="h-4 w-4" /> },
  ];

  const slaRisk = Number(visibleSummary.slaAtRisk ?? 0);
  const proofPending = Number(visibleSummary.proofPending ?? 0);
  const unassigned = Number(visibleSummary.unassignedJobs ?? 0);

  return (
    <div className="fleet-console space-y-3">
      <Toaster toast={toast} onClose={() => setToast(null)} />

      <PageHeader
        eyebrow={surfaceConfig.eyebrow}
        title={surfaceConfig.title}
        description={surfaceConfig.description}
        actions={<>
          <button type="button" className="btn-primary" disabled={!canCreate} title={!canCreate ? "You do not have permission to perform this action." : undefined} onClick={() => canCreate && setEditing({ priority: "Normal", jobType: "Delivery", status: "Unassigned" })}><Plus className="h-4 w-4" /> {surfaceConfig.createLabel}</button>
          <input ref={importInput} className="sr-only" type="file" accept=".csv,text/csv" onChange={chooseImport} tabIndex={-1} aria-hidden="true" />
          <button type="button" className="btn-ghost" disabled={!canCreate || previewImport.isPending} title={!canCreate ? "You do not have permission to import shipments." : undefined} onClick={() => canCreate && importInput.current?.click()}><Upload className="h-4 w-4" /> {previewImport.isPending ? "Validating..." : "Import CSV"}</button>
          <button type="button" className="btn-ghost" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : undefined} onClick={exportRoster}><Download className="h-4 w-4" /> Export Roster</button>
        </>}
      />

      {/* Ops intelligence bar — derived from live data, one-click triage.
          Light card (teal-tinted icon badge, dark text), matching the rest of the app's
          "fc-console" panels (Vehicles, Cold Chain, Fleet Compliance) instead of a solid
          dark fill -- a dark bar here was the odd one out, not the app's actual theme. */}
      <div className="anim-fade-up flex flex-col gap-3 overflow-hidden rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-teal-50"><Sparkles className="h-5 w-5 text-teal-600" /></span>
          <div>
            <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-teal-700">Live operations signal</p>
            <p className="mt-0.5 text-sm font-medium text-slate-600">
              {slaRisk + proofPending + unassigned === 0
                ? surface === "active-shipments"
                  ? "Active shipment execution is stable — no immediate SLA, proof, or assignment exceptions."
                  : surface === "shipments"
                    ? "Shipment lifecycle is stable end to end — no immediate SLA, proof, or assignment exceptions."
                    : "All jobs on track — no SLA, proof, or assignment exceptions right now."
                : [
                    slaRisk > 0 ? `${slaRisk} at SLA risk` : null,
                    unassigned > 0 ? `${unassigned} unassigned` : null,
                    proofPending > 0 ? `${proofPending} awaiting proof` : null,
                  ].filter(Boolean).join(" · ")}
            </p>
          </div>
        </div>
        {slaRisk > 0 && (
          <button type="button" onClick={() => setStatus("SLA At Risk")} className="inline-flex items-center gap-1.5 self-start rounded-lg bg-teal-500 px-3.5 py-2 text-xs font-bold text-white transition hover:bg-teal-400 sm:self-auto">
            Triage at-risk jobs <ArrowRight className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {/* Headline KPIs */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
        {headline.map((k) => <KpiCard key={k.label} label={k.label} value={String(k.value ?? "0")} status={k.status} />)}
      </div>

      {/* Lifecycle pipeline — clickable status filter with live counts */}
      <div className="panel p-2">
        <div className="grid grid-cols-2 gap-1.5 sm:grid-cols-4 lg:grid-cols-8">
          <PipelineChip label="All" count={String(visibleSummary.total ?? scopedRows.length)} active={status === "All"} onClick={() => setStatus("All")} />
          {PIPELINE.map((stage) => (
            <PipelineChip
              key={stage.statusValue}
              label={stage.label}
              count={String(visibleSummary[stage.summaryKey] ?? 0)}
              tone={stage.statusValue === "SLA Risk" || stage.statusValue === "Delayed" || stage.statusValue === "SLA At Risk" ? "warn" : stage.statusValue === "Completed" ? "good" : "default"}
              active={status === stage.statusValue}
              onClick={() => setStatus(stage.statusValue)}
            />
          ))}
        </div>
      </div>

      {/* Toolbar */}
      <div className="panel flex flex-col gap-3 p-3.5 lg:flex-row lg:items-center">
        <div className="relative flex-1 lg:max-w-md">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          {/* pl-9! (forced): `.field`'s own padding shorthand otherwise wins over the plain
              pl-9 utility, collapsing the left inset so the icon sits on top of the text. */}
          <input className="field h-10 pl-9!" aria-label="Search shipments" value={query} onChange={(e) => { setQuery(e.target.value); setJobsOffset(0); }} placeholder="Search jobs, customers, drivers, addresses..." />
        </div>
        <select className="field h-10 lg:max-w-[180px]" aria-label="Filter by priority" value={priority} onChange={(e) => setPriority(e.target.value)}>
          <option value="All">All priorities</option><option>Low</option><option>Normal</option><option>High</option><option>Critical</option>
        </select>
        <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-semibold text-slate-500">{rows.length} shown · {jobsTotal.toLocaleString()} total</span>
        {jobsTotal > JOBS_PAGE_SIZE && (
          <span className="ml-auto flex items-center gap-2 text-xs text-slate-500">
            <button type="button" disabled={jobsOffset === 0 || jobsPaged.isFetching}
              onClick={() => setJobsOffset(Math.max(0, jobsOffset - JOBS_PAGE_SIZE))}
              className="rounded-lg border border-slate-200 px-2.5 py-1 font-medium transition enabled:hover:bg-slate-50 disabled:opacity-40">← Prev</button>
            <span>Page {Math.floor(jobsOffset / JOBS_PAGE_SIZE) + 1} of {Math.max(1, Math.ceil(jobsTotal / JOBS_PAGE_SIZE))}</span>
            <button type="button" disabled={jobsOffset + JOBS_PAGE_SIZE >= jobsTotal || jobsPaged.isFetching}
              onClick={() => setJobsOffset(jobsOffset + JOBS_PAGE_SIZE)}
              className="rounded-lg border border-slate-200 px-2.5 py-1 font-medium transition enabled:hover:bg-slate-50 disabled:opacity-40">Next →</button>
          </span>
        )}
      </div>

      {rows.length ? (
        <DataTable rows={rows} columns={surfaceConfig.tableColumns} onSelect={setSelected} />
      ) : (
        <EmptyState title="No jobs match these filters" subtitle="Adjust the pipeline stage, priority, or search to widen results." />
      )}

      <JobDrawer
        detail={detail.data}
        loading={detail.isLoading}
        error={detail.error}
        onClose={() => {
          setSelected(null);
          if (focusedJobId) navigate(location.pathname, { replace: true });
        }}
        onEdit={(record) => canEdit && setEditing(record)}
        onAssign={(record) => canAssign && setAssigning(record)}
        onDelete={(id) => canDelete && window.confirm("Archive this job? It will be removed from the active shipment register.") && remove.mutate(id)}
        onEta={(id) => canDispatch && action.mutate({ type: "eta", id })}
        onProof={(id) => canDispatch && action.mutate({ type: "proof", id })}
        onStatus={(id, next) => (next === "Cancelled" ? canCancel : canDispatch) && changeStatus.mutate({ id, status: next })}
        statusPending={changeStatus.isPending}
        onExport={() => exportJobRecordCsv(selectedRecord(detail.data, selected), detail.data)}
        canEdit={canEdit} canDelete={canDelete} canDispatch={canDispatch} canCancel={canCancel} canAssign={canAssign} canExport={canExport}
      />
      {editing ? <JobModal initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(payload) => save.mutate(payload)} /> : null}
      {assigning ? <AssignmentModal initial={assigning} saving={assign.isPending} onClose={() => setAssigning(null)} onSave={(payload) => assign.mutate({ id: String(assigning.id), payload })} /> : null}
      {importSession ? <JobImportModal session={importSession} saving={commitImport.isPending} onClose={() => setImportSession(null)} onCommit={() => commitImport.mutate({ rows: importSession.rows, idempotencyKey: importSession.idempotencyKey })} /> : null}
    </div>
  );
}

function Toaster({ toast, onClose }: { toast: Toast | null; onClose: () => void }) {
  if (!toast) return null;
  const theme = toast.kind === "success"
    ? { ring: "border-emerald-200", bar: "bg-emerald-500", icon: <CheckCircle2 className="h-5 w-5 text-emerald-600" /> }
    : toast.kind === "error"
    ? { ring: "border-red-200", bar: "bg-red-500", icon: <TriangleAlert className="h-5 w-5 text-red-600" /> }
    : { ring: "border-slate-200", bar: "bg-slate-500", icon: <Info className="h-5 w-5 text-slate-600" /> };
  return (
    <div className="fixed right-5 top-5 z-[80] anim-slide-right">
      <div className={`relative flex items-center gap-3 overflow-hidden rounded-xl border ${theme.ring} bg-white/95 py-3 pl-5 pr-3 shadow-2xl backdrop-blur`}>
        <span className={`absolute left-0 top-0 h-full w-1 ${theme.bar}`} />
        {theme.icon}
        <p className="max-w-xs text-sm font-semibold text-slate-800">{toast.message}</p>
        <button type="button" className="icon-btn ml-1" onClick={onClose} aria-label="Dismiss"><X className="h-4 w-4" /></button>
      </div>
    </div>
  );
}

function PipelineChip({ label, count, active, tone = "default", onClick }: { label: string; count: number | string; active: boolean; tone?: "default" | "warn" | "good"; onClick: () => void }) {
  const accent = tone === "warn" ? "text-amber-700" : tone === "good" ? "text-emerald-700" : "text-slate-900";
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`flex flex-col items-start rounded-xl border px-3 py-2.5 text-left transition ${active ? "border-teal-300 bg-teal-50 shadow-sm" : "border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50"}`}
    >
      <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">{label}</span>
      <span className={`mt-0.5 text-xl font-bold tabular-nums ${active ? "text-teal-700" : accent}`}>{count}</span>
    </button>
  );
}

function JobDrawer({ detail, loading, error, onClose, onEdit, onAssign, onEta, onProof, onStatus, statusPending, onDelete, onExport, canEdit, canDelete, canDispatch, canCancel, canAssign, canExport }: { detail?: AnyRecord; loading: boolean; error?: unknown; onClose: () => void; onEdit: (record: AnyRecord) => void; onAssign: (record: AnyRecord) => void; onEta: (id: string | number) => void; onProof: (id: string | number) => void; onStatus: (id: string | number, status: string) => void; statusPending: boolean; onDelete: (id: string | number) => void; onExport: () => void; canEdit: boolean; canDelete: boolean; canDispatch: boolean; canCancel: boolean; canAssign: boolean; canExport: boolean }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading && !error) return null;
  if (!record) return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in">
      <aside role="dialog" aria-modal="true" aria-label="Shipment detail" className="anim-slide-right flex h-full w-full max-w-3xl flex-col overflow-y-auto border-l border-slate-200 bg-white p-6 shadow-2xl">
        <div className="flex justify-end"><button type="button" className="icon-btn" onClick={onClose} aria-label="Close shipment detail"><X className="h-5 w-5" /></button></div>
        {error ? <ErrorState message={requestError(error, "Unable to load shipment detail.")} /> : <LoadingState />}
      </aside>
    </div>
  );
  const transitions = nextStatuses(String(record.status ?? ""));
  const terminal = /completed|delivered|cancelled/i.test(String(record.status ?? ""));
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in">
      <aside role="dialog" aria-modal="true" aria-labelledby="job-detail-title" className="anim-slide-right flex h-full w-full max-w-3xl flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-2xl">
        {/* Sticky header */}
        <div className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="section-title text-teal-700">Job Detail</p>
              <h2 id="job-detail-title" className="mt-1 text-2xl font-bold text-slate-900">{String(record.jobNumber || record.jobCode)}</h2>
              <div className="mt-3 flex flex-wrap items-center gap-2">
                <StatusBadge status={record.status} />
                <RiskBadge risk={record.riskHeatScore} />
                <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-[3px] text-[10px] font-bold text-slate-600">SLA {String(record.slaStatus ?? "--")}</span>
                <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-[3px] text-[10px] font-bold text-slate-600">Proof {String(record.proofStatus ?? "--")}</span>
              </div>
            </div>
            <button type="button" className="icon-btn" onClick={onClose} aria-label="Close"><X className="h-5 w-5" /></button>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" className="btn-primary h-9 py-0" disabled={!canEdit} title={!canEdit ? "You do not have permission to perform this action." : undefined} onClick={() => canEdit && onEdit(record)}><Edit3 className="h-4 w-4" /> Edit</button>
            <button type="button" className="btn-ghost h-9 py-0" disabled={!canAssign || terminal} title={!canAssign ? "You do not have permission to assign jobs." : terminal ? "Terminal jobs cannot be reassigned." : undefined} onClick={() => canAssign && !terminal && onAssign(record)}><UserCheck className="h-4 w-4" /> {record.assignedDriverId ? "Reassign" : "Assign"}</button>
            <button type="button" className="btn-ghost h-9 py-0" disabled={!canDispatch || terminal} title={!canDispatch ? "You do not have permission to perform this action." : terminal ? "ETA updates are closed for terminal jobs." : undefined} onClick={() => canDispatch && !terminal && onEta(String(record.id))}><Send className="h-4 w-4" /> Send ETA</button>
            <button type="button" className="btn-ghost h-9 py-0" disabled={!canDispatch || terminal} title={!canDispatch ? "You do not have permission to perform this action." : terminal ? "POD is closed for terminal jobs." : undefined} onClick={() => canDispatch && !terminal && onProof(String(record.id))}><FileCheck2 className="h-4 w-4" /> Queue POD</button>
            <button type="button" className="btn-ghost h-9 py-0" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : undefined} onClick={() => canExport && onExport()}><Download className="h-4 w-4" /> Export</button>
            <button type="button" className="btn-ghost h-9 py-0 text-red-600" disabled={!canDelete} title={!canDelete ? "You do not have permission to perform this action." : "Archive this job"} onClick={() => canDelete && onDelete(String(record.id))}><Trash2 className="h-4 w-4" /> Archive</button>
          </div>
        </div>

        <div className="space-y-6 px-6 py-6">
          {/* Lifecycle advance */}
          <section className="rounded-2xl border border-teal-100 bg-teal-50/50 p-4">
            <div className="flex items-center justify-between">
              <h3 className="section-title text-teal-800">Advance Lifecycle</h3>
              <span className="text-xs font-medium text-slate-500">Current: <span className="font-bold text-slate-700">{String(record.status ?? "--")}</span></span>
            </div>
            {transitions.length ? (
              <div className="mt-3 flex flex-wrap gap-2">
                {transitions.map((next) => (
                  (() => {
                  const permitted = next === "Cancelled" ? canCancel : canDispatch;
                  return (
                  <button
                    key={next}
                    type="button"
                    className="btn-ghost h-9 py-0"
                    disabled={!permitted || statusPending}
                    title={!permitted ? "You do not have permission to perform this action." : `Move job to ${next}`}
                    onClick={() => permitted && onStatus(String(record.id), next)}
                  >
                    <ArrowRight className="h-4 w-4 text-teal-600" /> {next}
                  </button>
                  );
                  })()
                ))}
              </div>
            ) : (
              <p className="mt-3 flex items-center gap-2 text-sm text-emerald-700"><CheckCircle2 className="h-4 w-4" /> This job has reached a terminal state.</p>
            )}
          </section>

          <div className="grid gap-4 lg:grid-cols-2">
            <Panel title="Assignment" record={record} keys={["driverName", "vehicleCode", "requiredVehicleType", "requiredDriverCertification", "routeCode"]} />
            <Panel title="Pickup / Drop-off" record={record} keys={["pickupAddress", "dropoffAddress", "scheduledStart", "scheduledEnd"]} />
            <Panel title="SLA & ETA" record={record} keys={["slaWindowStart", "slaWindowEnd", "eta", "slaStatus", "customerUpdateStatus"]} />
            <Panel title="Costs & Billing" record={{ ...((detail?.costs as AnyRecord) || {}), billingReadiness: record.billingReadiness }} keys={["revenueEstimate", "costEstimate", "marginEstimate", "marginRisk", "billingReadiness"]} format="currency" />
          </div>

          <Grid title="Job Timeline" rows={(detail?.timeline as AnyRecord[]) || []} columns={["eventType", "title", "createdAt"]} />
          <Grid title="Route / Stops" rows={(detail?.stops as AnyRecord[]) || []} columns={["stopSequence", "stopType", "address", "status", "proofStatus", "eta"]} />
          <Grid title="Proof of Delivery / Service" rows={(detail?.proof as AnyRecord[]) || []} columns={["proofType", "status", "receivedBy", "capturedAt", "notes"]} />
          <Grid title="Customer Communications" rows={(detail?.communications as AnyRecord[]) || []} columns={["channel", "messageType", "message", "status", "sentAt"]} />
          <Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName", "actorName", "createdAt"]} />

          {((detail?.recommendations as AnyRecord[]) || []).length ? (
            <section>
              <h3 className="section-title">Recommended Actions</h3>
              <div className="mt-3 grid gap-3 lg:grid-cols-2">{((detail?.recommendations as AnyRecord[]) || []).slice(0, 4).map((x, i) => <AiInsightCard key={String(x.id || i)} insight={x} />)}</div>
            </section>
          ) : null}
        </div>
      </aside>
    </div>
  );
}

function AssignmentModal({ initial, saving, onClose, onSave }: { initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>({
    driverId: initial.assignedDriverId ?? "",
    vehicleId: initial.assignedVehicleId ?? "",
    override: false,
    overrideReason: "",
  });
  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSave({
      ...form,
      driverId: Number(form.driverId),
      vehicleId: Number(form.vehicleId),
    });
  };
  return (
    <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in">
      <form role="dialog" aria-modal="true" aria-labelledby="assignment-modal-title" className="panel w-full max-w-lg p-6 shadow-2xl" onSubmit={submit}>
        <div className="flex items-center justify-between border-b border-slate-200 pb-4">
          <div>
            <p className="section-title text-teal-700">Dispatch Assignment</p>
            <h2 id="assignment-modal-title" className="mt-1 text-xl font-bold text-slate-900">{String(initial.jobNumber ?? initial.jobCode ?? "Job")}</h2>
          </div>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Close"><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-5 grid gap-4 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1.5 block text-xs font-bold uppercase tracking-[0.14em] text-slate-500">Driver ID</span>
            <input className="field" type="number" min="1" step="1" required value={String(form.driverId)} onChange={(e) => setForm((x) => ({ ...x, driverId: e.target.value }))} />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-xs font-bold uppercase tracking-[0.14em] text-slate-500">Vehicle ID</span>
            <input className="field" type="number" min="1" step="1" required value={String(form.vehicleId)} onChange={(e) => setForm((x) => ({ ...x, vehicleId: e.target.value }))} />
          </label>
        </div>
        <label className="mt-4 flex items-center gap-2 text-sm font-semibold text-slate-700">
          <input type="checkbox" checked={Boolean(form.override)} onChange={(e) => setForm((x) => ({ ...x, override: e.target.checked }))} />
          Request authorized soft-rule override
        </label>
        {form.override ? (
          <label className="mt-4 block">
            <span className="mb-1.5 block text-xs font-bold uppercase tracking-[0.14em] text-slate-500">Override reason</span>
            <textarea className="field min-h-24" maxLength={500} required value={String(form.overrideReason)} onChange={(e) => setForm((x) => ({ ...x, overrideReason: e.target.value }))} />
          </label>
        ) : null}
        <p className="mt-4 text-xs text-slate-500">The server will verify tenant and branch ownership, driver HOS, vehicle availability, and active-assignment conflicts.</p>
        <div className="mt-6 flex justify-end gap-3 border-t border-slate-200 pt-4">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={saving}>{saving ? "Assigning..." : "Assign Resources"}</button>
        </div>
      </form>
    </div>
  );
}

function JobImportModal({ session, saving, onClose, onCommit }: { session: JobImportSession; saving: boolean; onClose: () => void; onCommit: () => void }) {
  const previewRows = Array.isArray(session.preview.rows) ? session.preview.rows as AnyRecord[] : [];
  const invalid = Number(session.preview.invalid ?? 0);
  return (
    <div className="fixed inset-0 z-[75] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in">
      <section role="dialog" aria-modal="true" aria-labelledby="jobs-import-title" className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6 shadow-2xl">
        <div className="flex items-start justify-between gap-4 border-b border-slate-200 pb-4">
          <div>
            <p className="section-title text-teal-700">Validated CSV Import</p>
            <h2 id="jobs-import-title" className="mt-1 text-xl font-bold text-slate-900">Review shipment changes</h2>
            <p className="mt-1 text-sm text-slate-500">{Number(session.preview.creates ?? 0)} creates · {Number(session.preview.updates ?? 0)} updates · {invalid} invalid</p>
          </div>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Close import review"><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-5 overflow-x-auto rounded-xl border border-slate-200">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500"><tr><th className="px-3 py-2">Row</th><th className="px-3 py-2">Job</th><th className="px-3 py-2">Action</th><th className="px-3 py-2">Validation</th></tr></thead>
            <tbody>
              {previewRows.map((row, index) => {
                const errors = Array.isArray(row.errors) ? row.errors.map(String) : [];
                return <tr key={`${String(row.key ?? "row")}-${index}`} className="border-t border-slate-100 align-top"><td className="px-3 py-2">{String(row.rowNumber ?? index + 1)}</td><td className="px-3 py-2 font-medium">{String(row.key ?? "--")}</td><td className="px-3 py-2">{String(row.action ?? "--")}</td><td className={`px-3 py-2 ${errors.length ? "text-red-700" : "text-emerald-700"}`}>{errors.length ? errors.join(" ") : "Ready"}</td></tr>;
              })}
            </tbody>
          </table>
        </div>
        <p className="mt-4 text-xs text-slate-500">Invalid rows are never written. Existing jobs are updated only when your role has the exact update permission and the job belongs to your branch.</p>
        <div className="mt-6 flex justify-end gap-3 border-t border-slate-200 pt-4">
          <button type="button" className="btn-ghost" onClick={onClose} disabled={saving}>Cancel</button>
          <button type="button" className="btn-primary" onClick={onCommit} disabled={saving || previewRows.length === 0 || invalid === previewRows.length}>{saving ? "Importing..." : "Commit Valid Rows"}</button>
        </div>
      </section>
    </div>
  );
}

function JobModal({ initial, saving, onClose, onSave }: { initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (event: FormEvent) => { event.preventDefault(); onSave(form); };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in">
      <form role="dialog" aria-modal="true" aria-labelledby="job-modal-title" className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6 shadow-2xl" onSubmit={submit}>
        <div className="flex items-center justify-between border-b border-slate-200 pb-4">
          <h2 id="job-modal-title" className="text-2xl font-bold text-slate-900">{form.id ? "Edit Job" : "Create Job"}</h2>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Close"><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-6 grid gap-4 md:grid-cols-2">
          {fields.map(([key, label]) => (
            <label key={key} className="block">
              <span className="mb-1.5 block text-xs font-bold uppercase tracking-[0.14em] text-slate-500">{label}</span>
              <input className="field" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} required={["jobNumber", "customerId", "pickupAddress", "dropoffAddress"].includes(key)} />
            </label>
          ))}
        </div>
        <div className="mt-6 flex justify-end gap-3 border-t border-slate-200 pt-4">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={saving}>{saving ? "Saving..." : "Save Job"}</button>
        </div>
      </form>
    </div>
  );
}

function Panel({ title, record, keys, format }: { title: string; record: AnyRecord; keys: string[]; format?: "currency" }) {
  return (
    <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
      <h3 className="section-title">{title}</h3>
      <div className="mt-3 space-y-2.5">
        {keys.map((key) => {
          const raw = record[key];
          const value = format === "currency" && raw != null && raw !== "" && !Number.isNaN(Number(raw)) && /estimate/i.test(key)
            ? `$${Number(raw).toLocaleString()}`
            : raw ?? "--";
          return (
            <div key={key} className="flex items-start justify-between gap-3">
              <span className="text-xs font-medium text-slate-500">{labelize(key)}</span>
              <span className="text-right text-sm font-medium text-slate-800">{String(value)}</span>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return (
    <section>
      <h3 className="section-title">{title}</h3>
      {!rows.length ? (
        <p className="mt-2 rounded-xl border border-dashed border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-400">No records yet.</p>
      ) : (
        <div className="mt-2">
          <DataTable rows={rows.slice(0, 8)} columns={columns} />
        </div>
      )}
    </section>
  );
}

function nextStatuses(current: string): string[] {
  const flow: Record<string, string[]> = {
    Unassigned: ["Cancelled"],
    Assigned: ["En Route", "Delayed", "Exception", "Cancelled"],
    "En Route": ["At Stop", "Delayed", "Exception", "Cancelled"],
    "In Progress": ["At Stop", "Delayed", "Exception", "Cancelled"],
    "At Stop": ["Completed", "Delayed", "Exception", "Cancelled"],
    Delayed: ["En Route", "At Stop", "Completed", "Exception", "Cancelled"],
    "At Risk": ["En Route", "At Stop", "Completed", "Exception", "Cancelled"],
    Exception: ["Assigned", "En Route", "At Stop", "Completed", "Cancelled"],
    Completed: ["Delivered"],
  };
  return flow[current] ?? [];
}

function exportJobRecordCsv(record?: AnyRecord | null, detail?: AnyRecord) {
  if (!record) return;
  const rows = [
    { section: "Job", key: "jobNumber", value: String(record.jobNumber ?? record.jobCode ?? record.id ?? "") },
    { section: "Job", key: "customerName", value: String(record.customerName ?? record.customer ?? "") },
    { section: "Job", key: "status", value: String(record.status ?? "") },
    { section: "Job", key: "driverName", value: String(record.driverName ?? record.assignedDriver ?? "") },
    { section: "Job", key: "vehicleCode", value: String(record.vehicleCode ?? record.assignedVehicle ?? "") },
    { section: "Detail", key: "timeline", value: JSON.stringify(detail?.timeline ?? []) },
    { section: "Detail", key: "proof", value: JSON.stringify(detail?.proof ?? []) },
    { section: "Detail", key: "communications", value: JSON.stringify(detail?.communications ?? []) },
  ];
  exportCsv(`job-${String(record.jobNumber ?? record.jobCode ?? record.id ?? "report")}`, rows.map((row) => ({
    ...row,
    value: safeSpreadsheetValue(row.value),
  })));
}

function safeSpreadsheetValue(value: unknown) {
  const text = String(value ?? "");
  return /^[\s]*[=+\-@]/.test(text) ? `'${text}` : text;
}

function selectedRecord(detail: AnyRecord | undefined, selected: AnyRecord | null) {
  return (detail?.record as AnyRecord | undefined) ?? selected;
}

function requestError(error: unknown, fallback: string) {
  const response = (error as { response?: { data?: { message?: unknown; errors?: unknown } } })?.response?.data;
  const message = typeof response?.message === "string" ? response.message.trim() : "";
  const errors = Array.isArray(response?.errors) ? response.errors.map(String).filter(Boolean) : [];
  if (message && errors.length) return `${message}: ${errors.join(" ")}`;
  if (message) return message;
  if (error instanceof Error && error.message.trim()) return error.message;
  return fallback;
}

function parseJobCsv(text: string): AnyRecord[] {
  const grid: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let quoted = false;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (char === '"') {
      if (quoted && text[index + 1] === '"') { cell += '"'; index += 1; }
      else quoted = !quoted;
    } else if (char === "," && !quoted) {
      row.push(cell.trim()); cell = "";
    } else if ((char === "\n" || char === "\r") && !quoted) {
      if (char === "\r" && text[index + 1] === "\n") index += 1;
      row.push(cell.trim()); cell = "";
      if (row.some((value) => value.length > 0)) grid.push(row);
      row = [];
    } else cell += char;
  }
  if (quoted) throw new Error("The CSV contains an unterminated quoted value.");
  row.push(cell.trim());
  if (row.some((value) => value.length > 0)) grid.push(row);
  if (grid.length < 2) throw new Error("The CSV must contain a header and at least one shipment row.");

  const allowed = new Set([
    "jobNumber", "jobCode", "customerId", "jobType", "priority", "pickupAddress", "dropoffAddress",
    "scheduledStart", "scheduledEnd", "slaWindowStart", "slaWindowEnd", "requiredVehicleType",
    "requiredDriverCertification", "notes",
  ]);
  const headers = grid[0].map((header, index) => index === 0 ? header.replace(/^\uFEFF/, "") : header);
  if (new Set(headers).size !== headers.length) throw new Error("The CSV header contains duplicate columns.");
  const unknown = headers.filter((header) => !allowed.has(header));
  if (unknown.length) throw new Error(`Unsupported CSV columns: ${unknown.join(", ")}.`);
  for (const required of ["customerId", "pickupAddress", "dropoffAddress"])
    if (!headers.includes(required)) throw new Error(`The CSV is missing required column '${required}'.`);
  if (!headers.includes("jobNumber") && !headers.includes("jobCode")) throw new Error("The CSV requires jobNumber.");

  return grid.slice(1).map((values, index) => {
    if (values.length !== headers.length)
      throw new Error(`CSV row ${index + 2} has ${values.length} columns; expected ${headers.length}.`);
    return Object.fromEntries(headers.map((header, column) => [header, values[column] ?? ""])) as AnyRecord;
  });
}

function buildJobSummary(rows: AnyRecord[], summary: AnyRecord | undefined, session?: AnyRecord | null): AnyRecord {
  const role = String(session?.role ?? "");
  const scoped = isDriverPortalRole(role) || isCustomerPortalRole(role);
  if (!scoped) return summary ?? {};

  const total = rows.length;
  const assigned = rows.filter((row) => /assigned|en route|at stop/i.test(String(row.status ?? ""))).length;
  const delayed = rows.filter((row) => /delayed|risk/i.test(String(row.slaStatus ?? row.status ?? ""))).length;
  const completed = rows.filter((row) => /completed|delivered/i.test(String(row.status ?? ""))).length;
  const proofPending = rows.filter((row) => /pending/i.test(String(row.proofStatus ?? ""))).length;
  const updatesSent = rows.filter((row) => /sent|delivered/i.test(String(row.customerUpdateStatus ?? row.status ?? ""))).length;
  // ETA accuracy: a row counts only when it has BOTH an eta and an SLA due timestamp
  // (field name varies across endpoints), and both parse to valid dates.
  const etaDue = (row: AnyRecord) => row.slaDueAt ?? row.sla_due_at ?? row.slaWindowEnd ?? row.sla_window_end;
  const withEta = rows.filter((row) => {
    const e = row.eta ? new Date(String(row.eta)).getTime() : NaN;
    const d = etaDue(row) ? new Date(String(etaDue(row))).getTime() : NaN;
    return !Number.isNaN(e) && !Number.isNaN(d);
  });
  const onTime = withEta.filter((row) => new Date(String(row.eta)) <= new Date(String(etaDue(row)))).length;
  const revenueMargin = rows.reduce((acc, row) => acc + Number(row.marginEstimate ?? 0), 0);
  return {
    ...summary,
    total,
    totalJobsToday: total,
    unassignedJobs: rows.filter((row) => /unassigned/i.test(String(row.status ?? ""))).length,
    assignedJobs: assigned,
    enRoute: rows.filter((row) => /en route|in progress/i.test(String(row.status ?? ""))).length,
    atStop: rows.filter((row) => /at stop/i.test(String(row.status ?? ""))).length,
    completed,
    delayed,
    slaAtRisk: delayed,
    proofPending,
    customerUpdatesSent: updatesSent,
    averageEtaAccuracy: withEta.length ? `${Math.round((onTime / withEta.length) * 100)}%` : "N/A",
    revenueMargin: revenueMargin ? `$${revenueMargin.toLocaleString()}` : "N/A",
  };
}
