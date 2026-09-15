import { useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Send, Sparkles } from "lucide-react";
import { AiInsightCard, ErrorState, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv } from "@/components/ui";
import { useAvailableDrivers, useAvailableVehicles, useDispatchBoard, useDispatchRecommendations, useDispatchSummary } from "@/hooks/useBatch2";
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
  // Never blend the demo Node simulator into customer dispatch. If the
  // authenticated dispatch API supplies events, render those; otherwise empty.
  const events = (((board.data as unknown as Record<string, unknown>)["events"] as AnyRecord[] | undefined) ?? []);
  const stageMap = board.data as unknown as Record<string, unknown>;
  const rawStageMap = (stageMap["stageMap"] ?? stageMap) as Record<string, AnyRecord[]>;
  const summaryMetrics = [
    ["Total jobs", "total", "neutral"],
    ["Unassigned", "unassigned", "review"],
    ["Assigned", "assigned", "neutral"],
    ["En route", "enRoute", "neutral"],
    ["Exceptions", "exceptions", "review"],
    ["Completed", "completed", "positive"],
    ["Dispatch readiness", "dispatchReadinessScore", "neutral"],
    ["SLA watch queue", "slaWatch", "review"],
    ["ETA action queue", "etaActionQueue", "review"],
  ] as const;

  return <div className="page-stack pb-4">
    <PageHeader
      eyebrow="Dispatch Board"
      title="AI-assisted assignment cockpit"
      description="Kanban dispatch with match scores, exception radar, SLA watch, customer ETA actions and status movement buttons."
      actions={<>
        <button type="button" className="btn-primary min-h-11 sm:min-h-9" onClick={() => autoSuggest.mutate()}><Sparkles className="h-4 w-4" /> Auto Suggest</button>
        <button type="button" className="btn-ghost min-h-11 sm:min-h-9" onClick={() => eta.mutate()}><Send className="h-4 w-4" /> Send ETA Updates</button>
        <button type="button" className="btn-ghost min-h-11 sm:min-h-9" onClick={() => {
          exportCsv("dispatch-plan", stages.flatMap((stage) => (rawStageMap[stage] ?? []).map((record: AnyRecord) => ({
            jobNumber: record["jobNumber"],
            customerName: record["customerName"],
            status: record["status"],
            driverName: record["driverName"],
            vehicleCode: record["vehicleCode"],
            eta: record["eta"],
            slaStatus: record["slaStatus"],
            priority: record["priority"],
          }))));
        }}><Download className="h-4 w-4" /> Export Dispatch Plan</button>
      </>}
    />

    <section className="panel p-3" aria-label="Dispatch summary">
      <div className="-mx-1 overflow-x-auto px-1 pb-1">
        <dl className="flex min-w-max gap-2">
          {summaryMetrics.map(([label, key, tone]) => (
            <div
              key={key}
              className={`w-32 rounded-xl border px-3 py-2 ${tone === "review" ? "border-amber-200 bg-amber-50/70" : tone === "positive" ? "border-emerald-200 bg-emerald-50/70" : "border-slate-200 bg-slate-50/70"}`}
            >
              <dt className="truncate text-[10px] font-bold uppercase tracking-[0.1em] text-slate-500" title={label}>{label}</dt>
              <dd className={`mt-0.5 text-lg font-bold leading-none tabular-nums ${tone === "review" ? "text-amber-700" : tone === "positive" ? "text-emerald-700" : "text-slate-900"}`}>{String(s[key] ?? 0)}</dd>
            </div>
          ))}
        </dl>
      </div>
    </section>

    <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_22rem]">
      <section className="panel min-w-0 p-3" aria-labelledby="dispatch-jobs-heading">
        <div className="mb-3 flex items-center justify-between gap-3">
          <div>
            <p className="section-title">Dispatch jobs</p>
            <h2 id="dispatch-jobs-heading" className="mt-0.5 text-base font-bold text-slate-900">Assignment and status board</h2>
          </div>
          <span className="text-xs font-medium text-slate-500">{String(s.total ?? 0)} jobs</span>
        </div>
        <div className="grid gap-3 md:grid-cols-2 2xl:grid-cols-3">
          {stages.map((stage) => {
            const jobs = rawStageMap[stage] ?? [];
            return <section key={stage} className="rounded-xl border border-slate-200 bg-slate-50/70 p-2.5">
              <div className="mb-2 flex items-center justify-between gap-2">
                <h3 className="text-[11px] font-bold uppercase tracking-[0.12em] text-slate-500">{stage}</h3>
                <span className="rounded-full bg-white px-2 py-0.5 text-xs font-semibold text-slate-600">{jobs.length}</span>
              </div>
              <div className="space-y-2">
                {jobs.slice(0, 10).map((job) => <button
                  type="button"
                  key={String(job["id"])}
                  onClick={() => setSelected(job)}
                  className={`min-h-11 w-full rounded-xl border p-2.5 text-left transition hover:border-teal-200 hover:bg-teal-50/50 ${selected?.["id"] === job["id"] ? "border-teal-300 bg-teal-50" : "border-slate-200 bg-white"}`}
                >
                  <div className="flex items-start justify-between gap-2"><p className="font-semibold text-slate-900">{String(job["jobNumber"] ?? job["jobCode"] ?? "")}</p><RiskBadge risk={job["riskHeatScore"] ?? job["priority"]} /></div>
                  <p className="mt-1 text-xs leading-5 text-slate-500">{String(job["customerName"] ?? "Customer")} | {String(job["pickupAddress"] ?? "Pickup")} to {String(job["dropoffAddress"] ?? "Destination")}</p>
                  <p className="mt-1 text-xs text-slate-500">SLA {String(job["slaWindowEnd"] ?? job["slaDueAt"] ?? "--")} | Required {String(job["requiredVehicleType"] ?? "--")}</p>
                  <div className="mt-2 flex items-center justify-between gap-2"><StatusBadge status={job["status"]} /><span className="text-xs font-medium text-teal-700">{job["matchScore"] != null ? `Match ${job["matchScore"]}%` : job["assignedDriverId"] ? "Assigned" : "Unassigned"}</span></div>
                  <p className="mt-1 text-xs text-slate-500">{String(job["driverName"] ?? "No driver")} / {String(job["vehicleCode"] ?? "No vehicle")} | ETA {String(job["eta"] ?? "--")}</p>
                </button>)}
                {!jobs.length ? <p className="rounded-lg border border-dashed border-slate-200 bg-white px-3 py-4 text-center text-xs text-slate-500">No jobs in this stage.</p> : null}
              </div>
            </section>;
          })}
        </div>
      </section>

      <aside className="space-y-3">
        <section className="panel p-3">
          <h2 className="section-title">Assignment Panel</h2>
          {selected ? <>
            <div className="mt-2 flex items-start justify-between gap-3">
              <div><p className="font-semibold text-slate-900">{String(selected.jobNumber || selected.jobCode)}</p><p className="mt-0.5 text-sm text-slate-500">{String(selected.customerName)} | {String(selected.priority)} priority</p></div>
              <span className="rounded-full border border-teal-200 bg-teal-50 px-2 py-1 text-xs font-bold text-teal-700">{matchScore != null ? `${matchScore}% match` : "Match —"}</span>
            </div>
            <p className="mt-2 text-xs leading-5 text-slate-500">Match combines driver readiness, safety, compliance, HOS, open DVIR defects, vehicle readiness and job risk.</p>
            <div className="mt-3 grid gap-2 text-sm">
              <p className="text-slate-700">Recommended driver: <strong>{String(bestDriver?.fullName || "No driver")}</strong></p>
              <p className="text-slate-700">Recommended vehicle: <strong>{String(bestVehicle?.vehicleCode || "No vehicle")}</strong></p>
              <button type="button" className="btn-primary min-h-11 w-full sm:min-h-9" onClick={() => assign.mutate({ jobId: selected.id, driverId: bestDriver?.id, vehicleId: bestVehicle?.id, override: true })}>Assign</button>
              <p className="text-xs text-amber-700">Assigning unavailable resources requires dispatcher approval and triggers an override audit entry.</p>
            </div>
            <div className="mt-3 flex flex-wrap gap-2">{["En Route", "At Stop", "Completed", "Delayed", "Exception"].map((next) => <button key={next} type="button" className="btn-ghost min-h-11 sm:min-h-8" onClick={() => status.mutate(next)}>Mark {next}</button>)}</div>
          </> : <p className="mt-2 text-sm text-slate-500">Select a job card to assign or move status.</p>}
        </section>

        <section className="panel p-3">
          <h2 className="section-title">Exception Radar</h2>
          <div className="mt-2 grid grid-cols-2 gap-2 text-sm">
            <p className="rounded-xl bg-amber-50 p-2 text-amber-900"><strong className="block text-lg leading-none tabular-nums">{String(s.exceptions || 0)}</strong><span className="mt-1 block text-xs">Delayed or exception</span></p>
            <p className="rounded-xl bg-slate-50 p-2 text-slate-700"><strong className="block text-lg leading-none tabular-nums">{String(s.slaWatch || 0)}</strong><span className="mt-1 block text-xs">On SLA watch</span></p>
          </div>
        </section>

        <section className="panel p-3">
          <h2 className="section-title">Live Ops Events</h2>
          <div className="mt-2 space-y-1.5">{events.slice(0, 5).map((event, index) => <p key={String(event.id || index)} className="text-sm text-slate-500">{String(event.type)}: {String(event.title)}</p>)}</div>
        </section>
        {(autoSuggest.data || recommendations.data || []).slice(0, 4).map((item, index) => <AiInsightCard key={String(item.id || index)} insight={{ title: item.title || "Dispatch recommendation", body: item.recommendation || item.body || String(item.matchReasons || item.match_reasons || "Review assignment") }} />)}
      </aside>
    </div>
  </div>;
}
