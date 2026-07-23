import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router-dom";
import { MessageSquare, Send, Star, Truck } from "lucide-react";
import { AiInsightCard, KpiCard, LoadingState, RiskBadge, StatusBadge, exportCsv } from "@/components/ui";
import { useCustomerEtaRecommendations, useCustomerEtaSummary, useCustomerTracking } from "@/hooks/useBatch2";
import { customerEtaApi } from "@/services/customerEtaApi";
import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// ── Job row ───────────────────────────────────────────────────────────────────

function JobRow({
  job,
  onSend,
  sending,
}: {
  job: AnyRecord;
  onSend: (id: string | number) => void;
  sending: boolean;
}) {
  const sla = String(job.slaStatus ?? job.sla_status ?? "On Track");
  const confidence = String(job.etaConfidenceLevel ?? job.eta_confidence_level ?? "High");
  const needsUpdate = String(job.customerUpdateStatus ?? job.customer_update_status ?? "") !== "Sent";
  const isAtRisk = sla === "At Risk" || sla === "Delayed";

  return (
    <tr className={`hover:bg-slate-50 ${isAtRisk ? "bg-red-50/40" : ""}`}>
      <td className="px-4 py-3">
        <p className="font-medium text-slate-900 text-sm">{String(job.jobNumber ?? job.job_number ?? "--")}</p>
        {job.trackingToken ? (
          <button
            type="button"
            onClick={() => {
              const url = `${window.location.origin}/eta/${String(job.trackingToken)}`;
              void navigator.clipboard?.writeText(url);
            }}
            className="text-xs text-teal-600 hover:underline mt-0.5"
            title="Copy secure customer tracking link"
          >
            Copy tracking link
          </button>
        ) : (
          <p className="text-xs text-slate-400 mt-0.5">Not shared yet</p>
        )}
      </td>
      <td className="px-4 py-3 text-sm text-slate-700">{String(job.customerName ?? job.customer_name ?? "--")}</td>
      <td className="px-4 py-3"><StatusBadge status={job.status} /></td>
      <td className="px-4 py-3 text-sm text-slate-700">{String(job.eta ?? "--")}</td>
      <td className="px-4 py-3"><RiskBadge risk={sla} /></td>
      <td className="px-4 py-3">
        <span className={`text-xs font-medium px-2 py-0.5 rounded-full border ${
          confidence === "High" || confidence === "At Risk"
            ? confidence === "At Risk" ? "bg-red-50 border-red-200 text-red-700" : "bg-teal-50 border-teal-200 text-teal-700"
            : "bg-amber-50 border-amber-200 text-amber-700"
        }`}>
          {confidence}
        </span>
      </td>
      <td className="px-4 py-3">
        {needsUpdate ? (
          <span className="text-xs px-2 py-0.5 rounded-full bg-amber-50 border border-amber-200 text-amber-700 font-medium">Needed</span>
        ) : (
          <span className="text-xs text-slate-400">Sent</span>
        )}
      </td>
      <td className="px-4 py-3 text-sm text-slate-500">{String(job.driverName ?? job.driver_name ?? "--")}</td>
      <td className="px-4 py-3 text-right">
        <button
          type="button"
          disabled={sending}
          onClick={() => onSend(job.id as string | number)}
          className="text-xs px-3 py-1 rounded-md bg-teal-50 border border-teal-200 text-teal-700 hover:bg-teal-100 transition-colors disabled:opacity-50"
        >
          {sending ? "Sending…" : "Send Update"}
        </button>
      </td>
    </tr>
  );
}

// ── Communication row ─────────────────────────────────────────────────────────

function CommRow({ comm }: { comm: AnyRecord }) {
  const channelColor =
    String(comm.channel) === "SMS" ? "bg-blue-50 border-blue-200 text-blue-700" :
    String(comm.channel) === "Email" ? "bg-violet-50 border-violet-200 text-violet-700" :
    "bg-green-50 border-green-200 text-green-700";

  return (
    <tr className="hover:bg-slate-50">
      <td className="px-4 py-3 text-sm font-medium text-slate-900">{String(comm.customerName ?? "--")}</td>
      <td className="px-4 py-3 text-xs text-slate-500">{String(comm.jobNumber ?? "--")}</td>
      <td className="px-4 py-3">
        <span className={`text-xs px-2 py-0.5 rounded-full border font-medium ${channelColor}`}>{String(comm.channel ?? "--")}</span>
      </td>
      <td className="px-4 py-3 text-xs text-slate-600">{String(comm.messageType ?? "--")}</td>
      <td className="px-4 py-3 text-xs text-slate-500 max-w-60 truncate">{String(comm.message ?? "--")}</td>
      <td className="px-4 py-3"><StatusBadge status={comm.status} /></td>
      <td className="px-4 py-3 text-xs text-slate-400">
        {comm.sentAt ? new Date(String(comm.sentAt)).toLocaleString() : "--"}
      </td>
    </tr>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function CustomerEtaPage() {
  const qc = useQueryClient();
  const [tab, setTab] = useState<"jobs" | "comms">("jobs");
  const [sendingId, setSendingId] = useState<string | number | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const summary = useCustomerEtaSummary();
  const recommendations = useCustomerEtaRecommendations();
  const commsQ = useQuery({ queryKey: ["customer-eta", "communications"], queryFn: () => unwrap<AnyRecord[]>(apiClient.get("/api/customer-eta/communications")) });

  const send = useMutation({
    mutationFn: (jobId: string | number) => customerEtaApi.sendUpdate(jobId),
    onSuccess: (data, jobId) => {
      qc.invalidateQueries({ queryKey: ["customer-eta"] });
      setSendingId(null);
      const token = (data as AnyRecord | undefined)?.trackingToken;
      if (token) {
        const url = `${window.location.origin}/eta/${String(token)}`;
        void navigator.clipboard?.writeText(url);
        showToast(`ETA sent for job ${String(jobId)} — secure tracking link copied`);
      } else {
        showToast(`ETA update sent for job ${String(jobId)}`);
      }
    },
    onError: () => setSendingId(null),
  });

  const bulkSend = useMutation({
    mutationFn: () => customerEtaApi.sendUpdate("bulk"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customer-eta"] });
      showToast("ETA updates sent to all at-risk jobs");
    },
  });

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

  function handleSend(id: string | number) {
    setSendingId(id);
    send.mutate(id);
  }

  if (summary.isLoading) return <LoadingState />;

  const data = (summary.data ?? {}) as AnyRecord;
  const s = (data.summary as AnyRecord) ?? data;
  const jobs = ((data.jobs as AnyRecord[]) ?? []).filter((j) => {
    if (!search) return true;
    const q = search.toLowerCase();
    return (
      String(j.jobNumber ?? "").toLowerCase().includes(q) ||
      String(j.customerName ?? "").toLowerCase().includes(q) ||
      String(j.trackingCode ?? "").toLowerCase().includes(q)
    );
  });
  const comms = (commsQ.data ?? []) as AnyRecord[];

  return (
    <div className="flex flex-col gap-6 py-6">
      {toast && (
        <div className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">{toast}</div>
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Customer ETA Portal</h1>
          <p className="text-sm text-slate-500 mt-0.5">Real-time delivery visibility, proactive communication and customer experience management</p>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            className="btn-secondary text-sm"
            onClick={() => exportCsv("customer-eta", jobs)}
          >
            Export CSV
          </button>
          <button
            type="button"
            disabled={bulkSend.isPending}
            className="bg-teal-600 hover:bg-teal-700 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors flex items-center gap-2"
            onClick={() => bulkSend.mutate()}
          >
            <Send className="w-4 h-4" />
            {bulkSend.isPending ? "Sending…" : "Bulk Send Updates"}
          </button>
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Tracked Jobs",        val: s.totalTracked ?? s.total_tracked ?? jobs.length },
          { label: "ETA Risk",            val: s.etaRisk ?? s.eta_risk ?? 0,           accent: "text-red-600" },
          { label: "Updates Needed",      val: s.updatesNeeded ?? s.updates_needed ?? 0, accent: "text-amber-600" },
          { label: "Communications Sent", val: s.communicationsSent ?? s.communications_sent ?? 0, accent: "text-teal-600" },
          { label: "Pending Comms",       val: s.pendingCommunications ?? s.pending_communications ?? 0, accent: "text-amber-600" },
          { label: "Experience Score",    val: s.customerExperienceScore ?? s.customer_experience_score ?? "--", accent: "text-violet-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-30">
            <span className={`text-2xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* ETA Wow Signals */}
      <div className="panel flex flex-wrap gap-2 items-center p-4">
        <span className="text-xs font-semibold text-slate-600 mr-2">ETA differentiators:</span>
        {["Branded tracking link", "ETA confidence level", "SLA risk explanation", "Real-time driver location", "POD preview", "Customer satisfaction rating"].map((x) => (
          <span key={x} className="text-xs px-2.5 py-1 rounded-full bg-teal-50 border border-teal-200 text-teal-700 font-medium">{x}</span>
        ))}
      </div>

      {/* Tab bar + search */}
      <div className="panel flex gap-2 items-center">
        {([["jobs", "At-Risk Jobs"], ["comms", "Communication Log"]] as const).map(([key, label]) => (
          <button
            key={key}
            type="button"
            onClick={() => setTab(key)}
            className={`px-4 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
              tab === key ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
            }`}
          >
            {label}
            {key === "jobs" && Number(s.updatesNeeded ?? 0) > 0 && (
              <span className="ml-1.5 text-xs px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-700 font-semibold">{String(s.updatesNeeded)}</span>
            )}
          </button>
        ))}
        {tab === "jobs" && (
          <input
            type="search"
            placeholder="Search jobs, customers…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-52"
          />
        )}
      </div>

      {/* Jobs table */}
      {tab === "jobs" && (
        <div className="grid gap-6 xl:grid-cols-[1fr_300px]">
          <div className="panel overflow-hidden p-0">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 bg-slate-50">
                    {["Job", "Customer", "Status", "ETA", "SLA", "Confidence", "Update", "Driver", ""].map((h) => (
                      <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {jobs.length === 0 ? (
                    <tr><td colSpan={9} className="px-4 py-8 text-center text-slate-400 text-sm">No at-risk jobs right now</td></tr>
                  ) : (
                    jobs.map((job, i) => (
                      <JobRow
                        key={String(job.id ?? i)}
                        job={job}
                        onSend={handleSend}
                        sending={sendingId === job.id && send.isPending}
                      />
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* AI recommendations */}
          <div className="flex flex-col gap-3">
            <h2 className="text-sm font-semibold text-slate-700">AI recommendations</h2>
            {((recommendations.data as AnyRecord[]) ?? []).slice(0, 4).map((x) => (
              <AiInsightCard key={String(x.id)} insight={x} />
            ))}
          </div>
        </div>
      )}

      {/* Communications log */}
      {tab === "comms" && (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Customer", "Job", "Channel", "Type", "Message", "Status", "Sent"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {comms.length === 0 ? (
                  <tr><td colSpan={7} className="px-4 py-8 text-center text-slate-400 text-sm">No communications yet</td></tr>
                ) : (
                  comms.map((comm, i) => (
                    <CommRow key={String(comm.id ?? i)} comm={comm} />
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Public tracking page (unchanged) ─────────────────────────────────────────

export function PublicEtaTrackingPage() {
  const params = useParams();
  const [rating, setRating] = useState(5);
  const tracking = useCustomerTracking(params.trackingCode);
  const feedback = useMutation({
    mutationFn: () =>
      customerEtaApi.feedback(String((tracking.data?.tracking as AnyRecord)?.id), {
        trackingCode: params.trackingCode,
        rating,
        sentiment: rating >= 4 ? "Positive" : rating >= 3 ? "Neutral" : "Negative",
        comments: "",
      }),
  });
  if (tracking.isLoading) return <LoadingState />;
  const data = tracking.data;
  const row = data?.tracking as AnyRecord | undefined;
  if (!row) return <div className="min-h-screen bg-[#f3f6fb] p-8 text-slate-700">Tracking link not found.</div>;
  const timeline = (data?.timeline as AnyRecord[]) || [];

  return (
    <main className="min-h-screen bg-[#f3f6fb] text-slate-900">
      <section className="mx-auto max-w-5xl px-6 py-10">
        <p className="text-xs font-bold uppercase tracking-[0.28em] text-teal-700">OpsTrax Transport Management Solution</p>
        <h1 className="mt-3 text-4xl font-semibold text-slate-950">Delivery tracking</h1>
        <p className="mt-2 text-slate-600">Connected transport. Intelligent control. Enterprise execution.</p>
        <div className="mt-8 grid gap-5 lg:grid-cols-[1fr_340px]">
          <div className="panel p-6">
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-sm text-slate-500">Shipment reference</p>
                <h2 className="text-2xl font-semibold text-slate-950">{String(row.reference ?? row.trackingCode ?? row.tracking_code ?? "--")}</h2>
              </div>
              <StatusBadge status={row.status} />
            </div>
            <div className="mt-6 grid gap-4 sm:grid-cols-2">
              <KpiCard label="Current ETA" value={String(row.eta || "Pending")} icon={<Truck />} status={String(row.etaConfidenceLevel ?? row.eta_confidence_level ?? "Medium")} />
              <KpiCard label="ETA Confidence" value={String(row.etaConfidenceLevel ?? row.eta_confidence_level)} icon={<Star />} status={String(row.etaConfidenceLevel ?? "Medium")} />
            </div>
            <div className="mt-8 grid gap-3 sm:grid-cols-5">
              {timeline.map((item) => (
                <div key={String(item.label)} className={`rounded-2xl border p-4 ${item.complete ? "border-teal-200 bg-teal-50 text-teal-800" : "border-slate-200 bg-slate-50 text-slate-600"}`}>
                  <p className="text-sm font-semibold">{String(item.label)}</p>
                </div>
              ))}
            </div>
            <div className="mt-8 rounded-2xl border border-slate-200 bg-slate-50 p-4">
              <h3 className="text-sm font-semibold text-slate-700">Proof of Delivery Preview</h3>
              <p className="mt-3 text-sm text-slate-700">Proof status: {String(row.proofStatus ?? row.proof_status ?? "Pending")}</p>
              <p className="mt-1 text-sm text-slate-500">Signature/photo links available once proof is captured.</p>
            </div>
          </div>
          <aside className="panel p-6">
            <h2 className="text-sm font-semibold text-slate-700">Customer Message</h2>
            <p className="mt-3 text-slate-700">{String(data?.customerMessage ?? "")}</p>
            <div className="mt-6">
              <RiskBadge risk={row.slaStatus ?? row.sla_status} />
              <p className="mt-3 text-sm text-slate-600">
                {(row.slaStatus ?? row.sla_status) === "At Risk"
                  ? "This service is being actively monitored due to ETA variance."
                  : "This service is tracking within the expected SLA window."}
              </p>
            </div>
            <div className="mt-6">
              <label htmlFor="delivery-rating" className="text-sm text-slate-700">Rate this delivery (1–5 stars)</label>
              <input
                id="delivery-rating"
                className="field mt-2"
                type="number"
                min="1"
                max="5"
                value={rating}
                onChange={(e) => setRating(Number(e.target.value))}
              />
              <button
                type="button"
                className="btn-primary mt-3 flex items-center gap-2"
                onClick={() => feedback.mutate()}
              >
                <MessageSquare className="w-4 h-4" />
                Submit Feedback
              </button>
            </div>
          </aside>
        </div>
      </section>
    </main>
  );
}
