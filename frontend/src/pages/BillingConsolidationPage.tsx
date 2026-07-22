import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { FileStack, Eye, CheckCircle2, RefreshCw } from "lucide-react";
import { billingApi } from "@/services/billingApi";
import { ErrorState, LoadingState, PageHeader } from "@/components/ui";
import type { AnyRecord } from "@/types";

// Billing consolidation — a task-oriented page: "combine a customer's delivered loads into invoices for
// a date range." Preview first (nothing is created), then create. Deliberately simple.
export function BillingConsolidationPage() {
  const [customerId, setCustomerId] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [result, setResult] = useState<AnyRecord | null>(null);

  const runsQ = useQuery({ queryKey: ["billing-runs"], queryFn: () => billingApi.runs() });

  const run = useMutation({
    mutationFn: (mode: "preview" | "commit") => billingApi.consolidate({ customerId: Number(customerId), periodStart: start, periodEnd: end, mode }),
    onSuccess: (r) => { setResult(r as AnyRecord); if ((r as AnyRecord).generated) void runsQ.refetch(); },
  });

  const ready = customerId && start && end;

  return (
    <div className="space-y-6">
      <PageHeader title="Consolidate Invoices" description="Combine a customer's delivered loads into one or more invoices for a date range." />

      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="Customer" hint="The customer to bill">
            <input value={customerId} onChange={(e) => setCustomerId(e.target.value)} placeholder="Customer ID" className={inputCls} />
          </Field>
          <Field label="From" hint="Loads delivered on/after"><input type="date" value={start} onChange={(e) => setStart(e.target.value)} className={inputCls} /></Field>
          <Field label="To" hint="Loads delivered on/before"><input type="date" value={end} onChange={(e) => setEnd(e.target.value)} className={inputCls} /></Field>
        </div>
        <div className="mt-4 flex items-center gap-3">
          <button disabled={!ready || run.isPending} onClick={() => run.mutate("preview")} className={btnGhost}>
            <Eye className="h-4 w-4" /> Preview
          </button>
          <button disabled={!ready || run.isPending} onClick={() => run.mutate("commit")} className={btnPrimary}>
            <CheckCircle2 className="h-4 w-4" /> Create invoices
          </button>
          <span className="text-xs text-slate-400">Preview shows what would be billed — nothing is created until you click Create.</span>
        </div>

        {result ? (
          <div className={`mt-4 rounded-lg border p-4 ${result.generated ? "border-emerald-200 bg-emerald-50" : "border-slate-200 bg-slate-50"}`}>
            {result.generated ? (
              <p className="text-sm text-emerald-800"><b>{String(result.draftCount)}</b> invoice draft(s) created · subtotal <b>{money(result.subtotal)}</b> across {String(result.groupCount)} group(s).</p>
            ) : result.reason === "preview" ? (
              <p className="text-sm text-slate-700">Preview: <b>{money(result.subtotal)}</b> would be billed across {String(result.groupCount)} group(s), {String((result.groups as unknown[] | undefined)?.length ?? 0)} line group(s). Click <b>Create invoices</b> to proceed.</p>
            ) : (
              <p className="text-sm text-slate-600">Nothing to bill: <b>{reason(String(result.reason))}</b>.</p>
            )}
          </div>
        ) : null}
      </section>

      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-slate-500"><FileStack className="h-4 w-4" /> Recent consolidation runs</h2>
          <button onClick={() => void runsQ.refetch()} className="text-xs text-slate-500 hover:text-slate-700"><RefreshCw className="mr-1 inline h-3.5 w-3.5" />Refresh</button>
        </div>
        {runsQ.isLoading ? <LoadingState /> : runsQ.isError ? <ErrorState message="Could not load runs." onRetry={() => { void runsQ.refetch(); }} /> : (
          <Table rows={(runsQ.data as AnyRecord[]) ?? []} cols={[
            ["Invoice #", (r) => String(r.allocatedInvoiceNo ?? "—")],
            ["Customer", (r) => String(r.customerId)],
            ["Charges", (r) => String(r.chargeCount)],
            ["Subtotal", (r) => money(r.subtotal)],
            ["Status", (r) => String(r.status)],
          ]} empty="No consolidation runs yet." />
        )}
      </section>
    </div>
  );
}

// ── shared friendly bits (kept local + tiny on purpose) ──
const inputCls = "w-full rounded-lg border border-slate-200 px-3 py-1.5 text-sm focus:border-blue-400 focus:outline-none";
const btnPrimary = "inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50";
const btnGhost = "inline-flex items-center gap-2 rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-50";

export function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return <label className="block"><span className="mb-1 block text-sm font-medium text-slate-700">{label}</span>{children}{hint ? <span className="mt-1 block text-xs text-slate-400">{hint}</span> : null}</label>;
}
export function Table({ rows, cols, empty }: { rows: AnyRecord[]; cols: [string, (r: AnyRecord) => string][]; empty: string }) {
  if (rows.length === 0) return <p className="py-3 text-sm text-slate-400">{empty}</p>;
  return (
    <div className="overflow-x-auto rounded-lg border border-slate-100">
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-left text-xs uppercase text-slate-400"><tr>{cols.map(([h]) => <th key={h} className="p-2">{h}</th>)}</tr></thead>
        <tbody>{rows.map((r, i) => <tr key={i} className="border-t border-slate-100">{cols.map(([h, f]) => <td key={h} className="p-2 text-slate-700">{f(r)}</td>)}</tr>)}</tbody>
      </table>
    </div>
  );
}
export function money(v: unknown): string { const n = Number(v); return Number.isFinite(n) ? `$${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` : "—"; }
function reason(r: string): string {
  return { no_billable_charges: "no unbilled delivered charges in this period", invalid_period: "the date range is invalid", all_locked: "the invoices already exist and are issued" }[r] ?? r;
}
