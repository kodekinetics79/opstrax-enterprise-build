import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Live data ─────────────────────────────────────────────────────────────────

const VIOLATION_TYPES = [
  "Speeding (>20 km/h over limit)",
  "Phone Use While Driving",
  "Seatbelt Violation",
  "Hard Braking",
  "Harsh Acceleration",
  "Late-night Driving",
  "Unauthorized Route Deviation",
  "Red Light / Stop Sign",
];

const violationsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/traffic-violations")).then((rows) =>
      rows.map((r) => ({
        ...r,
        violationType: r.violationType ?? r.violation_type ?? r.eventType ?? r.event_type ?? "",
        driverName: r.driverName ?? r.driver_name ?? "",
        driverCode: r.driverCode ?? r.driver_code ?? "",
        vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "",
        occurredAt: r.occurredAt ?? r.occurred_at ?? r.eventTime ?? r.event_time ?? "",
        riskLevel: r.riskLevel ?? r.risk_level ?? "Low",
        reviewStatus: r.reviewStatus ?? r.review_status ?? r.status ?? "New",
      }))
    ),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/traffic-violations/summary")),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function SeverityBadge({ severity }: { severity: string }) {
  const cls =
    severity === "Critical" ? "bg-red-100 border-red-300 text-red-800" :
    severity === "High" ? "bg-red-50 border-red-200 text-red-700" :
    severity === "Medium" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{severity}</span>;
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Open" || status === "New" ? "bg-red-50 border-red-200 text-red-700" :
    status === "Coaching Assigned" ? "bg-violet-50 border-violet-200 text-violet-700" :
    status === "Reviewed" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "Closed" ? "bg-teal-50 border-teal-200 text-teal-700" :
    "bg-amber-50 border-amber-200 text-amber-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function ViolationTypeBadge({ type }: { type: string }) {
  const color =
    type.includes("Speeding") ? "text-red-700 bg-red-50 border-red-200" :
    type.includes("Phone") ? "text-violet-700 bg-violet-50 border-violet-200" :
    type.includes("Seatbelt") ? "text-amber-700 bg-amber-50 border-amber-200" :
    type.includes("Braking") || type.includes("Harsh") ? "text-orange-700 bg-orange-50 border-orange-200" :
    "text-slate-600 bg-slate-50 border-slate-200";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${color}`}>{type}</span>;
}

// ── Main page ─────────────────────────────────────────────────────────────────

type SeverityFilter = "All" | "Critical" | "High" | "Medium" | "Low";
type StatusFilter = "All" | "Open" | "Reviewed" | "Coaching Assigned" | "Closed";

export function TrafficViolationsPage() {
  const [severityFilter, setSeverityFilter] = useState<SeverityFilter>("All");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);

  const listQ = useQuery({ queryKey: ["traffic-violations", "list"], queryFn: violationsApi.list, refetchInterval: 30_000 });
  const sumQ = useQuery({ queryKey: ["traffic-violations", "summary"], queryFn: violationsApi.summary });

  const violations = (listQ.data ?? []) as AnyRecord[];
  const s = (sumQ.data ?? {}) as AnyRecord;

  const filtered = violations.filter((v) => {
    if (severityFilter !== "All" && v.severity !== severityFilter) return false;
    if (statusFilter !== "All" && v.status !== statusFilter && v.reviewStatus !== statusFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(v.driverName ?? "").toLowerCase().includes(q) ||
        String(v.vehicleCode ?? "").toLowerCase().includes(q) ||
        String(v.violationType ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Traffic Violations</h1>
          <p className="text-sm text-slate-500 mt-0.5">Speeding, phone use, seatbelt, hard braking and route deviation events — review, coaching and closure workflow</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("traffic-violations", filtered)}>Export CSV</button>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Violations",  val: s.total ?? violations.length },
          { label: "High Severity",     val: s.highSeverity ?? violations.filter((v) => ["Critical", "High"].includes(String(v.severity))).length, accent: "text-red-600" },
          { label: "Speeding",          val: s.speedingCount ?? violations.filter((v) => String(v.violationType ?? "").includes("Speeding")).length, accent: "text-amber-600" },
          { label: "Phone Use",         val: s.phoneUseCount ?? violations.filter((v) => String(v.violationType ?? "").includes("Phone")).length, accent: "text-violet-600" },
          { label: "Pending Review",    val: s.pendingReview ?? violations.filter((v) => ["Open", "New"].includes(String(v.status ?? v.reviewStatus))).length, accent: "text-red-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5 flex-wrap">
          {(["All", "Critical", "High", "Medium", "Low"] as SeverityFilter[]).map((f) => (
            <button key={f} type="button" onClick={() => setSeverityFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                severityFilter === f ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}>{f}</button>
          ))}
        </div>
        <select title="Status filter" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as StatusFilter)}
          className="border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400">
          <option value="All">All Statuses</option>
          <option value="Open">Open</option>
          <option value="Reviewed">Reviewed</option>
          <option value="Coaching Assigned">Coaching Assigned</option>
          <option value="Closed">Closed</option>
        </select>
        <input type="search" placeholder="Search driver, vehicle, type…" value={search} onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-52" />
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No violations match your filters" /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Violation Type", "Driver", "Vehicle", "Severity", "Occurred", "Status", "Description"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((v, i) => (
                  <tr key={String(v.id ?? i)} className={`hover:bg-slate-50 cursor-pointer ${selected?.id === v.id ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === v.id ? null : v)}>
                    <td className="px-4 py-3"><ViolationTypeBadge type={String(v.violationType ?? "--")} /></td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(v.driverName ?? "—")}</p>
                      <p className="text-xs text-slate-400">{String(v.driverCode ?? "")}</p>
                    </td>
                    <td className="px-4 py-3 text-slate-700 text-sm font-medium">{String(v.vehicleCode ?? "—")}</td>
                    <td className="px-4 py-3"><SeverityBadge severity={String(v.severity ?? "Low")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(v.occurredAt ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(v.status ?? v.reviewStatus ?? "Open")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500 max-w-48 truncate">{String(v.description ?? "—")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Detail drawer */}
      {selected && (
        <div className="fixed inset-0 z-40 flex justify-end" onClick={() => setSelected(null)}>
          <div className="bg-slate-950 w-full max-w-sm h-full flex flex-col overflow-y-auto shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
              <span className="text-sm font-semibold text-white">Violation Detail</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6 flex gap-2 flex-wrap">
              <SeverityBadge severity={String(selected.severity ?? "Low")} />
              <StatusBadge status={String(selected.status ?? selected.reviewStatus ?? "Open")} />
            </div>
            <div className="px-5 py-4 border-b border-white/6">
              <p className="text-xs text-slate-400 mb-1">Violation Type</p>
              <p className="text-sm font-semibold text-white">{String(selected.violationType ?? "—")}</p>
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Driver", String(selected.driverName ?? "—")],
                ["Driver Code", String(selected.driverCode ?? "—")],
                ["Vehicle", String(selected.vehicleCode ?? "—")],
                ["Occurred", String(selected.occurredAt ?? "—")],
                ["Risk Level", String(selected.riskLevel ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            {!!selected.description && (
              <div className="px-5 py-4 border-b border-white/6">
                <p className="text-xs text-slate-400 mb-1">Description</p>
                <p className="text-sm text-slate-300">{String(selected.description)}</p>
              </div>
            )}
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-amber-400 uppercase tracking-wide mb-1.5">Recommended Action</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {String(selected.severity) === "Critical" || String(selected.severity) === "High"
                  ? "Assign coaching task immediately. This violation pattern indicates elevated accident risk if unaddressed."
                  : String(selected.violationType ?? "").includes("Phone")
                  ? "Phone use while driving is a zero-tolerance policy violation. Escalate to fleet manager for formal review."
                  : "Schedule a coaching call within 48 hours and document outcome in driver's safety record."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
