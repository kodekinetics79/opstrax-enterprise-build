import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Clock, DollarSign, Bell, CheckCircle2, Share2 } from "lucide-react";
import { detentionApi } from "@/services/detentionApi";
import { ErrorState, LoadingState, PageHeader } from "@/components/ui";
import { Field, Table, money } from "@/pages/BillingConsolidationPage";
import type { AnyRecord } from "@/types";

// Detention Recovery — the wedge. One page, plain language, funnel first (per the director panel:
// "the recovered-dollars counter IS the product"). Task queue, not a NASA dashboard.
const TABS = ["Ready to approve", "Needs attention", "History", "Rule Cards"] as const;
type Tab = (typeof TABS)[number];

const NEEDS_ATTENTION = ["unpriced_no_terms", "needs_appointment", "late_arrival", "unattributed"];

export function DetentionPage() {
  const [tab, setTab] = useState<Tab>("Ready to approve");
  const qc = useQueryClient();
  const invalidate = () => qc.invalidateQueries({ queryKey: ["detention"] });

  const funnel = useQuery({ queryKey: ["detention", "funnel"], queryFn: () => detentionApi.funnel(), refetchInterval: 60_000 });
  const dwells = useQuery({ queryKey: ["detention", "dwells"], queryFn: () => detentionApi.dwells(), refetchInterval: 60_000 });
  const cards = useQuery({ queryKey: ["detention", "cards"], queryFn: detentionApi.ruleCards, enabled: tab === "Rule Cards" });

  const approve = useMutation({
    mutationFn: ({ id, note }: { id: number; note?: string }) => detentionApi.approve(id, note),
    onSuccess: invalidate,
  });
  const dismiss = useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) => detentionApi.dismiss(id, reason),
    onSuccess: invalidate,
  });
  const share = useMutation({
    mutationFn: (id: number) => detentionApi.shareEvidence(id),
    onSuccess: (data) => {
      const url = `${window.location.origin}${String((data as AnyRecord)?.shareUrl ?? "")}`;
      void navigator.clipboard?.writeText(url);
    },
  });

  if (funnel.isLoading || dwells.isLoading) return <LoadingState />;
  if (dwells.isError) return <ErrorState message={(dwells.error as Error)?.message} onRetry={() => dwells.refetch()} />;

  const f = (funnel.data ?? {}) as AnyRecord;
  const rows = dwells.data ?? [];
  const ready = rows.filter((r) => r.status === "priced_pending_review");
  const attention = rows.filter((r) => NEEDS_ATTENTION.includes(String(r.status)));
  const history = rows.filter((r) => ["charged", "dismissed", "below_free_time"].includes(String(r.status)));

  return (
    <div className="flex flex-col gap-6 py-6">
      <PageHeader
        title="Detention Recovery"
        description="Turn dock time into paid invoices — GPS-proven, appointment-aware, approved by you before anything bills."
      />

      {/* The funnel: the number the owner screenshots. */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          { label: "Detected", value: money(f.detectedAmount), sub: `${f.detectedCount ?? 0} dwells`, icon: <Clock className="h-5 w-5" />, accent: "text-slate-700", bg: "bg-slate-50" },
          { label: "Notified", value: String(f.notifiedCount ?? 0), sub: "meter-running notices", icon: <Bell className="h-5 w-5" />, accent: "text-amber-700", bg: "bg-amber-50" },
          { label: "Approved", value: money(f.approvedAmount), sub: "charges created", icon: <CheckCircle2 className="h-5 w-5" />, accent: "text-teal-700", bg: "bg-teal-50" },
          { label: "Billed → Collected", value: `${money(f.billedAmount)} → ${money(f.collectedAmount)}`, sub: "on issued invoices", icon: <DollarSign className="h-5 w-5" />, accent: "text-emerald-700", bg: "bg-emerald-50" },
        ].map((k) => (
          <div key={k.label} className="panel flex items-start gap-3 p-4">
            <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${k.bg} ${k.accent}`}>{k.icon}</div>
            <div>
              <p className={`text-lg font-bold ${k.accent}`}>{k.value}</p>
              <p className="text-xs font-medium text-slate-500">{k.label} · {k.sub}</p>
            </div>
          </div>
        ))}
      </div>

      <div className="flex gap-2 border-b border-slate-200">
        {TABS.map((t) => (
          <button key={t} type="button" onClick={() => setTab(t)}
            className={`px-4 py-2 text-sm font-semibold ${tab === t ? "border-b-2 border-teal-600 text-teal-700" : "text-slate-500 hover:text-slate-700"}`}>
            {t}
            {t === "Ready to approve" && ready.length > 0 && (
              <span className="ml-1.5 rounded-full bg-teal-100 px-1.5 text-xs text-teal-800">{ready.length}</span>
            )}
            {t === "Needs attention" && attention.length > 0 && (
              <span className="ml-1.5 rounded-full bg-amber-100 px-1.5 text-xs text-amber-800">{attention.length}</span>
            )}
          </button>
        ))}
      </div>

      {tab === "Ready to approve" && (
        <DwellQueue rows={ready} kind="ready"
          onApprove={(id, note) => approve.mutate({ id, note })}
          onDismiss={(id, reason) => dismiss.mutate({ id, reason })}
          onShare={(id) => share.mutate(id)} />
      )}
      {tab === "Needs attention" && (
        <DwellQueue rows={attention} kind="attention"
          onApprove={(id, note) => approve.mutate({ id, note })}
          onDismiss={(id, reason) => dismiss.mutate({ id, reason })}
          onShare={(id) => share.mutate(id)} />
      )}
      {tab === "History" && (
        <Table rows={history} empty="No history yet — approved and dismissed dwells appear here."
          cols={[
            ["Site", (r) => String(r.siteName ?? "")],
            ["Customer", (r) => String(r.customerName ?? "")],
            ["Job", (r) => String(r.jobCode ?? "—")],
            ["Amount", (r) => money(r.amount)],
            ["Status", (r) => String(r.status)],
          ]} />
      )}
      {tab === "Rule Cards" && <RuleCards cards={cards.data ?? []} onSaved={invalidate} />}
    </div>
  );
}

// ── The queue: each dwell is a sentence, not a chart. ──────────────────────────
function DwellQueue({ rows, kind, onApprove, onDismiss, onShare }: {
  rows: AnyRecord[];
  kind: "ready" | "attention";
  onApprove: (id: number, note?: string) => void;
  onDismiss: (id: number, reason: string) => void;
  onShare: (id: number) => void;
}) {
  const [noteFor, setNoteFor] = useState<number | null>(null);
  const [note, setNote] = useState("");

  if (!rows.length)
    return <p className="py-8 text-center text-sm text-slate-400">
      {kind === "ready" ? "Nothing waiting for approval — detected detention lands here automatically." : "Nothing needs attention."}
    </p>;

  return (
    <div className="space-y-3">
      {rows.map((r) => {
        const id = Number(r.id);
        const needsOverride = r.status === "late_arrival";
        const daysLeft = Number(r.claimDaysLeft ?? 0);
        return (
          <div key={id} className="panel space-y-2 p-4">
            <p className="text-sm text-slate-800">
              <strong>{String(r.siteName ?? "Site")}</strong> — vehicle #{String(r.vehicleId)} dwelled{" "}
              {r.dwellMinutes != null ? `${Math.floor(Number(r.dwellMinutes) / 60)}h ${Number(r.dwellMinutes) % 60}m` : "…"}
              {r.freeMinutesApplied != null ? ` (${String(r.freeMinutesApplied)}m free applied)` : ""}
              {r.amount != null ? <> → <strong>{money(r.amount)}</strong> detention</> : null}
              {r.jobCode ? ` · job ${String(r.jobCode)}` : ""}
            </p>
            <p className="text-xs text-slate-500">
              {r.status === "unpriced_no_terms" && "No rule card for this customer — set terms in Rule Cards to start capturing this."}
              {r.status === "needs_appointment" && "No appointment on record — enter it (or attest none was scheduled) and it reprices automatically."}
              {r.status === "late_arrival" && "Arrived after the appointment + grace — approving requires an override note (shippers commonly void late-arrival detention)."}
              {r.status === "priced_pending_review" && (
                <>Clock: later of appointment vs arrival · billed from the shortest provable window
                  {r.warningNotifiedAt ? " · customer notified before charges began" : ""}
                  {daysLeft > 0 ? ` · ${daysLeft} days left in the claim window` : ""}</>
              )}
            </p>
            <div className="flex flex-wrap items-center gap-2">
              {(r.status === "priced_pending_review" || needsOverride) && (
                noteFor === id || needsOverride ? (
                  <>
                    <input type="text" value={note} onChange={(e) => setNote(e.target.value)}
                      placeholder={needsOverride ? "Override note (required for late arrival)" : "Optional override note"}
                      className="w-72 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none" />
                    <button type="button" className="btn-primary text-sm"
                      disabled={needsOverride && !note.trim()}
                      onClick={() => { onApprove(id, note.trim() || undefined); setNote(""); setNoteFor(null); }}>
                      Approve &amp; bill
                    </button>
                  </>
                ) : (
                  <>
                    <button type="button" className="btn-primary text-sm" onClick={() => onApprove(id)}>Approve &amp; bill</button>
                    <button type="button" className="text-xs text-slate-500 underline" onClick={() => setNoteFor(id)}>with note…</button>
                  </>
                )
              )}
              {r.status === "charged" || r.status === "priced_pending_review" ? (
                <button type="button" className="btn-secondary flex items-center gap-1 text-sm" onClick={() => onShare(id)}>
                  <Share2 className="h-3.5 w-3.5" /> Share proof
                </button>
              ) : null}
              <button type="button" className="text-sm text-red-600 hover:underline"
                onClick={() => { const reason = window.prompt("Reason for dismissing this dwell:"); if (reason?.trim()) onDismiss(id, reason.trim()); }}>
                Dismiss
              </button>
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ── Rule cards: one customer picker + five numbers, saveable in under a minute. ──
function RuleCards({ cards, onSaved }: { cards: AnyRecord[]; onSaved: () => void }) {
  const [customerId, setCustomerId] = useState("");
  const [freeMinutes, setFreeMinutes] = useState("120");
  const [ratePerHour, setRatePerHour] = useState("60");
  const [increment, setIncrement] = useState("15");
  const [claimDays, setClaimDays] = useState("30");
  const save = useMutation({
    mutationFn: () => detentionApi.saveRuleCard({
      customerId: customerId ? Number(customerId) : undefined,
      freeMinutes: Number(freeMinutes), ratePerHour: Number(ratePerHour),
      billingIncrementMinutes: Number(increment), claimWindowDays: Number(claimDays),
    }),
    onSuccess: onSaved,
  });
  return (
    <div className="space-y-4">
      <div className="panel space-y-3 p-4">
        <p className="section-title">New rule card {customerId ? "(customer-specific)" : "(tenant default)"}</p>
        <div className="flex flex-wrap gap-3">
          <Field label="Customer ID (blank = all customers)"><input className="w-40 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none" value={customerId} onChange={(e) => setCustomerId(e.target.value)} placeholder="e.g. 12" /></Field>
          <Field label="Free time (minutes)"><input className="w-28 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none" value={freeMinutes} onChange={(e) => setFreeMinutes(e.target.value)} /></Field>
          <Field label="Rate per hour"><input className="w-28 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none" value={ratePerHour} onChange={(e) => setRatePerHour(e.target.value)} /></Field>
          <Field label="Billing increment (min, rounded DOWN)"><input className="w-28 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none" value={increment} onChange={(e) => setIncrement(e.target.value)} /></Field>
          <Field label="Claim window (days)"><input className="w-28 rounded-lg border border-slate-300 px-3 py-1.5 text-sm focus:border-teal-500 focus:outline-none" value={claimDays} onChange={(e) => setClaimDays(e.target.value)} /></Field>
        </div>
        <button type="button" className="btn-primary text-sm" disabled={save.isPending || Number(ratePerHour) <= 0} onClick={() => save.mutate()}>
          {save.isPending ? "Saving…" : "Save terms"}
        </button>
        {save.isError ? <p className="text-xs text-red-600">{(save.error as Error)?.message}</p> : null}
      </div>
      <Table rows={cards} empty="No rule cards yet — without terms, dwell is detected and shown but never priced."
        cols={[
          ["Scope", (r) => (r.scopeType === "customer" ? `Customer #${String(r.scopeId)}` : "All customers")],
          ["Free time", (r) => `${String(r.freeMinutes)} min`],
          ["Rate", (r) => `${money(r.ratePerHour)}/h`],
          ["Increment", (r) => `${String(r.billingIncrementMinutes)} min (down)`],
          ["Claim window", (r) => `${String(r.claimWindowDays)} days`],
          ["Version", (r) => `v${String(r.version)}`],
        ]} />
    </div>
  );
}
