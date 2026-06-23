import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { leads as seedLeads } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed ────────────────────────────────────────────────────────────────────

function buildSeed(): AnyRecord[] {
  return (seedLeads as AnyRecord[]).map((l, i) => ({
    id: i + 1,
    title: String(l.company ?? ""),
    status: String(l.status ?? "New"),
    leadId: String(l.leadId ?? `LD-${2400 + i}`),
    company: String(l.company ?? ""),
    contactPerson: String(l.contactPerson ?? ""),
    industry: String(l.industry ?? ""),
    source: String(l.source ?? "Referral"),
    requiredService: String(l.requiredService ?? "FTL"),
    estimatedMonthlyLoads: Number(l.estimatedMonthlyLoads ?? 50),
    cityCountry: String(l.cityCountry ?? ""),
    assignedRep: String(l.assignedRep ?? ""),
    nextFollowUp: String(l.nextFollowUp ?? ""),
    riskLevel: "Low",
  }));
}

const SEED_SUMMARY: AnyRecord = { total: 4, active: 3, riskItems: 0 };

const leadsApi = {
  list: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/leads")).then((rows) =>
      rows.map((r) => ({
        ...r,
        leadId: r.leadId ?? r.code ?? `LD-${String(r.id)}`,
        company: r.company ?? r.title ?? "",
        contactPerson: r.contactPerson ?? r.contact_person ?? "",
        industry: r.industry ?? "",
        source: r.source ?? "",
        requiredService: r.requiredService ?? r.required_service ?? "",
        estimatedMonthlyLoads: r.estimatedMonthlyLoads ?? r.estimated_monthly_loads ?? 0,
        cityCountry: r.cityCountry ?? r.location_name ?? "",
        assignedRep: r.assignedRep ?? r.assigned_rep ?? "",
        nextFollowUp: r.nextFollowUp ?? r.due_at ?? "",
      }))
    ),
    () => buildSeed()
  ),
  summary: () => withFallback(
    unwrap<AnyRecord>(apiClient.get("/api/leads")).then((r) => (r as unknown as { summary: AnyRecord }).summary ?? SEED_SUMMARY),
    () => SEED_SUMMARY
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
    requiredService: "FTL", estimatedMonthlyLoads: "", cityCountry: "", assignedRep: "",
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
          ].map(({ label, key, placeholder, full }) => (
            <div key={key} className={full ? "col-span-2" : ""}>
              <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
              <input
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
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {showCreate && <CreateLeadModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Leads</h1>
          <p className="text-sm text-slate-500 mt-0.5">New business pipeline — track prospects from first contact to qualified opportunity</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("leads", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>Add Lead</button>
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Leads",          val: summary.total },
          { label: "Active",               val: summary.active, accent: "text-teal-600" },
          { label: "Qualified",            val: summary.qualified, accent: "text-violet-600" },
          { label: "Discovery Scheduled",  val: summary.discovery, accent: "text-blue-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}

        {/* Pipeline lane bar */}
        <div className="panel flex-1 min-w-64 p-4">
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Pipeline Stages</p>
          <div className="flex gap-2 flex-wrap">
            {PIPELINE_STAGES.map((stage) => (
              <button
                key={stage}
                type="button"
                onClick={() => setStageFilter(stageFilter === stage ? "All" : stage)}
                className={`flex flex-col items-center px-3 py-2 rounded-lg border text-xs font-medium transition-colors cursor-pointer ${
                  stageFilter === stage
                    ? "bg-teal-50 border-teal-300 text-teal-700"
                    : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
                }`}
              >
                <span className="text-base font-bold">{String(laneCounts[stage] ?? 0)}</span>
                <span className="whitespace-nowrap">{stage}</span>
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Search / filter bar */}
      <div className="panel flex items-center gap-3">
        <div className="flex gap-1.5">
          {["All", ...PIPELINE_STAGES].map((f) => (
            <button key={f} type="button" onClick={() => setStageFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                stageFilter === f ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}>
              {f}
            </button>
          ))}
        </div>
        <input type="search" placeholder="Search company, rep, industry…" value={search} onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56" />
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No leads match your filters" /> : (
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
