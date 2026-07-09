import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { Download, Sparkles, Wrench } from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, EmptyState, KpiCard } from "@/components/ui";
import { serviceHistory as seedServiceHistory, downtimeEvents as seedDowntimeEvents, vehicles as seedVehicles } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed builders ─────────────────────────────────────────────────────────────

function buildServiceHistorySeed(): AnyRecord[] {
  return (seedServiceHistory as AnyRecord[]).map((r) => ({
    id: Number(r.id),
    workOrderCode: String(r.workOrderCode),
    vehicleCode: String(r.vehicleCode),
    serviceType: String(r.serviceType),
    vendorName: String(r.vendorName),
    priority: String(r.priority),
    cost: Number(r.cost ?? 0),
    currency: String(r.currency ?? "USD"),
    downtimeHours: Number(r.downtimeHours ?? 0),
    completedAt: String(r.completedAt),
    issueType: String(r.issueType),
    technicianName: String(r.technicianName ?? "—"),
    partsReplaced: String(r.partsReplaced ?? "—"),
  }));
}

function buildDowntimeSeed(): AnyRecord[] {
  return (seedDowntimeEvents as AnyRecord[]).map((r) => ({
    id: Number(r.id),
    vehicleCode: String(r.vehicleCode),
    downtimeReason: String(r.downtimeReason),
    startDate: String(r.startDate),
    endDate: String(r.endDate),
    durationHours: Number(r.durationHours ?? 0),
    affectedSystem: String(r.affectedSystem),
    resolutionDescription: String(r.resolutionDescription),
    costImpact: Number(r.costImpact ?? 0),
    revenueLoss: Number(r.revenueLoss ?? 0),
    priority: String(r.priority),
    status: String(r.status),
  }));
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
  const uniqueVehicles = new Set(rows.map((r) => String(r.vehicleCode))).size;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard label="Completed Services" value={String(rows.length)} status="Active" />
        <KpiCard label="Total Cost" value={`$${totalCost.toLocaleString()}`} status={totalCost > 10000 ? "Warning" : "Active"} />
        <KpiCard label="Total Downtime" value={`${totalDowntime.toFixed(1)}h`} status={totalDowntime > 20 ? "Critical" : "Active"} />
        <KpiCard label="Vehicles Serviced" value={String(uniqueVehicles)} status="Active" />
      </div>
      {rows.length === 0 ? <EmptyState title="No completed service records" /> : (
        <div className="overflow-x-auto rounded-xl border border-slate-200">
          <table className="w-full min-w-[620px] text-left text-sm">
            <thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
              <tr>
                {["Work Order", "Vehicle", "Service Type", "Issue", "Vendor / Technician", "Priority", "Cost", "Downtime", "Completed"].map((h) => (
                  <th key={h} className="px-4 py-2.5">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50 cursor-pointer transition-colors">
                  <td className="px-4 py-3 font-medium text-slate-900">{String(r.workOrderCode ?? "--")}</td>
                  <td className="px-4 py-3 text-slate-700">{String(r.vehicleCode ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-600">{String(r.serviceType ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.issueType ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">
                    <div>{String(r.vendorName ?? "Internal")}</div>
                    <div className="text-slate-400">{String(r.technicianName ?? "")}</div>
                  </td>
                  <td className="px-4 py-3"><PriorityBadge priority={String(r.priority ?? "Normal")} /></td>
                  <td className="px-4 py-3 font-medium text-slate-700">${Number(r.cost ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-3 text-xs text-slate-600">{Number(r.downtimeHours ?? 0) > 0 ? `${String(r.downtimeHours)}h` : "—"}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.completedAt ?? "—")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function DowntimeTab() {
  const q = useQuery({ queryKey: ["downtime"], queryFn: downtimeApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const totalHours = rows.reduce((s, r) => s + Number(r.durationHours ?? r.downtimeHours ?? 0), 0);
  const totalRevenueLoss = rows.reduce((s, r) => s + Number(r.revenueLoss ?? 0), 0);
  const uniqueSystems = new Set(rows.map((r) => String(r.affectedSystem))).size;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard label="Downtime Events" value={String(rows.length)} status={rows.length > 5 ? "Warning" : "Active"} />
        <KpiCard label="Total Hours Off-Road" value={`${totalHours.toFixed(1)}h`} status={totalHours > 20 ? "Critical" : "Active"} />
        <KpiCard label="Est. Revenue Loss" value={`$${totalRevenueLoss.toLocaleString()}`} status={totalRevenueLoss > 5000 ? "Warning" : "Active"} />
        <KpiCard label="Systems Affected" value={String(uniqueSystems)} status="Active" />
      </div>
      {rows.length === 0 ? <EmptyState title="No downtime events recorded" /> : (
        <div className="overflow-x-auto rounded-xl border border-slate-200">
          <table className="w-full min-w-[620px] text-left text-sm">
            <thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
              <tr>
                {["Vehicle", "Downtime Reason", "Affected System", "Duration", "Start", "Resolved", "Cost Impact", "Revenue Loss", "Priority"].map((h) => (
                  <th key={h} className="px-4 py-2.5">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50 cursor-pointer transition-colors">
                  <td className="px-4 py-3 font-medium text-slate-900">{String(r.vehicleCode ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-600">
                    <div>{String(r.downtimeReason ?? r.title ?? "—")}</div>
                    <div className="mt-0.5 text-slate-400">{String(r.resolutionDescription ?? "")}</div>
                  </td>
                  <td className="px-4 py-3">
                    <span className="inline-flex text-xs px-2 py-0.5 rounded-full border border-slate-200 bg-slate-50 font-medium text-slate-600">{String(r.affectedSystem ?? "—")}</span>
                  </td>
                  <td className="px-4 py-3 font-medium text-red-700">{Number(r.durationHours ?? r.downtimeHours ?? 0).toFixed(1)}h</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.startDate ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.endDate ?? "—")}</td>
                  <td className="px-4 py-3 font-medium text-slate-700">${Number(r.costImpact ?? r.cost ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-3 text-xs font-medium text-amber-700">${Number(r.revenueLoss ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-3"><PriorityBadge priority={String(r.priority ?? "Normal")} /></td>
                </tr>
              ))}
            </tbody>
          </table>
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
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard label="Total PM Items" value={String(rows.length)} status="Active" />
        <KpiCard label="Overdue" value={String(overdue)} status={overdue > 0 ? "Critical" : "Active"} />
        <KpiCard label="Due Soon" value={String(dueSoon)} status={dueSoon > 2 ? "Warning" : "Active"} />
        <KpiCard label="Scheduled" value={String(rows.filter((r) => r.pmStatus === "Scheduled").length)} status="Active" />
      </div>
      {rows.length === 0 ? <EmptyState title="No PM items scheduled" /> : (
        <div className="overflow-x-auto rounded-xl border border-slate-200">
          <table className="w-full min-w-[620px] text-left text-sm">
            <thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
              <tr>
                {["Service", "Vehicle", "Category", "PM Status", "Due Date", "Days Left", "Est. Cost", "Risk"].map((h) => (
                  <th key={h} className="px-4 py-2.5">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50 cursor-pointer transition-colors">
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
      )}
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function MaintenancePlanningPage() {
  const { pathname } = useLocation();
  const defaultTab = (ROUTE_TAB[pathname] as Tab) ?? "history";
  const [tab, setTab] = useState<Tab>(defaultTab);

  // Sync tab with route when navigating between /service-history, /downtime, /preventive-maintenance
  useEffect(() => {
    const routeTab = ROUTE_TAB[pathname] as Tab | undefined;
    if (routeTab) setTab(routeTab);
  }, [pathname]);

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
    <div className="space-y-6 pb-10">
      {/* ── fh-hero header ─────────────────────────────────────── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Wrench className="h-3 w-3" /> Maintenance
                </span>
                <span className="text-[11px] font-semibold text-slate-500">{descriptions[tab]}</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                {titles[tab]}
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Service history, downtime tracking, and preventive maintenance scheduling
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button type="button" className="fh-btn-primary cursor-pointer" onClick={exportFns[tab]}>
                <Download className="h-4 w-4" /> Export CSV
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* ── Ops intelligence bar ─────────────────────────────── */}
      <div className="anim-fade-up relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
        <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-teal-500/10 blur-2xl" />
        <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-indigo-500/8 blur-2xl" />
        <div className="relative flex items-center gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-teal-400/20 to-teal-600/10 ring-1 ring-teal-400/20">
            <Sparkles className="h-5 w-5 text-teal-300" />
          </span>
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/80">Live operations signal</p>
            <p className="mt-1 text-sm font-medium leading-relaxed text-slate-600">
              {tab === "history"
                ? "Service history loaded — review completed maintenance costs, downtime, and vendor performance."
                : tab === "downtime"
                ? "Downtime events tracked — analyze fleet availability and estimated revenue impact."
                : "Preventive maintenance schedule active — monitor upcoming due dates and risk levels."}
            </p>
          </div>
        </div>
        <div className="relative flex items-center gap-6 text-xs">
          <div className="flex items-center gap-2">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
            <span className="text-slate-300">{tab === "pm" ? "PM schedule active" : tab === "downtime" ? "Downtime tracked" : "History loaded"}</span>
          </div>
          <div className="flex items-center gap-2">
            <Wrench className="h-3.5 w-3.5 text-teal-400" />
            <span className="text-slate-300">RBAC Enforced</span>
          </div>
        </div>
      </div>

      {/* ── Tab bar ──────────────────────────────────────────── */}
      <div className="panel p-2">
        <div className="flex flex-wrap gap-2">
          {TABS.map((t) => (
            <button key={t.key} type="button" onClick={() => setTab(t.key)}
              className={`rounded-xl px-4 py-2 text-sm font-semibold transition cursor-pointer ${
                tab === t.key
                  ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60"
                  : "text-slate-500 hover:bg-slate-50 hover:text-slate-700"
              }`}>{t.label}</button>
          ))}
        </div>
      </div>

      {tab === "history"  && <ServiceHistoryTab />}
      {tab === "downtime" && <DowntimeTab />}
      {tab === "pm"       && <PMScheduleTab />}
    </div>
  );
}
