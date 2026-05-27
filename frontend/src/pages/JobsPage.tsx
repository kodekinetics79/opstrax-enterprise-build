import { FormEvent, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Edit3, FileCheck2, Plus, RadioTower, Route, Send, X } from "lucide-react";
import { AiInsightCard, DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import { useJobDetail, useJobs, useJobSummary } from "@/hooks/useBatch2";
import { jobsApi } from "@/services/jobsApi";
import type { AnyRecord } from "@/types";

const fields = [
  ["jobNumber", "Job Number"], ["customerId", "Customer ID"], ["jobType", "Job Type"], ["priority", "Priority"],
  ["pickupAddress", "Pickup Address"], ["dropoffAddress", "Drop-off Address"], ["scheduledStart", "Scheduled Start"],
  ["scheduledEnd", "Scheduled End"], ["slaWindowStart", "SLA Start"], ["slaWindowEnd", "SLA End"],
  ["requiredVehicleType", "Required Vehicle Type"], ["requiredDriverCertification", "Required Driver Certification"],
  ["assignedDriverId", "Assigned Driver ID"], ["assignedVehicleId", "Assigned Vehicle ID"], ["routeId", "Route ID"], ["notes", "Notes"],
];

export function JobsPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("All");
  const [priority, setPriority] = useState("All");
  const jobs = useJobs();
  const summary = useJobSummary();
  const detail = useJobDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();

  const save = useMutation({
    mutationFn: (payload: AnyRecord) => payload.id ? jobsApi.update(String(payload.id), payload) : jobsApi.create(payload),
    onSuccess: async () => { setEditing(null); await qc.invalidateQueries({ queryKey: ["jobs"] }); },
  });
  const action = useMutation({
    mutationFn: ({ type, id }: { type: string; id: string | number }) => type === "eta" ? jobsApi.sendEta(id) : jobsApi.proofPlaceholder(id),
    onSuccess: async () => { await qc.invalidateQueries({ queryKey: ["jobs"] }); await qc.invalidateQueries({ queryKey: ["jobs", "detail", selected?.id] }); },
  });

  const rows = useMemo(() => (jobs.data || []).filter((row) => {
    const matchesText = !query || JSON.stringify(row).toLowerCase().includes(query.toLowerCase());
    const matchesStatus = status === "All" || String(row.status) === status || (status === "SLA At Risk" && row.slaStatus === "At Risk");
    const matchesPriority = priority === "All" || String(row.priority) === priority;
    return matchesText && matchesStatus && matchesPriority;
  }), [jobs.data, priority, query, status]);

  if (jobs.isLoading) return <LoadingState />;
  const s = summary.data || {};

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Jobs & Orders"
        title="Order execution cockpit"
        description="Create, assign, track, communicate, prove and audit every job with SLA risk prediction and customer update intelligence."
        actions={<><button className="btn-primary" onClick={() => setEditing({ priority: "Normal", jobType: "Delivery", status: "Unassigned" })}><Plus className="h-4 w-4" /> Create Job</button><button className="btn-ghost" onClick={() => exportCsv("jobs", rows)}><Download className="h-4 w-4" /> Export Roster</button></>}
      />
      <div className="grid gap-4 md:grid-cols-4 xl:grid-cols-6">
        {[
          ["Total Jobs Today", "totalJobsToday"], ["Unassigned Jobs", "unassignedJobs"], ["Assigned Jobs", "assignedJobs"], ["En Route", "enRoute"],
          ["At Stop", "atStop"], ["Completed", "completed"], ["Delayed", "delayed"], ["SLA At Risk", "slaAtRisk"],
          ["Proof Pending", "proofPending"], ["Updates Sent", "customerUpdatesSent"], ["Avg ETA Accuracy", "averageEtaAccuracy"], ["Revenue / Margin", "revenueMarginPlaceholder"],
        ].map(([label, key]) => <KpiCard key={key} label={label} value={String(s[key] ?? 0)} icon={<RadioTower />} status={/Delayed|Risk|Pending/.test(label) ? "Review" : "Active"} />)}
      </div>
      <div className="panel flex flex-col gap-3 p-4 xl:flex-row xl:items-center">
        <input className="field xl:max-w-md" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search jobs, customers, regions, proof, SLA..." />
        <select className="field xl:max-w-[180px]" value={status} onChange={(e) => setStatus(e.target.value)}><option>All</option><option>Unassigned</option><option>Assigned</option><option>En Route</option><option>At Stop</option><option>Completed</option><option>Delayed</option><option>SLA At Risk</option></select>
        <select className="field xl:max-w-[160px]" value={priority} onChange={(e) => setPriority(e.target.value)}><option>All</option><option>Low</option><option>Normal</option><option>High</option><option>Critical</option></select>
        <span className="badge">Filters: customer, driver, vehicle, region, dates, proof status are API-ready placeholders</span>
      </div>
      <DataTable rows={rows} columns={["jobNumber", "customerName", "jobType", "pickupAddress", "dropoffAddress", "timeWindow", "driverName", "vehicleCode", "status", "eta", "slaStatus", "priority", "proofStatus", "riskHeatScore", "recommendedAction"]} onSelect={setSelected} />
      <JobDrawer detail={detail.data} loading={detail.isLoading} onClose={() => setSelected(null)} onEdit={(record) => setEditing(record)} onEta={(id) => action.mutate({ type: "eta", id })} onProof={(id) => action.mutate({ type: "proof", id })} />
      {editing ? <JobModal initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(payload) => save.mutate(payload)} /> : null}
    </div>
  );
}

function JobDrawer({ detail, loading, onClose, onEdit, onEta, onProof }: { detail?: AnyRecord; loading: boolean; onClose: () => void; onEdit: (record: AnyRecord) => void; onEta: (id: string | number) => void; onProof: (id: string | number) => void }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm">
      <aside className="h-full w-full max-w-4xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6">
        <button className="float-right icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        <p className="section-title text-teal-300">OpsTrax Job Detail</p>
        <h2 className="mt-3 text-2xl font-semibold text-white">{String(record.jobNumber || record.jobCode)}</h2>
        <div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status} /><RiskBadge risk={record.riskHeatScore} /><span className="badge">SLA {String(record.slaStatus)}</span><span className="badge">Proof {String(record.proofStatus)}</span></div>
        <div className="mt-5 flex flex-wrap gap-3"><button className="btn-primary" onClick={() => onEdit(record)}><Edit3 className="h-4 w-4" /> Edit</button><button className="btn-ghost" onClick={() => onEta(String(record.id))}><Send className="h-4 w-4" /> Send ETA</button><button className="btn-ghost" onClick={() => onProof(String(record.id))}><FileCheck2 className="h-4 w-4" /> Proof Placeholder</button><button className="btn-ghost"><Download className="h-4 w-4" /> Job Report</button></div>
        <div className="mt-6 grid gap-4 lg:grid-cols-3">
          <Panel title="Assignment" record={record} keys={["driverName", "vehicleCode", "requiredVehicleType", "requiredDriverCertification", "routeCode"]} />
          <Panel title="Pickup / Drop-off" record={record} keys={["pickupAddress", "dropoffAddress", "scheduledStart", "scheduledEnd"]} />
          <Panel title="SLA & ETA" record={record} keys={["slaWindowStart", "slaWindowEnd", "eta", "slaStatus", "customerUpdateStatus"]} />
          <Panel title="Costs / Margin Placeholder" record={(detail?.costs as AnyRecord) || {}} keys={["revenueEstimate", "costEstimate", "marginEstimate", "marginRisk"]} />
        </div>
        <Grid title="Route / Stops" rows={(detail?.stops as AnyRecord[]) || []} columns={["stopSequence", "stopType", "address", "status", "proofStatus", "eta"]} />
        <Grid title="Proof of Delivery / Service" rows={(detail?.proof as AnyRecord[]) || []} columns={["proofType", "status", "receivedBy", "capturedAt", "notes"]} />
        <Grid title="Customer Communications" rows={(detail?.communications as AnyRecord[]) || []} columns={["channel", "messageType", "message", "status", "sentAt"]} />
        <Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName", "actorName", "createdAt"]} />
        <div className="mt-5 grid gap-4 lg:grid-cols-2">{((detail?.recommendations as AnyRecord[]) || []).slice(0, 4).map((x, i) => <AiInsightCard key={String(x.id || i)} insight={x} />)}</div>
      </aside>
    </div>
  );
}

function JobModal({ initial, saving, onClose, onSave }: { initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (event: FormEvent) => { event.preventDefault(); onSave(form); };
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4"><form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><h2 className="text-2xl font-semibold text-white">{form.id ? "Edit Job" : "Create Job"}</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div><div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key, label]) => <label key={key}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span><input className="field" value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} required={["jobNumber","customerId","pickupAddress","dropoffAddress"].includes(key)} /></label>)}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button className="btn-primary" disabled={saving}>Save Job</button></div></form></div>;
}

function Panel({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2">{keys.map((key) => <p key={key} className="text-sm text-slate-300"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[620px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-white/10">{rows.slice(0, 8).map((row, i) => <tr key={String(row.id || i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-300">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}

function exportCsv(name: string, rows: AnyRecord[]) {
  const cols = Array.from(new Set(rows.flatMap((row) => Object.keys(row)))).slice(0, 24);
  const csv = [cols.join(","), ...rows.map((row) => cols.map((c) => JSON.stringify(row[c] ?? "")).join(","))].join("\n");
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
  a.download = `opstrax-${name}.csv`;
  a.click();
}
