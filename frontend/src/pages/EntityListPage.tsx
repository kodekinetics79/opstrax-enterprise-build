import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Activity, AlertTriangle, Bot, ClipboardCheck, Download, Edit3, FileText, Plus, Save, Search, Sparkles, Target, Trash2, UserCheck, X } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis } from "recharts";
import { AiInsightCard, DataTable, EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { useAuth } from "@/hooks/useAuth";
import { isCustomerPortalRole, isDriverPortalRole, scopeRowsForSession } from "@/auth/accessScope";
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
  painPoints: string[];
  competitiveEdges: string[];
  decisionSignals: string[];
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
    painPoints: ["Unplanned downtime", "Dispatching unavailable units", "Device/camera blind spots", "Expiring documents", "Cost leakage by asset"],
    competitiveEdges: ["Readiness + risk in one score", "Camera/device status visible before dispatch", "Maintenance, safety, fuel and compliance evidence in one drawer", "Smart driver match action", "Audit-ready lifecycle trail"],
    decisionSignals: ["readiness_score", "data_quality_score", "risk_score", "device_status", "camera_status", "assigned_driver"],
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
    painPoints: ["HOS and compliance uncertainty", "Unsafe driver assignment", "Missing certification/document risk", "Coaching not connected to operations", "Manual vehicle pairing"],
    competitiveEdges: ["Readiness score blends safety, compliance and availability", "HOS, DVIR, certifications and safety history are joined in detail", "Smart vehicle match action", "Coaching and incident risk visible before assignment", "Audit-ready driver lifecycle"],
    decisionSignals: ["readiness_score", "safety_score", "compliance_score", "risk_score", "assigned_vehicle", "status"],
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
    painPoints: ["SLA surprise escalations", "Manual customer update follow-up", "Contract/rate context separated from jobs", "ETA history hard to find", "At-risk account drift"],
    competitiveEdges: ["SLA and delivery experience scored together", "ETA history and communications tied to each customer", "Contracts, active jobs and contacts in one view", "AI customer update recommendation", "Customer risk watch for service teams"],
    decisionSignals: ["sla_health_score", "delivery_experience_score", "risk_score", "active_jobs", "sla_tier", "status"],
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
    painPoints: ["Trailer location uncertainty", "Unauthorized movement", "Underutilized assets", "Asset assigned to wrong vehicle/customer", "Maintenance and document gaps"],
    competitiveEdges: ["Geofence risk badge by asset", "Utilization score on every row", "Vehicle, driver and customer assignment in one action", "Movement history and documents in one drawer", "Lost asset watch built into the workflow"],
    decisionSignals: ["utilization_score", "risk_score", "geofence_status", "current_zone", "assigned_vehicle", "assigned_driver"],
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
    painPoints: [],
    competitiveEdges: [],
    decisionSignals: [],
    detailSections: [],
  },
};

export function EntityListPage({ kind }: { kind: EntityKind }) {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [statusFilter, setStatusFilter] = useState("All");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const navigate = useNavigate();
  const hasPermission = useHasPermission();
  const { session } = useAuth();
  const cfg = config[kind];
  const isScopedViewer = Boolean(session && (isDriverPortalRole(String(session.role ?? "")) || isCustomerPortalRole(String(session.role ?? ""))));
  const queryClient = useQueryClient();
  const permissions = permissionMatrix(kind);
  const canCreate = hasPermission(permissions.create);
  const canUpdate = hasPermission(permissions.update);
  const canDelete = hasPermission(permissions.delete);
  const canAssign = hasPermission(permissions.assign);
  const canExport = hasPermission(permissions.export);

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
  const isFleetMaster = kind === "vehicles" || kind === "drivers" || kind === "assets";

  const driverOptions = useQuery({ queryKey: ["drivers", "assignment-options"], queryFn: driversApi.list, enabled: !isScopedViewer && (kind === "vehicles" || kind === "assets") });
  const vehicleOptions = useQuery({ queryKey: ["vehicles", "assignment-options"], queryFn: vehiclesApi.list, enabled: !isScopedViewer && (kind === "drivers" || kind === "assets") });
  const customerOptions = useQuery({ queryKey: ["customers", "assignment-options"], queryFn: customersApi.list, enabled: !isScopedViewer && kind === "assets" });
  const planningInsights = useQuery({ queryKey: ["vehicles", "planning-insights"], queryFn: vehiclesApi.planningInsights, enabled: kind === "vehicles" && !isScopedViewer });
  const scopedRows = useMemo(() => scopeRowsForSession(kind, list.data || [], session), [kind, list.data, session]);
  const visibleSummary = useMemo(() => buildVisibleSummary(kind, scopedRows, summary.data as AnyRecord | undefined, session), [kind, scopedRows, session, summary.data]);

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
  const assignMutation = useMutation({
    mutationFn: async () => {
      if (!selectedRecord?.id) return null;
      if (kind === "vehicles") {
        const driver = pickBestDriver(driverOptions.data || []);
        if (!driver?.id) throw new Error("No available driver found for assignment.");
        return vehiclesApi.assignDriver(String(selectedRecord.id), String(driver.id));
      }
      if (kind === "drivers") {
        const vehicle = pickBestVehicle(vehicleOptions.data || []);
        if (!vehicle?.id) throw new Error("No available vehicle found for assignment.");
        return driversApi.assignVehicle(String(selectedRecord.id), String(vehicle.id));
      }
      if (kind === "assets") {
        const vehicle = pickBestVehicle(vehicleOptions.data || []);
        const driver = pickBestDriver(driverOptions.data || []);
        const customer = (customerOptions.data || [])[0];
        return assetsApi.assign(String(selectedRecord.id), {
          vehicleId: vehicle?.id ?? null,
          driverId: driver?.id ?? null,
          customerId: customer?.id ?? null,
        });
      }
      return null;
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: [kind] });
      if (selectedRecord?.id) await queryClient.invalidateQueries({ queryKey: [kind, "detail", selectedRecord.id] });
    },
  });

  const rows = useMemo(() => {
    const source = scopedRows;
    return source.filter((row) => {
      const qLower = search.toLowerCase();
      const matchesStatus = statusFilter === "All" || 
        String(row.status || "").toLowerCase().includes(statusFilter.toLowerCase()) || 
        (statusFilter === "At Risk" && (Number(row.riskScore || row.risk_score || 0) >= 40 || /maintenance|delayed/i.test(String(row.status))));

      const matchesSearch = !search.trim() || 
        String(row.vehicleCode || row.driverCode || row.assetCode || row.customerCode || "").toLowerCase().includes(qLower) ||
        String(row.fullName || row.name || row.plateNumber || "").toLowerCase().includes(qLower);

      return matchesStatus && matchesSearch;
    });
  }, [scopedRows, search, statusFilter]);

  useEffect(() => {
    if (selected && !rows.some((row) => String(row.id) === String(selected.id))) {
      setSelected(null);
    }
  }, [rows, selected]);

  if (list.isLoading) return <LoadingState />;
  if (list.isError) return <ErrorState message={list.error instanceof Error ? list.error.message : `Unable to load ${cfg.title.toLowerCase()}.`} />;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow={cfg.eyebrow}
        title={cfg.title}
        description={cfg.description}
        actions={
          <>
            {cfg.api.create ? <button className="btn-primary" disabled={!canCreate} title={!canCreate ? "You do not have permission to perform this action." : undefined} onClick={() => { if (canCreate) { setIsCreating(true); setEditing({ ...cfg.defaults }); } }}><Plus className="h-4 w-4" /> Create</button> : null}
            <button className="btn-ghost" disabled={!canExport} title={!canExport ? "You do not have permission to perform this action." : undefined} onClick={() => { if (canExport) exportRows(kind, rows); }}><Download className="h-4 w-4" /> Export CSV</button>
          </>
        }
      />

      {isFleetMaster ? (
        <FleetPainPointCockpit
          kind={kind}
          config={cfg}
          rows={rows}
          summary={visibleSummary}
        />
      ) : null}

      {kind === "vehicles" && !isScopedViewer ? <VehiclePlanningForecast data={planningInsights.data} loading={planningInsights.isLoading} /> : null}

      <div className="grid gap-4 md:grid-cols-4">
          {cfg.kpis.map(([label, key, suffix]) => (
          <KpiCard key={label} label={label} value={`${visibleSummary?.[key] ?? (key === "aiSignals" ? recommendations.length || "Select" : 0)}${suffix}`} icon={<Target />} status={Number(visibleSummary?.[key] ?? 0) > 0 && /risk|exception|watch/i.test(label) ? "Review" : "Healthy"} />
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
          {rows.length ? <DataTable rows={rows} columns={cfg.columns} onSelect={setSelected} /> : <EmptyState title={`No ${cfg.title.toLowerCase()} found`} subtitle="Try another search or filter, or create a new record if you have permission." />}
        <div className="space-y-4">
          <div className="panel p-5">
            <div className="flex items-center gap-2 text-teal-700"><Sparkles className="h-4 w-4" /><span className="section-title">Competitive Intelligence</span></div>
            <div className="mt-4 flex flex-wrap gap-2">
              {cfg.wow.map((item) => <span key={item} className="badge">{item}</span>)}
            </div>
          </div>
          {isFleetMaster ? <RiskActionQueue kind={kind} rows={rows} /> : null}
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
        assignPending={assignMutation.isPending}
        onClose={() => setSelected(null)}
        onEdit={(record) => { if (canUpdate) { setIsCreating(false); setEditing(record); } }}
        onDelete={(record) => canDelete && cfg.api.remove && deleteMutation.mutate(String(record.id))}
        onSmartAssign={isFleetMaster && canAssign ? () => assignMutation.mutate() : undefined}
        onNavigate={navigate}
        canUpdate={canUpdate}
        canDelete={canDelete}
        canAssign={canAssign}
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

function BatchDetailDrawer({ kind, config: cfg, detail, record, loading, assignPending, onClose, onEdit, onDelete, onSmartAssign, onNavigate, canUpdate, canDelete, canAssign }: {
  kind: EntityKind;
  config: EntityConfig;
  detail?: AnyRecord;
  record: AnyRecord | null;
  loading: boolean;
  assignPending?: boolean;
  onClose: () => void;
  onEdit: (record: AnyRecord) => void;
  onDelete: (record: AnyRecord) => void;
  onSmartAssign?: () => void;
  onNavigate: (route: string) => void;
  canUpdate: boolean;
  canDelete: boolean;
  canAssign: boolean;
}) {
  if (!record) return null;
  const timeline = (detail?.timeline as AnyRecord[] | undefined) || [];
  const recommendations = (detail?.recommendations as AnyRecord[] | undefined) || [];
  const snapshot = buildSnapshot(kind, record, detail);

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
          <button className="btn-primary" disabled={!canUpdate} title={!canUpdate ? "You do not have permission to perform this action." : undefined} onClick={() => canUpdate && onEdit(record)}><Edit3 className="h-4 w-4" /> Edit</button>
          {onSmartAssign ? <button className="btn-ghost" onClick={onSmartAssign} disabled={assignPending || !canAssign} title={!canAssign ? "You do not have permission to perform this action." : undefined}><UserCheck className="h-4 w-4" /> {assignPending ? "Assigning..." : "Smart Assign"}</button> : null}
          <button className="btn-ghost" disabled={!canDelete} title={!canDelete ? "You do not have permission to perform this action." : undefined} onClick={() => canDelete && onDelete(record)}><Trash2 className="h-4 w-4" /> Delete</button>
          <button className="btn-ghost" onClick={() => onNavigate("/audit-logs")}><FileText className="h-4 w-4" /> Audit trail</button>
        </div>

        <section className="mt-6 rounded-2xl border border-blue-200 bg-gradient-to-br from-blue-50 via-white to-teal-50 p-4">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="section-title">Operational Snapshot</p>
              <h3 className="mt-1 text-lg font-semibold text-slate-950">Live context for this record</h3>
            </div>
            <span className="badge border-blue-200 bg-blue-50 text-blue-700">Connected workflow</span>
          </div>
          <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {snapshot.map((item) => (
              <div key={item.label} className="rounded-xl border border-slate-200 bg-white p-3 shadow-sm">
                <p className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-500">{item.label}</p>
                <p className="mt-1 text-sm font-semibold text-slate-900">{item.value}</p>
                {item.route ? (
                  <button type="button" className="mt-3 text-xs font-semibold text-blue-700 hover:text-blue-800" onClick={() => onNavigate(item.route)}>
                    Open {item.buttonLabel}
                  </button>
                ) : null}
              </div>
            ))}
          </div>
        </section>

        <DecisionBrief config={cfg} record={record} />

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

function FleetPainPointCockpit({ kind, config: cfg, rows, summary }: { kind: EntityKind; config: EntityConfig; rows: AnyRecord[]; summary?: AnyRecord }) {
  const blockers = rows.filter((row) => riskValue(row) >= 50 || /maintenance|delayed|suspended|outside|risk/i.test(String(row.status || row.geofenceStatus || row.geofenceRiskBadge || ""))).length;
  const ready = Math.max(0, rows.length - blockers);
  const assigned = rows.filter((row) => row.assignedDriver || row.assignedVehicle || row.assigned_driver || row.assigned_vehicle || row.customerName).length;
  const blindSpots = rows.filter((row) => /degraded|review|offline|unknown/i.test(String(row.deviceStatus || row.device_status || row.cameraStatus || row.camera_status || ""))).length;
  const primaryScore = kind === "vehicles"
    ? Number(summary?.fleetReadinessScore ?? summary?.fleet_readiness_score ?? 0)
    : kind === "drivers"
      ? Number(summary?.driverReadinessScore ?? summary?.driver_readiness_score ?? 0)
      : Number(summary?.utilizationScore ?? summary?.utilization_score ?? 0);
  const assignedPct = rows.length ? Math.round((assigned / rows.length) * 100) : 0;
  const title = kind === "vehicles" ? "Fleet readiness cockpit" : kind === "drivers" ? "Driver bench cockpit" : "Asset control cockpit";
  const sub = kind === "vehicles"
    ? "A live-feeling view of what can move, what is blocked, and what should be retired or fixed before it hurts dispatch."
    : kind === "drivers"
      ? "Availability, safety, compliance and assignment signals in one place so dispatch does not gamble with driver fit."
      : "Trailer and equipment control focused on utilization, geofence trust, and fast reassignment.";
  const lanes = [
    { label: "Ready", value: ready, pct: rows.length ? Math.round((ready / rows.length) * 100) : 0, color: "#059669", bg: "bg-emerald-50", border: "border-emerald-200" },
    { label: "Blocked", value: blockers, pct: rows.length ? Math.round((blockers / rows.length) * 100) : 0, color: "#d97706", bg: "bg-amber-50", border: "border-amber-200" },
    { label: "Assigned", value: assigned, pct: assignedPct, color: "#2563eb", bg: "bg-blue-50", border: "border-blue-200" },
    { label: kind === "vehicles" ? "Blind spots" : "Review", value: kind === "vehicles" ? blindSpots : rows.filter((row) => riskValue(row) >= 40).length, pct: rows.length ? Math.round(((kind === "vehicles" ? blindSpots : rows.filter((row) => riskValue(row) >= 40).length) / rows.length) * 100) : 0, color: "#7c3aed", bg: "bg-violet-50", border: "border-violet-200" },
  ];
  const watchList = [...rows].sort((a, b) => riskValue(b) - riskValue(a)).slice(0, 4);

  return (
    <section className="overflow-hidden rounded-[18px] border border-blue-100 bg-white shadow-sm">
      <div className="grid gap-0 xl:grid-cols-[360px_1fr]">
        <div className="relative min-h-[280px] overflow-hidden bg-gradient-to-br from-blue-600 via-sky-500 to-teal-500 p-6 text-white">
          <div className="absolute -right-16 -top-16 h-44 w-44 rounded-full bg-white/20 blur-2xl" />
          <div className="absolute bottom-0 left-0 right-0 h-28 bg-gradient-to-t from-blue-950/20 to-transparent" />
          <p className="relative text-xs font-black uppercase tracking-[0.24em] text-blue-50">OpsTrax Intelligence</p>
          <h2 className="relative mt-3 text-2xl font-black leading-tight">{title}</h2>
          <p className="relative mt-3 text-sm leading-relaxed text-blue-50/90">{sub}</p>
          <div className="relative mt-8 grid grid-cols-[120px_1fr] items-center gap-5">
            <div className="grid h-28 w-28 place-items-center rounded-full bg-white/15 shadow-inner" style={{ background: `conic-gradient(#ffffff ${Math.min(100, Math.max(0, primaryScore)) * 3.6}deg, rgba(255,255,255,.22) 0deg)` }}>
              <div className="grid h-20 w-20 place-items-center rounded-full bg-blue-600/95 text-center shadow-lg">
                <span className="text-2xl font-black">{Math.round(primaryScore || 0)}%</span>
              </div>
            </div>
            <div className="space-y-2 text-sm text-blue-50/95">
              <p className="font-bold">Decision index</p>
              <p>Combines readiness, risk, assignment coverage and evidence quality into one operating posture.</p>
            </div>
          </div>
        </div>

        <div className="p-5">
          <div className="grid gap-3 md:grid-cols-4">
            {lanes.map((lane) => (
              <div key={lane.label} className={`rounded-2xl border ${lane.border} ${lane.bg} p-4`}>
                <div className="flex items-center justify-between gap-3">
                  <p className="text-sm font-black text-slate-900">{lane.label}</p>
                  <span className="text-xs font-bold text-slate-500">{lane.pct}%</span>
                </div>
                <p className="mt-3 text-3xl font-black text-slate-950">{lane.value}</p>
                <div className="mt-3 h-2 overflow-hidden rounded-full bg-white/80">
                  <div className="h-full rounded-full" style={{ width: `${Math.min(100, lane.pct)}%`, background: lane.color }} />
                </div>
              </div>
            ))}
          </div>

          <div className="mt-5 grid gap-4 lg:grid-cols-[1fr_320px]">
            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="section-title">Watch Floor</p>
                  <h3 className="mt-1 font-black text-slate-950">Records needing action</h3>
                </div>
                <span className="badge border-teal-200 bg-teal-50 text-teal-700">Live ranked</span>
              </div>
              <div className="mt-4 grid gap-3 md:grid-cols-2">
                {watchList.map((row) => (
                  <div key={String(row.id)} className="rounded-xl border border-white bg-white p-3 shadow-sm">
                    <div className="flex items-start justify-between gap-3">
                      <p className="font-bold text-slate-950">{recordTitle(kind, row)}</p>
                      <RiskBadge risk={riskValue(row) >= 70 ? "High" : riskValue(row) >= 40 ? "Medium" : "Low"} />
                    </div>
                    <p className="mt-2 text-xs leading-relaxed text-slate-600">{String(row.recommendedAction || row.recommended_action || actionFor(kind, row))}</p>
                  </div>
                ))}
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-4">
              <p className="section-title">Pain Points Covered</p>
              <div className="mt-3 space-y-2">
                {cfg.painPoints.map((point, index) => (
                  <div key={point} className="flex items-center gap-3 rounded-xl border border-slate-100 bg-slate-50 px-3 py-2">
                    <span className="grid h-6 w-6 place-items-center rounded-full bg-blue-100 text-xs font-black text-blue-700">{index + 1}</span>
                    <span className="text-sm font-semibold text-slate-700">{point}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

function RiskActionQueue({ kind, rows }: { kind: EntityKind; rows: AnyRecord[] }) {
  const risky = [...rows].sort((a, b) => riskValue(b) - riskValue(a)).slice(0, 5);
  return (
    <div className="panel p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="section-title">Action Queue</p>
          <h3 className="mt-1 font-bold text-slate-950">Highest-value reviews</h3>
        </div>
        <span className="badge border-teal-200 bg-teal-50 text-teal-700">AI ranked</span>
      </div>
      <div className="mt-4 space-y-3">
        {risky.map((row) => (
          <div key={String(row.id)} className="rounded-2xl border border-slate-200 bg-slate-50 p-3">
            <div className="flex items-start justify-between gap-3">
              <p className="font-semibold text-slate-950">{recordTitle(kind, row)}</p>
              <RiskBadge risk={riskValue(row) >= 70 ? "High" : riskValue(row) >= 40 ? "Medium" : "Low"} />
            </div>
            <p className="mt-2 text-xs text-slate-600">{String(row.recommendedAction || row.recommended_action || actionFor(kind, row))}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function VehiclePlanningForecast({ data, loading }: { data?: AnyRecord; loading: boolean }) {
  const replacement = ((data?.replacementForecast as AnyRecord[]) || []).slice(0, 6);
  const customers = ((data?.customerBusiness as AnyRecord[]) || []).slice(0, 5);
  const routes = ((data?.routeBusiness as AnyRecord[]) || []).slice(0, 5);
  const gaps = ((data?.operationalGaps as AnyRecord[]) || []);
  const chartRows = replacement.map((row) => ({
    name: String(row.vehicleCode || row.vehicle_code || "").replace("-", "\n"),
    score: Number(row.capexPriorityScore ?? row.capex_priority_score ?? 0),
    status: String(row.lifecycleStatus || row.lifecycle_status || ""),
  }));

  return (
    <section className="grid gap-5 xl:grid-cols-[minmax(0,1.15fr)_minmax(360px,.85fr)]">
      <div className="overflow-hidden rounded-[18px] border border-slate-200 bg-white shadow-sm">
        <div className="border-b border-slate-100 bg-gradient-to-r from-slate-50 via-blue-50 to-teal-50 p-5">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <p className="section-title">Resource Planning / Forecasting</p>
              <h2 className="mt-2 text-xl font-black text-slate-950">Aging fleet and replacement plan</h2>
              <p className="mt-2 max-w-3xl text-sm text-slate-600">Real backend-calculated lifecycle visibility using vehicle year, odometer, readiness, risk and downtime status.</p>
            </div>
            <span className="badge border-violet-200 bg-violet-50 text-violet-700">CapEx forecast</span>
          </div>
        </div>
        {loading ? <p className="p-5 text-sm text-slate-500">Loading lifecycle forecast...</p> : null}
        <div className="grid gap-0 lg:grid-cols-[.8fr_1.2fr]">
          <div className="border-b border-slate-100 p-5 lg:border-b-0 lg:border-r">
            <p className="text-sm font-black text-slate-950">Replacement priority curve</p>
            <div className="mt-4 h-56">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={chartRows}>
                  <XAxis dataKey="name" tickLine={false} axisLine={false} tick={{ fontSize: 11, fill: "#64748b" }} />
                  <Tooltip cursor={{ fill: "rgba(37,99,235,.06)" }} />
                  <Bar dataKey="score" radius={[8, 8, 0, 0]}>
                    {chartRows.map((row) => <Cell key={row.name} fill={row.score > 180 ? "#dc2626" : row.score > 90 ? "#f59e0b" : "#2563eb"} />)}
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>
            <p className="mt-3 text-xs leading-relaxed text-slate-500">Higher bars mean stronger replacement or budget pressure. This gives the buyer a concrete CapEx queue, not a static vehicle list.</p>
          </div>
          <div>
            {replacement.map((row, index) => {
              const score = Number(row.capexPriorityScore ?? row.capex_priority_score ?? 0);
              return (
                <article key={String(row.id)} className="grid gap-3 border-b border-slate-100 p-4 last:border-b-0 md:grid-cols-[70px_1fr]">
                  <div className="grid h-14 w-14 place-items-center rounded-2xl bg-slate-100 text-lg font-black text-slate-700">#{index + 1}</div>
                  <div>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div>
                        <p className="font-black text-slate-950">{String(row.vehicleCode || row.vehicle_code)}</p>
                        <p className="text-xs text-slate-500">{String(row.type)} · {String(row.make)} {String(row.model)} · {String(row.ageYears ?? row.age_years)} years · {Number(row.odometerMiles ?? row.odometer_miles ?? 0).toLocaleString()} mi</p>
                      </div>
                      <StatusBadge status={row.lifecycleStatus || row.lifecycle_status} />
                    </div>
                    <div className="mt-3 h-2 overflow-hidden rounded-full bg-slate-100">
                      <div className="h-full rounded-full" style={{ width: `${Math.min(100, Math.round(score / 2.6))}%`, background: score > 180 ? "#dc2626" : score > 90 ? "#f59e0b" : "#2563eb" }} />
                    </div>
                    <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                      <p className="text-xs font-bold text-slate-600">{String(row.replacementWindow || row.replacement_window)} replacement window</p>
                      <p className="text-xs font-semibold text-blue-700">{String(row.recommendedAction || row.recommended_action)}</p>
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        </div>
      </div>

      <div className="space-y-5">
        <div className="overflow-hidden rounded-[18px] border border-slate-200 bg-white shadow-sm">
          <div className="border-b border-slate-100 p-5">
            <p className="section-title">Measured Features</p>
            <h3 className="mt-2 font-black text-slate-950">Pain points with live counts</h3>
          </div>
          <div className="grid gap-0 sm:grid-cols-2">
            {gaps.map((gap) => (
              <div key={String(gap.gapName || gap.gap_name)} className="border-b border-r border-slate-100 p-4">
                <p className="text-3xl font-black text-slate-950">{String(gap.affectedRecords ?? gap.affected_records ?? 0)}</p>
                <p className="mt-2 text-sm font-black text-slate-800">{String(gap.gapName || gap.gap_name)}</p>
                <p className="mt-1 text-xs leading-relaxed text-slate-500">{String(gap.visibility)}</p>
              </div>
            ))}
          </div>
        </div>

        <BusinessPlanningPanel title="Top Customers by Business" rows={customers} entityLabel="customer" />
        <BusinessPlanningPanel title="Top Routes by Business" rows={routes} entityLabel="route" />
      </div>
    </section>
  );
}

function BusinessPlanningPanel({ title, rows, entityLabel }: { title: string; rows: AnyRecord[]; entityLabel: "customer" | "route" }) {
  const counts = rows.map((row) => Number(row.jobCount ?? row.job_count ?? 0));
  const maxJobs = Math.max(1, ...counts);
  return (
    <div className="overflow-hidden rounded-[18px] border border-slate-200 bg-white shadow-sm">
      <div className="border-b border-slate-100 p-5">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="section-title">Business Planning</p>
            <h3 className="mt-2 text-lg font-black text-slate-950">{title}</h3>
          </div>
          <span className="badge border-blue-200 bg-blue-50 text-blue-700">Revenue + margin</span>
        </div>
      </div>
      <div className="divide-y divide-slate-100">
        {rows.map((row, index) => {
          const label = entityLabel === "customer" ? row.customerName || row.customer_name : row.routeName || row.route_name;
          const jobs = Number(row.jobCount ?? row.job_count ?? 0);
          const width = Math.max(8, Math.round((jobs / maxJobs) * 100));
          return (
            <article key={String(row.id)} className="p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="grid h-7 w-7 flex-shrink-0 place-items-center rounded-lg bg-slate-100 text-xs font-black text-slate-600">{index + 1}</span>
                    <p className="truncate font-black text-slate-950">{String(label)}</p>
                  </div>
                  <p className="mt-2 text-xs text-slate-500">{jobs} jobs · {String(row.revenueEstimate ?? row.revenue_estimate ?? "$0")} revenue · {String(row.marginEstimate ?? row.margin_estimate ?? "$0")} margin</p>
                </div>
                <RiskBadge risk={Number(row.avgJobRisk ?? row.avg_job_risk ?? 0) >= 55 ? "High" : Number(row.avgJobRisk ?? row.avg_job_risk ?? 0) >= 35 ? "Medium" : "Low"} />
              </div>
              <div className="mt-3 h-2 overflow-hidden rounded-full bg-slate-100">
                <div className="h-full rounded-full bg-gradient-to-r from-blue-500 to-teal-500" style={{ width: `${width}%` }} />
              </div>
              <p className="mt-2 text-xs font-bold text-blue-700">{String(row.planningSignal || row.planning_signal || "Maintain plan")}</p>
            </article>
          );
        })}
        {!rows.length ? (
          <div className="p-5 text-sm text-slate-500">No business planning records yet.</div>
        ) : null}
      </div>
    </div>
  );
}

function DecisionBrief({ config: cfg, record }: { config: EntityConfig; record: AnyRecord }) {
  const signals = cfg.decisionSignals.filter((key) => record[key] != null || record[toCamel(key)] != null).slice(0, 6);
  return (
    <section className="mt-6 rounded-2xl border border-blue-200 bg-gradient-to-br from-blue-50 via-white to-teal-50 p-4">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <p className="section-title">Operational Decision Brief</p>
          <h3 className="mt-2 text-lg font-bold text-slate-950">{String(record.recommendedAction || record.recommended_action || "Review readiness before next dispatch action")}</h3>
          <p className="mt-2 text-sm text-slate-600">This brief is designed around the practical questions clients ask: can it move, is it compliant, is it assigned correctly, and what should we do next?</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {cfg.competitiveEdges.slice(0, 3).map((edge) => <span key={edge} className="badge">{edge}</span>)}
        </div>
      </div>
      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {signals.map((key) => {
          const value = record[key] ?? record[toCamel(key)] ?? "--";
          return (
            <div key={key} className="rounded-xl border border-slate-200 bg-white p-3">
              <p className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-500">{labelize(key)}</p>
              <p className="mt-1 text-sm font-semibold text-slate-900">{String(value)}</p>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function pickBestDriver(rows: AnyRecord[]) {
  return [...rows].sort((a, b) => Number(b.driverReadinessScore ?? b.readinessScore ?? b.safetyScore ?? 0) - Number(a.driverReadinessScore ?? a.readinessScore ?? a.safetyScore ?? 0))[0];
}

function pickBestVehicle(rows: AnyRecord[]) {
  return [...rows].sort((a, b) => Number(b.fleetReadinessScore ?? b.readinessScore ?? b.dataQualityScore ?? 0) - Number(a.fleetReadinessScore ?? a.readinessScore ?? a.dataQualityScore ?? 0))[0];
}

function riskValue(row: AnyRecord) {
  return Number(row.riskHeatScore ?? row.risk_score ?? row.riskScore ?? (String(row.geofenceRiskBadge || "").toLowerCase() === "high" ? 75 : 0));
}

function recordTitle(kind: EntityKind, row: AnyRecord) {
  if (kind === "vehicles") return String(row.vehicleCode || row.vehicle_code || row.name || `Vehicle ${row.id}`);
  if (kind === "drivers") return String(row.fullName || row.full_name || row.driverCode || `Driver ${row.id}`);
  if (kind === "assets") return String(row.assetCode || row.asset_code || row.name || `Asset ${row.id}`);
  return String(row.name || row.title || `Record ${row.id}`);
}

function actionFor(kind: EntityKind, row: AnyRecord) {
  if (kind === "vehicles") return /maintenance/i.test(String(row.status)) ? "Create maintenance review before dispatch release." : "Confirm device, camera and driver assignment before next trip.";
  if (kind === "drivers") return Number(row.complianceScore ?? row.compliance_score ?? 100) < 85 ? "Review license, certification and HOS risk before assigning." : "Match with best-fit vehicle and monitor safety score.";
  if (kind === "assets") return /outside/i.test(String(row.geofenceStatus ?? row.geofence_status)) ? "Open unauthorized movement review and notify dispatch." : "Confirm customer/vehicle assignment and utilization target.";
  return "Review operational record.";
}

function buildSnapshot(kind: EntityKind, record: AnyRecord, detail?: AnyRecord) {
  if (kind === "vehicles") {
    return [
      { label: "Vehicle status", value: String(record.status ?? "--") },
      { label: "Assigned driver", value: String(record.assignedDriver || record.assignedDriverName || "Unassigned"), route: "/drivers", buttonLabel: "driver record" },
      { label: "Current shipment", value: String(record.currentShipment || record.currentJob || "--"), route: "/shipments", buttonLabel: "shipment" },
      { label: "Current location", value: String(record.currentLocation || record.location || record.city || "--") },
      { label: "Maintenance status", value: String(record.maintenanceStatus ?? record.maintenance_status ?? "--"), route: "/maintenance", buttonLabel: "maintenance" },
      { label: "Compliance status", value: String(record.complianceStatus ?? record.compliance_status ?? "--"), route: "/compliance", buttonLabel: "compliance" },
      { label: "Active alerts", value: String(record.alertCount ?? record.alerts ?? detail?.alertCount ?? "--"), route: "/alerts", buttonLabel: "alerts" },
    ];
  }
  if (kind === "drivers") {
    return [
      { label: "Driver status", value: String(record.status ?? "--") },
      { label: "Assigned vehicle", value: String(record.assignedVehicle || record.assignedVehicleCode || "Unassigned"), route: "/vehicles", buttonLabel: "vehicle record" },
      { label: "Current trip/job", value: String(record.currentTrip || record.currentJob || "--"), route: "/jobs", buttonLabel: "job" },
      { label: "License / compliance", value: String(record.licenseStatus || record.licenseNumber || record.complianceScore || "--"), route: "/compliance", buttonLabel: "compliance" },
      { label: "Safety score", value: String(record.safetyScore ?? "--"), route: "/safety", buttonLabel: "safety" },
      { label: "Availability / HOS", value: String(record.availability || record.hosStatus || "--"), route: "/hos-eld", buttonLabel: "HOS" },
      { label: "Incidents / coaching", value: String(record.incidents || record.coachingStatus || "--"), route: "/incidents", buttonLabel: "incidents" },
    ];
  }
  if (kind === "jobs") {
    return [
      { label: "Customer", value: String(record.customerName || record.customer || "--"), route: "/customers", buttonLabel: "customer" },
      { label: "Vehicle", value: String(record.vehicleCode || record.assignedVehicle || "--"), route: "/vehicles", buttonLabel: "vehicle" },
      { label: "Driver", value: String(record.driverName || record.assignedDriver || "--"), route: "/drivers", buttonLabel: "driver" },
      { label: "Pickup / drop-off", value: `${String(record.pickupAddress || "--")} → ${String(record.dropoffAddress || "--")}` },
      { label: "Status timeline", value: String((detail?.timeline as AnyRecord[] | undefined)?.[0]?.title || record.status || "--"), route: "/audit-logs", buttonLabel: "timeline" },
      { label: "Load / POD", value: `${String(record.cargoType || record.jobType || "--")} · ${String(record.proofStatus || detail?.proofStatus || "Pending")}`, route: "/proof-of-delivery", buttonLabel: "POD" },
      { label: "Invoice / compliance", value: String(record.invoiceStatus || "Not invoiced"), route: "/reports", buttonLabel: "invoice" },
    ];
  }
  return [
    { label: "Current status", value: String(record.status ?? "--") },
    { label: "Risk posture", value: String(record.riskHeatScore ?? record.riskScore ?? "--") },
    { label: "Recommended action", value: String(record.recommendedAction || record.recommended_action || actionFor(kind, record)) },
  ];
}

function toCamel(value: string) {
  return value.replace(/_([a-z])/g, (_, letter: string) => letter.toUpperCase());
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

function permissionMatrix(kind: EntityKind) {
  if (kind === "vehicles") return { create: "vehicles:create", update: "vehicles:update", delete: "vehicles:delete", assign: "vehicles:assign", export: "vehicles:export" };
  if (kind === "drivers") return { create: "drivers:create", update: "drivers:update", delete: "drivers:delete", assign: "drivers:assign", export: "drivers:export" };
  if (kind === "jobs") return { create: "shipments:create", update: "shipments:update", delete: "shipments:delete", assign: "dispatch:assign", export: "shipments:export" };
  return { create: "customers:create", update: "customers:update", delete: "customers:delete", assign: "customers:update", export: "customers:view" };
}

function buildVisibleSummary(kind: EntityKind, rows: AnyRecord[], summary: AnyRecord | undefined, session?: AnyRecord | null) {
  const role = String(session?.role ?? "");
  const scoped = isDriverPortalRole(role) || isCustomerPortalRole(role);

  if (!scoped) return summary ?? {};

  if (kind === "vehicles") {
    const readiness = rows.length ? Math.round(rows.reduce((total, row) => total + Number(row.fleetReadinessScore ?? row.readinessScore ?? 0), 0) / rows.length) : 0;
    const completeness = rows.length ? Math.round(rows.reduce((total, row) => total + Number(row.dataCompletenessScore ?? row.dataQualityScore ?? 0), 0) / rows.length) : 0;
    const risk = rows.filter((row) => riskValue(row) >= 40).length;
    const devices = rows.filter((row) => /offline|review|degraded/i.test(String(row.deviceStatus ?? row.device_status ?? ""))).length;
    return { ...summary, fleetReadinessScore: readiness, dataCompletenessScore: completeness, atRisk: risk, deviceExceptions: devices };
  }

  if (kind === "drivers") {
    const readiness = rows.length ? Math.round(rows.reduce((total, row) => total + Number(row.driverReadinessScore ?? row.readinessScore ?? 0), 0) / rows.length) : 0;
    const completeness = rows.length ? Math.round(rows.reduce((total, row) => total + Number(row.complianceScore ?? 0), 0) / rows.length) : 0;
    const risk = rows.filter((row) => riskValue(row) >= 40).length;
    const safety = rows.length ? Math.round(rows.reduce((total, row) => total + Number(row.safetyScore ?? 0), 0) / rows.length) : 0;
    return { ...summary, driverReadinessScore: readiness, dataCompletenessScore: completeness, atRisk: risk, safetyScore: safety };
  }

  if (kind === "jobs") {
    const total = rows.length;
    const active = rows.filter((row) => !/completed|delivered/i.test(String(row.status ?? ""))).length;
    const atRisk = rows.filter((row) => /delayed|risk/i.test(String(row.slaStatus ?? row.status ?? ""))).length;
    const assigned = rows.filter((row) => /assigned|en route|at stop/i.test(String(row.status ?? ""))).length;
    return { ...summary, total: total, active: active, atRisk: atRisk, aiSignals: assigned };
  }

  return { ...summary, total: rows.length };
}
