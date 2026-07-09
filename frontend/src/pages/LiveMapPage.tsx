import { type ReactNode, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Camera,
  CheckCircle,
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
import { routesApi } from "@/services/routesApi";
import { telemetryApi } from "@/services/telemetryApi";
import { LiveMap } from "@/components/LiveMap";
import { useLiveTelemetry } from "@/hooks/useLiveTelemetry";
import { AiInsightCard, ErrorState, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import type { AnyRecord } from "@/types";

const QUICK_FILTERS = ["All", "Speeding", "Device offline", "Camera offline", "Fleet risk"] as const;

type LayerKey = "vehicles" | "geofences";
type StatusBucket = "Moving" | "Idle" | "Offline";
type RouteTrail = {
  id: string;
  label: string;
  points: Array<[number, number]>;
  color?: string;
  summary?: string;
};

function hasValidPosition(entity: AnyRecord): boolean {
  const lat = Number(entity.lat ?? entity.latitude);
  const lng = Number(entity.lng ?? entity.longitude);
  return Number.isFinite(lat) && Number.isFinite(lng) && lat !== 0 && lng !== 0;
}

function extractPoint(entity: AnyRecord): [number, number] | null {
  const lat = Number(entity.lat ?? entity.latitude ?? entity.centerLat ?? entity.center_lat);
  const lng = Number(entity.lng ?? entity.longitude ?? entity.centerLng ?? entity.center_lng);
  return Number.isFinite(lat) && Number.isFinite(lng) && lat !== 0 && lng !== 0 ? [lat, lng] : null;
}

function haversineMiles(a: [number, number], b: [number, number]): number {
  const toRad = (value: number) => (value * Math.PI) / 180;
  const [lat1, lon1] = a;
  const [lat2, lon2] = b;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const s1 = Math.sin(dLat / 2);
  const s2 = Math.sin(dLon / 2);
  const c = s1 * s1 + Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * s2 * s2;
  return 3958.8 * 2 * Math.atan2(Math.sqrt(c), Math.sqrt(Math.max(0, 1 - c)));
}

function routeTrailMiles(points: Array<[number, number]>): number {
  if (points.length < 2) return 0;
  return points.slice(1).reduce((sum, point, index) => sum + haversineMiles(points[index], point), 0);
}

function formatDistance(miles: number): string {
  if (!Number.isFinite(miles) || miles <= 0) return "0 mi";
  return miles < 10 ? `${miles.toFixed(1)} mi` : `${Math.round(miles)} mi`;
}

/** Classify a vehicle into a single live-status bucket — the heart of the status board. */
function statusBucket(entity: AnyRecord): StatusBucket {
  if (!hasValidPosition(entity) || entity.isStale) return "Offline";
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
  const [selectedRouteId, setSelectedRouteId] = useState<string | null>(null);

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ["telemetry", "live-map-summary"],
    queryFn: telemetryApi.liveMapSummary,
    refetchInterval: 15_000,
  });
  const routesQ = useQuery({
    queryKey: ["routes"],
    queryFn: routesApi.list,
    refetchInterval: 60_000,
  });
  const liveStatesQ = useQuery({
    queryKey: ["telemetry", "live-states"],
    queryFn: telemetryApi.liveStates,
    refetchInterval: 30_000,
  });
  const telemetry = useLiveTelemetry();
  const qc = useQueryClient();

  // Reverse-geocode live positions to street addresses (server-side, cached + billing-
  // aware: only geocodes positions with no address or that moved >~55 m). Runs once on
  // mount and every 60s; best-effort — a missing/undconfigured Maps key just no-ops and
  // the map falls back to coordinates. After a pass, refresh positions so labels appear.
  useEffect(() => {
    let cancelled = false;
    const run = async () => {
      try {
        await apiClient.post("/api/maps/reverse-geocode-positions?limit=40");
        if (!cancelled) await telemetry.refresh();
      } catch { /* no Maps key / transient — map still works on coordinates */ }
    };
    void run();
    const timer = window.setInterval(run, 60_000);
    return () => { cancelled = true; window.clearInterval(timer); };
    // telemetry.refresh is stable; run once on mount + interval.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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

  const routeRows = (routesQ.data as AnyRecord[]) ?? [];
  useEffect(() => {
    if (selectedRouteId && routeRows.some((route) => String(route.id) === selectedRouteId)) return;
    const preferred =
      routeRows.find((route) => /active|planned|at risk|delayed/i.test(String(route.status ?? ""))) ??
      routeRows.find((route) => Number(route.stopCount ?? route.stops ?? 0) > 0) ??
      routeRows[0] ??
      null;
    setSelectedRouteId(preferred ? String(preferred.id) : null);
  }, [routeRows, selectedRouteId]);

  const selectedRoute = useMemo(
    () => routeRows.find((route) => String(route.id) === selectedRouteId) ?? null,
    [routeRows, selectedRouteId],
  );

  const routeStopsQ = useQuery({
    queryKey: ["routes", "stops", selectedRouteId],
    queryFn: () => routesApi.stops(selectedRouteId ?? ""),
    enabled: Boolean(selectedRouteId),
    refetchInterval: 60_000,
  });

  const routePreviewQ = useQuery({
    queryKey: ["routes", "optimize-preview", selectedRouteId],
    queryFn: () => routesApi.optimizePreview(selectedRouteId ?? ""),
    enabled: Boolean(selectedRouteId),
    refetchInterval: 120_000,
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
        // Reverse-geocoded street address for the map popup (may be undefined until the
        // reverse-geocode pass has run for this position).
        address: (live as { address?: string }).address,
      };
    });
  }, [baseEntities, telemetry.positions]);

  const routeStops = (routeStopsQ.data as AnyRecord[]) ?? [];
  const routeTrail = useMemo<RouteTrail[]>(() => {
    const points = routeStops
      .map((stop) => ({
        point: extractPoint(stop),
        order: Number(stop.stopSequence ?? stop.sequenceNo ?? stop.stop_sequence ?? 0),
      }))
      .filter((entry) => entry.point !== null)
      .sort((a, b) => a.order - b.order)
      .map((entry) => entry.point as [number, number]);

    if (points.length < 2) return [];

    return [{
      id: String(selectedRoute?.id ?? selectedRouteId ?? "route"),
      label: String(selectedRoute?.routeCode ?? selectedRoute?.routeName ?? selectedRoute?.name ?? "Selected route"),
      points,
      color: "#0ea5e9",
      summary: `${points.length} geo stops · ${formatDistance(routeTrailMiles(points))} span`,
    }];
  }, [routeStops, selectedRoute, selectedRouteId]);

  const assetStates = (liveStatesQ.data as AnyRecord[]) ?? [];
  const assetHealth = useMemo(() => {
    const source = assetStates.length > 0 ? assetStates : liveEntities;
    const stale = source.filter((row) => String(row.telemetryStatus ?? row.telemetry_status ?? "").toLowerCase() === "stale" || Boolean(row.isStale)).length;
    const highRisk = source.filter((row) => String(row.riskLevel ?? row.risk_level ?? "").toLowerCase() === "high").length;
    const watch = source.filter((row) => {
      const risk = String(row.riskLevel ?? row.risk_level ?? "").toLowerCase();
      return risk === "medium" || Number(row.openAlertCount ?? row.open_alert_count ?? 0) > 0;
    }).length;
    const geocoded = source.filter(hasValidPosition).length;
    const avgFreshness = source.reduce((sum, row) => sum + Number(row.secondsSincePing ?? row.seconds_since_ping ?? 0), 0) / Math.max(source.length, 1);
    return { stale, highRisk, watch, geocoded, total: source.length, avgFreshness };
  }, [assetStates, liveEntities]);

  const assetHealthRows = useMemo(() => {
    const source = (liveStatesQ.data as AnyRecord[]) ?? liveEntities;
    return [...source]
      .sort((a, b) => {
        const riskOrder: Record<string, number> = { high: 0, medium: 1, low: 2 };
        const aRisk = riskOrder[String(a.riskLevel ?? a.risk_level ?? "").toLowerCase()] ?? 3;
        const bRisk = riskOrder[String(b.riskLevel ?? b.risk_level ?? "").toLowerCase()] ?? 3;
        if (aRisk !== bRisk) return aRisk - bRisk;
        return Number(b.secondsSincePing ?? b.seconds_since_ping ?? 0) - Number(a.secondsSincePing ?? a.seconds_since_ping ?? 0);
      })
      .slice(0, 4)
      .map((row) => ({
        id: String(row.id ?? row.vehicleId ?? row.vehicle_id ?? row.vehicleCode ?? row.vehicle_code),
        label: String(row.vehicleCode ?? row.vehicle_code ?? row.label ?? "Vehicle"),
        risk: String(row.riskLevel ?? row.risk_level ?? "Low"),
        status: String(row.telemetryStatus ?? row.telemetry_status ?? row.status ?? "Healthy"),
        fresh: freshnessLabel(Number(row.secondsSincePing ?? row.seconds_since_ping ?? null)),
        note: String(row.nextAction ?? row.next_action ?? row.liveAlert ?? row.live_alert ?? "Monitoring live"),
        connectivity: String(row.connectivityStatus ?? row.connectivity_status ?? "Unknown"),
        connectivityIssues: String(row.connectivityIssues ?? row.connectivity_issues ?? "None"),
      }));
  }, [liveStatesQ.data, liveEntities]);

  // Live status segmentation — Moving / Idle / Offline — the signature fleet-status board.
  const buckets = useMemo(() => {
    const counts: Record<StatusBucket, number> = { Moving: 0, Idle: 0, Offline: 0 };
    for (const e of liveEntities) counts[statusBucket(e)] += 1;
    return counts;
  }, [liveEntities]);

  const matchedEntities = useMemo(
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

  // Cap the rendered roster so a large fleet (1000+ units) doesn't put 1000 DOM rows
  // on screen at once. Attention-priority sort means offline/moving units surface
  // first; search + status filters narrow beyond the cap.
  const ROSTER_CAP = 200;
  const visibleEntities = useMemo(() => matchedEntities.slice(0, ROSTER_CAP), [matchedEntities]);

  const focusOn = (entity: AnyRecord) => {
    setSelected(entity);
    setFocusId(String(entity.id ?? entity.vehicleId ?? entity.vehicle_id ?? entity.label ?? entity.vehicleCode ?? entity.vehicle_code ?? ""));
  };

  if (isError) {
    return (
      <ErrorState
        message="Unable to load the live map telemetry feed. Check backend connectivity and retry."
        onRetry={() => void refetch()}
      />
    );
  }
  if (isLoading || !data) return <LoadingState />;

  const kpis = (data.kpis as AnyRecord) ?? {};
  const geofences = layers.geofences ? ((data.geofences as AnyRecord[]) ?? []) : [];
  const mapEntities = layers.vehicles ? visibleEntities : [];
  const recommendations = (data.recommendations as AnyRecord[]) ?? [];
  const openAlerts = alerts.data ?? [];
  const locatedEntityCount = liveEntities.filter(hasValidPosition).length;

  return (
    <div className="control-tower live-map-workbench flex h-full min-w-0 max-w-full flex-col gap-4 overflow-x-hidden overflow-y-auto">
      <PageHeader
        eyebrow="Operations"
        title="Live Fleet Map"
        description="Real-time vehicle positions, status and geofences — streamed live from the operational database."
        actions={
          telemetry.connected ? (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-teal-50 px-3 py-1.5 text-xs font-semibold text-teal-700">
              <Wifi className="h-3.5 w-3.5" /> GPS live · {telemetry.positions.length} units
              {telemetry.lastUpdated ? <span className="font-medium text-teal-600/70">· {telemetry.lastUpdated.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}</span> : null}
            </span>
          ) : (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-slate-100 px-3 py-1.5 text-xs font-semibold text-slate-500">
              <WifiOff className="h-3.5 w-3.5" /> {telemetry.error ?? "Connecting to stream…"}
            </span>
          )
        }
      />

      {/* Status segmentation — the single primary metric row. */}
      <div className="live-map-status-grid order-2 grid min-w-0 gap-2 sm:grid-cols-2 lg:grid-cols-4">
        <StatusBoardCard label="All Units"     count={liveEntities.length} tone="slate"  meaning="Tracked"        active={activeFilter === "All"}     onClick={() => setActiveFilter("All")} />
        <StatusBoardCard label="Moving"        count={buckets.Moving}      tone="teal"   meaning="On the road"    active={activeFilter === "Moving"}  onClick={() => setActiveFilter(activeFilter === "Moving" ? "All" : "Moving")} />
        <StatusBoardCard label="Idle / Parked" count={buckets.Idle}        tone="indigo" meaning="Stopped, live"  active={activeFilter === "Idle"}    onClick={() => setActiveFilter(activeFilter === "Idle" ? "All" : "Idle")} />
        <StatusBoardCard label="Offline"       count={buckets.Offline}     tone="rose"   meaning="No recent GPS"  active={activeFilter === "Offline"} onClick={() => setActiveFilter(activeFilter === "Offline" ? "All" : "Offline")} />
      </div>

      <div className="live-map-primary order-1 grid min-w-0 items-stretch gap-3 xl:grid-cols-[minmax(0,1.55fr)_minmax(280px,.45fr)]">
        {/* Hero map */}
        <section className="panel live-map-stage flex min-w-0 flex-col overflow-hidden p-3 sm:p-4">
          <div className="flex flex-wrap items-center justify-between gap-x-5 gap-y-2">
            <h2 className="section-title">Live Operations Map</h2>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs font-semibold text-slate-400">
              <MetaStat icon={<Satellite className="h-3.5 w-3.5 text-teal-500" />} value={String(kpis.onlineDevices ?? 0)} label="devices" />
              <MetaStat icon={<Camera className="h-3.5 w-3.5 text-violet-500" />} value={String(kpis.onlineCameras ?? 0)} label="cameras" />
              <MetaStat icon={<Gauge className="h-3.5 w-3.5 text-blue-500" />} value={String(kpis.telemetryQuality ?? "--")} label="quality" />
              <MetaStat icon={<ShieldAlert className="h-3.5 w-3.5 text-amber-500" />} value={String(kpis.speedAlerts ?? 0)} label="speed alerts" />
            </div>
          </div>

          <div className="mt-3 grid min-w-0 gap-2 lg:grid-cols-2 xl:grid-cols-4">
            <div className="rounded-2xl border border-slate-200/80 bg-white/80 p-3 shadow-sm">
              <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-slate-400">Route intelligence</p>
              <div className="mt-2 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate text-sm font-semibold text-slate-900">{String(selectedRoute?.routeCode ?? selectedRoute?.routeName ?? selectedRoute?.name ?? "No route selected")}</p>
                  <p className="truncate text-xs text-slate-500">{routeTrail[0]?.summary ?? "No geocoded route trail yet"}</p>
                </div>
                <Route className="h-4 w-4 shrink-0 text-sky-500" />
              </div>
              <div className="mt-3 flex items-center gap-2">
                <select
                  className="field min-w-0 flex-1 py-2 text-sm"
                  value={selectedRouteId ?? ""}
                  onChange={(e) => setSelectedRouteId(e.target.value || null)}
                >
                  <option value="">Select a route</option>
                  {routeRows.map((route) => (
                    <option key={String(route.id)} value={String(route.id)}>
                      {String(route.routeCode ?? route.routeName ?? route.name ?? `Route ${route.id}`)}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200/80 bg-white/80 p-3 shadow-sm">
              <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-slate-400">Route optimization</p>
              {routePreviewQ.data ? (
                <>
                  <p className="mt-2 text-2xl font-black text-slate-950">{String(routePreviewQ.data.efficiencyScore ?? "--")}%</p>
                  <p className="mt-1 text-xs text-slate-500">
                    Saves {String(routePreviewQ.data.estimatedSavingsMinutes ?? 0)} min · {String(routePreviewQ.data.costLeakageReduction ?? "--")} leakage reduction
                  </p>
                </>
              ) : (
                <p className="mt-2 text-sm text-slate-500">Select a route with stops to generate a geospatial optimization preview.</p>
              )}
            </div>

            <div className="rounded-2xl border border-slate-200/80 bg-white/80 p-3 shadow-sm">
              <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-slate-400">Asset health</p>
              <div className="mt-2 grid grid-cols-2 gap-2 text-xs">
                <MetricPill label="Geocoded" value={`${assetHealth.geocoded}/${assetHealth.total}`} />
                <MetricPill label="Stale" value={String(assetHealth.stale)} tone="rose" />
                <MetricPill label="At risk" value={String(assetHealth.highRisk)} tone="amber" />
                <MetricPill label="Watch" value={String(assetHealth.watch)} tone="sky" />
              </div>
              <p className="mt-2 text-[11px] text-slate-500">
                Avg freshness {freshnessLabel(assetHealth.avgFreshness)} · geospatial state updates live from the telemetry feed
              </p>
            </div>

            <div className="rounded-2xl border border-slate-200/80 bg-white/80 p-3 shadow-sm">
              <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-slate-400">Connectivity</p>
              <div className="mt-2 grid grid-cols-2 gap-2 text-xs">
                <MetricPill label="Connected" value={String(kpis.connectedUnits ?? 0)} tone="sky" />
                <MetricPill label="Degraded" value={String(kpis.degradedUnits ?? 0)} tone="amber" />
                <MetricPill label="Device offline" value={String(kpis.deviceOfflineUnits ?? 0)} tone="rose" />
                <MetricPill label="Camera offline" value={String(kpis.cameraOfflineUnits ?? 0)} tone="rose" />
              </div>
              <p className="mt-2 text-[11px] text-slate-500">
                Connectivity coverage {String(kpis.connectivityCoverage ?? 0)}% · real vehicle, device, camera and GPS signals
              </p>
            </div>
          </div>

          <div className="-mx-1 mt-3 overflow-x-auto px-1 pb-1">
            <div className="flex min-w-max items-center gap-2">
              {QUICK_FILTERS.map((filter) => (
                <button
                  type="button"
                  key={filter}
                  className={filter === activeFilter ? "btn-primary whitespace-nowrap py-1.5 text-xs" : "btn-ghost whitespace-nowrap py-1.5 text-xs"}
                  onClick={() => setActiveFilter(activeFilter === filter ? "All" : filter)}
                >
                  {filter}
                </button>
              ))}
              <span className="mx-1 h-5 w-px bg-slate-200" />
              <LayerToggle label="Vehicles" icon={<Truck className="h-3.5 w-3.5" />} active={layers.vehicles} onClick={() => setLayers((l) => ({ ...l, vehicles: !l.vehicles }))} />
              <LayerToggle label="Geofences" icon={<Layers className="h-3.5 w-3.5" />} active={layers.geofences} onClick={() => setLayers((l) => ({ ...l, geofences: !l.geofences }))} />
            </div>
          </div>
          <div className="mt-1 flex justify-end">
            <span className="text-xs font-semibold text-slate-400">
              {matchedEntities.length > ROSTER_CAP ? `first ${ROSTER_CAP} of ${matchedEntities.length} — refine filters` : `${matchedEntities.length} of ${liveEntities.length} shown`}
            </span>
          </div>

          <div className="map-surface live-map-canvas relative mt-2 min-h-[400px] flex-1 overflow-hidden sm:min-h-[500px] xl:min-h-[560px]">
            <LiveMap entities={mapEntities} geofences={geofences} routeTrails={routeTrail} onSelect={setSelected} focusId={focusId} />
            {locatedEntityCount === 0 ? (
              <div className="pointer-events-none absolute inset-x-4 top-4 z-[500] rounded-2xl border border-amber-300 bg-amber-50/95 p-4 shadow-lg backdrop-blur">
                <div className="flex items-start gap-3">
                  <Satellite className="mt-0.5 h-5 w-5 shrink-0 text-amber-700" />
                  <div>
                    <p className="font-bold text-amber-950">No current GPS positions</p>
                    <p className="mt-1 text-sm leading-relaxed text-amber-900">
                      {String(kpis.registeredDevices ?? liveEntities.length)} devices are registered, but none has reported a valid location. The map will populate after an authenticated device telemetry ping.
                    </p>
                  </div>
                </div>
              </div>
            ) : null}
          </div>
        </section>

        {/* Unified right rail: roster (scrolls) over a pinned alerts strip. */}
        <aside className="panel live-map-tactile-card flex min-w-0 max-h-[700px] flex-col overflow-hidden p-0">
          <div className="border-b border-slate-100 px-4 pb-3 pt-4">
            <div className="flex items-center justify-between">
              <h2 className="section-title">Geospatial Health</h2>
              <span className="text-xs font-semibold text-slate-400">{formatDistance(routeTrailMiles(routeTrail[0]?.points ?? []))}</span>
            </div>
            <div className="mt-3 space-y-2">
              {assetHealthRows.length === 0 ? (
                <p className="text-sm text-slate-500">No live asset health rows yet.</p>
              ) : (
                assetHealthRows.map((item) => (
                  <div key={item.id} className="rounded-xl border border-slate-200 bg-white px-3 py-2.5 shadow-sm">
                    <div className="flex items-center justify-between gap-3">
                      <p className="truncate text-sm font-semibold text-slate-900">{item.label}</p>
                      <div className="flex items-center gap-2">
                        <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.14em] ${
                          item.connectivity === "Connected"
                            ? "bg-emerald-50 text-emerald-700"
                            : item.connectivity === "Degraded"
                              ? "bg-amber-50 text-amber-700"
                              : "bg-slate-100 text-slate-500"
                        }`}>
                          {item.connectivity}
                        </span>
                        <RiskBadge risk={item.risk} />
                      </div>
                    </div>
                    <p className="mt-1 text-xs text-slate-500">{item.status} · {item.fresh}</p>
                    <p className="mt-1 text-xs text-slate-400">{item.note} · {item.connectivityIssues}</p>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="border-b border-slate-100 px-4 pb-3 pt-4">
            <div className="flex items-center justify-between">
              <h2 className="section-title">Live Roster</h2>
              <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${telemetry.connected ? "text-teal-600" : "text-slate-400"}`}>
                <span className={`h-2 w-2 rounded-full ${telemetry.connected ? "animate-pulse bg-teal-500" : "bg-slate-300"}`} />
                {telemetry.connected ? "Live" : "Reconnecting"}
              </span>
            </div>
            <div className="relative mt-3">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                className="field pl-10"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Vehicle code, driver, status…"
              />
            </div>
          </div>

          <div className="min-h-[180px] flex-1 space-y-2 overflow-y-auto px-4 py-3">
            {visibleEntities.length === 0 ? (
              <p className="px-1 py-6 text-center text-sm text-slate-500">No vehicles match the current filters.</p>
            ) : (
              visibleEntities.map((entity) => (
                <RosterRow key={String(entity.id ?? entity.vehicleId ?? entity.label)} entity={entity} onClick={() => focusOn(entity)} />
              ))
            )}
          </div>

          <div className="border-t border-slate-100 px-4 py-3">
            <div className="flex items-center justify-between">
              <h3 className="text-xs font-bold uppercase tracking-[0.14em] text-slate-500">
                Telemetry Alerts{openAlerts.length > 0 ? ` · ${openAlerts.length}` : ""}
              </h3>
            </div>
            <div className="mt-2 max-h-44 space-y-2 overflow-y-auto">
              {openAlerts.length === 0 ? (
                <p className="flex items-center gap-2 py-1 text-sm text-slate-500"><CheckCircle className="h-4 w-4 text-teal-600" /> No open alerts.</p>
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
        <div className="order-4 grid min-w-0 gap-5 lg:grid-cols-2">
          {recommendations.slice(0, 2).map((item) => <AiInsightCard key={String(item.id)} insight={item} />)}
        </div>
      )}

      <VehicleDetailDrawer detail={detail.data} loading={detail.isLoading} onClose={() => { setSelected(null); setFocusId(null); }} />
    </div>
  );
}

type MetricTone = "slate" | "rose" | "amber" | "sky";

function MetricPill({ label, value, tone = "slate" }: { label: string; value: string; tone?: MetricTone }) {
  const toneClass: Record<MetricTone, string> = {
    slate: "bg-slate-50 text-slate-700",
    rose: "bg-rose-50 text-rose-700",
    amber: "bg-amber-50 text-amber-700",
    sky: "bg-sky-50 text-sky-700",
  };

  return (
    <div className={`rounded-xl px-3 py-2 ${toneClass[tone]}`}>
      <p className="text-[10px] font-bold uppercase tracking-[0.18em] opacity-70">{label}</p>
      <p className="mt-0.5 text-sm font-semibold tabular-nums">{value}</p>
    </div>
  );
}

const BOARD_TONES: Record<string, { dot: string; activeBorder: string; activeBg: string; text: string }> = {
  slate:  { dot: "bg-slate-400",   activeBorder: "border-slate-400",   activeBg: "bg-slate-50",   text: "text-slate-700" },
  teal:   { dot: "bg-teal-500",    activeBorder: "border-teal-400",    activeBg: "bg-teal-50",    text: "text-teal-700" },
  indigo: { dot: "bg-indigo-500",  activeBorder: "border-indigo-400",  activeBg: "bg-indigo-50",  text: "text-indigo-700" },
  rose:   { dot: "bg-rose-500",    activeBorder: "border-rose-400",    activeBg: "bg-rose-50",    text: "text-rose-700" },
};

function StatusBoardCard({ label, count, tone, meaning, active, onClick }: { label: string; count: number; tone: keyof typeof BOARD_TONES; meaning: string; active: boolean; onClick: () => void }) {
  const t = BOARD_TONES[tone];
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active ? "true" : "false"}
      className={`live-map-status-card flex items-center justify-between gap-3 rounded-2xl border px-3 py-2.5 text-left transition ${
        active ? `${t.activeBorder} ${t.activeBg} shadow-sm` : "border-slate-200 bg-white hover:border-slate-300 hover:shadow-sm"
      }`}
    >
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${t.dot} ${tone === "teal" ? "animate-pulse" : ""}`} />
          <span className="truncate text-sm font-semibold text-slate-700">{label}</span>
        </div>
        <p className="mt-1 pl-[18px] text-[11px] font-medium text-slate-400">{meaning}</p>
      </div>
      <span className={`text-3xl font-bold tabular-nums ${active ? t.text : "text-slate-900"}`}>{count}</span>
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
      className="flex w-full items-center gap-3 rounded-xl border border-slate-200 bg-white px-3 py-2 text-left transition hover:border-blue-300 hover:bg-blue-50/40"
    >
      <span className={`mt-0.5 h-2.5 w-2.5 shrink-0 rounded-full ${ROSTER_DOT[bucket]} ${bucket === "Moving" ? "animate-pulse" : ""}`} />
      <div className="min-w-0 flex-1">
        <div className="flex items-center justify-between gap-2">
          <p className="truncate text-sm font-bold text-slate-900">{label}</p>
          <RiskBadge risk={entity.riskLevel ?? entity.risk_level} />
        </div>
        <p className="truncate text-xs text-slate-500">{driver}</p>
        <div className="mt-1 flex flex-wrap items-center gap-2 text-[11px] font-medium text-slate-400">
          {bucket === "Moving" ? (
            <span className="inline-flex items-center gap-1 text-teal-600"><Navigation className="h-3 w-3" />{Math.round(speed)} mph</span>
          ) : (
            <span className={bucket === "Offline" ? "text-rose-500" : "text-indigo-500"}>{bucket === "Offline" ? "Offline" : "Idle"}</span>
          )}
          <span>·</span>
          <span>{fresh}</span>
          <span>·</span>
          <span className={String(entity.connectivityStatus ?? "").toLowerCase() === "connected" ? "text-teal-600" : String(entity.connectivityStatus ?? "").toLowerCase() === "degraded" ? "text-amber-600" : "text-slate-500"}>
            {String(entity.connectivityStatus ?? "Unknown")}
          </span>
        </div>
        <p className="mt-1 text-[10px] text-slate-400">{String(entity.connectivityIssues ?? "None")}</p>
      </div>
    </button>
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
          <Mini title="Live Telemetry" record={record} keys={["driverName", "driver_name", "lat", "lng", "speedMph", "speed_mph", "heading", "deviceStatus", "device_status", "cameraStatus", "camera_status", "connectivityStatus", "connectivity_status", "connectivityIssues", "connectivity_issues"]} />
          <Mini title="Health & Diagnostics" record={record} keys={["readinessScore", "readiness_score", "dataQualityScore", "data_quality_score", "riskScore", "risk_score", "odometerMiles", "odometer_miles", "type", "vehicleStatus", "vehicle_status"]} />
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
