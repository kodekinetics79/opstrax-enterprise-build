import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, CheckCircle, Clock, ClipboardList,
  Settings, ShieldAlert, Truck, Wrench, XCircle, Zap,
} from "lucide-react";
import { DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv } from "@/components/ui";
import { maintenanceApi } from "@/services/maintenanceApi";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

const TABS = ["Overview", "Defects", "Inspections", "Work Orders", "PM Rules", "Fault Codes"] as const;
type Tab = (typeof TABS)[number];

export function MaintenanceCommandPage() {
  const [activeTab, setActiveTab] = useState<Tab>("Overview");
  const qc = useQueryClient();

  const dashboard = useQuery<AnyRecord>({
    queryKey: ["maintenance", "dashboard"],
    queryFn: maintenanceApi.dashboard,
    refetchInterval: 60_000,
  });
  const defects = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "defects"],
    queryFn: () => maintenanceApi.defects(),
    staleTime: 15_000,
  });
  const inspections = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "inspections"],
    queryFn: () => maintenanceApi.inspections({ limit: 50 }),
    staleTime: 15_000,
  });
  const workOrders = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "work-orders"],
    queryFn: () => maintenanceApi.workOrders({ limit: 50 }),
    staleTime: 15_000,
  });
  const pmRules = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "pm-rules"],
    queryFn: maintenanceApi.pmRules,
    staleTime: 60_000,
  });
  const faultCodes = useQuery<AnyRecord[]>({
    queryKey: ["maintenance", "fault-codes"],
    queryFn: () => maintenanceApi.faultCodes("active"),
    staleTime: 30_000,
  });

  const hasPermission = useHasPermission();
  const canManage = hasPermission("maintenance:manage");
  const canClose  = hasPermission("maintenance:close");

  const invalidateAll = () => {
    qc.invalidateQueries({ queryKey: ["maintenance"] });
  };

  const ackDefect = useMutation({
    mutationFn: (id: number) => maintenanceApi.acknowledgeDefect(id),
    onSuccess: invalidateAll,
  });
  const resolveDefect = useMutation({
    mutationFn: (id: number) => maintenanceApi.resolveDefect(id),
    onSuccess: invalidateAll,
  });
  const reviewInspection = useMutation({
    mutationFn: (id: number) => maintenanceApi.reviewInspection(id),
    onSuccess: invalidateAll,
  });
  const completeWo = useMutation({
    mutationFn: (id: number) => maintenanceApi.completeWorkOrder(id),
    onSuccess: invalidateAll,
  });

  if (dashboard.isLoading) return <LoadingState />;
  if (dashboard.isError)
    return <div className="p-8 text-red-600">Failed to load maintenance dashboard. Check backend connectivity.</div>;

  const d = dashboard.data as AnyRecord;
  const kpis = (d?.kpis as AnyRecord) ?? {};
  const openDefectsList  = (d?.openDefects  as AnyRecord[]) ?? [];
  const duePmList        = (d?.duePm        as AnyRecord[]) ?? [];
  const recentWos        = (d?.recentWorkOrders as AnyRecord[]) ?? [];
  const insights         = (d?.insights     as AnyRecord[]) ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Fleet Maintenance"
        title="Maintenance Command Center"
        description="DVIR inspections, defect management, work orders, fault codes, and preventive maintenance — all persisted and RBAC-enforced."
        actions={
          <button
            type="button"
            className="btn-ghost cursor-pointer"
            onClick={() => exportCsv("maintenance-defects", defects.data ?? [])}
          >
            Export Defects
          </button>
        }
      />

      {/* KPI Strip */}
      <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-5">
        <KpiCard
          label="Fleet Available"
          value={`${String(kpis["fleetAvailabilityPct"] ?? "--")}%`}
          icon={<Truck />}
          status={Number(kpis["fleetAvailabilityPct"] ?? 100) >= 80 ? "Active" : "Warning"}
        />
        <KpiCard
          label="Vehicles Out of Service"
          value={String(kpis["vehiclesOutOfService"] ?? 0)}
          icon={<XCircle />}
          status={Number(kpis["vehiclesOutOfService"] ?? 0) > 0 ? "Critical" : "Active"}
        />
        <KpiCard
          label="Critical Open Defects"
          value={String(kpis["criticalOpenDefects"] ?? 0)}
          icon={<ShieldAlert />}
          status={Number(kpis["criticalOpenDefects"] ?? 0) > 0 ? "Critical" : "Active"}
        />
        <KpiCard
          label="Open Work Orders"
          value={String(kpis["openWorkOrders"] ?? 0)}
          icon={<Wrench />}
          status="Review"
        />
        <KpiCard
          label="PM Overdue"
          value={String(kpis["overduePm"] ?? 0)}
          icon={<Clock />}
          status={Number(kpis["overduePm"] ?? 0) > 0 ? "Warning" : "Active"}
        />
      </div>

      {/* System Maintenance Insights */}
      {insights.length > 0 && (
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="section-title">System Maintenance Insights</h2>
          <div className="mt-4 space-y-3">
            {insights.map((ins, i) => (
              <InsightRow key={i} insight={ins} />
            ))}
          </div>
        </section>
      )}

      {/* Tabs */}
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap gap-2 border-b border-slate-200 pb-4">
          {TABS.map((tab) => (
            <button
              key={tab}
              type="button"
              className={`rounded-xl px-4 py-2 text-sm font-semibold transition cursor-pointer ${
                tab === activeTab
                  ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60"
                  : "text-slate-500 hover:bg-slate-50 hover:text-slate-700"
              }`}
              onClick={() => setActiveTab(tab)}
            >
              {tab}
            </button>
          ))}
        </div>

        <div className="mt-5">
          {activeTab === "Overview" && (
            <OverviewTab
              openDefects={openDefectsList}
              duePm={duePmList}
              recentWos={recentWos}
              kpis={kpis}
              canManage={canManage}
              canClose={canClose}
              onAck={(id) => ackDefect.mutate(id)}
              onResolve={(id) => resolveDefect.mutate(id)}
              onCompleteWo={(id) => completeWo.mutate(id)}
            />
          )}

          {activeTab === "Defects" && (
            <DefectsTab
              rows={defects.data ?? []}
              isLoading={defects.isLoading}
              canManage={canManage}
              canClose={canClose}
              onAck={(id) => ackDefect.mutate(id)}
              onResolve={(id) => resolveDefect.mutate(id)}
            />
          )}

          {activeTab === "Inspections" && (
            <InspectionsTab
              rows={inspections.data ?? []}
              isLoading={inspections.isLoading}
              canManage={canManage}
              onReview={(id) => reviewInspection.mutate(id)}
            />
          )}

          {activeTab === "Work Orders" && (
            <WorkOrdersTab
              rows={workOrders.data ?? []}
              isLoading={workOrders.isLoading}
              canManage={canManage}
              canClose={canClose}
              onComplete={(id) => completeWo.mutate(id)}
            />
          )}

          {activeTab === "PM Rules" && (
            <PmRulesTab
              rows={pmRules.data ?? []}
              isLoading={pmRules.isLoading}
            />
          )}

          {activeTab === "Fault Codes" && (
            <FaultCodesTab
              rows={faultCodes.data ?? []}
              isLoading={faultCodes.isLoading}
            />
          )}
        </div>
      </section>
    </div>
  );
}

// ── Insight Row ───────────────────────────────────────────────────────────────
function InsightRow({ insight }: { insight: AnyRecord }) {
  const level   = String(insight["level"] ?? "info");
  const message = String(insight["message"] ?? "");
  const type    = String(insight["type"] ?? "System Maintenance Insight");
  const styles: Record<string, string> = {
    critical: "border-red-200 bg-red-50 text-red-800",
    warning:  "border-amber-200 bg-amber-50 text-amber-800",
    ok:       "border-teal-200 bg-teal-50 text-teal-800",
    info:     "border-blue-200 bg-blue-50 text-blue-800",
  };
  const icons: Record<string, typeof AlertTriangle> = {
    critical: AlertTriangle,
    warning:  AlertTriangle,
    ok:       CheckCircle,
    info:     Zap,
  };
  const Icon = icons[level] ?? Zap;
  return (
    <div className={`rounded-xl border p-4 ${styles[level] ?? styles.info}`}>
      <div className="flex items-start gap-3">
        <Icon className="mt-0.5 h-4 w-4 shrink-0" />
        <div>
          <p className="text-xs font-bold uppercase tracking-wide opacity-70">{type}</p>
          <p className="mt-0.5 text-sm">{message}</p>
        </div>
      </div>
    </div>
  );
}

// ── Overview Tab ──────────────────────────────────────────────────────────────
function OverviewTab({
  openDefects, duePm, recentWos, kpis,
  canManage, canClose, onAck, onResolve, onCompleteWo,
}: {
  openDefects: AnyRecord[];
  duePm: AnyRecord[];
  recentWos: AnyRecord[];
  kpis: AnyRecord;
  canManage: boolean;
  canClose: boolean;
  onAck: (id: number) => void;
  onResolve: (id: number) => void;
  onCompleteWo: (id: number) => void;
}) {
  return (
    <div className="grid gap-6 lg:grid-cols-2">
      <section>
        <h3 className="section-title mb-3">Open Defects Queue</h3>
        {openDefects.length === 0
          ? <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No open defects" />
          : openDefects.slice(0, 8).map((d) => (
              <DefectCard
                key={String(d["id"])}
                defect={d}
                canManage={canManage}
                canClose={canClose}
                onAck={() => onAck(Number(d["id"]))}
                onResolve={() => onResolve(Number(d["id"]))}
              />
            ))
        }
      </section>

      <div className="space-y-6">
        <section>
          <h3 className="section-title mb-3">PM Due / Overdue</h3>
          {duePm.length === 0
            ? <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No PM items due in 14 days" />
            : <DataTable
                rows={duePm}
                columns={["vehicleCode", "serviceType", "status", "priority", "dueDate", "estimatedCost"]}
              />
          }
        </section>

        <section>
          <h3 className="section-title mb-3">Recent Work Orders</h3>
          {recentWos.length === 0
            ? <Empty icon={<Wrench className="h-8 w-8 text-slate-300" />} message="No open work orders" />
            : recentWos.slice(0, 5).map((wo) => (
                <WorkOrderCard
                  key={String(wo["id"])}
                  wo={wo}
                  canClose={canClose}
                  onComplete={() => onCompleteWo(Number(wo["id"]))}
                />
              ))
          }
        </section>
      </div>
    </div>
  );
}

// ── Defects Tab ───────────────────────────────────────────────────────────────
function DefectsTab({
  rows, isLoading, canManage, canClose, onAck, onResolve,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  canClose: boolean;
  onAck: (id: number) => void;
  onResolve: (id: number) => void;
}) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<CheckCircle className="h-8 w-8 text-teal-400" />} message="No defects found" />;
  return (
    <div className="space-y-3">
      {rows.map((d) => (
        <DefectCard
          key={String(d["id"])}
          defect={d}
          canManage={canManage}
          canClose={canClose}
          onAck={() => onAck(Number(d["id"]))}
          onResolve={() => onResolve(Number(d["id"]))}
        />
      ))}
    </div>
  );
}

// ── Inspections Tab ───────────────────────────────────────────────────────────
function InspectionsTab({
  rows, isLoading, canManage, onReview,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  onReview: (id: number) => void;
}) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<ClipboardList className="h-8 w-8 text-slate-300" />} message="No inspections yet" />;
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th className="px-3 py-2">Report #</th>
            <th className="px-3 py-2">Vehicle</th>
            <th className="px-3 py-2">Driver</th>
            <th className="px-3 py-2">Type</th>
            <th className="px-3 py-2">Status</th>
            <th className="px-3 py-2">Defects</th>
            <th className="px-3 py-2">Submitted</th>
            <th className="px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {rows.map((r) => (
            <tr key={String(r["id"])} className="hover:bg-slate-50 cursor-pointer transition-colors">
              <td className="px-3 py-2 font-mono text-xs">{String(r["reportNumber"] ?? "--")}</td>
              <td className="px-3 py-2 font-medium">{String(r["vehicleCode"] ?? "--")}</td>
              <td className="px-3 py-2">{String(r["driverName"] ?? "--")}</td>
              <td className="px-3 py-2">{String(r["inspectionType"] ?? "--")}</td>
              <td className="px-3 py-2"><StatusBadge status={r["inspectionStatus"]} /></td>
              <td className="px-3 py-2">
                <span className={Number(r["criticalDefects"] ?? 0) > 0 ? "font-bold text-red-600" : "text-slate-600"}>
                  {String(r["totalDefects"] ?? 0)}
                  {Number(r["criticalDefects"] ?? 0) > 0 ? ` (${r["criticalDefects"]} critical)` : ""}
                </span>
              </td>
              <td className="px-3 py-2 text-xs text-slate-500">{fmtDate(r["submittedAt"])}</td>
              <td className="px-3 py-2">
                {canManage && String(r["inspectionStatus"]) !== "reviewed" && (
                  <button
                    type="button"
                    className="btn-ghost text-xs py-1 px-2 cursor-pointer"
                    onClick={() => onReview(Number(r["id"]))}
                  >
                    Review
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Work Orders Tab ───────────────────────────────────────────────────────────
function WorkOrdersTab({
  rows, isLoading, canManage, canClose, onComplete,
}: {
  rows: AnyRecord[];
  isLoading: boolean;
  canManage: boolean;
  canClose: boolean;
  onComplete: (id: number) => void;
}) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<Wrench className="h-8 w-8 text-slate-300" />} message="No work orders" />;
  return (
    <div className="space-y-3">
      {rows.map((wo) => (
        <WorkOrderCard key={String(wo["id"])} wo={wo} canClose={canClose} onComplete={() => onComplete(Number(wo["id"]))} />
      ))}
    </div>
  );
}

// ── PM Rules Tab ──────────────────────────────────────────────────────────────
function PmRulesTab({ rows, isLoading }: { rows: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<Settings className="h-8 w-8 text-slate-300" />} message="No PM rules configured" />;
  return (
    <DataTable
      rows={rows}
      columns={["ruleName", "serviceType", "triggerType", "intervalMiles", "intervalEngineHours", "intervalDays", "priority", "estimatedCost", "enabled"]}
    />
  );
}

// ── Fault Codes Tab ───────────────────────────────────────────────────────────
function FaultCodesTab({ rows, isLoading }: { rows: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!rows.length) return <Empty icon={<Zap className="h-8 w-8 text-slate-300" />} message="No active fault codes" />;
  return (
    <DataTable
      rows={rows}
      columns={["vehicleCode", "code", "codeType", "severity", "description", "occurrenceCount", "firstSeenAt", "lastSeenAt", "status"]}
    />
  );
}

// ── Defect Card ───────────────────────────────────────────────────────────────
const SEV_STYLES: Record<string, string> = {
  Critical: "border-red-300 bg-red-50",
  Major:    "border-amber-200 bg-amber-50",
  Minor:    "border-slate-200 bg-slate-50",
};

function DefectCard({
  defect, canManage, canClose, onAck, onResolve,
}: {
  defect: AnyRecord;
  canManage: boolean;
  canClose: boolean;
  onAck: () => void;
  onResolve: () => void;
}) {
  const sev   = String(defect["severity"] ?? "Minor");
  const oos   = Boolean(defect["outOfService"] ?? defect["out_of_service"]);
  const style = SEV_STYLES[sev] ?? SEV_STYLES.Minor;
  const status = String(defect["status"] ?? "Open");

  return (
    <div className={`mb-3 rounded-xl border p-4 ${style}`}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            {oos && <span className="rounded-full bg-red-600 px-2 py-0.5 text-xs font-bold text-white">OUT OF SERVICE</span>}
            <RiskBadge risk={sev} />
            <StatusBadge status={status} />
            <span className="text-xs text-slate-500">{String(defect["vehicleCode"] ?? "--")}</span>
          </div>
          <p className="mt-1.5 text-sm font-semibold text-slate-900">
            {String(defect["defectDescription"] ?? defect["defect_description"] ?? "Defect")}
          </p>
          <p className="mt-0.5 text-xs text-slate-500">
            {String(defect["defectCategory"] ?? defect["defect_category"] ?? "--")} · {String(defect["source"] ?? "dvir")} · {fmtDate(defect["createdAt"] ?? defect["created_at"])}
          </p>
        </div>
      </div>
      {status !== "resolved" && (
        <div className="mt-3 flex gap-2">
          {canManage && status === "Open" && (
            <button type="button" className="btn-ghost text-xs py-1 px-2 cursor-pointer" onClick={onAck}>Acknowledge</button>
          )}
          {canClose && status !== "rejected" && (
            <button type="button" className="btn-ghost text-xs py-1 px-2 text-teal-700 cursor-pointer" onClick={onResolve}>Resolve</button>
          )}
        </div>
      )}
    </div>
  );
}

// ── Work Order Card ───────────────────────────────────────────────────────────
function WorkOrderCard({
  wo, canClose, onComplete,
}: {
  wo: AnyRecord;
  canClose: boolean;
  onComplete: () => void;
}) {
  const status = String(wo["status"] ?? "Open");
  const isOpen = !["Completed","completed","Cancelled","cancelled"].includes(status);

  return (
    <div className="mb-3 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <RiskBadge risk={wo["priority"]} />
            <StatusBadge status={wo["status"]} />
            <span className="font-mono text-xs text-slate-500">{String(wo["woNumber"] ?? wo["workOrderNumber"] ?? wo["workOrderCode"] ?? "--")}</span>
          </div>
          <p className="mt-1.5 text-sm font-semibold text-slate-900">{String(wo["title"] ?? wo["issueType"] ?? "Work Order")}</p>
          <p className="mt-0.5 text-xs text-slate-500">
            {String(wo["vehicleCode"] ?? "--")}
            {wo["assignedToName"] ? ` · Assigned: ${String(wo["assignedToName"])}` : ""}
            {wo["estimatedCost"] ? ` · Est: $${Number(wo["estimatedCost"]).toLocaleString()}` : ""}
          </p>
        </div>
        {canClose && isOpen && (
          <button type="button" className="btn-ghost text-xs py-1 px-2 cursor-pointer" onClick={onComplete}>Complete</button>
        )}
      </div>
    </div>
  );
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function Empty({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-10 text-center text-slate-400">
      <div className="mb-3">{icon}</div>
      <p className="text-sm">{message}</p>
    </div>
  );
}

function fmtDate(val: unknown): string {
  if (!val) return "--";
  try { return new Date(String(val)).toLocaleDateString(); } catch { return String(val); }
}
