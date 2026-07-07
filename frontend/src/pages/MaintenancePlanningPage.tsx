import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import {
  exportCsv, LoadingState, EmptyState,
  KpiCard, ClayCard, ProgressBar, Timeline, RiskBadge,
} from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Live data builders ────────────────────────────────────────────────────────

function buildServiceHistorySeed(): AnyRecord[] {
  return [];
}

function buildDowntimeSeed(): AnyRecord[] {
  return [];
}

function buildPMSeed(): AnyRecord[] {
  return [];
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

/** Section wrapper: a claymorphic rail card with a small titled header. */
function RailCard({ title, count, children }: { title: string; count?: number; children: React.ReactNode }) {
  return (
    <ClayCard className="p-5">
      <div className="mb-4 flex items-center justify-between gap-2">
        <h2 className="section-title">{title}</h2>
        {count !== undefined && (
          <span className="rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-[10px] font-bold text-slate-500">
            {count}
          </span>
        )}
      </div>
      {children}
    </ClayCard>
  );
}

/** A single labelled distribution row with a live proportion bar. */
function BreakdownRow({ label, value, total, color }: { label: string; value: number; total: number; color?: string }) {
  const pct = total > 0 ? Math.round((value / total) * 100) : 0;
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between gap-3">
        <span className="truncate text-sm font-semibold text-slate-700">{label}</span>
        <span className="shrink-0 text-xs font-bold text-slate-500 tabular-nums">{value} · {pct}%</span>
      </div>
      <ProgressBar value={value} max={total || 1} color={color} />
    </div>
  );
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
  const avgCost = rows.length ? totalCost / rows.length : 0;

  // Per-vendor cost + downtime rollup, all from the fetched rows.
  const vendorRollup = useMemo(() => {
    const map = new Map<string, { cost: number; downtime: number; count: number }>();
    for (const r of rows) {
      const key = String(r.vendorName ?? "Internal");
      const cur = map.get(key) ?? { cost: 0, downtime: 0, count: 0 };
      cur.cost += Number(r.cost ?? 0);
      cur.downtime += Number(r.downtimeHours ?? 0);
      cur.count += 1;
      map.set(key, cur);
    }
    return Array.from(map.entries())
      .map(([vendor, v]) => ({ vendor, ...v }))
      .sort((a, b) => b.cost - a.cost)
      .slice(0, 6);
  }, [rows]);

  if (q.isLoading) return <LoadingState />;
  return (
    <div className="fleet-console flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard label="Completed Services" value={rows.length} />
        <KpiCard label="Total Cost" value={`$${totalCost.toLocaleString()}`} delta={`$${Math.round(avgCost).toLocaleString()} avg / order`} />
        <KpiCard label="Total Downtime" value={`${totalDowntime.toFixed(1)}h`} status={totalDowntime > 0 ? "Impact" : undefined} />
        <KpiCard label="Vendors Engaged" value={vendorRollup.length} />
      </div>

      {rows.length === 0 ? <EmptyState title="No completed service records" /> : (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
          <ClayCard className="overflow-hidden p-0">
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
          </ClayCard>

          <RailCard title="Spend by Vendor" count={vendorRollup.length}>
            <div className="flex flex-col gap-4">
              {vendorRollup.map((v) => (
                <div key={v.vendor} className="flex flex-col gap-1.5">
                  <div className="flex items-center justify-between gap-3">
                    <span className="truncate text-sm font-semibold text-slate-700">{v.vendor}</span>
                    <span className="shrink-0 text-xs font-bold text-slate-800 tabular-nums">${v.cost.toLocaleString()}</span>
                  </div>
                  <ProgressBar value={v.cost} max={vendorRollup[0]?.cost || 1} color="var(--teal)" />
                  <div className="flex items-center justify-between text-[11px] text-slate-500">
                    <span>{v.count} order{v.count === 1 ? "" : "s"}</span>
                    <span>{v.downtime.toFixed(1)}h downtime</span>
                  </div>
                </div>
              ))}
            </div>
          </RailCard>
        </div>
      )}
    </div>
  );
}

function DowntimeTab() {
  const q = useQuery({ queryKey: ["downtime"], queryFn: downtimeApi });
  const rows = (q.data ?? []) as AnyRecord[];

  const totalHours = rows.reduce((s, r) => s + Number(r.downtimeHours ?? 0), 0);
  const worstHours = rows.reduce((m, r) => Math.max(m, Number(r.downtimeHours ?? 0)), 0);

  // Per-vehicle downtime rollup — from the same fetched rows.
  const vehicleRollup = useMemo(() => {
    const map = new Map<string, { hours: number; count: number }>();
    for (const r of rows) {
      const key = String(r.vehicleCode || "Unassigned");
      const cur = map.get(key) ?? { hours: 0, count: 0 };
      cur.hours += Number(r.downtimeHours ?? 0);
      cur.count += 1;
      map.set(key, cur);
    }
    return Array.from(map.entries())
      .map(([vehicle, v]) => ({ vehicle, ...v }))
      .sort((a, b) => b.hours - a.hours)
      .slice(0, 7);
  }, [rows]);

  // Priority distribution — from the same fetched rows.
  const priorityDist = useMemo(() => {
    const order = ["Critical", "High", "Medium", "Normal"];
    const counts = new Map<string, number>();
    for (const r of rows) {
      const p = String(r.priority ?? "Normal");
      counts.set(p, (counts.get(p) ?? 0) + 1);
    }
    return order
      .map((p) => ({ label: p, value: counts.get(p) ?? 0 }))
      .filter((d) => d.value > 0);
  }, [rows]);

  const priorityColor: Record<string, string> = {
    Critical: "#dc2626", High: "#ef4444", Medium: "#f59e0b", Normal: "#64748b",
  };

  if (q.isLoading) return <LoadingState />;
  return (
    <div className="fleet-console flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard label="Downtime Events" value={rows.length} />
        <KpiCard label="Total Hours" value={`${totalHours.toFixed(1)}h`} status={totalHours > 0 ? "Risk" : undefined} />
        <KpiCard label="Est. Revenue Loss" value={`$${(totalHours * 280).toLocaleString()}`} delta="@ $280 / hr" />
        <KpiCard label="Longest Event" value={`${worstHours.toFixed(1)}h`} />
      </div>

      {rows.length === 0 ? <EmptyState title="No downtime events recorded" /> : (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
          <ClayCard className="overflow-hidden p-0">
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
          </ClayCard>

          <div className="flex flex-col gap-4">
            <RailCard title="Top Downtime by Vehicle" count={vehicleRollup.length}>
              <div className="flex flex-col gap-4">
                {vehicleRollup.map((v) => (
                  <div key={v.vehicle} className="flex flex-col gap-1.5">
                    <div className="flex items-center justify-between gap-3">
                      <span className="truncate text-sm font-semibold text-slate-700">{v.vehicle}</span>
                      <span className="shrink-0 text-xs font-bold text-red-700 tabular-nums">{v.hours.toFixed(1)}h</span>
                    </div>
                    <ProgressBar value={v.hours} max={vehicleRollup[0]?.hours || 1} color="#ef4444" />
                    <span className="text-[11px] text-slate-500">{v.count} event{v.count === 1 ? "" : "s"}</span>
                  </div>
                ))}
              </div>
            </RailCard>

            {priorityDist.length > 0 && (
              <RailCard title="By Priority">
                <div className="flex flex-col gap-3.5">
                  {priorityDist.map((d) => (
                    <BreakdownRow key={d.label} label={d.label} value={d.value} total={rows.length} color={priorityColor[d.label] ?? "#64748b"} />
                  ))}
                </div>
              </RailCard>
            )}
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
  const scheduled = rows.filter((r) => r.pmStatus === "Scheduled").length;

  // Nearest-due items → Timeline (from the same fetched rows).
  const upcoming = useMemo(() => {
    return [...rows]
      .sort((a, b) => Number(a.daysUntilDue ?? 9999) - Number(b.daysUntilDue ?? 9999))
      .slice(0, 8)
      .map((r) => {
        const days = Number(r.daysUntilDue ?? 0);
        return {
          id: r.id,
          type: "maintenance",
          title: `${String(r.title ?? "PM item")} · ${String(r.vehicleCode || "—")}`,
          eventTime: days < 0 ? `${Math.abs(days)}d overdue` : days === 0 ? "Due today" : `Due in ${days}d`,
        } as AnyRecord;
      });
  }, [rows]);

  if (q.isLoading) return <LoadingState />;
  return (
    <div className="fleet-console flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard label="Total PM Items" value={rows.length} />
        <KpiCard label="Overdue" value={overdue} status={overdue > 0 ? "Overdue" : undefined} />
        <KpiCard label="Due Soon" value={dueSoon} status={dueSoon > 0 ? "Risk" : undefined} />
        <KpiCard label="Scheduled" value={scheduled} />
      </div>

      {rows.length === 0 ? <EmptyState title="No PM items scheduled" /> : (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
          <ClayCard className="overflow-hidden p-0">
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
          </ClayCard>

          <div className="flex flex-col gap-4">
            <RailCard title="Readiness Breakdown">
              <div className="flex flex-col gap-3.5">
                <BreakdownRow label="Overdue" value={overdue} total={rows.length} color="#dc2626" />
                <BreakdownRow label="Due Soon" value={dueSoon} total={rows.length} color="#f59e0b" />
                <BreakdownRow label="Scheduled" value={scheduled} total={rows.length} color="var(--teal)" />
              </div>
              {(overdue > 0 || dueSoon > 0) && (
                <p className="mt-4 flex items-center gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs font-medium text-amber-700">
                  <RiskBadge risk={overdue > 0 ? "High" : "Medium"} />
                  {overdue + dueSoon} item{overdue + dueSoon === 1 ? "" : "s"} need attention
                </p>
              )}
            </RailCard>

            <Timeline items={upcoming} />
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
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{titles[tab]}</h1>
          <p className="text-sm text-slate-500 mt-0.5">{descriptions[tab]}</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={exportFns[tab]}>Export CSV</button>
      </div>

      <div className="panel flex gap-1 p-1.5">
        {TABS.map((t) => (
          <button key={t.key} type="button" onClick={() => setTab(t.key)}
            className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
              tab === t.key ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"
            }`}>{t.label}</button>
        ))}
      </div>

      {tab === "history"  && <ServiceHistoryTab />}
      {tab === "downtime" && <DowntimeTab />}
      {tab === "pm"       && <PMScheduleTab />}
    </div>
  );
}
