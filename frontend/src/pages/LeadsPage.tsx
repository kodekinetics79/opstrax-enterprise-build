import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { requireCommercialModuleRecords } from "@/services/commercialModulePayload";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { CommercialDisclosure, CommercialMetricRail, CommercialToolbar, RevenueWorkspaceHeader } from "@/components/CommercialWorkspace";
import type { AnyRecord } from "@/types";

const leadsApi = {
  list: () =>
    unwrap<unknown>(apiClient.get("/api/leads")).then((payload) =>
      requireCommercialModuleRecords(payload, "leads").map((r) => ({
        ...r,
        leadId: r.leadId ?? r.code ?? `LD-${String(r.id)}`,
        company: r.company ?? r.title ?? "",
        contactPerson: r.contactPerson ?? r.contact_person ?? "",
        industry: r.industry ?? "",
        source: r.source ?? "",
        requiredService: r.requiredService ?? r.required_service ?? "",
        estimatedMonthlyLoads: r.estimatedMonthlyLoads ?? r.estimated_monthly_loads ?? 0,
        cityCountry: r.cityCountry ?? r.locationName ?? "",
        assignedRep: r.assignedRep ?? r.ownerName ?? "",
        nextFollowUp: r.nextFollowUp ?? r.dueAt ?? "",
      }))
    ),
  create: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/leads", body)),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

const PIPELINE_STAGES = ["New", "Contacted", "Discovery Scheduled", "Qualified", "Proposal Needed"] as const;

function StageBadge({ status }: { status: string }) {
  const cls =
    status === "Qualified" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Proposal Needed" ? "bg-violet-50 border-violet-200 text-violet-700" :
    status === "Discovery Scheduled" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "Contacted" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

// ── Create Lead Modal ─────────────────────────────────────────────────────────

function CreateLeadModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    title: "", contactPerson: "", industry: "", source: "Referral",
    requiredService: "FTL", estimatedMonthlyLoads: "", cityCountry: "", assignedRep: "", nextFollowUp: "",
  });
  const qc = useQueryClient();
  const mut = useMutation({
    mutationFn: () => leadsApi.create({ ...form, status: "New" } as unknown as AnyRecord),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["leads"] }); onSaved(); },
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="panel w-full max-w-lg p-6 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-base font-bold text-slate-900">Add Lead</h2>
        <div className="grid grid-cols-2 gap-3">
          {[
            { label: "Company Name*", key: "title", placeholder: "Jeddah Fresh Foods", full: true },
            { label: "Contact Person", key: "contactPerson", placeholder: "Hassan Bari" },
            { label: "Industry", key: "industry", placeholder: "FMCG" },
            { label: "City, Country", key: "cityCountry", placeholder: "Jeddah, KSA" },
            { label: "Est. Monthly Loads", key: "estimatedMonthlyLoads", placeholder: "96" },
            { label: "Assigned Rep", key: "assignedRep", placeholder: "Maya Patel" },
            { label: "Next Follow-up", key: "nextFollowUp", placeholder: "2026-09-15", type: "date" },
          ].map(({ label, key, placeholder, full, type }) => (
            <div key={key} className={full ? "col-span-2" : ""}>
              <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
              <input type={type ?? "text"}
                className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
                placeholder={placeholder}
                value={String(form[key as keyof typeof form])}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
              />
            </div>
          ))}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Source</label>
            <select title="Lead source" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.source} onChange={(e) => setForm((f) => ({ ...f, source: e.target.value }))}>
              {["Referral", "LinkedIn", "Campaign", "Inbound", "Cold Call", "Conference"].map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Required Service</label>
            <select title="Required service" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.requiredService} onChange={(e) => setForm((f) => ({ ...f, requiredService: e.target.value }))}>
              {["FTL", "LTL", "Last Mile", "Reefer FTL", "Flatbed", "Temperature Controlled", "Cross Dock"].map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" disabled={!form.title || mut.isPending} className="btn-primary text-sm" onClick={() => mut.mutate()}>
            {mut.isPending ? "Saving…" : "Add Lead"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function LeadsPage() {
  const [stageFilter, setStageFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const listQ = useQuery({ queryKey: ["leads", "list"], queryFn: leadsApi.list, refetchInterval: 60_000 });
  const leads = (listQ.data ?? []) as AnyRecord[];

  const summary = {
    total: leads.length,
    active: leads.filter((l) => !["Closed", "Lost"].includes(String(l.status))).length,
    qualified: leads.filter((l) => l.status === "Qualified" || l.status === "Proposal Needed").length,
    discovery: leads.filter((l) => l.status === "Discovery Scheduled").length,
  };

  const filtered = leads.filter((l) => {
    if (stageFilter !== "All" && l.status !== stageFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(l.company ?? l.title ?? "").toLowerCase().includes(q) ||
        String(l.contactPerson ?? "").toLowerCase().includes(q) ||
        String(l.industry ?? "").toLowerCase().includes(q) ||
        String(l.assignedRep ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  // Pipeline lane counts
  const laneCounts = PIPELINE_STAGES.reduce<Record<string, number>>((acc, stage) => {
    acc[stage] = leads.filter((l) => l.status === stage).length;
    return acc;
  }, {});

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) {
    return (
      <ErrorState
        message={(listQ.error as Error)?.message ?? "Unable to load live leads."}
      />
    );
  }

  return (
    <div className="page-stack min-w-0">
      {showCreate && <CreateLeadModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}

      <RevenueWorkspaceHeader
        title="Leads"
        description="Qualify new Saudi customer demand, ownership, service fit and the next follow-up."
        activeStage="leads"
        actions={<div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("leads", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>Add Lead</button>
        </div>}
      />

      <CommercialMetricRail metrics={[
        { label: "Total leads", value: summary.total, detail: `${filtered.length} shown`, active: stageFilter === "All", onClick: () => setStageFilter("All") },
        { label: "Active", value: summary.active, detail: "open pipeline", tone: "info" },
        { label: "Discovery", value: summary.discovery, detail: `${laneCounts["Discovery Scheduled"] ?? 0} scheduled`, tone: "info", active: stageFilter === "Discovery Scheduled", onClick: () => setStageFilter("Discovery Scheduled") },
        { label: "Qualified", value: summary.qualified, detail: "quote-ready", tone: "good", active: stageFilter === "Qualified", onClick: () => setStageFilter("Qualified") },
        { label: "Proposal needed", value: laneCounts["Proposal Needed"] ?? 0, detail: "needs action", tone: "warn", active: stageFilter === "Proposal Needed", onClick: () => setStageFilter("Proposal Needed") },
      ]} />

      {/* Search / filter bar */}
      <CommercialToolbar
        filters={<>
          {["All", ...PIPELINE_STAGES].map((f) => (
            <button key={f} type="button" onClick={() => setStageFilter(f)}
              className={`filter-chip shrink-0 ${
                stageFilter === f
                  ? "filter-chip-active"
                  : ""
              }`}>
              {f}
            </button>
          ))}
        </>}
        meta={`${filtered.length} of ${leads.length}`}
        search={<input type="search" aria-label="Search leads" placeholder="Search company, rep, industry…" value={search} onChange={(e) => setSearch(e.target.value)} className="field" />}
      />

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No leads match your filters" subtitle={leads.length ? "Clear a stage or search filter to see the full pipeline." : "Add the first prospect when customer demand is confirmed."} action={!leads.length ? <button type="button" className="btn-primary" onClick={() => setShowCreate(true)}>Add Lead</button> : undefined} /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Company", "Contact", "Industry", "Service", "Est. Loads/Mo", "Source", "Stage", "Assigned", "Follow-up"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((l, i) => (
                  <tr key={String(l.id ?? i)} className={`hover:bg-slate-50 cursor-pointer ${selected?.id === l.id ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === l.id ? null : l)}>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(l.company ?? l.title ?? "--")}</p>
                      <p className="text-xs text-slate-400">{String(l.leadId ?? "")}</p>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{String(l.contactPerson ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(l.industry ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(l.requiredService ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700 font-medium">{Number(l.estimatedMonthlyLoads ?? 0) > 0 ? String(l.estimatedMonthlyLoads) : "—"}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(l.source ?? "—")}</td>
                    <td className="px-4 py-3"><StageBadge status={String(l.status ?? "New")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(l.assignedRep ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(l.nextFollowUp ?? "—")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <CommercialDisclosure>
        <p><strong>Pipeline integrity:</strong> This register reflects persisted lead records returned by the service. Missing records are not presented as an empty sales pipeline.</p>
        <p><strong>Workflow boundary:</strong> Lead-to-opportunity and quotation conversion is not automated in this build; each register remains a separate saved workflow.</p>
      </CommercialDisclosure>

      {/* Detail drawer */}
      {selected && (
        <div className="fixed inset-0 z-40 flex justify-end" onClick={() => setSelected(null)}>
          <div className="bg-slate-950 w-full max-w-sm h-full flex flex-col overflow-y-auto shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
              <span className="text-sm font-semibold text-white">{String(selected.company ?? selected.title)}</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6"><StageBadge status={String(selected.status ?? "New")} /></div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Lead ID", String(selected.leadId ?? "")],
                ["Contact", String(selected.contactPerson ?? "—")],
                ["Industry", String(selected.industry ?? "—")],
                ["Source", String(selected.source ?? "—")],
                ["Service Needed", String(selected.requiredService ?? "—")],
                ["Est. Loads/Mo", String(selected.estimatedMonthlyLoads ?? "—")],
                ["Location", String(selected.cityCountry ?? "—")],
                ["Assigned Rep", String(selected.assignedRep ?? "—")],
                ["Follow-up", String(selected.nextFollowUp ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Next Step</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {String(selected.status) === "Qualified" ? "Generate a formal rate proposal and send to prospect." :
                 String(selected.status) === "Proposal Needed" ? "Prepare and send rate proposal immediately — prospect is ready." :
                 String(selected.status) === "Discovery Scheduled" ? "Confirm meeting details and prepare qualification questions." :
                 String(selected.status) === "Contacted" ? "Follow up and schedule a discovery call." :
                 "Initiate first contact — reach out via the assigned channel."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
