import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Trash2, FileText, Wand2, Printer } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney, formatAmount, formatRate, minorUnits } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PHeader, PCard, PKpi, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty, PDrawer, PConfirm, PCheckbox, PBulkBar, useRowSelection } from "./ui";
import { BillingReadiness } from "./BillingReadiness";
import { printInvoice } from "./invoicePrint";

// Platform Admin — Collections. Every document here is itemized and taxed: the
// header totals are derived from the line sub-ledger, never typed in. Drafts are
// editable; issued documents are not, and are corrected by credit note.

type DraftLine = {
  description: string;
  featureKey?: string;
  quantity: string;
  unitPrice: string;
  discount: string;
};

const EMPTY_LINE: DraftLine = { description: "", quantity: "1", unitPrice: "0", discount: "0" };

const TREATMENT_LABEL: Record<string, string> = {
  standard: "Standard rated",
  zero_rated: "Zero rated",
  exempt: "Exempt",
  reverse_charge: "Reverse charge",
  out_of_scope: "Out of scope",
};

export function PlatformBillingPage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:billing:manage");

  const { data, isLoading, error } = useQuery({ queryKey: ["platform", "invoices"], queryFn: platformApi.invoices });
  const { data: tenants } = useQuery({ queryKey: ["platform", "tenants"], queryFn: platformApi.tenants });
  const { data: packages } = useQuery({ queryKey: ["platform", "packages"], queryFn: platformApi.packages });

  const [createOpen, setCreateOpen] = useState(false);
  const [generateOpen, setGenerateOpen] = useState(false);
  const [taxOpen, setTaxOpen] = useState(false);
  const [openDocId, setOpenDocId] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const rows = (data ?? []) as AnyRecord[];
  const sel = useRowSelection(rows.map((r) => Number(r.id)));
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkConfirm, setBulkConfirm] = useState<"void" | "delete" | null>(null);

  const refresh = () => qc.invalidateQueries({ queryKey: ["platform", "invoices"] });

  const runBulk = async (action: string) => {
    setBulkBusy(true); setNotice(null);
    try {
      const res = await platformApi.bulkInvoices({ ids: sel.selectedIds.map(Number), action }) as AnyRecord;
      const failed = Number(res.failed ?? 0);
      setNotice(`${action.replace(/-/g, " ")}: ${res.succeeded}/${res.requested} applied${failed > 0 ? ` · ${failed} failed` : ""}`);
      sel.clear();
      refresh();
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Bulk action failed");
    } finally {
      setBulkBusy(false); setBulkConfirm(null);
    }
  };

  // Totals are grouped by currency: summing SAR into a USD number would produce a
  // KPI that is wrong in every currency at once.
  const totals = useMemo(() => {
    const acc: Record<string, { collected: number; outstanding: number; tax: number }> = {};
    for (const r of rows) {
      const cur = String(r.currency ?? "USD");
      acc[cur] ??= { collected: 0, outstanding: 0, tax: 0 };
      const total = Number(r.totalCents ?? r.amountCents ?? 0);
      const status = String(r.status);
      if (status === "paid") acc[cur].collected += total;
      if (status === "sent" || status === "overdue") acc[cur].outstanding += total;
      if (status !== "draft" && status !== "void") acc[cur].tax += Number(r.taxTotalCents ?? 0);
    }
    return acc;
  }, [rows]);

  const primaryCurrency = Object.keys(totals).sort((a, b) =>
    (totals[b].collected + totals[b].outstanding) - (totals[a].collected + totals[a].outstanding))[0] ?? "USD";
  const head = totals[primaryCurrency] ?? { collected: 0, outstanding: 0, tax: 0 };
  const multiCurrency = Object.keys(totals).length > 1;

  const markPaid = async (id: number) => {
    setBusy(true);
    try { await platformApi.markPaid(id); refresh(); }
    finally { setBusy(false); }
  };

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  const draftCount = rows.filter((r) => String(r.status) === "draft").length;

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Billing & Invoices"
        title="Collections"
        description="Itemized invoicing with per-country VAT/GST determination. Generate a period from the tenant's billing plan, or build lines by hand — both take the same tax and rounding path."
        actions={canManage ? (
          <>
            <PButton variant="ghost" onClick={() => setTaxOpen(true)}>Tax setup</PButton>
            <PButton variant="ghost" onClick={() => setGenerateOpen(true)}><Wand2 className="h-4 w-4" /> Generate period</PButton>
            <PButton onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New Invoice</PButton>
          </>
        ) : undefined}
      />

      <BillingReadiness
        canManage={canManage}
        packages={(packages ?? []) as AnyRecord[]}
        onOpenTaxSetup={() => setTaxOpen(true)}
        onNotice={setNotice}
      />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PKpi label="Collected" value={formatAmount(head.collected, primaryCurrency)} tone="good"
              sub={multiCurrency ? `${primaryCurrency} · other currencies listed below` : primaryCurrency} />
        <PKpi label="Outstanding" value={formatAmount(head.outstanding, primaryCurrency)}
              tone={head.outstanding > 0 ? "warn" : "default"} sub={primaryCurrency} />
        <PKpi label="Tax billed" value={formatAmount(head.tax, primaryCurrency)}
              sub="VAT / GST across issued documents" />
        <PKpi label="Documents" value={rows.length} sub={draftCount > 0 ? `${draftCount} draft${draftCount === 1 ? "" : "s"} awaiting issue` : "all issued"} />
      </div>

      {multiCurrency && (
        <PCard className="px-5 py-3">
          <p className="text-[10px] font-black uppercase tracking-[0.24em] text-slate-400">By currency</p>
          <div className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-sm text-slate-600">
            {Object.entries(totals).map(([cur, t]) => (
              <span key={cur}>
                <strong className="text-slate-900">{cur}</strong>{" "}
                {formatAmount(t.collected, cur)} collected · {formatAmount(t.outstanding, cur)} outstanding
              </span>
            ))}
          </div>
        </PCard>
      )}

      {notice && (
        <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{notice}</div>
      )}

      {rows.length === 0 ? (
        <PEmpty title="No documents yet" subtitle="Generate a billing period for a tenant, or create an invoice by hand." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1040px] text-left text-sm">
              <thead className="border-b border-slate-200 bg-slate-50">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {canManage && (
                    <th className="w-10 px-5 py-3">
                      <PCheckbox
                        checked={sel.allVisibleSelected}
                        indeterminate={sel.someVisibleSelected}
                        onToggle={() => sel.toggleAllVisible()}
                        ariaLabel="Select all invoices"
                      />
                    </th>
                  )}
                  {["Document", "Tenant", "Lines", "Net", "Tax", "Total", "Status", "Due", ""].map((h) => (
                    <th key={h} className="px-5 py-3 font-semibold">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {rows.map((r) => {
                  const cur = String(r.currency ?? "USD");
                  const isCredit = String(r.documentType) === "credit_note";
                  return (
                    <tr key={String(r.id)} className={`hover:bg-slate-50 ${sel.isSelected(r.id) ? "bg-teal-50" : ""}`}>
                      {canManage && (
                        <td className="px-5 py-3.5">
                          <PCheckbox checked={sel.isSelected(r.id)} onToggle={(shift) => sel.toggle(r.id, shift)} ariaLabel={`Select ${String(r.invoiceNumber)}`} />
                        </td>
                      )}
                      <td className="px-5 py-3.5">
                        <button type="button" onClick={() => setOpenDocId(Number(r.id))}
                                className="font-mono text-xs font-semibold text-teal-700 hover:underline">
                          {String(r.invoiceNumber)}
                        </button>
                        {isCredit && <span className="ml-2 rounded bg-violet-50 px-1.5 py-0.5 text-[10px] font-bold text-violet-700">CREDIT</span>}
                        {r.taxCountry ? <span className="ml-2 text-[10px] font-semibold text-slate-400">{String(r.taxCountry)}</span> : null}
                      </td>
                      <td className="px-5 py-3.5 text-slate-700">{String(r.tenant)}</td>
                      <td className="px-5 py-3.5 text-slate-500">{Number(r.lineCount ?? 0)}</td>
                      <td className="px-5 py-3.5 text-slate-600">{formatAmount(Number(r.subtotalCents ?? 0), cur)}</td>
                      <td className="px-5 py-3.5 text-slate-600">
                        {formatAmount(Number(r.taxTotalCents ?? 0), cur)}
                        {Boolean(r.taxTreatment) && String(r.taxTreatment) !== "standard" && (
                          <span className="ml-1.5 text-[10px] font-semibold uppercase text-amber-600">
                            {TREATMENT_LABEL[String(r.taxTreatment)] ?? String(r.taxTreatment)}
                          </span>
                        )}
                      </td>
                      <td className="px-5 py-3.5 font-semibold text-slate-900">
                        {formatAmount(Number(r.totalCents ?? r.amountCents ?? 0), cur)}
                      </td>
                      <td className="px-5 py-3.5"><PBadge value={r.status} /></td>
                      <td className="px-5 py-3.5 font-mono text-xs text-slate-500">{String(r.dueAt ?? "").slice(0, 10) || "—"}</td>
                      <td className="px-5 py-3.5 text-right whitespace-nowrap">
                        <PButton variant="ghost" onClick={() => setOpenDocId(Number(r.id))}>Open</PButton>
                        {canManage && String(r.status) === "sent" && (
                          <PButton variant="ghost" disabled={busy} onClick={() => markPaid(Number(r.id))}>Mark paid</PButton>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </PCard>
      )}

      {canManage && (
        <PBulkBar count={sel.count} onClear={sel.clear}>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => runBulk("mark-paid")}>Mark paid</PButton>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => setBulkConfirm("void")}>Void</PButton>
          <PButton variant="danger" disabled={bulkBusy} onClick={() => setBulkConfirm("delete")}>Delete</PButton>
        </PBulkBar>
      )}

      <PConfirm
        open={bulkConfirm === "void"}
        title={`Void ${sel.count} document${sel.count === 1 ? "" : "s"}?`}
        body={<>The selected documents are marked void and drop out of outstanding collections. Already-paid documents are skipped — those need a credit note so the reversal stays on the record.</>}
        confirmLabel="Void documents"
        busy={bulkBusy}
        onConfirm={() => runBulk("void")}
        onClose={() => setBulkConfirm(null)}
      />
      <PConfirm
        open={bulkConfirm === "delete"}
        title={`Delete ${sel.count} document${sel.count === 1 ? "" : "s"}?`}
        body={<>The records and their line detail are permanently removed. This cannot be undone. Prefer <strong>Void</strong> — deleting an issued document leaves a hole in the numbering sequence that an auditor will ask about.</>}
        confirmLabel="Delete documents"
        busy={bulkBusy}
        onConfirm={() => runBulk("delete")}
        onClose={() => setBulkConfirm(null)}
      />

      {createOpen && (
        <CreateInvoiceDrawer
          tenants={(tenants ?? []) as AnyRecord[]}
          onClose={() => setCreateOpen(false)}
          onCreated={(msg) => { setCreateOpen(false); setNotice(msg); refresh(); }}
        />
      )}

      {generateOpen && (
        <GeneratePeriodDrawer
          tenants={(tenants ?? []) as AnyRecord[]}
          onClose={() => setGenerateOpen(false)}
          onCreated={(msg, id) => { setGenerateOpen(false); setNotice(msg); refresh(); if (id) setOpenDocId(id); }}
        />
      )}

      {taxOpen && <TaxSetupDrawer canManage={canManage} onClose={() => setTaxOpen(false)} />}

      {openDocId !== null && (
        <DocumentDrawer
          id={openDocId}
          canManage={canManage}
          onClose={() => setOpenDocId(null)}
          onChanged={(msg) => { setNotice(msg); refresh(); }}
        />
      )}
    </div>
  );
}

// ══════════════════════════ Document viewer ═══════════════════════════════════

function DocumentDrawer({ id, canManage, onClose, onChanged }: {
  id: number; canManage: boolean; onClose: () => void; onChanged: (msg: string) => void;
}) {
  const qc = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ["platform", "invoice", id], queryFn: () => platformApi.invoice(id) });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [prompt, setPrompt] = useState<"void" | "credit" | null>(null);
  const [reason, setReason] = useState("");

  const act = async (fn: () => Promise<AnyRecord>, msg: string) => {
    setBusy(true); setErr(null);
    try {
      await fn();
      onChanged(msg);
      qc.invalidateQueries({ queryKey: ["platform", "invoice", id] });
      setPrompt(null); setReason("");
    } catch (e) { setErr(e instanceof Error ? e.message : "Action failed"); }
    finally { setBusy(false); }
  };

  const inv = (data as AnyRecord)?.invoice as AnyRecord | undefined;
  const lines = ((data as AnyRecord)?.lines ?? []) as AnyRecord[];
  const breakdown = ((data as AnyRecord)?.taxBreakdown ?? []) as AnyRecord[];
  const cur = String(inv?.currency ?? "USD");
  const status = String(inv?.status ?? "");
  const isDraft = status === "draft";

  return (
    <PDrawer open onClose={onClose} title={inv ? String(inv.invoiceNumber) : "Document"}>
      {isLoading && <PLoading />}
      {error && <PError message={(error as Error)?.message} />}
      {err && <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{err}</div>}

      {inv && (
        <div className="space-y-6">
          {/* Parties + tax posture — the statutory header of any VAT invoice */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs leading-6">
              <p className="font-black uppercase tracking-[0.2em] text-slate-400">Supplier</p>
              <p className="font-semibold text-slate-900">{String(inv.sellerLegalName ?? "OpsTrax")}</p>
              <p className="text-slate-500">
                {inv.sellerTaxNo ? `${inv.taxLabel ?? "Tax"} ${String(inv.sellerTaxNo)}` : `No ${String(inv.taxLabel ?? "tax")} registration on file`}
              </p>
            </div>
            <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs leading-6">
              <p className="font-black uppercase tracking-[0.2em] text-slate-400">Customer</p>
              <p className="font-semibold text-slate-900">{String(inv.buyerLegalName ?? inv.tenant)}</p>
              <p className="text-slate-500">
                {inv.buyerTaxNo ? `${inv.taxLabel ?? "Tax"} ${String(inv.buyerTaxNo)}` : "No tax ID recorded"}
                {inv.buyerCountry ? ` · ${String(inv.buyerCountry)}` : ""}
              </p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3 text-xs sm:grid-cols-4">
            <Meta label="Status" value={<PBadge value={status} />} />
            <Meta label="Place of supply" value={String(inv.placeOfSupply ?? "—")} />
            <Meta label="Treatment" value={TREATMENT_LABEL[String(inv.taxTreatment)] ?? String(inv.taxTreatment ?? "—")} />
            <Meta label="Period" value={`${String(inv.periodStart ?? "").slice(0, 10) || "—"} → ${String(inv.periodEnd ?? "").slice(0, 10) || "—"}`} />
            <Meta label="Issued" value={String(inv.issuedAt ?? "").slice(0, 10) || "not issued"} />
            <Meta label="Due" value={String(inv.dueAt ?? "").slice(0, 10) || "—"} />
            <Meta label="Terms" value={`${Number(inv.paymentTermsDays ?? 0)} days`} />
            <Meta label="Currency" value={cur} />
          </div>

          {/* Line detail */}
          <div>
            <h3 className="text-xs font-black uppercase tracking-[0.2em] text-slate-400">Lines</h3>
            <div className="mt-2 overflow-x-auto rounded-xl border border-slate-200">
              <table className="w-full min-w-[640px] text-left text-xs">
                <thead className="bg-slate-50 text-[10px] uppercase tracking-wider text-slate-500">
                  <tr>
                    <th className="px-3 py-2 font-semibold">Description</th>
                    <th className="px-3 py-2 font-semibold">Qty</th>
                    <th className="px-3 py-2 font-semibold">Unit</th>
                    <th className="px-3 py-2 font-semibold">Net</th>
                    <th className="px-3 py-2 font-semibold">Tax</th>
                    <th className="px-3 py-2 text-right font-semibold">Total</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {lines.map((l) => (
                    <tr key={String(l.id)}>
                      <td className="px-3 py-2">
                        <p className="text-slate-800">{String(l.description)}</p>
                        <p className="text-[10px] text-slate-400">
                          {String(l.source)}{l.featureKey ? ` · ${String(l.featureKey)}` : ""} · {String(l.chargeModel)}
                        </p>
                      </td>
                      <td className="px-3 py-2 text-slate-600">{Number(l.quantity ?? 0).toLocaleString()}</td>
                      <td className="px-3 py-2 text-slate-600">{formatAmount(Number(l.unitPriceCents ?? 0), cur)}</td>
                      <td className="px-3 py-2 text-slate-600">
                        {formatAmount(Number(l.netAmountCents ?? 0), cur)}
                        {Number(l.discountCents ?? 0) !== 0 && (
                          <span className="ml-1 text-[10px] text-emerald-600">−{formatAmount(Number(l.discountCents), cur)}</span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-slate-600">
                        {formatAmount(Number(l.taxAmountCents ?? 0), cur)}
                        <span className="ml-1 text-[10px] text-slate-400">{formatRate(Number(l.taxRate ?? 0))}</span>
                      </td>
                      <td className="px-3 py-2 text-right font-semibold text-slate-900">
                        {formatAmount(Number(l.totalCents ?? 0), cur)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Tax summary — what a filing is assembled from */}
          {breakdown.length > 0 && (
            <div>
              <h3 className="text-xs font-black uppercase tracking-[0.2em] text-slate-400">Tax summary</h3>
              <div className="mt-2 overflow-hidden rounded-xl border border-slate-200">
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-50 text-[10px] uppercase tracking-wider text-slate-500">
                    <tr>
                      <th className="px-3 py-2 font-semibold">Code</th>
                      <th className="px-3 py-2 font-semibold">Cat.</th>
                      <th className="px-3 py-2 font-semibold">Rate</th>
                      <th className="px-3 py-2 font-semibold">Taxable</th>
                      <th className="px-3 py-2 text-right font-semibold">Tax</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {breakdown.map((b, i) => (
                      <tr key={i}>
                        <td className="px-3 py-2 font-mono text-slate-700">{String(b.taxCode ?? "—")}</td>
                        <td className="px-3 py-2 text-slate-600">{String(b.taxCategory ?? "—")}</td>
                        <td className="px-3 py-2 text-slate-600">{formatRate(Number(b.rate ?? 0))}</td>
                        <td className="px-3 py-2 text-slate-600">{formatAmount(Number(b.taxableCents ?? 0), cur)}</td>
                        <td className="px-3 py-2 text-right font-semibold text-slate-900">{formatAmount(Number(b.taxCents ?? 0), cur)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          <Totals currency={cur} subtotal={Number(inv.subtotalCents ?? 0)} tax={Number(inv.taxTotalCents ?? 0)} total={Number(inv.totalCents ?? 0)} />

          {Boolean(inv.taxTreatment) && String(inv.taxTreatment) !== "standard" && (
            <p className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              {String(lines[0]?.exemptionReason ?? TREATMENT_LABEL[String(inv.taxTreatment)] ?? "")}
            </p>
          )}

          {Boolean(inv.notes) && <p className="text-xs text-slate-500">{String(inv.notes)}</p>}
          {Boolean(inv.voidReason) && (
            <p className="rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
              Voided: {String(inv.voidReason)}
            </p>
          )}

          <div className="flex flex-wrap gap-2 border-t border-slate-200 pt-4">
            <PButton variant="ghost" onClick={() => printInvoice(data as AnyRecord)}>
              <Printer className="h-4 w-4" /> Print / PDF
            </PButton>
          </div>

          {canManage && (
            <div className="flex flex-wrap gap-2">
              {isDraft && (
                <PButton disabled={busy} onClick={() => act(() => platformApi.issueInvoice(id), "Invoice issued")}>
                  <FileText className="h-4 w-4" /> Issue
                </PButton>
              )}
              {status === "sent" && (
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.markPaid(id), "Marked paid")}>Mark paid</PButton>
              )}
              {status !== "void" && status !== "paid" && (
                <PButton variant="ghost" disabled={busy} onClick={() => setPrompt("void")}>Void</PButton>
              )}
              {!isDraft && String(inv.documentType) !== "credit_note" && (
                <PButton variant="danger" disabled={busy} onClick={() => setPrompt("credit")}>Credit note</PButton>
              )}
            </div>
          )}

          {prompt && (
            <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
              <PField label={prompt === "void" ? "Reason for voiding (recorded in the audit trail)" : "Reason for the credit note"}>
                <PInput value={reason} maxLength={400} onChange={(e) => setReason(e.target.value)}
                        placeholder={prompt === "void" ? "e.g. Raised against the wrong tenant" : "e.g. Service credit agreed under SLA clause 7"} />
              </PField>
              <div className="mt-3 flex gap-2">
                <PButton
                  disabled={busy || !reason.trim()}
                  onClick={() => act(
                    () => prompt === "void" ? platformApi.voidInvoice(id, reason.trim()) : platformApi.creditNote(id, reason.trim()),
                    prompt === "void" ? "Document voided" : "Credit note issued")}
                >
                  Confirm
                </PButton>
                <PButton variant="ghost" onClick={() => { setPrompt(null); setReason(""); }}>Cancel</PButton>
              </div>
            </div>
          )}
        </div>
      )}
    </PDrawer>
  );
}

function Meta({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-lg border border-slate-200 px-2.5 py-2">
      <p className="text-[9px] font-black uppercase tracking-[0.18em] text-slate-400">{label}</p>
      <p className="mt-1 text-xs font-medium text-slate-800">{value}</p>
    </div>
  );
}

function Totals({ currency, subtotal, tax, total }: { currency: string; subtotal: number; tax: number; total: number }) {
  return (
    <div className="ml-auto w-full max-w-xs space-y-1 text-sm">
      <div className="flex justify-between text-slate-600"><span>Net</span><span>{formatAmount(subtotal, currency)}</span></div>
      <div className="flex justify-between text-slate-600"><span>Tax</span><span>{formatAmount(tax, currency)}</span></div>
      <div className="flex justify-between border-t border-slate-300 pt-1 text-base font-black text-slate-950">
        <span>Total</span><span>{formatAmount(total, currency)}</span>
      </div>
    </div>
  );
}

// ══════════════════════════ Generate from billing plan ════════════════════════

function GeneratePeriodDrawer({ tenants, onClose, onCreated }: {
  tenants: AnyRecord[]; onClose: () => void; onCreated: (msg: string, id?: number) => void;
}) {
  const today = new Date();
  const firstOfMonth = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1)).toISOString().slice(0, 10);
  const lastOfMonth = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() + 1, 0)).toISOString().slice(0, 10);

  const [companyId, setCompanyId] = useState("");
  const [periodStart, setPeriodStart] = useState(firstOfMonth);
  const [periodEnd, setPeriodEnd] = useState(lastOfMonth);
  const [terms, setTerms] = useState("15");
  const [issue, setIssue] = useState(false);
  const [preview, setPreview] = useState<AnyRecord | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const runPreview = async () => {
    if (!companyId) return;
    setBusy(true); setErr(null); setPreview(null);
    try {
      setPreview(await platformApi.previewInvoice({ companyId: Number(companyId), periodStart, periodEnd }));
    } catch (e) { setErr(e instanceof Error ? e.message : "Preview failed"); }
    finally { setBusy(false); }
  };

  const generate = async () => {
    setBusy(true); setErr(null);
    try {
      const res = await platformApi.generateInvoice({
        companyId: Number(companyId), periodStart, periodEnd,
        paymentTermsDays: Number(terms), issue,
      });
      onCreated(issue ? `Invoice ${res.invoiceNumber} issued` : "Draft invoice generated", Number(res.id));
    } catch (e) { setErr(e instanceof Error ? e.message : "Generation failed"); }
    finally { setBusy(false); }
  };

  const lines = (preview?.lines ?? []) as AnyRecord[];
  const tax = preview?.tax as AnyRecord | undefined;
  const cur = String(preview?.currency ?? "USD");

  return (
    <PDrawer open onClose={onClose} title="Generate billing period">
      {err && <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{err}</div>}
      <div className="space-y-4">
        <PField label="Tenant">
          <PSelect value={companyId} onChange={(e) => { setCompanyId(e.target.value); setPreview(null); }}>
            <option value="">— Select tenant —</option>
            {tenants.map((t) => <option key={String(t.id)} value={String(t.id)}>{String(t.name)}</option>)}
          </PSelect>
        </PField>
        <div className="grid grid-cols-2 gap-3">
          <PField label="Period start"><PInput type="date" value={periodStart} onChange={(e) => setPeriodStart(e.target.value)} /></PField>
          <PField label="Period end"><PInput type="date" value={periodEnd} onChange={(e) => setPeriodEnd(e.target.value)} /></PField>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <PField label="Payment terms (days)"><PInput type="number" min={0} value={terms} onChange={(e) => setTerms(e.target.value)} /></PField>
          <PField label="On create">
            <PSelect value={issue ? "issue" : "draft"} onChange={(e) => setIssue(e.target.value === "issue")}>
              <option value="draft">Save as draft (review first)</option>
              <option value="issue">Issue immediately</option>
            </PSelect>
          </PField>
        </div>

        <PButton variant="ghost" disabled={busy || !companyId} onClick={runPreview}>Preview this period</PButton>

        {preview && (
          <div className="space-y-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
            <p className="text-xs text-slate-500">
              {String(tax?.countryName ?? "Unspecified")} · {TREATMENT_LABEL[String(tax?.treatment)] ?? String(tax?.treatment)} ·{" "}
              {formatRate(Number(tax?.rate ?? 0))} {String(tax?.taxLabel ?? "tax")} · rule <span className="font-mono">{String(tax?.ruleKey)}</span>
            </p>

            {lines.length === 0 ? (
              <PEmpty title="Nothing billable" subtitle="Assign a package or add billing plan items for this tenant." />
            ) : (
              <table className="w-full text-left text-xs">
                <tbody className="divide-y divide-slate-200">
                  {lines.map((l, i) => (
                    <tr key={i}>
                      <td className="py-1.5 pr-2 text-slate-700">{String(l.description)}</td>
                      <td className="py-1.5 text-right text-slate-500">{formatAmount(Number(l.netAmountCents ?? 0), cur)}</td>
                      <td className="py-1.5 pl-2 text-right font-medium text-slate-900">{formatAmount(Number(l.totalCents ?? 0), cur)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            <Totals currency={cur} subtotal={Number(preview.subtotalCents ?? 0)} tax={Number(preview.taxTotalCents ?? 0)} total={Number(preview.totalCents ?? 0)} />

            {((preview.notes ?? []) as string[]).map((n, i) => (
              <p key={i} className="rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1.5 text-[11px] text-amber-800">{n}</p>
            ))}
          </div>
        )}

        <div className="flex gap-2 pt-2">
          <PButton onClick={generate} disabled={busy || !companyId || (preview !== null && lines.length === 0)}>
            {issue ? "Generate & issue" : "Generate draft"}
          </PButton>
          <PButton variant="ghost" onClick={onClose}>Cancel</PButton>
        </div>
      </div>
    </PDrawer>
  );
}

// ══════════════════════════ Manual itemized invoice ═══════════════════════════

function CreateInvoiceDrawer({ tenants, onClose, onCreated }: {
  tenants: AnyRecord[]; onClose: () => void; onCreated: (msg: string) => void;
}) {
  const [companyId, setCompanyId] = useState("");
  const [kind, setKind] = useState("one_time");
  const [dueDays, setDueDays] = useState("15");
  const [asDraft, setAsDraft] = useState(true);
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([{ ...EMPTY_LINE }]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  // The tax posture is fetched for the chosen tenant so the operator sees the rate
  // that will actually be applied BEFORE they commit the document.
  const { data: taxCtx } = useQuery({
    queryKey: ["platform", "tax-context", companyId],
    queryFn: () => platformApi.tenantTaxContext(Number(companyId)),
    enabled: companyId !== "",
  });

  const currency = String(taxCtx?.currency ?? "USD");
  const digits = minorUnits(currency);
  const scale = 10 ** digits;
  const rate = String(taxCtx?.treatment) === "standard" ? Number(taxCtx?.rate ?? 0) : 0;

  const toMinor = (v: string) => Math.round((Number(v) || 0) * scale);

  const computed = lines.map((l) => {
    const net = Math.max(0, Math.round((Number(l.quantity) || 0) * toMinor(l.unitPrice)) - toMinor(l.discount));
    return { net, tax: Math.round(net * rate) };
  });
  const subtotal = computed.reduce((a, c) => a + c.net, 0);
  const taxTotal = computed.reduce((a, c) => a + c.tax, 0);

  const setLine = (i: number, patch: Partial<DraftLine>) =>
    setLines((prev) => prev.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  const validLines = lines.filter((l) => l.description.trim() !== "");
  const canSubmit = companyId !== "" && validLines.length > 0;

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      await platformApi.createInvoice({
        companyId: Number(companyId),
        kind,
        dueDays: Number(dueDays),
        status: asDraft ? "draft" : "sent",
        notes: notes || undefined,
        lines: validLines.map((l) => ({
          description: l.description.trim(),
          quantity: Number(l.quantity) || 0,
          unitPriceCents: toMinor(l.unitPrice),
          discountCents: toMinor(l.discount),
          chargeModel: "flat",
          source: "manual",
        })),
      });
      onCreated(asDraft ? "Draft invoice created" : "Invoice created and issued");
    } catch (e) { setErr(e instanceof Error ? e.message : "Failed"); }
    finally { setBusy(false); }
  };

  return (
    <PDrawer open onClose={onClose} title="New Invoice">
      {err && <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{err}</div>}
      <div className="space-y-4">
        <PField label="Tenant">
          <PSelect value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            <option value="">— Select tenant —</option>
            {tenants.map((t) => <option key={String(t.id)} value={String(t.id)}>{String(t.name)}</option>)}
          </PSelect>
        </PField>

        {taxCtx && (
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs leading-6 text-slate-600">
            <span className="font-semibold text-slate-800">{String(taxCtx.countryName)}</span> ·{" "}
            {TREATMENT_LABEL[String(taxCtx.treatment)] ?? String(taxCtx.treatment)} · {formatRate(Number(taxCtx.rate ?? 0))}{" "}
            {String(taxCtx.taxLabel)} · billed in {currency}
            {!taxCtx.sellerRegistered && String(taxCtx.treatment) === "standard" && (
              <p className="mt-1 text-amber-700">
                No OpsTrax {String(taxCtx.taxLabel)} registration is on file for {String(taxCtx.countryCode)} — record it under Tax setup before issuing.
              </p>
            )}
            {taxCtx.reasonText ? <p className="mt-1 text-slate-500">{String(taxCtx.reasonText)}</p> : null}
          </div>
        )}

        <div>
          <div className="flex items-center justify-between">
            <p className="text-xs font-black uppercase tracking-[0.2em] text-slate-400">Lines</p>
            <PButton variant="ghost" onClick={() => setLines((p) => [...p, { ...EMPTY_LINE }])}>
              <Plus className="h-3.5 w-3.5" /> Add line
            </PButton>
          </div>
          <div className="mt-2 space-y-2">
            {lines.map((l, i) => (
              <div key={i} className="rounded-xl border border-slate-200 p-3">
                <div className="flex items-start gap-2">
                  <div className="flex-1">
                    <PInput
                      value={l.description}
                      placeholder="Description (e.g. Onboarding & data migration)"
                      onChange={(e) => setLine(i, { description: e.target.value })}
                    />
                  </div>
                  {lines.length > 1 && (
                    <button
                      type="button"
                      aria-label="Remove line"
                      className="mt-2 text-slate-400 hover:text-red-600"
                      onClick={() => setLines((p) => p.filter((_, idx) => idx !== i))}
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </div>
                <div className="mt-2 grid grid-cols-3 gap-2">
                  <PField label="Qty"><PInput type="number" min={0} step="any" value={l.quantity} onChange={(e) => setLine(i, { quantity: e.target.value })} /></PField>
                  <PField label={`Unit price (${currency})`}><PInput type="number" min={0} step="any" value={l.unitPrice} onChange={(e) => setLine(i, { unitPrice: e.target.value })} /></PField>
                  <PField label="Discount"><PInput type="number" min={0} step="any" value={l.discount} onChange={(e) => setLine(i, { discount: e.target.value })} /></PField>
                </div>
                <p className="mt-1 text-right text-[11px] text-slate-500">
                  Net {formatAmount(computed[i].net, currency)} · Tax {formatAmount(computed[i].tax, currency)} ·{" "}
                  <span className="font-semibold text-slate-800">{formatAmount(computed[i].net + computed[i].tax, currency)}</span>
                </p>
              </div>
            ))}
          </div>
        </div>

        <Totals currency={currency} subtotal={subtotal} tax={taxTotal} total={subtotal + taxTotal} />

        <div className="grid grid-cols-2 gap-3">
          <PField label="Type">
            <PSelect value={kind} onChange={(e) => setKind(e.target.value)}>
              <option value="one_time">One-time</option>
              <option value="recurring">Recurring</option>
            </PSelect>
          </PField>
          <PField label="Due in (days)"><PInput type="number" min={0} value={dueDays} onChange={(e) => setDueDays(e.target.value)} /></PField>
        </div>
        <PField label="On create">
          <PSelect value={asDraft ? "draft" : "issue"} onChange={(e) => setAsDraft(e.target.value === "draft")}>
            <option value="draft">Save as draft (no number allocated yet)</option>
            <option value="issue">Issue immediately</option>
          </PSelect>
        </PField>
        <PField label="Notes"><PInput value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} /></PField>

        <div className="flex gap-2 pt-2">
          <PButton onClick={submit} disabled={busy || !canSubmit}>{asDraft ? "Create draft" : "Create & issue"}</PButton>
          <PButton variant="ghost" onClick={onClose}>Cancel</PButton>
        </div>
      </div>
    </PDrawer>
  );
}

// ══════════════════════════ Tax setup ═════════════════════════════════════════

function TaxSetupDrawer({ canManage, onClose }: { canManage: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const { data: regs, isLoading } = useQuery({ queryKey: ["platform", "tax-registrations"], queryFn: platformApi.taxRegistrations });
  const { data: rules } = useQuery({ queryKey: ["platform", "tax-rules"], queryFn: platformApi.taxRules });
  const [editing, setEditing] = useState<string | null>(null);
  const [form, setForm] = useState({ legalName: "", taxRegistrationNo: "", standardRate: "", registered: false });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const startEdit = (r: AnyRecord) => {
    setEditing(String(r.countryCode));
    setForm({
      legalName: String(r.legalName ?? ""),
      taxRegistrationNo: String(r.taxRegistrationNo ?? ""),
      standardRate: r.standardRate == null ? "" : String(Number(r.standardRate) * 100),
      registered: Boolean(r.registered),
    });
    setErr(null);
  };

  const save = async () => {
    if (!editing) return;
    setBusy(true); setErr(null);
    try {
      await platformApi.setTaxRegistration(editing, {
        legalName: form.legalName || undefined,
        taxRegistrationNo: form.taxRegistrationNo || undefined,
        standardRate: form.standardRate === "" ? undefined : Number(form.standardRate) / 100,
        registered: form.registered,
      });
      setEditing(null);
      qc.invalidateQueries({ queryKey: ["platform", "tax-registrations"] });
    } catch (e) { setErr(e instanceof Error ? e.message : "Save failed"); }
    finally { setBusy(false); }
  };

  // Countries with no tenants are noise on this screen — surface where OpsTrax is
  // actually supplying, plus anywhere a registration is already claimed.
  const relevant = ((regs ?? []) as AnyRecord[]).filter((r) => Number(r.tenantCount ?? 0) > 0 || r.registered);
  const rest = ((regs ?? []) as AnyRecord[]).filter((r) => !relevant.includes(r));

  return (
    <PDrawer open onClose={onClose} title="Tax setup">
      {isLoading && <PLoading />}
      {err && <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{err}</div>}

      <div className="space-y-6">
        <p className="text-xs leading-6 text-slate-500">
          A tenant's invoice is taxed on its <strong>country of activation</strong>. Where OpsTrax holds a registration,
          the domestic standard rate applies. Where it does not and the customer has supplied a business tax ID, the
          supply is reverse-charged. Both outcomes come from the rule table below, not from code.
        </p>

        <div>
          <h3 className="text-xs font-black uppercase tracking-[0.2em] text-slate-400">Registrations — countries you supply</h3>
          <div className="mt-2 space-y-2">
            {relevant.length === 0 && <PEmpty title="No tenants have a country of activation set" subtitle="Set a country on the tenant record so its supply can be located." />}
            {relevant.map((r) => (
              <div key={String(r.countryCode)} className="rounded-xl border border-slate-200 p-3">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-semibold text-slate-900">
                      {String(r.countryName ?? r.countryCode)} <span className="text-xs font-normal text-slate-400">({String(r.countryCode)})</span>
                    </p>
                    <p className="text-xs text-slate-500">
                      {String(r.taxLabel ?? "Tax")} {formatRate(Number(r.standardRate ?? 0))} ·{" "}
                      {Number(r.tenantCount ?? 0)} tenant{Number(r.tenantCount ?? 0) === 1 ? "" : "s"}
                      {r.taxRegistrationNo ? ` · ${String(r.taxRegistrationNo)}` : ""}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <PBadge value={r.registered ? "active" : "draft"} />
                    {canManage && <PButton variant="ghost" onClick={() => startEdit(r)}>Edit</PButton>}
                  </div>
                </div>

                {editing === String(r.countryCode) && (
                  <div className="mt-3 space-y-3 border-t border-slate-200 pt-3">
                    <PField label="Registered legal entity name">
                      <PInput value={form.legalName} onChange={(e) => setForm({ ...form, legalName: e.target.value })} placeholder="OpsTrax Technologies Ltd" />
                    </PField>
                    <div className="grid grid-cols-2 gap-3">
                      <PField label={`${String(r.taxIdLabel ?? r.taxLabel ?? "Registration")} number`}>
                        <PInput value={form.taxRegistrationNo} onChange={(e) => setForm({ ...form, taxRegistrationNo: e.target.value })} />
                      </PField>
                      <PField label="Standard rate (%)">
                        <PInput type="number" min={0} max={100} step="any" value={form.standardRate}
                                onChange={(e) => setForm({ ...form, standardRate: e.target.value })} />
                      </PField>
                    </div>
                    <label className="flex items-center gap-2 text-xs text-slate-600">
                      <input type="checkbox" checked={form.registered} onChange={(e) => setForm({ ...form, registered: e.target.checked })} />
                      OpsTrax is registered to collect in this country
                    </label>
                    <div className="flex gap-2">
                      <PButton disabled={busy} onClick={save}>Save</PButton>
                      <PButton variant="ghost" onClick={() => setEditing(null)}>Cancel</PButton>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
          {rest.length > 0 && (
            <p className="mt-2 text-[11px] text-slate-400">
              {rest.length} further country profile{rest.length === 1 ? "" : "s"} are configured but have no tenants yet.
            </p>
          )}
        </div>

        <div>
          <h3 className="text-xs font-black uppercase tracking-[0.2em] text-slate-400">Determination rules</h3>
          <p className="mt-1 text-[11px] text-slate-400">Evaluated in priority order; the first match decides. A blank condition matches anything.</p>
          <div className="mt-2 overflow-hidden rounded-xl border border-slate-200">
            <table className="w-full text-left text-xs">
              <thead className="bg-slate-50 text-[10px] uppercase tracking-wider text-slate-500">
                <tr>
                  <th className="px-3 py-2 font-semibold">#</th>
                  <th className="px-3 py-2 font-semibold">Rule</th>
                  <th className="px-3 py-2 font-semibold">When</th>
                  <th className="px-3 py-2 font-semibold">Treatment</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {((rules ?? []) as AnyRecord[]).map((r) => (
                  <tr key={String(r.ruleKey)}>
                    <td className="px-3 py-2 text-slate-400">{Number(r.priority)}</td>
                    <td className="px-3 py-2">
                      <p className="font-mono text-slate-800">{String(r.ruleKey)}</p>
                      <p className="text-[10px] text-slate-400">{String(r.description ?? "")}</p>
                    </td>
                    <td className="px-3 py-2 text-slate-600">
                      {[
                        r.matchCountry ? `country = ${String(r.matchCountry)}` : null,
                        r.matchRegistered === null || r.matchRegistered === undefined ? null : `registered = ${String(r.matchRegistered)}`,
                        r.matchHasTaxId === null || r.matchHasTaxId === undefined ? null : `buyer tax ID = ${String(r.matchHasTaxId)}`,
                      ].filter(Boolean).join(", ") || "any"}
                    </td>
                    <td className="px-3 py-2">
                      <span className="font-medium text-slate-800">{TREATMENT_LABEL[String(r.treatment)] ?? String(r.treatment)}</span>
                      <span className="ml-1 text-slate-400">{r.rate == null ? "at standard rate" : formatRate(Number(r.rate))}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </PDrawer>
  );
}
