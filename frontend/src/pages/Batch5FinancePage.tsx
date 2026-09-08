import { FormEvent, ReactNode, useMemo, useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip as ChartTooltip, XAxis, YAxis } from "recharts";
import {
  Download, Fuel, Landmark,
  PenTool, Plus, TrendingDown, Truck, WalletCards, X, Zap,
} from "lucide-react";
import { AiInsightCard, DataTable, ErrorState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv, labelize } from "@/components/ui";
import {
  useCarrierDetail, useCarriers, useCarriersSummary,
  useContractDetail, useContracts, useContractsSummary,
  useCostLeakageItemDetail, useCostLeakageItems, useCostLeakageSummary,
  useCostMarginJobDetail, useCostMarginJobs, useCostMarginSummary,
  useExpenseDetail, useExpenses, useExpensesSummary,
  useFuelSummary, useFuelTransaction, useFuelTransactions,
} from "@/hooks/useBatch5";
import { carriersApi } from "@/services/carriersApi";
import { contractsApi } from "@/services/contractsApi";
import { costLeakageApi } from "@/services/costLeakageApi";
import { expensesApi } from "@/services/expensesApi";
import { fuelApi } from "@/services/fuelApi";
import type { AnyRecord } from "@/types";
import { apiErrorMessage } from "@/utils/apiErrorMessage";

type Kind = "fuel" | "expenses" | "contracts" | "carriers" | "cost-margin" | "cost-leakage";

/* ── Per-module tab definitions ── */
const MODULE_TABS: Partial<Record<Kind, Array<{ label: string; columns: string[] }>>> = {
  fuel: [
    { label: "Transactions",  columns: ["transactionNumber","recordOrigin","vehicleCode","driverName","fuelType","quantity","unit","unitPrice","totalCost","currency","odometer","fuelStation","paymentMethod","anomalyStatus","fuelDate"] },
    { label: "Idling Events", columns: ["eventNumber","recordOrigin","vehicleCode","driverName","locationDescription","durationMinutes","estimatedCost","currency","costEvidenceStatus","thresholdStatus","riskScore"] },
    { label: "Anomaly Findings", columns: ["anomalyType","recordOrigin","severity","description","estimatedLoss","currency","amountEvidenceStatus","status"] },
  ],
};

/* ── Per-module filter options ── */
const FILTER_OPTIONS: Record<Kind, string[]> = {
  "fuel":          ["All","Normal","Anomaly Detected","Under Review","Excessive","Warning"],
  "expenses":      ["All","Pending","Approved","Rejected","Missing","High"],
  "contracts":     ["All","Active","Expiring Soon","Expired","High","Medium"],
  "carriers":      ["All","Active","Pending","Suspended","Compliant","Non-Compliant","At Risk"],
  "cost-margin":   ["All","Calculated","Cost evidence unavailable","Issued revenue unavailable"],
  "cost-leakage":  ["All","Open","Acknowledged","In Progress","Critical","High"],
};

const configs = {
  fuel: {
    queryKey: "fuel", eyebrow: "Fuel & Idling Evidence", title: "Recorded fuel and idling evidence", icon: <Fuel />,
    description: "Persisted fuel transactions and idling events with their source, currency, unit, and estimate status shown explicitly.",
    useRows: useFuelTransactions, useSummary: useFuelSummary, useDetail: useFuelTransaction,
    api: { create: fuelApi.createTransaction, update: (id: string | number, p: AnyRecord) => fuelApi.updateTransaction(id, p) },
    createLabel: "Record Transaction",
    kpis: [["Evidence-Qualified Transactions","fuelTransactions"],["Open Runtime Findings","openAnomalies"],["High Idle Vehicles","highIdleVehicles"],["Idling Events Today","idlingEventsToday"],["Legacy Fuel Origin Unverified","legacyUnverifiedTransactions"]],
    columns: ["transactionNumber","recordOrigin","vehicleCode","driverName","fuelType","quantity","unit","unitPrice","totalCost","currency","odometer","fuelStation","paymentMethod","anomalyStatus","fuelDate"],
    fields: [["transactionNumber","Transaction #"],["vehicleId","Vehicle ID"],["driverId","Driver ID"],["jobId","Job ID"],["fuelDate","Fuel Date"],["fuelType","Fuel Type"],["quantity","Quantity"],["unit","Unit (Gallons or Liters)"],["unitPrice","Unit Price"],["currency","Currency"],["odometer","Odometer"],["fuelStation","Fuel Station"],["paymentMethod","Payment Method"],["region","Region"],["notes","Notes"]],
    actions: ["reviewAnomaly"],
    sections: [["Recorded Anomaly Findings","anomalies",["anomalyType","severity","estimatedLoss","currency","amountEvidenceStatus","status","createdAt"]]] as [string,string,string[]][],
  },
  expenses: {
    queryKey: "expenses", eyebrow: "Expenses", title: "Operating expense register and approval workflow", icon: <WalletCards />,
    description: "Persisted operating expenses, receipt tracking and explicit approval decisions. Monetary totals stay separated by their recorded currency.",
    useRows: useExpenses, useSummary: useExpensesSummary, useDetail: useExpenseDetail,
    api: { create: expensesApi.create, update: (id: string | number, p: AnyRecord) => expensesApi.update(id, p) },
    createLabel: "Create Expense",
    kpis: [["Total Records","total"],["Pending Approval","pendingApproval"],["Approved","approvedExpenses"],["Rejected","rejectedExpenses"],["Missing Receipts","missingReceipts"]],
    columns: ["expenseNumber","recordOrigin","categoryName","amount","currency","approvalStatus","receiptStatus","vendorName","vehicleCode","driverName","expenseDate","recordAttention"],
    fields: [["categoryName","Category"],["amount","Amount"],["currency","Currency"],["expenseDate","Expense Date"],["vehicleId","Vehicle ID"],["driverId","Driver ID"],["jobId","Job ID"],["customerId","Customer ID"],["vendorName","Vendor Name"],["receiptStatus","Receipt Status"],["notes","Notes"]],
    actions: ["approve","reject"],
    sections: [] as [string,string,string[]][],
  },
  contracts: {
    queryKey: "contracts", eyebrow: "Contracts / Rates", title: "Recorded contract terms and rate structures", icon: <Landmark />,
    description: "Persisted customer and carrier contract terms, rate structures, currencies, fuel surcharge configuration and date-based renewal workflows. Generated demo agreements are excluded.",
    useRows: useContracts, useSummary: useContractsSummary, useDetail: useContractDetail,
    api: { create: contractsApi.create, update: (id: string | number, p: AnyRecord) => contractsApi.update(id, p) },
    createLabel: "Create Contract",
    kpis: [["Active","activeContracts"],["Expiring Soon","expiringSoon"],["Expired","expiredContracts"],["Customers Covered","customersCovered"],["Carrier Agreements","carrierAgreements"],["Renewal Queue","renewalQueue"],["Legacy Origin Unverified","legacyOriginUnverified"],["Total","total"]],
    columns: ["contractNumber","recordOrigin","contractType","rateType","status","customerName","carrierName","baseRate","currency","effectiveDate","expiryDate","recommendedAction"],
    fields: [["contractNumber","Contract #"],["title","Title"],["customerId","Customer ID"],["carrierId","Carrier ID"],["contractType","Contract Type"],["rateType","Rate Type"],["baseRate","Base Rate"],["currency","Currency"],["effectiveDate","Effective Date"],["expiryDate","Expiry Date"],["fuelSurchargeEnabled","Fuel Surcharge?"],["fuelSurchargePercent","Surcharge %"],["slaTerms","SLA Terms"],["notes","Notes"]],
    actions: ["activate","expire"],
    sections: [["Recorded Contract Rates","rates",["rateCode","recordOrigin","rateType","baseRate","currency","effectiveDate","status"]]] as [string,string,string[]][],
  },
  carriers: {
    queryKey: "carriers", eyebrow: "Carrier Management", title: "Partner carrier registry and performance", icon: <Truck />,
    description: "Partner carriers, compliance status, insurance tracking, performance scoring, cost governance and carrier document management.",
    useRows: useCarriers, useSummary: useCarriersSummary, useDetail: useCarrierDetail,
    api: { create: carriersApi.create, update: (id: string | number, p: AnyRecord) => carriersApi.update(id, p) },
    createLabel: "Add Carrier",
    kpis: [["Active Carriers","activeCarriers"],["Compliance Risk","complianceRiskCarriers"],["Insurance Expiring","insuranceExpiring"],["Avg Performance","averageCarrierScore"],["On-Time %","onTimePerformance"],["Preferred","preferredCarriers"],["Docs Missing","documentsMissing"],["Total","total"]],
    columns: ["carrierNumber","name","region","complianceStatus","contractStatus","onTimePercent","safetyScore","performanceScore","riskScore","insuranceExpiry","status","recommendedAction"],
    fields: [["name","Carrier Name"],["mcNumber","MC Number"],["contactName","Contact Name"],["phone","Phone"],["email","Email"],["region","Region"],["status","Status"],["complianceStatus","Compliance Status"],["insuranceExpiry","Insurance Expiry"],["contractStatus","Contract Status"],["notes","Notes"]],
    actions: ["setStatus"],
    sections: [
      ["Performance History","performance",["periodStart","periodEnd","jobsHandled","onTimePercent","incidentCount","performanceScore"]],
      ["Documents","documents",["documentType","documentNumber","status","expiryDate"]],
    ] as [string,string,string[]][],
  },
  "cost-margin": {
    queryKey: "cost-margin", eyebrow: "Cost & Margin Evidence", title: "Recorded job cost and margin evidence", icon: <Zap />,
    description: "Job margin calculated from issued invoices and approved, non-demo expenses recorded for the same job and currency.",
    useRows: useCostMarginJobs, useSummary: useCostMarginSummary, useDetail: useCostMarginJobDetail,
    api: { create: null as unknown as (p: AnyRecord) => Promise<AnyRecord>, update: null as unknown as (id: string | number, p: AnyRecord) => Promise<AnyRecord> },
    createLabel: "",
    kpis: [["Jobs With Evidence","jobsWithEvidence"],["Complete Margins","completeMargins"],["Missing Cost Evidence","missingCostEvidence"],["Missing Issued Revenue","missingIssuedRevenue"]],
    columns: ["entityType","entityLabel","customerName","revenueEstimate","totalCost","marginEstimate","marginPercent","currency","invoiceCount","costRecordCount","status","dataOrigin"],
    fields: [],
    actions: [],
    sections: [] as [string,string,string[]][],
  },
  "cost-leakage": {
    queryKey: "cost-leakage", eyebrow: "Revenue Leakage Evidence", title: "Recorded revenue leakage review queue", icon: <TrendingDown />,
    description: "Runtime findings derived from completed jobs, billable charges and contract minimums, with currency and evidence status shown explicitly.",
    useRows: useCostLeakageItems, useSummary: useCostLeakageSummary, useDetail: useCostLeakageItemDetail,
    api: { create: null as unknown as (p: AnyRecord) => Promise<AnyRecord>, update: null as unknown as (id: string | number, p: AnyRecord) => Promise<AnyRecord> },
    createLabel: "",
    kpis: [["Open Items","openItems"],["High Severity","highSeverityItems"],["In Progress","inProgressItems"],["Acknowledged","acknowledgedItems"],["Amount Unavailable","amountUnavailableItems"],["Open Actions","openActions"],["Total","total"]],
    columns: ["leakageNumber","category","title","severity","estimatedLoss","currency","amountEvidenceStatus","status","ownerRole","recordOrigin"],
    fields: [],
    actions: ["acknowledge"],
    sections: [] as [string,string,string[]][],
  },
} satisfies Record<Kind, {
  queryKey: string; eyebrow: string; title: string; icon: ReactNode; description: string;
  useRows: () => { data?: AnyRecord[]; isLoading: boolean; isError?: boolean; refetch?: () => unknown };
  useSummary: () => { data?: AnyRecord };
  useDetail: (id?: string | number) => { data?: AnyRecord; isLoading: boolean };
  api: { create: ((p: AnyRecord) => Promise<AnyRecord>) | null; update: ((id: string | number, p: AnyRecord) => Promise<AnyRecord>) | null };
  createLabel: string; kpis: string[][]; columns: string[]; fields: string[][];
  actions: string[]; sections: [string,string,string[]][];
}>;

/* ──────────────────────────────────────────────────────────
   PAGE COMPONENT
────────────────────────────────────────────────────────── */
export function Batch5FinancePage({ kind }: { kind: Kind }) {
  const config   = configs[kind];
  const rowsQ    = config.useRows();
  const summaryQ = config.useSummary();

  const [selected,  setSelected]  = useState<AnyRecord | null>(null);
  const [editing,   setEditing]   = useState<AnyRecord | null>(null);
  const [search,    setSearch]    = useState("");
  const [filter,    setFilter]    = useState("All");
  const [activeTab, setActiveTab] = useState(0);

  // Extra fuel-module queries — disabled on other pages (no wasted network calls)
  const idlingQ      = useQuery({ queryKey: ["fuel","idling-tab"],   queryFn: fuelApi.idlingEvents,   enabled: kind === "fuel" });
  const anomalyQ     = useQuery({ queryKey: ["fuel","anomaly-tab"],  queryFn: fuelApi.anomalies,      enabled: kind === "fuel" });
  const vehicleAggQ  = useQuery({ queryKey: ["fuel","vehicle-agg"],  queryFn: fuelApi.vehicleSummary, enabled: kind === "fuel" });

  const tabDefs = MODULE_TABS[kind];
  const tabSources: AnyRecord[][] = kind === "fuel"
    ? [rowsQ.data ?? [], idlingQ.data ?? [], anomalyQ.data ?? []]
    : [rowsQ.data ?? []];
  const safeTab = Math.min(activeTab, tabSources.length - 1);
  const standardDetail = config.useDetail(kind === "fuel" ? undefined : selected?.id as string | number | undefined);
  const fuelDetail = useQuery({
    queryKey: ["fuel", "selected-detail", safeTab, selected?.id],
    queryFn: () => safeTab === 0
      ? fuelApi.transaction(selected!.id as string | number)
      : safeTab === 1
        ? fuelApi.idlingEvent(selected!.id as string | number)
        : Promise.resolve({ record: selected! }),
    enabled: kind === "fuel" && Boolean(selected?.id),
  });
  const detail = kind === "fuel" ? fuelDetail : standardDetail;
  const qc     = useQueryClient();

  const invalidate = async () => {
    await qc.invalidateQueries({ queryKey: [config.queryKey] });
    await qc.invalidateQueries({ queryKey: [config.queryKey, "summary"] });
  };

  const save = useMutation({
    mutationFn: (payload: AnyRecord) => {
      if (!config.api.create || !config.api.update) return Promise.resolve({} as AnyRecord);
      return (payload.id ? config.api.update(payload.id as string | number, payload) : config.api.create(payload));
    },
    onSuccess: async () => { setEditing(null); await invalidate(); },
  });

  const act = useMutation({
    mutationFn: ({ type, row }: { type: string; row: AnyRecord }) => runAction(kind, type, row),
    onSuccess: invalidate,
  });

  // Tab data sources
  const tabRows  = tabSources[safeTab] ?? [];
  const tabCols  = tabDefs ? (tabDefs[safeTab]?.columns ?? config.columns) : config.columns;

  const displayRows = useMemo(() => tabRows.filter((row) => {
    const searchLower = search.toLowerCase();
    const filterLower = filter.toLowerCase();
    const matchesSearch = !search || 
      String(row.transactionNumber || row.expenseNumber || row.contractNumber || row.carrierNumber || row.leakageNumber || "").toLowerCase().includes(searchLower) ||
      String(row.vehicleCode || row.driverName || row.customerName || row.vendorName || row.entityLabel || row.status || "").toLowerCase().includes(searchLower);

    const statusVal = String(row.status ?? row.approvalStatus ?? row.complianceStatus ?? row.severity ?? row.thresholdStatus ?? row.threshold_status ?? "").toLowerCase();
    const matchesFilter = filter === "All" || statusVal.includes(filterLower);
    return matchesSearch && matchesFilter;
  }), [tabRows, search, filter]);

  if (rowsQ.isLoading) return <LoadingState />;
  if (rowsQ.isError) {
    return (
      <ErrorState
        message={`Unable to load ${config.title}. Check backend connectivity and retry.`}
        onRetry={rowsQ.refetch ? () => void rowsQ.refetch?.() : undefined}
      />
    );
  }

  const summaryData = summaryQ.data ?? {};
  const s = summaryData;

  return (
    <div className="flex h-full flex-col gap-8 overflow-y-auto">
      {/* Header */}
      <PageHeader
        eyebrow={config.eyebrow}
        title={config.title}
        description={config.description}
        actions={
          <>
            {config.createLabel && (
              <button className="btn-primary" onClick={() => setEditing(defaultForm(kind))}>
                <Plus className="h-4 w-4" /> {config.createLabel}
              </button>
            )}
            <button className="btn-ghost" onClick={() => exportCsv(kind, displayRows)}>
              <Download className="h-4 w-4" /> Export Report
            </button>
          </>
        }
      />

      {/* KPI Grid */}
      <div className="grid gap-6 sm:grid-cols-3 xl:grid-cols-5">
        {config.kpis.slice(0, 5).map(([label, key]) => (
          <KpiCard
            key={key}
            label={label}
            value={String(s[key] ?? 0)}
            status={/anomaly|missing|critical|leakage|unusual|rejected/i.test(label) ? "Critical" : /pending|risk|expir/i.test(label) ? "pending" : undefined}
          />
        ))}
      </div>

      {/* Module Chart */}
      <ModuleChart kind={kind} rows={rowsQ.data ?? []} vehicleSummary={vehicleAggQ.data as AnyRecord[] | undefined} />

      {kind === "fuel" && (
        <div className="grid gap-4 lg:grid-cols-2">
          <div className="panel p-4 text-sm text-slate-600">
            <p className="font-semibold text-slate-900">Recorded fuel spend by currency and unit</p>
            {((summaryData.fuelByCurrencyAndUnit as AnyRecord[] | undefined) ?? []).length > 0 ? (
              <div className="mt-2 flex flex-wrap gap-2">
                {((summaryData.fuelByCurrencyAndUnit as AnyRecord[]) ?? []).map((total) => (
                  <span key={`${String(total.currency)}:${String(total.unit)}`} className="badge">
                    {String(total.currency)} · {String(total.unit)} · today {Number(total.spendToday ?? 0).toLocaleString()} · month {Number(total.spendThisMonth ?? 0).toLocaleString()} · avg/unit {Number(total.averageUnitPrice ?? 0).toLocaleString(undefined, { maximumFractionDigits: 4 })}
                  </span>
                ))}
              </div>
            ) : <p className="mt-2">No non-demo fuel transactions are recorded.</p>}
            <p className="mt-2 text-xs text-slate-500">Currencies and measurement units remain separate. No MPG result is claimed without distance evidence.</p>
          </div>
          <div className="panel p-4 text-sm text-slate-600">
            <p className="font-semibold text-slate-900">Today’s recorded idling estimates by currency</p>
            {((summaryData.idlingByCurrency as AnyRecord[] | undefined) ?? []).length > 0 ? (
              <div className="mt-2 flex flex-wrap gap-2">
                {((summaryData.idlingByCurrency as AnyRecord[]) ?? []).map((total) => (
                  <span key={String(total.currency)} className="badge">
                    {String(total.currency)} · estimated cost {Number(total.recordedEstimatedCost ?? 0).toLocaleString()} · {Number(total.durationMinutes ?? 0).toLocaleString()} minutes · {String(total.eventCount ?? 0)} events
                  </span>
                ))}
              </div>
            ) : <p className="mt-2">No non-demo idling events are recorded today.</p>}
            <p className="mt-2 text-xs text-slate-500">Costs remain estimates supplied with each event; they are not certified savings.</p>
          </div>
        </div>
      )}

      {kind === "expenses" && (
        <div className="panel p-4 text-sm text-slate-600">
          <p className="font-semibold text-slate-900">Recorded monthly totals by currency</p>
          {((summaryData.monthlyTotals as AnyRecord[] | undefined) ?? []).length > 0 ? (
            <div className="mt-2 flex flex-wrap gap-2">
              {((summaryData.monthlyTotals as AnyRecord[]) ?? []).map((total) => (
                <span key={String(total.currency)} className="badge">
                  {String(total.currency)} {Number(total.amount ?? 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} · {String(total.recordCount ?? 0)} records
                </span>
              ))}
            </div>
          ) : <p className="mt-2">No persisted expenses are recorded for this month.</p>}
          <p className="mt-2 text-xs text-slate-500">Currencies are displayed separately; no exchange-rate conversion is claimed.</p>
        </div>
      )}

      {kind === "cost-margin" && (
        <div className="panel p-4 text-sm text-slate-600">
          <p className="font-semibold text-slate-900">Recorded margin evidence by currency</p>
          {((summaryData.byCurrency as AnyRecord[] | undefined) ?? []).length > 0 ? (
            <div className="mt-2 flex flex-wrap gap-2">
              {((summaryData.byCurrency as AnyRecord[]) ?? []).map((total) => (
                <span key={String(total.currency)} className="badge">
                  {String(total.currency)} · revenue {Number(total.revenueEstimate ?? 0).toLocaleString()} · approved cost {Number(total.totalCost ?? 0).toLocaleString()} · {String(total.completeMarginCount ?? 0)} complete margins
                </span>
              ))}
            </div>
          ) : <p className="mt-2">No jobs have issued-invoice or approved-expense evidence yet.</p>}
          <p className="mt-2 text-xs text-slate-500">Margins remain unavailable when either evidence side is missing. Currencies are never combined.</p>
        </div>
      )}

      {kind === "cost-leakage" && (
        <div className="panel p-4 text-sm text-slate-600">
          <p className="font-semibold text-slate-900">Detected loss and action estimates by currency</p>
          {((summaryData.byCurrency as AnyRecord[] | undefined) ?? []).length > 0 ? (
            <div className="mt-2 flex flex-wrap gap-2">
              {((summaryData.byCurrency as AnyRecord[]) ?? []).map((total) => (
                <span key={String(total.currency)} className="badge">
                  {String(total.currency)} · detected {Number(total.detectedLoss ?? 0).toLocaleString()} · open action estimates {Number(total.openActionEstimatedSavings ?? 0).toLocaleString()} · {String(total.itemCount ?? 0)} findings
                </span>
              ))}
            </div>
          ) : <p className="mt-2">No runtime revenue leakage findings are recorded.</p>}
          <p className="mt-2 text-xs text-slate-500">Currencies are never combined. “Unknown” means the source record did not preserve a currency.</p>
        </div>
      )}

      {act.isError && (
        <p role="alert" className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {apiErrorMessage(act.error, "The expense workflow action was rejected. Reload the record and try again.")}
        </p>
      )}

      {/* Search + Filter bar */}
      <div className="flex flex-col gap-3 xl:flex-row xl:items-center">
        <input
          className="field xl:max-w-md"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={kind === "cost-margin"
            ? "Search evidence by job, customer or status…"
            : `Search ${config.eyebrow.toLowerCase()} by vehicle, driver, status…`}
        />
        <select className="field xl:max-w-[200px]" value={filter} onChange={(e) => setFilter(e.target.value)}>
          {FILTER_OPTIONS[kind].map((opt) => <option key={opt}>{opt}</option>)}
        </select>
        <div className="ml-auto flex items-center gap-2">
          {kind === "fuel" && <span className="text-xs text-slate-500">Fuel-card provider import: not configured</span>}
        </div>
      </div>

      {/* Tabs (multi-view modules) */}
      {tabDefs && (
        <TabBar
          tabs={tabDefs.map((t, i) => ({ label: t.label, count: tabSources[i]?.length ?? 0 }))}
          active={safeTab}
          onChange={(i) => { setActiveTab(i); setSelected(null); }}
        />
      )}

      {/* Data Table */}
      <DataTable rows={displayRows} columns={tabCols} onSelect={setSelected} />

      {/* Detail Drawer */}
      <Drawer
        config={config}
        detail={detail.data}
        loading={detail.isLoading}
        onClose={() => setSelected(null)}
        onEdit={(r) => { if (config.createLabel) setEditing(r); }}
        onAction={(type, row) => act.mutate({ type, row })}
      />

      {/* Create / Edit Modal */}
      {editing && (
        <Modal
          title={config.createLabel}
          fields={config.fields}
          initial={editing}
          saving={save.isPending}
          error={save.isError ? apiErrorMessage(save.error, "The record could not be saved. Review the fields and try again.") : null}
          onClose={() => { save.reset(); setEditing(null); }}
          onSave={(payload) => save.mutate(payload)}
        />
      )}
    </div>
  );
}

/* ──────────────────────────────────────────────────────────
   TAB BAR
────────────────────────────────────────────────────────── */
function TabBar({ tabs, active, onChange }: {
  tabs: Array<{ label: string; count: number }>;
  active: number;
  onChange: (i: number) => void;
}) {
  return (
    <div className="flex items-center gap-1 border-b border-slate-200">
      {tabs.map((tab, i) => (
        <button
          key={tab.label}
          type="button"
          onClick={() => onChange(i)}
          className={`relative flex items-center gap-2 px-4 py-2.5 text-sm font-medium transition-colors ${
            i === active ? "text-slate-900" : "text-slate-500 hover:text-slate-700"
          }`}
        >
          {tab.label}
          <span className={`rounded-full px-1.5 py-0.5 text-[10px] font-bold ${
            i === active ? "bg-teal-50 text-teal-700" : "bg-slate-100 text-slate-500"
          }`}>
            {tab.count}
          </span>
          {i === active && (
            <span className="absolute bottom-0 left-0 right-0 h-0.5 rounded-t-full bg-gradient-to-r from-teal-400 to-blue-500" />
          )}
        </button>
      ))}
    </div>
  );
}

/* ──────────────────────────────────────────────────────────
   MODULE CHART
────────────────────────────────────────────────────────── */
const CHART_TOOLTIP_STYLE = {
  background: tokens.surface,
  border: `1px solid ${tokens.border}`,
  borderRadius: 8,
  color: tokens.textSecondary,
  fontSize: 12,
};

function ModuleChart({ kind, rows, vehicleSummary }: {
  kind: Kind;
  rows: AnyRecord[];
  vehicleSummary?: AnyRecord[];
}) {
  if (kind === "fuel") {
    const data = (vehicleSummary ?? []).slice(0, 10).map((r) => ({
      name: `${String(r.currency ?? "Unknown")} · ${String(r.unit ?? "Unit")} · ${String(r.vehicle_code ?? r.vehicleCode ?? `V${r.vehicle_id}`)}`.slice(0, 24),
      cost: Number(r.total_cost ?? r.totalCost ?? 0),
      anomalies: Number(r.anomaly_count ?? r.anomalyCount ?? 0),
    }));
    if (!data.length) return null;
    return (
      <div className="panel p-5">
        <p className="section-title mb-4">Recorded fuel cost by vehicle, currency and unit</p>
        <ResponsiveContainer width="100%" height={180}>
          <BarChart data={data} margin={{ top: 0, right: 0, bottom: 0, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" vertical={false} />
            <XAxis dataKey="name" tick={{ fill: chart.slate500, fontSize: 11 }} axisLine={false} tickLine={false} />
            <YAxis tick={{ fill: chart.slate500, fontSize: 11 }} axisLine={false} tickLine={false} width={52} tickFormatter={(v: number) => v >= 1000 ? `${(v/1000).toFixed(1)}k` : String(v)} />
            <ChartTooltip contentStyle={CHART_TOOLTIP_STYLE} formatter={(v: unknown) => [Number(v ?? 0).toFixed(2), "Recorded cost"]} />
            <Bar dataKey="cost" radius={[3, 3, 0, 0]}>
              {data.map((d, i) => (
                <Cell key={i} fill={d.anomalies > 0 ? "rgba(248,113,113,.75)" : "rgba(45,212,191,.7)"} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
        <p className="mt-2 text-[11px] text-slate-500">Each bar is scoped to one currency and measurement unit. Red indicates a persisted, non-demo anomaly finding.</p>
      </div>
    );
  }

  if (kind === "cost-margin") {
    const data = rows
      .filter((r) => r.marginPercent != null || r.margin_percent != null)
      .slice(0, 12)
      .map((r) => ({
        name: String(r.entityLabel ?? r.entity_label ?? `#${r.id}`).slice(0, 10),
        margin: Number(r.marginPercent ?? r.margin_percent),
      }));
    if (!data.length) return null;
    return (
      <div className="panel p-5">
        <p className="section-title mb-4">Recorded margin % by job</p>
        <ResponsiveContainer width="100%" height={180}>
          <BarChart data={data} margin={{ top: 0, right: 0, bottom: 0, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" vertical={false} />
            <XAxis dataKey="name" tick={{ fill: chart.slate500, fontSize: 11 }} axisLine={false} tickLine={false} />
            <YAxis tick={{ fill: chart.slate500, fontSize: 11 }} axisLine={false} tickLine={false} width={40} unit="%" />
            <ChartTooltip contentStyle={CHART_TOOLTIP_STYLE} formatter={(v: unknown) => [`${Number(v ?? 0).toFixed(1)}%`, "Margin"]} />
            <Bar dataKey="margin" radius={[3, 3, 0, 0]}>
              {data.map((_, i) => <Cell key={i} fill="rgba(45,212,191,.75)" />)}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
        <p className="mt-2 text-[11px] text-slate-500">Only jobs with both issued-revenue and approved-cost evidence appear.</p>
      </div>
    );
  }

  if (kind === "cost-leakage") {
    const byCategory = Object.entries(
      rows.reduce<Record<string, { loss: number; currency: string; category: string }>>((acc, r) => {
        const cat = String(r.category ?? "Other");
        const currency = String(r.currency ?? "Unknown");
        const key = `${currency}:${cat}`;
        const current = acc[key] ?? { loss: 0, currency, category: cat };
        acc[key] = { ...current, loss: current.loss + Number(r.estimatedLoss ?? r.estimated_loss ?? 0) };
        return acc;
      }, {})
    )
      .map(([, item]) => ({ name: `${item.currency} · ${item.category}`.slice(0, 24), loss: item.loss }))
      .sort((a, b) => b.loss - a.loss)
      .slice(0, 8);

    if (!byCategory.length) return null;
    return (
      <div className="panel p-5">
        <p className="section-title mb-4">Recorded leakage by category and currency</p>
        <ResponsiveContainer width="100%" height={220}>
          <BarChart data={byCategory} layout="vertical" margin={{ top: 0, right: 24, bottom: 0, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" horizontal={false} />
            <XAxis type="number" tick={{ fill: chart.slate500, fontSize: 11 }} axisLine={false} tickLine={false} tickFormatter={(v: number) => v >= 1000 ? `${(v/1000).toFixed(0)}k` : String(v)} />
            <YAxis type="category" dataKey="name" tick={{ fill: chart.slate400, fontSize: 11 }} axisLine={false} tickLine={false} width={130} />
            <ChartTooltip contentStyle={CHART_TOOLTIP_STYLE} formatter={(v: unknown) => [Number(v ?? 0).toFixed(2), "Detected amount"]} />
            <Bar dataKey="loss" fill="rgba(248,113,113,.7)" radius={[0, 3, 3, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  }

  return null;
}

/* ──────────────────────────────────────────────────────────
   DETAIL DRAWER
────────────────────────────────────────────────────────── */
function Drawer({ config, detail, loading, onClose, onEdit, onAction }: {
  config: (typeof configs)[Kind];
  detail?: AnyRecord;
  loading: boolean;
  onClose: () => void;
  onEdit: (record: AnyRecord) => void;
  onAction: (type: string, row: AnyRecord) => void;
}) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record && !loading) return null;
  if (!record) return null;

  const title = String(
    record.transactionNumber ?? record.expenseNumber ?? record.contractNumber ??
    record.carrierNumber ?? record.leakageNumber ?? record.leakage_number ??
    record.entityLabel ?? record.entity_label ?? `Record ${record.id}`
  );
  const isExpense = config.queryKey === "expenses";
  const isFuel = config.queryKey === "fuel";
  const isCostMargin = config.queryKey === "cost-margin";
  const isCostLeakage = config.queryKey === "cost-leakage";
  const isContract = config.queryKey === "contracts";
  const isFuelTransaction = isFuel && Boolean(record.transactionNumber);
  const isFuelAnomaly = isFuel && Boolean(record.anomalyType);
  const fuelAnomalyReviewable = ["open", "under review"].includes(String(record.status ?? "").toLowerCase());
  const expensePending = String(record.approvalStatus ?? "").toLowerCase() === "pending";
  const leakageOpen = String(record.status ?? "").toLowerCase() === "open";

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/55 backdrop-blur-sm">
      <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/[0.09] bg-slate-950 shadow-2xl">
        {/* Drawer header */}
        <div className="sticky top-0 z-10 flex items-center justify-between border-b border-white/[0.07] bg-slate-950/95 px-6 py-4 backdrop-blur-xl">
          <div>
            <p className="text-[10px] font-extrabold uppercase tracking-[0.22em] text-teal-400">{config.eyebrow} · Detail</p>
            <h2 className="mt-1 text-xl font-bold text-white">{title}</h2>
          </div>
          <button type="button" className="icon-btn" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>

        <div className="p-6 space-y-6">
          {/* Status badges + actions */}
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={record.status ?? record.approvalStatus ?? record.complianceStatus} />
            {!isExpense && !isCostMargin && !isContract && <RiskBadge risk={record.severity ?? record.riskScore ?? record.anomalyStatus} />}
            <span className="badge">{isExpense ? String(record.recordOrigin ?? "Recorded expense") : isCostMargin ? "Recorded financial evidence" : isCostLeakage ? String(record.recordOrigin ?? "Runtime detector") : isFuel || isContract ? String(record.recordOrigin ?? "Origin unavailable") : "OpsTrax Finance Intelligence"}</span>
          </div>

          <div className="flex flex-wrap gap-3">
            {!!config.createLabel && (!isExpense || expensePending) && (!isFuel || isFuelTransaction) && (
              <button type="button" className="btn-primary" onClick={() => onEdit(record)}>
                <PenTool className="h-4 w-4" /> Edit
              </button>
            )}
            {config.actions.filter(() => (!isExpense || expensePending) && (!isCostLeakage || leakageOpen) && (!isFuel || (isFuelAnomaly && fuelAnomalyReviewable))).map((type) => (
              <button key={type} type="button" className="btn-ghost" onClick={() => onAction(type, record)}>
                {actionLabel(type)}
              </button>
            ))}
            <button type="button" className="btn-ghost" onClick={() => exportCsv(config.eyebrow, record ? [record] : [])}><Download className="h-4 w-4" /> Export Record</button>
          </div>

          {/* Info grid */}
          <div className="grid gap-4 lg:grid-cols-3">
            <Info title="Primary Details" record={record} keys={Object.keys(record).slice(0, 10)} />
            <Info
              title={isExpense ? "Financial / Approval" : isCostMargin ? "Financial Evidence" : isCostLeakage ? "Detected Evidence" : "Financial / Risk"}
              record={record}
              keys={isExpense
                ? ["amount","currency","approvalStatus","receiptStatus","recordOrigin","recordAttention"]
                : isCostMargin
                  ? ["revenueEstimate","totalCost","marginEstimate","marginPercent","currency","invoiceCount","costRecordCount","dataOrigin"]
                  : isCostLeakage
                    ? ["estimatedLoss","currency","amountEvidenceStatus","category","severity","dataOrigin","actionsCount","openActionEstimatedSavings"]
                  : isFuel
                    ? ["totalCost","currency","quantity","unit","unitPrice","estimatedCost","costEvidenceStatus","estimatedLoss","amountEvidenceStatus","dataOrigin","anomalyStatus"]
                    : ["totalCost","amount","baseRate","estimatedLoss","marginPercent","marginRisk","riskScore","anomalyStatus","complianceStatus","approvalStatus"]}
            />
            <Info title="Recommended Action" record={record} keys={["recommendedAction","thresholdStatus","source","ownerRole","notes"]} />
          </div>

          {/* Sub-record sections */}
          {config.sections.map(([title, key, columns]) => (
            <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) ?? []} columns={columns} />
          ))}

          <Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) ?? []} columns={["actionName","actorName","createdAt"]} />

          {/* AI recommendations */}
          {((detail?.recommendations as AnyRecord[]) ?? []).length > 0 && (
            <div>
              <p className="section-title mb-4">Recommendations</p>
              <div className="grid gap-4 lg:grid-cols-2">
                {((detail?.recommendations as AnyRecord[]) ?? []).slice(0, 4).map((insight, i) => (
                  <AiInsightCard key={String(insight.id ?? i)} insight={insight} />
                ))}
              </div>
            </div>
          )}
        </div>
      </aside>
    </div>
  );
}

/* ──────────────────────────────────────────────────────────
   CREATE / EDIT MODAL
────────────────────────────────────────────────────────── */
function Modal({ title, fields, initial, saving, error, onClose, onSave }: {
  title: string;
  fields: string[][];
  initial: AnyRecord;
  saving: boolean;
  error?: string | null;
  onClose: () => void;
  onSave: (payload: AnyRecord) => void;
}) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/65 p-4 backdrop-blur-sm">
      <form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}>
        <div className="flex items-center justify-between">
          <h2 className="text-xl font-bold text-slate-900">{title}</h2>
          <button type="button" className="icon-btn" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>
        <div className="mt-6 grid gap-4 md:grid-cols-2">
          {fields.map(([key, label]) => (
            <label key={key}>
              <span className="mb-2 block text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>
              <input
                className="field"
                value={String(form[key] ?? "")}
                onChange={(e) => setForm((x: AnyRecord) => ({ ...x, [key]: e.target.value }))}
              />
            </label>
          ))}
        </div>
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={saving}>
            {saving ? "Saving…" : "Save"}
          </button>
        </div>
        {error && <p role="alert" className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</p>}
      </form>
    </div>
  );
}

/* ──────────────────────────────────────────────────────────
   INFO PANEL
────────────────────────────────────────────────────────── */
function Info({ title, record, keys }: { title: string; record: AnyRecord; keys: string[] }) {
  const pairs = keys.filter((k) => record[k] !== undefined && record[k] !== null && record[k] !== "");
  if (!pairs.length) return null;
  return (
    <section className="rounded-2xl border border-white/[0.08] bg-white/[0.025] p-4">
      <h3 className="section-title">{title}</h3>
      <div className="mt-3 space-y-2">
        {pairs.map((key) => (
          <p key={key} className="text-sm text-slate-300">
            <span className="text-slate-500">{labelize(key)}: </span>
            {String(record[key])}
          </p>
        ))}
      </div>
    </section>
  );
}

/* ──────────────────────────────────────────────────────────
   SUB-RECORD GRID
────────────────────────────────────────────────────────── */
function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  if (!rows.length) return null;
  return (
    <section className="rounded-2xl border border-white/[0.08] bg-white/[0.025] p-4">
      <h3 className="section-title mb-3">{title}</h3>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[640px] text-left text-sm">
          <thead>
            <tr className="border-b border-white/[0.06]">
              {columns.map((c) => (
                <th key={c} className="px-3 py-2 text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">
                  {labelize(c)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-white/[0.05]">
            {rows.slice(0, 12).map((row, i) => (
              <tr key={String(row.id ?? i)} className="hover:bg-white/[0.02]">
                {columns.map((c) => (
                  <td key={c} className="px-3 py-2 text-slate-300">{String(row[c] ?? "—")}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/* ──────────────────────────────────────────────────────────
   HELPERS
────────────────────────────────────────────────────────── */
function actionLabel(type: string): string {
  const map: Record<string, string> = {
    reviewAnomaly: "Review Anomaly",
    approve:       "Approve",
    reject:        "Reject",
    activate:      "Activate Contract",
    expire:        "Mark Expired",
    setStatus:     "Update Status",
    recalculate:   "Recalculate Margin",
    acknowledge:   "Acknowledge",
  };
  return map[type] ?? labelize(type);
}

function defaultForm(kind: Kind): AnyRecord {
  const today = new Date().toISOString().split("T")[0];
  if (kind === "fuel")       return { fuelType: "Diesel", quantity: "", unit: "Gallons", unitPrice: "", currency: "USD", paymentMethod: "Fleet Card", fuelDate: today };
  if (kind === "expenses")   return { categoryName: "", amount: "", currency: "", receiptStatus: "Missing", expenseDate: today };
  if (kind === "contracts")  return { contractType: "Customer", rateType: "Per Mile", baseRate: "", currency: "USD", status: "Draft", effectiveDate: today };
  if (kind === "carriers")   return { status: "Active", complianceStatus: "Compliant", contractStatus: "Active", onTimePercent: 90, safetyScore: 88, performanceScore: 86, riskScore: 20 };
  return {};
}

async function runAction(kind: Kind, type: string, row: AnyRecord): Promise<AnyRecord> {
  const id = row.id as string | number;
  if (kind === "fuel")         return fuelApi.reviewAnomaly(id, { status: "Reviewed" });
  if (kind === "expenses")     return type === "approve" ? expensesApi.approve(id) : expensesApi.reject(id);
  if (kind === "contracts")    return type === "activate" ? contractsApi.activate(id) : contractsApi.expire(id);
  if (kind === "carriers")     return carriersApi.setStatus(id, { status: "Active" });
  return costLeakageApi.acknowledge(id);
}
