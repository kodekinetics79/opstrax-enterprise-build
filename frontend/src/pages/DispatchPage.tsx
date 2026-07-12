import { useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Send, Sparkles, UserCheck } from "lucide-react";
import { AiInsightCard, ErrorState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv } from "@/components/ui";
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

  // Real, multi-factor match scoring from the fields the dispatch API already
  // returns — composite readiness (safety + readiness + compliance), remaining
  // HOS hours, and open DVIR defects — instead of the previous risk-only formula.
  const driverScore = (d: AnyRecord) => {
    const readiness = Number(
      d?.matchReadiness ?? d?.match_readiness ??
      ((Number(d?.readinessScore ?? d?.readiness_score ?? 50) + Number(d?.safetyScore ?? d?.safety_score ?? 50) + Number(d?.complianceScore ?? d?.compliance_score ?? 50)) / 3),
    );
    const hos = Number(d?.availableHosHours ?? d?.available_hos_hours ?? 0);
    const defects = Number(d?.openDefectCount ?? d?.open_defect_count ?? 0);
    return Math.max(0, readiness * (hos > 0 ? 1 : 0.6) - Math.min(15, defects * 5));
  };
  const vehicleScore = (v: AnyRecord) => Number(v?.matchReadiness ?? v?.match_readiness ?? v?.readinessScore ?? v?.readiness_score ?? v?.healthScore ?? 50);

  // Recommend the highest-scoring available resources, not just the first row.
  const bestDriver = useMemo(() => [...(drivers.data ?? [])].sort((a, b) => driverScore(b) - driverScore(a))[0], [drivers.data]);
  const bestVehicle = useMemo(() => [...(vehicles.data ?? [])].sort((a, b) => vehicleScore(b) - vehicleScore(a))[0], [vehicles.data]);
  const matchScore = useMemo(() => {
    if (!selected || !bestDriver) return null;
    const risk = Number(selected?.riskScore ?? selected?.riskHeatScore ?? 10);
    const d = driverScore(bestDriver);
    const v = bestVehicle ? vehicleScore(bestVehicle) : d;
    return Math.round(Math.min(100, Math.max(0, d * 0.55 + v * 0.35 - risk * 0.1)));
  }, [selected, bestDriver, bestVehicle]);

  if (board.isLoading) return <LoadingState />;
  if (board.isError) return <ErrorState message={(board.error as Error)?.message} />;
  if (!board.data) return <LoadingState />;
  const s = summary.data || {};

  return <div className="space-y-6">
    <PageHeader eyebrow="Dispatch Board" title="AI-assisted assignment cockpit" description="Kanban dispatch with match scores, exception radar, SLA watch, customer ETA actions and status movement buttons." actions={<><button type="button" className="btn-primary" onClick={() => autoSuggest.mutate()}><Sparkles className="h-4 w-4" /> Auto Suggest</button><button type="button" className="btn-ghost" onClick={() => eta.mutate()}><Send className="h-4 w-4" /> Send ETA Updates</button><button type="button" className="btn-ghost" onClick={() => { const sm = ((board.data as unknown as Record<string,unknown>)?.["stageMap"] ?? board.data) as Record<string, AnyRecord[]> | undefined; exportCsv("dispatch-plan", stages.flatMap(st => (sm?.[st] ?? []).map((r: AnyRecord) => ({ jobNumber: r["jobNumber"], customerName: r["customerName"], status: r["status"], driverName: r["driverName"], vehicleCode: r["vehicleCode"], eta: r["eta"], slaStatus: r["slaStatus"], priority: r["priority"] })))); }}><Download className="h-4 w-4" /> Export Dispatch Plan</button></>} />
    <div className="grid gap-4 md:grid-cols-4 xl:grid-cols-6">
      {[["Total Jobs","total"],["Unassigned","unassigned"],["Assigned","assigned"],["En Route","enRoute"],["Exceptions","exceptions"],["Completed","completed"],["Dispatch Readiness","dispatchReadinessScore"],["SLA Watch Queue","slaWatch"],["ETA Action Queue","etaActionQueue"]].map(([label,key])=><KpiCard key={key} label={label} value={String(s[key] ?? 0)} icon={<UserCheck />} status={/Exception|Watch|Queue|Unassigned/.test(label) ? "Review" : "Active"} />)}
    </div>
    <div className="grid gap-6 xl:grid-cols-[1fr_380px]">
      <div className="grid gap-4 xl:grid-cols-3 2xl:grid-cols-6">
        {stages.map((stage) => {
          const stageMap = (board.data as unknown as Record<string, unknown>);
          const rawMap = (stageMap?.["stageMap"] ?? stageMap) as Record<string, AnyRecord[]> | undefined;
          const jobs: AnyRecord[] = rawMap?.[stage] ?? [];
          return <section key={stage} className="panel min-h-[520px] p-4"><div className="mb-4 flex items-center justify-between"><h2 className="text-sm font-bold uppercase tracking-[0.16em] text-slate-400">{stage}</h2><span className="rounded-full bg-slate-100 px-2 py-1 text-xs text-slate-600">{jobs.length}</span></div><div className="space-y-3">{jobs.slice(0, 10).map((job) => <article key={String(job["id"])} onClick={() => setSelected(job)} className={`cursor-pointer rounded-2xl border p-3 transition hover:bg-slate-50 ${selected?.["id"] === job["id"] ? "border-teal-300 bg-teal-50" : "border-slate-200 bg-white"}`}><div className="flex items-start justify-between gap-3"><p className="font-semibold text-slate-900">{String(job["jobNumber"] ?? job["jobCode"] ?? "")}</p><RiskBadge risk={job["riskHeatScore"] ?? job["priority"]} /></div><p className="mt-2 text-xs text-slate-500">{String(job["customerName"] ?? "Customer")} | {String(job["pickupAddress"] ?? "Pickup")} to {String(job["dropoffAddress"] ?? "Destination")}</p><p className="mt-2 text-xs text-slate-500">SLA {String(job["slaWindowEnd"] ?? job["slaDueAt"] ?? "--")} | Required {String(job["requiredVehicleType"] ?? "--")}</p><div className="mt-3 flex items-center justify-between"><StatusBadge status={job["status"]} /><span className="text-xs text-teal-600">{job["matchScore"] != null ? `Match ${job["matchScore"]}%` : job["assignedDriverId"] ? "Assigned" : "Unassigned"}</span></div><p className="mt-2 text-xs text-slate-500">{String(job["driverName"] ?? "No driver")} / {String(job["vehicleCode"] ?? "No vehicle")} | ETA {String(job["eta"] ?? "--")}</p></article>)}</div></section>;
        })}
      </div>
      <aside className="space-y-4">
        <section className="panel p-5"><h2 className="section-title">Assignment Panel</h2>{selected ? <><p className="mt-3 font-semibold text-slate-900">{String(selected.jobNumber || selected.jobCode)}</p><p className="mt-1 text-sm text-slate-500">{String(selected.customerName)} | {String(selected.priority)} priority</p><div className="mt-4 rounded-2xl border border-teal-200 bg-teal-50 p-4"><p className="text-sm font-semibold text-teal-700">Driver/Vehicle Match Score {matchScore != null ? `${matchScore}%` : "—"}</p><p className="mt-2 text-xs text-slate-500">Composite of driver readiness (safety, compliance, HOS), open DVIR defects, vehicle readiness and job risk.</p></div><div className="mt-4 grid gap-3"><p className="text-sm text-slate-700">Recommended driver: {String(bestDriver?.fullName || "No driver")}</p><p className="text-sm text-slate-700">Recommended vehicle: {String(bestVehicle?.vehicleCode || "No vehicle")}</p><button type="button" className="btn-primary" onClick={() => assign.mutate({ jobId: selected.id, driverId: bestDriver?.id, vehicleId: bestVehicle?.id, override: true })}>Assign</button><p className="text-xs text-amber-700">Note: assigning unavailable resources requires dispatcher approval and will trigger an override audit entry.</p></div><div className="mt-4 flex flex-wrap gap-2">{["En Route","At Stop","Completed","Delayed","Exception"].map(x=><button key={x} type="button" className="btn-ghost" onClick={()=>status.mutate(x)}>Mark {x}</button>)}</div></> : <p className="mt-3 text-sm text-slate-500">Select a job card to assign or move status.</p>}</section>
        <section className="panel p-5"><h2 className="section-title">Exception Radar</h2><p className="mt-3 text-sm text-slate-700">{String(s.exceptions || 0)} jobs are delayed or in exception posture.</p><p className="mt-2 text-sm text-slate-700">{String(s.slaWatch || 0)} jobs are on SLA watch.</p></section>
        <section className="panel p-5"><h2 className="section-title">Live Ops Events</h2>{events.slice(0,5).map((event, index)=><p key={String(event.id || index)} className="mt-2 text-sm text-slate-400">{String(event.type)}: {String(event.title)}</p>)}</section>
        {(autoSuggest.data || recommendations.data || []).slice(0, 4).map((item, i) => <AiInsightCard key={String(item.id || i)} insight={{ title: item.title || "Dispatch recommendation", body: item.recommendation || item.body || String(item.matchReasons || item.match_reasons || "Review assignment") }} />)}
      </aside>
    </div>
  </div>;
}
