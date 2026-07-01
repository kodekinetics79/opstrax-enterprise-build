import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileText, Truck, Camera, MessageSquare, Send, AlertTriangle } from "lucide-react";
import { PageHeader, KpiCard, StatusBadge, DataTable, EmptyState, LoadingState } from "@/components/ui";
import { portalApi } from "@/services/portalApi";
import type { AnyRecord } from "@/types";

function money(value: unknown, currency = "USD") {
  const n = Number(value ?? 0);
  return `${currency === "USD" ? "$" : currency + " "}${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function ErrorPanel({ message }: { message?: string }) {
  return (
    <div className="panel flex items-center gap-3 border-l-2 border-red-400 p-5">
      <AlertTriangle className="h-5 w-5 shrink-0 text-red-600" />
      <div>
        <p className="font-semibold text-slate-900">Couldn’t load your data</p>
        <p className="text-sm text-slate-500">{message ?? "Please try again shortly."}</p>
      </div>
    </div>
  );
}

export function CustomerPortalPage() {
  const qc = useQueryClient();
  const [selectedJob, setSelectedJob] = useState<AnyRecord | null>(null);
  const [rating, setRating] = useState("");
  const [subject, setSubject] = useState("");
  const [comment, setComment] = useState("");

  const invoicesQ = useQuery({ queryKey: ["portal", "invoices"], queryFn: portalApi.invoices });
  const jobsQ = useQuery({ queryKey: ["portal", "jobs"], queryFn: portalApi.jobs });
  const feedbackQ = useQuery({ queryKey: ["portal", "feedback"], queryFn: portalApi.feedback });
  const proofsQ = useQuery({
    queryKey: ["portal", "proofs", selectedJob?.id],
    queryFn: () => portalApi.jobProofs(selectedJob!.id as string | number),
    enabled: !!selectedJob,
  });

  const submit = useMutation({
    mutationFn: (payload: AnyRecord) => portalApi.submitFeedback(payload),
    onSuccess: () => {
      setRating(""); setSubject(""); setComment("");
      qc.invalidateQueries({ queryKey: ["portal", "feedback"] });
    },
  });

  const invoices = invoicesQ.data ?? [];
  const jobs = jobsQ.data ?? [];
  const feedback = feedbackQ.data ?? [];

  const outstanding = useMemo(
    () => invoices.reduce((sum, i) => sum + Number(i.balanceDue ?? 0), 0),
    [invoices],
  );
  const overdueCount = useMemo(
    () => invoices.filter((i) => String(i.arStatus ?? "").startsWith("Overdue")).length,
    [invoices],
  );

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Your account"
        title="Customer Portal"
        description="Your shipments, delivery proof, and invoices — always up to date. Everything here is scoped to your account only."
      />

      {/* KPI summary — computed from your live data only. */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <KpiCard label="Outstanding Balance" value={money(outstanding)} status={overdueCount > 0 ? "overdue" : undefined} icon={<FileText className="h-5 w-5" />} />
        <KpiCard label="Overdue Invoices" value={overdueCount} status={overdueCount > 0 ? "overdue" : undefined} />
        <KpiCard label="Active Shipments" value={jobs.filter((j) => !/delivered|completed|cancelled/i.test(String(j.status ?? ""))).length} icon={<Truck className="h-5 w-5" />} />
      </div>

      {/* ── Invoices ── */}
      <section className="space-y-3">
        <h2 className="section-title flex items-center gap-2"><FileText className="h-4 w-4 text-teal-600" />Your Invoices</h2>
        {invoicesQ.isLoading ? <LoadingState /> :
          invoicesQ.isError ? <ErrorPanel message={(invoicesQ.error as Error)?.message} /> :
          invoices.length === 0 ? <EmptyState title="No invoices yet" subtitle="Invoices will appear here once your shipments are billed." /> :
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
            {invoices.map((inv, i) => (
              <div key={i} className="panel p-4">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Invoice</p>
                    <p className="font-bold text-slate-900">{String(inv.invoiceNumber ?? "—")}</p>
                  </div>
                  <StatusBadge status={inv.arStatus} />
                </div>
                <div className="mt-3 grid grid-cols-2 gap-2 text-sm">
                  <div><p className="text-slate-400 text-xs">Total</p><p className="font-semibold text-slate-900">{money(inv.total, String(inv.currency ?? "USD"))}</p></div>
                  <div><p className="text-slate-400 text-xs">Balance Due</p><p className="font-semibold text-slate-900">{money(inv.balanceDue, String(inv.currency ?? "USD"))}</p></div>
                </div>
              </div>
            ))}
          </div>}
      </section>

      {/* ── Shipments / trip status ── */}
      <section className="space-y-3">
        <h2 className="section-title flex items-center gap-2"><Truck className="h-4 w-4 text-teal-600" />Your Shipments</h2>
        {jobsQ.isLoading ? <LoadingState /> :
          jobsQ.isError ? <ErrorPanel message={(jobsQ.error as Error)?.message} /> :
          jobs.length === 0 ? <EmptyState title="No shipments yet" subtitle="Your jobs and their live status will appear here." /> :
          <DataTable
            rows={jobs}
            columns={["jobNumber", "status", "scheduledStart", "scheduledEnd", "pickupAddress", "dropoffAddress", "eta"]}
            onSelect={(row) => setSelectedJob(row)}
          />}
      </section>

      {/* ── Proof of delivery gallery (for the selected shipment) ── */}
      {selectedJob && (
        <section className="space-y-3">
          <h2 className="section-title flex items-center gap-2"><Camera className="h-4 w-4 text-teal-600" />Proof of Delivery — {String(selectedJob.jobNumber ?? selectedJob.id)}</h2>
          {proofsQ.isLoading ? <LoadingState /> :
            proofsQ.isError ? <ErrorPanel message={(proofsQ.error as Error)?.message} /> :
            (proofsQ.data ?? []).length === 0 ? <EmptyState title="No proof captured yet" subtitle="Delivery photos and signatures will appear here once captured." /> :
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
              {(proofsQ.data ?? []).map((p, i) => (
                <div key={i} className="panel p-4">
                  <div className="flex items-center justify-between">
                    <p className="font-bold text-slate-900">{String(p.proofType ?? "Proof")}</p>
                    <StatusBadge status={p.status} />
                  </div>
                  <p className="mt-2 text-sm text-slate-500">Received by <span className="font-medium text-slate-700">{String(p.receiverName ?? "—")}</span></p>
                  <p className="text-xs text-slate-400">{p.completedAt ? new Date(String(p.completedAt)).toLocaleString() : ""}</p>
                  <p className="mt-2 text-xs font-semibold text-teal-600">{Array.isArray(p.artifacts) ? `${(p.artifacts as unknown[]).length} artifact(s)` : ""}</p>
                </div>
              ))}
            </div>}
        </section>
      )}

      {/* ── Feedback / complaint intake ── */}
      <section className="space-y-3">
        <h2 className="section-title flex items-center gap-2"><MessageSquare className="h-4 w-4 text-teal-600" />Feedback & Complaints</h2>
        <div className="panel p-5">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
            <select className="field" value={String(selectedJob?.id ?? "")} onChange={(e) => setSelectedJob(jobs.find((j) => String(j.id) === e.target.value) ?? null)}>
              <option value="">Select a shipment…</option>
              {jobs.map((j, i) => <option key={i} value={String(j.id)}>{String(j.jobNumber ?? j.id)}</option>)}
            </select>
            <select className="field" value={rating} onChange={(e) => setRating(e.target.value)}>
              <option value="">Rating…</option>
              {[1, 2, 3, 4, 5].map((n) => <option key={n} value={n}>{n} ★</option>)}
            </select>
            <input className="field md:col-span-2" placeholder="Subject" value={subject} onChange={(e) => setSubject(e.target.value)} />
          </div>
          <textarea className="field mt-3 w-full" rows={3} placeholder="Tell us what happened…" value={comment} onChange={(e) => setComment(e.target.value)} />
          <div className="mt-3 flex items-center justify-between">
            {submit.isError ? <span className="text-sm text-red-600">Couldn’t submit — please try again.</span> : <span />}
            <button
              type="button"
              className="btn-primary flex items-center gap-2 text-sm disabled:opacity-50"
              disabled={!selectedJob || submit.isPending}
              onClick={() => selectedJob && submit.mutate({ jobId: selectedJob.id, rating: rating || undefined, subject, comment, feedbackType: "complaint" })}
            >
              <Send className="h-4 w-4" />{submit.isPending ? "Submitting…" : "Submit feedback"}
            </button>
          </div>
        </div>

        {feedbackQ.isLoading ? <LoadingState /> :
          feedbackQ.isError ? <ErrorPanel message={(feedbackQ.error as Error)?.message} /> :
          feedback.length === 0 ? <EmptyState title="No feedback submitted yet" /> :
          <div className="flex flex-col gap-2">
            {feedback.map((f, i) => (
              <div key={i} className="panel flex items-center justify-between p-4">
                <div>
                  <p className="font-semibold text-slate-900">{String(f.subject ?? f.feedbackType ?? "Feedback")}</p>
                  <p className="text-sm text-slate-500">{String(f.comment ?? "")}</p>
                </div>
                <StatusBadge status={f.status} />
              </div>
            ))}
          </div>}
      </section>
    </div>
  );
}
