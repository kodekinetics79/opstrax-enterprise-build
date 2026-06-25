import { type ReactNode, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Camera,
  CheckCircle,
  Gauge,
  Layers,
  MapPin,
  Navigation,
  RadioTower,
  Route,
  Satellite,
  Search,
  ShieldAlert,
  Truck,
  Wifi,
  WifiOff,
  X,
} from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { controlTowerApi } from "@/services/controlTowerApi";
import { LiveMap } from "@/components/LiveMap";
import { useLiveTelemetry } from "@/hooks/useLiveTelemetry";
import { AiInsightCard, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import type { AnyRecord } from "@/types";

const QUICK_FILTERS = ["All", "Speeding", "Device offline", "Camera offline", "Fleet risk", "Moving", "Idle"] as const;

type LayerKey = "vehicles" | "geofences";

function matchesFilter(entity: AnyRecord, filter: string): boolean {
  if (filter === "All") return true;
  const alert = String(entity.liveAlert ?? entity.live_alert ?? "").toLowerCase();
  const status = String(entity.status ?? "").toLowerCase();
  const risk = String(entity.riskLevel ?? entity.risk_level ?? "").toLowerCase();
  switch (filter) {
    case "Speeding":        return alert.includes("speed");
    case "Device offline":  return alert.includes("device");
    case "Camera offline":  return alert.includes("camera");
    case "Fleet risk":      return alert.includes("risk") || risk === "high";
    case "Moving":          return /active|on route|moving|driving/.test(status);
    case "Idle":            return /idle|parked|stopped|available/.test(status);
    default:                return true;
  }
}

function matchesSearch(entity: AnyRecord, q: string): boolean {
  if (!q) return true;
  const haystack = [
    entity.label, entity.vehicleCode, entity.vehicle_code,
    entity.driverName, entity.driver_name, entity.status, entity.liveAlert,
  ].map((v) => String(v ?? "")).join(" ").toLowerCase();
  return haystack.includes(q.toLowerCase());
}

export function LiveMapPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [activeFilter, setActiveFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [layers, setLayers] = useState<Record<LayerKey, boolean>>({ vehicles: true, geofences: true });

  const { data, isLoading } = useQuery({
    queryKey: ["live-map", "summary"],
    queryFn: controlTowerApi.summary,
    refetchInterval: 15_000,
  });
  const telemetry = useLiveTelemetry();
  const qc = useQueryClient();

  const detail = useQuery({
    queryKey: ["live-map", "entity", selected?.vehicleId ?? selected?.id],
    queryFn: () => controlTowerApi.entity("vehicle", (selected?.vehicleId ?? selected?.id) as string | number),
    enabled: Boolean(selected?.vehicleId ?? selected?.id),
  });

  const alerts = useQuery({
    queryKey: ["telemetry-alerts"],
    queryFn: () => unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/alerts?status=Open")),
    refetchInterval: 30_000,
  });
  const ackAlert = useMutation({
    mutationFn: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/telemetry/alerts/${id}/acknowledge`)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["telemetry-alerts"] }),
  });
  const resolveAlert = useMutation({
    mutationFn: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/telemetry/alerts/${id}/resolve`)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["telemetry-alerts"] }),
  });

  // Merge the live SSE GPS stream onto the DB entity snapshot so markers move in real time.
  const baseEntities = (data?.entities as AnyRecord[]) ?? [];
  const liveEntities = useMemo<AnyRecord[]>(() => {
    if (telemetry.positions.length === 0) return baseEntities;
    const posMap = new Map(telemetry.positions.map((p) => [p.vehicleCode, p]));
    return baseEntities.map((e) => {
      const live = posMap.get(String(e.label ?? e.vehicleCode ?? ""));
      if (!live) return e;
      return { ...e, lat: live.lat, lng: live.lng, speedMph: live.speedMph, heading: live.heading };
    });
  }, [baseEntities, telemetry.positions]);

  const visibleEntities = useMemo(
    () => liveEntities.filter((e) => matchesFilter(e, activeFilter) && matchesSearch(e, search)),
    [liveEntities, activeFilter, search],
  );

  if (isLoading || !data) return <LoadingState />;

  const kpis = (data.kpis as AnyRecord) ?? {};
  const geofences = layers.geofences ? ((data.geofences as AnyRecord[]) ?? []) : [];
  const mapEntities = layers.vehicles ? visibleEntities : [];
  const recommendations = (data.recommendations as AnyRecord[]) ?? [];
  const openAlerts = alerts.data ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Operations"
        title="Live Fleet Map"
        description="Real-time vehicle positions, geofences and driver locations streamed live from the fleet — backed by the operational database and GPS telemetry."
        actions={
          telemetry.connected ? (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-teal-50 px-3 py-1.5 text-xs font-semibold text-teal-700">
              <Wifi className="h-3.5 w-3.5" /> GPS stream live · {telemetry.positions.length} vehicles
            </span>
          ) : (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-slate-100 px-3 py-1.5 text-xs font-semibold text-slate-500">
              <WifiOff className="h-3.5 w-3.5" /> {telemetry.error ?? "Connecting to stream…"}
            </span>
          )
        }
      />

      <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-6">
        <KpiCard label="Tracked Vehicles" value={String(kpis.trackedEntities ?? liveEntities.length)} status="Active" />
        <KpiCard label="Online Devices" value={String(kpis.onlineDevices ?? 0)} status="Live" />
        <KpiCard label="Online Cameras" value={String(kpis.onlineCameras ?? 0)} status="Live" />
        <KpiCard label="Telemetry Quality" value={String(kpis.telemetryQuality ?? "--")} status="Healthy" />
        <KpiCard label="High Risk Units" value={String(kpis.highRiskUnits ?? 0)} status="Review" />
        <KpiCard label="Speed Alerts" value={String(kpis.speedAlerts ?? 0)} status="Warning" />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.4fr_.6fr]">
        <section className="panel p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="section-title">Live Operations Map</h2>
            <span className="rounded-full border border-blue-100 bg-blue-50 px-3 py-1 text-xs font-bold text-blue-700">
              Showing {visibleEntities.length} of {liveEntities.length} tracked units
            </span>
          </div>

          <div className="mt-3 flex flex-wrap items-center gap-2">
            {QUICK_FILTERS.map((filter) => (
              <button
                type="button"
                key={filter}
                className={filter === activeFilter ? "btn-primary py-1.5 text-xs" : "btn-ghost py-1.5 text-xs"}
                onClick={() => setActiveFilter(filter)}
              >
                {filter}
              </button>
            ))}
            <span className="mx-1 h-5 w-px bg-slate-200" />
            <LayerToggle label="Vehicles" icon={<Truck className="h-3.5 w-3.5" />} active={layers.vehicles} onClick={() => setLayers((l) => ({ ...l, vehicles: !l.vehicles }))} />
            <LayerToggle label="Geofences" icon={<Layers className="h-3.5 w-3.5" />} active={layers.geofences} onClick={() => setLayers((l) => ({ ...l, geofences: !l.geofences }))} />
          </div>

          <div className="map-surface mt-4 h-[640px]">
            <LiveMap entities={mapEntities} geofences={geofences} onSelect={setSelected} />
          </div>
        </section>

        <aside className="space-y-6">
          <section className="panel p-5">
            <h2 className="section-title">Find a Unit</h2>
            <div className="relative mt-3">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                className="field pl-10"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Vehicle code, driver, status…"
              />
            </div>
            <div className="mt-4 max-h-64 space-y-2 overflow-y-auto pr-1">
              {visibleEntities.length === 0 ? (
                <p className="text-sm text-slate-500">No vehicles match the current filters.</p>
              ) : (
                visibleEntities.map((entity) => (
                  <button
                    type="button"
                    key={String(entity.id ?? entity.vehicleId ?? entity.label)}
                    onClick={() => setSelected(entity)}
                    className="flex w-full items-center justify-between gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-left transition hover:border-blue-300 hover:bg-blue-50/40"
                  >
                    <div className="min-w-0">
                      <p className="truncate text-sm font-bold text-slate-900">{String(entity.label ?? entity.vehicleCode ?? "Vehicle")}</p>
                      <p className="truncate text-xs text-slate-500">{String(entity.driverName ?? entity.driver_name ?? "Unassigned")} · {String(entity.status ?? "--")}</p>
                    </div>
                    <RiskBadge risk={entity.riskLevel ?? entity.risk_level} />
                  </button>
                ))
              )}
            </div>
          </section>

          <section className="panel p-5">
            <div className="flex items-center justify-between">
              <h2 className="section-title">Live Status</h2>
              <StatusBadge status={telemetry.connected ? "Connected" : "Reconnecting"} />
            </div>
            <div className="mt-4 grid grid-cols-2 gap-3 text-sm">
              <StatTile icon={<Satellite className="h-4 w-4 text-teal-600" />} label="Online devices" value={String(kpis.onlineDevices ?? 0)} />
              <StatTile icon={<Camera className="h-4 w-4 text-violet-600" />} label="Online cameras" value={String(kpis.onlineCameras ?? 0)} />
              <StatTile icon={<Gauge className="h-4 w-4 text-blue-600" />} label="Fleet readiness" value={String(kpis.fleetReadiness ?? "--")} />
              <StatTile icon={<RadioTower className="h-4 w-4 text-emerald-600" />} label="Last GPS sync" value={telemetry.lastUpdated ? telemetry.lastUpdated.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "--"} />
            </div>
          </section>

          <section className="panel p-5">
            <h2 className="section-title">Telemetry Alerts{openAlerts.length > 0 ? ` (${openAlerts.length})` : ""}</h2>
            <div className="mt-4">
              {openAlerts.length === 0 ? (
                <p className="flex items-center gap-2 text-sm text-slate-500"><CheckCircle className="h-4 w-4 text-teal-600" /> No open alerts.</p>
              ) : (
                <div className="space-y-3">
                  {openAlerts.slice(0, 6).map((alert) => (
                    <TelemetryAlertRow
                      key={String(alert.id)}
                      alert={alert}
                      onAck={() => ackAlert.mutate(Number(alert.id))}
                      onResolve={() => resolveAlert.mutate(Number(alert.id))}
                    />
                  ))}
                </div>
              )}
            </div>
          </section>

          {recommendations.slice(0, 2).map((item) => <AiInsightCard key={String(item.id)} insight={item} />)}
        </aside>
      </div>

      <VehicleDetailDrawer detail={detail.data} loading={detail.isLoading} onClose={() => setSelected(null)} />
    </div>
  );
}

function LayerToggle({ label, icon, active, onClick }: { label: string; icon: ReactNode; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1.5 text-xs font-semibold transition ${
        active ? "border-blue-200 bg-blue-50 text-blue-700" : "border-slate-200 bg-white text-slate-400"
      }`}
    >
      {icon}
      {label}
    </button>
  );
}

function StatTile({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-100 bg-slate-50 p-3">
      <div className="flex items-center gap-1.5 text-xs text-slate-500">{icon}{label}</div>
      <p className="mt-1 text-lg font-bold text-slate-900">{value}</p>
    </div>
  );
}

const SEVERITY_STYLES: Record<string, string> = {
  High: "border-red-200 bg-red-50",
  Critical: "border-red-300 bg-red-100",
  Warning: "border-amber-200 bg-amber-50",
  Low: "border-slate-200 bg-slate-50",
};

function TelemetryAlertRow({ alert, onAck, onResolve }: { alert: AnyRecord; onAck: () => void; onResolve: () => void }) {
  const type = String(alert.alertType ?? alert.alert_type ?? "");
  const sev = String(alert.severity ?? "Warning");
  const msg = String(alert.message ?? "");
  const vehicle = String(alert.vehicleCode ?? alert.vehicle_code ?? "");
  return (
    <div className={`rounded-xl border p-3 ${SEVERITY_STYLES[sev] ?? SEVERITY_STYLES.Warning}`}>
      <div className="flex items-start gap-2">
        <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-slate-500" />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold text-slate-900">{type.replace(/_/g, " ")}{vehicle ? ` · ${vehicle}` : ""}</p>
          <p className="mt-0.5 text-xs leading-relaxed text-slate-600">{msg}</p>
        </div>
      </div>
      <div className="mt-2 flex gap-2">
        <button type="button" onClick={onAck} className="btn-ghost flex items-center gap-1 px-2 py-1 text-xs"><CheckCircle className="h-3 w-3" /> Ack</button>
        <button type="button" onClick={onResolve} className="btn-ghost flex items-center gap-1 px-2 py-1 text-xs"><X className="h-3 w-3" /> Resolve</button>
      </div>
    </div>
  );
}

function VehicleDetailDrawer({ detail, loading, onClose }: { detail?: AnyRecord; loading: boolean; onClose: () => void }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm" onClick={onClose}>
      <aside className="h-full w-full max-w-3xl overflow-y-auto border-l border-slate-200 bg-white p-6 shadow-2xl" onClick={(e) => e.stopPropagation()}>
        <button type="button" aria-label="Close" className="float-right icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        <p className="section-title">Live Vehicle Detail</p>
        <h2 className="mt-3 text-2xl font-semibold text-slate-900">{String(record.vehicleCode ?? record.vehicle_code)}</h2>
        <div className="mt-4 flex flex-wrap gap-2">
          <StatusBadge status={record.status} />
          <RiskBadge risk={record.riskScore ?? record.risk_score} />
          <span className="badge"><MapPin className="mr-1 inline h-3 w-3" />Last seen {String(record.lastSeenAt ?? record.last_seen_at ?? "--")}</span>
        </div>
        <div className="mt-6 grid gap-4 lg:grid-cols-2">
          <Mini title="Live Telemetry" record={record} keys={["driverName", "driver_name", "lat", "lng", "speedMph", "speed_mph", "heading", "deviceStatus", "device_status", "cameraStatus", "camera_status"]} />
          <Mini title="Health & Diagnostics" record={record} keys={["readinessScore", "readiness_score", "dataQualityScore", "data_quality_score", "riskScore", "risk_score", "odometerMiles", "odometer_miles", "type"]} />
        </div>
        <Grid title="Active Jobs / SLA" rows={(detail?.activeJobs as AnyRecord[]) ?? []} columns={["jobNumber", "status", "slaStatus", "eta", "priority"]} />
        <Grid title="Safety Events" rows={(detail?.safetyEvents as AnyRecord[]) ?? []} columns={["eventNumber", "eventType", "severity", "reviewStatus", "occurredAt"]} />
        <Grid title="Maintenance Watch" rows={(detail?.maintenance as AnyRecord[]) ?? []} columns={["serviceType", "status", "priority", "dueDate", "riskScore"]} />
        <Grid title="Replay Trail" rows={(detail?.replayTrail as AnyRecord[]) ?? []} columns={["lat", "lng", "speedMph", "heading", "eventType", "eventTime"]} />
      </aside>
    </div>
  );
}

function Mini({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  // De-dupe camelCase/snake_case key pairs, keeping whichever value is present.
  const seen = new Set<string>();
  const rows = keys
    .map((key) => ({ key, value: record[key] }))
    .filter(({ key, value }) => {
      const norm = key.replace(/_/g, "").toLowerCase();
      if (seen.has(norm) || value == null || value === "") return false;
      seen.add(norm);
      return true;
    });
  return (
    <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
      <h3 className="section-title">{title}</h3>
      <div className="mt-3 space-y-2">
        {rows.length === 0 ? <p className="text-sm text-slate-500">No data.</p> : rows.map(({ key, value }) => (
          <p key={key} className="text-sm text-slate-600"><span className="text-slate-500">{labelize(key.replace(/_/g, ""))}:</span> {String(value)}</p>
        ))}
      </div>
    </section>
  );
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return (
    <section className="mt-6 rounded-2xl border border-slate-200 bg-slate-50 p-4">
      <h3 className="section-title flex items-center gap-2"><Route className="h-4 w-4 text-slate-400" />{title}</h3>
      {!rows.length ? (
        <p className="mt-3 text-sm text-slate-500">No records yet.</p>
      ) : (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[680px] text-left text-sm">
            <thead className="text-xs uppercase tracking-[0.16em] text-slate-500">
              <tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr>
            </thead>
            <tbody className="divide-y divide-slate-200">
              {rows.slice(0, 10).map((row, i) => (
                <tr key={String(row.id ?? i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-600">{String(row[c] ?? "--")}</td>)}</tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
