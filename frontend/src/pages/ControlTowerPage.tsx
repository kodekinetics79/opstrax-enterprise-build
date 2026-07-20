import { type ReactNode, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Bell, Camera, CheckCircle, CircleDot, Gauge, MapPin, Navigation, RadioTower, Route, Satellite, Send, ShieldAlert, Wifi, WifiOff, Wrench, X } from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { AiInsightCard, DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import { useEventStream } from "@/hooks/useEventStream";
import { useLiveTelemetry } from "@/hooks/useLiveTelemetry";
import { controlTowerApi } from "@/services/controlTowerApi";
import { LiveMap } from "@/components/LiveMap";
import { useTrips, useTripBreadcrumbs, useTripCompliance } from "@/hooks/useBatch7";
import type { AnyRecord } from "@/types";

export function ControlTowerPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [activeFilter, setActiveFilter] = useState("All");
  const [activeTab, setActiveTab] = useState("Dispatch");
  const { data, isLoading } = useQuery({ queryKey: ["control-tower"], queryFn: controlTowerApi.summary, refetchInterval: 15000 });
  const detail = useQuery({
    queryKey: ["control-tower", "entity", selected?.vehicleId || selected?.id],
    queryFn: () => controlTowerApi.entity("vehicle", (selected?.vehicleId || selected?.id) as string | number),
    enabled: Boolean(selected?.vehicleId || selected?.id),
  });
  const stream = useEventStream();
  const telemetry = useLiveTelemetry();
  const qc = useQueryClient();
  const action = useMutation({
    mutationFn: (type: string) => type === "eta" ? controlTowerApi.sendEta() : type === "dispatch" ? controlTowerApi.createDispatchReview() : controlTowerApi.createMaintenanceReview(),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["control-tower"] }),
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

  // All hooks must be called before any early return
  const baseEntities = (data?.entities as AnyRecord[]) || [];
  const liveEntities = useMemo<AnyRecord[]>(() => {
    if (telemetry.positions.length === 0) return baseEntities;
    const posMap = new Map(telemetry.positions.map((p) => [p.vehicleCode, p]));
    return baseEntities.map((e) => {
      const live = posMap.get(String(e.label ?? e.vehicleCode ?? ""));
      if (!live) return e;
      return { ...e, lat: live.lat, lng: live.lng, speedMph: live.speedMph, heading: live.heading, engineStatus: live.engineStatus };
    });
  }, [baseEntities, telemetry.positions]);
  const trips = useTrips({ status: "active" });

  if (isLoading || !data) return <LoadingState />;

  const actionQueue = (data.actionQueue as AnyRecord[]) || [];
  const alertCount = alerts.data?.length ?? 0;

  const entities = liveEntities.filter((entity) => {
    if (activeFilter === "All") return true;
    const filterLower = activeFilter.toLowerCase();
    return String(entity.liveAlert || "").toLowerCase().includes(filterLower) ||
           String(entity.status || "").toLowerCase().includes(filterLower) ||
           String(entity.riskLevel || "").toLowerCase().includes(filterLower);
  });
  const geofences = (data.geofences as AnyRecord[]) || [];
  const recommendations = (data.recommendations as AnyRecord[]) || [];
  const kpis = (data.kpis as AnyRecord) || {};
  const events = stream.length ? stream : ((data.events as AnyRecord[]) || []);
  const tabs = ["Dispatch", "Active Trips", "Diagnostics", "Video Safety"];

  return (
    <div className="control-tower flex h-full flex-col gap-6 overflow-y-auto">
      <PageHeader
        eyebrow="Control Tower"
        title="Fleet Command Center"
        description="Live vehicle positions, telemetry alerts, trip compliance, and dispatch exceptions — updated every 15 seconds."
        actions={<><button className="btn-primary" onClick={() => action.mutate("eta")}><Send className="h-4 w-4" /> Send ETA Update</button><button className="btn-ghost" onClick={() => action.mutate("dispatch")}><Route className="h-4 w-4" /> Dispatch Review</button><button className="btn-ghost" onClick={() => action.mutate("maintenance")}><Wrench className="h-4 w-4" /> Maintenance Review</button></>}
      />
      <ControlStatusStrip kpis={kpis} generatedAt={data.generatedAt} alertCount={alertCount} actionCount={actionQueue.length} />
      <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-6">
        <KpiCard label="Tracked Vehicles" value={String(kpis.trackedEntities ?? entities.length)} icon={<RadioTower />} status="Active" />
        <KpiCard label="Online Devices" value={String(kpis.onlineDevices ?? 0)} icon={<Satellite />} status="Live" />
        <KpiCard label="Online Cameras" value={String(kpis.onlineCameras ?? 0)} icon={<Camera />} status="Live" />
        <KpiCard label="Telemetry Quality" value={String(kpis.telemetryQuality ?? "--")} icon={<Gauge />} status="Healthy" />
        <KpiCard label="High Risk Units" value={String(kpis.highRiskUnits ?? 0)} icon={<ShieldAlert />} status="Review" />
        <KpiCard label="Speed Alerts" value={String(kpis.speedAlerts ?? 0)} icon={<CircleDot />} status="Warning" />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.35fr_.65fr]">
        <section className="panel p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="section-title">Live Operations Map</h2>
              <div className="mt-1.5 flex items-center gap-2">
                {telemetry.connected
                  ? <span className="inline-flex items-center gap-1.5 rounded-full bg-teal-50 px-2 py-0.5 text-xs font-medium text-teal-700"><Wifi className="h-3 w-3" />GPS stream live · {telemetry.positions.length} vehicles</span>
                  : <span className="inline-flex items-center gap-1.5 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500"><WifiOff className="h-3 w-3" />{telemetry.error ?? "Connecting to stream…"}</span>
                }
              </div>
            </div>
            <div className="flex flex-wrap gap-2">{["All","Speeding","Device offline","Camera offline","Fleet risk","Delayed"].map((filter) => <button type="button" key={filter} className={filter === activeFilter ? "btn-primary" : "btn-ghost"} onClick={() => setActiveFilter(filter)}>{filter}</button>)}</div>
          </div>
          <div className="map-surface mt-4 h-[660px]">
            <LiveMap entities={entities} geofences={geofences} onSelect={setSelected} />
          </div>
        </section>

        <aside className="space-y-6">
          <Panel title="Live Event Feed">
            <div className="space-y-3">{events.slice(0, 10).map((event, index) => <EventRow key={String(event.id || index)} event={event} />)}</div>
          </Panel>
          <Panel title={`Needs Attention${actionQueue.length > 0 ? ` (${actionQueue.length})` : ""}`}>
            {actionQueue.length === 0 ? (
              <p className="flex items-center gap-2 text-sm text-slate-500"><CheckCircle className="h-4 w-4 text-teal-600" /> No pending actions</p>
            ) : (
              <div className="space-y-2">
                {actionQueue.slice(0, 8).map((item, i) => {
                  const p = String(item.priority ?? "Medium");
                  const accent = /critical/i.test(p)
                    ? "border-red-200 border-l-red-500 bg-red-50"
                    : /high/i.test(p)
                    ? "border-amber-200 border-l-amber-500 bg-amber-50"
                    : "border-slate-200 border-l-slate-300 bg-slate-50";
                  return (
                    <div key={String(item.id || i)} className={`rounded-xl border border-l-4 p-3 ${accent}`}>
                      <div className="flex items-start justify-between gap-2">
                        <p className="text-sm font-semibold leading-snug text-slate-900">{String(item.title)}</p>
                        <RiskBadge risk={p} />
                      </div>
                      <p className="mt-1 text-xs text-slate-500">
                        {String(item.moduleKey || item.module_key || "operations")}
                        {item.vehicleCode ? ` · ${String(item.vehicleCode)}` : ""}
                      </p>
                    </div>
                  );
                })}
              </div>
            )}
          </Panel>
          <Panel title={`Telemetry Alerts${(alerts.data?.length ?? 0) > 0 ? ` (${alerts.data!.length})` : ""}`}>
            {(!alerts.data || alerts.data.length === 0)
              ? <p className="text-sm text-slate-500">No open alerts.</p>
              : <div className="space-y-3">{alerts.data.slice(0, 6).map((alert) => <TelemetryAlertRow key={String(alert["id"])} alert={alert} onAck={() => ackAlert.mutate(Number(alert["id"]))} onResolve={() => resolveAlert.mutate(Number(alert["id"]))} />)}</div>
            }
          </Panel>
          {recommendations.slice(0, 2).map((item) => <AiInsightCard key={String(item.id)} insight={item} />)}
        </aside>
      </div>

      <Panel title="Operations Intelligence">
        <div className="flex flex-wrap gap-2">
          {tabs.map((tab) => <TabButton key={tab} active={tab === activeTab} onClick={() => setActiveTab(tab)}>{tab}</TabButton>)}
        </div>
        <div className="mt-5">
          {activeTab === "Dispatch" && <DataTable rows={(data.jobs as AnyRecord[]) || []} columns={["jobNumber","customerName","status","priority","slaStatus","eta","vehicleCode","driverName","recommendedAction"]} />}
          {activeTab === "Active Trips" && <ActiveTripsTable trips={trips.data ?? []} isLoading={trips.isLoading} />}
          {activeTab === "Diagnostics" && <DataTable rows={(data.diagnostics as AnyRecord[]) || []} columns={["vehicleCode","deviceStatus","cameraStatus","readinessScore","dataQualityScore","riskScore","recommendedAction"]} />}
          {activeTab === "Video Safety" && <div className="grid gap-4 lg:grid-cols-3">{((data.safetyVideo as AnyRecord[]) || []).map((event) => <div key={String(event.id)} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm"><div className="flex aspect-video items-center justify-center rounded-xl border border-violet-100 bg-violet-50 text-violet-700"><Camera className="h-10 w-10" /></div><p className="mt-3 font-semibold text-slate-900">{String(event.eventNumber)}</p><p className="mt-1 text-sm text-slate-500">{String(event.aiSummary || event.eventType)}</p><div className="mt-3 flex gap-2"><RiskBadge risk={event.severity} /><StatusBadge status={event.evidenceStatus} /></div></div>)}</div>}
        </div>
      </Panel>

      <EntityDrawer detail={detail.data} loading={detail.isLoading} onClose={() => setSelected(null)} />
    </div>
  );
}

function ControlStatusStrip({ kpis, generatedAt, alertCount, actionCount }: {
  kpis: AnyRecord;
  generatedAt?: unknown;
  alertCount: number;
  actionCount: number;
}) {
  const lastSync = generatedAt
    ? new Date(String(generatedAt)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
    : "--";
  const highRisk = Number(kpis.highRiskUnits ?? 0);
  const isNominal = highRisk === 0 && alertCount === 0;
  const isCritical = highRisk > 3 || alertCount > 5;

  const dotColor = isNominal ? "bg-teal-500" : isCritical ? "bg-red-500" : "bg-amber-500";
  const label    = isNominal ? "All Systems Nominal" : isCritical ? "Action Required" : "Review Needed";

  const details = isNominal
    ? "All tracked assets within parameters. No open alerts."
    : [
        alertCount > 0    && `${alertCount} open alert${alertCount > 1 ? "s" : ""}`,
        actionCount > 0   && `${actionCount} queued action${actionCount > 1 ? "s" : ""}`,
        highRisk > 0      && `${highRisk} high-risk unit${highRisk > 1 ? "s" : ""}`,
      ].filter(Boolean).join(" · ");

  return (
    <section className="control-status-strip">
      <div>
        <div className="flex items-center gap-2">
          <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${dotColor} ${!isNominal ? "animate-pulse" : ""}`} />
          <p className="section-title">{label}</p>
        </div>
        <h2 className="mt-1.5">{details}</h2>
      </div>
      <div className="control-status-grid">
        <span><b>{String(kpis.telemetryQuality ?? "--")}</b> Telemetry quality</span>
        <span><b>{String(kpis.fleetReadiness ?? "--")}</b> Fleet readiness</span>
        <span><b>{String(kpis.onlineCameras ?? 0)}</b> Cameras online</span>
        <span><b>{lastSync}</b> Last sync</span>
      </div>
    </section>
  );
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return <button type="button" className={active ? "control-tab control-tab-active" : "control-tab"} onClick={onClick}>{children}</button>;
}



const SEVERITY_STYLES: Record<string, string> = {
  High:     "border-red-200 bg-red-50",
  Critical: "border-red-300 bg-red-100",
  Warning:  "border-amber-200 bg-amber-50",
  Low:      "border-slate-200 bg-slate-50",
};

const ALERT_ICONS: Record<string, ReactNode> = {
  speeding:     <CircleDot className="h-4 w-4 text-red-600" />,
  stale_device: <Bell className="h-4 w-4 text-amber-600" />,
  geofence_breach: <AlertTriangle className="h-4 w-4 text-orange-600" />,
};

function TelemetryAlertRow({ alert, onAck, onResolve }: { alert: AnyRecord; onAck: () => void; onResolve: () => void }) {
  const type    = String(alert["alertType"] ?? alert["alert_type"] ?? "");
  const sev     = String(alert["severity"] ?? "Warning");
  const msg     = String(alert["message"] ?? "");
  const vehicle = String(alert["vehicleCode"] ?? alert["vehicle_code"] ?? "");
  const style   = SEVERITY_STYLES[sev] ?? SEVERITY_STYLES.Warning;
  return (
    <div className={`rounded-xl border p-3 ${style}`}>
      <div className="flex items-start gap-2">
        <span className="mt-0.5">{ALERT_ICONS[type] ?? <ShieldAlert className="h-4 w-4 text-slate-500" />}</span>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold text-slate-900">{type.replace(/_/g, " ")} {vehicle ? `· ${vehicle}` : ""}</p>
          <p className="mt-0.5 text-xs text-slate-600 leading-relaxed">{msg}</p>
        </div>
      </div>
      <div className="mt-2 flex gap-2">
        <button type="button" onClick={onAck} className="btn-ghost text-xs py-1 px-2 flex items-center gap-1"><CheckCircle className="h-3 w-3" /> Ack</button>
        <button type="button" onClick={onResolve} className="btn-ghost text-xs py-1 px-2 flex items-center gap-1"><X className="h-3 w-3" /> Resolve</button>
      </div>
    </div>
  );
}

function EventRow({ event }: { event: AnyRecord }) {
  return <div className="rounded-xl border border-slate-200 bg-slate-50 p-3"><div className="flex justify-between gap-3"><p className="text-sm font-semibold text-slate-900">{String(event.title || event.type || event.eventType)}</p><StatusBadge status={event.severity || "Live"} /></div><p className="mt-1 text-xs text-slate-500">{String(event.eventTime || event.generatedAt || "")}</p></div>;
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return <section className="panel p-5"><h2 className="section-title">{title}</h2><div className="mt-4">{children}</div></section>;
}

function EntityDrawer({ detail, loading, onClose }: { detail?: AnyRecord; loading: boolean; onClose: () => void }) {
  const record = detail?.record as AnyRecord | undefined;
  // Find the active trip for this vehicle from the embedded trips list.
  const activeTrip = (detail?.activeTrips as AnyRecord[] | undefined)?.[0];
  const tripId = activeTrip ? Number(activeTrip["id"]) : undefined;
  const breadcrumbs = useTripBreadcrumbs(tripId);
  const compliance  = useTripCompliance(tripId);

  if (!record && !loading) return null;
  if (!record) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm">
      <aside className="h-full w-full max-w-4xl overflow-y-auto border-l border-slate-200 bg-white p-6 shadow-2xl">
        <button type="button" aria-label="Close" className="float-right icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        <p className="section-title">Live Vehicle Detail</p>
        <h2 className="mt-3 text-2xl font-semibold text-slate-900">{String(record.vehicleCode)}</h2>
        <div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status} /><RiskBadge risk={record.riskScore} /><span className="badge">Last seen {String(record.lastSeenAt || "--")}</span></div>
        {activeTrip && (
          <div className="mt-4 rounded-xl border border-teal-200 bg-teal-50 p-3">
            <div className="flex items-center gap-2">
              <Route className="h-4 w-4 text-teal-700" />
              <span className="text-sm font-semibold text-teal-800">Active Trip: {String(activeTrip["tripRef"] || activeTrip["id"])}</span>
              {compliance.data && (
                <span className="ml-auto rounded-full bg-teal-100 px-2 py-0.5 text-xs font-semibold text-teal-800">
                  Compliance {String(compliance.data["complianceScore"] ?? "--")}%
                </span>
              )}
            </div>
            <p className="mt-1 text-xs text-teal-600">
              {String(activeTrip["origin"] ?? "Origin unknown")} → {String(activeTrip["destination"] ?? "Destination")}
            </p>
          </div>
        )}
        <div className="mt-6 grid gap-4 lg:grid-cols-2">
          <Mini title="Live Telemetry" record={record} keys={["driverName","lat","lng","speedMph","heading","deviceStatus","cameraStatus"]} />
          <Mini title="Health & Diagnostics" record={record} keys={["readinessScore","dataQualityScore","riskScore","odometerMiles","type"]} />
        </div>
        <Grid title="Active Jobs / SLA" rows={(detail?.activeJobs as AnyRecord[]) || []} columns={["jobNumber","status","slaStatus","eta","priority"]} />
        <Grid title="Safety Events" rows={(detail?.safetyEvents as AnyRecord[]) || []} columns={["eventNumber","eventType","severity","reviewStatus","occurredAt"]} />
        <Grid title="Video Events" rows={(detail?.videoEvents as AnyRecord[]) || []} columns={["eventNumber","eventType","severity","reviewStatus","evidenceStatus"]} />
        <Grid title="Maintenance Watch" rows={(detail?.maintenance as AnyRecord[]) || []} columns={["serviceType","status","priority","dueDate","riskScore"]} />
        <Grid
          title={`Replay Trail${breadcrumbs.data?.length ? ` (${breadcrumbs.data.length} points)` : ""}`}
          rows={breadcrumbs.data ?? (detail?.replayTrail as AnyRecord[]) ?? []}
          columns={["lat","lng","speedMph","heading","eventType","eventTime"]}
        />
      </aside>
    </div>
  );
}

function Mini({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2">{keys.map((key) => <p key={key} className="text-sm text-slate-600"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-slate-200 bg-slate-50 p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-170 text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-slate-200">{rows.slice(0, 10).map((row, i) => <tr key={String(row.id || i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-600">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}

const COMPLIANCE_SCORE_STYLE = (score: number) => {
  if (score >= 90) return "text-teal-700 bg-teal-50 border-teal-200";
  if (score >= 75) return "text-amber-700 bg-amber-50 border-amber-200";
  return "text-red-700 bg-red-50 border-red-200";
};

function ActiveTripsTable({ trips, isLoading }: { trips: AnyRecord[]; isLoading: boolean }) {
  if (isLoading) return <LoadingState />;
  if (!trips.length) return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 p-6 text-center">
      <Route className="mx-auto h-8 w-8 text-slate-400" />
      <p className="mt-2 text-sm font-medium text-slate-500">No active trips at this time</p>
      <p className="mt-1 text-xs text-slate-400">Trips are auto-created when routes become active with assigned vehicles</p>
    </div>
  );
  return (
    <div className="space-y-3">
      {trips.map((trip) => {
        const score = Number(trip["routeComplianceScore"] ?? 100);
        const scoreStyle = COMPLIANCE_SCORE_STYLE(score);
        const stopsTotal     = Number(trip["totalPlannedStops"] ?? 0);
        const stopsCompleted = Number(trip["stopsCompleted"] ?? 0);
        const stopsRemaining = stopsTotal - stopsCompleted;
        return (
          <div key={String(trip["id"])} className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="flex flex-wrap items-start gap-3">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <Navigation className="h-4 w-4 text-teal-600" />
                  <span className="font-semibold text-slate-900">{String(trip["tripRef"] ?? `TRP-${trip["id"]}`)}</span>
                  <StatusBadge status={trip["status"]} />
                </div>
                <p className="mt-1 text-sm text-slate-500">
                  <span className="font-medium text-slate-700">{String(trip["vehicleCode"] ?? "--")}</span>
                  {" · "}
                  <span>{String(trip["driverName"] ?? "--")}</span>
                </p>
                <div className="mt-1.5 flex items-center gap-1 text-xs text-slate-500">
                  <MapPin className="h-3 w-3" />
                  <span className="truncate">{String(trip["origin"] ?? "Origin")} → {String(trip["destination"] ?? "Destination")}</span>
                </div>
              </div>
              <div className="flex flex-col items-end gap-1">
                <span className={`rounded-full border px-2.5 py-0.5 text-xs font-bold ${scoreStyle}`}>
                  {score.toFixed(0)}% compliant
                </span>
                <span className="text-xs text-slate-500">
                  {stopsCompleted}/{stopsTotal} stops · {stopsRemaining} remaining
                </span>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
