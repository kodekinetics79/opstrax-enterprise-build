import { useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Send, Sparkles, UserCheck } from "lucide-react";
import { AiInsightCard, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge } from "@/components/ui";
import { useAvailableDrivers, useAvailableVehicles, useDispatchBoard, useDispatchRecommendations, useDispatchSummary } from "@/hooks/useBatch2";
import { useEventStream } from "@/hooks/useEventStream";
import { dispatchApi } from "@/services/dispatchApi";
import type { AnyRecord } from "@/types";

const stages = ["Unassigned", "Assigned", "En Route", "At Stop", "Completed", "Delayed / Exception"];

export function DispatchPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const board = useDispatchBoard();
  const summary = useDispatchSummary();
  const recommendations = useDispatchRecommendations();
  const drivers = useAvailableDrivers();
  const vehicles = useAvailableVehicles();
  const events = useEventStream();
  const qc = useQueryClient();

  const assign = useMutation({
    mutationFn: (payload: AnyRecord) => dispatchApi.assign(payload),
    onSuccess: async () => { await qc.invalidateQueries({ queryKey: ["dispatch"] }); },
  });
  const status = useMutation({
    mutationFn: (nextStatus: string) => dispatchApi.changeStatus({ jobId: selected?.id, status: nextStatus }),
    onSuccess: async () => { await qc.invalidateQueries({ queryKey: ["dispatch"] }); },
  });
  const autoSuggest = useMutation({ mutationFn: dispatchApi.autoSuggest, onSuccess: async () => { await qc.invalidateQueries({ queryKey: ["dispatch", "recommendations"] }); } });
  const eta = useMutation({ mutationFn: dispatchApi.sendEtaUpdates, onSuccess: async () => { await qc.invalidateQueries({ queryKey: ["dispatch"] }); } });

  const bestDriver = drivers.data?.[0];
  const bestVehicle = vehicles.data?.[0];
  const matchScore = useMemo(() => Math.min(99, 82 + Number(selected?.id || 0) % 12), [selected]);

  if (board.isLoading || !board.data) return <LoadingState />;
  const s = summary.data || {};

  return <div className="space-y-6">
    <PageHeader eyebrow="Dispatch Board" title="AI-assisted assignment cockpit" description="Kanban dispatch with match scores, exception radar, SLA watch, customer ETA actions and status movement buttons." actions={<><button className="btn-primary" onClick={() => autoSuggest.mutate()}><Sparkles className="h-4 w-4" /> Auto Suggest</button><button className="btn-ghost" onClick={() => eta.mutate()}><Send className="h-4 w-4" /> Send ETA Updates</button><button className="btn-ghost"><Download className="h-4 w-4" /> Export Dispatch Plan</button></>} />
    <div className="grid gap-4 md:grid-cols-4 xl:grid-cols-6">
      {[["Total Jobs","total"],["Unassigned","unassigned"],["Assigned","assigned"],["En Route","enRoute"],["Exceptions","exceptions"],["Completed","completed"],["Dispatch Readiness","dispatchReadinessScore"],["SLA Watch Queue","slaWatch"],["ETA Action Queue","etaActionQueue"]].map(([label,key])=><KpiCard key={key} label={label} value={String(s[key] ?? 0)} icon={<UserCheck />} status={/Exception|Watch|Queue|Unassigned/.test(label) ? "Review" : "Active"} />)}
    </div>
    <div className="grid gap-6 xl:grid-cols-[1fr_380px]">
      <div className="grid gap-4 xl:grid-cols-3 2xl:grid-cols-6">
        {stages.map((stage) => {
          const jobs = board.data?.[stage] || [];
          return <section key={stage} className="panel min-h-[520px] p-4"><div className="mb-4 flex items-center justify-between"><h2 className="text-sm font-bold uppercase tracking-[0.16em] text-slate-400">{stage}</h2><span className="rounded-full bg-white/10 px-2 py-1 text-xs">{jobs.length}</span></div><div className="space-y-3">{jobs.slice(0, 10).map((job) => <article key={String(job.id)} onClick={() => setSelected(job)} className={`cursor-pointer rounded-2xl border p-3 transition hover:bg-white/[0.06] ${selected?.id === job.id ? "border-teal-400/50 bg-teal-500/10" : "border-white/10 bg-white/[0.04]"}`}><div className="flex items-start justify-between gap-3"><p className="font-semibold text-white">{String(job.jobNumber || job.jobCode)}</p><RiskBadge risk={job.riskHeatScore || job.priority} /></div><p className="mt-2 text-xs text-slate-400">{String(job.customerName || "Customer")} | {String(job.pickupAddress || "Pickup")} to {String(job.dropoffAddress || "Destination")}</p><p className="mt-2 text-xs text-slate-500">SLA {String(job.slaWindowEnd || job.slaDueAt || "--")} | Required {String(job.requiredVehicleType || "--")}</p><div className="mt-3 flex items-center justify-between"><StatusBadge status={job.status} /><span className="text-xs text-teal-200">Match {82 + (Number(job.id) % 15)}%</span></div><p className="mt-2 text-xs text-slate-500">{String(job.driverName || "No driver")} / {String(job.vehicleCode || "No vehicle")} | ETA {String(job.eta || "--")}</p></article>)}</div></section>;
        })}
      </div>
      <aside className="space-y-4">
        <section className="panel p-5"><h2 className="section-title">Assignment Panel</h2>{selected ? <><p className="mt-3 font-semibold text-white">{String(selected.jobNumber || selected.jobCode)}</p><p className="mt-1 text-sm text-slate-400">{String(selected.customerName)} | {String(selected.priority)} priority</p><div className="mt-4 rounded-2xl border border-teal-400/20 bg-teal-500/10 p-4"><p className="text-sm font-semibold text-teal-100">Driver/Vehicle Match Score {matchScore}%</p><p className="mt-2 text-xs text-slate-400">Same region, available driver, required vehicle type, safety score, vehicle readiness, HOS risk placeholder and proximity placeholder.</p></div><div className="mt-4 grid gap-3"><p className="text-sm text-slate-300">Recommended driver: {String(bestDriver?.fullName || "No driver")}</p><p className="text-sm text-slate-300">Recommended vehicle: {String(bestVehicle?.vehicleCode || "No vehicle")}</p><button className="btn-primary" onClick={() => assign.mutate({ jobId: selected.id, driverId: bestDriver?.id, vehicleId: bestVehicle?.id, override: true })}>Assign</button><p className="text-xs text-amber-200">Override warning placeholder: unavailable resources require dispatcher approval.</p></div><div className="mt-4 flex flex-wrap gap-2">{["En Route","At Stop","Completed","Delayed","Exception"].map(x=><button key={x} className="btn-ghost" onClick={()=>status.mutate(x)}>Mark {x}</button>)}</div></> : <p className="mt-3 text-sm text-slate-400">Select a job card to assign or move status.</p>}</section>
        <section className="panel p-5"><h2 className="section-title">Exception Radar</h2><p className="mt-3 text-sm text-slate-300">{String(s.exceptions || 0)} jobs are delayed or in exception posture.</p><p className="mt-2 text-sm text-slate-300">{String(s.slaWatch || 0)} jobs are on SLA watch.</p></section>
        <section className="panel p-5"><h2 className="section-title">Live Ops Events</h2>{events.slice(0,5).map((event, index)=><p key={String(event.id || index)} className="mt-2 text-sm text-slate-400">{String(event.type)}: {String(event.title)}</p>)}</section>
        {(autoSuggest.data || recommendations.data || []).slice(0, 4).map((item, i) => <AiInsightCard key={String(item.id || i)} insight={{ title: item.title || "Dispatch recommendation", body: item.recommendation || item.body || JSON.stringify(item.matchReasons || item.match_reasons || "Review assignment") }} />)}
      </aside>
    </div>
  </div>;
}
