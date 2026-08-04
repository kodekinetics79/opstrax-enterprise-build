import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronRight, Coins, DollarSign, Info, Wallet, X, XCircle } from "lucide-react";
import { driverEarningsApi } from "@/services/driverEarningsApi";
import type { AnyRecord } from "@/types";

// Driver self-service earnings. A single mobile scroll: what you're earning this period (live
// estimate), the money you've been approved/paid, and — the differentiator — your share of the
// detention we collected for you. Read-only; pure-live (a failed call shows an honest error, never
// fabricated pay). Employer economics (customer charge, internal share %) never reach this screen.

function money(amount: unknown, currency = "USD"): string {
  const n = Number(amount ?? 0);
  try {
    return new Intl.NumberFormat(undefined, { style: "currency", currency, maximumFractionDigits: 2 }).format(n);
  } catch {
    return `${currency} ${n.toFixed(2)}`;
  }
}

function fmtDate(v: unknown): string {
  if (v == null) return "—";
  const d = new Date(String(v));
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

export function DriverEarningsPage() {
  const [openStatementId, setOpenStatementId] = useState<number | string | null>(null);
  const { data, isLoading, isError, error } = useQuery<AnyRecord>({
    queryKey: ["driver", "earnings"],
    queryFn: driverEarningsApi.earnings,
    refetchInterval: 120_000,
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <Wallet className="h-12 w-12 animate-pulse text-slate-300" />
        <p className="text-sm text-slate-500">Loading your pay…</p>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12 text-center">
        <XCircle className="h-10 w-10 text-red-400" />
        <p className="text-sm font-medium text-red-700">{(error as Error)?.message ?? "Could not load your earnings."}</p>
        <p className="text-xs text-slate-400">Pull down to retry once you're back online.</p>
      </div>
    );
  }

  const d = data ?? {};
  const currency = String(d["currency"] ?? "USD");
  const open = (d["openPeriod"] as AnyRecord) ?? {};
  const ytd = (d["ytd"] as AnyRecord) ?? {};
  const lastPayment = d["lastPayment"] as AnyRecord | null;
  const statements = (d["statements"] as AnyRecord[]) ?? [];
  const policyEnabled = Boolean(d["detentionPolicyEnabled"]);
  const openAvailable = Boolean(open["available"]);
  const openDetention = Number(open["detentionPay"] ?? 0);
  const openEvents = Number(open["detentionEventCount"] ?? 0);

  return (
    <div className="p-4 space-y-4 pb-10">
      {/* Period header */}
      <div className="pt-2">
        <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Your Pay</p>
        <div className="mt-1 flex items-center justify-between">
          <h1 className="text-xl font-bold text-slate-900">This period so far</h1>
          <span className="rounded-full bg-slate-100 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wide text-slate-500">
            Preview
          </span>
        </div>
        <p className="text-xs text-slate-400">
          {fmtDate(open["periodStart"])} – {fmtDate(open["periodEnd"])} · estimated, not yet finalized
        </p>
      </div>

      {/* Hero gross */}
      <div className="rounded-2xl border border-slate-200 bg-white p-5">
        {openAvailable ? (
          <>
            <p className="text-xs font-semibold text-slate-400">Estimated gross</p>
            <p className="mt-1 text-4xl font-extrabold tabular-nums text-slate-900">{money(open["grossPay"], currency)}</p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Chip label="Line-haul" value={money(open["linehaulPay"], currency)} />
              <Chip label="Detention" value={money(open["detentionPay"], currency)} accent={openDetention > 0} />
              <Chip label="Loads" value={String(open["loadCount"] ?? 0)} />
            </div>
          </>
        ) : (
          <div className="flex items-start gap-3">
            <Info className="mt-0.5 h-5 w-5 shrink-0 text-slate-400" />
            <div>
              <p className="text-sm font-semibold text-slate-700">No earnings calculated for this period yet</p>
              <p className="text-xs text-slate-500">{String(open["reason"] ?? "Check back once you've run a load this period.")}</p>
            </div>
          </div>
        )}
      </div>

      {/* Detention Pay card — the differentiator, the ONLY accented block on the screen. */}
      {policyEnabled && (
        <div className="rounded-2xl border border-teal-200 bg-teal-50 p-5">
          <div className="flex items-center gap-2">
            <span className="flex h-8 w-8 items-center justify-center rounded-full bg-teal-500/15">
              <Coins className="h-4 w-4 text-teal-600" />
            </span>
            <p className="text-xs font-bold uppercase tracking-wide text-teal-700">Detention pay — recovered for you</p>
          </div>
          {openDetention > 0 ? (
            <>
              <p className="mt-3 text-3xl font-extrabold tabular-nums text-teal-700">{money(openDetention, currency)}</p>
              <p className="mt-1 text-sm text-teal-800">
                Your share of detention we collected for you on {openEvents} load{openEvents === 1 ? "" : "s"} this period.
              </p>
            </>
          ) : (
            <p className="mt-3 text-sm text-teal-800">No detention recovered this period — we keep watching every stop for you.</p>
          )}
          {Number(ytd["detentionPay"] ?? 0) > 0 && (
            <div className="mt-4 rounded-xl bg-white/70 px-3 py-2">
              <p className="text-xs font-semibold text-teal-800">
                {money(ytd["detentionPay"], currency)} in detention pay earned this year
                {Number(ytd["detentionEvents"] ?? 0) > 0 ? ` · ${Number(ytd["detentionEvents"])} events` : ""}
              </p>
            </div>
          )}
        </div>
      )}

      {/* YTD strip */}
      <div className="grid grid-cols-2 gap-3">
        <Stat label="Earned this year" value={money(ytd["earned"], currency)} />
        <Stat label="Paid this year" value={money(ytd["paid"], currency)} />
      </div>
      {Number(d["unpaidTotal"] ?? 0) > 0 && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3">
          <p className="text-sm font-semibold text-amber-800">
            {money(d["unpaidTotal"], currency)} approved and awaiting payment
          </p>
        </div>
      )}

      {/* Recent statements */}
      <div className="space-y-2">
        <p className="pt-2 text-xs font-bold uppercase tracking-wider text-slate-400">Statements</p>
        {statements.length === 0 ? (
          <div className="rounded-2xl border border-slate-200 bg-white p-6 text-center">
            <p className="text-sm text-slate-500">Your approved pay will appear here once your dispatcher approves the period.</p>
          </div>
        ) : (
          statements.map((s) => (
            <button
              key={String(s["id"])}
              type="button"
              onClick={() => setOpenStatementId(s["id"] as number | string)}
              className="flex w-full items-center justify-between rounded-2xl border border-slate-200 bg-white p-4 text-left active:bg-slate-50"
            >
              <div className="min-w-0">
                <p className="truncate text-sm font-bold text-slate-900">{String(s["statementNo"] ?? "Statement")}</p>
                <p className="text-xs text-slate-400">
                  {fmtDate(s["periodStart"])} – {fmtDate(s["periodEnd"])}
                  {Number(s["detentionTotal"] ?? 0) > 0 ? ` · incl. ${money(s["detentionTotal"], String(s["currency"] ?? currency))} detention` : ""}
                </p>
              </div>
              <div className="flex shrink-0 items-center gap-3">
                <div className="text-right">
                  <p className="text-sm font-bold tabular-nums text-slate-900">{money(s["total"], String(s["currency"] ?? currency))}</p>
                  <StatusPill status={String(s["status"])} outstanding={Number(s["outstanding"] ?? 0)} />
                </div>
                <ChevronRight className="h-4 w-4 text-slate-300" />
              </div>
            </button>
          ))
        )}
      </div>

      {/* Last payment */}
      {lastPayment && (
        <div className="rounded-2xl border border-slate-200 bg-white p-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <DollarSign className="h-4 w-4 text-slate-400" />
              <p className="text-sm text-slate-500">Last payment</p>
            </div>
            <div className="text-right">
              <p className="text-sm font-bold tabular-nums text-slate-900">{money(lastPayment["amount"], currency)}</p>
              <p className="text-xs text-slate-400">{fmtDate(lastPayment["paidAt"])}</p>
            </div>
          </div>
        </div>
      )}

      <p className="pt-2 text-center text-xs text-slate-400">
        Pay figures are calculated from approved settlements. Questions about your pay? Contact your dispatcher.
      </p>

      {openStatementId != null && (
        <StatementDetailSheet id={openStatementId} fallbackCurrency={currency} onClose={() => setOpenStatementId(null)} />
      )}
    </div>
  );
}

function StatementDetailSheet({ id, fallbackCurrency, onClose }: { id: number | string; fallbackCurrency: string; onClose: () => void }) {
  const { data, isLoading, isError, error } = useQuery<AnyRecord>({
    queryKey: ["driver", "earnings", "statement", id],
    queryFn: () => driverEarningsApi.statement(id),
  });

  const stmt = (data?.["statement"] as AnyRecord) ?? {};
  const totals = (data?.["totals"] as AnyRecord) ?? {};
  const lines = (data?.["lines"] as AnyRecord[]) ?? [];
  const payments = (data?.["payments"] as AnyRecord[]) ?? [];
  const currency = String(stmt["currency"] ?? fallbackCurrency);

  return (
    <div className="fixed inset-0 z-50 flex flex-col justify-end bg-black/40" onClick={onClose}>
      <div
        className="max-h-[85vh] overflow-y-auto rounded-t-3xl bg-white p-5 pb-10"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-base font-bold text-slate-900">{String(stmt["statementNo"] ?? "Statement")}</h2>
          <button type="button" onClick={onClose} className="rounded-full p-1 text-slate-400 active:bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </div>

        {isLoading && <p className="py-8 text-center text-sm text-slate-500">Loading statement…</p>}
        {isError && <p className="py-8 text-center text-sm text-red-600">{(error as Error)?.message ?? "Could not load this statement."}</p>}

        {!isLoading && !isError && (
          <>
            <p className="text-xs text-slate-400">
              {fmtDate(stmt["periodStart"])} – {fmtDate(stmt["periodEnd"])}
            </p>
            <div className="mt-3 space-y-1.5">
              {lines.map((l, i) => (
                <div key={i} className="flex items-start justify-between gap-3 border-b border-slate-100 py-1.5 last:border-0">
                  <div className="min-w-0">
                    <p className="truncate text-sm text-slate-700">{String(l["description"] ?? l["label"] ?? l["payCode"])}</p>
                    {Number(l["quantity"] ?? 0) > 0 && (
                      <p className="text-[11px] text-slate-400">Qty {String(l["quantity"])}</p>
                    )}
                  </div>
                  <p className={`shrink-0 text-sm font-semibold tabular-nums ${l["payCode"] === "detention" ? "text-teal-700" : "text-slate-800"}`}>
                    {money(l["amount"], currency)}
                  </p>
                </div>
              ))}
            </div>

            <div className="mt-4 space-y-1 rounded-2xl bg-slate-50 p-4">
              <TotalRow label="Line-haul" value={money(totals["linehaul"], currency)} />
              {Number(totals["detention"] ?? 0) > 0 && <TotalRow label="Detention pay" value={money(totals["detention"], currency)} accent />}
              {Number(totals["other"] ?? 0) > 0 && <TotalRow label="Other" value={money(totals["other"], currency)} />}
              <div className="mt-1 flex items-center justify-between border-t border-slate-200 pt-2">
                <p className="text-sm font-bold text-slate-900">Total</p>
                <p className="text-base font-extrabold tabular-nums text-slate-900">{money(stmt["total"], currency)}</p>
              </div>
              <div className="flex items-center justify-between">
                <p className="text-xs text-slate-500">Paid</p>
                <p className="text-xs font-semibold tabular-nums text-slate-600">{money(stmt["amountPaid"], currency)}</p>
              </div>
              {Number(stmt["outstanding"] ?? 0) > 0 && (
                <div className="flex items-center justify-between">
                  <p className="text-xs text-amber-700">Outstanding</p>
                  <p className="text-xs font-semibold tabular-nums text-amber-700">{money(stmt["outstanding"], currency)}</p>
                </div>
              )}
            </div>

            {payments.length > 0 && (
              <div className="mt-4">
                <p className="mb-1 text-xs font-bold uppercase tracking-wider text-slate-400">Payments</p>
                {payments.map((p, i) => (
                  <div key={i} className="flex items-center justify-between py-1 text-sm">
                    <span className="text-slate-500">{fmtDate(p["paidAt"])}</span>
                    <span className="font-semibold tabular-nums text-slate-800">{money(p["amount"], currency)}</span>
                  </div>
                ))}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function Chip({ label, value, accent = false }: { label: string; value: string; accent?: boolean }) {
  return (
    <div className={`rounded-xl px-3 py-2 ${accent ? "bg-teal-50 text-teal-800" : "bg-slate-50 text-slate-600"}`}>
      <p className="text-[10px] font-semibold uppercase tracking-wide opacity-70">{label}</p>
      <p className="text-sm font-bold tabular-nums">{value}</p>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4">
      <p className="text-xs font-semibold text-slate-400">{label}</p>
      <p className="mt-1 text-lg font-bold tabular-nums text-slate-900">{value}</p>
    </div>
  );
}

function TotalRow({ label, value, accent = false }: { label: string; value: string; accent?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <p className={`text-sm ${accent ? "text-teal-700" : "text-slate-500"}`}>{label}</p>
      <p className={`text-sm font-semibold tabular-nums ${accent ? "text-teal-700" : "text-slate-700"}`}>{value}</p>
    </div>
  );
}

function StatusPill({ status, outstanding }: { status: string; outstanding: number }) {
  const paid = status === "paid" && outstanding <= 0;
  return (
    <span
      className={`inline-block rounded-full px-2 py-0.5 text-[10px] font-semibold ${
        paid ? "bg-green-100 text-green-700" : "bg-amber-100 text-amber-700"
      }`}
    >
      {paid ? "Paid" : "Approved — awaiting pay"}
    </span>
  );
}
