import { useState } from "react";
import { tokens, chart } from "@/styles/tokens";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

const oppApi = {
  list: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/opportunities")).then((rows) =>
      rows.map((r) => ({
        ...r,
        opportunityId: r.opportunityId ?? r.code ?? `OPP-${String(r.id)}`,
        customerLead: r.customerLead ?? r.customer_lead ?? r.title ?? "",
        estimatedContractValue: Number(r.estimatedContractValue ?? r.amount ?? 0),
        currency: r.currency ?? "SAR",
        probability: Number(r.probability ?? 50),
        expectedCloseDate: r.expectedCloseDate ?? r.due_at ?? "",
        stage: r.stage ?? r.status ?? "Discovery",
        owner: r.owner ?? "",
        competitor: r.competitor ?? "",
        weightedValue: Math.round(Number(r.estimatedContractValue ?? r.amount ?? 0) * Number(r.probability ?? 50) / 100),
        riskLevel: Number(r.probability ?? 50) < 40 ? "High" : Number(r.probability ?? 50) < 60 ? "Medium" : "Low",
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

function ProbabilityBar({ pct }: { pct: number }) {
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

function fmtCurrency(val: number, currency: string): string {
  return `${currency} ${val >= 1_000_000 ? `${(val / 1_000_000).toFixed(2)}M` : val.toLocaleString()}`;
}

// ── Create Opportunity Modal ──────────────────────────────────────────────────

function CreateOppModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ title: "", probability: "50", expectedCloseDate: "", owner: "" });
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
  const [stageFilter, setStageFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const listQ = useQuery({ queryKey: ["opportunities", "list"], queryFn: oppApi.list, refetchInterval: 60_000 });
  const opps = (listQ.data ?? []) as AnyRecord[];

  const active = opps.filter((o) => !["Closed Won", "Closed Lost"].includes(String(o.stage)));
  const totalPipeline = active.reduce((s, o) => s + Number(o.estimatedContractValue ?? 0), 0);
  const weightedPipeline = active.reduce((s, o) => s + Number(o.weightedValue ?? 0), 0);
  const wonThisMonth = opps.filter((o) => o.stage === "Closed Won").length;

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
    value: opps.filter((o) => o.stage === s).reduce((acc, o) => acc + Number(o.estimatedContractValue ?? 0), 0) / 1000,
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
    <div className="flex flex-col gap-6 py-6">
      {showCreate && <CreateOppModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Opportunities</h1>
          <p className="text-sm text-slate-500 mt-0.5">Active deal pipeline — weighted revenue forecast, probability tracking, and competitor intelligence</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("opportunities", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Opportunity</button>
        </div>
      </div>

      <div className="panel grid gap-3 md:grid-cols-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Deal bridge</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Qualified opportunities stay connected to live contract and pricing work.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Live pipeline</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">This board reflects your current opportunities in real time.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Risk visibility</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Probability and competitor pressure remain visible for each live deal.</p>
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Pipeline",    val: `SAR ${(totalPipeline / 1_000_000).toFixed(2)}M`, accent: "text-teal-600" },
          { label: "Weighted Pipeline", val: `SAR ${(weightedPipeline / 1_000_000).toFixed(2)}M`, accent: "text-violet-600" },
          { label: "Active Deals",      val: active.length, accent: "text-blue-600" },
          { label: "Closed Won",        val: wonThisMonth, accent: "text-teal-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-36">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Pipeline funnel chart */}
      <div className="panel p-5">
        <h2 className="text-sm font-semibold text-slate-900 mb-4">Pipeline by Stage (count · value SAR 000s)</h2>
        <ResponsiveContainer width="100%" height={180}>
          <BarChart data={chartData} margin={{ top: 4, right: 8, left: -20, bottom: 4 }}>
            <CartesianGrid stroke="rgba(0,0,0,0.05)" strokeDasharray="3 3" />
            <XAxis dataKey="stage" tick={{ fontSize: 10 }} />
            <YAxis tick={{ fontSize: 10 }} />
            <Tooltip contentStyle={{ background: tokens.surface, border: `1px solid ${tokens.border}`, borderRadius: 8, fontSize: 12 }}
              formatter={(val, name) => [String(val), name === "count" ? "Deals" : "Value (000s)"]} />
            <Bar dataKey="count" fill={chart.teal500} radius={[3, 3, 0, 0]} name="count" />
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap gap-2 items-center">
        <div className="flex gap-1.5 flex-wrap">
          {["All", ...STAGES.slice(0, 5)].map((f) => (
            <button key={f} type="button" onClick={() => setStageFilter(f)}
              className={`px-2.5 py-1.5 rounded-lg text-xs font-medium border transition-colors ${
                stageFilter === f ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}>{f}</button>
          ))}
        </div>
        <input type="search" placeholder="Search customer, owner…" value={search} onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-52" />
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No opportunities match your filters" /> : (
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
                    <td className="px-4 py-3 font-medium text-slate-900">{fmtCurrency(Number(o.estimatedContractValue ?? 0), String(o.currency ?? "SAR"))}</td>
                    <td className="px-4 py-3 text-teal-700 font-medium text-xs">{fmtCurrency(Number(o.weightedValue ?? 0), String(o.currency ?? "SAR"))}</td>
                    <td className="px-4 py-3"><ProbabilityBar pct={Number(o.probability ?? 0)} /></td>
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
              <ProbabilityBar pct={Number(selected.probability ?? 0)} />
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Opp. ID", String(selected.opportunityId ?? "")],
                ["Est. Value", fmtCurrency(Number(selected.estimatedContractValue ?? 0), String(selected.currency ?? "SAR"))],
                ["Weighted", fmtCurrency(Number(selected.weightedValue ?? 0), String(selected.currency ?? "SAR"))],
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
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Strategic Insight</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {String(selected.stage) === "Negotiation" ? `Competitor is ${String(selected.competitor)}. Focus on SLA and reliability differentiation to win.` :
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
