import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowRight, CheckCircle2, Download, Edit3, FileCheck2, Info, MapPin, Package, Plus,
  Search, Send, Sparkles, Trash2, TriangleAlert, Truck, X,
} from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  AiInsightCard, DataTable, EmptyState, ErrorState, KpiCard, LoadingState, PageHeader,
  RiskBadge, StatusBadge, exportCsv, labelize,
} from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { useAuth } from "@/hooks/useAuth";
import { isCustomerPortalRole, isDriverPortalRole, scopeRowsForSession } from "@/auth/accessScope";
import { useJobDetail, useJobSummary } from "@/hooks/useBatch2";
import { jobsApi } from "@/services/jobsApi";
import type { AnyRecord } from "@/types";

const fields = [
  ["jobNumber", "Job Number"], ["customerId", "Customer ID"], ["jobType", "Job Type"], ["priority", "Priority"],
  ["pickupAddress", "Pickup Address"], ["dropoffAddress", "Drop-off Address"], ["scheduledStart", "Scheduled Start"],
  ["scheduledEnd", "Scheduled End"], ["slaWindowStart", "SLA Start"], ["slaWindowEnd", "SLA End"],
  ["requiredVehicleType", "Required Vehicle Type"], ["requiredDriverCertification", "Required Driver Certification"],
  ["assignedDriverId", "Assigned Driver ID"], ["assignedVehicleId", "Assigned Vehicle ID"], ["routeId", "Route ID"], ["notes", "Notes"],
];

// Lifecycle pipeline — drives both the status filter strip and KPI distribution.
const PIPELINE: { label: string; summaryKey: string; statusValue: string }[] = [
  { label: "Unassigned", summaryKey: "unassignedJobs", statusValue: "Unassigned" },
  { label: "Assigned", summaryKey: "assignedJobs", statusValue: "Assigned" },
  { label: "En Route", summaryKey: "enRoute", statusValue: "En Route" },
  { label: "At Stop", summaryKey: "atStop", statusValue: "At Stop" },
  { label: "Completed", summaryKey: "completed", statusValue: "Completed" },
  { label: "Delayed", summaryKey: "delayed", statusValue: "Delayed" },
  { label: "SLA Risk", summaryKey: "slaAtRisk", statusValue: "SLA At Risk" },
];

type Toast = { kind: "success" | "error" | "info"; message: string };

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
    title: "Order execution cockpit",
    description: "Create, assign, track, prove and audit every job with live SLA, proof, customer update, and assignment logic in one operational surface.",
    exportName: "jobs",
    createLabel: "Create Job",
    tableColumns: ["jobNumber", "customerName", "timeWindow", "driverName", "vehicleCode", "status", "slaStatus", "priority", "proofStatus", "recommendedAction"],
    filterRows: () => true,
  },
  "active-shipments": {
    eyebrow: "Active Shipments",
    title: "Execution in motion",
    description: "Live shipment execution board for in-flight work only: dispatch state, ETA confidence, proof readiness, and customer promise exposure.",
    exportName: "active-shipments",
    createLabel: "Create Shipment Job",
    tableColumns: ["jobNumber", "customerName", "timeWindow", "driverName", "vehicleCode", "status", "slaStatus", "proofStatus", "customerUpdateStatus", "recommendedAction"],
    filterRows: (row) => !/completed|delivered/i.test(String(row.status ?? "")),
  },
  shipments: {
    eyebrow: "Shipments",
    title: "Shipment lifecycle register",
    description: "Full shipment entity register from assignment through delivered proof, with stops, customer communication, and audit continuity tied to the underlying job.",
    exportName: "shipments",
    createLabel: "Create Shipment Job",
    tableColumns: ["jobNumber", "customerName", "trackingCode", "driverName", "vehicleCode", "status", "slaStatus", "proofStatus", "customerUpdateStatus", "recommendedAction"],
    filterRows: () => true,
  },
};

export function JobsPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("All");
  const [priority, setPriority] = useState("All");
  const [toast, setToast] = useState<Toast | null>(null);
  const notify = (kind: Toast["kind"], message: string) => setToast({ kind, message });
  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 3200);
    return () => clearTimeout(t);
  }, [toast]);
  const JOBS_PAGE_SIZE = 50;
  const [jobsOffset, setJobsOffset] = useState(0);
  // Server-side paginated + searched — never fetches all 4000+ jobs at once.
  const jobsPaged = useQuery({
    queryKey: ["jobs", "paged", query.trim(), jobsOffset],
    queryFn: () => jobsApi.listPaged({ limit: JOBS_PAGE_SIZE, offset: jobsOffset, search: query }),
  });
  const jobs = { data: jobsPaged.data?.rows ?? [], isLoading: jobsPaged.isLoading, isError: jobsPaged.isError, isFetching: jobsPaged.isFetching, error: jobsPaged.error };
  const jobsTotal = jobsPaged.data?.total ?? (jobsPaged.data?.rows?.length ?? 0);
  const summary = useJobSummary();
  const detail = useJobDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();
  const hasPermission = useHasPermission();
  const { session } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const surface = ((): ShipmentSurface => {
    if (location.pathname === "/active-shipments") return "active-shipments";
    if (location.pathname === "/shipments") return "shipments";
    return "jobs";
  })();
  const surfaceConfig = SURFACE_CONFIG[surface];
  const canManage = hasPermission("shipments:create") || hasPermission("shipments:update") || hasPermission("dispatch:update") || hasPermission("dispatch:assign");
  const canExport = hasPermission("shipments:export") || hasPermission("shipments:view") || canManage;
  const canCreate = canManage;
  const canEdit = canManage;
  const canDelete = canManage;
  const canDispatch = hasPermission("dispatch:assign") || hasPermission("dispatch:update") || canManage;
  const scopedRows = useMemo(() => scopeRowsForSession("jobs", jobs.data || [], session), [jobs.data, session]);
  const visibleSummary = useMemo(() => buildJobSummary(scopedRows, summary.data as AnyRecord | undefined, session), [scopedRows, session, summary.data]);

  const save = useMutation({
    mutationFn: (payload: AnyRecord) => payload.id ? jobsApi.update(String(payload.id), payload) : jobsApi.create(payload),
    onSuccess: async (_d, payload) => { setEditing(null); await qc.invalidateQueries({ queryKey: ["jobs"] }); notify("success", payload.id ? "Job updated" : "Job created"); },
    onError: () => notify("error", "Could not save the job. Please try again."),
  });
  const remove = useMutation({
    mutationFn: (id: string | number) => jobsApi.remove(id),
    onSuccess: async () => { setSelected(null); await qc.invalidateQueries({ queryKey: ["jobs"] }); notify("success", "Job archived"); },
    onError: () => notify("error", "Could not archive the job."),
  });
  const action = useMutation({
    mutationFn: ({ type, id }: { type: string; id: string | number }) => type === "eta" ? jobsApi.sendEta(id) : jobsApi.proofPlaceholder(id),
    onSuccess: async (_d, vars) => {
      await qc.invalidateQueries({ queryKey: ["jobs"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "summary"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "detail", selected?.id] });
      await qc.invalidateQueries({ queryKey: ["pod"] });
      await qc.invalidateQueries({ queryKey: ["pod", "summary"] });
      notify("success", vars.type === "eta" ? "Customer ETA update sent" : "POD workflow queued");
      if (vars.type === "proof") {
        navigate(`/proof-of-delivery?jobId=${vars.id}`);
      }
    },
    onError: () => notify("error", "The action could not be completed."),
  });
  const changeStatus = useMutation({
    mutationFn: ({ id, status: next }: { id: string | number; status: string }) => jobsApi.changeStatus(id, next),
    onSuccess: async (_d, vars) => {
      await qc.invalidateQueries({ queryKey: ["jobs"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "summary"] });
      await qc.invalidateQueries({ queryKey: ["jobs", "detail", selected?.id] });
      notify("success", `Job advanced to ${vars.status}`);
    },
    onError: () => notify("error", "Status change was rejected."),
  });

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
      || (status === "SLA At Risk" && (row.slaStatus === "At Risk" || /At Risk/i.test(String(row.status))));
    const matchesPriority = priority === "All" || String(row.priority) === priority;
    return matchesText && matchesStatus && matchesPriority;
  }), [baseRows, priority, query, status]);

  useEffect(() => {
    if (selected && !rows.some((row) => String(row.id) === String(selected.id))) {
      setSelected(null);
    }
  }, [rows, selected]);

  if (jobs.isLoading) return <LoadingState />;
  if (jobs.isError) return <ErrorState message={jobs.error instanceof Error ? jobs.error.message : "Unable to load jobs."} />;

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
    <div className="space-y-6">
      <Toaster toast={toast} onClose={() => setToast(null)} />

      <PageHeader
        eyebrow={surfaceConfig.eyebrow}
        title={surfaceConfig.title}
        description={surfaceConfig.description}
        actions={<>
          <button type="button" className="btn-primary" disabled={!canCreate} title={!canCreate ? "You do not have permission to perform this action." : undefined} onClick={() => canCreate && setEditing({ priority: "Normal", jobType: "Delivery", status: "Unassigned" })}><Plus className="h-4 w-4" /> {surfaceConfig.createLabel}</button>
          <button type="button" className="btn-ghost" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : undefined} onClick={() => canExport && exportCsv(surfaceConfig.exportName, rows)}><Download className="h-4 w-4" /> Export Roster</button>
        </>}
      />

      {/* Ops intelligence bar — derived from live data, one-click triage */}
      <div className="anim-fade-up flex flex-col gap-3 overflow-hidden rounded-2xl border border-slate-200 bg-gradient-to-r from-slate-900 to-slate-800 p-4 text-white sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-white/10"><Sparkles className="h-5 w-5 text-teal-300" /></span>
          <div>
            <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-teal-300">Live operations signal</p>
            <p className="mt-0.5 text-sm font-medium text-slate-100">
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
          <PipelineChip label="All" count={String(visibleSummary.totalJobsToday ?? scopedRows.length)} active={status === "All"} onClick={() => setStatus("All")} />
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
          <input className="field h-10 pl-9" value={query} onChange={(e) => { setQuery(e.target.value); setJobsOffset(0); }} placeholder="Search jobs, customers, drivers, addresses..." />
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
        onClose={() => setSelected(null)}
        onEdit={(record) => canEdit && setEditing(record)}
        onDelete={(id) => canDelete && remove.mutate(id)}
        onEta={(id) => canDispatch && action.mutate({ type: "eta", id })}
        onProof={(id) => canDispatch && action.mutate({ type: "proof", id })}
        onStatus={(id, next) => canDispatch && changeStatus.mutate({ id, status: next })}
        statusPending={changeStatus.isPending}
        onExport={() => exportJobRecordCsv(selectedRecord(detail.data, selected), detail.data)}
        canEdit={canEdit} canDelete={canDelete} canDispatch={canDispatch} canExport={canExport}
      />
      {editing ? <JobModal initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(payload) => save.mutate(payload)} /> : null}
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
      className={`flex flex-col items-start rounded-xl border px-3 py-2.5 text-left transition ${active ? "border-teal-300 bg-teal-50 shadow-sm" : "border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50"}`}
    >
      <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">{label}</span>
      <span className={`mt-0.5 text-xl font-bold tabular-nums ${active ? "text-teal-700" : accent}`}>{count}</span>
    </button>
  );
}

function JobDrawer({ detail, loading, onClose, onEdit, onEta, onProof, onStatus, statusPending, onDelete, onExport, canEdit, canDelete, canDispatch, canExport }: { detail?: AnyRecord; loading: boolean; onClose: () => void; onEdit: (record: AnyRecord) => void; onEta: (id: string | number) => void; onProof: (id: string | number) => void; onStatus: (id: string | number, status: string) => void; statusPending: boolean; onDelete: (id: string | number) => void; onExport: () => void; canEdit: boolean; canDelete: boolean; canDispatch: boolean; canExport: boolean }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;
  const transitions = nextStatuses(String(record.status ?? ""));
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in">
      <aside className="anim-slide-right flex h-full w-full max-w-3xl flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-2xl">
        {/* Sticky header */}
        <div className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="section-title text-teal-700">Job Detail</p>
              <h2 className="mt-1 text-2xl font-bold text-slate-900">{String(record.jobNumber || record.jobCode)}</h2>
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
            <button type="button" className="btn-ghost h-9 py-0" disabled={!canDispatch} title={!canDispatch ? "You do not have permission to perform this action." : undefined} onClick={() => canDispatch && onEta(String(record.id))}><Send className="h-4 w-4" /> Send ETA</button>
            <button type="button" className="btn-ghost h-9 py-0" disabled={!canDispatch} title={!canDispatch ? "You do not have permission to perform this action." : undefined} onClick={() => canDispatch && onProof(String(record.id))}><FileCheck2 className="h-4 w-4" /> Queue POD</button>
            <button type="button" className="btn-ghost h-9 py-0" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : undefined} onClick={() => canExport && onExport()}><Download className="h-4 w-4" /> Export</button>
            <button type="button" className="btn-ghost h-9 py-0 text-red-600" disabled={!canDelete} title={!canDelete ? "You do not have permission to perform this action." : undefined} onClick={() => canDelete && onDelete(String(record.id))}><Trash2 className="h-4 w-4" /> Delete</button>
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
                  <button
                    key={next}
                    type="button"
                    className="btn-ghost h-9 py-0"
                    disabled={!canDispatch || statusPending}
                    title={!canDispatch ? "You do not have permission to perform this action." : `Move job to ${next}`}
                    onClick={() => canDispatch && onStatus(String(record.id), next)}
                  >
                    <ArrowRight className="h-4 w-4 text-teal-600" /> {next}
                  </button>
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
            <Panel title="Costs & Margin" record={(detail?.costs as AnyRecord) || {}} keys={["revenueEstimate", "costEstimate", "marginEstimate", "marginRisk"]} format="currency" />
          </div>

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

function JobModal({ initial, saving, onClose, onSave }: { initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (event: FormEvent) => { event.preventDefault(); onSave(form); };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in">
      <form className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6 shadow-2xl" onSubmit={submit}>
        <div className="flex items-center justify-between border-b border-slate-200 pb-4">
          <h2 className="text-2xl font-bold text-slate-900">{form.id ? "Edit Job" : "Create Job"}</h2>
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
    Unassigned: ["Assigned"],
    Assigned: ["En Route", "Delayed", "Exception"],
    "En Route": ["At Stop", "Delayed", "Exception"],
    "In Progress": ["At Stop", "Delayed", "Exception"],
    "At Stop": ["Completed", "Delayed", "Exception"],
    Delayed: ["En Route", "At Stop", "Completed", "Exception"],
    "At Risk": ["En Route", "At Stop", "Completed", "Exception"],
    Exception: ["Assigned", "En Route", "At Stop", "Completed"],
    Completed: ["Delivered"],
  };
  return flow[current] ?? ["Assigned", "En Route", "At Stop", "Completed", "Delayed", "Exception"];
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
  exportCsv(`job-${String(record.jobNumber ?? record.jobCode ?? record.id ?? "report")}`, rows);
}

function selectedRecord(detail: AnyRecord | undefined, selected: AnyRecord | null) {
  return (detail?.record as AnyRecord | undefined) ?? selected;
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
