import { useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { requireCommercialModuleRecords } from "@/services/commercialModulePayload";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { CommercialDisclosure, CommercialMetricRail, CommercialToolbar, RevenueWorkspaceHeader } from "@/components/CommercialWorkspace";
import { useTenantCurrency } from "@/hooks/useTenantRegion";
import type { AnyRecord } from "@/types";

function persistedNumber(value: unknown): number | null {
  if (value == null || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

const oppApi = {
  list: () =>
    unwrap<unknown>(apiClient.get("/api/opportunities")).then((payload) =>
      requireCommercialModuleRecords(payload, "opportunities").map((r) => ({
        ...r,
        opportunityId: r.opportunityId ?? r.code ?? `OPP-${String(r.id)}`,
        customerLead: r.customerLead ?? r.customer_lead ?? r.title ?? "",
        estimatedContractValue: persistedNumber(r.estimatedContractValue ?? r.amount),
        currency: r.currency ?? "",
        probability: persistedNumber(r.probability),
        expectedCloseDate: r.expectedCloseDate ?? r.dueAt ?? "",
        stage: r.stage ?? r.status ?? "Discovery",
        owner: r.owner ?? r.ownerName ?? "",
        competitor: r.competitor ?? "",
        weightedValue: persistedNumber(r.estimatedContractValue ?? r.amount) != null && persistedNumber(r.probability) != null
          ? Math.round(Number(r.estimatedContractValue ?? r.amount) * Number(r.probability) / 100)
          : null,
        riskLevel: persistedNumber(r.probability) == null ? "Not assessed" : Number(r.probability) < 40 ? "High" : Number(r.probability) < 60 ? "Medium" : "Low",
      }))
    ),
  create: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/opportunities", body)),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

const STAGES = ["Discovery", "Requirements Collected", "Rate Proposal Sent", "Negotiation", "Contracting", "Closed Won", "Closed Lost"] as const;

function StageBadge({ stage }: { stage: string }) {
  const cls =
    stage === "Closed Won" ? "bg-teal-50 border-teal-200 text-teal-700" :
    stage === "Closed Lost" ? "bg-slate-100 border-slate-200 text-slate-500" :
    stage === "Negotiation" ? "bg-violet-50 border-violet-200 text-violet-700" :
    stage === "Contracting" ? "bg-blue-50 border-blue-200 text-blue-700" :
    stage === "Rate Proposal Sent" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{stage}</span>;
}

function ProbabilityBar({ pct }: { pct: number | null }) {
  if (pct == null) return <span className="text-xs text-slate-500">Not recorded</span>;
  const color = pct >= 70 ? "bg-teal-500" : pct >= 50 ? "bg-amber-400" : "bg-red-400";
  return (
    <div className="flex items-center gap-2 text-xs">
      <div className="w-16 h-1.5 rounded-full bg-slate-100 overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="font-medium text-slate-700">{pct}%</span>
    </div>
  );
}

function fmtCurrency(val: number | null, currency: string): string {
  if (val == null || !currency) return "—";
  return `${currency} ${val >= 1_000_000 ? `${(val / 1_000_000).toFixed(2)}M` : val.toLocaleString()}`;
}

// ── Create Opportunity Modal ──────────────────────────────────────────────────

function CreateOppModal({ onClose, onSaved, defaultCurrency }: { onClose: () => void; onSaved: () => void; defaultCurrency: string }) {
  const [form, setForm] = useState({ title: "", estimatedContractValue: "", currency: defaultCurrency, probability: "50", expectedCloseDate: "", owner: "" });
  const qc = useQueryClient();
  const mut = useMutation({
    mutationFn: () => oppApi.create({ ...form, status: "Discovery" } as unknown as AnyRecord),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["opportunities"] }); onSaved(); },
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="panel w-full max-w-md p-6 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-base font-bold text-slate-900">New Opportunity</h2>
        <div className="grid grid-cols-2 gap-3">
          {[
            { label: "Customer / Lead*", key: "title", placeholder: "Jeddah Fresh Foods", full: true },
            { label: "Estimated Contract Value", key: "estimatedContractValue", placeholder: "250000", type: "number" },
            { label: "Probability (%)", key: "probability", placeholder: "50" },
            { label: "Expected Close", key: "expectedCloseDate", placeholder: "2026-07-15", type: "date" },
            { label: "Owner", key: "owner", placeholder: "Maya Patel", full: true },
          ].map(({ label, key, placeholder, full, type }) => (
            <div key={key} className={full ? "col-span-2" : ""}>
              <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
              <input type={type ?? "text"} className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
                placeholder={placeholder} value={String(form[key as keyof typeof form])}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))} />
            </div>
          ))}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Currency</label>
            <select title="Currency" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.currency} onChange={(e) => setForm((f) => ({ ...f, currency: e.target.value }))}>
              <option value="">Select currency</option>
              {["USD", "CAD", "SAR", "AED", "PKR", "EUR", "GBP"].map((currency) => <option key={currency}>{currency}</option>)}
            </select>
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" disabled={!form.title || mut.isPending} className="btn-primary text-sm" onClick={() => mut.mutate()}>
            {mut.isPending ? "Saving…" : "Create"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function OpportunitiesPage() {
  const tenantCurrency = useTenantCurrency() ?? "";
  const [stageFilter, setStageFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const listQ = useQuery({ queryKey: ["opportunities", "list"], queryFn: oppApi.list, refetchInterval: 60_000 });
  const opps = (listQ.data ?? []) as AnyRecord[];

  const active = opps.filter((o) => !["Closed Won", "Closed Lost"].includes(String(o.stage)));
  const valuedActive = active.filter((o) => o.estimatedContractValue != null && o.currency);
  const activeCurrencies = [...new Set(valuedActive.map((o) => String(o.currency)))];
  const totalPipeline = valuedActive.reduce((sum, o) => sum + Number(o.estimatedContractValue), 0);
  const weightedActive = active.filter((o) => o.weightedValue != null && o.currency);
  const weightedCurrencies = [...new Set(weightedActive.map((o) => String(o.currency)))];
  const weightedPipeline = weightedActive.reduce((sum, o) => sum + Number(o.weightedValue), 0);
  const totalPipelineLabel = activeCurrencies.length === 1 ? fmtCurrency(totalPipeline, activeCurrencies[0]) : activeCurrencies.length > 1 ? "Multiple currencies" : "—";
  const weightedPipelineLabel = weightedCurrencies.length === 1 ? fmtCurrency(weightedPipeline, weightedCurrencies[0]) : weightedCurrencies.length > 1 ? "Multiple currencies" : "—";
  const won = opps.filter((o) => o.stage === "Closed Won").length;

  const filtered = opps.filter((o) => {
    if (stageFilter !== "All" && o.stage !== stageFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(o.customerLead ?? o.title ?? "").toLowerCase().includes(q) ||
        String(o.owner ?? "").toLowerCase().includes(q) ||
        String(o.competitor ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  const chartData = STAGES.slice(0, 5).map((s) => ({
    stage: s.replace("Requirements Collected", "Req. Collected").replace("Rate Proposal Sent", "Proposal Sent"),
    count: opps.filter((o) => o.stage === s).length,
  }));

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) {
    return (
      <ErrorState
        message={(listQ.error as Error)?.message ?? "Unable to load live opportunities."}
      />
    );
  }

  return (
    <div className="page-stack min-w-0">
      {showCreate && <CreateOppModal defaultCurrency={tenantCurrency} onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}

      <RevenueWorkspaceHeader
        title="Opportunities"
        description="Prioritize qualified deals by value, probability, close date and accountable owner."
        activeStage="opportunities"
        actions={<div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("opportunities", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Opportunity</button>
        </div>}
      />

      <CommercialMetricRail metrics={[
        { label: "Active deals", value: active.length, detail: `${filtered.length} shown`, tone: "info", active: stageFilter === "All", onClick: () => setStageFilter("All") },
        { label: "Pipeline", value: totalPipelineLabel, detail: activeCurrencies.length > 1 ? "kept separate" : "recorded value", tone: "good" },
        { label: "Weighted", value: weightedPipelineLabel, detail: "probability-adjusted", tone: "info" },
        { label: "Proposal sent", value: chartData.find((item) => item.stage === "Proposal Sent")?.count ?? 0, detail: "awaiting response", tone: "warn", active: stageFilter === "Rate Proposal Sent", onClick: () => setStageFilter("Rate Proposal Sent") },
        { label: "Closed won", value: won, detail: "recorded deals", tone: "good", active: stageFilter === "Closed Won", onClick: () => setStageFilter("Closed Won") },
      ]} />

      {/* Filters */}
      <CommercialToolbar
        filters={<>
          {["All", ...STAGES.slice(0, 5)].map((f) => (
            <button key={f} type="button" onClick={() => setStageFilter(f)}
              className={`filter-chip shrink-0 ${
                stageFilter === f
                  ? "filter-chip-active"
                  : ""
              }`}>{f}</button>
          ))}
        </>}
        meta={`${filtered.length} of ${opps.length}`}
        search={<input type="search" aria-label="Search opportunities" placeholder="Search customer, owner…" value={search} onChange={(e) => setSearch(e.target.value)} className="field" />}
      />

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No opportunities match your filters" subtitle={opps.length ? "Clear a stage or search filter to see the full deal register." : "Create the first opportunity after a prospect is qualified."} action={!opps.length ? <button type="button" className="btn-primary" onClick={() => setShowCreate(true)}>New Opportunity</button> : undefined} /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Opportunity", "Stage", "Est. Contract Value", "Weighted", "Probability", "Close Date", "Competitor", "Owner"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((o, i) => (
                  <tr key={String(o.id ?? i)} className={`hover:bg-slate-50 cursor-pointer ${selected?.id === o.id ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === o.id ? null : o)}>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(o.customerLead ?? o.title ?? "--")}</p>
                      <p className="text-xs text-slate-400">{String(o.opportunityId ?? "")}</p>
                    </td>
                    <td className="px-4 py-3"><StageBadge stage={String(o.stage ?? "Discovery")} /></td>
                    <td className="px-4 py-3 font-medium text-slate-900">{fmtCurrency(o.estimatedContractValue == null ? null : Number(o.estimatedContractValue), String(o.currency ?? ""))}</td>
                    <td className="px-4 py-3 text-teal-700 font-medium text-xs">{fmtCurrency(o.weightedValue == null ? null : Number(o.weightedValue), String(o.currency ?? ""))}</td>
                    <td className="px-4 py-3"><ProbabilityBar pct={o.probability == null ? null : Number(o.probability)} /></td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(o.expectedCloseDate ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(o.competitor ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(o.owner ?? "—")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <CommercialDisclosure>
        <p><strong>Persisted pipeline:</strong> Values, probabilities and competitor pressure appear only when they were recorded by the service.</p>
        <p><strong>Workflow boundary:</strong> Opportunity-to-contract and pricing conversion is not automated in this build.</p>
      </CommercialDisclosure>

      {/* Pipeline funnel chart */}
      <details className="panel p-3">
        <summary className="cursor-pointer text-sm font-semibold text-slate-900">Opportunities by stage (count)</summary>
        {active.length === 0 ? <p className="py-3 text-sm text-slate-500">No active opportunities are recorded.</p> : (
        <ResponsiveContainer width="100%" height={180}>
          <BarChart data={chartData} margin={{ top: 4, right: 8, left: -20, bottom: 4 }}>
            <CartesianGrid stroke="rgba(0,0,0,0.05)" strokeDasharray="3 3" />
            <XAxis dataKey="stage" tick={{ fontSize: 10 }} />
            <YAxis tick={{ fontSize: 10 }} />
            <Tooltip contentStyle={{ background: tokens.surface, border: `1px solid ${tokens.border}`, borderRadius: 8, fontSize: 12 }}
              formatter={(val) => [String(val), "Deals"]} />
            <Bar dataKey="count" fill={chart.teal500} radius={[3, 3, 0, 0]} name="count" />
          </BarChart>
        </ResponsiveContainer>
        )}
      </details>

      {/* Detail drawer */}
      {selected && (
        <div className="fixed inset-0 z-40 flex justify-end" onClick={() => setSelected(null)}>
          <div className="bg-slate-950 w-full max-w-sm h-full flex flex-col overflow-y-auto shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
              <span className="text-sm font-semibold text-white">{String(selected.customerLead ?? selected.title)}</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6"><StageBadge stage={String(selected.stage ?? "Discovery")} /></div>
            <div className="px-5 py-4 flex flex-col gap-3 border-b border-white/6">
              <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide">Win Probability</p>
              <ProbabilityBar pct={selected.probability == null ? null : Number(selected.probability)} />
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Opp. ID", String(selected.opportunityId ?? "")],
                ["Est. Value", fmtCurrency(selected.estimatedContractValue == null ? null : Number(selected.estimatedContractValue), String(selected.currency ?? ""))],
                ["Weighted", fmtCurrency(selected.weightedValue == null ? null : Number(selected.weightedValue), String(selected.currency ?? ""))],
                ["Loads/Mo", String(selected.expectedLoadsMonth ?? "—")],
                ["Close Date", String(selected.expectedCloseDate ?? "—")],
                ["Competitor", String(selected.competitor ?? "—")],
                ["Owner", String(selected.owner ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Workflow guidance</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {String(selected.stage) === "Negotiation" && selected.competitor ? `Recorded competitor: ${String(selected.competitor)}. Review SLA and reliability evidence before the next discussion.` :
                 String(selected.stage) === "Rate Proposal Sent" ? "Proposal is out — schedule a follow-up within 48 hours to address objections." :
                 String(selected.stage) === "Contracting" ? "Close to closed — escalate any legal blockers to accelerate contract signing." :
                 "Move this deal forward to the next stage to maintain pipeline velocity."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
