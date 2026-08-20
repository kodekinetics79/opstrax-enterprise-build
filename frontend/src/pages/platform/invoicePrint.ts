import { formatAmount, formatRate } from "@/services/platformApi";

type AnyRecord = Record<string, any>;

const TREATMENT_NOTE: Record<string, string> = {
  reverse_charge: "Reverse charge — the customer accounts for the tax due on this supply.",
  zero_rated: "Zero rated supply.",
  exempt: "Exempt supply — no tax chargeable.",
  out_of_scope: "Outside the scope of VAT/GST in the place of supply.",
};

function esc(value: unknown): string {
  return String(value ?? "")
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

function date(value: unknown): string {
  const raw = String(value ?? "").slice(0, 10);
  return raw || "—";
}

// Renders the document the CUSTOMER receives, not the operator's working view:
// the statutory header (both parties and their registration numbers, the place of
// supply, the tax point), the priced lines, and the tax summary by rate that a
// VAT/GST return is actually assembled from.
//
// Opened in its own window with fully inline styles so the console's own CSS —
// dark surfaces, app chrome, drawer layout — can never bleed into a printed
// document, and so the output is identical on every machine in the room.
export function printInvoice(doc: AnyRecord): void {
  const inv = (doc?.invoice ?? {}) as AnyRecord;
  const lines = ((doc?.lines ?? []) as AnyRecord[]);
  const breakdown = ((doc?.taxBreakdown ?? []) as AnyRecord[]);
  const cur = String(inv.currency ?? "USD");
  const isCredit = String(inv.documentType) === "credit_note";
  const taxLabel = String(inv.taxLabel ?? "Tax");
  const treatment = String(inv.taxTreatment ?? "standard");

  const lineRows = lines.map((l, i) => `
    <tr>
      <td class="num">${i + 1}</td>
      <td>
        <div class="desc">${esc(l.description)}</div>
        ${l.featureKey ? `<div class="sub">${esc(l.featureKey)}</div>` : ""}
      </td>
      <td class="num">${Number(l.quantity ?? 0).toLocaleString()}</td>
      <td class="num">${esc(formatAmount(Number(l.unitPriceCents ?? 0), cur))}</td>
      <td class="num">${esc(formatAmount(Number(l.netAmountCents ?? 0), cur))}</td>
      <td class="num">${esc(l.taxCode ?? "—")}</td>
      <td class="num">${esc(formatRate(Number(l.taxRate ?? 0)))}</td>
      <td class="num">${esc(formatAmount(Number(l.taxAmountCents ?? 0), cur))}</td>
      <td class="num strong">${esc(formatAmount(Number(l.totalCents ?? 0), cur))}</td>
    </tr>`).join("");

  const breakdownRows = breakdown.map((b) => `
    <tr>
      <td>${esc(b.taxCode ?? "—")}</td>
      <td>${esc(b.taxCategory ?? "—")}</td>
      <td class="num">${esc(formatRate(Number(b.rate ?? 0)))}</td>
      <td class="num">${esc(formatAmount(Number(b.taxableCents ?? 0), cur))}</td>
      <td class="num strong">${esc(formatAmount(Number(b.taxCents ?? 0), cur))}</td>
    </tr>`).join("");

  const treatmentNote = treatment !== "standard"
    ? `<p class="notice">${esc(lines[0]?.exemptionReason ?? TREATMENT_NOTE[treatment] ?? "")}</p>`
    : "";

  const draftBanner = String(inv.status) === "draft"
    ? `<div class="draft-stamp">DRAFT — NOT ISSUED</div>` : "";
  const voidBanner = String(inv.status) === "void"
    ? `<div class="draft-stamp void">VOID</div>` : "";

  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8" />
<title>${esc(inv.invoiceNumber)}</title>
<style>
  @page { size: A4; margin: 16mm; }
  * { box-sizing: border-box; }
  body { font: 12px/1.5 -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
         color: #0f172a; margin: 0; padding: 24px; background: #fff; }
  .wrap { max-width: 860px; margin: 0 auto; position: relative; }
  header { display: flex; justify-content: space-between; align-items: flex-start;
           border-bottom: 3px solid #0f172a; padding-bottom: 16px; }
  .brand { font-size: 22px; font-weight: 800; letter-spacing: -.02em; }
  .brand span { color: #0d9488; }
  .doctype { text-align: right; }
  .doctype h1 { margin: 0; font-size: 20px; letter-spacing: .08em; text-transform: uppercase; }
  .doctype .no { font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
                 font-size: 14px; font-weight: 700; margin-top: 4px; }
  .parties { display: flex; gap: 32px; margin-top: 20px; }
  .party { flex: 1; }
  .label { font-size: 9px; font-weight: 800; letter-spacing: .16em; text-transform: uppercase; color: #64748b; }
  .party .name { font-weight: 700; margin-top: 4px; }
  .party .line { color: #475569; }
  .meta { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin-top: 20px;
          border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px; }
  .meta div .v { font-weight: 600; margin-top: 2px; }
  table { width: 100%; border-collapse: collapse; margin-top: 20px; }
  thead th { font-size: 9px; font-weight: 800; letter-spacing: .1em; text-transform: uppercase;
             color: #475569; text-align: left; border-bottom: 2px solid #cbd5e1; padding: 8px 6px; }
  tbody td { padding: 8px 6px; border-bottom: 1px solid #f1f5f9; vertical-align: top; }
  .num { text-align: right; white-space: nowrap; }
  thead th.num { text-align: right; }
  .strong { font-weight: 700; }
  .desc { font-weight: 500; }
  .sub { font-size: 10px; color: #94a3b8; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
  .totals { margin-top: 18px; margin-left: auto; width: 300px; }
  .totals div { display: flex; justify-content: space-between; padding: 4px 0; }
  .totals .grand { border-top: 2px solid #0f172a; margin-top: 6px; padding-top: 8px;
                   font-size: 16px; font-weight: 800; }
  .section { margin-top: 26px; }
  .section h2 { font-size: 10px; font-weight: 800; letter-spacing: .16em;
                text-transform: uppercase; color: #64748b; margin: 0 0 8px; }
  .notice { margin-top: 14px; padding: 10px 12px; border: 1px solid #fcd34d;
            background: #fffbeb; border-radius: 8px; color: #92400e; font-weight: 500; }
  footer { margin-top: 32px; padding-top: 14px; border-top: 1px solid #e2e8f0;
           font-size: 10px; color: #94a3b8; }
  .draft-stamp { position: absolute; top: 140px; left: 50%; transform: translateX(-50%) rotate(-14deg);
                 font-size: 62px; font-weight: 900; letter-spacing: .06em; color: rgba(15,23,42,.08);
                 pointer-events: none; white-space: nowrap; }
  .draft-stamp.void { color: rgba(220,38,38,.12); }
  @media print { body { padding: 0; } .noprint { display: none !important; } }
  .noprint { position: fixed; top: 12px; right: 12px; }
  .noprint button { font: inherit; padding: 8px 14px; border-radius: 8px; border: 1px solid #0f172a;
                    background: #0f172a; color: #fff; cursor: pointer; font-weight: 600; }
</style></head>
<body>
<div class="noprint"><button onclick="window.print()">Print / Save as PDF</button></div>
<div class="wrap">
  ${draftBanner}${voidBanner}
  <header>
    <div>
      <div class="brand">Ops<span>Trax</span></div>
      <div class="label" style="margin-top:6px">${esc(inv.sellerLegalName ?? "OpsTrax")}</div>
    </div>
    <div class="doctype">
      <h1>${isCredit ? "Credit Note" : `${taxLabel} Invoice`}</h1>
      <div class="no">${esc(inv.invoiceNumber)}</div>
      ${inv.creditNoteOf ? `<div class="label" style="margin-top:4px">against invoice #${esc(inv.creditNoteOf)}</div>` : ""}
    </div>
  </header>

  <div class="parties">
    <div class="party">
      <div class="label">Supplier</div>
      <div class="name">${esc(inv.sellerLegalName ?? "OpsTrax")}</div>
      <div class="line">${inv.sellerTaxNo ? `${esc(taxLabel)} No. ${esc(inv.sellerTaxNo)}` : `No ${esc(taxLabel)} registration on file`}</div>
    </div>
    <div class="party">
      <div class="label">Customer</div>
      <div class="name">${esc(inv.buyerLegalName ?? inv.tenant)}</div>
      <div class="line">${inv.buyerTaxNo ? `${esc(taxLabel)} No. ${esc(inv.buyerTaxNo)}` : "No tax registration number recorded"}</div>
      <div class="line">${esc(inv.placeOfSupply ?? "")}</div>
    </div>
  </div>

  <div class="meta">
    <div><div class="label">Issue date</div><div class="v">${esc(date(inv.issuedAt))}</div></div>
    <div><div class="label">Due date</div><div class="v">${esc(date(inv.dueAt))}</div></div>
    <div><div class="label">Billing period</div><div class="v">${esc(date(inv.periodStart))} → ${esc(date(inv.periodEnd))}</div></div>
    <div><div class="label">Currency</div><div class="v">${esc(cur)}</div></div>
    <div><div class="label">Place of supply</div><div class="v">${esc(inv.placeOfSupply ?? "—")}</div></div>
    <div><div class="label">Tax treatment</div><div class="v">${esc(treatment.replace(/_/g, " "))}</div></div>
    <div><div class="label">Payment terms</div><div class="v">${esc(Number(inv.paymentTermsDays ?? 0))} days</div></div>
    <div><div class="label">Status</div><div class="v">${esc(String(inv.status ?? "").toUpperCase())}</div></div>
  </div>

  <table>
    <thead><tr>
      <th class="num">#</th><th>Description</th><th class="num">Qty</th><th class="num">Unit price</th>
      <th class="num">Net</th><th class="num">Code</th><th class="num">Rate</th>
      <th class="num">${esc(taxLabel)}</th><th class="num">Total</th>
    </tr></thead>
    <tbody>${lineRows}</tbody>
  </table>

  <div class="totals">
    <div><span>Net total</span><span>${esc(formatAmount(Number(inv.subtotalCents ?? 0), cur))}</span></div>
    <div><span>${esc(taxLabel)}</span><span>${esc(formatAmount(Number(inv.taxTotalCents ?? 0), cur))}</span></div>
    <div class="grand"><span>Amount ${isCredit ? "credited" : "due"}</span><span>${esc(formatAmount(Number(inv.totalCents ?? 0), cur))}</span></div>
  </div>

  ${breakdown.length > 0 ? `
  <div class="section">
    <h2>${esc(taxLabel)} summary</h2>
    <table>
      <thead><tr><th>Code</th><th>Category</th><th class="num">Rate</th><th class="num">Taxable amount</th><th class="num">${esc(taxLabel)}</th></tr></thead>
      <tbody>${breakdownRows}</tbody>
    </table>
  </div>` : ""}

  ${treatmentNote}
  ${inv.notes ? `<p class="notice" style="border-color:#e2e8f0;background:#f8fafc;color:#475569">${esc(inv.notes)}</p>` : ""}
  ${inv.voidReason ? `<p class="notice" style="border-color:#fecaca;background:#fef2f2;color:#991b1b">Voided: ${esc(inv.voidReason)}</p>` : ""}

  <footer>
    ${esc(inv.invoiceNumber)} · issued by ${esc(inv.issuedBy ?? "—")} ·
    ${isCredit ? "Credit note" : "Invoice"} generated by OpsTrax Platform Billing.
    ${treatment === "standard" ? `${esc(taxLabel)} charged at the rate in force in ${esc(inv.placeOfSupply ?? "the place of supply")}.` : ""}
  </footer>
</div>
</body></html>`;

  const win = window.open("", "_blank", "width=900,height=1100");
  if (!win) return;
  win.document.write(html);
  win.document.close();
}
