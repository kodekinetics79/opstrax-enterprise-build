import { FormEvent, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowUpRight, Boxes, Camera, ChevronRight, Cpu, Download, Gauge, MapPin,
  Plus, Save, Search, ShieldAlert, Sparkles, Trash2, TrendingUp, UserCheck, Wrench, X,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { vehiclesApi } from "@/services/vehiclesApi";
import { driversApi } from "@/services/driversApi";
import { useHasPermission } from "@/hooks/usePermission";
import { useAuth } from "@/hooks/useAuth";
import { scopeRowsForSession } from "@/auth/accessScope";
import { exportCsv, labelize, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

/* ------------------------------------------------------------------ helpers */

const g = (row: AnyRecord, ...keys: string[]) => {
  for (const k of keys) if (row?.[k] != null && row[k] !== "") return row[k];
  return undefined;
};
const num = (v: unknown) => (Number.isFinite(Number(v)) ? Number(v) : 0);

function riskTier(row: AnyRecord): "High" | "Medium" | "Low" {
  const heat = String(g(row, "riskHeatScore", "risk_heat_score") ?? "");
  if (/high|critical/i.test(heat)) return "High";
  if (/medium|warning/i.test(heat)) return "Medium";
  if (/low/i.test(heat)) return "Low";
  const n = num(g(row, "riskScore", "risk_score"));
  return n >= 70 ? "High" : n >= 40 ? "Medium" : "Low";
}

const FIELDS = [
  { key: "vehicleCode", label: "Vehicle code", required: true },
  { key: "type", label: "Type", type: "select", options: ["Truck", "Van", "Box Truck", "Reefer"], required: true },
  { key: "make", label: "Make" },
  { key: "model", label: "Model" },
  { key: "year", label: "Year", type: "number" },
  { key: "odometerMiles", label: "Odometer (mi)", type: "number" },
  { key: "vin", label: "VIN" },
  { key: "plateNumber", label: "Plate number" },
  { key: "status", label: "Status", type: "select", options: ["Available", "On Route", "At Stop", "Idle", "Delayed", "Maintenance"] },
] as const;

const FILTERS = ["All", "Available", "On Route", "Maintenance", "At risk"] as const;

/* ------------------------------------------------------------------ page */

export function VehiclesPage() {
  const navigate = useNavigate();
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const queryClient = useQueryClient();

  const canCreate = hasPermission("vehicles:create");
  const canUpdate = hasPermission("vehicles:update");
  const canDelete = hasPermission("vehicles:delete");
  const canAssign = hasPermission("vehicles:assign");
  const canExport = hasPermission("vehicles:export");

  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>("All");
  const [selectedId, setSelectedId] = useState<string | number | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [isCreating, setIsCreating] = useState(false);

  const list = useQuery({ queryKey: ["vehicles"], queryFn: vehiclesApi.list });
  const summary = useQuery({ queryKey: ["vehicles", "summary"], queryFn: vehiclesApi.summary });
  const planning = useQuery({ queryKey: ["vehicles", "planning-insights"], queryFn: vehiclesApi.planningInsights });
  const detail = useQuery({
    queryKey: ["vehicles", "detail", selectedId],
    queryFn: () => vehiclesApi.detail(String(selectedId)),
    enabled: selectedId != null,
  });
  const drivers = useQuery({ queryKey: ["drivers", "assign-pool"], queryFn: driversApi.list, enabled: canAssign });

  const rows = useMemo(() => scopeRowsForSession("vehicles", list.data || [], session), [list.data, session]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return rows.filter((row) => {
      const status = String(g(row, "status") ?? "");
      const matchFilter =
        filter === "All" ? true :
        filter === "At risk" ? (riskTier(row) === "High" || /maintenance|delayed/i.test(status)) :
        status.toLowerCase().includes(filter.toLowerCase());
      const matchSearch = !q || [
        g(row, "vehicleCode", "vehicle_code"), g(row, "make"), g(row, "model"),
        g(row, "plateNumber", "plate_number"), g(row, "assignedDriver", "assigned_driver"),
      ].some((v) => String(v ?? "").toLowerCase().includes(q));
      return matchFilter && matchSearch;
    });
  }, [rows, search, filter]);

  const sum = (summary.data as AnyRecord) || {};
  const readiness = Math.round(num(g(sum, "fleetReadinessScore", "fleet_readiness_score")) || avg(rows, "fleetReadinessScore"));
  const available = rows.filter((r) => /available/i.test(String(g(r, "status")))).length;
  const atRisk = num(g(sum, "atRisk", "at_risk")) || rows.filter((r) => riskTier(r) === "High").length;
  const deviceEx = num(g(sum, "deviceExceptions", "device_exceptions")) ||
    rows.filter((r) => !/online/i.test(String(g(r, "deviceStatus", "device_status") ?? "Online")) || !/online/i.test(String(g(r, "cameraStatus", "camera_status") ?? "Online"))).length;

  const save = useMutation({
    mutationFn: (p: AnyRecord) => (p.id && !isCreating ? vehiclesApi.update(String(p.id), p) : vehiclesApi.create(p)),
    onSuccess: async () => { setEditing(null); setIsCreating(false); await queryClient.invalidateQueries({ queryKey: ["vehicles"] }); },
  });
  const remove = useMutation({
    mutationFn: (id: string | number) => vehiclesApi.remove(id),
    onSuccess: async () => { setSelectedId(null); await queryClient.invalidateQueries({ queryKey: ["vehicles"] }); },
  });
  const assign = useMutation({
    mutationFn: async (vehicleId: string | number) => {
      const pool = (drivers.data || []).filter((d) => !g(d, "assignedVehicleId", "assigned_vehicle_id"));
      const best = [...(pool.length ? pool : drivers.data || [])]
        .sort((a, b) => num(g(b, "driverReadinessScore", "safetyScore")) - num(g(a, "driverReadinessScore", "safetyScore")))[0];
      if (!best?.id) throw new Error("No available driver to assign.");
      return vehiclesApi.assignDriver(String(vehicleId), String(best.id));
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["vehicles"] });
      await queryClient.invalidateQueries({ queryKey: ["drivers"] });
      if (selectedId != null) await queryClient.invalidateQueries({ queryKey: ["vehicles", "detail", selectedId] });
    },
  });

  if (list.isLoading) return <LoadingState />;
  if (list.isError) return <ErrorState message={list.error instanceof Error ? list.error.message : "Unable to load vehicles."} />;

  const selectedRecord = (detail.data?.record as AnyRecord) || rows.find((r) => String(r.id) === String(selectedId)) || null;

  return (
    <div className="space-y-8 pb-10">
      {/* Header */}
      <header className="flex flex-col gap-5 border-b border-slate-200 pb-7 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-white px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-500">
            <span className="h-1.5 w-1.5 rounded-full bg-teal-500" /> Fleet · Master data
          </div>
          <h1 className="mt-3 text-[2rem] font-semibold leading-none tracking-tight text-slate-900">Vehicles</h1>
          <p className="mt-2.5 text-sm text-slate-500">
            <span className="font-semibold text-slate-700 tabular-nums">{rows.length}</span> in fleet ·{" "}
            <span className="font-semibold text-emerald-600 tabular-nums">{available}</span> available ·{" "}
            <span className="font-semibold text-rose-600 tabular-nums">{atRisk}</span> need attention
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button type="button" disabled={!canExport} onClick={() => canExport && exportCsv("vehicles", filtered)}
            className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 py-2.5 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-40">
            <Download className="h-4 w-4" /> Export
          </button>
          <button type="button" disabled={!canCreate} onClick={() => { if (canCreate) { setIsCreating(true); setEditing({ type: "Truck", status: "Available" }); } }}
            className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-slate-800 disabled:opacity-40">
            <Plus className="h-4 w-4" /> New vehicle
          </button>
        </div>
      </header>

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Stat icon={<Gauge />} label="Fleet readiness" value={`${readiness}%`} meter={readiness} accent="teal" />
        <Stat icon={<Boxes />} label="Available now" value={available} sub={`${rows.length ? Math.round((available / rows.length) * 100) : 0}% of fleet`} />
        <Stat icon={<ShieldAlert />} label="At risk" value={atRisk} tone={atRisk > 0 ? "rose" : "default"} sub="High risk or down" />
        <Stat icon={<Cpu />} label="Device / camera gaps" value={deviceEx} tone={deviceEx > 0 ? "amber" : "default"} sub="Telematics blind spots" />
      </div>

      {/* Lifecycle insight band */}
      <LifecycleBand data={planning.data as AnyRecord} loading={planning.isLoading} />

      {/* Toolbar */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative w-full sm:max-w-sm">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search code, make, plate, driver…"
            className="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-10 pr-3 text-sm text-slate-800 outline-none transition placeholder:text-slate-400 focus:border-teal-400 focus:ring-2 focus:ring-teal-100" />
        </div>
        <div className="inline-flex rounded-xl border border-slate-200 bg-white p-1">
          {FILTERS.map((f) => (
            <button key={f} type="button" onClick={() => setFilter(f)}
              className={`rounded-lg px-3 py-1.5 text-xs font-semibold transition ${filter === f ? "bg-slate-900 text-white" : "text-slate-500 hover:text-slate-800"}`}>
              {f}
            </button>
          ))}
        </div>
      </div>

      {/* Roster */}
      {filtered.length ? (
        <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-[11px] uppercase tracking-[0.12em] text-slate-400">
                <th className="px-5 py-3 font-semibold">Vehicle</th>
                <th className="px-5 py-3 font-semibold">Status</th>
                <th className="px-5 py-3 font-semibold">Readiness</th>
                <th className="px-5 py-3 font-semibold">Risk</th>
                <th className="hidden px-5 py-3 font-semibold lg:table-cell">Odometer</th>
                <th className="hidden px-5 py-3 font-semibold md:table-cell">Driver</th>
                <th className="hidden px-5 py-3 font-semibold xl:table-cell">Health</th>
                <th className="px-5 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {filtered.map((row) => {
                const ready = num(g(row, "fleetReadinessScore", "fleet_readiness_score", "readinessScore"));
                return (
                  <tr key={String(row.id)} onClick={() => setSelectedId(row.id as string)}
                    className="group cursor-pointer transition hover:bg-slate-50/80">
                    <td className="px-5 py-3.5">
                      <div className="font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</div>
                      <div className="text-xs text-slate-500">{[g(row, "make"), g(row, "model")].filter(Boolean).join(" ") || String(g(row, "type") ?? "—")}</div>
                    </td>
                    <td className="px-5 py-3.5"><StatusPill status={g(row, "status")} /></td>
                    <td className="px-5 py-3.5"><Meter value={ready} /></td>
                    <td className="px-5 py-3.5"><RiskChip tier={riskTier(row)} /></td>
                    <td className="hidden px-5 py-3.5 tabular-nums text-slate-600 lg:table-cell">{num(g(row, "odometerMiles", "odometer_miles")).toLocaleString()} mi</td>
                    <td className="hidden px-5 py-3.5 text-slate-600 md:table-cell">{String(g(row, "assignedDriver", "assigned_driver") ?? "—")}</td>
                    <td className="hidden px-5 py-3.5 xl:table-cell">
                      <div className="flex items-center gap-3 text-slate-400">
                        <HealthDot ok={/online/i.test(String(g(row, "deviceStatus", "device_status") ?? "Online"))} icon={<Cpu className="h-3.5 w-3.5" />} />
                        <HealthDot ok={/online|recording/i.test(String(g(row, "cameraStatus", "camera_status") ?? "Online"))} icon={<Camera className="h-3.5 w-3.5" />} />
                      </div>
                    </td>
                    <td className="px-5 py-3.5 text-right">
                      <ChevronRight className="ml-auto h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-slate-500" />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <EmptyState title="No vehicles match" subtitle="Adjust your search or filter, or add a new vehicle." />
      )}

      {selectedRecord && (
        <VehicleDrawer
          record={selectedRecord} detail={detail.data} loading={detail.isLoading}
          canUpdate={canUpdate} canDelete={canDelete} canAssign={canAssign} assigning={assign.isPending}
          onClose={() => setSelectedId(null)}
          onEdit={() => { if (canUpdate) { setIsCreating(false); setEditing(selectedRecord); } }}
          onDelete={() => canDelete && remove.mutate(String(selectedRecord.id))}
          onAssign={() => canAssign && assign.mutate(selectedRecord.id as string)}
          onNavigate={navigate}
        />
      )}

      {editing && (
        <VehicleFormModal title={isCreating ? "New vehicle" : "Edit vehicle"} initial={editing} saving={save.isPending}
          onClose={() => { setEditing(null); setIsCreating(false); }} onSave={(p) => save.mutate(p)} />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ primitives */

function avg(rows: AnyRecord[], key: string) {
  if (!rows.length) return 0;
  return rows.reduce((t, r) => t + num(g(r, key, key.replace(/([A-Z])/g, "_$1").toLowerCase())), 0) / rows.length;
}

function Stat({ icon, label, value, sub, meter, tone = "default" }:
  { icon: React.ReactNode; label: string; value: React.ReactNode; sub?: string; meter?: number; tone?: "default" | "rose" | "amber" }) {
  const valueColor = tone === "rose" && num(value) > 0 ? "text-rose-600" : tone === "amber" && num(value) > 0 ? "text-amber-600" : "text-slate-900";
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-slate-300">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-slate-500">{label}</span>
        <span className="grid h-8 w-8 place-items-center rounded-lg bg-slate-50 text-slate-400 [&_svg]:h-4 [&_svg]:w-4">{icon}</span>
      </div>
      <div className={`mt-3 text-3xl font-semibold tracking-tight tabular-nums ${valueColor}`}>{value}</div>
      {meter != null ? (
        <div className="mt-3 h-1.5 overflow-hidden rounded-full bg-slate-100">
          <div className="h-full rounded-full bg-teal-500 transition-all" style={{ width: `${Math.min(100, meter)}%` }} />
        </div>
      ) : sub ? <p className="mt-2 text-xs text-slate-400">{sub}</p> : null}
      {meter != null && sub ? <p className="mt-2 text-xs text-slate-400">{sub}</p> : null}
      {accent ? null : null}
    </div>
  );
}

function StatusPill({ status }: { status?: unknown }) {
  const text = String(status ?? "—");
  const tone =
    /available|active|on route|en route|driving/i.test(text) ? "bg-emerald-50 text-emerald-700 ring-emerald-600/15" :
    /maintenance|delayed|out/i.test(text) ? "bg-rose-50 text-rose-700 ring-rose-600/15" :
    /idle|at stop|stop/i.test(text) ? "bg-amber-50 text-amber-700 ring-amber-600/15" :
    "bg-slate-100 text-slate-600 ring-slate-500/15";
  return <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset ${tone}`}>
    <span className="h-1.5 w-1.5 rounded-full bg-current opacity-70" />{text}
  </span>;
}

function RiskChip({ tier }: { tier: "High" | "Medium" | "Low" }) {
  const tone = tier === "High" ? "bg-rose-50 text-rose-700 ring-rose-600/15" : tier === "Medium" ? "bg-amber-50 text-amber-700 ring-amber-600/15" : "bg-slate-50 text-slate-500 ring-slate-500/15";
  return <span className={`inline-flex rounded-md px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${tone}`}>{tier}</span>;
}

function Meter({ value }: { value: number }) {
  const v = Math.round(value);
  const color = v >= 85 ? "bg-teal-500" : v >= 70 ? "bg-amber-500" : "bg-rose-500";
  return (
    <div className="flex items-center gap-2.5">
      <div className="h-1.5 w-20 overflow-hidden rounded-full bg-slate-100"><div className={`h-full rounded-full ${color}`} style={{ width: `${Math.min(100, v)}%` }} /></div>
      <span className="w-9 text-xs font-semibold tabular-nums text-slate-600">{v}%</span>
    </div>
  );
}

function HealthDot({ ok, icon }: { ok: boolean; icon: React.ReactNode }) {
  return <span className={`inline-flex items-center ${ok ? "text-emerald-500" : "text-amber-500"}`} title={ok ? "Online" : "Attention"}>{icon}</span>;
}

/* ------------------------------------------------------------------ lifecycle band */

function LifecycleBand({ data, loading }: { data?: AnyRecord; loading: boolean }) {
  const forecast = ((data?.replacementForecast as AnyRecord[]) || []).slice(0, 4);
  const gaps = ((data?.operationalGaps as AnyRecord[]) || []).slice(0, 4);
  if (loading) return <div className="h-28 animate-pulse rounded-2xl border border-slate-200 bg-slate-50" />;
  if (!forecast.length && !gaps.length) return null;
  return (
    <div className="grid gap-4 lg:grid-cols-[1.4fr_1fr]">
      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><TrendingUp className="h-4 w-4 text-teal-500" /> Replacement priority</div>
          <span className="text-[11px] font-medium uppercase tracking-wide text-slate-400">CapEx forecast</span>
        </div>
        <div className="mt-4 space-y-3">
          {forecast.map((row, i) => {
            const score = num(g(row, "capexPriorityScore", "capex_priority_score"));
            const pct = Math.min(100, Math.round(score / 2.6));
            const color = score > 180 ? "bg-rose-500" : score > 90 ? "bg-amber-500" : "bg-teal-500";
            return (
              <div key={String(row.id ?? i)} className="flex items-center gap-3">
                <span className="w-5 text-xs font-semibold text-slate-400">#{i + 1}</span>
                <div className="w-24 shrink-0 truncate text-sm font-medium text-slate-800">{String(g(row, "vehicleCode", "vehicle_code") ?? "—")}</div>
                <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100"><div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} /></div>
                <span className="hidden w-28 truncate text-right text-xs text-slate-500 sm:block">{String(g(row, "replacementWindow", "replacement_window") ?? "")}</span>
              </div>
            );
          })}
        </div>
      </div>
      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><Sparkles className="h-4 w-4 text-teal-500" /> Operational gaps</div>
        <div className="mt-4 grid grid-cols-2 gap-3">
          {gaps.map((gap, i) => (
            <div key={i} className="rounded-xl border border-slate-100 bg-slate-50/60 p-3">
              <div className="text-xl font-semibold tabular-nums text-slate-900">{num(g(gap, "affectedRecords", "affected_records"))}</div>
              <div className="mt-0.5 text-[11px] font-medium leading-tight text-slate-500">{String(g(gap, "gapName", "gap_name") ?? "")}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ drawer */

function VehicleDrawer({ record, detail, loading, canUpdate, canDelete, canAssign, assigning, onClose, onEdit, onDelete, onAssign, onNavigate }: {
  record: AnyRecord; detail?: AnyRecord; loading: boolean;
  canUpdate: boolean; canDelete: boolean; canAssign: boolean; assigning: boolean;
  onClose: () => void; onEdit: () => void; onDelete: () => void; onAssign: () => void; onNavigate: (r: string) => void;
}) {
  const code = String(g(record, "vehicleCode", "vehicle_code") ?? `Vehicle ${record.id}`);
  const recs = (detail?.recommendations as AnyRecord[]) || [];
  const snapshot = [
    { label: "Status", value: String(g(record, "status") ?? "—") },
    { label: "Driver", value: String(g(record, "assignedDriver", "assigned_driver") ?? "Unassigned"), route: "/drivers" },
    { label: "Odometer", value: `${num(g(record, "odometerMiles", "odometer_miles")).toLocaleString()} mi` },
    { label: "Year", value: String(g(record, "year") ?? "—") },
    { label: "Device", value: String(g(record, "deviceStatus", "device_status") ?? "—") },
    { label: "Camera", value: String(g(record, "cameraStatus", "camera_status") ?? "—") },
  ];
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-[2px]" onClick={onClose}>
      <aside className="h-full w-full max-w-xl overflow-y-auto border-l border-slate-200 bg-white shadow-2xl anim-slide-left" onClick={(e) => e.stopPropagation()}>
        {/* sticky header */}
        <div className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-6 py-4 backdrop-blur">
          <div className="flex items-start justify-between">
            <div>
              <div className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-400">Vehicle</div>
              <h2 className="mt-1 text-xl font-semibold tracking-tight text-slate-900">{code}</h2>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <StatusPill status={g(record, "status")} />
                <RiskChip tier={riskTier(record)} />
              </div>
            </div>
            <button type="button" aria-label="Close" onClick={onClose} className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700"><X className="h-5 w-5" /></button>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" disabled={!canUpdate} onClick={onEdit} className="inline-flex items-center gap-1.5 rounded-lg bg-slate-900 px-3 py-2 text-xs font-semibold text-white transition hover:bg-slate-800 disabled:opacity-40"><Wrench className="h-3.5 w-3.5" /> Edit</button>
            <button type="button" disabled={!canAssign || assigning} onClick={onAssign} className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-2 text-xs font-semibold text-slate-700 transition hover:bg-slate-50 disabled:opacity-40"><UserCheck className="h-3.5 w-3.5" /> {assigning ? "Assigning…" : "Smart assign"}</button>
            <button type="button" onClick={() => onNavigate("/audit-logs")} className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-2 text-xs font-semibold text-slate-700 transition hover:bg-slate-50"><MapPin className="h-3.5 w-3.5" /> Audit trail</button>
            <button type="button" disabled={!canDelete} onClick={onDelete} className="ml-auto inline-flex items-center gap-1.5 rounded-lg border border-rose-200 px-3 py-2 text-xs font-semibold text-rose-600 transition hover:bg-rose-50 disabled:opacity-40"><Trash2 className="h-3.5 w-3.5" /> Delete</button>
          </div>
        </div>

        <div className="space-y-6 p-6">
          {/* snapshot */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            {snapshot.map((s) => (
              <button key={s.label} type="button" disabled={!s.route} onClick={() => s.route && onNavigate(s.route)}
                className={`rounded-xl border border-slate-200 bg-slate-50/60 p-3 text-left ${s.route ? "transition hover:border-teal-300 hover:bg-white" : ""}`}>
                <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-400">{s.label}</div>
                <div className="mt-1 flex items-center gap-1 text-sm font-semibold text-slate-800">{s.value}{s.route && <ArrowUpRight className="h-3 w-3 text-teal-500" />}</div>
              </button>
            ))}
          </div>

          {/* AI recommendation */}
          {recs.length > 0 && (
            <div className="rounded-xl border border-teal-200 bg-teal-50/50 p-4">
              <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-teal-700"><Sparkles className="h-3.5 w-3.5" /> Recommended action</div>
              <p className="mt-1.5 text-sm font-medium text-slate-800">{String(recs[0].title ?? "")}</p>
              <p className="mt-1 text-sm text-slate-600">{String(recs[0].body ?? "")}</p>
            </div>
          )}

          <SectionTable title="Maintenance" icon={<Wrench className="h-4 w-4" />} rows={detail?.maintenance as AnyRecord[]} cols={["title", "category", "status", "dueDate"]} loading={loading} />
          <SectionTable title="Compliance" icon={<ShieldAlert className="h-4 w-4" />} rows={detail?.compliance as AnyRecord[]} cols={["documentName", "documentType", "status", "expiryDate"]} loading={loading} />
          <SectionTable title="Documents" icon={<Boxes className="h-4 w-4" />} rows={detail?.documents as AnyRecord[]} cols={["documentName", "documentType", "status", "expiryDate"]} loading={loading} />
          <SectionTable title="Safety events" icon={<ShieldAlert className="h-4 w-4" />} rows={detail?.safetyEvents as AnyRecord[]} cols={["eventType", "severity", "reviewStatus", "eventTime"]} loading={loading} />
          <SectionTable title="Trip activity" icon={<MapPin className="h-4 w-4" />} rows={detail?.trips as AnyRecord[]} cols={["status", "startedAt", "completedAt"]} loading={loading} />
          <SectionTable title="Audit trail" icon={<MapPin className="h-4 w-4" />} rows={detail?.auditTrail as AnyRecord[]} cols={["actionName", "actorName", "createdAt"]} loading={loading} />
        </div>
      </aside>
    </div>
  );
}

function SectionTable({ title, icon, rows, cols, loading }: { title: string; icon: React.ReactNode; rows?: AnyRecord[]; cols: string[]; loading?: boolean }) {
  const data = rows || [];
  return (
    <section>
      <div className="mb-2 flex items-center justify-between">
        <div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><span className="text-slate-400">{icon}</span>{title}</div>
        {data.length > 0 && <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-semibold text-slate-500">{data.length}</span>}
      </div>
      {loading ? <div className="h-12 animate-pulse rounded-xl bg-slate-50" />
        : data.length === 0 ? <p className="rounded-xl border border-dashed border-slate-200 px-3 py-3 text-xs text-slate-400">No linked records.</p>
        : (
          <div className="overflow-hidden rounded-xl border border-slate-200">
            <table className="w-full text-left text-xs">
              <thead><tr className="border-b border-slate-100 bg-slate-50/60 text-[10px] uppercase tracking-wide text-slate-400">
                {cols.map((c) => <th key={c} className="px-3 py-2 font-semibold">{labelize(c)}</th>)}
              </tr></thead>
              <tbody className="divide-y divide-slate-100">
                {data.slice(0, 6).map((row, i) => (
                  <tr key={String(row.id ?? i)} className="text-slate-600">
                    {cols.map((c) => <td key={c} className="px-3 py-2">{fmt(row[c])}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
    </section>
  );
}

function fmt(v: unknown) {
  if (v == null || v === "") return "—";
  const s = String(v);
  if (/^\d{4}-\d{2}-\d{2}T/.test(s)) return s.slice(0, 10);
  return s;
}

/* ------------------------------------------------------------------ form modal */

function VehicleFormModal({ title, initial, saving, onClose, onSave }: { title: string; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (p: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/40 p-4 backdrop-blur-sm" onClick={onClose}>
      <form onClick={(e) => e.stopPropagation()} onSubmit={submit} className="w-full max-w-xl rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl anim-fade-up">
        <div className="flex items-start justify-between">
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-400">Fleet</div>
            <h2 className="mt-1 text-xl font-semibold tracking-tight text-slate-900">{title}</h2>
          </div>
          <button type="button" aria-label="Close" onClick={onClose} className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700"><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-6 grid gap-4 sm:grid-cols-2">
          {FIELDS.map((f) => (
            <label key={f.key} className="block">
              <span className="mb-1.5 block text-xs font-semibold text-slate-600">{f.label}{("required" in f && f.required) ? <span className="text-rose-500"> *</span> : null}</span>
              {"options" in f && f.options ? (
                <select className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100"
                  required={"required" in f && f.required} value={String(form[f.key] ?? "")} onChange={(e) => setForm((c) => ({ ...c, [f.key]: e.target.value }))}>
                  <option value="">Select</option>
                  {f.options.map((o) => <option key={o} value={o}>{o}</option>)}
                </select>
              ) : (
                <input className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none transition placeholder:text-slate-400 focus:border-teal-400 focus:ring-2 focus:ring-teal-100"
                  type={"type" in f && f.type === "number" ? "number" : "text"} required={"required" in f && f.required}
                  value={String(form[f.key] ?? "")} onChange={(e) => setForm((c) => ({ ...c, [f.key]: "type" in f && f.type === "number" ? Number(e.target.value) : e.target.value }))} />
              )}
            </label>
          ))}
        </div>
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="rounded-xl border border-slate-200 px-4 py-2.5 text-sm font-semibold text-slate-600 transition hover:bg-slate-50">Cancel</button>
          <button type="submit" disabled={saving} className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:opacity-40"><Save className="h-4 w-4" /> {saving ? "Saving…" : "Save vehicle"}</button>
        </div>
      </form>
    </div>
  );
}
