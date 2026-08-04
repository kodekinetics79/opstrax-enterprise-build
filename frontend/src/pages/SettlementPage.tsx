import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Wallet, Eye, CheckCircle2, BadgeCheck, DollarSign, RefreshCw } from "lucide-react";
import { settlementApi } from "@/services/settlementApi";
import { ErrorState, LoadingState, PageHeader } from "@/components/ui";
import { Field, Table, money } from "@/pages/BillingConsolidationPage";
import type { AnyRecord } from "@/types";

// Driver pay (settlement) — "figure out what a driver is owed for the loads they delivered in a period,"
// then approve and pay. Preview first; plain language.
export function SettlementPage() {
  const qc = useQueryClient();
  const [driverId, setDriverId] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [result, setResult] = useState<AnyRecord | null>(null);

  const listQ = useQuery({ queryKey: ["settlements"], queryFn: () => settlementApi.list() });
  const apQ = useQuery({ queryKey: ["ap-summary"], queryFn: () => settlementApi.apSummary() });
  const refresh = () => { void listQ.refetch(); void apQ.refetch(); };

  const gen = useMutation({
    mutationFn: (mode: "preview" | "commit") => settlementApi.generate({ payeeType: "driver", payeeId: Number(driverId), periodStart: start, periodEnd: end, mode }),
    onSuccess: (r) => { setResult(r as AnyRecord); if ((r as AnyRecord).generated) refresh(); },
  });
  const approve = useMutation({ mutationFn: (id: number) => settlementApi.approve(id), onSuccess: refresh });
  const pay = useMutation({ mutationFn: (v: { id: number; amount: number }) => settlementApi.pay(v.id, { amount: v.amount, method: "manual", idempotencyKey: `pay-${v.id}-${v.amount}` }), onSuccess: refresh });

  const ready = driverId && start && end;
  const ap = apQ.data as AnyRecord | undefined;

  return (
    <div className="space-y-6">
      <PageHeader title="Driver Pay" description="Work out what a driver is owed for the loads they delivered, then approve and pay." />

      {/* AP summary — one glance at what's owed */}
      {ap ? (
        <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
          <Kpi label="Total pay" value={money(ap.totalPay)} />
          <Kpi label="Paid" value={money(ap.totalPaid)} />
          <Kpi label="Outstanding" value={money(ap.outstanding)} tone="warn" />
          <Kpi label="Statements" value={String(ap.statementCount ?? 0)} />
        </div>
      ) : null}

      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="Driver" hint="The driver to pay"><input value={driverId} onChange={(e) => setDriverId(e.target.value)} placeholder="Driver ID" className={inputCls} /></Field>
          <Field label="From"><input type="date" value={start} onChange={(e) => setStart(e.target.value)} className={inputCls} /></Field>
          <Field label="To"><input type="date" value={end} onChange={(e) => setEnd(e.target.value)} className={inputCls} /></Field>
        </div>
        <div className="mt-4 flex items-center gap-3">
          <button disabled={!ready || gen.isPending} onClick={() => gen.mutate("preview")} className={btnGhost}><Eye className="h-4 w-4" /> Preview</button>
          <button disabled={!ready || gen.isPending} onClick={() => gen.mutate("commit")} className={btnPrimary}><CheckCircle2 className="h-4 w-4" /> Create statement</button>
          <span className="text-xs text-slate-400">Preview shows the total and the loads — nothing is created until you click Create.</span>
        </div>
        {result ? (
          <div className={`mt-4 rounded-lg border p-4 ${result.generated ? "border-emerald-200 bg-emerald-50" : "border-slate-200 bg-slate-50"}`}>
            {result.generated ? <p className="text-sm text-emerald-800">Statement created · total <b>{money(result.total)}</b> across {String((result.lines as unknown[] | undefined)?.length ?? 0)} load(s).</p>
              : result.status === "preview" ? <p className="text-sm text-slate-700">Preview: <b>{money(result.total)}</b> for {String((result.lines as unknown[] | undefined)?.length ?? 0)} load(s). Click <b>Create statement</b> to proceed.</p>
              : <p className="text-sm text-slate-600">Not generated: <b>{reason(String(result.reason))}</b>.</p>}
          </div>
        ) : null}
      </section>

      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-slate-500"><Wallet className="h-4 w-4" /> Pay statements</h2>
          <button onClick={refresh} className="text-xs text-slate-500 hover:text-slate-700"><RefreshCw className="mr-1 inline h-3.5 w-3.5" />Refresh</button>
        </div>
        {listQ.isLoading ? <LoadingState /> : listQ.isError ? <ErrorState message="Could not load statements." onRetry={() => { void listQ.refetch(); }} /> : (() => {
          const rows = (listQ.data as AnyRecord[]) ?? [];
          if (rows.length === 0) return <p className="py-3 text-sm text-slate-400">No pay statements yet.</p>;
          return (
            <div className="overflow-x-auto rounded-lg border border-slate-100">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-left text-xs uppercase text-slate-400"><tr><th className="p-2">Statement</th><th className="p-2">Driver</th><th className="p-2">Total</th><th className="p-2">Paid</th><th className="p-2">Status</th><th className="p-2">Action</th></tr></thead>
                <tbody>
                  {rows.map((r) => {
                    const id = Number(r.id); const status = String(r.status); const total = Number(r.total); const paid = Number(r.amountPaid ?? 0);
                    return (
                      <tr key={id} className="border-t border-slate-100">
                        <td className="p-2 font-medium text-slate-700">{String(r.statementNo)}</td>
                        <td className="p-2">{String(r.payeeId)}</td>
                        <td className="p-2">{money(total)}</td>
                        <td className="p-2">{money(paid)}</td>
                        <td className="p-2"><Pill s={status} /></td>
                        <td className="p-2">
                          {status === "draft" ? <button onClick={() => approve.mutate(id)} disabled={approve.isPending} className="inline-flex items-center gap-1 text-emerald-700 hover:underline"><BadgeCheck className="h-4 w-4" />Approve</button>
                            : status === "approved" ? <button onClick={() => pay.mutate({ id, amount: total - paid })} disabled={pay.isPending} className="inline-flex items-center gap-1 text-blue-700 hover:underline"><DollarSign className="h-4 w-4" />Pay {money(total - paid)}</button>
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
      </section>
    </div>
  );
}

const inputCls = "w-full rounded-lg border border-slate-200 px-3 py-1.5 text-sm focus:border-blue-400 focus:outline-none";
const btnPrimary = "inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50";
const btnGhost = "inline-flex items-center gap-2 rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-50";

function Kpi({ label, value, tone }: { label: string; value: string; tone?: "warn" }) {
  return <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm"><div className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</div><div className={`mt-1 text-2xl font-semibold ${tone === "warn" ? "text-orange-600" : "text-slate-800"}`}>{value}</div></div>;
}
function Pill({ s }: { s: string }) {
  const m: Record<string, string> = { draft: "bg-slate-100 text-slate-600", approved: "bg-blue-100 text-blue-700", paid: "bg-emerald-100 text-emerald-700" };
  return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${m[s] ?? "bg-slate-100 text-slate-600"}`}>{s}</span>;
}
function reason(r: string): string {
  return { no_pay_agreement: "this driver has no pay agreement set up", no_delivered_loads: "no delivered loads in this period", "basis_unsupported_phase1:percent": "percent-based pay isn't supported yet", invalid_period: "the date range is invalid", statement_locked: "an approved/paid statement already exists" }[r] ?? r;
}
