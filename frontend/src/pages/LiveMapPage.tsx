import { type ReactNode, useMemo, useState } from "react";
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
import { telemetryApi } from "@/services/telemetryApi";
import { LiveMap } from "@/components/LiveMap";
import { useLiveTelemetry } from "@/hooks/useLiveTelemetry";
import { AiInsightCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
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
    queryKey: ["telemetry", "live-map-summary"],
    queryFn: telemetryApi.liveMapSummary,
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

  if (isLoading || !data) return <LoadingState />;

  const kpis = (data.kpis as AnyRecord) ?? {};
  const geofences = layers.geofences ? ((data.geofences as AnyRecord[]) ?? []) : [];
  const mapEntities = layers.vehicles ? visibleEntities : [];
  const recommendations = (data.recommendations as AnyRecord[]) ?? [];
  const deviceRegistry = (data.deviceRegistry as AnyRecord[]) ?? [];
  const riskRules = (data.riskRules as AnyRecord[]) ?? [];
  const mobileReadiness = (data.mobileReadiness as AnyRecord[]) ?? [];
  const openAlerts = alerts.data ?? [];

  return (
    <div className="control-tower flex h-full flex-col gap-6 overflow-y-auto">
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
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatusBoardCard label="All Units"     count={liveEntities.length} tone="slate"  meaning="Tracked"        active={activeFilter === "All"}     onClick={() => setActiveFilter("All")} />
        <StatusBoardCard label="Moving"        count={buckets.Moving}      tone="teal"   meaning="On the road"    active={activeFilter === "Moving"}  onClick={() => setActiveFilter(activeFilter === "Moving" ? "All" : "Moving")} />
        <StatusBoardCard label="Idle / Parked" count={buckets.Idle}        tone="indigo" meaning="Stopped, live"  active={activeFilter === "Idle"}    onClick={() => setActiveFilter(activeFilter === "Idle" ? "All" : "Idle")} />
        <StatusBoardCard label="Offline"       count={buckets.Offline}     tone="rose"   meaning="No recent GPS"  active={activeFilter === "Offline"} onClick={() => setActiveFilter(activeFilter === "Offline" ? "All" : "Offline")} />
      </div>

      <div className="grid gap-4 xl:grid-cols-[1.25fr_.85fr_.9fr]">
        <section className="panel p-5">
          <h3 className="section-title">Telemetry Backbone</h3>
          <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
            <TelemetryMiniStat label="Live units" value={String(kpis.liveUnits ?? 0)} tone="teal" />
            <TelemetryMiniStat label="Devices" value={String(kpis.registeredDevices ?? 0)} tone="blue" />
            <TelemetryMiniStat label="Open alerts" value={String(kpis.openAlerts ?? 0)} tone="rose" />
            <TelemetryMiniStat label="Coverage" value={`${String(kpis.liveCoverage ?? 0)}%`} tone="indigo" />
          </div>
          <div className="mt-4 grid gap-3 lg:grid-cols-2">
            <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
              <div className="flex items-center justify-between">
                <h4 className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Live State</h4>
                <span className="text-xs font-semibold text-slate-400">{String(kpis.healthyUnits ?? 0)} healthy</span>
              </div>
              <div className="mt-3 space-y-2">
                {deviceRegistry.slice(0, 4).map((device) => (
                  <div key={String(device.id)} className="flex items-center justify-between rounded-xl bg-white px-3 py-2 shadow-sm ring-1 ring-slate-200/70">
                    <div>
                      <p className="text-sm font-semibold text-slate-800">{String(device.device_serial ?? device.deviceSerial ?? "Unknown device")}</p>
                      <p className="text-[11px] text-slate-500">{String(device.vehicle_code ?? device.vehicleCode ?? "Unassigned")} · {String(device.telemetry_status ?? device.telemetryStatus ?? "healthy")}</p>
                    </div>
                    <span className="text-xs font-semibold text-slate-500">{String(device.risk_level ?? device.riskLevel ?? "low")}</span>
                  </div>
                ))}
                {deviceRegistry.length === 0 ? <p className="text-sm text-slate-500">No device registry rows yet.</p> : null}
              </div>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
              <div className="flex items-center justify-between">
                <h4 className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Risk Rules</h4>
                <span className="text-xs font-semibold text-slate-400">{String(riskRules.length)} rules</span>
              </div>
              <div className="mt-3 space-y-2">
                {riskRules.slice(0, 4).map((rule) => (
                  <div key={String(rule.id)} className="rounded-xl bg-white px-3 py-2 shadow-sm ring-1 ring-slate-200/70">
                    <div className="flex items-center justify-between gap-3">
                      <p className="text-sm font-semibold text-slate-800">{String(rule.rule_type ?? rule.ruleType ?? "Rule")}</p>
                      <StatusBadge status={String(rule.severity ?? "Watch")} />
                    </div>
                    <p className="mt-1 text-[11px] text-slate-500">
                      Threshold {String(rule.threshold_value ?? rule.thresholdValue ?? "—")} · {Boolean(rule.enabled ?? true) ? "Enabled" : "Disabled"}
                    </p>
                  </div>
                ))}
                {riskRules.length === 0 ? <p className="text-sm text-slate-500">No risk rules configured.</p> : null}
              </div>
            </div>
          </div>
        </section>

        <section className="panel p-5">
          <h3 className="section-title">Billing / Risk Signal</h3>
          <div className="mt-4 space-y-3">
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Open telemetry alerts</p>
              <p className="mt-2 text-3xl font-black text-slate-900">{String(kpis.openAlerts ?? 0)}</p>
              <p className="mt-2 text-sm text-slate-500">Telemetry is real-time and does not fabricate empty confidence.</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Next action</p>
              <p className="mt-2 text-sm font-semibold text-slate-800">{String((deviceRegistry.find((row) => String(row.next_action ?? "") !== "")?.next_action ?? deviceRegistry[0]?.next_action) ?? "Not configured yet")}</p>
              <p className="mt-2 text-xs text-slate-500">Alerts, live state and device registry are all tenant scoped.</p>
            </div>
          </div>
        </section>

        <section className="panel p-5">
          <h3 className="section-title">Mobile Readiness</h3>
          <div className="mt-4 space-y-2">
            {mobileReadiness.slice(0, 4).map((row) => (
              <div key={String(row.role)} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-3">
                <p className="text-sm font-semibold text-slate-800">{String(row.role ?? "Role")}</p>
                <p className="mt-1 text-xs text-slate-500">{Array.isArray(row.routeFamilies) ? row.routeFamilies.join(" · ") : "No route families"}</p>
                <p className="mt-2 text-[11px] text-slate-500">{String(row.offlineIdempotency ?? "Not configured")}</p>
              </div>
            ))}
            {mobileReadiness.length === 0 ? <p className="text-sm text-slate-500">Mobile readiness preview is not configured yet.</p> : null}
          </div>
        </section>
      </div>

      <div className="grid items-stretch gap-5 xl:grid-cols-[1.55fr_.45fr]">
        {/* Hero map */}
        <section className="panel flex flex-col p-5">
          <div className="flex flex-wrap items-center justify-between gap-x-5 gap-y-2">
            <h2 className="section-title">Live Operations Map</h2>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs font-semibold text-slate-400">
              <MetaStat icon={<Satellite className="h-3.5 w-3.5 text-teal-500" />} value={String(kpis.onlineDevices ?? 0)} label="devices" />
              <MetaStat icon={<Camera className="h-3.5 w-3.5 text-violet-500" />} value={String(kpis.onlineCameras ?? 0)} label="cameras" />
              <MetaStat icon={<Gauge className="h-3.5 w-3.5 text-blue-500" />} value={String(kpis.telemetryQuality ?? "--")} label="quality" />
              <MetaStat icon={<ShieldAlert className="h-3.5 w-3.5 text-amber-500" />} value={String(kpis.speedAlerts ?? 0)} label="speed alerts" />
            </div>
          </div>

          <div className="mt-3 flex flex-wrap items-center gap-2">
            {QUICK_FILTERS.map((filter) => (
              <button
                type="button"
                key={filter}
                className={filter === activeFilter ? "btn-primary py-1.5 text-xs" : "btn-ghost py-1.5 text-xs"}
                onClick={() => setActiveFilter(activeFilter === filter ? "All" : filter)}
              >
                {filter}
              </button>
            ))}
            <span className="mx-1 hidden h-5 w-px bg-slate-200 sm:block" />
            <LayerToggle label="Vehicles" icon={<Truck className="h-3.5 w-3.5" />} active={layers.vehicles} onClick={() => setLayers((l) => ({ ...l, vehicles: !l.vehicles }))} />
            <LayerToggle label="Geofences" icon={<Layers className="h-3.5 w-3.5" />} active={layers.geofences} onClick={() => setLayers((l) => ({ ...l, geofences: !l.geofences }))} />
            <span className="ml-auto text-xs font-semibold text-slate-400">
              {matchedEntities.length > ROSTER_CAP ? `first ${ROSTER_CAP} of ${matchedEntities.length} — refine filters` : `${matchedEntities.length} of ${liveEntities.length} shown`}
            </span>
          </div>

          <div className="map-surface mt-4 min-h-[560px] flex-1">
            <LiveMap entities={mapEntities} geofences={geofences} onSelect={setSelected} focusId={focusId} />
          </div>
        </section>

        {/* Unified right rail: roster (scrolls) over a pinned alerts strip. */}
        <aside className="panel flex max-h-[760px] flex-col overflow-hidden p-0">
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
        <div className="grid gap-5 lg:grid-cols-2">
          {recommendations.slice(0, 2).map((item) => <AiInsightCard key={String(item.id)} insight={item} />)}
        </div>
      )}

      <VehicleDetailDrawer detail={detail.data} loading={detail.isLoading} onClose={() => { setSelected(null); setFocusId(null); }} />
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
      className={`flex items-center justify-between gap-3 rounded-2xl border px-4 py-3.5 text-left transition ${
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

function TelemetryMiniStat({ label, value, tone }: { label: string; value: string; tone: "teal" | "blue" | "rose" | "indigo" }) {
  const styles = {
    teal: "border-teal-200 bg-teal-50 text-teal-700",
    blue: "border-blue-200 bg-blue-50 text-blue-700",
    rose: "border-rose-200 bg-rose-50 text-rose-700",
    indigo: "border-indigo-200 bg-indigo-50 text-indigo-700",
  }[tone];

  return (
    <div className={`rounded-2xl border px-4 py-3 ${styles}`}>
      <p className="text-[11px] font-bold uppercase tracking-[0.16em] opacity-80">{label}</p>
      <p className="mt-2 text-2xl font-black tracking-tight">{value}</p>
    </div>
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
        <div className="mt-1 flex items-center gap-2 text-[11px] font-medium text-slate-400">
          {bucket === "Moving" ? (
            <span className="inline-flex items-center gap-1 text-teal-600"><Navigation className="h-3 w-3" />{Math.round(speed)} mph</span>
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
