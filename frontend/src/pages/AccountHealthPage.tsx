import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, EmptyState } from "@/components/ui";
import { customers as seedCustomers, supportTickets as seedTickets, leads as seedLeads, contracts as seedContracts } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed builders ─────────────────────────────────────────────────────────────

function buildHealthSeed(): AnyRecord[] {
  return (seedCustomers as AnyRecord[]).map((c, i) => ({
    id: i + 1,
    name: String(c.companyName ?? c.name ?? ""),
    status: String(c.status ?? "Healthy") === "Healthy" ? "Healthy" : String(c.status ?? "Healthy"),
    healthScore: Number(c.healthScore ?? 85),
    slaCompliance: Number(c.healthScore ?? 85) - 2,
    npsEstimate: Math.round((Number(c.healthScore ?? 80) - 50) * 1.2),
    atRisk: Number(c.healthScore ?? 85) < 80,
    accountManager: String(c.accountManager ?? "Ops"),
    renewalDate: String(c.renewalDate ?? "2026-12-31"),
    monthlyRevenue: Number(c.revenueMtd ?? 0),
    currency: String(c.currency ?? "SAR"),
    activeContracts: Number(c.activeContracts ?? 1),
  }));
}

function buildFollowUpSeed(): AnyRecord[] {
  const reps = ["Maya Patel", "Omar Khan", "Sofia Cruz", "Avery Stone"];
  return (seedLeads as AnyRecord[]).map((l, i) => ({
    id: i + 1,
    company: String(l.company ?? ""),
    contactPerson: String(l.contactPerson ?? ""),
    followUpType: (["Renewal Discussion", "Upsell Call", "SLA Review", "Credit Check"] as const)[i % 4],
    priority: (["High", "Medium", "High", "Low"] as const)[i % 4],
    dueDate: String(l.nextFollowUp ?? "2026-06-25"),
    assignedRep: reps[i % reps.length],
    notes: "Scheduled follow-up in CRM pipeline.",
    status: (["Pending", "In Progress", "Overdue", "Completed"] as const)[i % 4],
  }));
}

function buildSupportSeed(): AnyRecord[] {
  return (seedTickets as AnyRecord[]).map((t, i) => ({
    id: i + 1,
    ticketId: String(t.ticketId ?? `TCK-${2200 + i}`),
    customer: String(t.customer ?? ""),
    shipment: String(t.shipment ?? ""),
    issueType: String(t.issueType ?? ""),
    priority: String(t.priority ?? "Medium"),
    slaTimer: String(t.slaTimer ?? "—"),
    assignedTeam: String(t.assignedTeam ?? "Customer Ops"),
    status: String(t.status ?? "Open"),
    createdDate: String(t.createdDate ?? ""),
  }));
}

function buildRenewalSeed(): AnyRecord[] {
  return (seedContracts as AnyRecord[]).map((c, i) => ({
    id: i + 1,
    contractId: String(c.contractId ?? `CON-${1001 + i}`),
    customer: String(c.customer ?? ""),
    currentValue: 1200000 - i * 200000,
    renewalRisk: (["Low", "High", "Medium"] as const)[i % 3],
    expiryDate: String(c.endDate ?? ""),
    renewalOwner: (["Maya Patel", "Omar Khan", "Sofia Cruz"] as const)[i % 3],
    stage: (["Monitoring", "Negotiating", "Renewal Sent"] as const)[i % 3],
  }));
}

function buildUpsellSeed(): AnyRecord[] {
  return (seedCustomers as AnyRecord[]).slice(0, 4).map((c, i) => ({
    id: i + 1,
    customer: String(c.companyName ?? c.name ?? ""),
    currentService: (["FTL", "Cold Chain", "Last Mile", "FTL + Cross Dock"] as const)[i % 4],
    upsellOpportunity: (["Add Last Mile", "Add Cold Chain", "Volume Uplift", "Cross-border Expansion"] as const)[i % 4],
    estimatedValue: [450000, 320000, 180000, 650000][i % 4],
    probability: [65, 48, 72, 55][i % 4],
    owner: (["Maya Patel", "Omar Khan", "Sofia Cruz", "Avery Stone"] as const)[i % 4],
    status: (["Identified", "Pitched", "Interested", "Proposal Ready"] as const)[i % 4],
  }));
}

// ── Customers API for health tab ───────────────────────────────────────────────

const healthApi = () => withFallback(
  unwrap<AnyRecord[]>(apiClient.get("/api/customers")).then((rows) =>
    rows.map((r) => ({
      id: r.id,
      name: String(r.name ?? ""),
      status: String(r.status ?? "Active"),
      healthScore: Number(r.slaHealthScore ?? 85),
      slaCompliance: Number(r.slaHealthScore ?? 85),
      npsEstimate: Math.round((Number(r.slaHealthScore ?? 80) - 50) * 1.2),
      atRisk: String(r.riskHeatScore) === "High",
      renewalDate: "—",
      monthlyRevenue: 0,
      currency: "SAR",
      activeContracts: Number(r.activeJobs ?? 1),
      accountManager: "—",
    }))
  ),
  () => buildHealthSeed()
);

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
                {["Customer", "Status", "Health Score", "SLA Compliance", "Est. NPS", "Active Contracts", "Account Manager"].map((h) => (
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
                  <td className="px-4 py-3 text-slate-700 font-medium">{Number(r.npsEstimate ?? 0) > 0 ? `+${String(r.npsEstimate)}` : String(r.npsEstimate ?? "—")}</td>
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
  const rows = buildFollowUpSeed();
  const overdue = rows.filter((r) => r.status === "Overdue").length;
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
  const rows = buildSupportSeed();
  const open = rows.filter((r) => r.status === "Open").length;
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
  const rows = buildRenewalSeed();
  const negotiating = rows.filter((r) => r.stage === "Negotiating").length;
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
  const rows = buildUpsellSeed();
  const totalValue = rows.reduce((s, r) => s + Number(r.estimatedValue ?? 0), 0);
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Upsell Opportunities", val: rows.length },
          { label: "Pipeline Value", val: `SAR ${(totalValue / 1_000_000).toFixed(2)}M`, accent: "text-teal-600" },
          { label: "Avg Probability", val: `${Math.round(rows.reduce((s, r) => s + Number(r.probability ?? 0), 0) / (rows.length || 1))}%`, accent: "text-violet-600" },
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
                {["Customer", "Current Service", "Upsell Opportunity", "Est. Value", "Probability", "Owner", "Status"].map((h) => (
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
                  <td className="px-4 py-3 font-medium text-teal-700">SAR {Number(r.estimatedValue ?? 0).toLocaleString()}</td>
                  <td className="px-4 py-3 text-slate-700 font-medium">{String(r.probability ?? 0)}%</td>
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

export function AccountHealthPage() {
  const { pathname } = useLocation();
  const defaultTab = (ROUTE_TAB[pathname] as Tab) ?? "health";
  const [tab, setTab] = useState<Tab>(defaultTab);

  function exportActive() {
    if (tab === "health") exportCsv("account-health", buildHealthSeed());
    else if (tab === "follow-ups") exportCsv("follow-ups", buildFollowUpSeed());
    else if (tab === "tickets") exportCsv("support-tickets", buildSupportSeed());
    else if (tab === "renewals") exportCsv("renewals", buildRenewalSeed());
    else exportCsv("upsell-opportunities", buildUpsellSeed());
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
