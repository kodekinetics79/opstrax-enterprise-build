import { type ReactNode, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowRight,
  Camera,
  CheckCircle,
  Clock,
  Gauge,
  Layers,
  MapPin,
  Navigation,
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
import { AiInsightCard, LoadingState, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import type { AnyRecord } from "@/types";

const QUICK_FILTERS = ["All", "Speeding", "Device offline", "Camera offline", "Fleet risk"] as const;

type LayerKey = "vehicles" | "geofences";
type StatusBucket = "Moving" | "Idle" | "Offline";

/** Classify a vehicle into a single live-status bucket — the heart of the status board. */
function statusBucket(entity: AnyRecord): StatusBucket {
  if (entity.isStale) return "Offline";
  const status = String(entity.status ?? "").toLowerCase();
  const speed = Number(entity.speedMph ?? entity.speed_mph ?? 0);
  if (speed > 3 || /active|on route|moving|driving|en route/.test(status)) return "Moving";
  return "Idle";
}

function matchesFilter(entity: AnyRecord, filter: string): boolean {
  if (filter === "All") return true;
  if (filter === "Moving" || filter === "Idle" || filter === "Offline") {
    return statusBucket(entity) === filter;
  }
  const alert = String(entity.liveAlert ?? entity.live_alert ?? "").toLowerCase();
  const risk = String(entity.riskLevel ?? entity.risk_level ?? "").toLowerCase();
  switch (filter) {
    case "Speeding":        return alert.includes("speed");
    case "Device offline":  return alert.includes("device");
    case "Camera offline":  return alert.includes("camera");
    case "Fleet risk":      return alert.includes("risk") || risk === "high";
    default:                return true;
  }
}

/** Compact "2m ago" style freshness from seconds-since-last-ping. */
function freshnessLabel(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds)) return "—";
  const s = Math.max(0, Math.round(seconds));
  if (s < 60) return `${s}s ago`;
  if (s < 3600) return `${Math.floor(s / 60)}m ago`;
  return `${Math.floor(s / 3600)}h ago`;
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
  const [focusId, setFocusId] = useState<string | null>(null);
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

  // Merge the live SSE GPS stream onto the DB entity snapshot so markers move in real time
  // and carry GPS freshness (secondsSincePing / isStale) for the status board and roster.
  const baseEntities = (data?.entities as AnyRecord[]) ?? [];
  const liveEntities = useMemo<AnyRecord[]>(() => {
    if (telemetry.positions.length === 0) return baseEntities;
    const posMap = new Map(telemetry.positions.map((p) => [p.vehicleCode, p]));
    return baseEntities.map((e) => {
      const live = posMap.get(String(e.label ?? e.vehicleCode ?? ""));
      if (!live) return e;
      return {
        ...e,
        lat: live.lat,
        lng: live.lng,
        speedMph: live.speedMph,
        heading: live.heading,
        secondsSincePing: live.secondsSincePing,
        isStale: live.isStale,
      };
    });
  }, [baseEntities, telemetry.positions]);

  // Live status segmentation — Moving / Idle / Offline — the signature fleet-status board.
  const buckets = useMemo(() => {
    const counts: Record<StatusBucket, number> = { Moving: 0, Idle: 0, Offline: 0 };
    for (const e of liveEntities) counts[statusBucket(e)] += 1;
    return counts;
  }, [liveEntities]);

  const visibleEntities = useMemo(
    () =>
      liveEntities
        .filter((e) => matchesFilter(e, activeFilter) && matchesSearch(e, search))
        // Surface what needs attention first: offline, then moving, then idle.
        .sort((a, b) => {
          const order: Record<StatusBucket, number> = { Offline: 0, Moving: 1, Idle: 2 };
          return order[statusBucket(a)] - order[statusBucket(b)];
        }),
    [liveEntities, activeFilter, search],
  );

  const focusOn = (entity: AnyRecord) => {
    setSelected(entity);
    setFocusId(String(entity.id ?? entity.vehicleId ?? entity.vehicle_id ?? entity.label ?? entity.vehicleCode ?? entity.vehicle_code ?? ""));
  };

  if (isLoading || !data) return <LoadingState />;

  const kpis = (data.kpis as AnyRecord) ?? {};
  const geofences = layers.geofences ? ((data.geofences as AnyRecord[]) ?? []) : [];
  const mapEntities = layers.vehicles ? visibleEntities : [];
  const recommendations = (data.recommendations as AnyRecord[]) ?? [];
  const openAlerts = alerts.data ?? [];

  return (
    <div className="space-y-6">
      {/* ── Hero banner ─────────────────────────────────────────── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />

        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Satellite className="h-3 w-3" /> Operations
                </span>
                <span className="relative flex h-2.5 w-2.5">
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60" />
                  <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-emerald-500" />
                </span>
                <span className="text-[11px] font-semibold text-slate-500">
                  {telemetry.connected ? `Live · ${telemetry.positions.length} units` : "Reconnecting"}
                  {telemetry.lastUpdated ? ` · ${telemetry.lastUpdated.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}` : ""}
                </span>
              </div>

              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Live Fleet Map
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Real-time vehicle positions, status and geofences — streamed live from the operational database
              </p>
            </div>

            <div className="flex items-center gap-2">
              {telemetry.connected ? (
                <span className="inline-flex items-center gap-2 rounded-lg bg-white/90 px-4 py-2 text-xs font-bold text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Wifi className="h-3.5 w-3.5" /> GPS live
                </span>
              ) : (
                <span className="inline-flex items-center gap-2 rounded-lg bg-white/90 px-4 py-2 text-xs font-bold text-slate-500 ring-1 ring-slate-200/50 shadow-sm">
                  <WifiOff className="h-3.5 w-3.5" /> {telemetry.error ?? "Connecting…"}
                </span>
              )}
            </div>
          </div>
        </div>
      </header>

      {/* ── Status segmentation ─────────────────────────────────── */}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatusBoardCard label="All Units"     count={liveEntities.length} tone="slate"  meaning="Tracked"        active={activeFilter === "All"}     onClick={() => setActiveFilter("All")} />
        <StatusBoardCard label="Moving"        count={buckets.Moving}      tone="teal"   meaning="On the road"    active={activeFilter === "Moving"}  onClick={() => setActiveFilter(activeFilter === "Moving" ? "All" : "Moving")} />
        <StatusBoardCard label="Idle / Parked" count={buckets.Idle}        tone="indigo" meaning="Stopped, live"  active={activeFilter === "Idle"}    onClick={() => setActiveFilter(activeFilter === "Idle" ? "All" : "Idle")} />
        <StatusBoardCard label="Offline"       count={buckets.Offline}     tone="rose"   meaning="No recent GPS"  active={activeFilter === "Offline"} onClick={() => setActiveFilter(activeFilter === "Offline" ? "All" : "Offline")} />
      </div>

      <div className="grid items-stretch gap-5 xl:grid-cols-[1.55fr_.45fr]">
        {/* ── Hero map ────────────────────────────────────────────── */}
        <section className="lm-map-panel">
          <div className="lm-map-header">
            <h2 className="lm-map-title">Live Operations Map</h2>
            <div className="lm-meta-stats">
              <span className="lm-meta-stat">
                <Satellite className="lm-meta-stat-icon text-teal-500" />
                <span className="lm-meta-stat-value">{String(kpis.onlineDevices ?? 0)}</span>
                <span>devices</span>
              </span>
              <span className="lm-meta-stat">
                <Camera className="lm-meta-stat-icon text-violet-500" />
                <span className="lm-meta-stat-value">{String(kpis.onlineCameras ?? 0)}</span>
                <span>cameras</span>
              </span>
              <span className="lm-meta-stat">
                <Gauge className="lm-meta-stat-icon text-blue-500" />
                <span className="lm-meta-stat-value">{String(kpis.telemetryQuality ?? "--")}</span>
                <span>quality</span>
              </span>
              <span className="lm-meta-stat">
                <ShieldAlert className="lm-meta-stat-icon text-amber-500" />
                <span className="lm-meta-stat-value">{String(kpis.speedAlerts ?? 0)}</span>
                <span>speed alerts</span>
              </span>
            </div>
          </div>

          <div className="lm-filter-bar">
            {QUICK_FILTERS.map((filter) => (
              <button
                type="button"
                key={filter}
                className={filter === activeFilter ? "fh-chip fh-chip-active" : "fh-chip"}
                onClick={() => setActiveFilter(activeFilter === filter ? "All" : filter)}
              >
                {filter}
              </button>
            ))}
            <span className="lm-filter-divider" />
            <LayerToggle label="Vehicles" icon={<Truck className="lm-layer-toggle-icon" />} active={layers.vehicles} onClick={() => setLayers((l) => ({ ...l, vehicles: !l.vehicles }))} />
            <LayerToggle label="Geofences" icon={<Layers className="lm-layer-toggle-icon" />} active={layers.geofences} onClick={() => setLayers((l) => ({ ...l, geofences: !l.geofences }))} />
            <span className="lm-filter-count">{visibleEntities.length} of {liveEntities.length} shown</span>
          </div>

          <div className="lm-map-surface">
            <LiveMap entities={mapEntities} geofences={geofences} onSelect={setSelected} focusId={focusId} />
          </div>
        </section>

        {/* ── Right sidebar: roster + alerts ─────────────────────── */}
        <aside className="lm-sidebar-panel">
          <div className="lm-sidebar-header">
            <div className="lm-sidebar-title-row">
              <h2 className="lm-sidebar-title">Live Roster</h2>
              <span className={`lm-live-badge ${telemetry.connected ? "text-teal-600" : "text-slate-400"}`}>
                <span className={`lm-live-dot ${telemetry.connected ? "bg-teal-500" : "bg-slate-300"}`} />
                {telemetry.connected ? "Live" : "Reconnecting"}
              </span>
            </div>
            <div className="lm-search-wrap">
              <Search className="lm-search-icon" />
              <input
                className="lm-search-input"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Vehicle code, driver, status…"
              />
            </div>
          </div>

          <div className="lm-roster-list">
            {visibleEntities.length === 0 ? (
              <p className="lm-roster-empty">No vehicles match the current filters.</p>
            ) : (
              visibleEntities.map((entity) => (
                <RosterRow key={String(entity.id ?? entity.vehicleId ?? entity.label)} entity={entity} onClick={() => focusOn(entity)} />
              ))
            )}
          </div>

          <div className="lm-alerts-section">
            <h3 className="lm-alerts-title">
              Telemetry Alerts{openAlerts.length > 0 ? ` · ${openAlerts.length}` : ""}
            </h3>
            <div className="lm-alerts-list">
              {openAlerts.length === 0 ? (
                <p className="lm-alerts-empty">
                  <CheckCircle className="lm-alerts-empty-icon" /> No open alerts.
                </p>
              ) : (
                openAlerts.slice(0, 6).map((alert) => (
                  <TelemetryAlertRow
                    key={String(alert.id)}
                    alert={alert}
                    onAck={() => ackAlert.mutate(Number(alert.id))}
                    onResolve={() => resolveAlert.mutate(Number(alert.id))}
                  />
                ))
              )}
            </div>
          </div>
        </aside>
      </div>

      {recommendations.length > 0 && (
        <div className="lm-recommendations">
          {recommendations.slice(0, 2).map((item) => <AiInsightCard key={String(item.id)} insight={item} />)}
        </div>
      )}

      <VehicleDetailDrawer detail={detail.data} loading={detail.isLoading} onClose={() => { setSelected(null); setFocusId(null); }} />
    </div>
  );
}

const BOARD_TONES: Record<string, { dot: string; text: string }> = {
  slate:  { dot: "bg-slate-400",   text: "text-slate-700" },
  teal:   { dot: "bg-teal-500",    text: "text-teal-700" },
  indigo: { dot: "bg-indigo-500",  text: "text-indigo-700" },
  rose:   { dot: "bg-rose-500",    text: "text-rose-700" },
};

function StatusBoardCard({ label, count, tone, meaning, active, onClick }: { label: string; count: number; tone: keyof typeof BOARD_TONES; meaning: string; active: boolean; onClick: () => void }) {
  const t = BOARD_TONES[tone];
  const iconMap: Record<string, React.ElementType> = {
    slate: Gauge, teal: Truck, indigo: Clock, rose: WifiOff,
  };
  const Icon = iconMap[tone] ?? Gauge;
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active ? "true" : "false"}
      className={`fo-kpi-card ${active ? "fo-kpi-active" : ""}`}
    >
      <div className="fo-kpi-icon fo-kpi-icon-inactive">
        <Icon className="h-5 w-5 text-teal-600" />
      </div>
      <div className="min-w-0 flex-1">
        <div className="fo-kpi-count">
          <span className={`fo-kpi-dot ${t.dot} ${tone === "teal" ? "animate-pulse" : ""}`} />
          <span className={active ? t.text : "text-slate-900"}>{count}</span>
        </div>
        <p className={`fo-kpi-label ${active ? t.text : "text-slate-500"}`}>{label}</p>
        <p className="mt-0.5 text-[11px] text-slate-400">{meaning}</p>
      </div>
    </button>
  );
}

const ROSTER_DOT: Record<StatusBucket, string> = {
  Moving: "bg-teal-500",
  Idle: "bg-indigo-400",
  Offline: "bg-rose-500",
};

function RosterRow({ entity, onClick }: { entity: AnyRecord; onClick: () => void }) {
  const bucket = statusBucket(entity);
  const speed = Number(entity.speedMph ?? entity.speed_mph ?? 0);
  const driver = String(entity.driverName ?? entity.driver_name ?? "Unassigned");
  const label = String(entity.label ?? entity.vehicleCode ?? "Vehicle");
  const fresh = freshnessLabel(entity.secondsSincePing as number | null | undefined);
  return (
    <button
      type="button"
      onClick={onClick}
      className="lm-roster-row"
    >
      <span className={`lm-roster-dot ${ROSTER_DOT[bucket]} ${bucket === "Moving" ? "animate-pulse" : ""}`} />
      <div className="min-w-0 flex-1">
        <div className="flex items-center justify-between gap-2">
          <p className="lm-roster-name">{label}</p>
          <RiskBadge risk={entity.riskLevel ?? entity.risk_level} />
        </div>
        <p className="lm-roster-driver">{driver}</p>
        <div className="lm-roster-meta">
          {bucket === "Moving" ? (
            <span className="lm-roster-speed"><Navigation className="lm-roster-speed-icon" />{Math.round(speed)} mph</span>
          ) : (
            <span className={bucket === "Offline" ? "text-rose-500" : "text-indigo-500"}>{bucket === "Offline" ? "Offline" : "Idle"}</span>
          )}
          <span>·</span>
          <span>{fresh}</span>
        </div>
      </div>
    </button>
  );
}

function LayerToggle({ label, icon, active, onClick }: { label: string; icon: ReactNode; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`lm-layer-toggle ${active ? "lm-layer-toggle-active" : ""}`}
    >
      {icon}
      {label}
    </button>
  );
}

function MetaStat({ icon, value, label }: { icon: ReactNode; value: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      {icon}
      <span className="font-bold text-slate-700">{value}</span>
      <span className="text-slate-400">{label}</span>
    </span>
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
  const sevClass = {
    High: "lm-alert-card-high",
    Critical: "lm-alert-card-critical",
    Warning: "lm-alert-card-warning",
    Low: "lm-alert-card-low",
  }[sev] ?? "lm-alert-card-warning";
  return (
    <div className={`lm-alert-card ${sevClass}`}>
      <div className="lm-alert-header">
        <ShieldAlert className="lm-alert-icon" />
        <div className="min-w-0 flex-1">
          <p className="lm-alert-title">{type.replace(/_/g, " ")}{vehicle ? ` · ${vehicle}` : ""}</p>
          <p className="lm-alert-message">{msg}</p>
        </div>
      </div>
      <div className="lm-alert-actions">
        <button type="button" onClick={onAck} className="fh-btn-ghost py-1 text-[11px]"><CheckCircle className="h-3 w-3" /> Ack</button>
        <button type="button" onClick={onResolve} className="fh-btn-ghost py-1 text-[11px]"><X className="h-3 w-3" /> Resolve</button>
      </div>
    </div>
  );
}

function VehicleDetailDrawer({ detail, loading, onClose }: { detail?: AnyRecord; loading: boolean; onClose: () => void }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;
  return (
    <div className="lm-drawer-overlay" onClick={onClose}>
      <aside className="lm-drawer-panel" onClick={(e) => e.stopPropagation()}>
        <button type="button" aria-label="Close" className="lm-drawer-close" onClick={onClose}><X className="lm-drawer-close-icon" /></button>
        <p className="lm-drawer-eyebrow">Live Vehicle Detail</p>
        <h2 className="lm-drawer-vehicle-code">{String(record.vehicleCode ?? record.vehicle_code)}</h2>
        <div className="lm-drawer-badges">
          <StatusBadge status={record.status} />
          <RiskBadge risk={record.riskScore ?? record.risk_score} />
          <span className="inline-flex items-center gap-1 rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs font-semibold text-slate-600">
            <MapPin className="h-3 w-3" />Last seen {String(record.lastSeenAt ?? record.last_seen_at ?? "--")}
          </span>
        </div>
        <div className="lm-drawer-grid">
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
    <section className="lm-mini-card">
      <h3 className="lm-mini-title">{title}</h3>
      <div className="lm-mini-rows">
        {rows.length === 0 ? <p className="lm-mini-empty">No data.</p> : rows.map(({ key, value }) => (
          <p key={key} className="lm-mini-row"><span className="lm-mini-label">{labelize(key.replace(/_/g, ""))}:</span> {String(value)}</p>
        ))}
      </div>
    </section>
  );
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return (
    <section className="lm-grid-card">
      <h3 className="lm-grid-title"><Route className="lm-grid-title-icon" />{title}</h3>
      {!rows.length ? (
        <p className="lm-grid-empty">No records yet.</p>
      ) : (
        <div className="lm-grid-table-wrap">
          <table className="lm-grid-table">
            <thead>
              <tr>{columns.map((c) => <th key={c}>{labelize(c)}</th>)}</tr>
            </thead>
            <tbody>
              {rows.slice(0, 10).map((row, i) => (
                <tr key={String(row.id ?? i)}>{columns.map((c) => <td key={c}>{String(row[c] ?? "--")}</td>)}</tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
