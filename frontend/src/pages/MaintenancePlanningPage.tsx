import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, EmptyState, PageHeader } from "@/components/ui";
import { maintenance as seedMaintenance, vehicles as seedVehicles } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed builders ─────────────────────────────────────────────────────────────

function buildServiceHistorySeed(): AnyRecord[] {
  return (seedMaintenance as AnyRecord[]).map((m, i) => ({
    id: i + 1,
    workOrderCode: String(m.workOrderId ?? `WO-${7100 + i}`),
    title: String(m.issueType ?? "Service"),
    vehicleCode: String(m.vehicle ?? ""),
    vendorName: String(m.assignedWorkshop ?? "Internal"),
    status: "Completed",
    priority: String(m.priority ?? "Normal"),
    cost: Number(m.estimatedCost ?? 0),
    currency: String(m.currency ?? "USD"),
    downtimeHours: [3.5, 6.0][i % 2],
    completedAt: String(m.dueDate ?? "2026-05-28"),
    issueType: String(m.issueType ?? ""),
  }));
}

function buildDowntimeSeed(): AnyRecord[] {
  const vehicles = seedVehicles as AnyRecord[];
  return vehicles.map((v, i) => ({
    id: i + 1,
    workOrderCode: `WO-DT-${i + 1}`,
    vehicleCode: String(v.vehicleId ?? ""),
    title: (["Brake Repair", "Reefer Calibration", "Tire Replacement", "Engine Diagnostic"] as const)[i % 4],
    downtimeHours: [3.5, 6.0, 1.5, 0][i % 4],
    cost: [1800, 2400, 420, 0][i % 4],
    priority: (["Critical", "High", "Normal", "Normal"] as const)[i % 4],
    status: (["In Progress", "Completed", "Completed", "N/A"] as const)[i % 4],
    completedAt: i < 2 ? "2026-05-28" : "—",
    vendorName: (["DC Fleet Repair", "Jeddah Cold Service", "Internal", "—"] as const)[i % 4],
  })).filter((r) => Number(r.downtimeHours) > 0);
}

function buildPMSeed(): AnyRecord[] {
  const vehicles = seedVehicles as AnyRecord[];
  return vehicles.flatMap((v, vi) =>
    (["Oil Change", "Tire Rotation", "Brake Inspection", "AC Service"] as const).slice(0, vi < 2 ? 4 : 2).map((title, i) => ({
      id: vi * 4 + i + 1,
      title,
      category: (["Preventive", "Safety", "Preventive", "Comfort"] as const)[i % 4],
      vehicleCode: String(v.vehicleId ?? ""),
      currentOdometer: Number(v.odometer ?? 85000) + vi * 30000,
      dueDate: `2026-0${6 + i}-${String(15 + vi * 5).padStart(2, "0")}`,
      dueOdometer: Number(v.odometer ?? 85000) + vi * 30000 + 5000,
      serviceIntervalDays: [90, 180, 365, 180][i % 4],
      estimatedCost: [350, 180, 280, 420][i % 4],
      riskLevel: (["Medium", "Low", "High", "Low"] as const)[i % 4],
      pmStatus: (["Due Soon", "Scheduled", "Scheduled", "Overdue"] as const)[i % 4],
      daysUntilDue: [-2, 14, 28, 7][i % 4],
    }))
  );
}

const serviceHistoryApi = () => withFallback(
  unwrap<AnyRecord[]>(apiClient.get("/api/service-history")).then((rows) =>
    rows.map((r) => ({
      ...r,
      workOrderCode: r.workOrderCode ?? r.work_order_code ?? `WO-${String(r.id)}`,
      vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "",
      vendorName: r.vendorName ?? r.vendor_name ?? "Internal",
      cost: Number(r.estimatedCost ?? r.estimated_cost ?? 0),
      downtimeHours: Number(r.downtimeHours ?? r.downtime_hours ?? 0),
      completedAt: r.completedAt ?? r.completed_at ?? r.dueDate ?? r.due_date ?? "",
      issueType: r.issueType ?? r.issue_type ?? r.title ?? "",
    }))
  ),
  () => buildServiceHistorySeed()
);

const downtimeApi = () => withFallback(
  unwrap<AnyRecord[]>(apiClient.get("/api/downtime")).then((rows) =>
    rows.map((r) => ({ ...r, vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "", downtimeHours: Number(r.downtimeHours ?? r.downtime_hours ?? 0) }))
  ),
  () => buildDowntimeSeed()
);

const pmApi = () => withFallback(
  unwrap<AnyRecord[]>(apiClient.get("/api/preventive-maintenance")).then((rows) =>
    rows.map((r) => ({
      ...r,
      vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "",
      pmStatus: r.pmStatus ?? r.pm_status ?? "Scheduled",
      daysUntilDue: Number(r.daysUntilDue ?? r.days_until_due ?? 30),
      riskLevel: r.riskLevel ?? r.risk_level ?? "Medium",
    }))
  ),
  () => buildPMSeed()
);

// ── Helpers ──────────────────────────────────────────────────────────────────

function PriorityBadge({ priority }: { priority: string }) {
  const cls =
    priority === "Critical" ? "bg-red-100 border-red-300 text-red-800" :
    priority === "High" ? "bg-red-50 border-red-200 text-red-700" :
    priority === "Medium" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{priority}</span>;
}

function PmStatusBadge({ status }: { status: string }) {
  const cls =
    status === "Overdue" ? "bg-red-50 border-red-200 text-red-700" :
    status === "Due Soon" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-teal-50 border-teal-200 text-teal-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

// ── Route → tab mapping ───────────────────────────────────────────────────────

const ROUTE_TAB: Record<string, string> = {
  "/service-history": "history",
  "/downtime": "downtime",
  "/preventive-maintenance": "pm",
};

type Tab = "history" | "downtime" | "pm";

const TABS: { key: Tab; label: string }[] = [
  { key: "history",  label: "Service History" },
  { key: "downtime", label: "Downtime" },
  { key: "pm",       label: "PM Schedule" },
];

// ── Tab content ───────────────────────────────────────────────────────────────

function ServiceHistoryTab() {
  const q = useQuery({ queryKey: ["service-history"], queryFn: serviceHistoryApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const totalCost = rows.reduce((s, r) => s + Number(r.cost ?? 0), 0);
  const totalDowntime = rows.reduce((s, r) => s + Number(r.downtimeHours ?? 0), 0);
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Completed Services", val: rows.length },
          { label: "Total Cost", val: `$${totalCost.toLocaleString()}`, accent: "text-amber-600" },
          { label: "Total Downtime", val: `${totalDowntime.toFixed(1)}h`, accent: "text-red-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No completed service records" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Work Order", "Vehicle", "Service Type", "Vendor", "Priority", "Cost", "Downtime", "Completed"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.workOrderCode ?? "--")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(r.vehicleCode ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.issueType ?? r.title ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.vendorName ?? "Internal")}</td>
                    <td className="px-4 py-3"><PriorityBadge priority={String(r.priority ?? "Normal")} /></td>
                    <td className="px-4 py-3 font-medium text-slate-700">${Number(r.cost ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{Number(r.downtimeHours ?? 0) > 0 ? `${String(r.downtimeHours)}h` : "—"}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.completedAt ?? "—")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

function DowntimeTab() {
  const q = useQuery({ queryKey: ["downtime"], queryFn: downtimeApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const totalHours = rows.reduce((s, r) => s + Number(r.downtimeHours ?? 0), 0);
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Downtime Events", val: rows.length },
          { label: "Total Hours", val: `${totalHours.toFixed(1)}h`, accent: "text-red-600" },
          { label: "Est. Revenue Loss", val: `$${(totalHours * 280).toLocaleString()}`, accent: "text-amber-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-36">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No downtime events recorded" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Work Order", "Vehicle", "Issue", "Priority", "Downtime Hrs", "Est. Cost", "Vendor", "Status"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.workOrderCode ?? "--")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(r.vehicleCode ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.title ?? "—")}</td>
                    <td className="px-4 py-3"><PriorityBadge priority={String(r.priority ?? "Normal")} /></td>
                    <td className="px-4 py-3 font-medium text-red-700">{String(r.downtimeHours ?? 0)}h</td>
                    <td className="px-4 py-3 text-slate-700 text-xs">${Number(r.cost ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.vendorName ?? "Internal")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.status ?? "—")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

function PMScheduleTab() {
  const q = useQuery({ queryKey: ["preventive-maintenance"], queryFn: pmApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const overdue = rows.filter((r) => r.pmStatus === "Overdue").length;
  const dueSoon = rows.filter((r) => r.pmStatus === "Due Soon").length;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total PM Items", val: rows.length },
          { label: "Overdue", val: overdue, accent: "text-red-600" },
          { label: "Due Soon", val: dueSoon, accent: "text-amber-600" },
          { label: "Scheduled", val: rows.filter((r) => r.pmStatus === "Scheduled").length, accent: "text-teal-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No PM items scheduled" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Service", "Vehicle", "Category", "PM Status", "Due Date", "Days Left", "Est. Cost", "Risk"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.title ?? "--")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(r.vehicleCode ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.category ?? "—")}</td>
                    <td className="px-4 py-3"><PmStatusBadge status={String(r.pmStatus ?? "Scheduled")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.dueDate ?? "—")}</td>
                    <td className="px-4 py-3">
                      <span className={`text-xs font-medium ${Number(r.daysUntilDue) < 0 ? "text-red-700" : Number(r.daysUntilDue) < 7 ? "text-amber-700" : "text-slate-600"}`}>
                        {Number(r.daysUntilDue) < 0 ? `${Math.abs(Number(r.daysUntilDue))}d overdue` : `${String(r.daysUntilDue)}d`}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-600">${Number(r.estimatedCost ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3"><PriorityBadge priority={String(r.riskLevel ?? "Low")} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function MaintenancePlanningPage() {
  const { pathname } = useLocation();
  const defaultTab = (ROUTE_TAB[pathname] as Tab) ?? "history";
  const [tab, setTab] = useState<Tab>(defaultTab);

  const exportFns: Record<Tab, () => void> = {
    history: () => exportCsv("service-history", buildServiceHistorySeed()),
    downtime: () => exportCsv("downtime", buildDowntimeSeed()),
    pm: () => exportCsv("preventive-maintenance", buildPMSeed()),
  };

  const titles: Record<Tab, string> = {
    history: "Service History",
    downtime: "Downtime",
    pm: "Preventive Maintenance Schedule",
  };

  const descriptions: Record<Tab, string> = {
    history: "Completed maintenance events — cost, downtime, vendor, and vehicle impact",
    downtime: "Fleet downtime hours, causes, and estimated revenue loss per event",
    pm: "Upcoming preventive maintenance schedule with due dates, intervals, and risk prioritization",
  };

  return (
    <div className="flex flex-col gap-6 py-6">
      <PageHeader
        eyebrow="Maintenance"
        title={titles[tab]}
        description={descriptions[tab]}
        actions={
          <button type="button" className="btn-primary text-sm" onClick={exportFns[tab]}>Export CSV</button>
        }
      />

      <div className="panel flex gap-1.5 p-2">
        {TABS.map((t) => (
          <button key={t.key} type="button" onClick={() => setTab(t.key)}
            className={tab === t.key ? "control-tab control-tab-active" : "control-tab"}>{t.label}</button>
        ))}
      </div>

      {tab === "history"  && <ServiceHistoryTab />}
      {tab === "downtime" && <DowntimeTab />}
      {tab === "pm"       && <PMScheduleTab />}
    </div>
  );
}
