import { useRef, useState } from "react";
import { chart } from "@/styles/tokens";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useNavigate } from "react-router";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer } from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { jobsApi } from "@/services/jobsApi";
import { financeOrderToCashApi } from "@/services/financeOrderToCashApi";
import { exportCsv, LoadingState, EmptyState, ErrorState, KpiCard, DataTable } from "@/components/ui";
import { useAuth } from "@/hooks/useAuth";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useHasPermission } from "@/hooks/usePermission";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import type { AnyRecord } from "@/types";

// Present the real revenue-spine payment status as a human label the canonical StatusBadge
// understands. issued_invoices.paymentStatus is 'paid' | 'partial' | 'unpaid'; an unpaid
// invoice past its due date is surfaced as Overdue.
function invoiceDisplayStatus(paymentStatus: string, balanceDue: number, dueAt: string): string {
  const ps = String(paymentStatus).toLowerCase();
  if (ps === "paid" || balanceDue <= 0) return "Paid";
  if (dueAt && new Date(dueAt).getTime() < Date.now()) return "Overdue";
  if (ps === "partial") return "Partial";
  return "Issued";
}

const financialApi = {
  // Real revenue-spine invoices (built + tested in the Finance module), NOT module_records.
  invoices: () =>
    unwrap<{ items: AnyRecord[] }>(apiClient.get("/api/issued-invoices")).then((res) =>
      (res.items ?? []).map((r) => {
        const total = Number(r.total ?? 0);
        const amountPaid = Number(r.amountPaid ?? 0);
        const balanceDue = Number(r.balanceDue ?? total - amountPaid);
        const dueAt = String(r.dueAt ?? "");
        const agingDays = balanceDue > 0 && dueAt
          ? Math.max(0, Math.floor((Date.now() - new Date(dueAt).getTime()) / 86_400_000))
          : 0;
        return {
          ...r,
          invoiceNumber: r.invoiceNumber ?? String(r.id),
          customerName: r.customerName ?? (r.customerId != null ? `Customer #${r.customerId}` : "—"),
          paymentStatus: invoiceDisplayStatus(String(r.paymentStatus ?? r.status ?? ""), balanceDue, dueAt),
          dueDate: dueAt ? dueAt.slice(0, 10) : "",
          agingDays,
          amount: total,
          amountPaid,
          balanceDue,
          total,
          currency: currencyCode(r.currency),
        };
      })
    ),
  payments: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/payments")).then((rows) =>
      rows.map((r) => ({
        ...r,
        paymentNumber: r.paymentReference ?? r.payment_reference ?? r.paymentNumber ?? r.payment_number ?? String(r.id),
        customerName: r.customerName ?? r.customer_name ?? "",
        amount: Number(r.amount ?? 0),
        paymentMethod: r.paymentMethod ?? r.payment_method ?? "",
        paymentDate: r.receivedAt ?? r.received_at ?? "",
        invoiceRef: r.invoiceNumber ?? r.invoice_number ?? "",
        status: r.recordStatus ?? r.record_status ?? "Recorded",
        providerSettlementClaim: r.providerSettlementClaim ?? r.provider_settlement_claim ?? false,
      }))
    ),
  profitability: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/profitability")).then((rows) =>
      rows.map((r) => ({
        ...r,
        entityName: r.entityName ?? r.entity_name ?? String(r.id),
        entityType: r.entityType ?? r.entity_type ?? "Customer",
        revenueEstimate: Number(r.revenueEstimate ?? r.revenue_estimate ?? 0),
        totalCost: Number(r.totalCost ?? r.total_cost ?? 0),
        grossMargin: r.grossMargin == null && r.gross_margin == null ? null : Number(r.grossMargin ?? r.gross_margin),
        grossMarginPercent: r.grossMarginPercent == null && r.gross_margin_percent == null ? null : Number(r.grossMarginPercent ?? r.gross_margin_percent),
        invoiceCount: Number(r.invoiceCount ?? r.invoice_count ?? 0),
        costRecordCount: Number(r.costRecordCount ?? r.cost_record_count ?? 0),
        currency: currencyCode(r.currency ?? r.currency_code),
      }))
    ),
};

async function loadInvoiceRows() {
  return financialApi.invoices();
}

async function loadPaymentRows() {
  return financialApi.payments();
}

async function loadProfitabilityRows() {
  return financialApi.profitability();
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function PaymentStatusBadge({ status }: { status: string }) {
  const cls =
    status === "Recorded" ? "bg-sky-50 border-sky-200 text-sky-700" :
    status === "Pending" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function utcDateTime(value: unknown): string {
  if (!value) return "—";
  const date = new Date(String(value));
  if (Number.isNaN(date.getTime())) return "—";
  return `${new Intl.DateTimeFormat("en-US", {
    timeZone: "UTC",
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
    second: "2-digit",
    hour12: true,
  }).format(date)} UTC`;
}

function MarginBadge({ pct }: { pct: number }) {
  const cls = pct >= 28 ? "bg-teal-50 border-teal-200 text-teal-700" : pct >= 18 ? "bg-amber-50 border-amber-200 text-amber-700" : "bg-red-50 border-red-200 text-red-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{pct.toFixed(1)}%</span>;
}

// ── Tabs ──────────────────────────────────────────────────────────────────────

function currencyCode(value: unknown): string {
  const code = String(value ?? "").trim().toUpperCase();
  return /^[A-Z]{3}$/.test(code) ? code : "Unknown";
}

function money(n: number, currency: string): string {
  if (currency === "Unknown") return `${n.toLocaleString(undefined, { maximumFractionDigits: 2 })} (currency unavailable)`;
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency, maximumFractionDigits: 2 }).format(n);
  } catch {
    return `${n.toLocaleString(undefined, { maximumFractionDigits: 2 })} ${currency}`;
  }
}

function totalsByCurrency(rows: AnyRecord[], value: (row: AnyRecord) => number): Array<{ currency: string; total: number }> {
  const totals = new Map<string, number>();
  rows.forEach((row) => {
    const currency = currencyCode(row.currency);
    totals.set(currency, (totals.get(currency) ?? 0) + value(row));
  });
  return [...totals].sort(([left], [right]) => left.localeCompare(right)).map(([currency, total]) => ({ currency, total }));
}

type PaymentTarget = {
  id: string;
  invoiceNumber: string;
  customerName: string;
  balanceDue: number;
  currency: string;
};

function PaymentDialog({ target, saving, serverError, onClose, onSubmit }: {
  target: PaymentTarget;
  saving: boolean;
  serverError: string | null;
  onClose: () => void;
  onSubmit: (input: { amount: number; currency: string; paymentReference: string; paymentMethod: string }) => void;
}) {
  const isLocalRehearsal = import.meta.env.DEV && import.meta.env.VITE_POC_REHEARSAL === "true";
  const [amount, setAmount] = useState(String(target.balanceDue));
  const [paymentReference, setPaymentReference] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("bank_transfer");
  const [validationError, setValidationError] = useState<string | null>(null);
  const dialogRef = useDialogFocus<HTMLDivElement>(true, onClose);

  const submit = () => {
    const parsedAmount = Number(amount);
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setValidationError("Enter a payment amount greater than zero.");
      return;
    }
    if (parsedAmount > target.balanceDue) {
      setValidationError(`Payment cannot exceed the outstanding balance of ${money(target.balanceDue, target.currency)}.`);
      return;
    }
    if (!paymentReference.trim()) {
      setValidationError("Enter the bank, cheque, card or remittance reference used to reconcile this payment.");
      return;
    }
    setValidationError(null);
    onSubmit({ amount: parsedAmount, currency: target.currency, paymentReference: paymentReference.trim(), paymentMethod });
  };

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/50 p-4" role="presentation">
      <div ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby="record-payment-title" className="panel w-full max-w-lg p-5 shadow-2xl">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 id="record-payment-title" className="text-lg font-bold text-slate-900">Record invoice payment</h2>
            <p className="mt-1 text-sm text-slate-500">{target.invoiceNumber} · {target.customerName}</p>
          </div>
          <button type="button" className="btn-secondary" onClick={onClose} disabled={saving} aria-label="Close payment dialog">Close</button>
        </div>
        <p className="mt-4 rounded-lg border border-blue-200 bg-blue-50 p-3 text-sm text-blue-800">
          Outstanding balance: <strong>{money(target.balanceDue, target.currency)}</strong>. {isLocalRehearsal
            ? "Local test ledger only; records a simulated receipt and does not move money."
            : "Record only funds confirmed by your payment provider."}
        </p>
        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <label className="text-sm font-medium text-slate-700">Amount ({target.currency})
            <input autoFocus className="input mt-1 w-full" inputMode="decimal" type="number" min="0.01" max={target.balanceDue} step="0.01" value={amount} onChange={(event) => setAmount(event.target.value)} />
          </label>
          <label className="text-sm font-medium text-slate-700">Payment method
            <select className="input mt-1 w-full" value={paymentMethod} onChange={(event) => setPaymentMethod(event.target.value)}>
              <option value="bank_transfer">Bank transfer</option>
              <option value="cheque">Cheque</option>
              <option value="card">Card</option>
              <option value="cash">Cash</option>
              <option value="other">Other</option>
            </select>
          </label>
        </div>
        <label className="mt-4 block text-sm font-medium text-slate-700">Payment reference
          <input className="input mt-1 w-full" value={paymentReference} onChange={(event) => setPaymentReference(event.target.value)} placeholder="Bank transaction or remittance reference" />
        </label>
        {(validationError || serverError) && <div className="mt-4 rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-700" role="alert">{validationError || serverError}</div>}
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-secondary" onClick={onClose} disabled={saving}>Cancel</button>
          <button type="button" className="btn-primary" onClick={submit} disabled={saving}>{saving ? "Recording…" : "Record payment"}</button>
        </div>
      </div>
    </div>
  );
}

function JobBillingPreparation() {
  const hasPermission = useHasPermission();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [jobId, setJobId] = useState("");
  const [chargeName, setChargeName] = useState("");
  const [description, setDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [unitRate, setUnitRate] = useState("");
  const [currency, setCurrency] = useState("USD");
  const [notice, setNotice] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const draftKeys = useRef(new Map<string, string>());
  const canReadJobs = hasPermission("jobs:view") || hasPermission("shipments:view") || hasPermission("dispatch:view");
  const canReadCharges = hasPermission("charge.read");
  const canCreateCharge = hasPermission("charge.create");
  const canReady = hasPermission("finance.job.ready_to_bill");
  const canDraft = hasPermission("finance.invoice_draft.create");
  const jobsQ = useQuery({ queryKey: ["billing-job-options", search], queryFn: () => jobsApi.listPaged({ limit: 200, search }), enabled: canReadJobs });
  const detailQ = useQuery({ queryKey: ["billing-job-detail", jobId], queryFn: () => jobsApi.detail(jobId), enabled: canReadJobs && Boolean(jobId) });
  const chargesQ = useQuery({ queryKey: ["job-charges", jobId], queryFn: () => financeOrderToCashApi.jobCharges(jobId), enabled: canReadCharges && Boolean(jobId) });
  const job = detailQ.data?.record;
  const charges = chargesQ.data ?? [];
  const currencies = new Set(charges.map((charge) => currencyCode(charge.currency)));
  const terminal = /^(completed|delivered|ready_to_bill)$/i.test(String(job?.status ?? ""));
  const hasCustomer = Boolean(job?.customerId ?? job?.customer_id);
  const prerequisite = !jobId ? "Choose a job to review its charges." : detailQ.isLoading ? "Loading the selected job…" : !hasCustomer ? "Link an active customer in Jobs before drafting an invoice." : !terminal ? "Complete delivery and review the proof before preparing the invoice." : !charges.length ? "Add the agreed billable charge before preparing the invoice." : currencies.size !== 1 || currencies.has("Unknown") ? "Charges must use one known currency before drafting. Review the charges." : "Charges are ready for draft review. Invoice issue still requires authorized approval.";
  const action = useMutation({
    mutationFn: async (input: { kind: "charge" | "ready" | "draft"; selectedJobId: string }) => {
      if (input.kind === "charge") {
        const q = Number(quantity), rate = Number(unitRate), amount = Math.round(q * rate * 100) / 100;
        if (!chargeName.trim() || !Number.isFinite(q) || q <= 0 || !Number.isFinite(rate) || rate <= 0 || !Number.isFinite(amount) || amount <= 0 || !/^[A-Z]{3}$/.test(currency)) throw new Error("Enter a charge name, positive quantity and unit rate, and a three-letter currency.");
        return financeOrderToCashApi.createJobCharge({ jobId: Number(input.selectedJobId), chargeCode: `MANUAL-${crypto.randomUUID()}`, chargeName: chargeName.trim(), description: description.trim(), quantity: q, unitRate: rate, amount, currency });
      }
      if (input.kind === "ready") return financeOrderToCashApi.markReadyToBill(input.selectedJobId);
      let key = draftKeys.current.get(input.selectedJobId);
      if (!key) { key = crypto.randomUUID(); draftKeys.current.set(input.selectedJobId, key); }
      return financeOrderToCashApi.createInvoiceDraft(input.selectedJobId, key);
    },
    onSuccess: async (_, input) => {
      setNotice({ kind: "success", text: input.kind === "charge" ? "Manual charge saved. Review its amount and currency below." : input.kind === "ready" ? "Job marked ready to bill. Create and review the invoice draft next." : "Invoice draft created. Review its totals below, then request invoice issue approval." });
      if (input.kind === "charge") { setChargeName(""); setDescription(""); setUnitRate(""); }
      await Promise.all([queryClient.invalidateQueries({ queryKey: ["job-charges", input.selectedJobId] }), queryClient.invalidateQueries({ queryKey: ["billing-job-detail", input.selectedJobId] }), queryClient.invalidateQueries({ queryKey: ["invoice-drafts"] }), queryClient.invalidateQueries({ queryKey: ["billing-job-options"] }), queryClient.invalidateQueries({ queryKey: ["jobs"] })]);
    },
    onError: (error) => setNotice({ kind: "error", text: apiErrorMessage(error, "Billing preparation failed. Review the selected job and charges, then retry.") }),
  });
  const run = (kind: "charge" | "ready" | "draft") => { setNotice(null); action.mutate({ kind, selectedJobId: jobId }); };
  const blocked = !jobId || !job || !terminal || !hasCustomer || !charges.length || currencies.size !== 1 || currencies.has("Unknown") || detailQ.isError || chargesQ.isError || chargesQ.isFetching || action.isPending;
  const amount = Math.round(Number(quantity) * Number(unitRate) * 100) / 100;
  return <details className="panel p-3">
    <summary className="cursor-pointer text-sm font-bold text-slate-900">Prepare invoice from a job</summary>
    <div className="mt-3 space-y-3">
      <p className="text-xs text-slate-500">Select the execution record, add agreed charges, and create a draft. A quotation total is not automatically a billable charge.</p>
      {!canReadJobs ? <p className="text-sm text-slate-600">Jobs read permission is required to select an execution record.</p> : <div className="grid gap-2 sm:grid-cols-2">
        <label className="text-xs font-semibold">Find job<input className="input mt-1 w-full" value={search} placeholder="Job code or customer" disabled={action.isPending} onChange={(event) => setSearch(event.target.value)} /></label>
        <label className="text-xs font-semibold">Job<select className="input mt-1 w-full" value={jobId} disabled={jobsQ.isLoading || action.isPending} onChange={(event) => { setJobId(event.target.value); setNotice(null); action.reset(); }}><option value="">Select job</option>{jobId && !(jobsQ.data?.rows ?? []).some((row) => String(row.id) === jobId) && <option value={jobId}>{String(job?.jobCode ?? job?.jobNumber ?? "Selected job")}</option>}{(jobsQ.data?.rows ?? []).map((row) => <option key={String(row.id)} value={String(row.id)}>{String(row.jobCode ?? row.jobNumber ?? row.id)} · {String(row.customerName ?? "No customer")} · {String(row.status ?? "Unknown")}</option>)}</select></label>
      </div>}
      {jobsQ.isError && <ErrorState message={apiErrorMessage(jobsQ.error, "Unable to load job options.")} onRetry={() => { void jobsQ.refetch(); }} />}
      {jobId && detailQ.isError && <ErrorState message={apiErrorMessage(detailQ.error, "Unable to load the selected job.")} onRetry={() => { void detailQ.refetch(); }} />}
      {notice && <p role={notice.kind === "error" ? "alert" : "status"} className={`rounded-lg border p-2 text-sm ${notice.kind === "error" ? "border-red-200 bg-red-50 text-red-700" : "border-emerald-200 bg-emerald-50 text-emerald-700"}`}>{notice.text}</p>}
      {jobId && <>
        <p className="text-sm text-slate-600">{prerequisite} <Link className="font-semibold text-teal-700 underline" to={`/jobs?jobId=${encodeURIComponent(jobId)}`}>Review job</Link> · <Link className="font-semibold text-teal-700 underline" to={`/proof-of-delivery?jobId=${encodeURIComponent(jobId)}`}>Review delivery proof</Link></p>
        {canReadCharges ? chargesQ.isLoading ? <LoadingState /> : chargesQ.isError ? <ErrorState message={apiErrorMessage(chargesQ.error, "Unable to load job charges.")} onRetry={() => { void chargesQ.refetch(); }} /> : charges.length ? <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="border-b bg-slate-50 text-left"><th className="p-2">Charge</th><th className="p-2">Quantity</th><th className="p-2">Unit rate</th><th className="p-2">Amount</th><th className="p-2">Status</th></tr></thead><tbody>{charges.map((charge) => <tr key={String(charge.id)} className="border-b"><td className="p-2">{String(charge.chargeName ?? charge.charge_name ?? "Charge")}</td><td className="p-2">{String(charge.quantity ?? "—")}</td><td className="p-2">{money(Number(charge.unitRate ?? charge.unit_rate ?? 0), currencyCode(charge.currency))}</td><td className="p-2">{money(Number(charge.amount ?? 0), currencyCode(charge.currency))}</td><td className="p-2">{String(charge.status ?? "pending")}</td></tr>)}</tbody></table></div> : <p className="text-xs text-slate-500">No recorded charges for this job.</p> : <p className="text-xs text-slate-500">Charge read permission is required to review billing prerequisites.</p>}
        {canCreateCharge && <form className="grid gap-2 rounded-lg border border-slate-200 p-3 sm:grid-cols-3" onSubmit={(event) => { event.preventDefault(); run("charge"); }}>
          <label className="text-xs font-semibold">Charge name<input className="input mt-1 w-full" required maxLength={160} value={chargeName} disabled={action.isPending} onChange={(event) => setChargeName(event.target.value)} placeholder="Agreed line haul" /></label>
          <label className="text-xs font-semibold sm:col-span-2">Description<input className="input mt-1 w-full" maxLength={2000} value={description} disabled={action.isPending} onChange={(event) => setDescription(event.target.value)} placeholder="Service and agreed pricing basis" /></label>
          <label className="text-xs font-semibold">Quantity<input className="input mt-1 w-full" type="number" min="0.01" step="0.01" required value={quantity} disabled={action.isPending} onChange={(event) => setQuantity(event.target.value)} /></label>
          <label className="text-xs font-semibold">Unit rate<input className="input mt-1 w-full" type="number" min="0.01" step="0.01" required value={unitRate} disabled={action.isPending} onChange={(event) => setUnitRate(event.target.value)} /></label>
          <label className="text-xs font-semibold">Currency<input className="input mt-1 w-full" maxLength={3} pattern="[A-Z]{3}" required value={currency} disabled={action.isPending} onChange={(event) => setCurrency(event.target.value.toUpperCase())} /></label>
          <div className="flex flex-wrap items-center justify-between gap-2 sm:col-span-3"><span className="text-sm">Charge total: <strong>{Number.isFinite(amount) ? money(amount, currencyCode(currency)) : "Enter quantity and rate"}</strong></span><button className="btn-secondary text-xs" type="submit" disabled={!job || detailQ.isError || action.isPending}>{action.isPending && action.variables?.kind === "charge" ? "Saving…" : "Add manual charge"}</button></div>
        </form>}
        <div className="flex flex-wrap items-center gap-2"><button className="btn-secondary text-xs" type="button" disabled={!canReady || blocked || String(job?.status).toLowerCase() === "ready_to_bill"} title={!canReady ? "Ready-to-bill permission is required" : prerequisite} onClick={() => run("ready")}>{action.isPending && action.variables?.kind === "ready" ? "Checking…" : "Mark ready to bill"}</button><button className="btn-primary text-xs" type="button" disabled={!canDraft || blocked} title={!canDraft ? "Invoice draft create permission is required" : prerequisite} onClick={() => run("draft")}>{action.isPending && action.variables?.kind === "draft" ? "Creating…" : "Create invoice draft"}</button></div>
      </>}
    </div>
  </details>;
}

function InvoiceApprovalReview({ drafts }: { drafts: AnyRecord[] }) {
  const hasPermission = useHasPermission();
  const { session } = useAuth();
  const queryClient = useQueryClient();
  const canReview = hasPermission("finance.invoice.issue");
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null);
  const approvalsQ = useQuery({ queryKey: ["invoice-approval-requests"], queryFn: financeOrderToCashApi.pendingApprovals, enabled: canReview });
  const decide = useMutation({
    mutationFn: ({ id, decision }: { id: number; decision: "approved" | "rejected" }) => financeOrderToCashApi.decideApproval(id, decision, (notes[String(id)] ?? "").trim()),
    onSuccess: async (_, input) => {
      setNotice({ error: false, text: input.decision === "approved" ? "Invoice approval recorded. Return to the draft and issue the invoice; approval alone does not create accounts receivable." : "Invoice request rejected. The draft remains unissued; review the reason with the invoice creator." });
      await Promise.all([queryClient.invalidateQueries({ queryKey: ["invoice-approval-requests"] }), queryClient.invalidateQueries({ queryKey: ["invoice-drafts"] })]);
    },
    onError: (error) => setNotice({ error: true, text: apiErrorMessage(error, "The approval decision could not be recorded.") }),
  });
  const pending = (approvalsQ.data ?? []).filter((request) => String(request.requestType ?? request.actionKey ?? "") === "finance.invoice.issue");
  return <section id="invoice-approvals" className="panel p-3" aria-labelledby="invoice-approvals-title">
    <div className="flex flex-wrap items-center justify-between gap-2"><h2 id="invoice-approvals-title" className="text-sm font-bold text-slate-900">Invoice approval review</h2>{canReview && <button type="button" className="btn-secondary text-xs" disabled={approvalsQ.isFetching} onClick={() => { void approvalsQ.refetch(); }}>{approvalsQ.isFetching ? "Refreshing…" : "Refresh approvals"}</button>}</div>
    <p className="mt-1 text-xs text-slate-500">A different authorized user reviews the requested invoice total. After approval, use Issue invoice on the draft. This does not send money or record payment.</p>
    {notice && <p role={notice.error ? "alert" : "status"} className={`mt-2 rounded-lg border p-2 text-sm ${notice.error ? "border-red-200 bg-red-50 text-red-700" : "border-emerald-200 bg-emerald-50 text-emerald-700"}`}>{notice.text}</p>}
    {!canReview ? <p className="mt-2 text-xs text-slate-600">Invoice issue permission is required to review requests.</p> : approvalsQ.isLoading ? <LoadingState /> : approvalsQ.isError ? <ErrorState message={apiErrorMessage(approvalsQ.error, "Unable to load invoice approval requests.")} onRetry={() => { void approvalsQ.refetch(); }} /> : pending.length === 0 ? <p className="mt-2 text-xs text-slate-500">No pending invoice issue requests.</p> : <div className="mt-2 divide-y divide-slate-200">{pending.map((request) => {
      const id = Number(request.id);
      const draft = drafts.find((item) => String(item.id) === String(request.entityId ?? request.resourceId));
      const requester = String(request.requestedByUserId ?? request.requestedByActorId ?? "");
      const hasRecordedRequester = requester !== "";
      const ownRequest = requester !== "" && requester === String(session?.user.id ?? session?.user.userId ?? "");
      const total = request.total ?? draft?.total;
      const code = currencyCode(request.currency ?? draft?.currency);
      const knownTotal = total != null && Number.isFinite(Number(total)) && code !== "Unknown";
      return <div className="py-3" key={id}><div className="flex flex-wrap items-start justify-between gap-2"><div><p className="text-sm font-semibold text-slate-900">{String(request.invoiceDraftNo ?? draft?.invoiceDraftNo ?? `Request #${id}`)} · {knownTotal ? money(Number(total), code) : "Invoice total unavailable"}</p><p className="text-xs text-slate-500">Request #{id} · Requested by {String(request.requestedByName ?? (requester || "unknown"))} · {request.requestedAt ? new Date(String(request.requestedAt)).toLocaleString() : "Time unavailable"}</p></div><span className="badge">Pending review</span></div>
        {draft?.jobId != null && <p className="mt-1 text-xs"><Link className="font-semibold text-teal-700 underline" to={`/jobs?jobId=${encodeURIComponent(String(draft.jobId))}`}>Review linked job and customer</Link> · <Link className="font-semibold text-teal-700 underline" to={`/proof-of-delivery?jobId=${encodeURIComponent(String(draft.jobId))}`}>Review delivery evidence</Link></p>}
        <label className="mt-2 block text-xs font-semibold">Review notes<input className="input mt-1 w-full" maxLength={2000} value={notes[String(id)] ?? ""} disabled={decide.isPending} onChange={(event) => setNotes((current) => ({ ...current, [String(id)]: event.target.value }))} placeholder="Confirm pricing, customer and delivery evidence, or explain rejection" /></label>
        {ownRequest && <p className="mt-2 text-xs font-semibold text-amber-700">You requested this invoice. Sign in as a different authorized reviewer to approve it.</p>}
        {!hasRecordedRequester && <p className="mt-2 text-xs font-semibold text-amber-700">This request lacks a recorded submitter. It cannot be independently reviewed; ask the invoice creator to resubmit after the attribution issue is corrected.</p>}
        {!knownTotal && <p className="mt-2 text-xs text-amber-700">Load and review the invoice total before approval.</p>}
        <div className="mt-2 flex gap-2"><button type="button" className="btn-primary text-xs" disabled={!hasRecordedRequester || ownRequest || !knownTotal || decide.isPending} onClick={() => { setNotice(null); decide.mutate({ id, decision: "approved" }); }}>{decide.isPending && decide.variables?.id === id && decide.variables.decision === "approved" ? "Approving…" : "Approve invoice issue"}</button><button type="button" className="btn-secondary text-xs" disabled={!hasRecordedRequester || ownRequest || decide.isPending || !(notes[String(id)] ?? "").trim()} title="Enter a reason to reject" onClick={() => { setNotice(null); decide.mutate({ id, decision: "rejected" }); }}>Reject request</button></div>
      </div>;
    })}</div>}
  </section>;
}

function InvoicesTab() {
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const canReadDrafts = hasPermission("finance.invoice_draft.read");
  const canIssue = hasPermission("finance.invoice.issue");
  const canRecordPayment = hasPermission("finance.invoice.payment.record");
  const canReadJobs = hasPermission("jobs:view") || hasPermission("shipments:view") || hasPermission("dispatch:view");
  const [paymentTarget, setPaymentTarget] = useState<PaymentTarget | null>(null);
  const [notice, setNotice] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const q = useQuery({ queryKey: ["issued-invoices"], queryFn: financialApi.invoices });
  const draftsQ = useQuery({
    queryKey: ["invoice-drafts"],
    queryFn: financeOrderToCashApi.invoiceDrafts,
    enabled: canReadDrafts,
  });
  // Reuse the same tenant/branch-scoped job choices loaded by the preparation control.
  // Finance-only users do not gain a new customer data read through this display.
  const billingJobsQ = useQuery({
    queryKey: ["billing-job-options", ""],
    queryFn: () => jobsApi.listPaged({ limit: 200, search: "" }),
    enabled: canReadJobs,
  });
  const issue = useMutation({
    mutationFn: ({ id, key }: { id: string; key: string }) => financeOrderToCashApi.issueInvoiceDraft(id, key),
    onSuccess: async (result) => {
      setNotice({
        kind: "success",
        text: result.approvalRequired
          ? `Invoice approval requested${result.approvalRequestId ? ` (request #${result.approvalRequestId})` : ""}. The draft remains unissued until a different authorized user approves it in Invoice approval review below.`
          : "Invoice issued successfully and moved into accounts receivable.",
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["invoice-drafts"] }),
        queryClient.invalidateQueries({ queryKey: ["issued-invoices"] }),
        queryClient.invalidateQueries({ queryKey: ["invoice-approval-requests"] }),
      ]);
    },
    onError: (error) => setNotice({ kind: "error", text: apiErrorMessage(error, "The invoice could not be issued. Review the draft and try again.") }),
  });
  const recordPayment = useMutation({
    mutationFn: ({ invoiceId, input }: { invoiceId: string; input: { amount: number; currency: string; paymentReference: string; paymentMethod: string } }) =>
      financeOrderToCashApi.recordInvoicePayment(invoiceId, input),
    onSuccess: async () => {
      setPaymentTarget(null);
      setNotice({ kind: "success", text: "Payment recorded. The invoice balance and collections ledger have been refreshed." });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["issued-invoices"] }),
        queryClient.invalidateQueries({ queryKey: ["payments"] }),
      ]);
    },
  });
  const rows = (q.data ?? []) as AnyRecord[];
  const drafts = (draftsQ.data ?? []) as AnyRecord[];
  const customerNames = new Map(
    (billingJobsQ.data?.rows ?? [])
      .filter((job) => (job.customerId ?? job.customer_id) != null && String(job.customerName ?? job.customer_name ?? "").trim())
      .map((job) => [String(job.customerId ?? job.customer_id), String(job.customerName ?? job.customer_name)] as const)
  );
  const displayRows: AnyRecord[] = rows.map((row) => {
    const recordedName = String(row.customerName ?? row.customer_name ?? "").trim();
    const scopedName = customerNames.get(String(row.customerId ?? row.customer_id ?? ""));
    return {
      ...row,
      customerName: (recordedName && !/^Customer\s+#\d+$/i.test(recordedName))
        ? recordedName
        : scopedName ?? "Customer name unavailable",
    } as AnyRecord;
  });
  const outstandingBalances = totalsByCurrency(rows, (r) => Number(r.balanceDue ?? 0));
  const overdue = rows.filter((r) => String(r.paymentStatus) === "Overdue").length;
  const totalValues = totalsByCurrency(rows, (r) => Number(r.total ?? 0));
  const paidCount = rows.filter((r) => String(r.paymentStatus) === "Paid").length;
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load invoices."} />;

  // DataTable renders a `status` column via the canonical StatusBadge and $-prefixed
  // strings as currency — so we pre-format amounts and expose a `status` column.
  const tableRows = displayRows.map((r) => ({
    "Invoice #": String(r.invoiceNumber ?? "—"),
    Customer: String(r.customerName ?? "—"),
    Total: money(Number(r.total ?? 0), currencyCode(r.currency)),
    Paid: money(Number(r.amountPaid ?? 0), currencyCode(r.currency)),
    "Balance Due": money(Number(r.balanceDue ?? 0), currencyCode(r.currency)),
    status: String(r.paymentStatus ?? "—"),
    "Due Date": String(r.dueDate || "—"),
    Aging: Number(r.agingDays) > 0 ? `${Number(r.agingDays)}d` : "—",
  }));

  return (
    <div className="flex flex-col gap-4">
      {notice && <div className={`rounded-lg border p-3 text-sm ${notice.kind === "error" ? "border-red-300 bg-red-50 text-red-700" : "border-emerald-300 bg-emerald-50 text-emerald-700"}`} role={notice.kind === "error" ? "alert" : "status"}>{notice.text}</div>}
      <div className="panel flex flex-wrap divide-x divide-slate-100" aria-label="Invoice summary">
        <KpiCard compact label="Total invoices" value={rows.length} />
        <KpiCard compact label="Overdue" value={overdue} status={overdue > 0 ? "Overdue" : "No recorded overdue invoices"} />
        <KpiCard compact label="Paid" value={paidCount} />
        {outstandingBalances.map(({ currency, total }) => <KpiCard compact key={`outstanding-${currency}`} label={`Outstanding (${currency})`} value={money(total, currency)} status="Review" />)}
      </div>
      <JobBillingPreparation />
      {canReadDrafts && (
        <section className="panel overflow-hidden p-0" aria-labelledby="invoice-drafts-title">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 px-4 py-3">
            <div>
              <h2 id="invoice-drafts-title" className="text-sm font-bold text-slate-900">Invoice drafts awaiting issue</h2>
              <p className="text-xs text-slate-500">Review draft totals before they enter accounts receivable.</p>
            </div>
            {!canIssue && <span className="text-xs text-slate-500">Read-only · issue permission required</span>}
          </div>
          {draftsQ.isLoading ? <div className="p-4"><LoadingState /></div> : draftsQ.isError ? (
            <div className="p-4"><ErrorState message={apiErrorMessage(draftsQ.error, "Unable to load invoice drafts.")} onRetry={() => { void draftsQ.refetch(); }} /></div>
          ) : drafts.length === 0 ? <div className="p-4"><EmptyState title="No invoice drafts awaiting issue" /></div> : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead><tr className="border-b border-slate-200 bg-slate-50">
                  {['Draft #', 'Customer', 'Status', 'Subtotal', 'Tax', 'Total', 'Action'].map((heading) => <th key={heading} className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">{heading}</th>)}
                </tr></thead>
                <tbody className="divide-y divide-slate-100">{drafts.map((draft) => {
                  const id = String(draft.id ?? "");
                  const status = String(draft.status ?? "draft");
                  const currency = currencyCode(draft.currency);
                  const eligible = !/issued|cancelled|void/i.test(status);
                  const customerName = String(draft.customerName ?? draft.customer_name ?? customerNames.get(String(draft.customerId ?? draft.customer_id ?? "")) ?? "").trim();
                  return <tr key={id}>
                    <td className="px-4 py-3 font-medium text-slate-900">{String(draft.invoiceDraftNo ?? id)}</td>
                    <td className="px-4 py-3 text-slate-700">{customerName || "Customer name unavailable"}</td>
                    <td className="px-4 py-3 text-slate-600">{status}</td>
                    <td className="px-4 py-3 text-slate-700">{money(Number(draft.subtotal ?? 0), currency)}</td>
                    <td className="px-4 py-3 text-slate-700">{money(Number(draft.taxTotal ?? 0), currency)}</td>
                    <td className="px-4 py-3 font-semibold text-slate-900">{money(Number(draft.total ?? 0), currency)}</td>
                    <td className="px-4 py-3"><button type="button" className="btn-primary whitespace-nowrap text-xs" disabled={!canIssue || !eligible || issue.isPending} title={!canIssue ? "Requires invoice issue permission" : !eligible ? "This draft cannot be issued in its current status" : "Issue this invoice"} onClick={() => { setNotice(null); issue.mutate({ id, key: crypto.randomUUID() }); }}>{issue.isPending && issue.variables?.id === id ? "Issuing…" : "Issue invoice"}</button></td>
                  </tr>;
                })}</tbody>
              </table>
            </div>
          )}
        </section>
      )}
      <InvoiceApprovalReview drafts={drafts} />
      {displayRows.length === 0 ? <EmptyState title="No invoices found" /> : (
        <DataTable
          rows={tableRows}
          columns={["Invoice #", "Customer", "Total", "Paid", "Balance Due", "status", "Due Date", "Aging"]}
        />
      )}
      {displayRows.some((row) => Number(row.balanceDue ?? 0) > 0) && (
        <section className="panel overflow-hidden p-0" aria-labelledby="collections-actions-title">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 px-4 py-3">
            <div><h2 id="collections-actions-title" className="text-sm font-bold text-slate-900">Collections actions</h2><p className="text-xs text-slate-500">Record confirmed payments against an issued invoice.</p></div>
            {!canRecordPayment && <span className="text-xs text-slate-500">Read-only · payment record permission required</span>}
          </div>
          <div className="divide-y divide-slate-100">{displayRows.filter((row) => Number(row.balanceDue ?? 0) > 0).map((row) => {
            const target = { id: String(row.id), invoiceNumber: String(row.invoiceNumber ?? row.id), customerName: String(row.customerName ?? "—"), balanceDue: Number(row.balanceDue), currency: currencyCode(row.currency) };
            return <div key={target.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3"><div><p className="font-medium text-slate-900">{target.invoiceNumber} · {target.customerName}</p><p className="text-xs text-slate-500">Outstanding {money(target.balanceDue, target.currency)}</p></div><button type="button" className="btn-secondary text-xs" disabled={!canRecordPayment} title={!canRecordPayment ? "Requires invoice payment record permission" : "Record a confirmed payment"} onClick={() => { setNotice(null); setPaymentTarget(target); }}>Record payment</button></div>;
          })}</div>
        </section>
      )}
      <details className="panel p-3">
        <summary className="cursor-pointer text-sm font-semibold text-slate-700">Issued value totals and invoice evidence</summary>
        <div className="mt-3 space-y-3 border-t border-slate-100 pt-3">
          <div className="panel flex flex-wrap divide-x divide-slate-100">
            {totalValues.map(({ currency, total }) => <KpiCard compact key={`total-${currency}`} label={`Total value (${currency})`} value={money(total, currency)} />)}
          </div>
          <div className="grid gap-3 md:grid-cols-3">
            <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">AR posture</p><p className="mt-1 text-sm font-semibold text-slate-900">{overdue > 0 ? `${overdue} overdue invoice${overdue === 1 ? "" : "s"}` : "No overdue invoices in the recorded ledger"}</p></div>
            <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Recorded collection status</p><p className="mt-1 text-sm font-semibold text-slate-900">{paidCount > 0 ? `${paidCount} invoice${paidCount === 1 ? "" : "s"} fully collected` : "No invoices collected yet"}</p></div>
            <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Persisted source</p><p className="mt-1 text-sm font-semibold text-slate-900">Calculated from tenant-scoped issued invoice records.</p></div>
          </div>
        </div>
      </details>
      {paymentTarget && <PaymentDialog target={paymentTarget} saving={recordPayment.isPending} serverError={recordPayment.isError ? apiErrorMessage(recordPayment.error, "The payment could not be recorded. Check the amount and invoice status, then try again.") : null} onClose={() => { if (!recordPayment.isPending) { recordPayment.reset(); setPaymentTarget(null); } }} onSubmit={(input) => recordPayment.mutate({ invoiceId: paymentTarget.id, input })} />}
    </div>
  );
}

function buildAgingByCurrency(rows: AnyRecord[]): AnyRecord[] {
  const groups = new Map<string, AnyRecord & { customersMap: Map<string, AnyRecord> }>();
  for (const row of rows) {
    const balance = Number(row.balanceDue ?? 0);
    if (!Number.isFinite(balance) || balance <= 0) continue;
    const currency = currencyCode(row.currency);
    const group = groups.get(currency) ?? { currency, current: 0, days1To30: 0, days31To60: 0, days61To90: 0, days90Plus: 0, totalOutstanding: 0, customersMap: new Map<string, AnyRecord>() };
    const customerName = String(row.customerName ?? "Unknown customer");
    const customerKey = String(row.customerId ?? customerName);
    const customer = group.customersMap.get(customerKey) ?? { customerName, currency, current: 0, days1To30: 0, days31To60: 0, days61To90: 0, days90Plus: 0, totalOutstanding: 0 };
    const age = Math.max(0, Number(row.agingDays ?? 0));
    const bucket = age <= 0 ? "current" : age <= 30 ? "days1To30" : age <= 60 ? "days31To60" : age <= 90 ? "days61To90" : "days90Plus";
    group[bucket] = Number(group[bucket] ?? 0) + balance;
    group.totalOutstanding = Number(group.totalOutstanding) + balance;
    customer[bucket] = Number(customer[bucket] ?? 0) + balance;
    customer.totalOutstanding = Number(customer.totalOutstanding) + balance;
    group.customersMap.set(customerKey, customer);
    groups.set(currency, group);
  }
  return [...groups.values()].map(({ customersMap, ...group }) => ({ ...group, customers: [...customersMap.values()] }));
}

function ArAgingTab() {
  const q = useQuery({ queryKey: ["issued-invoices"], queryFn: financialApi.invoices });
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load AR aging."} />;
  const groups = buildAgingByCurrency((q.data ?? []) as AnyRecord[]);
  const buckets: { label: string; key: string; status?: string }[] = [
    { label: "Current", key: "current" }, { label: "1–30 days", key: "days1To30" },
    { label: "31–60 days", key: "days31To60", status: "Review" }, { label: "61–90 days", key: "days61To90", status: "Review" },
    { label: "90+ days", key: "days90Plus", status: "Overdue" },
  ];
  const custRows = groups.flatMap((group) => ((group.customers ?? []) as AnyRecord[]).map((customer) => ({
    Currency: String(group.currency), Customer: String(customer.customerName ?? "—"),
    Current: money(Number(customer.current ?? 0), String(group.currency)), "1–30": money(Number(customer.days1To30 ?? 0), String(group.currency)),
    "31–60": money(Number(customer.days31To60 ?? 0), String(group.currency)), "61–90": money(Number(customer.days61To90 ?? 0), String(group.currency)),
    "90+": money(Number(customer.days90Plus ?? 0), String(group.currency)), "Total Outstanding": money(Number(customer.totalOutstanding ?? 0), String(group.currency)),
  })));

  return <div className="flex flex-col gap-4">
    <div className="panel flex flex-wrap divide-x divide-slate-100" aria-label="Receivables aging summary by currency">
      {groups.map((group) => <KpiCard compact key={`${group.currency}-summary`} label={`Outstanding (${group.currency})`} value={money(Number(group.totalOutstanding ?? 0), String(group.currency))} status={Number(group.days90Plus ?? 0) > 0 ? "Overdue" : undefined} trend={`90+ days: ${money(Number(group.days90Plus ?? 0), String(group.currency))}`} />)}
    </div>
    {custRows.length === 0 ? <EmptyState title="No outstanding receivables" /> : <DataTable rows={custRows} columns={["Currency", "Customer", "Current", "1–30", "31–60", "61–90", "90+", "Total Outstanding"]} />}
    {groups.length > 0 && <details className="panel p-3">
      <summary className="cursor-pointer text-sm font-semibold text-slate-700">Detailed aging buckets and calculation basis</summary>
      <div className="mt-3 space-y-3 border-t border-slate-100 pt-3">
        <div className="panel flex flex-wrap divide-x divide-slate-100">
          {groups.flatMap((group) => buckets.map((bucket) => <KpiCard compact key={`${group.currency}-${bucket.key}`} label={`${bucket.label} (${group.currency})`} value={money(Number(group[bucket.key] ?? 0), String(group.currency))} status={bucket.status} />))}
        </div>
        <div className="grid gap-3 md:grid-cols-3">
          <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Aging basis</p><p className="mt-1 text-sm font-semibold text-slate-900">Outstanding balance bucketed by days past due and separated by currency.</p></div>
          <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Collections risk</p><div className="mt-1 space-y-1 text-sm font-semibold text-slate-900">{groups.map((group) => <p key={String(group.currency)}>{Number(group.days90Plus ?? 0) > 0 ? `${money(Number(group.days90Plus), String(group.currency))} is 90+ days overdue` : `No ${String(group.currency)} balances past 90 days`}</p>)}</div></div>
          <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Persisted source</p><p className="mt-1 text-sm font-semibold text-slate-900">Calculated from tenant-scoped issued invoices; currencies are never combined.</p></div>
        </div>
      </div>
    </details>}
  </div>;
}

function PaymentsTab() {
  const q = useQuery({ queryKey: ["payments"], queryFn: financialApi.payments });
  const rows = (q.data ?? []) as AnyRecord[];
  const recorded = totalsByCurrency(rows, (r) => Number(r.amount ?? 0));
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load payments."} />;
  return (
    <div className="flex flex-col gap-4">
      <div className="panel flex flex-wrap divide-x divide-slate-100" aria-label="Payment summary">
        <KpiCard compact label="Total payments" value={rows.length} />
        {recorded.map(({ currency, total }) => <KpiCard compact key={currency} label={`Recorded amount (${currency})`} value={money(total, currency)} />)}
      </div>
      {rows.length === 0 ? <EmptyState title="No payments found" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Payment Reference", "Customer", "Invoice", "Amount", "Currency", "Method", "Recorded At", "Ledger Status", "Provider Settlement"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.paymentNumber ?? "--")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(r.customerName ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.invoiceRef ?? "—")}</td>
                    <td className="px-4 py-3 font-semibold text-slate-900">{money(Number(r.amount ?? 0), currencyCode(r.currency))}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{currencyCode(r.currency)}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.paymentMethod ?? "—")}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-xs text-slate-500">{utcDateTime(r.paymentDate)}</td>
                    <td className="px-4 py-3"><PaymentStatusBadge status={String(r.status ?? "Pending")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{r.providerSettlementClaim === true ? "Verified" : "Not evidenced"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

function ProfitabilityTab() {
  const q = useQuery({ queryKey: ["profitability"], queryFn: financialApi.profitability });
  const rows = (q.data ?? []) as AnyRecord[];
  const totalRev = totalsByCurrency(rows, (r) => Number(r.revenueEstimate ?? 0));
  const totalCost = totalsByCurrency(rows, (r) => Number(r.totalCost ?? 0));
  const marginRows = rows.filter((r) => r.grossMargin != null && r.grossMarginPercent != null);
  const avgMarginPct = marginRows.length > 0 ? marginRows.reduce((s, r) => s + Number(r.grossMarginPercent), 0) / marginRows.length : null;
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load profitability data."} />;
  const chartData = marginRows.slice(0, 8).map((r) => ({
    name: String(r.entityName ?? "").split(" ")[0],
    margin: Number(r.grossMarginPercent ?? 0),
    revenue: Math.round(Number(r.revenueEstimate ?? 0) / 1000),
  }));
  return (
    <div className="flex flex-col gap-4">
      <div className="panel flex flex-wrap divide-x divide-slate-100" aria-label="Profitability summary">
        {totalRev.map(({ currency, total }) => <KpiCard compact key={`revenue-${currency}`} label={`Revenue (${currency})`} value={money(total, currency)} />)}
        {totalCost.map(({ currency, total }) => <KpiCard compact key={`cost-${currency}`} label={`Cost (${currency})`} value={money(total, currency)} />)}
        <KpiCard compact label="Customers with cost evidence" value={`${marginRows.length} / ${rows.length}`} />
        {avgMarginPct == null ? null : <KpiCard compact label="Avg margin % (covered)" value={`${avgMarginPct.toFixed(1)}%`} />}
      </div>
      {rows.length === 0 ? <EmptyState title="No profitability data" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Customer", "Invoiced Total", "Recorded Approved Costs", "Calculated Margin", "Currency", "Margin %", "Evidence"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.entityName ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700">{money(Number(r.revenueEstimate ?? 0), currencyCode(r.currency))}</td>
                    <td className="px-4 py-3 text-slate-600">{money(Number(r.totalCost ?? 0), currencyCode(r.currency))}</td>
                    <td className="px-4 py-3 font-semibold text-teal-700">{r.grossMargin == null ? "Unavailable" : money(Number(r.grossMargin), currencyCode(r.currency))}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{currencyCode(r.currency)}</td>
                    <td className="px-4 py-3">{r.grossMarginPercent == null ? "—" : <MarginBadge pct={Number(r.grossMarginPercent)} />}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{Number(r.costRecordCount ?? 0) > 0 ? `${Number(r.invoiceCount ?? 0)} invoices · ${Number(r.costRecordCount)} approved costs` : "Cost evidence unavailable"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {(chartData.length > 0 || rows.length > 0) && <details className="panel p-3">
        <summary className="cursor-pointer text-sm font-semibold text-slate-700">Margin analysis and evidence policy</summary>
        <div className="mt-3 space-y-3 border-t border-slate-100 pt-3">
          {chartData.length > 0 && <div>
            <p className="mb-3 text-sm font-semibold text-slate-700">Margin % by customer</p>
            <ResponsiveContainer width="100%" height={180}>
              <BarChart data={chartData} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
                <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                <YAxis unit="%" tick={{ fontSize: 11 }} />
                <Tooltip formatter={(val) => [`${String(val)}%`, "Margin"]} />
                <Bar dataKey="margin" fill={chart.teal600} radius={[3, 3, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>}
          <div className="grid gap-3 md:grid-cols-3">
            <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Revenue evidence</p><p className="mt-1 text-sm font-semibold text-slate-900">Revenue uses persisted issued invoice totals after recorded credits.</p></div>
            <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Cost evidence</p><p className="mt-1 text-sm font-semibold text-slate-900">Costs include approved customer-linked expense records in the same currency.</p></div>
            <div><p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Data policy</p><p className="mt-1 text-sm font-semibold text-slate-900">Margin stays unavailable where allocated cost evidence is missing.</p></div>
          </div>
        </div>
      </details>}
    </div>
  );
}

// ── Route → tab ───────────────────────────────────────────────────────────────

const ROUTE_TAB: Record<string, Tab> = {
  "/invoices": "invoices",
  "/payments": "payments",
  "/profitability": "profitability",
  "/ar-aging": "ar-aging",
};

const TAB_ROUTE: Record<Tab, string> = {
  invoices: "/invoices",
  "ar-aging": "/ar-aging",
  payments: "/payments",
  profitability: "/profitability",
};

type Tab = "invoices" | "ar-aging" | "payments" | "profitability";

const TABS: { key: Tab; label: string }[] = [
  { key: "invoices",      label: "Invoices" },
  { key: "ar-aging",      label: "AR Aging" },
  { key: "payments",      label: "Payments" },
  { key: "profitability", label: "Profitability" },
];

const TITLES: Record<Tab, string> = {
  invoices:      "Invoices",
  "ar-aging":    "Accounts Receivable Aging",
  payments:      "Payments",
  profitability: "Profitability",
};

const DESCRIPTIONS: Record<Tab, string> = {
  invoices:      "Invoice lifecycle — issued, paid, overdue with balance and aging tracking",
  "ar-aging":    "Outstanding receivables bucketed by days past due — current / 1-30 / 31-60 / 61-90 / 90+",
  payments:      "Payments recorded against issued invoices; provider settlement is shown only when evidenced",
  profitability: "Issued invoice totals against approved customer-linked costs, with incomplete evidence kept visible",
};

// ── Main page ─────────────────────────────────────────────────────────────────

export function FinancialAnalyticsPage() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const tab = ROUTE_TAB[pathname] ?? "invoices";

  const exportFns: Record<Tab, () => void> = {
    invoices: async () => exportCsv("invoices", await loadInvoiceRows()),
    "ar-aging": async () => {
      const groups = buildAgingByCurrency(await loadInvoiceRows());
      const rows = groups.flatMap((group) => ((group.customers ?? []) as AnyRecord[]).map((customer) => ({
        ...customer,
        currency: group.currency,
      })));
      exportCsv("ar-aging", rows);
    },
    payments: async () => exportCsv("payments", await loadPaymentRows()),
    profitability: async () => exportCsv("profitability", await loadProfitabilityRows()),
  };

  return (
    <div className="page-stack min-w-0">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{TITLES[tab]}</h1>
          <p className="text-sm text-slate-500 mt-0.5">{DESCRIPTIONS[tab]}</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={() => void exportFns[tab]()}>Export CSV</button>
      </div>

      <nav className="panel flex gap-1 overflow-x-auto p-1.5" aria-label="Financial analytics sections">
        {TABS.map((t) => (
          <button key={t.key} type="button" onClick={() => navigate(TAB_ROUTE[t.key])}
            aria-current={tab === t.key ? "page" : undefined}
            className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
              tab === t.key ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"
            }`}>{t.label}</button>
        ))}
      </nav>

      {tab === "invoices"      && <InvoicesTab />}
      {tab === "ar-aging"      && <ArAgingTab />}
      {tab === "payments"      && <PaymentsTab />}
      {tab === "profitability" && <ProfitabilityTab />}
    </div>
  );
}
