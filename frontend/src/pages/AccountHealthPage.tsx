import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router";
import { customersApi } from "@/services/customersApi";
import { contractsApi } from "@/services/contractsApi";
import { exportCsv, LoadingState, EmptyState, ErrorState } from "@/components/ui";
import { CommercialMetricRail, CommercialTabs, RevenueWorkspaceHeader } from "@/components/CommercialWorkspace";
import type { AnyRecord } from "@/types";

// ── Persisted record builders ─────────────────────────────────────────────────

function optionalNumber(value: unknown): number | null {
  if (value == null || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function buildHealthRows(customers: AnyRecord[], contracts: AnyRecord[]): AnyRecord[] {
  return customers.map((c) => {
    const customerId = String(c.id ?? "");
    const healthScore = optionalNumber(c.customerDeliveryExperienceScore);
    const slaCompliance = optionalNumber(c.slaHealthScore);
    const activeContracts = contracts.filter((contract) =>
      String(contract.customerId ?? "") === customerId && /active/i.test(String(contract.status ?? "")),
    ).length;
    return {
      id: c.id,
      name: String(c.name ?? c.companyName ?? ""),
      status: String(c.healthState === "insufficient_data" ? "Not enough data" : c.status ?? "Unrated"),
      healthScore,
      slaCompliance,
      atRisk: /risk/i.test(String(c.status ?? "")) || /high/i.test(String(c.riskHeatScore ?? "")),
      accountManager: c.accountManager == null ? "—" : String(c.accountManager),
      activeContracts,
    };
  });
}

function buildRenewalRows(contracts: AnyRecord[]): AnyRecord[] {
  return contracts.map((c) => ({
    id: c.id,
    contractId: String(c.contractNumber ?? c.contractCode ?? "—"),
    customer: String(c.customerName ?? c.customer ?? ""),
    baseRate: optionalNumber(c.baseRate),
    currency: c.currency == null ? "—" : String(c.currency),
    expiryDate: String(c.expiryDate ?? c.expirationDate ?? "—"),
    renewalState: String(c.displayStatus ?? c.status ?? "Unknown"),
    status: String(c.status ?? "Unknown"),
  }));
}

const healthApi = () => Promise.all([customersApi.list(), contractsApi.list()]).then(([customers, contracts]) => buildHealthRows(customers as AnyRecord[], contracts as AnyRecord[]));
const renewalsApi = () => contractsApi.list().then((rows) => buildRenewalRows(rows as AnyRecord[]));

// ── Badge helpers ─────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Healthy" || status === "Completed" || status === "Renewal Sent" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Open" || status === "Overdue" || status === "High" ? "bg-red-50 border-red-200 text-red-700" :
    status === "In Progress" || status === "Negotiating" || status === "Interested" || status === "Pitched" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "At Risk" || status === "Pending" || status === "Monitoring" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function ScoreBar({ score }: { score: number | null }) {
  if (score == null) return <span className="text-xs text-slate-500">Not enough data</span>;
  const pct = Math.min(100, Math.max(0, score));
  const color = pct >= 88 ? "bg-teal-500" : pct >= 75 ? "bg-amber-400" : "bg-red-400";
  return (
    <div className="flex items-center gap-2 text-xs">
      <div className="w-16 h-1.5 rounded-full bg-slate-100 overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="font-medium text-slate-700">{Math.round(pct)}</span>
    </div>
  );
}

// ── Tab content components ────────────────────────────────────────────────────

function AccountHealthTab() {
  const q = useQuery({ queryKey: ["account-health"], queryFn: healthApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const atRisk = rows.filter((r) => r.atRisk).length;
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message="Customer health records are unavailable." onRetry={() => void q.refetch()} />;
  return (
    <div className="flex flex-col gap-3">
      <CommercialMetricRail metrics={[
        { label: "Accounts", value: rows.length, detail: "authorized scope" },
        { label: "At risk", value: atRisk, detail: "needs review", tone: atRisk ? "bad" : "neutral" },
        { label: "Average health", value: (() => { const scored = rows.map((r) => optionalNumber(r.healthScore)).filter((v): v is number => v != null); return scored.length ? (scored.reduce((a, b) => a + b, 0) / scored.length).toFixed(1) : "—"; })(), detail: "scored accounts", tone: "info" },
        { label: "Active contracts", value: rows.reduce((sum, row) => sum + Number(row.activeContracts ?? 0), 0), detail: "customer-linked", tone: "good" },
      ]} />
      <div className="panel overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50">
                {["Customer", "Status", "Health Score", "SLA Compliance", "Active Contracts", "Account Manager"].map((h) => (
                  <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium text-slate-900">{String(r.name ?? "—")}</td>
                  <td className="px-4 py-3"><StatusBadge status={String(r.atRisk ? "At Risk" : r.status ?? "Active")} /></td>
                  <td className="px-4 py-3"><ScoreBar score={optionalNumber(r.healthScore)} /></td>
                  <td className="px-4 py-3"><ScoreBar score={optionalNumber(r.slaCompliance)} /></td>
                  <td className="px-4 py-3 text-slate-700">{String(r.activeContracts ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.accountManager ?? "—")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function FollowUpsTab() {
  return (
    <EmptyState title="Follow-up workflow unavailable" subtitle="No persisted customer follow-up workflow is connected to this account yet, so OpsTrax does not generate follow-up records from customer profiles." />
  );
}

function SupportTicketsTab() {
  return (
    <EmptyState title="Support-ticket workflow unavailable" subtitle="Customer communication records are not support tickets. OpsTrax will show tickets here only after a persisted support workflow is connected." />
  );
}

function RenewalsTab() {
  const q = useQuery({ queryKey: ["account-health", "renewals"], queryFn: renewalsApi });
  const rows = (q.data ?? []) as AnyRecord[];
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message="Contract renewal records are unavailable." onRetry={() => void q.refetch()} />;
  return (
    <div className="flex flex-col gap-3">
      <CommercialMetricRail metrics={[
        { label: "Renewal pipeline", value: rows.length, detail: "recorded contracts" },
        { label: "Expiring soon", value: rows.filter((r) => /expiring/i.test(String(r.renewalState ?? ""))).length, detail: "needs outreach", tone: "warn" },
        { label: "Expired", value: rows.filter((r) => /expired/i.test(String(r.renewalState ?? ""))).length, detail: "action blocked", tone: "bad" },
        { label: "Active", value: rows.filter((r) => /active/i.test(String(r.status ?? ""))).length, detail: "current terms", tone: "good" },
      ]} />
      <div className="panel overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50">
                {["Contract", "Customer", "Base Rate", "Currency", "Expiry", "Renewal State", "Contract Status"].map((h) => (
                  <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium text-slate-900">{String(r.contractId ?? "--")}</td>
                  <td className="px-4 py-3 text-slate-700">{String(r.customer ?? "—")}</td>
                  <td className="px-4 py-3 text-slate-700">{r.baseRate == null ? "—" : Number(r.baseRate).toLocaleString()}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.currency ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.expiryDate ?? "—")}</td>
                  <td className="px-4 py-3"><StatusBadge status={String(r.renewalState ?? "Unknown")} /></td>
                  <td className="px-4 py-3"><StatusBadge status={String(r.status ?? "Unknown")} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function UpsellTab() {
  return (
    <EmptyState title="Upsell workflow unavailable" subtitle="OpsTrax does not infer sales opportunities from a customer’s industry or row position. Persisted, reviewed opportunities will appear here after that workflow is connected." />
  );
}

// ── Route → default tab mapping ───────────────────────────────────────────────

const ROUTE_TAB: Record<string, string> = {
  "/account-health": "health",
  "/follow-ups": "follow-ups",
  "/support-tickets": "tickets",
  "/renewals": "renewals",
  "/upsell-opportunities": "upsell",
};

type Tab = "health" | "follow-ups" | "tickets" | "renewals" | "upsell";

const TABS: { key: Tab; label: string }[] = [
  { key: "health",      label: "Account Health" },
  { key: "follow-ups",  label: "Follow-ups" },
  { key: "tickets",     label: "Support Tickets" },
  { key: "renewals",    label: "Renewals" },
  { key: "upsell",      label: "Upsell" },
];

const TAB_ROUTE: Record<Tab, string> = {
  "health": "/account-health",
  "follow-ups": "/follow-ups",
  "tickets": "/support-tickets",
  "renewals": "/renewals",
  "upsell": "/upsell-opportunities",
};

// ── Main page ─────────────────────────────────────────────────────────────────

const TAB_QUERY_KEY: Record<Tab, unknown[]> = {
  "health":     ["account-health"],
  "follow-ups": ["account-health", "follow-ups"],
  "tickets":    ["account-health", "support"],
  "renewals":   ["account-health", "renewals"],
  "upsell":     ["account-health", "upsell"],
};

export function AccountHealthPage() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const tab = (ROUTE_TAB[pathname] as Tab) ?? "health";
  const canExport = tab === "health" || tab === "renewals";

  // Export the currently displayed tab's real rows (read straight from the
  // react-query cache) to CSV. No-op with a hint if the tab hasn't loaded yet.
  function exportActive() {
    const rows = (qc.getQueryData(TAB_QUERY_KEY[tab]) ?? []) as AnyRecord[];
    if (rows.length) exportCsv(`account-${tab}`, rows);
  }

  const titles: Record<Tab, string> = {
    "health":     "Account Health",
    "follow-ups": "Follow-ups",
    "tickets":    "Support Tickets",
    "renewals":   "Renewals",
    "upsell":     "Upsell Opportunities",
  };

  const descriptions: Record<Tab, string> = {
    "health":     "Persisted customer health scores, SLA evidence and at-risk status",
    "follow-ups": "Customer follow-up records from a persisted workflow",
    "tickets":    "Persisted customer support issues and their resolution status",
    "renewals":   "Current contract expiry and status records",
    "upsell":     "Persisted, reviewed expansion opportunities",
  };

  return (
    <div className="page-stack">
      <RevenueWorkspaceHeader
        title={titles[tab]}
        description={descriptions[tab]}
        activeStage="accounts"
        eyebrow="Customer success"
        actions={canExport ? <button type="button" className="btn-secondary text-sm" onClick={exportActive}>Export CSV</button> : undefined}
      />

      {/* Tabs */}
      <CommercialTabs label="Customer success sections" items={TABS} active={tab} onSelect={(key) => navigate(TAB_ROUTE[key as Tab])} />

      {/* Tab content */}
      {tab === "health"     && <AccountHealthTab />}
      {tab === "follow-ups" && <FollowUpsTab />}
      {tab === "tickets"    && <SupportTicketsTab />}
      {tab === "renewals"   && <RenewalsTab />}
      {tab === "upsell"     && <UpsellTab />}
    </div>
  );
}
