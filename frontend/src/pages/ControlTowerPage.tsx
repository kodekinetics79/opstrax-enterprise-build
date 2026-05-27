import { useQuery } from "@tanstack/react-query";
import { Bot, CircleDot, MapPinned, RadioTower, Send } from "lucide-react";
import { AiInsightCard, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge } from "@/components/ui";
import { useEventStream } from "@/hooks/useEventStream";
import { controlTowerApi } from "@/services/controlTowerApi";
import type { AnyRecord } from "@/types";

export function ControlTowerPage() {
  const { data, isLoading } = useQuery({ queryKey: ["control-tower"], queryFn: controlTowerApi.summary, refetchInterval: 12000 });
  const stream = useEventStream();
  if (isLoading || !data) return <LoadingState />;
  const entities = (data.entities as AnyRecord[]) || [];
  const geofences = (data.geofences as AnyRecord[]) || [];
  const recommendations = (data.recommendations as AnyRecord[]) || [];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Live Map / Control Tower"
        title="Real-time exception command"
        description="No paid map dependency: OpsTrax renders a simulated premium control surface with live vehicle pins, route lines, geofences, incidents and AI recommendations."
        actions={<><button className="btn-primary"><Send className="h-4 w-4" /> Send ETA Update</button><button className="btn-ghost">Create Dispatch Review</button></>}
      />
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard label="Tracked Entities" value={entities.length} icon={<RadioTower />} status="Active" />
        <KpiCard label="Geofences" value={geofences.length} icon={<MapPinned />} status="Active" />
        <KpiCard label="Live Events" value={stream.length || ((data.events as AnyRecord[]) || []).length} icon={<CircleDot />} status="Live" />
        <KpiCard label="AI Recommendations" value={recommendations.length} icon={<Bot />} status="Recommended" />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.3fr_.7fr]">
        <div className="panel p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="section-title">Simulated Operations Map</h2>
            <div className="flex flex-wrap gap-2">{["Vehicles","Routes","Geofences","Incidents","Maintenance"].map((filter) => <button key={filter} className="btn-ghost">{filter}</button>)}</div>
          </div>
          <div className="map-surface mt-4 h-[620px]">
            <svg className="absolute inset-0 h-full w-full opacity-70">
              <path d="M80 410 C 220 320, 330 260, 460 220 S 780 190, 930 90" fill="none" stroke="#38bdf8" strokeWidth="2" strokeDasharray="8 8" />
              <path d="M120 180 C 250 250, 400 430, 760 460" fill="none" stroke="#2dd4bf" strokeWidth="2" strokeDasharray="10 10" />
            </svg>
            {geofences.slice(0, 6).map((zone, index) => (
              <div key={String(zone.id)} className="absolute rounded-full border border-teal-300/20 bg-teal-400/5" style={{ left: `${10 + index * 13}%`, top: `${18 + (index % 3) * 21}%`, width: 120, height: 120 }} />
            ))}
            {entities.slice(0, 18).map((entity, index) => (
              <button key={String(entity.id)} className="map-pin group" style={{ left: `${10 + (index * 9) % 78}%`, top: `${16 + (index * 13) % 70}%` }}>
                <span />
                <b className="pointer-events-none absolute left-5 top-0 hidden rounded-lg border border-white/10 bg-slate-950 px-2 py-1 text-xs text-white group-hover:block">{String(entity.label)}</b>
              </button>
            ))}
          </div>
        </div>
        <div className="space-y-6">
          <div className="panel p-5">
            <h2 className="section-title">Live Event Feed</h2>
            <div className="mt-4 space-y-3">
              {(stream.length ? stream : ((data.events as AnyRecord[]) || [])).slice(0, 10).map((event, index) => (
                <div key={String(event.id || index)} className="rounded-xl border border-white/10 bg-white/[0.04] p-3">
                  <div className="flex justify-between gap-3"><p className="text-sm font-semibold text-white">{String(event.title || event.type || event.eventType)}</p><StatusBadge status={event.severity || "Live"} /></div>
                  <p className="mt-1 text-xs text-slate-500">{String(event.eventTime || event.generatedAt || "")}</p>
                </div>
              ))}
            </div>
          </div>
          {recommendations.slice(0, 2).map((item) => <AiInsightCard key={String(item.id)} insight={item} />)}
          <div className="panel p-5"><h2 className="section-title">Selected Entity</h2><p className="mt-3 text-sm text-slate-400">Select a vehicle pin to open its operational drawer.</p><div className="mt-4"><RiskBadge risk="At Risk" /></div></div>
        </div>
      </div>
    </div>
  );
}
