import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity, ArrowUpRight, Boxes, Camera, ChevronRight, Cpu, Download, Gauge, Info,
  MapPin, Navigation, Plus, Save, Search, ShieldAlert, Sparkles, Trash2, TrendingUp,
  Truck, UserCheck, Video, Wrench, X, Radio,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { vehiclesApi } from "@/services/vehiclesApi";
import { driversApi } from "@/services/driversApi";
import { downloadServerExport } from "@/services/fleetDomainApi";
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

// Live movement is derived from real telemetry only. speedMph/lastSeenAt come from the
// location_events join; where they are absent we say so honestly (no fabricated motion).
function isMoving(row: AnyRecord): boolean {
  return num(g(row, "speedMph", "speed_mph")) > 1;
}

function freshness(iso?: unknown): { label: string; live: boolean } | null {
  if (iso == null || iso === "") return null;
  const t = Date.parse(String(iso));
  if (Number.isNaN(t)) return null;
  const mins = Math.max(0, Math.round((Date.now() - t) / 60_000));
  if (mins < 3) return { label: "live", live: true };
  if (mins < 60) return { label: `${mins}m ago`, live: false };
  const hours = Math.round(mins / 60);
  if (hours < 48) return { label: `${hours}h ago`, live: false };
  return { label: `${Math.round(hours / 24)}d ago`, live: false };
}

type VehicleField = {
  key: string;
  label: string;
  required?: boolean;
  type?: "text" | "number" | "select" | "email";
  options?: readonly string[];
};

const FIELDS: VehicleField[] = [
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

const FILTERS = ["All", "Moving", "Available", "On Route", "Maintenance", "At risk"] as const;

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
  const [offset, setOffset] = useState(0);
  const PAGE_SIZE = 50;

  // Server-side paginated + searched — never fetches the full fleet. The search term
  // is applied server-side (so a 1000-vehicle fleet is searchable); the status filter
  // below operates on the returned page. Live telemetry columns refresh on an interval.
  const list = useQuery({
    queryKey: ["vehicles", "paged", search.trim(), offset],
    queryFn: () => vehiclesApi.listPaged({ limit: PAGE_SIZE, offset, search }),
    refetchInterval: 30_000,
  });
  const pagedRows = (list.data?.rows ?? []) as AnyRecord[];
  const totalRows = list.data?.total ?? pagedRows.length;
  const summary = useQuery({ queryKey: ["vehicles", "summary"], queryFn: vehiclesApi.summary, refetchInterval: 30_000 });
  const planning = useQuery({ queryKey: ["vehicles", "planning-insights"], queryFn: vehiclesApi.planningInsights });
  const detail = useQuery({
    queryKey: ["vehicles", "detail", selectedId],
    queryFn: () => vehiclesApi.detail(String(selectedId)),
    enabled: selectedId != null,
    refetchInterval: selectedId != null ? 20_000 : false,
  });
  const drivers = useQuery({ queryKey: ["drivers", "assign-pool"], queryFn: driversApi.list, enabled: canAssign });

  const rows = useMemo(() => scopeRowsForSession("vehicles", pagedRows, session), [pagedRows, session]);

  // Status filter operates on the current server page (search is already applied
  // server-side across the whole fleet).
  const filtered = useMemo(() => {
    return rows.filter((row) => {
      const status = String(g(row, "status") ?? "");
      return filter === "All" ? true :
        filter === "Moving" ? isMoving(row) :
        filter === "At risk" ? (riskTier(row) === "High" || /maintenance|delayed/i.test(status)) :
        status.toLowerCase().includes(filter.toLowerCase());
    });
  }, [rows, filter]);

  const sum = (summary.data as AnyRecord) || {};
  const readiness = Math.round(num(g(sum, "fleetReadinessScore", "fleet_readiness_score")) || avg(rows, "fleetReadinessScore"));
  const available = rows.filter((r) => /available/i.test(String(g(r, "status")))).length;
  const moving = rows.filter(isMoving).length;
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
    <div className="fleet-console flex h-full min-h-0 flex-col gap-3">

      {/* ── Console rail — brushed header with screws + primary actions ───── */}
      <header className="fc-rail relative shrink-0 px-5 py-3.5 pl-7 pr-7">
        <Screw className="left-2.5 top-2.5" slot="20deg" />
        <Screw className="right-2.5 top-2.5" slot="-38deg" />
        <Screw className="bottom-2.5 left-2.5" slot="62deg" />
        <Screw className="bottom-2.5 right-2.5" slot="-14deg" />
        <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
          <div className="min-w-0">
            <span className="section-title inline-flex items-center gap-2">
              <Truck className="h-3.5 w-3.5 text-teal-700" />
              Fleet · Master Data
            </span>
            <h1 className="mt-1 text-[26px] font-black leading-none tracking-tight text-slate-950">Vehicles</h1>
            <p className="mt-1.5 text-[12.5px] font-medium text-slate-500">
              <span className="font-bold text-slate-700 tabular-nums">{rows.length}</span> on this page ·{" "}
              <span className="font-bold text-emerald-600 tabular-nums">{moving}</span> moving ·{" "}
              <span className="font-bold text-sky-600 tabular-nums">{available}</span> available ·{" "}
              <span className="font-bold text-rose-600 tabular-nums">{atRisk}</span> need attention
            </p>
          </div>
          <div className="ml-auto flex flex-wrap items-center gap-2.5">
            <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-slate-500">
              <Activity className="h-3 w-3 text-teal-600" /> Live · 30 s
            </span>
            <button type="button" disabled={!canExport}
              onClick={() => { if (!canExport) return; void downloadServerExport("/api/vehicles/export", `vehicles_${new Date().toISOString().slice(0, 10)}.csv`).catch(() => exportCsv("vehicles", filtered)); }}
              title="Export the full fleet (all pages)" className="btn-ghost h-10">
              <Download className="h-4 w-4" /> Export
            </button>
            <button type="button" disabled={!canCreate}
              onClick={() => { if (canCreate) { setIsCreating(true); setEditing({ type: "Truck", status: "Available" }); } }}
              className="btn-primary h-10">
              <Plus className="h-4 w-4" /> New vehicle
            </button>
          </div>
        </div>
      </header>

      {/* ── Clay KPI tiles ───────────────────────────────────────────────── */}
      <div className="grid shrink-0 grid-cols-2 gap-3 xl:grid-cols-4">
        <ClayStat Icon={Gauge}      tone="fc-clay-teal"    iconCls="text-teal-700"    label="Fleet readiness"      value={`${readiness}%`} meter={readiness} caption={`${rows.length} units this page`} />
        <ClayStat Icon={Navigation} tone="fc-clay-emerald" iconCls="text-emerald-700" label="Moving now"           value={moving}          meter={rows.length ? (moving / rows.length) * 100 : 0} caption={`${available} available idle`} />
        <ClayStat Icon={ShieldAlert} tone="fc-clay-red"    iconCls="text-rose-700"    label="At risk"              value={atRisk}          alert={atRisk > 0} caption="High risk or down" />
        <ClayStat Icon={Cpu}        tone="fc-clay-amber"   iconCls="text-amber-700"   label="Device / camera gaps" value={deviceEx}        alert={deviceEx > 0} caption="Telematics blind spots" />
      </div>

      {/* ── Lifecycle band — replacement pressure + operational gaps ──────── */}
      <LifecycleBand data={planning.data as AnyRecord} loading={planning.isLoading} />

      {/* ── Roster console — neumorphic chassis, inset bezel screen ───────── */}
      <section className="fc-neumo flex min-h-[460px] flex-col overflow-hidden xl:min-h-0 xl:flex-1">
        <div className="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-2 px-4 pb-3 pt-3.5">
          <div className="relative w-full sm:max-w-xs">
            <Search className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input value={search} onChange={(e) => { setSearch(e.target.value); setOffset(0); }}
              placeholder="Search code, make, plate, driver…"
              className="fc-search w-full py-2.5 pl-10 pr-3 text-sm text-slate-800 outline-none placeholder:text-slate-400" />
          </div>
          <div className="fc-seg flex flex-wrap items-center gap-1 p-1">
            {FILTERS.map((f) => (
              <button key={f} type="button" onClick={() => setFilter(f)}
                className={`fc-seg-btn ${filter === f ? "fc-seg-btn-active" : ""}`}>
                {f}
              </button>
            ))}
          </div>
          <span className="ml-auto text-[11px] font-semibold text-slate-400 tabular-nums">
            {filtered.length} shown
          </span>
        </div>

        <div className="fc-bezel mx-3 mb-1 flex min-h-0 flex-1 flex-col">
          <div className="fc-screen min-h-0 flex-1 overflow-auto">
            {filtered.length ? (
              <table className="w-full text-left text-sm">
                <thead className="sticky top-0 z-10 bg-[#fcfdff]">
                  <tr className="border-b border-slate-200/80 text-[10px] uppercase tracking-[0.12em] text-slate-400">
                    <th className="px-5 py-3 font-bold">Vehicle</th>
                    <th className="px-4 py-3 font-bold">Status</th>
                    <th className="px-4 py-3 font-bold">Live speed</th>
                    <th className="hidden px-4 py-3 font-bold lg:table-cell">Last seen</th>
                    <th className="px-4 py-3 font-bold">Readiness</th>
                    <th className="px-4 py-3 font-bold">Risk</th>
                    <th className="hidden px-4 py-3 font-bold md:table-cell">Driver</th>
                    <th className="hidden px-4 py-3 font-bold xl:table-cell">Health</th>
                    <th className="px-4 py-3"><span className="sr-only">Open</span></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100/90">
                  {filtered.map((row) => {
                    const ready = num(g(row, "fleetReadinessScore", "fleet_readiness_score", "readinessScore"));
                    const speed = num(g(row, "speedMph", "speed_mph"));
                    const fresh = freshness(g(row, "lastSeenAt", "last_seen_at"));
                    const moving = isMoving(row);
                    return (
                      <tr key={String(row.id)} onClick={() => setSelectedId(row.id as string)}
                        className="group cursor-pointer transition hover:bg-sky-50/50">
                        <td className="px-5 py-3">
                          <div className="flex items-center gap-2.5">
                            <span className={`h-2 w-2 shrink-0 rounded-full ${moving ? "bg-emerald-500 animate-pulse" : fresh?.live ? "bg-sky-400" : "bg-slate-300"}`} />
                            <div className="min-w-0">
                              <div className="font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</div>
                              <div className="truncate text-xs text-slate-500">{[g(row, "make"), g(row, "model")].filter(Boolean).join(" ") || String(g(row, "type") ?? "—")}</div>
                            </div>
                          </div>
                        </td>
                        <td className="px-4 py-3"><StatusPill status={g(row, "status")} /></td>
                        <td className="px-4 py-3">
                          {fresh ? (
                            <span className="inline-flex items-baseline gap-1">
                              <span className={`text-[15px] font-bold tabular-nums ${moving ? "text-emerald-600" : "text-slate-400"}`}>{Math.round(speed)}</span>
                              <span className="text-[10px] font-semibold text-slate-400">mph</span>
                            </span>
                          ) : (
                            <span className="text-xs italic text-slate-400">No GPS</span>
                          )}
                        </td>
                        <td className="hidden px-4 py-3 lg:table-cell">
                          {fresh ? (
                            <span className={`inline-flex items-center gap-1.5 text-xs font-medium ${fresh.live ? "text-emerald-600" : "text-slate-500"}`}>
                              {fresh.live && <span className="live-dot h-1.5 w-1.5" />}
                              {fresh.label}
                            </span>
                          ) : <span className="text-xs italic text-slate-400">—</span>}
                        </td>
                        <td className="px-4 py-3"><Meter value={ready} /></td>
                        <td className="px-4 py-3"><RiskChip tier={riskTier(row)} /></td>
                        <td className="hidden px-4 py-3 text-slate-600 md:table-cell">{String(g(row, "assignedDriver", "assigned_driver", "driverName") ?? "—")}</td>
                        <td className="hidden px-4 py-3 xl:table-cell">
                          <div className="flex items-center gap-3">
                            <HealthDot ok={/online/i.test(String(g(row, "deviceStatus", "device_status") ?? "Online"))} icon={<Cpu className="h-3.5 w-3.5" />} />
                            <HealthDot ok={/online|recording/i.test(String(g(row, "cameraStatus", "camera_status") ?? "Online"))} icon={<Camera className="h-3.5 w-3.5" />} />
                          </div>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <ChevronRight className="ml-auto h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-slate-500" />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            ) : (
              <div className="grid h-full place-items-center py-12">
                <EmptyState title="No vehicles match" subtitle="Adjust your search or filter, or add a new vehicle." />
              </div>
            )}
          </div>
        </div>

        {/* Pager / instrument footer */}
        {totalRows > PAGE_SIZE ? (
          <div className="flex shrink-0 items-center justify-between px-5 py-3 text-[12px] text-slate-600">
            <span className="tabular-nums">
              Showing <strong>{totalRows === 0 ? 0 : offset + 1}–{Math.min(offset + PAGE_SIZE, totalRows)}</strong> of <strong>{totalRows.toLocaleString()}</strong>
            </span>
            <div className="flex items-center gap-2">
              <button type="button" disabled={offset === 0 || list.isFetching}
                onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
                className="fc-pager-btn">← Prev</button>
              <span className="text-xs text-slate-400 tabular-nums">Page {Math.floor(offset / PAGE_SIZE) + 1} of {Math.max(1, Math.ceil(totalRows / PAGE_SIZE))}</span>
              <button type="button" disabled={offset + PAGE_SIZE >= totalRows || list.isFetching}
                onClick={() => setOffset(offset + PAGE_SIZE)}
                className="fc-pager-btn">Next →</button>
            </div>
          </div>
        ) : (
          <div className="flex shrink-0 items-center gap-4 px-5 py-3 text-[11.5px] font-semibold text-slate-500">
            <span className="inline-flex items-center gap-2"><span className="deck-led deck-led-emerald" /> {moving} moving</span>
            <span className="inline-flex items-center gap-2"><span className={`deck-led ${deviceEx > 0 ? "deck-led-amber" : "deck-led-slate"}`} /> {deviceEx} device gaps</span>
            <button type="button" onClick={() => navigate("/iot-devices")} className="ml-auto inline-flex items-center gap-1 font-bold text-teal-700 hover:underline">
              Device health <ChevronRight className="h-3 w-3" />
            </button>
          </div>
        )}
      </section>

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

function Screw({ className, slot }: { className: string; slot: string }) {
  return <span aria-hidden className={`deck-screw absolute ${className}`} style={{ "--slot": slot } as React.CSSProperties} />;
}

function ClayStat({ Icon, tone, iconCls, label, value, meter, caption, alert }:
  { Icon: React.ElementType; tone: string; iconCls: string; label: string; value: React.ReactNode; meter?: number; caption?: string; alert?: boolean }) {
  const valueColor = alert && num(value) > 0 ? (tone.includes("red") ? "text-rose-600" : "text-amber-600") : "text-slate-900";
  return (
    <div className={`fc-clay ${tone} p-4`}>
      <div className="flex items-center justify-between">
        <span className="text-[12px] font-bold text-slate-600">{label}</span>
        <span className="fc-blob"><Icon className={`h-4 w-4 ${iconCls}`} /></span>
      </div>
      <div className={`mt-2 text-[30px] font-black leading-none tracking-tight tabular-nums ${valueColor}`}>{value}</div>
      {meter != null ? (
        <div className="deck-track mt-3">
          <div className="deck-fill deck-fill-teal" style={{ width: `${Math.min(100, meter)}%` }} />
        </div>
      ) : null}
      {caption ? <p className="mt-2 text-[11px] font-medium text-slate-500">{caption}</p> : null}
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
  const color = v >= 85 ? "deck-fill-emerald" : v >= 70 ? "deck-fill-amber" : "deck-fill-red";
  return (
    <div className="flex items-center gap-2.5">
      <div className="deck-track w-20"><div className={`deck-fill ${color}`} style={{ width: `${Math.min(100, v)}%` }} /></div>
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
  if (loading) return <div className="fc-neumo h-24 shrink-0 animate-pulse" />;
  if (!forecast.length && !gaps.length) return null;
  return (
    <div className="grid shrink-0 gap-3 lg:grid-cols-[1.4fr_1fr]">
      <div className="fc-neumo p-4">
        <div className="flex items-center justify-between">
          <div className="section-title inline-flex items-center gap-2"><TrendingUp className="h-3.5 w-3.5 text-teal-700" /> Replacement priority</div>
          <span className="text-[10px] font-bold uppercase tracking-wide text-slate-400">CapEx forecast</span>
        </div>
        <div className="mt-3 space-y-2.5">
          {forecast.map((row, i) => {
            const score = num(g(row, "capexPriorityScore", "capex_priority_score"));
            const pct = Math.min(100, Math.round(score / 2.6));
            const color = score > 180 ? "deck-fill-red" : score > 90 ? "deck-fill-amber" : "deck-fill-teal";
            return (
              <div key={String(row.id ?? i)} className="flex items-center gap-3">
                <span className="w-5 text-xs font-bold text-slate-400 tabular-nums">#{i + 1}</span>
                <div className="w-24 shrink-0 truncate text-sm font-semibold text-slate-800">{String(g(row, "vehicleCode", "vehicle_code") ?? "—")}</div>
                <div className="deck-track flex-1"><div className={`deck-fill ${color}`} style={{ width: `${pct}%` }} /></div>
                <span className="hidden w-28 truncate text-right text-xs text-slate-500 sm:block">{String(g(row, "replacementWindow", "replacement_window") ?? "")}</span>
              </div>
            );
          })}
        </div>
      </div>
      <div className="fc-neumo p-4">
        <div className="section-title inline-flex items-center gap-2"><Sparkles className="h-3.5 w-3.5 text-teal-700" /> Operational gaps</div>
        <div className="mt-3 grid grid-cols-2 gap-2.5">
          {gaps.map((gap, i) => (
            <div key={i} className="deck-inset rounded-xl p-3">
              <div className="text-xl font-black tabular-nums text-slate-900">{num(g(gap, "affectedRecords", "affected_records"))}</div>
              <div className="mt-0.5 text-[11px] font-semibold leading-tight text-slate-500">{String(g(gap, "gapName", "gap_name") ?? "")}</div>
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
  const activeJobs = (detail?.activeJobs as AnyRecord[]) || [];
  const replayTrail = (detail?.replayTrail as AnyRecord[]) || [];
  const speed = num(g(record, "speedMph", "speed_mph"));
  const fresh = freshness(g(record, "lastSeenAt", "last_seen_at"));
  const hasGps = g(record, "lat") != null && g(record, "lng") != null;

  const snapshot = [
    { label: "Status", value: String(g(record, "status") ?? "—") },
    { label: "Driver", value: String(g(record, "assignedDriver", "assigned_driver", "driverName") ?? "Unassigned"), route: "/drivers" },
    { label: "Odometer", value: `${num(g(record, "odometerMiles", "odometer_miles")).toLocaleString()} mi` },
    { label: "Year", value: String(g(record, "year") ?? "—") },
    { label: "Device", value: String(g(record, "deviceStatus", "device_status") ?? "—") },
    { label: "Camera", value: String(g(record, "cameraStatus", "camera_status") ?? "—") },
  ];

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-[2px]" onClick={onClose}>
      <aside className="fleet-console h-full w-full max-w-xl overflow-y-auto border-l border-slate-200 shadow-2xl anim-slide-left" onClick={(e) => e.stopPropagation()}>
        {/* sticky header */}
        <div className="fc-drawer-head sticky top-0 z-10 px-6 py-4">
          <div className="flex items-start justify-between">
            <div>
              <div className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Vehicle</div>
              <h2 className="mt-1 text-xl font-black tracking-tight text-slate-900">{code}</h2>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <StatusPill status={g(record, "status")} />
                <RiskChip tier={riskTier(record)} />
              </div>
            </div>
            <button type="button" aria-label="Close" onClick={onClose} className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700"><X className="h-5 w-5" /></button>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" disabled={!canUpdate} onClick={onEdit} className="btn-primary h-9 px-3 text-xs"><Wrench className="h-3.5 w-3.5" /> Edit</button>
            <button type="button" disabled={!canAssign || assigning} onClick={onAssign} className="btn-ghost h-9 px-3 text-xs"><UserCheck className="h-3.5 w-3.5" /> {assigning ? "Assigning…" : "Smart assign"}</button>
            <button type="button" onClick={() => onNavigate("/map-view")} className="btn-ghost h-9 px-3 text-xs"><MapPin className="h-3.5 w-3.5" /> Live map</button>
            <button type="button" disabled={!canDelete} onClick={onDelete} className="ml-auto inline-flex h-9 items-center gap-1.5 rounded-xl border border-rose-200 px-3 text-xs font-semibold text-rose-600 transition hover:bg-rose-50 disabled:opacity-40"><Trash2 className="h-3.5 w-3.5" /> Delete</button>
          </div>
        </div>

        <div className="space-y-5 p-6">
          {/* Live telemetry gauge cluster — real GPS/speed only */}
          <div className="fc-neumo p-4">
            <div className="flex items-center justify-between">
              <span className="section-title inline-flex items-center gap-2"><Radio className="h-3.5 w-3.5 text-teal-700" /> Live telemetry</span>
              {fresh ? (
                <span className={`inline-flex items-center gap-1.5 text-[11px] font-bold ${fresh.live ? "text-emerald-600" : "text-slate-500"}`}>
                  {fresh.live && <span className="live-dot h-1.5 w-1.5" />}{fresh.label}
                </span>
              ) : <span className="text-[11px] font-semibold italic text-slate-400">No telemetry</span>}
            </div>
            {fresh ? (
              <div className="mt-3 grid grid-cols-3 gap-2.5">
                <Instrument label="Speed" value={Math.round(speed)} unit="mph" tone={speed > 1 ? "text-emerald-600" : "text-slate-500"} />
                <Instrument label="Heading" value={headingLabel(g(record, "heading"))} />
                <Instrument label="Odometer" value={num(g(record, "odometerMiles", "odometer_miles")).toLocaleString()} unit="mi" />
              </div>
            ) : (
              <p className="mt-3 deck-inset rounded-xl px-3 py-3 text-[12px] font-medium text-slate-500">
                No GPS fix reported for this unit yet. Live speed, heading and position appear here once its device sends a location event.
              </p>
            )}
            {hasGps && (
              <div className="mt-2.5 flex items-center gap-1.5 text-[11px] font-medium text-slate-500">
                <MapPin className="h-3.5 w-3.5 text-slate-400" />
                <span className="tabular-nums">{Number(g(record, "lat")).toFixed(4)}, {Number(g(record, "lng")).toFixed(4)}</span>
                <button type="button" onClick={() => onNavigate("/map-view")} className="ml-auto font-bold text-teal-700 hover:underline">Track on map →</button>
              </div>
            )}
          </div>

          {/* snapshot */}
          <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-3">
            {snapshot.map((s) => (
              <button key={s.label} type="button" disabled={!s.route} onClick={() => s.route && onNavigate(s.route)}
                className={`deck-inset rounded-xl p-3 text-left ${s.route ? "transition hover:-translate-y-0.5" : ""}`}>
                <div className="text-[10px] font-bold uppercase tracking-[0.14em] text-slate-400">{s.label}</div>
                <div className="mt-1 flex items-center gap-1 text-sm font-bold text-slate-800">{s.value}{s.route && <ArrowUpRight className="h-3 w-3 text-teal-500" />}</div>
              </button>
            ))}
          </div>

          {/* AI recommendation — from live detail envelope, not fabricated */}
          {recs.length > 0 && (
            <div className="fc-reco p-4">
              <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-teal-700"><Sparkles className="h-3.5 w-3.5" /> Recommended action</div>
              <p className="mt-1.5 text-sm font-bold text-slate-800">{String(recs[0].title ?? "")}</p>
              {recs[0].body ? <p className="mt-1 text-sm text-slate-600">{String(recs[0].body)}</p> : null}
            </div>
          )}

          {/* Active jobs — live work tied to the unit */}
          <DrawerSection title="Active jobs" icon={<Boxes className="h-4 w-4" />} count={activeJobs.length} loading={loading}>
            {activeJobs.length ? (
              <div className="space-y-2">
                {activeJobs.map((j, i) => (
                  <button key={String(j.id ?? i)} type="button" onClick={() => onNavigate("/jobs")} className="deck-alert flex w-full items-center gap-3 px-3 py-2.5 text-left">
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-[13px] font-bold text-slate-800">{String(g(j, "jobNumber", "job_number") ?? `Job ${j.id}`)}</span>
                      <span className="text-[11px] font-semibold text-slate-400">{String(g(j, "status") ?? "—")}{g(j, "eta") ? ` · ETA ${fmt(g(j, "eta"))}` : ""}</span>
                    </span>
                    {g(j, "slaStatus", "sla_status") ? <SlaChip status={String(g(j, "slaStatus", "sla_status"))} /> : null}
                    <ChevronRight className="h-3.5 w-3.5 shrink-0 text-slate-300" />
                  </button>
                ))}
              </div>
            ) : <EmptyLine text="No active jobs assigned to this unit." />}
          </DrawerSection>

          {/* GPS replay trail — recent real location events */}
          <DrawerSection title="GPS replay trail" icon={<Navigation className="h-4 w-4" />} count={replayTrail.length} loading={loading}>
            {replayTrail.length ? (
              <ReplayTrail points={replayTrail} />
            ) : <EmptyLine text="No recent location events recorded for this unit." />}
          </DrawerSection>

          <DrawerTable title="Upcoming maintenance" icon={<Wrench className="h-4 w-4" />} rows={detail?.maintenance as AnyRecord[]} cols={["serviceType", "status", "priority", "dueDate"]} loading={loading} onEmpty="No maintenance items scheduled." />
          <DrawerTable title="Safety events" icon={<ShieldAlert className="h-4 w-4" />} rows={detail?.safetyEvents as AnyRecord[]} cols={["eventNumber", "eventType", "severity", "reviewStatus"]} loading={loading} onEmpty="No safety events on record." />

          {/* Dashcam / video events */}
          <DrawerSection title="Video events" icon={<Video className="h-4 w-4" />} count={((detail?.videoEvents as AnyRecord[]) || []).length} loading={loading}>
            {((detail?.videoEvents as AnyRecord[]) || []).length ? (
              <div className="grid grid-cols-2 gap-2.5">
                {((detail?.videoEvents as AnyRecord[]) || []).slice(0, 4).map((v, i) => (
                  <div key={String(v.id ?? i)} className="deck-inset overflow-hidden rounded-xl">
                    <div className="relative aspect-video bg-slate-800">
                      {g(v, "thumbnailUrl", "thumbnail_url") ? (
                        <img src={String(g(v, "thumbnailUrl", "thumbnail_url"))} alt="" className="h-full w-full object-cover opacity-90" loading="lazy" />
                      ) : <div className="grid h-full place-items-center"><Video className="h-6 w-6 text-slate-500" /></div>}
                      <span className="absolute right-1.5 top-1.5 rounded-md bg-black/60 px-1.5 py-0.5 text-[9px] font-bold uppercase text-white">{String(g(v, "severity") ?? "")}</span>
                    </div>
                    <div className="px-2.5 py-2">
                      <p className="truncate text-[11.5px] font-bold text-slate-800">{String(g(v, "eventType", "event_type") ?? "Event")}</p>
                      <p className="truncate text-[10px] font-semibold text-slate-400">{String(g(v, "reviewStatus", "review_status") ?? "")}</p>
                    </div>
                  </div>
                ))}
              </div>
            ) : <EmptyLine text="No dashcam video events captured." />}
          </DrawerSection>
        </div>
      </aside>
    </div>
  );
}

function Instrument({ label, value, unit, tone = "text-slate-800" }: { label: string; value: React.ReactNode; unit?: string; tone?: string }) {
  return (
    <div className="deck-inset rounded-xl px-3 py-2.5 text-center">
      <div className="text-[9.5px] font-bold uppercase tracking-wider text-slate-400">{label}</div>
      <div className={`mt-0.5 text-[19px] font-black leading-none tabular-nums ${tone}`}>
        {value}{unit && <span className="ml-0.5 text-[10px] font-bold text-slate-400">{unit}</span>}
      </div>
    </div>
  );
}

function headingLabel(heading: unknown): string {
  // Number(null) === 0 (a valid finite value), which would fabricate a "N" heading
  // for units that report no bearing — reject null/empty explicitly first.
  if (heading == null || heading === "") return "—";
  const h = Number(heading);
  if (!Number.isFinite(h)) return "—";
  const dirs = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
  return dirs[Math.round(((h % 360) / 45)) % 8];
}

function SlaChip({ status }: { status: string }) {
  const tone = /risk|breach|late/i.test(status) ? "bg-rose-50 text-rose-700 ring-rose-600/15" : /track|met|ok/i.test(status) ? "bg-emerald-50 text-emerald-700 ring-emerald-600/15" : "bg-slate-100 text-slate-600 ring-slate-500/15";
  return <span className={`shrink-0 rounded-md px-2 py-0.5 text-[10px] font-bold ring-1 ring-inset ${tone}`}>{status}</span>;
}

function ReplayTrail({ points }: { points: AnyRecord[] }) {
  const trail = points.slice(0, 12);
  const maxSpeed = Math.max(1, ...trail.map((p) => num(g(p, "speedMph", "speed_mph"))));
  return (
    <div className="deck-inset rounded-xl p-3">
      <div className="flex items-end gap-1" style={{ height: 44 }}>
        {trail.slice().reverse().map((p, i) => {
          const sp = num(g(p, "speedMph", "speed_mph"));
          const h = Math.max(4, Math.round((sp / maxSpeed) * 40));
          const color = sp > 1 ? "bg-emerald-400" : "bg-slate-300";
          return <div key={i} className={`flex-1 rounded-t ${color}`} style={{ height: h }} title={`${Math.round(sp)} mph`} />;
        })}
      </div>
      <div className="mt-2 flex items-center justify-between text-[10px] font-semibold text-slate-400">
        <span>{trail.length} recent points · speed profile</span>
        <span className="tabular-nums">peak {Math.round(maxSpeed)} mph</span>
      </div>
    </div>
  );
}

function DrawerSection({ title, icon, count, loading, children }: { title: string; icon: React.ReactNode; count: number; loading?: boolean; children: React.ReactNode }) {
  return (
    <section>
      <div className="mb-2 flex items-center justify-between">
        <div className="section-title inline-flex items-center gap-2"><span className="text-teal-700">{icon}</span>{title}</div>
        {count > 0 && <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-bold text-slate-500 tabular-nums">{count}</span>}
      </div>
      {loading ? <div className="skeleton h-12 rounded-xl" /> : children}
    </section>
  );
}

function DrawerTable({ title, icon, rows, cols, loading, onEmpty }: { title: string; icon: React.ReactNode; rows?: AnyRecord[]; cols: string[]; loading?: boolean; onEmpty: string }) {
  const data = rows || [];
  return (
    <DrawerSection title={title} icon={icon} count={data.length} loading={loading}>
      {data.length === 0 ? <EmptyLine text={onEmpty} /> : (
        <div className="deck-inset overflow-hidden rounded-xl">
          <table className="w-full text-left text-xs">
            <thead><tr className="border-b border-slate-200/70 text-[10px] uppercase tracking-wide text-slate-400">
              {cols.map((c) => <th key={c} className="px-3 py-2 font-bold">{labelize(c)}</th>)}
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
    </DrawerSection>
  );
}

function EmptyLine({ text }: { text: string }) {
  return (
    <p className="deck-inset flex items-center gap-2 rounded-xl px-3 py-2.5 text-[12px] font-medium text-slate-400">
      <Info className="h-3.5 w-3.5 shrink-0" />{text}
    </p>
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
  const [errors, setErrors] = useState<string[]>([]);

  useEffect(() => { setForm(initial); }, [initial]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const nextErrors: string[] = [];
    const payload: AnyRecord = { ...form };

    for (const field of FIELDS) {
      const raw = payload[field.key];
      const value = String(raw ?? "").trim();
      if (field.required && value === "") {
        nextErrors.push(`${field.label} is required.`);
        continue;
      }
      if (field.type === "number") {
        if (value === "") {
          payload[field.key] = undefined;
          continue;
        }
        const parsed = Number(value);
        if (Number.isNaN(parsed)) {
          nextErrors.push(`${field.label} must be a valid number.`);
          continue;
        }
        payload[field.key] = parsed;
      } else {
        payload[field.key] = raw;
      }
    }

    if (nextErrors.length > 0) {
      setErrors(nextErrors);
      return;
    }

    setErrors([]);
    onSave(payload);
  };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/40 p-4 backdrop-blur-sm" onClick={onClose}>
      <form onClick={(e) => e.stopPropagation()} onSubmit={submit} className="fc-neumo w-full max-w-xl p-6 anim-fade-up">
        <div className="flex items-start justify-between">
          <div>
            <div className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Fleet</div>
            <h2 className="mt-1 text-xl font-black tracking-tight text-slate-900">{title}</h2>
          </div>
          <button type="button" aria-label="Close" onClick={onClose} className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700"><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-6 grid gap-4 sm:grid-cols-2">
          {FIELDS.map((f) => (
            <label key={f.key} className="block">
              <span className="mb-1.5 block text-xs font-bold text-slate-600">{f.label}{f.required ? <span className="text-rose-500"> *</span> : null}</span>
              {f.type === "select" && f.options ? (
                <select className="fc-search w-full px-3 py-2.5 text-sm text-slate-800 outline-none"
                  required={Boolean(f.required)} value={String(form[f.key] ?? "")} onChange={(e) => setForm((c) => ({ ...c, [f.key]: e.target.value }))}>
                  <option value="">Select</option>
                  {f.options.map((o) => <option key={o} value={o}>{o}</option>)}
                </select>
              ) : (
                <input className="fc-search w-full px-3 py-2.5 text-sm text-slate-800 outline-none placeholder:text-slate-400"
                  type={f.type === "number" ? "number" : "text"} required={Boolean(f.required)}
                  value={String(form[f.key] ?? "")} onChange={(e) => setForm((c) => ({ ...c, [f.key]: e.target.value }))} />
              )}
            </label>
          ))}
        </div>
        {errors.length > 0 && (
          <div className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
            <ul className="space-y-1">
              {errors.map((error) => <li key={error}>{error}</li>)}
            </ul>
          </div>
        )}
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="btn-ghost h-11">Cancel</button>
          <button type="submit" disabled={saving} className="btn-primary h-11"><Save className="h-4 w-4" /> {saving ? "Saving…" : "Save vehicle"}</button>
        </div>
      </form>
    </div>
  );
}
