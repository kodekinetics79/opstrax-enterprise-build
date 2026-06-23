import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, EmptyState } from "@/components/ui";
import { drivers as seedDrivers, vehicles as seedVehicles } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed ─────────────────────────────────────────────────────────────────────

function buildOwnersSeed(): AnyRecord[] {
  return [
    { id: 1, ownerCode: "OWN-101", ownerName: "Mohammed Al-Rashid Transport", contactName: "Mohammed Al-Rashid", phone: "+966 55 123 4567", vehicleCount: 3, revenueSharePct: 82, totalLoads: 124, status: "Active",   contractExpiry: "2026-12-31" },
    { id: 2, ownerCode: "OWN-102", ownerName: "Gulf Star Logistics LLC",       contactName: "Khalid Ibrahim",      phone: "+971 50 987 6543", vehicleCount: 5, revenueSharePct: 79, totalLoads: 287, status: "Active",   contractExpiry: "2027-03-31" },
    { id: 3, ownerCode: "OWN-103", ownerName: "Nour Express LLC",              contactName: "Fatima Al-Nour",      phone: "+974 44 222 1111", vehicleCount: 2, revenueSharePct: 80, totalLoads: 63,  status: "Active",   contractExpiry: "2026-09-30" },
    { id: 4, ownerCode: "OWN-104", ownerName: "Desert Route Services",         contactName: "Abdullah Saud",       phone: "+966 54 444 7890", vehicleCount: 1, revenueSharePct: 75, totalLoads: 31,  status: "Inactive", contractExpiry: "2026-06-30" },
    { id: 5, ownerCode: "OWN-105", ownerName: "Emirates Fast Cargo",           contactName: "Omar Al-Hamdan",      phone: "+971 56 333 8800", vehicleCount: 4, revenueSharePct: 81, totalLoads: 198, status: "Active",   contractExpiry: "2027-06-30" },
  ];
}

function buildAssignmentsSeed(): AnyRecord[] {
  const drivers = seedDrivers as AnyRecord[];
  const vehicles = seedVehicles as AnyRecord[];
  return drivers.flatMap((d, di) =>
    vehicles.slice(0, di < 2 ? 2 : 1).map((v, vi) => ({
      id: di * 3 + vi + 1,
      assignmentType: (["Dispatch", "Permanent", "Temporary", "Dispatch"] as const)[(di + vi) % 4],
      driverName: String(d.name ?? ""),
      driverCode: String(d.driverId ?? ""),
      vehicleCode: String(v.vehicleId ?? ""),
      vehicleType: String(v.type ?? ""),
      startDate: `2026-0${3 + vi}-${String(10 + di * 5).padStart(2, "0")}`,
      endDate: (di + vi) % 3 === 0 ? "—" : `2026-0${5 + vi}-${String(10 + di * 5).padStart(2, "0")}`,
      status: (di + vi) % 3 === 0 ? "Active" : "Completed",
      assignedBy: (["Dispatch Manager", "Fleet Supervisor", "Admin"] as const)[(di) % 3],
    }))
  ).slice(0, 10);
}

const ownersApi = () => withFallback(
  unwrap<AnyRecord[]>(apiClient.get("/api/owners")).then((rows) =>
    rows.map((r) => ({
      ...r,
      ownerCode: r.ownerCode ?? r.owner_code ?? String(r.id),
      ownerName: r.ownerName ?? r.owner_name ?? r.title ?? "",
      contactName: r.contactName ?? r.contact_name ?? r.assignedToName ?? "",
      vehicleCount: Number(r.vehicleCount ?? r.vehicle_count ?? r.numericValue ?? r.numeric_value ?? 0),
      revenueSharePct: Number(r.revenueSharePct ?? r.revenue_share_pct ?? r.secondaryValue ?? r.secondary_value ?? 0),
    }))
  ),
  () => buildOwnersSeed()
);

const assignmentsApi = () => withFallback(
  unwrap<AnyRecord[]>(apiClient.get("/api/vehicle-assignments")).then((rows) =>
    rows.map((r) => ({
      ...r,
      driverName: r.driverName ?? r.driver_name ?? "",
      driverCode: r.driverCode ?? r.driver_code ?? "",
      vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "",
      assignmentType: r.assignmentType ?? r.assignment_type ?? "Dispatch",
      startDate: r.startDate ?? r.assignment_date ?? r.start_date ?? "",
      endDate: r.endDate ?? r.release_date ?? r.end_date ?? "—",
      status: r.status ?? "Active",
      assignedBy: r.assignedBy ?? r.assigned_by ?? "—",
    }))
  ),
  () => buildAssignmentsSeed()
);

// ── Helpers ──────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Active" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Inactive" || status === "Expired" ? "bg-slate-100 border-slate-300 text-slate-500" :
    status === "Completed" ? "bg-blue-50 border-blue-200 text-blue-700" :
    "bg-amber-50 border-amber-200 text-amber-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function TypeBadge({ type }: { type: string }) {
  const cls =
    type === "Permanent" ? "bg-blue-50 border-blue-200 text-blue-700" :
    type === "Temporary" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{type}</span>;
}

// ── Tab components ────────────────────────────────────────────────────────────

function OwnersTab() {
  const q = useQuery({ queryKey: ["owners"], queryFn: ownersApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const active = rows.filter((r) => r.status === "Active").length;
  const totalVehicles = rows.reduce((s, r) => s + Number(r.vehicleCount ?? 0), 0);
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Owners",  val: rows.length },
          { label: "Active",        val: active,        accent: "text-teal-600" },
          { label: "Fleet Managed", val: totalVehicles, accent: "text-blue-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{val}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No owner-operators registered" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Code", "Owner / Company", "Contact", "Vehicles", "Rev. Share %", "Total Loads", "Contract Expiry", "Status"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 text-xs font-medium text-slate-500">{String(r.ownerCode ?? "--")}</td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(r.ownerName ?? "—")}</p>
                    </td>
                    <td className="px-4 py-3">
                      <p className="text-slate-700 text-xs">{String(r.contactName ?? "—")}</p>
                      {!!r.phone && <p className="text-xs text-slate-400">{String(r.phone)}</p>}
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-800">{String(r.vehicleCount ?? 0)}</td>
                    <td className="px-4 py-3">
                      <span className={`text-xs font-semibold ${Number(r.revenueSharePct ?? 0) >= 80 ? "text-teal-700" : "text-amber-700"}`}>
                        {String(r.revenueSharePct ?? 0)}%
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{String(r.totalLoads ?? 0)}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.contractExpiry ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(r.status ?? "Active")} /></td>
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

function AssignmentsTab() {
  const q = useQuery({ queryKey: ["vehicle-assignments"], queryFn: assignmentsApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const active = rows.filter((r) => r.status === "Active").length;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Assignments", val: rows.length },
          { label: "Active",            val: active, accent: "text-teal-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{val}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No assignment history found" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Type", "Driver", "Vehicle", "Vehicle Type", "Start", "End", "Assigned By", "Status"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3"><TypeBadge type={String(r.assignmentType ?? "—")} /></td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(r.driverName ?? "—")}</p>
                      <p className="text-xs text-slate-400">{String(r.driverCode ?? "")}</p>
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-700">{String(r.vehicleCode ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.vehicleType ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.startDate ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.endDate ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.assignedBy ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(r.status ?? "Active")} /></td>
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

// ── Route → tab ───────────────────────────────────────────────────────────────

const ROUTE_TAB: Record<string, "owners" | "assignments"> = {
  "/owners":      "owners",
  "/assignments": "assignments",
};

type Tab = "owners" | "assignments";

const TABS: { key: Tab; label: string }[] = [
  { key: "owners",      label: "Owners" },
  { key: "assignments", label: "Assignments" },
];

// ── Main page ─────────────────────────────────────────────────────────────────

export function FleetAssignmentsPage() {
  const { pathname } = useLocation();
  const defaultTab = ROUTE_TAB[pathname] ?? "owners";
  const [tab, setTab] = useState<Tab>(defaultTab);

  const titles: Record<Tab, [string, string]> = {
    owners:      ["Owners", "Owner-operator registry — vehicle count, revenue share, contract expiry and status"],
    assignments: ["Assignments", "Driver and vehicle assignment history — dispatch, permanent and temporary assignments"],
  };

  const exportFns: Record<Tab, () => void> = {
    owners:      () => exportCsv("owners", buildOwnersSeed()),
    assignments: () => exportCsv("assignments", buildAssignmentsSeed()),
  };

  const [title, description] = titles[tab];

  return (
    <div className="flex flex-col gap-6 py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{title}</h1>
          <p className="text-sm text-slate-500 mt-0.5">{description}</p>
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

      {tab === "owners"      && <OwnersTab />}
      {tab === "assignments" && <AssignmentsTab />}
    </div>
  );
}
