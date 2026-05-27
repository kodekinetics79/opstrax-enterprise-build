import { type ReactNode, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Activity, Bot, Camera, CircleDot, Gauge, RadioTower, Satellite, Send, ShieldAlert, Wrench, X } from "lucide-react";
import { AiInsightCard, DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import { useEventStream } from "@/hooks/useEventStream";
import { controlTowerApi } from "@/services/controlTowerApi";
import type { AnyRecord } from "@/types";

export function ControlTowerPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [activeFilter, setActiveFilter] = useState("All");
  const [activeTab, setActiveTab] = useState("Dispatch");
  const { data, isLoading } = useQuery({ queryKey: ["control-tower"], queryFn: controlTowerApi.summary, refetchInterval: 10000 });
  const detail = useQuery({
    queryKey: ["control-tower", "entity", selected?.vehicleId || selected?.id],
    queryFn: () => controlTowerApi.entity("vehicle", (selected?.vehicleId || selected?.id) as string | number),
    enabled: Boolean(selected?.vehicleId || selected?.id),
  });
  const stream = useEventStream();
  const qc = useQueryClient();
  const action = useMutation({
    mutationFn: (type: string) => type === "eta" ? controlTowerApi.sendEta() : type === "dispatch" ? controlTowerApi.createDispatchReview() : controlTowerApi.createMaintenanceReview(),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["control-tower"] }),
  });

  if (isLoading || !data) return <LoadingState />;
  const entities = ((data.entities as AnyRecord[]) || []).filter((entity) => {
    if (activeFilter === "All") return true;
    const text = JSON.stringify(entity).toLowerCase();
    return text.includes(activeFilter.toLowerCase());
  });
  const geofences = (data.geofences as AnyRecord[]) || [];
  const recommendations = (data.recommendations as AnyRecord[]) || [];
  const kpis = (data.kpis as AnyRecord) || {};
  const events = stream.length ? stream : ((data.events as AnyRecord[]) || []);
  const tabs = ["Dispatch", "Diagnostics", "Video Safety", "Benchmark"];

  return (
    <div className="control-tower space-y-6">
      <PageHeader
        eyebrow="Live Map / Control Tower"
        title="Real-time operations superiority"
        description="OpsTrax unifies live location, geofences, jobs, SLA risk, camera health, device diagnostics, video safety, replay evidence and customer ETA actions in one command surface."
        actions={<><button className="btn-primary" onClick={() => action.mutate("eta")}><Send className="h-4 w-4" /> Send ETA Update</button><button className="btn-ghost" onClick={() => action.mutate("dispatch")}>Create Dispatch Review</button><button className="btn-ghost" onClick={() => action.mutate("maintenance")}><Wrench className="h-4 w-4" /> Maintenance Review</button></>}
      />
      <ControlStatusStrip kpis={kpis} generatedAt={data.generatedAt} />
      <div className="grid gap-4 md:grid-cols-4 xl:grid-cols-8">
        <KpiCard label="Tracked Entities" value={String(kpis.trackedEntities ?? entities.length)} icon={<RadioTower />} status="Active" />
        <KpiCard label="Online Devices" value={String(kpis.onlineDevices ?? 0)} icon={<Satellite />} status="Live" />
        <KpiCard label="Online Cameras" value={String(kpis.onlineCameras ?? 0)} icon={<Camera />} status="Live" />
        <KpiCard label="Telemetry Quality" value={String(kpis.telemetryQuality ?? "--")} icon={<Gauge />} status="Healthy" />
        <KpiCard label="Fleet Readiness" value={String(kpis.fleetReadiness ?? "--")} icon={<Activity />} status="Active" />
        <KpiCard label="High Risk Units" value={String(kpis.highRiskUnits ?? 0)} icon={<ShieldAlert />} status="Review" />
        <KpiCard label="Speed Alerts" value={String(kpis.speedAlerts ?? 0)} icon={<CircleDot />} status="Warning" />
        <KpiCard label="AI Actions" value={recommendations.length} icon={<Bot />} status="Recommended" />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.35fr_.65fr]">
        <section className="panel p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="section-title">Live Operations Map</h2>
              <p className="mt-2 text-sm text-slate-400">Pins combine location, driver, speed, device health, camera health, route/job context and live risk status.</p>
            </div>
            <div className="flex flex-wrap gap-2">{["All","Speeding","Device offline","Camera offline","Fleet risk","Delayed"].map((filter) => <button key={filter} className={filter === activeFilter ? "btn-primary" : "btn-ghost"} onClick={() => setActiveFilter(filter)}>{filter}</button>)}</div>
          </div>
          <div className="map-surface mt-4 h-[660px]">
            <svg className="absolute inset-0 h-full w-full opacity-75">
              <path d="M80 410 C 220 320, 330 260, 460 220 S 780 190, 930 90" fill="none" stroke="#38bdf8" strokeWidth="2" strokeDasharray="8 8" />
              <path d="M120 180 C 250 250, 400 430, 760 460" fill="none" stroke="#2dd4bf" strokeWidth="2" strokeDasharray="10 10" />
              <path d="M180 520 C 360 490, 510 340, 860 360" fill="none" stroke="#f59e0b" strokeWidth="2" strokeDasharray="6 12" />
            </svg>
            {geofences.slice(0, 6).map((zone, index) => <div key={String(zone.id)} className="absolute rounded-full border border-teal-300/20 bg-teal-400/5" style={{ left: `${8 + index * 14}%`, top: `${16 + (index % 3) * 22}%`, width: 130, height: 130 }}><span className="absolute left-4 top-4 text-[10px] font-bold uppercase tracking-[0.16em] text-teal-200/80">{String(zone.name)}</span></div>)}
            {entities.slice(0, 24).map((entity, index) => <VehiclePin key={String(entity.id)} entity={entity} index={index} onSelect={setSelected} />)}
            <div className="absolute bottom-4 left-4 max-w-sm rounded-2xl border border-blue-200 bg-white/95 p-4 text-xs text-slate-600 shadow-lg backdrop-blur">
              <p className="font-semibold text-slate-900">Replay Ready</p>
              <p>{String((data.replay as AnyRecord)?.description || "GPS, speed, geofence and event replay placeholder.")}</p>
            </div>
          </div>
        </section>

        <aside className="space-y-6">
          <Panel title="Live Event Feed">
            <div className="space-y-3">{events.slice(0, 10).map((event, index) => <EventRow key={String(event.id || index)} event={event} />)}</div>
          </Panel>
          <Panel title="Priority Action Queue">
            <div className="space-y-3">{((data.actionQueue as AnyRecord[]) || []).slice(0, 8).map((item, i) => <div key={String(item.id || i)} className="rounded-xl border border-slate-200 bg-slate-50 p-3"><div className="flex justify-between gap-2"><p className="text-sm font-semibold text-slate-900">{String(item.title)}</p><RiskBadge risk={item.priority} /></div><p className="mt-1 text-xs text-slate-500">{String(item.moduleKey || item.module_key || "operations")}</p></div>)}</div>
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
          {activeTab === "Diagnostics" && <DataTable rows={(data.diagnostics as AnyRecord[]) || []} columns={["vehicleCode","deviceStatus","cameraStatus","readinessScore","dataQualityScore","riskScore","recommendedAction"]} />}
          {activeTab === "Video Safety" && <div className="grid gap-4 lg:grid-cols-3">{((data.safetyVideo as AnyRecord[]) || []).map((event) => <div key={String(event.id)} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm"><div className="flex aspect-video items-center justify-center rounded-xl border border-violet-100 bg-violet-50 text-violet-700"><Camera className="h-10 w-10" /></div><p className="mt-3 font-semibold text-slate-900">{String(event.eventNumber)}</p><p className="mt-1 text-sm text-slate-500">{String(event.aiSummary || event.eventType)}</p><div className="mt-3 flex gap-2"><RiskBadge risk={event.severity} /><StatusBadge status={event.evidenceStatus} /></div></div>)}</div>}
          {activeTab === "Benchmark" && <GapAnalysis cards={(data.competitorGapAnalysis as AnyRecord[]) || []} />}
        </div>
      </Panel>

      <EntityDrawer detail={detail.data} loading={detail.isLoading} onClose={() => setSelected(null)} />
    </div>
  );
}

function ControlStatusStrip({ kpis, generatedAt }: { kpis: AnyRecord; generatedAt?: unknown }) {
  return (
    <section className="control-status-strip">
      <div>
        <p className="section-title">Command Integrity</p>
        <h2>Live monitoring, safety intelligence, dispatch actioning and customer SLA control are unified.</h2>
      </div>
      <div className="control-status-grid">
        <span><b>{String(kpis.telemetryQuality ?? "--")}</b> Telemetry quality</span>
        <span><b>{String(kpis.fleetReadiness ?? "--")}</b> Readiness score</span>
        <span><b>{String(kpis.onlineCameras ?? 0)}</b> Cameras online</span>
        <span><b>{generatedAt ? new Date(String(generatedAt)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "--"}</b> Last sync</span>
      </div>
    </section>
  );
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return <button className={active ? "control-tab control-tab-active" : "control-tab"} onClick={onClick}>{children}</button>;
}

function VehiclePin({ entity, index, onSelect }: { entity: AnyRecord; index: number; onSelect: (entity: AnyRecord) => void }) {
  const risk = String(entity.riskLevel || "Low");
  const color = /high/i.test(risk) ? "bg-red-400" : /medium/i.test(risk) ? "bg-amber-300" : "bg-teal-300";
  return (
    <button className="map-pin group" style={{ left: `${10 + (index * 9) % 78}%`, top: `${16 + (index * 13) % 70}%` }} onClick={() => onSelect(entity)}>
      <span className={color} />
      <b className="pointer-events-none absolute left-5 top-0 hidden w-64 rounded-xl border border-blue-200 bg-white p-3 text-left text-xs text-slate-900 shadow-2xl group-hover:block">
        <span className="block font-semibold">{String(entity.label)}</span>
        <span className="mt-1 block text-slate-400">{String(entity.driverName || "Unassigned")} | {String(entity.speedMph ?? "--")} mph</span>
        <span className="mt-1 block text-slate-400">Device {String(entity.deviceStatus)} | Camera {String(entity.cameraStatus)}</span>
        <span className="mt-2 inline-block"><RiskBadge risk={entity.liveAlert} /></span>
      </b>
    </button>
  );
}

function GapAnalysis({ cards }: { cards: AnyRecord[] }) {
  return <section className="grid gap-4 lg:grid-cols-5">{cards.map((card) => <div key={String(card.capability)} className="rounded-2xl border border-blue-200 bg-blue-50/70 p-4"><p className="text-xs font-bold uppercase tracking-[0.18em] text-blue-700">{String(card.status)}</p><h3 className="mt-2 font-semibold text-slate-900">{String(card.capability)}</h3><p className="mt-2 text-sm leading-6 text-slate-600">{String(card.opstraxAdvantage)}</p></div>)}</section>;
}

function EventRow({ event }: { event: AnyRecord }) {
  return <div className="rounded-xl border border-slate-200 bg-slate-50 p-3"><div className="flex justify-between gap-3"><p className="text-sm font-semibold text-slate-900">{String(event.title || event.type || event.eventType)}</p><StatusBadge status={event.severity || "Live"} /></div><p className="mt-1 text-xs text-slate-500">{String(event.eventTime || event.generatedAt || "")}</p></div>;
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return <section className="panel p-5"><h2 className="section-title">{title}</h2><div className="mt-4">{children}</div></section>;
}

function EntityDrawer({ detail, loading, onClose }: { detail?: AnyRecord; loading: boolean; onClose: () => void }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm">
      <aside className="h-full w-full max-w-4xl overflow-y-auto border-l border-slate-200 bg-white p-6 shadow-2xl">
        <button className="float-right icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        <p className="section-title text-teal-300">Live Entity Command Drawer</p>
        <h2 className="mt-3 text-2xl font-semibold text-slate-900">{String(record.vehicleCode)}</h2>
        <div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status} /><RiskBadge risk={record.riskScore} /><span className="badge">Last seen {String(record.lastSeenAt || "--")}</span></div>
        <div className="mt-6 grid gap-4 lg:grid-cols-3">
          <Mini title="Live Telemetry" record={record} keys={["driverName","lat","lng","speedMph","heading","deviceStatus","cameraStatus"]} />
          <Mini title="Health & Diagnostics" record={record} keys={["readinessScore","dataQualityScore","riskScore","odometerMiles","type"]} />
          <Mini title="Competitive Edge" record={{ a: "SLA/job context", b: "Camera and gateway health", c: "Replay/evidence bundle", d: "ETA action ready" }} keys={["a","b","c","d"]} />
        </div>
        <Grid title="Active Jobs / SLA" rows={(detail?.activeJobs as AnyRecord[]) || []} columns={["jobNumber","status","slaStatus","eta","priority"]} />
        <Grid title="Safety Events" rows={(detail?.safetyEvents as AnyRecord[]) || []} columns={["eventNumber","eventType","severity","reviewStatus","occurredAt"]} />
        <Grid title="Video Events" rows={(detail?.videoEvents as AnyRecord[]) || []} columns={["eventNumber","eventType","severity","reviewStatus","evidenceStatus"]} />
        <Grid title="Maintenance Watch" rows={(detail?.maintenance as AnyRecord[]) || []} columns={["serviceType","status","priority","dueDate","riskScore"]} />
        <Grid title="Replay Trail Placeholder" rows={(detail?.replayTrail as AnyRecord[]) || []} columns={["lat","lng","speedMph","heading","eventType","eventTime"]} />
      </aside>
    </div>
  );
}

function Mini({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  return <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4"><h3 className="section-title">{title}</h3><div className="mt-3 space-y-2">{keys.map((key) => <p key={key} className="text-sm text-slate-600"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}</div></section>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-slate-200 bg-slate-50 p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[680px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((c) => <th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-slate-200">{rows.slice(0, 10).map((row, i) => <tr key={String(row.id || i)}>{columns.map((c) => <td key={c} className="px-3 py-2 text-slate-600">{String(row[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}
