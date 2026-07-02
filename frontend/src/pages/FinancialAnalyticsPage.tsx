import { useState } from "react";
import { chart } from "@/styles/tokens";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer } from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, EmptyState, ErrorState, KpiCard, DataTable } from "@/components/ui";
import type { AnyRecord } from "@/types";

// Present the real revenue-spine payment status as a human label the canonical StatusBadge
// understands. issued_invoices.paymentStatus is 'paid' | 'partial' | 'unpaid'; an unpaid
// invoice past its due date is surfaced as Overdue.
function invoiceDisplayStatus(paymentStatus: string, balanceDue: number, dueAt: string): string {
  const ps = String(paymentStatus).toLowerCase();
  if (ps === "paid" || balanceDue <= 0) return "Paid";
  if (dueAt && new Date(dueAt).getTime() < Date.now()) return "Overdue";
  if (ps === "partial") return "Partial";
  return "Sent";
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
          currency: r.currency ?? "USD",
        };
      })
    ),
  payments: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/payments")).then((rows) =>
      rows.map((r) => ({
        ...r,
        paymentNumber: r.paymentNumber ?? r.payment_number ?? String(r.id),
        customerName: r.customerName ?? r.customer_name ?? "",
        amount: Number(r.amount ?? 0),
        paymentMethod: r.paymentMethod ?? r.payment_method ?? r.tags ?? "Bank Transfer",
        paymentDate: r.paymentDate ?? r.payment_date ?? "",
        invoiceRef: r.invoiceRef ?? r.invoice_ref ?? "",
      }))
    ),
  // Real AR aging buckets (built + tested in the Finance module).
  arAging: () => unwrap<AnyRecord>(apiClient.get("/api/finance/ar-aging")),
  profitability: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/profitability")).then((rows) =>
      rows.map((r) => ({
        ...r,
        entityName: r.entityName ?? r.entity_name ?? String(r.id),
        entityType: r.entityType ?? r.entity_type ?? "Customer",
        revenueEstimate: Number(r.revenueEstimate ?? r.revenue_estimate ?? 0),
        totalCost: Number(r.totalCost ?? r.total_cost ?? 0),
        grossMargin: Number(r.grossMargin ?? r.gross_margin ?? 0),
        grossMarginPercent: Number(r.grossMarginPercent ?? r.gross_margin_percent ?? 0),
        riskScore: Number(r.riskScore ?? r.risk_score ?? 0),
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
    status === "Received" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Pending" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-red-50 border-red-200 text-red-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function MarginBadge({ pct }: { pct: number }) {
  const cls = pct >= 28 ? "bg-teal-50 border-teal-200 text-teal-700" : pct >= 18 ? "bg-amber-50 border-amber-200 text-amber-700" : "bg-red-50 border-red-200 text-red-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{pct.toFixed(1)}%</span>;
}

// ── Tabs ──────────────────────────────────────────────────────────────────────

function money(n: number, currency = "USD"): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency, maximumFractionDigits: 2 }).format(n);
}

function InvoicesTab() {
  const q = useQuery({ queryKey: ["issued-invoices"], queryFn: financialApi.invoices });
  const rows = (q.data ?? []) as AnyRecord[];
  const outstandingBalance = rows.reduce((s, r) => s + Number(r.balanceDue ?? 0), 0);
  const overdue = rows.filter((r) => String(r.paymentStatus) === "Overdue").length;
  const totalValue = rows.reduce((s, r) => s + Number(r.total ?? 0), 0);
  const paidCount = rows.filter((r) => String(r.paymentStatus) === "Paid").length;
  const currency = String(rows[0]?.currency ?? "USD");
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load invoices."} />;

  // DataTable renders a `status` column via the canonical StatusBadge and $-prefixed
  // strings as currency — so we pre-format amounts and expose a `status` column.
  const tableRows = rows.map((r) => ({
    "Invoice #": String(r.invoiceNumber ?? "—"),
    Customer: String(r.customerName ?? "—"),
    Total: money(Number(r.total ?? 0), String(r.currency ?? currency)),
    Paid: money(Number(r.amountPaid ?? 0), String(r.currency ?? currency)),
    "Balance Due": money(Number(r.balanceDue ?? 0), String(r.currency ?? currency)),
    status: String(r.paymentStatus ?? "—"),
    "Due Date": String(r.dueDate || "—"),
    Aging: Number(r.agingDays) > 0 ? `${Number(r.agingDays)}d` : "—",
  }));

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        <KpiCard label="Total Invoices" value={rows.length} />
        <KpiCard label="Outstanding" value={money(outstandingBalance, currency)} status="Review" />
        <KpiCard label="Overdue" value={overdue} status={overdue > 0 ? "Overdue" : "Healthy"} />
        <KpiCard label="Paid" value={paidCount} status="Healthy" />
        <KpiCard label="Total Value" value={money(totalValue, currency)} />
      </div>
      <div className="panel grid gap-3 md:grid-cols-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">AR posture</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">
            {overdue > 0 ? "Collection attention required" : "Collections are within expected range"}
          </p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Billing confidence</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">
            {paidCount > 0 ? `${paidCount} invoice${paidCount === 1 ? "" : "s"} fully collected` : "No invoices collected yet"}
          </p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Live data policy</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Sourced from the live revenue spine (issued_invoices).</p>
        </div>
      </div>
      {rows.length === 0 ? <EmptyState title="No invoices found" /> : (
        <DataTable
          rows={tableRows}
          columns={["Invoice #", "Customer", "Total", "Paid", "Balance Due", "status", "Due Date", "Aging"]}
        />
      )}
    </div>
  );
}

function ArAgingTab() {
  const q = useQuery({ queryKey: ["ar-aging"], queryFn: financialApi.arAging });
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load AR aging."} />;
  const d = (q.data ?? {}) as AnyRecord;
  const currency = String(d.currency ?? "USD");
  const buckets: { label: string; key: string; status?: string }[] = [
    { label: "Current",     key: "current" },
    { label: "1–30 days",   key: "days1To30" },
    { label: "31–60 days",  key: "days31To60", status: "Review" },
    { label: "61–90 days",  key: "days61To90", status: "Review" },
    { label: "90+ days",    key: "days90Plus", status: "Overdue" },
  ];
  const customers = (d.customers ?? []) as AnyRecord[];
  const custRows = customers.map((c) => ({
    Customer: String(c.customerName ?? "—"),
    Current: money(Number(c.current ?? 0), currency),
    "1–30": money(Number(c.days1To30 ?? 0), currency),
    "31–60": money(Number(c.days31To60 ?? 0), currency),
    "61–90": money(Number(c.days61To90 ?? 0), currency),
    "90+": money(Number(c.days90Plus ?? 0), currency),
    "Total Outstanding": money(Number(c.totalOutstanding ?? 0), currency),
  }));

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {buckets.map((b) => (
          <KpiCard key={b.key} label={b.label} value={money(Number(d[b.key] ?? 0), currency)} status={b.status} />
        ))}
        <KpiCard label="Total Outstanding" value={money(Number(d.totalOutstanding ?? 0), currency)} status="Review" />
      </div>
      <div className="panel grid gap-3 md:grid-cols-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Aging basis</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Outstanding balance bucketed by days past due.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Collections risk</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">
            {Number(d.days90Plus ?? 0) > 0 ? `${money(Number(d.days90Plus), currency)} is 90+ days overdue` : "No balances past 90 days"}
          </p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Live data policy</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Sourced from the live revenue spine (issued_invoices).</p>
        </div>
      </div>
      {custRows.length === 0 ? <EmptyState title="No outstanding receivables" /> : (
        <DataTable
          rows={custRows}
          columns={["Customer", "Current", "1–30", "31–60", "61–90", "90+", "Total Outstanding"]}
        />
      )}
    </div>
  );
}

function PaymentsTab() {
  const q = useQuery({ queryKey: ["payments"], queryFn: financialApi.payments });
  const rows = (q.data ?? []) as AnyRecord[];
  const received = rows.filter((r) => r.status === "Received").reduce((s, r) => s + Number(r.amount ?? 0), 0);
  const pending = rows.filter((r) => r.status !== "Received").reduce((s, r) => s + Number(r.amount ?? 0), 0);
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load payments."} />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Payments", val: rows.length },
          { label: "Collected", val: `$${received.toLocaleString()}`, accent: "text-teal-600" },
          { label: "Pending", val: `$${pending.toLocaleString()}`, accent: "text-amber-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No payments found" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Payment #", "Customer", "Invoice Ref", "Amount", "Currency", "Method", "Date", "Status"].map((h) => (
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
                    <td className="px-4 py-3 font-semibold text-slate-900">{Number(r.amount ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.currency ?? "USD")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.paymentMethod ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.paymentDate ?? "—")}</td>
                    <td className="px-4 py-3"><PaymentStatusBadge status={String(r.status ?? "Pending")} /></td>
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
  const totalRev = rows.reduce((s, r) => s + Number(r.revenueEstimate ?? 0), 0);
  const totalCost = rows.reduce((s, r) => s + Number(r.totalCost ?? 0), 0);
  const totalMargin = totalRev - totalCost;
  const avgMarginPct = rows.length > 0 ? rows.reduce((s, r) => s + Number(r.grossMarginPercent ?? 0), 0) / rows.length : 0;
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load profitability data."} />;
  const chartData = rows.slice(0, 8).map((r) => ({
    name: String(r.entityName ?? "").split(" ")[0],
    margin: Number(r.grossMarginPercent ?? 0),
    revenue: Math.round(Number(r.revenueEstimate ?? 0) / 1000),
  }));
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Revenue",  val: `$${totalRev.toLocaleString()}`,    accent: "text-teal-600" },
          { label: "Total Cost",     val: `$${totalCost.toLocaleString()}`,   accent: "text-slate-700" },
          { label: "Gross Margin",   val: `$${totalMargin.toLocaleString()}`, accent: totalMargin > 0 ? "text-teal-600" : "text-red-600" },
          { label: "Avg Margin %",   val: `${avgMarginPct.toFixed(1)}%`,      accent: avgMarginPct >= 25 ? "text-teal-600" : "text-amber-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-36">
            <span className={`text-xl font-bold ${accent}`}>{val}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      <div className="panel grid gap-3 md:grid-cols-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Finance story</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Billing confidence is tied to live invoice and payment signals.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Actionable view</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Margin and risk are shown per customer without auto-issuing invoices.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Data policy</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">No fabricated finance rows are used here.</p>
        </div>
      </div>
      {chartData.length > 0 && (
        <div className="panel">
          <p className="text-sm font-semibold text-slate-700 mb-3">Margin % by Customer</p>
          <ResponsiveContainer width="100%" height={180}>
            <BarChart data={chartData} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
              <XAxis dataKey="name" tick={{ fontSize: 11 }} />
              <YAxis unit="%" tick={{ fontSize: 11 }} />
              <Tooltip formatter={(val) => [`${String(val)}%`, "Margin"]} />
              <Bar dataKey="margin" fill={chart.teal600} radius={[3, 3, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
      {rows.length === 0 ? <EmptyState title="No profitability data" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Entity", "Type", "Revenue", "Total Cost", "Gross Margin", "Margin %", "Risk Score"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.entityName ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.entityType ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700">${Number(r.revenueEstimate ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-slate-600">${Number(r.totalCost ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 font-semibold text-teal-700">${Number(r.grossMargin ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3"><MarginBadge pct={Number(r.grossMarginPercent ?? 0)} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{Number(r.riskScore ?? 0).toFixed(0)}</td>
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

// ── Route → tab ───────────────────────────────────────────────────────────────

const ROUTE_TAB: Record<string, Tab> = {
  "/invoices": "invoices",
  "/payments": "payments",
  "/profitability": "profitability",
  "/ar-aging": "ar-aging",
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
  payments:      "Payment collections — received, pending and unapplied cash by customer",
  profitability: "Revenue vs. cost by customer and route — gross margin, margin % and risk score",
};

// ── Main page ─────────────────────────────────────────────────────────────────

export function FinancialAnalyticsPage() {
  const { pathname } = useLocation();
  const defaultTab = ROUTE_TAB[pathname] ?? "invoices";
  const [tab, setTab] = useState<Tab>(defaultTab);

  const exportFns: Record<Tab, () => void> = {
    invoices: async () => exportCsv("invoices", await loadInvoiceRows()),
    "ar-aging": async () => exportCsv("ar-aging", ((await financialApi.arAging()).customers as AnyRecord[]) ?? []),
    payments: async () => exportCsv("payments", await loadPaymentRows()),
    profitability: async () => exportCsv("profitability", await loadProfitabilityRows()),
  };

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{TITLES[tab]}</h1>
          <p className="text-sm text-slate-500 mt-0.5">{DESCRIPTIONS[tab]}</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={() => void exportFns[tab]()}>Export CSV</button>
      </div>

      <div className="panel flex gap-1 p-1.5">
        {TABS.map((t) => (
          <button key={t.key} type="button" onClick={() => setTab(t.key)}
            className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
              tab === t.key ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"
            }`}>{t.label}</button>
        ))}
      </div>

      {tab === "invoices"      && <InvoicesTab />}
      {tab === "ar-aging"      && <ArAgingTab />}
      {tab === "payments"      && <PaymentsTab />}
      {tab === "profitability" && <ProfitabilityTab />}
    </div>
  );
}
