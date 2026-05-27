import { FormEvent, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, Download, Edit3, FileText, Plus, Save, Search, ShieldCheck, Sparkles, Target, Trash2, X } from "lucide-react";
import { AiInsightCard, DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import { assetsApi } from "@/services/assetsApi";
import { customersApi } from "@/services/customersApi";
import { driversApi } from "@/services/driversApi";
import { jobsApi } from "@/services/jobsApi";
import { vehiclesApi } from "@/services/vehiclesApi";
import type { AnyRecord } from "@/types";

type EntityKind = "vehicles" | "drivers" | "jobs" | "customers" | "assets";

type Field = {
  key: string;
  label: string;
  required?: boolean;
  type?: "text" | "number" | "select";
  options?: string[];
};

type EntityApi = {
  list: () => Promise<AnyRecord[]>;
  summary: () => Promise<AnyRecord>;
  detail: (id: string | number) => Promise<AnyRecord>;
  recommendations: (id: string | number) => Promise<AnyRecord[]>;
  create?: (payload: AnyRecord) => Promise<AnyRecord>;
  update?: (id: string | number, payload: AnyRecord) => Promise<AnyRecord>;
  remove?: (id: string | number) => Promise<AnyRecord>;
};

type EntityConfig = {
  title: string;
  eyebrow: string;
  description: string;
  columns: string[];
  api: EntityApi;
  fields: Field[];
  defaults: AnyRecord;
  kpis: string[][];
  wow: string[];
  detailSections: [string, string, string[]][];
};

const config: Record<EntityKind, EntityConfig> = {
  vehicles: {
    title: "Vehicles",
    eyebrow: "Fleet Master Data",
    description: "Fleet registry with readiness, risk heat, device/camera health, maintenance, compliance, cost, documents, safety events and AI recommendations.",
    columns: ["vehicleCode", "type", "make", "model", "plateNumber", "status", "fleetReadinessScore", "dataQualityScore", "riskHeatScore", "assignedDriver"],
    api: vehiclesApi,
    fields: [
      { key: "vehicleCode", label: "Vehicle Code", required: true },
      { key: "type", label: "Type", required: true, type: "select", options: ["Truck", "Van", "Box Truck", "Reefer"] },
      { key: "make", label: "Make" },
      { key: "model", label: "Model" },
      { key: "year", label: "Year", type: "number" },
      { key: "vin", label: "VIN" },
      { key: "plateNumber", label: "Plate Number" },
      { key: "status", label: "Status", type: "select", options: ["Available", "On Route", "At Stop", "Idle", "Delayed", "Maintenance"] },
    ],
    defaults: { type: "Truck", status: "Available" },
    kpis: [
      ["Fleet Readiness", "fleetReadinessScore", "%"],
      ["Data Completeness", "dataCompletenessScore", "%"],
      ["At Risk", "atRisk", ""],
      ["Device Exceptions", "deviceExceptions", ""],
    ],
    wow: ["Vehicle Risk Heat Score", "Recommended Action", "Smart Driver Assignment Suggestion", "Fleet Readiness Score", "Data Completeness Score"],
    detailSections: [
      ["Maintenance Summary", "maintenance", ["title", "category", "status", "riskLevel", "dueDate"]],
      ["Compliance Summary", "compliance", ["documentName", "documentType", "status", "expiryDate"]],
      ["Documents", "documents", ["documentName", "documentType", "status", "expiryDate"]],
      ["Trips Placeholder", "trips", ["status", "startedAt", "completedAt"]],
      ["Safety Events", "safetyEvents", ["eventType", "severity", "reviewStatus", "eventTime"]],
      ["Audit Trail", "auditTrail", ["actionName", "actorName", "createdAt"]],
    ],
  },
  drivers: {
    title: "Drivers",
    eyebrow: "Driver Operations",
    description: "Driver readiness, risk heat, HOS posture, certifications, DVIR, safety score, coaching queue, assigned vehicle and audit trail.",
    columns: ["driverCode", "fullName", "phone", "licenseNumber", "status", "driverReadinessScore", "safetyScore", "complianceScore", "riskHeatScore", "assignedVehicle"],
    api: driversApi,
    fields: [
      { key: "driverCode", label: "Driver Code", required: true },
      { key: "fullName", label: "Full Name", required: true },
      { key: "phone", label: "Phone" },
      { key: "email", label: "Email" },
      { key: "licenseNumber", label: "License Number" },
      { key: "status", label: "Status", type: "select", options: ["Available", "On Route", "At Stop", "Idle", "Delayed", "Suspended"] },
    ],
    defaults: { status: "Available" },
    kpis: [
      ["Driver Readiness", "driverReadinessScore", "%"],
      ["Compliance/Data", "dataCompletenessScore", "%"],
      ["Safety Score", "safetyScore", ""],
      ["At Risk", "atRisk", ""],
    ],
    wow: ["Driver Risk Heat Score", "Recommended Action", "Smart Vehicle Assignment Suggestion", "Driver Readiness Score", "Compliance/Data Completeness Score"],
    detailSections: [
      ["License / Certifications", "certifications", ["certificationType", "status", "expiryDate"]],
      ["Documents", "documents", ["documentName", "documentType", "status", "expiryDate"]],
      ["HOS Status", "hos", ["logDate", "drivingHours", "onDutyHours", "cycleHoursLeft", "status"]],
      ["DVIR Status", "inspections", ["inspectionType", "result", "createdAt"]],
      ["Safety / Coaching Queue", "safetyEvents", ["eventType", "severity", "reviewStatus", "eventTime"]],
      ["Audit Trail", "auditTrail", ["actionName", "actorName", "createdAt"]],
    ],
  },
  customers: {
    title: "Clients / Customers",
    eyebrow: "Customer Operations",
    description: "Customer profiles with contacts, addresses, active jobs, SLA health, communication history, contracts, ETA history and AI recommendations.",
    columns: ["customerCode", "name", "contactName", "email", "status", "slaTier", "slaHealthScore", "deliveryExperienceScore", "activeJobs", "riskHeatScore"],
    api: customersApi,
    fields: [
      { key: "customerCode", label: "Customer Code", required: true },
      { key: "name", label: "Customer Name", required: true },
      { key: "contactName", label: "Primary Contact" },
      { key: "email", label: "Email" },
      { key: "phone", label: "Phone" },
      { key: "billingAddress", label: "Billing Address" },
      { key: "shippingAddress", label: "Shipping Address" },
      { key: "status", label: "Status", type: "select", options: ["Active", "At Risk", "On Hold"] },
      { key: "slaTier", label: "SLA Tier", type: "select", options: ["Standard", "Gold", "Platinum"] },
    ],
    defaults: { status: "Active", slaTier: "Standard" },
    kpis: [
      ["SLA Health", "slaHealthScore", "%"],
      ["Delivery Experience", "deliveryExperienceScore", "%"],
      ["At Risk Watch", "atRisk", ""],
      ["Platinum Accounts", "platinumAccounts", ""],
    ],
    wow: ["Customer SLA Health Score", "At-Risk Customer Watch", "Recommended Customer Update", "Customer Delivery Experience Score"],
    detailSections: [
      ["Contact Info", "contacts", ["fullName", "title", "email", "phone", "isPrimary"]],
      ["Billing / Shipping Addresses", "addresses", ["addressType", "addressLine", "city", "state", "postalCode"]],
      ["Active Jobs", "activeJobs", ["jobCode", "jobType", "status", "priority", "scheduledStart"]],
      ["Communication History", "communications", ["channel", "message", "status", "sentAt"]],
      ["Contract / Rate Summary", "contracts", ["contractCode", "title", "rateType", "status", "expirationDate"]],
      ["Customer ETA History", "etaHistory", ["message", "channel", "status", "sentAt"]],
      ["Audit Trail", "auditTrail", ["actionName", "actorName", "createdAt"]],
    ],
  },
  assets: {
    title: "Assets / Trailers / Equipment",
    eyebrow: "Asset Control",
    description: "Asset registry with utilization, geofence posture, assignments, maintenance status, documents, movement history and AI recommendations.",
    columns: ["assetCode", "assetType", "name", "status", "currentLocation", "assignedVehicle", "assignedDriver", "customerName", "utilizationScore", "geofenceRiskBadge"],
    api: assetsApi,
    fields: [
      { key: "assetCode", label: "Asset Code", required: true },
      { key: "assetType", label: "Asset Type", required: true, type: "select", options: ["Trailer", "Generator", "Reefer Unit", "Equipment"] },
      { key: "name", label: "Asset Name", required: true },
      { key: "status", label: "Status", type: "select", options: ["Available", "Assigned", "Maintenance", "At Risk"] },
      { key: "currentLocation", label: "Current Location" },
      { key: "currentZone", label: "Current Zone" },
      { key: "geofenceStatus", label: "Geofence Status", type: "select", options: ["Inside authorized zone", "Outside authorized zone", "Unknown"] },
      { key: "utilizationScore", label: "Utilization Score", type: "number" },
      { key: "riskScore", label: "Risk Score", type: "number" },
    ],
    defaults: { assetType: "Trailer", status: "Available", geofenceStatus: "Inside authorized zone", utilizationScore: 80, riskScore: 12 },
    kpis: [
      ["Utilization", "utilizationScore", "%"],
      ["Geofence Watch", "geofenceExceptions", ""],
      ["Unassigned", "unassigned", ""],
      ["At Risk", "atRisk", ""],
    ],
    wow: ["Asset Utilization Score", "Lost Asset / Unauthorized Movement Watch", "Geofence Risk Badge", "Smart Assignment Suggestion"],
    detailSections: [
      ["Documents", "documents", ["documentName", "documentType", "status", "expiryDate"]],
      ["Movement History Placeholder", "movementHistory", ["eventType", "title", "severity", "eventTime", "createdAt"]],
      ["Audit Trail", "auditTrail", ["actionName", "actorName", "createdAt"]],
    ],
  },
  jobs: {
    title: "Jobs & Orders",
    eyebrow: "Dispatch Workflow",
    description: "Customer jobs with pickup/drop-off, SLA/ETA, dispatch assignment, proof of delivery, communications and AI recommendations.",
    columns: ["jobCode", "customerName", "jobType", "pickupAddress", "dropoffAddress", "status", "priority", "vehicleCode", "driverName"],
    api: jobsApi,
    fields: [],
    defaults: {},
    kpis: [["Total Records", "total", ""], ["Active", "active", ""], ["At Risk", "atRisk", ""], ["AI Signals", "aiSignals", ""]],
    wow: [],
    detailSections: [],
  },
};

export function EntityListPage({ kind }: { kind: EntityKind }) {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [statusFilter, setStatusFilter] = useState("All");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const cfg = config[kind];
  const queryClient = useQueryClient();

  const list = useQuery({ queryKey: [kind], queryFn: cfg.api.list });
  const summary = useQuery({ queryKey: [kind, "summary"], queryFn: cfg.api.summary });
  const detail = useQuery({
    queryKey: [kind, "detail", selected?.id],
    queryFn: () => cfg.api.detail(String(selected?.id)),
    enabled: Boolean(selected?.id),
  });
  const selectedDetail = detail.data;
  const selectedRecord = (selectedDetail?.record as AnyRecord | undefined) || selected;
  const recommendations = (selectedDetail?.recommendations as AnyRecord[] | undefined) || [];

  const saveMutation = useMutation({
    mutationFn: (payload: AnyRecord) => payload.id && !isCreating ? cfg.api.update!(String(payload.id), payload) : cfg.api.create!(payload),
    onSuccess: async () => {
      setEditing(null);
      setIsCreating(false);
      await queryClient.invalidateQueries({ queryKey: [kind] });
    },
  });
  const deleteMutation = useMutation({
    mutationFn: (id: string | number) => cfg.api.remove!(id),
    onSuccess: async () => {
      setSelected(null);
      await queryClient.invalidateQueries({ queryKey: [kind] });
    },
  });

  const rows = useMemo(() => {
    const source = list.data || [];
    return source.filter((row) => {
      const matchesStatus = statusFilter === "All" || String(row.status || "").toLowerCase().includes(statusFilter.toLowerCase()) || (statusFilter === "At Risk" && /high|medium|risk|delayed|maintenance/i.test(JSON.stringify(row)));
      const matchesSearch = !search.trim() || JSON.stringify(row).toLowerCase().includes(search.toLowerCase());
      return matchesStatus && matchesSearch;
    });
  }, [list.data, search, statusFilter]);

  if (list.isLoading) return <LoadingState />;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow={cfg.eyebrow}
        title={cfg.title}
        description={cfg.description}
        actions={
          <>
            {cfg.api.create ? <button className="btn-primary" onClick={() => { setIsCreating(true); setEditing({ ...cfg.defaults }); }}><Plus className="h-4 w-4" /> Create</button> : null}
            <button className="btn-ghost" onClick={() => exportRows(kind, rows)}><Download className="h-4 w-4" /> Export CSV</button>
          </>
        }
      />

      <div className="grid gap-4 md:grid-cols-4">
        {cfg.kpis.map(([label, key, suffix]) => (
          <KpiCard key={label} label={label} value={`${summary.data?.[key] ?? (key === "aiSignals" ? recommendations.length || "Select" : 0)}${suffix}`} icon={<Target />} status={Number(summary.data?.[key] ?? 0) > 0 && /risk|exception|watch/i.test(label) ? "Review" : "Healthy"} />
        ))}
      </div>

      <div className="panel flex flex-col gap-3 p-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="relative max-w-xl flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" />
          <input value={search} onChange={(event) => setSearch(event.target.value)} className="field pl-10" placeholder={`Search ${cfg.title.toLowerCase()}...`} />
        </div>
        <div className="flex flex-wrap gap-2">
          {["All", "Active", "Available", "At Risk", "Maintenance"].map((item) => (
            <button key={item} className={statusFilter === item ? "btn-primary" : "btn-ghost"} onClick={() => setStatusFilter(item)}>{item}</button>
          ))}
        </div>
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_380px]">
        <DataTable rows={rows} columns={cfg.columns} onSelect={setSelected} />
        <div className="space-y-4">
          <div className="panel p-5">
            <div className="flex items-center gap-2 text-teal-200"><Sparkles className="h-4 w-4" /><span className="section-title">Batch 1 Intelligence</span></div>
            <div className="mt-4 flex flex-wrap gap-2">
              {cfg.wow.map((item) => <span key={item} className="badge">{item}</span>)}
            </div>
          </div>
          {(recommendations.length ? recommendations : [{ title: "Select a record", body: "Open a row to inspect detail evidence, timeline, recommendations, documents, assignments and audit trail." }]).slice(0, 3).map((item, i) => (
            <AiInsightCard key={String(item.id || i)} insight={item} />
          ))}
        </div>
      </div>

      <BatchDetailDrawer
        kind={kind}
        config={cfg}
        detail={selectedDetail}
        record={selectedRecord}
        loading={detail.isLoading}
        onClose={() => setSelected(null)}
        onEdit={(record) => { setIsCreating(false); setEditing(record); }}
        onDelete={(record) => cfg.api.remove && deleteMutation.mutate(String(record.id))}
      />

      {editing ? (
        <CreateEditModal
          title={`${isCreating ? "Create" : "Edit"} ${cfg.title}`}
          fields={cfg.fields}
          initial={editing}
          saving={saveMutation.isPending}
          onClose={() => { setEditing(null); setIsCreating(false); }}
          onSave={(payload) => saveMutation.mutate(payload)}
        />
      ) : null}
    </div>
  );
}

function BatchDetailDrawer({ config: cfg, detail, record, loading, onClose, onEdit, onDelete }: {
  kind: EntityKind;
  config: EntityConfig;
  detail?: AnyRecord;
  record: AnyRecord | null;
  loading: boolean;
  onClose: () => void;
  onEdit: (record: AnyRecord) => void;
  onDelete: (record: AnyRecord) => void;
}) {
  if (!record) return null;
  const timeline = (detail?.timeline as AnyRecord[] | undefined) || [];
  const recommendations = (detail?.recommendations as AnyRecord[] | undefined) || [];

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm">
      <aside className="h-full w-full max-w-3xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6 shadow-2xl">
        <button className="float-right rounded-lg p-2 text-slate-400 hover:bg-white/10 hover:text-white" onClick={onClose}><X className="h-5 w-5" /></button>
        <p className="text-xs font-bold uppercase tracking-[0.25em] text-teal-300">OpsTrax Batch 1 Detail</p>
        <h2 className="mt-3 text-2xl font-semibold text-white">{String(record.title || record.name || record.vehicleCode || record.driverCode || record.customerCode || record.assetCode || `Record ${record.id}`)}</h2>
        <div className="mt-4 flex flex-wrap gap-2">
          <StatusBadge status={record.status} />
          <RiskBadge risk={record.riskHeatScore || record.geofenceRiskBadge || record.riskScore || "Low"} />
          <span className="badge"><Bot className="h-4 w-4" /> {String(record.recommendedAction || "AI monitoring active")}</span>
        </div>
        <div className="mt-5 flex gap-3">
          <button className="btn-primary" onClick={() => onEdit(record)}><Edit3 className="h-4 w-4" /> Edit</button>
          <button className="btn-ghost" onClick={() => onDelete(record)}><Trash2 className="h-4 w-4" /> Delete</button>
          <button className="btn-ghost"><FileText className="h-4 w-4" /> Report Placeholder</button>
        </div>

        <div className="mt-6 grid gap-3 sm:grid-cols-2">
          {Object.entries(record).slice(0, 18).map(([key, value]) => (
            <div key={key} className="rounded-xl border border-white/10 bg-white/[0.03] p-3">
              <p className="text-[11px] uppercase tracking-[0.18em] text-slate-500">{labelize(key)}</p>
              <p className="mt-1 break-words text-sm text-slate-200">{String(value ?? "--")}</p>
            </div>
          ))}
        </div>

        <div className="mt-6 grid gap-4 lg:grid-cols-2">
          {recommendations.slice(0, 4).map((item, index) => <AiInsightCard key={String(item.id || index)} insight={item} />)}
        </div>

        <Section title="Timeline" rows={timeline} columns={["eventType", "title", "severity", "eventTime"]} loading={loading} />
        {cfg.detailSections.map(([title, key, columns]) => (
          <Section key={title} title={title} rows={(detail?.[key] as AnyRecord[] | undefined) || []} columns={columns} loading={loading} />
        ))}
      </aside>
    </div>
  );
}

function Section({ title, rows, columns, loading }: { title: string; rows: AnyRecord[]; columns: string[]; loading?: boolean }) {
  return (
    <section className="mt-6 rounded-2xl border border-white/10 bg-white/[0.03] p-4">
      <h3 className="section-title">{title}</h3>
      {loading ? <p className="mt-3 text-sm text-slate-400">Loading evidence...</p> : null}
      {!loading && !rows.length ? <p className="mt-3 text-sm text-slate-500">No linked records yet.</p> : null}
      {rows.length ? (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[520px] text-left text-sm">
            <thead className="text-xs uppercase tracking-[0.16em] text-slate-500">
              <tr>{columns.map((column) => <th key={column} className="px-3 py-2 font-semibold">{labelize(column)}</th>)}</tr>
            </thead>
            <tbody className="divide-y divide-white/10">
              {rows.slice(0, 8).map((row, index) => (
                <tr key={String(row.id || index)} className="text-slate-300">
                  {columns.map((column) => <td key={column} className="px-3 py-2">{String(row[column] ?? "--")}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}

function CreateEditModal({ title, fields, initial, saving, onClose, onSave }: {
  title: string;
  fields: Field[];
  initial: AnyRecord;
  saving: boolean;
  onClose: () => void;
  onSave: (payload: AnyRecord) => void;
}) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSave(form);
  };

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4 backdrop-blur-sm">
      <form onSubmit={submit} className="panel w-full max-w-2xl p-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.25em] text-teal-300">OpsTrax Create / Edit</p>
            <h2 className="mt-2 text-2xl font-semibold text-white">{title}</h2>
          </div>
          <button type="button" className="icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-6 grid gap-4 md:grid-cols-2">
          {fields.map((field) => (
            <label key={field.key} className="block">
              <span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{field.label}</span>
              {field.type === "select" ? (
                <select className="field" value={String(form[field.key] ?? "")} required={field.required} onChange={(event) => setForm((current) => ({ ...current, [field.key]: event.target.value }))}>
                  <option value="">Select</option>
                  {field.options?.map((option) => <option key={option} value={option}>{option}</option>)}
                </select>
              ) : (
                <input className="field" type={field.type || "text"} value={String(form[field.key] ?? "")} required={field.required} onChange={(event) => setForm((current) => ({ ...current, [field.key]: field.type === "number" ? Number(event.target.value) : event.target.value }))} />
              )}
            </label>
          ))}
        </div>
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={saving}><Save className="h-4 w-4" /> {saving ? "Saving..." : "Save"}</button>
        </div>
      </form>
    </div>
  );
}

function exportRows(kind: string, rows: AnyRecord[]) {
  const columns = Array.from(new Set(rows.flatMap((row) => Object.keys(row)))).slice(0, 24);
  const csv = [columns.join(","), ...rows.map((row) => columns.map((column) => JSON.stringify(row[column] ?? "")).join(","))].join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `opstrax-${kind}-export.csv`;
  anchor.click();
  URL.revokeObjectURL(url);
}
