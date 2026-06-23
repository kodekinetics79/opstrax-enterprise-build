import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer } from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, EmptyState } from "@/components/ui";
import { invoices as seedInvoices, customers as seedCustomers } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed ─────────────────────────────────────────────────────────────────────

function buildInvoiceSeed(): AnyRecord[] {
  const extra: AnyRecord[] = [
    { invoiceId: "INV-8803", customer: "Al Noor Pharma Distribution", shipmentBooking: "SHP-6205", invoiceDate: "2026-05-28", dueDate: "2026-06-15", amount: 14500, tax: 725, total: 15225, currency: "SAR", paymentStatus: "Sent", agingDays: 3 },
    { invoiceId: "INV-8804", customer: "Qatar Cold Foods", shipmentBooking: "SHP-6206", invoiceDate: "2026-05-20", dueDate: "2026-06-05", amount: 9800, tax: 490, total: 10290, currency: "USD", paymentStatus: "Overdue", agingDays: 13 },
    { invoiceId: "INV-8805", customer: "Riyadh Automotive Parts", shipmentBooking: "SHP-6207", invoiceDate: "2026-06-01", dueDate: "2026-07-01", amount: 3200, tax: 160, total: 3360, currency: "SAR", paymentStatus: "Paid", agingDays: 0 },
    { invoiceId: "INV-8806", customer: "Abu Dhabi Retail Group", shipmentBooking: "SHP-6208", invoiceDate: "2026-06-05", dueDate: "2026-07-05", amount: 5600, tax: 280, total: 5880, currency: "AED", paymentStatus: "Draft", agingDays: 0 },
  ];
  return [
    ...(seedInvoices as AnyRecord[]).map((inv) => ({
      ...inv, invoiceId: inv.invoiceId, customer: inv.customer,
      invoiceNumber: String(inv.invoiceId), customerName: String(inv.customer),
      amount: Number(inv.amount), total: Number(inv.total ?? inv.amount),
      paymentStatus: String(inv.paymentStatus ?? "Draft"),
      dueDate: String(inv.dueDate ?? ""), agingDays: 0,
    })),
    ...extra.map((e) => ({ ...e, invoiceNumber: String(e.invoiceId), customerName: String(e.customer) })),
  ];
}

function buildPaymentSeed(): AnyRecord[] {
  const methods = ["Bank Transfer", "Card", "Cheque", "Cash"];
  return buildInvoiceSeed()
    .filter((_, i) => i % 2 === 0)
    .map((inv, i) => ({
      id: i + 1,
      paymentNumber: `PAY-${9100 + i}`,
      customerName: String(inv.customerName ?? ""),
      invoiceRef: String(inv.invoiceNumber ?? ""),
      amount: Number(inv.amount ?? 0) * 0.95,
      currency: String(inv.currency ?? "USD"),
      paymentMethod: methods[i % 4],
      paymentDate: String(inv.invoiceDate ?? "2026-05-26"),
      status: (["Received", "Received", "Pending", "Received"] as const)[i % 4],
      notes: "",
    }));
}

function buildProfitabilitySeed(): AnyRecord[] {
  return (seedCustomers as AnyRecord[]).map((c, i) => ({
    id: i + 1,
    entityType: "Customer",
    entityName: String(c.name ?? ""),
    revenueEstimate: [120000, 280000, 95000, 340000, 175000][i % 5],
    totalCost:       [84000,  196000, 72000, 238000, 122500][i % 5],
    grossMargin:     [36000,  84000,  23000, 102000,  52500][i % 5],
    grossMarginPercent: [30, 30, 24, 30, 30][i % 5],
    riskScore: [22, 15, 38, 10, 28][i % 5],
    status: "Active",
  }));
}

const financialApi = {
  invoices: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/invoices")).then((rows) =>
      rows.map((r) => ({
        ...r,
        invoiceNumber: r.invoiceNumber ?? r.invoice_number ?? String(r.id),
        customerName: r.customerName ?? r.customer_name ?? r.title ?? "",
        paymentStatus: r.paymentStatus ?? r.payment_status ?? r.status ?? "Draft",
        dueDate: r.dueDate ?? r.due_date ?? "",
        agingDays: Number(r.agingDays ?? r.aging_days ?? 0),
        amount: Number(r.amount ?? 0),
        total: Number(r.totalAmount ?? r.total_amount ?? r.total ?? r.amount ?? 0),
      }))
    ),
    () => buildInvoiceSeed()
  ),
  payments: () => withFallback(
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
    () => buildPaymentSeed()
  ),
  profitability: () => withFallback(
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
    () => buildProfitabilitySeed()
  ),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function InvoiceStatusBadge({ status }: { status: string }) {
  const cls =
    status === "Paid" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Overdue" ? "bg-red-50 border-red-200 text-red-700" :
    status === "Sent" || status === "Ready" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "Disputed" ? "bg-orange-50 border-orange-200 text-orange-700" :
    "bg-slate-50 border-slate-200 text-slate-500";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

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

function InvoicesTab() {
  const q = useQuery({ queryKey: ["invoices"], queryFn: financialApi.invoices });
  const rows = (q.data ?? []) as AnyRecord[];
  const outstanding = rows.filter((r) => !["Paid"].includes(String(r.paymentStatus))).length;
  const overdue = rows.filter((r) => String(r.paymentStatus) === "Overdue").length;
  const totalValue = rows.reduce((s, r) => s + Number(r.total ?? r.amount ?? 0), 0);
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Invoices", val: rows.length },
          { label: "Outstanding", val: outstanding, accent: "text-amber-600" },
          { label: "Overdue", val: overdue, accent: "text-red-600" },
          { label: "Total Value", val: `$${totalValue.toLocaleString()}`, accent: "text-teal-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No invoices found" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Invoice #", "Customer", "Shipment", "Amount", "Total", "Currency", "Status", "Due Date", "Aging"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? r.invoiceId ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.invoiceNumber ?? r.invoiceId ?? "--")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(r.customerName ?? r.customer ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.shipmentBooking ?? r.shipment ?? "—")}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{Number(r.amount ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 font-semibold text-slate-900">{Number(r.total ?? r.amount ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.currency ?? "USD")}</td>
                    <td className="px-4 py-3"><InvoiceStatusBadge status={String(r.paymentStatus ?? r.status ?? "Draft")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.dueDate ?? "—")}</td>
                    <td className="px-4 py-3 text-xs">
                      {Number(r.agingDays) > 0 ? (
                        <span className={Number(r.agingDays) > 7 ? "text-red-600 font-medium" : "text-amber-600"}>{Number(r.agingDays)}d</span>
                      ) : <span className="text-slate-400">—</span>}
                    </td>
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

function PaymentsTab() {
  const q = useQuery({ queryKey: ["payments"], queryFn: financialApi.payments });
  const rows = (q.data ?? []) as AnyRecord[];
  const received = rows.filter((r) => r.status === "Received").reduce((s, r) => s + Number(r.amount ?? 0), 0);
  const pending = rows.filter((r) => r.status !== "Received").reduce((s, r) => s + Number(r.amount ?? 0), 0);
  if (q.isLoading) return <LoadingState />;
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
      {chartData.length > 0 && (
        <div className="panel">
          <p className="text-sm font-semibold text-slate-700 mb-3">Margin % by Customer</p>
          <ResponsiveContainer width="100%" height={180}>
            <BarChart data={chartData} margin={{ top: 4, right: 8, bottom: 4, left: 0 }}>
              <XAxis dataKey="name" tick={{ fontSize: 11 }} />
              <YAxis unit="%" tick={{ fontSize: 11 }} />
              <Tooltip formatter={(val) => [`${String(val)}%`, "Margin"]} />
              <Bar dataKey="margin" fill="#0d9488" radius={[3, 3, 0, 0]} />
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

const ROUTE_TAB: Record<string, "invoices" | "payments" | "profitability"> = {
  "/invoices": "invoices",
  "/payments": "payments",
  "/profitability": "profitability",
};

type Tab = "invoices" | "payments" | "profitability";

const TABS: { key: Tab; label: string }[] = [
  { key: "invoices",      label: "Invoices" },
  { key: "payments",      label: "Payments" },
  { key: "profitability", label: "Profitability" },
];

const TITLES: Record<Tab, string> = {
  invoices:      "Invoices",
  payments:      "Payments",
  profitability: "Profitability",
};

const DESCRIPTIONS: Record<Tab, string> = {
  invoices:      "Invoice lifecycle — draft, sent, paid, overdue and disputed with aging tracking",
  payments:      "Payment collections — received, pending and unapplied cash by customer",
  profitability: "Revenue vs. cost by customer and route — gross margin, margin % and risk score",
};

// ── Main page ─────────────────────────────────────────────────────────────────

export function FinancialAnalyticsPage() {
  const { pathname } = useLocation();
  const defaultTab = ROUTE_TAB[pathname] ?? "invoices";
  const [tab, setTab] = useState<Tab>(defaultTab);

  const exportFns: Record<Tab, () => void> = {
    invoices: () => exportCsv("invoices", buildInvoiceSeed()),
    payments: () => exportCsv("payments", buildPaymentSeed()),
    profitability: () => exportCsv("profitability", buildProfitabilitySeed()),
  };

  return (
    <div className="flex flex-col gap-6 py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{TITLES[tab]}</h1>
          <p className="text-sm text-slate-500 mt-0.5">{DESCRIPTIONS[tab]}</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={exportFns[tab]}>Export CSV</button>
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
      {tab === "payments"      && <PaymentsTab />}
      {tab === "profitability" && <ProfitabilityTab />}
    </div>
  );
}
