import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router-dom";
import { MessageSquare, Send, Star, Truck } from "lucide-react";
import { AiInsightCard, DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge } from "@/components/ui";
import { useCustomerEtaRecommendations, useCustomerEtaSummary, useCustomerTracking } from "@/hooks/useBatch2";
import { customerEtaApi } from "@/services/customerEtaApi";
import type { AnyRecord } from "@/types";

export function CustomerEtaPage() {
  const summary = useCustomerEtaSummary();
  const recommendations = useCustomerEtaRecommendations();
  const communications = useQuery({ queryKey: ["customer-eta", "communications"], queryFn: customerEtaApi.communications });
  const qc = useQueryClient();
  const send = useMutation({ mutationFn: (jobId: string | number) => customerEtaApi.sendUpdate(jobId), onSuccess: async () => { await qc.invalidateQueries({ queryKey: ["customer-eta"] }); } });
  if (summary.isLoading) return <LoadingState />;
  const data = summary.data || {};
  const s = (data.summary as AnyRecord) || {};
  const jobs = (data.jobs as AnyRecord[]) || [];

  return <div className="space-y-6">
    <PageHeader eyebrow="Customer ETA Portal" title="Customer ETA operations" description="Monitor customer-facing tracking, send proactive updates, explain SLA risk and protect delivery experience." actions={<button className="btn-primary" onClick={() => jobs[0]?.id && send.mutate(String(jobs[0].id))}><Send className="h-4 w-4" /> Send Next Update</button>} />
    <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-6">
      {[["Tracked Jobs","totalTracked"],["ETA Risk","etaRisk"],["Updates Needed","updatesNeeded"],["Comms Sent","communicationsSent"],["Pending Comms","pendingCommunications"],["Experience Score","customerExperienceScore"]].map(([label,key])=><KpiCard key={key} label={label} value={String(s[key] ?? 0)} icon={<MessageSquare />} status={/Risk|Needed|Pending/.test(label) ? "Review" : "Healthy"} />)}
    </div>
    <div className="grid gap-6 xl:grid-cols-[1fr_380px]">
      <div className="space-y-4">
        <DataTable rows={jobs} columns={["jobNumber","customerName","trackingCode","status","eta","slaStatus","etaConfidenceLevel","customerUpdateStatus","recommendedAction"]} onSelect={(row)=>row.id && send.mutate(String(row.id))} />
        <DataTable rows={communications.data || []} columns={["customerName","jobNumber","channel","messageType","message","status","sentAt"]} />
      </div>
      <div className="space-y-4">
        <div className="panel p-5"><h2 className="section-title">ETA Wow Signals</h2><div className="mt-4 flex flex-wrap gap-2">{["Customer Experience Score","ETA Confidence Level","Branded Tracking Link","SLA Risk Explanation","Proof Preview","Customer Sentiment Placeholder"].map(x=><span key={x} className="badge">{x}</span>)}</div></div>
        {(recommendations.data || []).slice(0,4).map((x)=><AiInsightCard key={String(x.id)} insight={x} />)}
      </div>
    </div>
  </div>;
}

export function PublicEtaTrackingPage() {
  const params = useParams();
  const [rating, setRating] = useState(5);
  const tracking = useCustomerTracking(params.trackingCode);
  const feedback = useMutation({ mutationFn: () => customerEtaApi.feedback(String((tracking.data?.tracking as AnyRecord)?.id), { trackingCode: params.trackingCode, rating, sentiment: "Customer-facing placeholder", comments: "Feedback captured from branded tracking page." }) });
  if (tracking.isLoading) return <LoadingState />;
  const data = tracking.data;
  const row = data?.tracking as AnyRecord | undefined;
  if (!row) return <div className="min-h-screen bg-slate-950 p-8 text-slate-200">Tracking link not found.</div>;
  const timeline = (data?.timeline as AnyRecord[]) || [];
  return <main className="min-h-screen bg-slate-950 text-slate-100">
    <section className="mx-auto max-w-5xl px-6 py-10">
      <p className="text-xs font-bold uppercase tracking-[0.28em] text-teal-300">OpsTrax Transport Management Solution</p>
      <h1 className="mt-3 text-4xl font-semibold">Delivery tracking</h1>
      <p className="mt-2 text-slate-400">Connected transport. Intelligent control. Enterprise execution.</p>
      <div className="mt-8 grid gap-5 lg:grid-cols-[1fr_340px]">
        <div className="panel p-6">
          <div className="flex items-start justify-between gap-4"><div><p className="text-sm text-slate-400">Tracking code</p><h2 className="text-2xl font-semibold text-white">{String(row.trackingCode)}</h2></div><StatusBadge status={row.status} /></div>
          <div className="mt-6 grid gap-4 sm:grid-cols-2"><KpiCard label="Current ETA" value={String(row.eta || "Pending")} icon={<Truck />} status={String(row.etaConfidenceLevel || "Medium")} /><KpiCard label="ETA Confidence" value={String(row.etaConfidenceLevel)} icon={<Star />} status={String(row.etaConfidenceLevel || "Medium")} /></div>
          <div className="mt-8 grid gap-3 sm:grid-cols-5">{timeline.map((item)=><div key={String(item.label)} className={`rounded-2xl border p-4 ${item.complete ? "border-teal-400/30 bg-teal-500/10" : "border-white/10 bg-white/[0.03]"}`}><p className="text-sm font-semibold">{String(item.label)}</p></div>)}</div>
          <div className="mt-8 rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">Proof of Delivery Preview</h3><p className="mt-3 text-sm text-slate-300">Proof status: {String(row.proofStatus || "Pending")}</p><p className="mt-1 text-sm text-slate-500">Signature/photo links are intentionally hidden until proof is captured.</p></div>
        </div>
        <aside className="panel p-6"><h2 className="section-title">Customer Message</h2><p className="mt-3 text-slate-300">{String(data?.customerMessage)}</p><div className="mt-6"><RiskBadge risk={row.slaStatus} /><p className="mt-3 text-sm text-slate-400">SLA risk explanation: {row.slaStatus === "At Risk" ? "This service is being actively monitored due to ETA variance." : "This service is tracking within the expected SLA window."}</p></div><div className="mt-6"><label className="text-sm text-slate-300">Feedback / rating placeholder</label><input className="field mt-2" type="number" min="1" max="5" value={rating} onChange={(e)=>setRating(Number(e.target.value))} /><button className="btn-primary mt-3" onClick={()=>feedback.mutate()}>Submit Feedback</button></div></aside>
      </div>
    </section>
  </main>;
}
