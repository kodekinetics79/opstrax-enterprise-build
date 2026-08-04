import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarCheck, Lock, PlayCircle, RefreshCw } from "lucide-react";
import { revrecApi } from "@/services/revrecApi";
import { ErrorState, LoadingState, PageHeader } from "@/components/ui";
import { Table, money } from "@/pages/BillingConsolidationPage";
import type { AnyRecord } from "@/types";

// Revenue recognition — plain language: "how much revenue we've recognized, and closing accounting
// periods." Recognition happens automatically when invoices issue; this page is for review + period close.
export function RevenueRecognitionPage() {
  const qc = useQueryClient();
  const periodsQ = useQuery({ queryKey: ["revrec-periods"], queryFn: () => revrecApi.periods() });
  const entriesQ = useQuery({ queryKey: ["revrec-entries"], queryFn: () => revrecApi.entries() });
  const summaryQ = useQuery({ queryKey: ["revrec-summary"], queryFn: () => revrecApi.summary() });
  const refresh = () => { void periodsQ.refetch(); void entriesQ.refetch(); void summaryQ.refetch(); };

  const close = useMutation({ mutationFn: (code: string) => revrecApi.closePeriod(code), onSuccess: refresh });
  const backfill = useMutation({ mutationFn: () => revrecApi.backfill(), onSuccess: refresh });

  const today = new Date().toISOString().slice(0, 10);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Revenue Recognition"
        description="Revenue is recognized automatically when an invoice is issued. Review it here and close accounting periods."
      />

      {/* Recognized-revenue summary per currency */}
      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-slate-500">Recognized revenue (year to date)</h2>
        {summaryQ.isLoading ? <LoadingState /> : (
          <div className="flex flex-wrap gap-4">
            {((summaryQ.data as AnyRecord[]) ?? []).length === 0 ? <p className="text-sm text-slate-400">No revenue recognized yet. If you have historical invoices, use “Recognize historical invoices” below.</p> :
              ((summaryQ.data as AnyRecord[]) ?? []).map((s, i) => (
                <div key={i} className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
                  <div className="text-xs font-medium uppercase tracking-wide text-slate-400">{String(s.functionalCurrency)}</div>
                  <div className="mt-1 text-2xl font-semibold text-slate-800">{money(s.recognizedFunctional)}</div>
                  <div className="text-xs text-slate-400">{String(s.entryCount)} entries</div>
                </div>
              ))}
          </div>
        )}
        <div className="mt-4 flex items-center gap-3">
          <button onClick={() => backfill.mutate()} disabled={backfill.isPending} className="inline-flex items-center gap-2 rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-50">
            <PlayCircle className="h-4 w-4" /> Recognize historical invoices
          </button>
          <span className="text-xs text-slate-400">One-time sweep for invoices issued before recognition was on. Run this <b>before</b> closing any period.</span>
        </div>
      </section>

      {/* Fiscal periods with close */}
      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-slate-500"><CalendarCheck className="h-4 w-4" /> Accounting periods</h2>
          <button onClick={refresh} className="text-xs text-slate-500 hover:text-slate-700"><RefreshCw className="mr-1 inline h-3.5 w-3.5" />Refresh</button>
        </div>
        {periodsQ.isLoading ? <LoadingState /> : periodsQ.isError ? <ErrorState message="Could not load periods." onRetry={() => { void periodsQ.refetch(); }} /> : (() => {
          const rows = (periodsQ.data as AnyRecord[]) ?? [];
          if (rows.length === 0) return <p className="py-3 text-sm text-slate-400">No accounting periods yet — they are created automatically as revenue is recognized.</p>;
          return (
            <div className="overflow-x-auto rounded-lg border border-slate-100">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-left text-xs uppercase text-slate-400"><tr><th className="p-2">Period</th><th className="p-2">Dates</th><th className="p-2">Recognized</th><th className="p-2">Status</th><th className="p-2">Action</th></tr></thead>
                <tbody>
                  {rows.map((r) => {
                    const code = String(r.periodCode); const status = String(r.status);
                    const canClose = status === "open" && String(r.periodEnd).slice(0, 10) < today;
                    return (
                      <tr key={code} className="border-t border-slate-100">
                        <td className="p-2 font-medium text-slate-700">{code}</td>
                        <td className="p-2 text-slate-500">{String(r.periodStart).slice(0, 10)} → {String(r.periodEnd).slice(0, 10)}</td>
                        <td className="p-2">{money(r.recognizedTotalFunctional)}</td>
                        <td className="p-2"><span className={`rounded-full px-2 py-0.5 text-xs font-medium ${status === "closed" ? "bg-slate-200 text-slate-600" : "bg-emerald-100 text-emerald-700"}`}>{status}</span></td>
                        <td className="p-2">
                          {canClose ? <button onClick={() => close.mutate(code)} disabled={close.isPending} className="inline-flex items-center gap-1 text-slate-700 hover:underline"><Lock className="h-4 w-4" />Close</button>
                            : status === "open" ? <span className="text-xs text-slate-400">current/future period</span>
                            : <span className="text-slate-400">—</span>}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          );
        })()}
        <p className="mt-2 text-xs text-slate-400">Closing a period freezes its entries. You can only close a period that has fully ended; corrections after close are posted as reversing entries in the open period.</p>
      </section>

      {/* Recent entries */}
      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-slate-500">Recent recognition entries</h2>
        {entriesQ.isLoading ? <LoadingState /> : (
          <Table rows={(entriesQ.data as AnyRecord[]) ?? []} cols={[
            ["Type", (r) => String(r.entryType)],
            ["Date", (r) => String(r.recognitionDate).slice(0, 10)],
            ["Amount", (r) => money(r.amountFunctional)],
            ["Status", (r) => String(r.status)],
          ]} empty="No recognition entries yet." />
        )}
      </section>
    </div>
  );
}
