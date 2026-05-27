import { FormEvent, ReactNode, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip as ChartTooltip, XAxis, YAxis } from "recharts";
import {
  AlertTriangle, Download, Flame, Fuel, Landmark,
  PenTool, Plus, Sparkles, TrendingDown, Truck, WalletCards, X, Zap,
} from "lucide-react";
import { AiInsightCard, DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
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
import { costMarginApi } from "@/services/costMarginApi";
import { expensesApi } from "@/services/expensesApi";
import { fuelApi } from "@/services/fuelApi";
import type { AnyRecord } from "@/types";

type Kind = "fuel" | "expenses" | "contracts" | "carriers" | "cost-margin" | "cost-leakage";

/* ── Per-module tab definitions ── */
const MODULE_TABS: Partial<Record<Kind, Array<{ label: string; columns: string[] }>>> = {
  fuel: [
    { label: "Transactions",  columns: ["transactionNumber","vehicleCode","driverName","fuelType","quantity","unitPrice","totalCost","odometer","fuelStation","paymentMethod","anomalyStatus","fuelDate"] },
    { label: "Idling Events", columns: ["eventNumber","vehicleCode","driverName","locationDescription","durationMinutes","estimatedCost","thresholdStatus","riskScore","recommendedAction"] },
    { label: "Anomalies",     columns: ["anomalyType","severity","description","estimatedLoss","status"] },
  ],
};

/* ── Per-module filter options ── */
const FILTER_OPTIONS: Record<Kind, string[]> = {
  "fuel":          ["All","Normal","Anomaly Detected","Under Review","Excessive","Warning"],
  "expenses":      ["All","Pending","Approved","Rejected","Missing","High"],
  "contracts":     ["All","Active","Expiring Soon","Expired","High","Medium"],
  "carriers":      ["All","Active","Pending","Suspended","Compliant","Non-Compliant","At Risk"],
  "cost-margin":   ["All","High","Medium","Low"],
  "cost-leakage":  ["All","Open","Acknowledged","In Progress","Critical","High"],
};

const configs = {
  fuel: {
    queryKey: "fuel", eyebrow: "Fuel & Idling", title: "Fleet fuel cost and idling intelligence", icon: <Fuel />,
    description: "Fuel transactions, idling cost leakage, anomaly detection, driver and vehicle fuel profiles, and AI cost reduction recommendations.",
    useRows: useFuelTransactions, useSummary: useFuelSummary, useDetail: useFuelTransaction,
    api: { create: fuelApi.createTransaction, update: (id: string | number, p: AnyRecord) => fuelApi.updateTransaction(id, p) },
    createLabel: "Record Transaction",
    kpis: [["Spend Today","fuel_spend_today"],["Spend This Month","fuel_spend_this_month"],["Idle Cost Today","idle_cost_today"],["Transactions","fuel_transactions"],["Anomalies","fuel_anomalies"],["High Idle Vehicles","high_idle_vehicles"],["Cost / Gallon","cost_per_gallon"],["Savings Opportunity","estimated_savings_opportunity"]],
    columns: ["transactionNumber","vehicleCode","driverName","fuelType","quantity","unitPrice","totalCost","odometer","fuelStation","paymentMethod","anomalyStatus","fuelDate"],
    fields: [["vehicleId","Vehicle ID"],["driverId","Driver ID"],["jobId","Job ID"],["fuelDate","Fuel Date"],["fuelType","Fuel Type"],["quantity","Gallons"],["unitPrice","Unit Price"],["totalCost","Total Cost"],["odometer","Odometer"],["fuelStation","Fuel Station"],["paymentMethod","Payment Method"],["region","Region"],["anomalyStatus","Anomaly Status"],["notes","Notes"]],
    actions: ["reviewAnomaly"],
    sections: [["Fuel Anomalies","anomalies",["anomalyType","severity","estimatedLoss","status","createdAt"]]] as [string,string,string[]][],
  },
  expenses: {
    queryKey: "expenses", eyebrow: "Expenses", title: "Operating expense register and approval workflow", icon: <WalletCards />,
    description: "Operating expenses, approval workflow, anomaly detection, receipt tracking, cost allocation and AI expense governance recommendations.",
    useRows: useExpenses, useSummary: useExpensesSummary, useDetail: useExpenseDetail,
    api: { create: expensesApi.create, update: (id: string | number, p: AnyRecord) => expensesApi.update(id, p) },
    createLabel: "Create Expense",
    kpis: [["Expenses This Month","total_expenses_this_month"],["Pending Approval","pending_approval"],["Approved","approved_expenses"],["Rejected","rejected_expenses"],["Unusual","unusual_expenses"],["Missing Receipts","missing_receipts"],["Avg Amount","average_expense_amount"],["Total","total"]],
    columns: ["expenseNumber","categoryName","amount","approvalStatus","receiptStatus","vendorName","vehicleCode","driverName","riskScore","expenseDate","recommendedAction"],
    fields: [["categoryName","Category"],["amount","Amount"],["currency","Currency"],["expenseDate","Expense Date"],["vehicleId","Vehicle ID"],["driverId","Driver ID"],["jobId","Job ID"],["customerId","Customer ID"],["vendorName","Vendor Name"],["approvalStatus","Approval Status"],["receiptStatus","Receipt Status"],["notes","Notes"]],
    actions: ["approve","reject"],
    sections: [] as [string,string,string[]][],
  },
  contracts: {
    queryKey: "contracts", eyebrow: "Contracts / Rates", title: "Contract management and rate structures", icon: <Landmark />,
    description: "Customer and carrier contracts, rate structures, margin risk governance, fuel surcharge configuration and renewal workflows.",
    useRows: useContracts, useSummary: useContractsSummary, useDetail: useContractDetail,
    api: { create: contractsApi.create, update: (id: string | number, p: AnyRecord) => contractsApi.update(id, p) },
    createLabel: "Create Contract",
    kpis: [["Active","active_contracts"],["Expiring Soon","expiring_soon"],["Expired","expired_contracts"],["Margin Risk","margin_risk_contracts"],["Underpriced","underpriced_contracts"],["Renewal Queue","renewal_queue"],["Fuel Surcharge","fuel_surcharge_active"],["Total","total"]],
    columns: ["contractNumber","contractType","rateType","status","customerName","carrierName","baseRate","marginRisk","effectiveDate","expirationDate","recommendedAction"],
    fields: [["contractNumber","Contract #"],["customerId","Customer ID"],["carrierId","Carrier ID"],["contractType","Contract Type"],["rateType","Rate Type"],["baseRate","Base Rate"],["currency","Currency"],["effectiveDate","Effective Date"],["expirationDate","Expiry Date"],["fuelSurchargeEnabled","Fuel Surcharge?"],["fuelSurchargePercent","Surcharge %"],["marginRisk","Margin Risk"],["slaTerms","SLA Terms"],["notes","Notes"]],
    actions: ["activate","expire"],
    sections: [["Contract Rates","rates",["rateCode","rateType","baseRate","effectiveDate","status"]]] as [string,string,string[]][],
  },
  carriers: {
    queryKey: "carriers", eyebrow: "Carrier Management", title: "Partner carrier registry and performance", icon: <Truck />,
    description: "Partner carriers, compliance status, insurance tracking, performance scoring, cost governance and carrier document management.",
    useRows: useCarriers, useSummary: useCarriersSummary, useDetail: useCarrierDetail,
    api: { create: carriersApi.create, update: (id: string | number, p: AnyRecord) => carriersApi.update(id, p) },
    createLabel: "Add Carrier",
    kpis: [["Active Carriers","active_carriers"],["Compliance Risk","compliance_risk_carriers"],["Insurance Expiring","insurance_expiring"],["Avg Performance","average_carrier_score"],["On-Time %","on_time_performance"],["Preferred","preferred_carriers"],["Docs Missing","documents_missing"],["Total","total"]],
    columns: ["carrierNumber","name","region","complianceStatus","contractStatus","onTimePercent","safetyScore","performanceScore","riskScore","insuranceExpiry","status","recommendedAction"],
    fields: [["name","Carrier Name"],["mcNumber","MC Number"],["contactName","Contact Name"],["phone","Phone"],["email","Email"],["region","Region"],["status","Status"],["complianceStatus","Compliance Status"],["insuranceExpiry","Insurance Expiry"],["contractStatus","Contract Status"],["notes","Notes"]],
    actions: ["setStatus"],
    sections: [
      ["Performance History","performance",["periodStart","periodEnd","jobsHandled","onTimePercent","incidentCount","performanceScore"]],
      ["Documents","documents",["documentType","documentNumber","status","expiryDate"]],
    ] as [string,string,string[]][],
  },
  "cost-margin": {
    queryKey: "cost-margin", eyebrow: "Predictive Cost & Margin", title: "Cost and margin intelligence center", icon: <Zap />,
    description: "Job, route, vehicle and customer cost profiles, margin percent analysis, predictions and AI profitability improvement recommendations.",
    useRows: useCostMarginJobs, useSummary: useCostMarginSummary, useDetail: useCostMarginJobDetail,
    api: { create: null as unknown as (p: AnyRecord) => Promise<AnyRecord>, update: null as unknown as (id: string | number, p: AnyRecord) => Promise<AnyRecord> },
    createLabel: "",
    kpis: [["Revenue","revenue_estimate"],["Cost","cost_estimate"],["Gross Margin","gross_margin_estimate"],["Margin %","margin_pct"],["Jobs Below Target","jobs_below_margin_target"],["High Cost Vehicles","high_cost_vehicles"],["Fuel Impact","fuel_cost_impact"],["Savings Opp","savings_opportunity"]],
    columns: ["entityType","jobCode","customerName","revenueEstimate","totalCost","marginEstimate","marginPercent","fuelCost","delayCost","idleCost","marginRisk"],
    fields: [],
    actions: ["recalculate"],
    sections: [] as [string,string,string[]][],
  },
  "cost-leakage": {
    queryKey: "cost-leakage", eyebrow: "Cost Leakage Intelligence", title: "ROI and cost leakage action queue", icon: <TrendingDown />,
    description: "Leakage categories, estimated dollar loss, recoverable savings opportunities, acknowledgement workflow and cost recovery action queue.",
    useRows: useCostLeakageItems, useSummary: useCostLeakageSummary, useDetail: useCostLeakageItemDetail,
    api: { create: null as unknown as (p: AnyRecord) => Promise<AnyRecord>, update: null as unknown as (id: string | number, p: AnyRecord) => Promise<AnyRecord> },
    createLabel: "",
    kpis: [["Total Leakage","total_estimated_leakage"],["Monthly Projection","monthly_leakage_projection"],["Open Items","open_items"],["Critical","critical_leakage_items"],["Recoverable","recoverable_savings"],["Open Actions","open_actions"],["Acknowledged","acknowledged_items"],["Total","total"]],
    columns: ["leakageNumber","category","title","severity","estimatedLoss","projectedMonthlyLoss","status","ownerRole","recommendedAction"],
    fields: [],
    actions: ["acknowledge","createAction"],
    sections: [] as [string,string,string[]][],
  },
} satisfies Record<Kind, {
  queryKey: string; eyebrow: string; title: string; icon: ReactNode; description: string;
  useRows: () => { data?: AnyRecord[]; isLoading: boolean };
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

  const detail = config.useDetail(selected?.id as string | number | undefined);
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
  const tabDefs = MODULE_TABS[kind];
  const tabSources: AnyRecord[][] = kind === "fuel"
    ? [rowsQ.data ?? [], idlingQ.data ?? [], anomalyQ.data ?? []]
    : [rowsQ.data ?? []];
  const safeTab  = Math.min(activeTab, tabSources.length - 1);
  const tabRows  = tabSources[safeTab] ?? [];
  const tabCols  = tabDefs ? (tabDefs[safeTab]?.columns ?? config.columns) : config.columns;

  const displayRows = useMemo(() => tabRows.filter((row) => {
    const text   = JSON.stringify(row).toLowerCase();
    const status = String(row.status ?? row.approvalStatus ?? row.complianceStatus ?? row.severity ?? row.threshold_status ?? "").toLowerCase();
    return (
      (!search || text.includes(search.toLowerCase())) &&
      (filter === "All" || status.includes(filter.toLowerCase()) || text.includes(filter.toLowerCase()))
    );
  }), [tabRows, search, filter]);

  if (rowsQ.isLoading) return <LoadingState />;

  const s = summaryQ.data ?? {};

  return (
    <div className="space-y-6">
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
      <div className="grid gap-4 md:grid-cols-4 xl:grid-cols-8">
        {config.kpis.map(([label, key]) => (
          <KpiCard
            key={key}
            label={label}
            value={String(s[key] ?? 0)}
            icon={config.icon}
            status={/risk|anomaly|missing|below|critical|expir|leakage|unusual|pending|rejected/i.test(label) ? "Review" : "Active"}
          />
        ))}
      </div>

      {/* Module Chart */}
      <ModuleChart kind={kind} rows={rowsQ.data ?? []} vehicleSummary={vehicleAggQ.data as AnyRecord[] | undefined} />

      {/* Search + Filter bar */}
      <div className="panel flex flex-col gap-3 p-4 xl:flex-row xl:items-center">
        <input
          className="field xl:max-w-md"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={`Search ${config.eyebrow.toLowerCase()} by vehicle, driver, status…`}
        />
        <select className="field xl:max-w-[200px]" value={filter} onChange={(e) => setFilter(e.target.value)}>
          {FILTER_OPTIONS[kind].map((opt) => <option key={opt}>{opt}</option>)}
        </select>
        <div className="ml-auto flex items-center gap-2">
          <span className="badge"><Sparkles className="h-3.5 w-3.5" /> AI intelligence active</span>
          {kind === "fuel"    && <span className="badge"><AlertTriangle className="h-3.5 w-3.5 text-amber-400" /> Anomaly detection</span>}
          {kind === "fuel"    && <span className="badge"><Flame className="h-3.5 w-3.5 text-orange-400" /> Idle cost radar</span>}
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
          onClose={() => setEditing(null)}
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
    <div className="flex items-center gap-1 border-b border-white/[0.07]">
      {tabs.map((tab, i) => (
        <button
          key={tab.label}
          onClick={() => onChange(i)}
          className={`relative flex items-center gap-2 px-4 py-2.5 text-sm font-medium transition-colors ${
            i === active ? "text-white" : "text-slate-400 hover:text-slate-200"
          }`}
        >
          {tab.label}
          <span className={`rounded-full px-1.5 py-0.5 text-[10px] font-bold ${
            i === active ? "bg-teal-400/20 text-teal-300" : "bg-white/[0.06] text-slate-500"
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
  background: "#0f172a",
  border: "1px solid rgba(255,255,255,.1)",
  borderRadius: 8,
  color: "#e2e8f0",
  fontSize: 12,
};

function ModuleChart({ kind, rows, vehicleSummary }: {
  kind: Kind;
  rows: AnyRecord[];
  vehicleSummary?: AnyRecord[];
}) {
  if (kind === "fuel") {
    const data = (vehicleSummary ?? []).slice(0, 10).map((r) => ({
      name: String(r.vehicle_code ?? r.vehicleCode ?? `V${r.vehicle_id}`).slice(0, 9),
      cost: Number(r.total_cost ?? r.totalCost ?? 0),
      anomalies: Number(r.anomaly_count ?? r.anomalyCount ?? 0),
    }));
    if (!data.length) return null;
    return (
      <div className="panel p-5">
        <p className="section-title mb-4">Fuel Cost by Vehicle (Fleet Top 10)</p>
        <ResponsiveContainer width="100%" height={180}>
          <BarChart data={data} margin={{ top: 0, right: 0, bottom: 0, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,.04)" vertical={false} />
            <XAxis dataKey="name" tick={{ fill: "#64748b", fontSize: 11 }} axisLine={false} tickLine={false} />
            <YAxis tick={{ fill: "#64748b", fontSize: 11 }} axisLine={false} tickLine={false} width={52} tickFormatter={(v: number) => `$${v >= 1000 ? `${(v/1000).toFixed(1)}k` : v}`} />
            <ChartTooltip contentStyle={CHART_TOOLTIP_STYLE} formatter={(v: unknown) => [`$${Number(v ?? 0).toFixed(2)}`, "Fuel Cost"]} />
            <Bar dataKey="cost" radius={[3, 3, 0, 0]}>
              {data.map((d, i) => (
                <Cell key={i} fill={d.anomalies > 0 ? "rgba(248,113,113,.75)" : "rgba(45,212,191,.7)"} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
        <p className="mt-2 text-[11px] text-slate-500">Red bars indicate vehicles with fuel anomalies detected.</p>
      </div>
    );
  }

  if (kind === "cost-margin") {
    const data = rows.slice(0, 12).map((r) => ({
      name: String(r.jobCode ?? r.job_code ?? r.entityLabel ?? r.entity_label ?? `#${r.id}`).slice(0, 10),
      margin: Number(r.marginPercent ?? r.margin_percent ?? 0),
    }));
    if (!data.length) return null;
    return (
      <div className="panel p-5">
        <p className="section-title mb-4">Margin % by Job (Lowest First)</p>
        <ResponsiveContainer width="100%" height={180}>
          <BarChart data={data} margin={{ top: 0, right: 0, bottom: 0, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,.04)" vertical={false} />
            <XAxis dataKey="name" tick={{ fill: "#64748b", fontSize: 11 }} axisLine={false} tickLine={false} />
            <YAxis tick={{ fill: "#64748b", fontSize: 11 }} axisLine={false} tickLine={false} width={40} unit="%" />
            <ChartTooltip contentStyle={CHART_TOOLTIP_STYLE} formatter={(v: unknown) => [`${Number(v ?? 0).toFixed(1)}%`, "Margin"]} />
            <Bar dataKey="margin" radius={[3, 3, 0, 0]}>
              {data.map((d, i) => (
                <Cell key={i} fill={d.margin < 10 ? "rgba(248,113,113,.8)" : d.margin < 20 ? "rgba(251,191,36,.75)" : "rgba(52,211,153,.7)"} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
        <p className="mt-2 text-[11px] text-slate-500">Red &lt;10% · Amber 10–20% · Green &gt;20% margin.</p>
      </div>
    );
  }

  if (kind === "cost-leakage") {
    const byCategory = Object.entries(
      rows.reduce<Record<string, number>>((acc, r) => {
        const cat = String(r.category ?? "Other");
        acc[cat] = (acc[cat] ?? 0) + Number(r.estimatedLoss ?? r.estimated_loss ?? 0);
        return acc;
      }, {})
    )
      .map(([name, loss]) => ({ name: name.length > 18 ? name.slice(0, 16) + "…" : name, loss }))
      .sort((a, b) => b.loss - a.loss)
      .slice(0, 8);

    if (!byCategory.length) return null;
    return (
      <div className="panel p-5">
        <p className="section-title mb-4">Estimated Leakage by Category</p>
        <ResponsiveContainer width="100%" height={220}>
          <BarChart data={byCategory} layout="vertical" margin={{ top: 0, right: 24, bottom: 0, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,.04)" horizontal={false} />
            <XAxis type="number" tick={{ fill: "#64748b", fontSize: 11 }} axisLine={false} tickLine={false} tickFormatter={(v: number) => `$${v >= 1000 ? `${(v/1000).toFixed(0)}k` : v}`} />
            <YAxis type="category" dataKey="name" tick={{ fill: "#94a3b8", fontSize: 11 }} axisLine={false} tickLine={false} width={130} />
            <ChartTooltip contentStyle={CHART_TOOLTIP_STYLE} formatter={(v: unknown) => [`$${Number(v ?? 0).toFixed(2)}`, "Est. Leakage"]} />
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

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/55 backdrop-blur-sm">
      <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/[0.09] bg-slate-950 shadow-2xl">
        {/* Drawer header */}
        <div className="sticky top-0 z-10 flex items-center justify-between border-b border-white/[0.07] bg-slate-950/95 px-6 py-4 backdrop-blur-xl">
          <div>
            <p className="text-[10px] font-extrabold uppercase tracking-[0.22em] text-teal-400">{config.eyebrow} · Detail</p>
            <h2 className="mt-1 text-xl font-bold text-white">{title}</h2>
          </div>
          <button className="icon-btn" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>

        <div className="p-6 space-y-6">
          {/* Status badges + actions */}
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={record.status ?? record.approvalStatus ?? record.complianceStatus} />
            <RiskBadge risk={record.severity ?? record.marginRisk ?? record.riskScore ?? record.anomalyStatus} />
            <span className="badge">OpsTrax Finance Intelligence</span>
          </div>

          <div className="flex flex-wrap gap-3">
            {!!config.createLabel && (
              <button className="btn-primary" onClick={() => onEdit(record)}>
                <PenTool className="h-4 w-4" /> Edit
              </button>
            )}
            {config.actions.map((type) => (
              <button key={type} className="btn-ghost" onClick={() => onAction(type, record)}>
                {actionLabel(type)}
              </button>
            ))}
            <button className="btn-ghost"><Download className="h-4 w-4" /> Export Placeholder</button>
          </div>

          {/* Info grid */}
          <div className="grid gap-4 lg:grid-cols-3">
            <Info title="Primary Details" record={record} keys={Object.keys(record).slice(0, 10)} />
            <Info title="Financial / Risk" record={record} keys={["totalCost","amount","baseRate","estimatedLoss","marginPercent","marginRisk","riskScore","anomalyStatus","complianceStatus","approvalStatus"]} />
            <Info title="AI Recommended Action" record={record} keys={["recommendedAction","thresholdStatus","source","ownerRole","notes"]} />
          </div>

          {/* Sub-record sections */}
          {config.sections.map(([title, key, columns]) => (
            <Grid key={title} title={title} rows={(detail?.[key] as AnyRecord[]) ?? []} columns={columns} />
          ))}

          <Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) ?? []} columns={["actionName","actorName","createdAt"]} />

          {/* AI recommendations */}
          {((detail?.recommendations as AnyRecord[]) ?? []).length > 0 && (
            <div>
              <p className="section-title mb-4">AI Recommendations</p>
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
function Modal({ title, fields, initial, saving, onClose, onSave }: {
  title: string;
  fields: string[][];
  initial: AnyRecord;
  saving: boolean;
  onClose: () => void;
  onSave: (payload: AnyRecord) => void;
}) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/65 p-4 backdrop-blur-sm">
      <form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}>
        <div className="flex items-center justify-between">
          <h2 className="text-xl font-bold text-white">{title}</h2>
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
    createAction:  "Create Recovery Action",
  };
  return map[type] ?? labelize(type);
}

function defaultForm(kind: Kind): AnyRecord {
  const today = new Date().toISOString().split("T")[0];
  if (kind === "fuel")       return { fuelType: "Diesel", quantity: 50, unitPrice: 3.89, paymentMethod: "Fleet Card", anomalyStatus: "Normal", fuelDate: today };
  if (kind === "expenses")   return { categoryName: "Fuel", amount: 250, currency: "USD", approvalStatus: "Pending", receiptStatus: "Uploaded", expenseDate: today };
  if (kind === "contracts")  return { contractType: "Customer", rateType: "Per Mile", baseRate: 2.85, currency: "USD", status: "Active", marginRisk: "Low", effectiveDate: today };
  if (kind === "carriers")   return { status: "Active", complianceStatus: "Compliant", contractStatus: "Active", onTimePercent: 90, safetyScore: 88, performanceScore: 86, riskScore: 20 };
  return {};
}

async function runAction(kind: Kind, type: string, row: AnyRecord): Promise<AnyRecord> {
  const id = row.id as string | number;
  if (kind === "fuel")         return fuelApi.reviewAnomaly(id, { status: "Reviewed" });
  if (kind === "expenses")     return type === "approve" ? expensesApi.approve(id) : expensesApi.reject(id);
  if (kind === "contracts")    return type === "activate" ? contractsApi.activate(id) : contractsApi.expire(id);
  if (kind === "carriers")     return carriersApi.setStatus(id, { status: "Active" });
  if (kind === "cost-margin")  return costMarginApi.recalculate();
  return type === "acknowledge"
    ? costLeakageApi.acknowledge(id)
    : costLeakageApi.createAction(id, { actionTitle: "Cost recovery action", estimatedSavings: 500 });
}

function exportCsv(name: string, rows: AnyRecord[]) {
  const cols = Array.from(new Set(rows.flatMap((r) => Object.keys(r)))).slice(0, 24);
  const csv  = [cols.join(","), ...rows.map((r) => cols.map((c) => JSON.stringify(r[c] ?? "")).join(","))].join("\n");
  const a    = document.createElement("a");
  a.href     = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
  a.download = `opstrax-${name}-${new Date().toISOString().slice(0, 10)}.csv`;
  a.click();
}
