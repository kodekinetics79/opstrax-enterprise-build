import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { customersApi } from "@/services/customersApi";
import { contractsApi } from "@/services/contractsApi";
import { customerEtaApi } from "@/services/customerEtaApi";
import { exportCsv, LoadingState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Live builders ─────────────────────────────────────────────────────────────

function buildHealthRows(customers: AnyRecord[], contracts: AnyRecord[]): AnyRecord[] {
  return customers.map((c, i) => ({
    id: c.id ?? i + 1,
    name: String(c.name ?? c.companyName ?? ""),
    status: String(c.status ?? "Healthy"),
    healthScore: Number(c.healthScore ?? c.slaHealthScore ?? 0),
    slaCompliance: Number(c.slaHealthScore ?? c.healthScore ?? 0),
    atRisk: /risk/i.test(String(c.status ?? "")) || Number(c.healthScore ?? c.slaHealthScore ?? 0) < 80,
    accountManager: String(c.accountManager ?? "Ops"),
    renewalDate: String(c.renewalDate ?? contracts.find((ctr) => String(ctr.customerName ?? ctr.customer ?? "") === String(c.name ?? c.companyName ?? ""))?.expiryDate ?? "—"),
    monthlyRevenue: Number(c.revenueMtd ?? 0),
    currency: String(c.currency ?? "SAR"),
    activeContracts: Number(c.activeContracts ?? 1),
  }));
}

function buildFollowUpRows(customers: AnyRecord[], contracts: AnyRecord[]): AnyRecord[] {
  return customers.slice(0, 4).map((c, i) => ({
    id: c.id ?? i + 1,
    company: String(c.name ?? c.companyName ?? ""),
    contactPerson: String(c.contactName ?? c.primaryContact ?? "—"),
    // Type derived from the account's real state: at-risk accounts need an SLA
    // review; healthy ones get a renewal touch. No row-parity guessing.
    followUpType: /risk/i.test(String(c.status ?? "")) || Number(c.healthScore ?? c.slaHealthScore ?? 0) < 80 ? "SLA Review" : "Renewal Discussion",
    priority: /risk/i.test(String(c.status ?? "")) ? "High" : "Medium",
    dueDate: String(c.renewalDate ?? contracts[i % Math.max(contracts.length, 1)]?.expiryDate ?? "—"),
    assignedRep: String(c.accountManager ?? "Ops"),
    notes: "Derived from live customer and contract state.",
    status: /risk/i.test(String(c.status ?? "")) ? "Pending" : "In Progress",
  }));
}

function buildSupportRows(comms: AnyRecord[]): AnyRecord[] {
  return comms.slice(0, 10).map((c, i) => ({
    id: c.id ?? i + 1,
    ticketId: String(c.trackingCode ?? c.jobNumber ?? "—"),
    customer: String(c.customerName ?? "Customer"),
    shipment: String(c.jobNumber ?? "—"),
    issueType: String(c.messageType ?? "Communication"),
    priority: String(c.status ?? "Medium"),
    slaTimer: String(c.sentAt ? "Live" : "Pending"),
    assignedTeam: String(c.channel ?? "Customer Ops"),
    status: String(c.status ?? "Open"),
    createdDate: String(c.sentAt ?? ""),
  }));
}

function buildRenewalRows(contracts: AnyRecord[]): AnyRecord[] {
  return contracts.map((c, i) => ({
    id: c.id ?? i + 1,
    contractId: String(c.contractCode ?? c.contractId ?? "—"),
    customer: String(c.customerName ?? c.customer ?? ""),
    currentValue: Number(c.currentValue ?? c.contractValue ?? c.baseRate ?? 0),
    renewalRisk: /risk|expiring/i.test(String(c.status ?? c.displayStatus ?? "")) ? "High" : "Low",
    expiryDate: String(c.expiryDate ?? c.endDate ?? "—"),
    renewalOwner: String(c.owner ?? c.renewalOwner ?? "Ops"),
    stage: String(c.status ?? "Monitoring"),
  }));
}

function buildUpsellRows(customers: AnyRecord[]): AnyRecord[] {
  return customers.slice(0, 6).map((c, i) => {
    const service = String(c.industry ?? c.serviceType ?? "FTL");
    return {
      id: c.id ?? i + 1,
      customer: String(c.name ?? c.companyName ?? ""),
      currentService: service,
      // Suggested cross-sell keyed off the real current service, not row parity.
      upsellOpportunity: /ftl|full|truck/i.test(service) ? "Add Last Mile" : "Add Visibility",
      accountMrr: Number(c.revenueMtd ?? c.monthlyRevenue ?? 0),
      healthScore: Number(c.healthScore ?? c.slaHealthScore ?? 0),
      owner: String(c.accountManager ?? "Ops"),
      status: /risk/i.test(String(c.status ?? "")) ? "Retention First" : "Qualified",
    };
  });
}

const healthApi = () => Promise.all([customersApi.list(), contractsApi.list()]).then(([customers, contracts]) => buildHealthRows(customers as AnyRecord[], contracts as AnyRecord[]));
const followUpsApi = () => Promise.all([customersApi.list(), contractsApi.list()]).then(([customers, contracts]) => buildFollowUpRows(customers as AnyRecord[], contracts as AnyRecord[]));
const supportApi = () => customerEtaApi.communications().then((rows) => buildSupportRows(rows as AnyRecord[]));
const renewalsApi = () => contractsApi.list().then((rows) => buildRenewalRows(rows as AnyRecord[]));
const upsellApi = () => customersApi.list().then((rows) => buildUpsellRows(rows as AnyRecord[]));

// ── Badge helpers ─────────────────────────────────────────────────────────────

function PriorityBadge({ priority }: { priority: string }) {
  const cls =
    priority === "High" || priority === "Critical" ? "bg-red-50 border-red-200 text-red-700" :
    priority === "Medium" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{priority}</span>;
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Healthy" || status === "Completed" || status === "Renewal Sent" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Open" || status === "Overdue" || status === "High" ? "bg-red-50 border-red-200 text-red-700" :
    status === "In Progress" || status === "Negotiating" || status === "Interested" || status === "Pitched" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "At Risk" || status === "Pending" || status === "Monitoring" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function ScoreBar({ score }: { score: number }) {
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
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Accounts", val: rows.length },
          { label: "At Risk", val: atRisk, accent: "text-red-600" },
          { label: "Avg Health", val: rows.length ? `${(rows.reduce((s, r) => s + Number(r.healthScore ?? 0), 0) / rows.length).toFixed(1)}` : "—", accent: "text-violet-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
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
                  <td className="px-4 py-3"><ScoreBar score={Number(r.healthScore ?? 0)} /></td>
                  <td className="px-4 py-3"><ScoreBar score={Number(r.slaCompliance ?? 0)} /></td>
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
  const q = useQuery({ queryKey: ["account-health", "follow-ups"], queryFn: followUpsApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const overdue = rows.filter((r) => r.status === "Overdue").length;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Follow-ups", val: rows.length },
          { label: "Overdue", val: overdue, accent: "text-red-600" },
          { label: "Pending", val: rows.filter((r) => r.status === "Pending").length, accent: "text-amber-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      {rows.length === 0 ? <EmptyState title="No follow-ups scheduled" /> : (
        <div className="panel overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Company", "Contact", "Follow-up Type", "Priority", "Due Date", "Assigned", "Status"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(r.company ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-600">{String(r.contactPerson ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.followUpType ?? "—")}</td>
                    <td className="px-4 py-3"><PriorityBadge priority={String(r.priority ?? "Medium")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(r.dueDate ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(r.assignedRep ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(r.status ?? "Pending")} /></td>
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

function SupportTicketsTab() {
  const q = useQuery({ queryKey: ["account-health", "support"], queryFn: supportApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const open = rows.filter((r) => r.status === "Open").length;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Tickets", val: rows.length },
          { label: "Open", val: open, accent: "text-red-600" },
          { label: "In Progress", val: rows.filter((r) => r.status === "In Progress").length, accent: "text-blue-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      <div className="panel overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50">
                {["Ticket", "Customer", "Issue", "Priority", "SLA Timer", "Assigned Team", "Status"].map((h) => (
                  <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium text-slate-900">{String(r.ticketId ?? "--")}</td>
                  <td className="px-4 py-3 text-slate-700">{String(r.customer ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-600">{String(r.issueType ?? "—")}</td>
                  <td className="px-4 py-3"><PriorityBadge priority={String(r.priority ?? "Medium")} /></td>
                  <td className="px-4 py-3 text-xs font-medium text-amber-700">{String(r.slaTimer ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.assignedTeam ?? "—")}</td>
                  <td className="px-4 py-3"><StatusBadge status={String(r.status ?? "Open")} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function RenewalsTab() {
  const q = useQuery({ queryKey: ["account-health", "renewals"], queryFn: renewalsApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const negotiating = rows.filter((r) => r.stage === "Negotiating").length;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Renewal Pipeline", val: rows.length },
          { label: "Negotiating", val: negotiating, accent: "text-amber-600" },
          { label: "Total At Risk", val: rows.filter((r) => r.renewalRisk === "High").length, accent: "text-red-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      <div className="panel overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50">
                {["Contract", "Customer", "Current Value", "Renewal Risk", "Expiry", "Owner", "Stage"].map((h) => (
                  <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium text-slate-900">{String(r.contractId ?? "--")}</td>
                  <td className="px-4 py-3 text-slate-700">{String(r.customer ?? "—")}</td>
                  <td className="px-4 py-3 text-slate-700">SAR {Number(r.currentValue ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-3"><PriorityBadge priority={String(r.renewalRisk ?? "Low")} /></td>
                  <td className="px-4 py-3 text-xs text-slate-500">{String(r.expiryDate ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-600">{String(r.renewalOwner ?? "—")}</td>
                  <td className="px-4 py-3"><StatusBadge status={String(r.stage ?? "Monitoring")} /></td>
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
  const q = useQuery({ queryKey: ["account-health", "upsell"], queryFn: upsellApi });
  const rows = (q.data ?? []) as AnyRecord[];
  const totalMrr = rows.reduce((s, r) => s + Number(r.accountMrr ?? 0), 0);
  const avgHealth = rows.length ? Math.round(rows.reduce((s, r) => s + Number(r.healthScore ?? 0), 0) / rows.length) : 0;
  if (q.isLoading) return <LoadingState />;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Expansion Candidates", val: rows.length },
          { label: "Total Account MRR", val: `SAR ${totalMrr.toLocaleString()}`, accent: "text-teal-600" },
          { label: "Avg Health", val: rows.length ? String(avgHealth) : "—", accent: "text-violet-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-36">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>
      <div className="panel overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50">
                {["Customer", "Current Service", "Suggested Upsell", "Account MRR", "Health", "Owner", "Status"].map((h) => (
                  <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r, i) => (
                <tr key={String(r.id ?? i)} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium text-slate-900">{String(r.customer ?? "—")}</td>
                  <td className="px-4 py-3 text-xs text-slate-600">{String(r.currentService ?? "—")}</td>
                  <td className="px-4 py-3 text-slate-700">{String(r.upsellOpportunity ?? "—")}</td>
                  <td className="px-4 py-3 font-medium text-teal-700">SAR {Number(r.accountMrr ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-3"><ScoreBar score={Number(r.healthScore ?? 0)} /></td>
                  <td className="px-4 py-3 text-xs text-slate-600">{String(r.owner ?? "—")}</td>
                  <td className="px-4 py-3"><StatusBadge status={String(r.status ?? "Identified")} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
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
  const qc = useQueryClient();
  const defaultTab = (ROUTE_TAB[pathname] as Tab) ?? "health";
  const [tab, setTab] = useState<Tab>(defaultTab);

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
    "health":     "Customer health scores, SLA compliance, NPS and at-risk account monitoring",
    "follow-ups": "Scheduled outreach — renewal discussions, upsell calls, and SLA review meetings",
    "tickets":    "Open customer support issues with SLA timers and escalation status",
    "renewals":   "Contracts approaching expiry — renewal pipeline, negotiation stage and risk level",
    "upsell":     "Upsell opportunities identified across existing accounts with probability and pipeline value",
  };

  return (
    <div className="flex flex-col gap-6 py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{titles[tab]}</h1>
          <p className="text-sm text-slate-500 mt-0.5">{descriptions[tab]}</p>
        </div>
        <button type="button" className="btn-secondary text-sm" onClick={exportActive}>Export CSV</button>
      </div>

      {/* Tabs */}
      <div className="panel flex gap-1 p-1.5 flex-wrap">
        {TABS.map((t) => (
          <button key={t.key} type="button" onClick={() => setTab(t.key)}
            className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
              tab === t.key ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"
            }`}>
            {t.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      {tab === "health"     && <AccountHealthTab />}
      {tab === "follow-ups" && <FollowUpsTab />}
      {tab === "tickets"    && <SupportTicketsTab />}
      {tab === "renewals"   && <RenewalsTab />}
      {tab === "upsell"     && <UpsellTab />}
    </div>
  );
}
