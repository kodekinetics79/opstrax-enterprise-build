import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileText, Truck, Camera, MessageSquare, Send, AlertTriangle, Printer, X } from "lucide-react";
import { PageHeader, KpiCard, StatusBadge, DataTable, EmptyState, LoadingState } from "@/components/ui";
import { ChangePasswordCard } from "@/components/ChangePasswordCard";
import { portalApi } from "@/services/portalApi";
import type { AnyRecord } from "@/types";

// ISO 4217 exponents that are not 2. A three-decimal Gulf currency printed to two
// places misstates the invoice a customer is being asked to pay.
const MINOR_UNITS: Record<string, number> = {
  JPY: 0, KRW: 0, CLP: 0, ISK: 0, VND: 0,
  KWD: 3, BHD: 3, OMR: 3, JOD: 3, TND: 3,
};

function money(value: unknown, currency = "USD") {
  const digits = MINOR_UNITS[String(currency).toUpperCase()] ?? 2;
  const n = Number(value ?? 0);
  return `${currency === "USD" ? "$" : currency + " "}${n.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })}`;
}

function day(value: unknown) {
  const raw = String(value ?? "").slice(0, 10);
  return raw || "—";
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
  const [openInvoiceId, setOpenInvoiceId] = useState<string | null>(null);
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
    <div className="flex h-full flex-col gap-6 overflow-y-auto">
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
              <button
                key={String(inv.id ?? i)}
                type="button"
                onClick={() => inv.id && setOpenInvoiceId(String(inv.id))}
                disabled={!inv.id}
                className="panel p-4 text-left transition hover:border-teal-300 hover:shadow-md disabled:cursor-default"
              >
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
                {inv.id ? <p className="mt-3 text-xs font-semibold text-teal-700">View invoice &rarr;</p> : null}
              </button>
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

      {/* Account & security — self-service, no email/SMTP required */}
      <section className="space-y-3 print:hidden">
        <h2 className="section-title">Account &amp; security</h2>
        <ChangePasswordCard />
      </section>

      {openInvoiceId && (
        <InvoiceDocument invoiceId={openInvoiceId} onClose={() => setOpenInvoiceId(null)} />
      )}
    </div>
  );
}

// The invoice as the CUSTOMER sees it: both parties with their tax registration
// numbers, the priced lines, and the tax summary by rate their AP team reconciles
// against. Printing uses the browser with a print stylesheet — the same approach
// the detention evidence page already ships, and the only PDF path in the product.
function InvoiceDocument({ invoiceId, onClose }: { invoiceId: string; onClose: () => void }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ["portal", "invoice", invoiceId],
    queryFn: () => portalApi.invoice(invoiceId),
  });

  const inv = (data ?? {}) as AnyRecord;
  const cur = String(inv.currency ?? "USD");
  const lines = (inv.lines ?? []) as AnyRecord[];
  const taxRows = (inv.taxBreakdown ?? []) as AnyRecord[];
  const payments = (inv.payments ?? []) as AnyRecord[];
  const taxLabel = String(inv.sellerTaxRegime ?? "").toLowerCase() === "gst" ? "GST" : "VAT";

  return (
    <div
      className="fixed inset-0 z-50 overflow-y-auto bg-slate-900/50 p-4 print:static print:bg-white print:p-0"
      onClick={onClose}
    >
      <div
        className="mx-auto max-w-3xl rounded-2xl bg-white p-6 shadow-2xl print:max-w-none print:rounded-none print:shadow-none"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between print:hidden">
          <h2 className="text-lg font-bold text-slate-900">Invoice</h2>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => window.print()}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 px-3 py-1.5 text-sm font-semibold text-slate-700 hover:bg-slate-50"
            >
              <Printer className="h-4 w-4" /> Print / save PDF
            </button>
            <button type="button" onClick={onClose} aria-label="Close" className="rounded-lg border border-slate-300 p-1.5 text-slate-500 hover:bg-slate-50">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        {isLoading ? <LoadingState /> : error ? <ErrorPanel message={(error as Error)?.message} /> : (
          <>
            <div className="flex items-start justify-between border-b-2 border-slate-900 pb-4">
              <div>
                <p className="text-xl font-black tracking-tight text-slate-900">{String(inv.sellerName ?? "")}</p>
                <p className="text-xs text-slate-500">
                  {inv.sellerTaxRegistrationNo
                    ? `${taxLabel} No. ${String(inv.sellerTaxRegistrationNo)}`
                    : `No ${taxLabel} registration on file`}
                </p>
              </div>
              <div className="text-right">
                <p className="text-sm font-black uppercase tracking-widest text-slate-900">{taxLabel} Invoice</p>
                <p className="font-mono text-sm font-bold text-slate-700">{String(inv.invoiceNumber ?? "")}</p>
              </div>
            </div>

            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <div>
                <p className="text-[10px] font-black uppercase tracking-widest text-slate-400">Billed to</p>
                <p className="font-semibold text-slate-900">{String(inv.customerName ?? "")}</p>
                <p className="text-xs text-slate-500">
                  {inv.customerTaxId ? `${taxLabel} No. ${String(inv.customerTaxId)}` : "No tax registration recorded"}
                </p>
              </div>
              <div className="grid grid-cols-2 gap-2 text-xs">
                <div><p className="text-slate-400">Issued</p><p className="font-semibold text-slate-800">{day(inv.issuedAt)}</p></div>
                <div><p className="text-slate-400">Due</p><p className="font-semibold text-slate-800">{day(inv.dueAt)}</p></div>
                <div><p className="text-slate-400">Place of supply</p><p className="font-semibold text-slate-800">{String(inv.placeOfSupply ?? "—")}</p></div>
                <div><p className="text-slate-400">Status</p><p className="font-semibold text-slate-800">{String(inv.arStatus ?? "")}</p></div>
              </div>
            </div>

            <table className="mt-5 w-full text-left text-xs">
              <thead className="border-b border-slate-300 text-[10px] uppercase tracking-wider text-slate-500">
                <tr>
                  <th className="pb-2">Description</th>
                  <th className="pb-2 text-right">Qty</th>
                  <th className="pb-2 text-right">Rate</th>
                  <th className="pb-2 text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {lines.length === 0 ? (
                  <tr><td colSpan={4} className="py-3 text-slate-400">No line detail recorded on this invoice.</td></tr>
                ) : lines.map((l, i) => (
                  <tr key={i}>
                    <td className="py-2 pr-2 text-slate-800">
                      {String(l.description ?? "")}
                      {l.chargeCode ? <span className="ml-1.5 font-mono text-[10px] text-slate-400">{String(l.chargeCode)}</span> : null}
                    </td>
                    <td className="py-2 text-right text-slate-600">
                      {Number(l.quantity ?? 0).toLocaleString()}{l.unit ? ` ${String(l.unit)}` : ""}
                    </td>
                    <td className="py-2 text-right text-slate-600">{money(l.unitRate, cur)}</td>
                    <td className="py-2 text-right font-semibold text-slate-900">{money(l.amount, cur)}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div className="mt-4 ml-auto w-full max-w-xs space-y-1 text-sm">
              <div className="flex justify-between text-slate-600"><span>Net</span><span>{money(inv.subtotal, cur)}</span></div>
              <div className="flex justify-between text-slate-600"><span>{taxLabel}</span><span>{money(inv.taxTotal, cur)}</span></div>
              <div className="flex justify-between border-t-2 border-slate-900 pt-1 text-base font-black text-slate-900">
                <span>Total</span><span>{money(inv.total, cur)}</span>
              </div>
              <div className="flex justify-between text-slate-600"><span>Paid</span><span>{money(inv.amountPaid, cur)}</span></div>
              <div className="flex justify-between font-bold text-slate-900"><span>Balance due</span><span>{money(inv.balanceDue, cur)}</span></div>
            </div>

            {taxRows.length > 0 && (
              <div className="mt-5">
                <p className="text-[10px] font-black uppercase tracking-widest text-slate-400">{taxLabel} summary</p>
                <table className="mt-2 w-full text-left text-xs">
                  <thead className="border-b border-slate-200 text-[10px] uppercase tracking-wider text-slate-500">
                    <tr><th className="pb-1">Code</th><th className="pb-1">Rate</th><th className="pb-1 text-right">Taxable</th><th className="pb-1 text-right">{taxLabel}</th></tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {taxRows.map((t, i) => (
                      <tr key={i}>
                        <td className="py-1.5 font-mono text-slate-700">{String(t.taxCode ?? "—")}</td>
                        <td className="py-1.5 text-slate-600">{(Number(t.rate ?? 0) * 100).toFixed(2)}%</td>
                        <td className="py-1.5 text-right text-slate-600">{money(t.taxableAmount, cur)}</td>
                        <td className="py-1.5 text-right font-semibold text-slate-900">{money(t.taxAmount, cur)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {payments.length > 0 && (
              <div className="mt-5">
                <p className="text-[10px] font-black uppercase tracking-widest text-slate-400">Payments received</p>
                <ul className="mt-1 space-y-1 text-xs text-slate-600">
                  {payments.map((pm, i) => (
                    <li key={i} className="flex justify-between">
                      <span>{day(pm.receivedAt)} · {String(pm.paymentMethod ?? "")} {String(pm.paymentReference ?? "")}</span>
                      <span className="font-semibold text-slate-800">{money(pm.amount, cur)}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
