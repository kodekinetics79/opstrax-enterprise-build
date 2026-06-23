import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { campaigns as seedCampaigns } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed ────────────────────────────────────────────────────────────────────

function buildSeed(): AnyRecord[] {
  return (seedCampaigns as AnyRecord[]).map((c, i) => ({
    id: i + 1,
    title: String(c.campaignName ?? ""),
    status: String(c.status ?? "Active"),
    campaignName: String(c.campaignName ?? ""),
    segment: String(c.segment ?? ""),
    channel: String(c.channel ?? "Email"),
    audienceSize: Number(c.audienceSize ?? 0),
    openRate: String(c.openRate ?? "0%"),
    responseRate: String(c.responseRate ?? "0%"),
    leadsGenerated: Number(c.leadsGenerated ?? 0),
    revenueInfluenced: Number(c.revenueInfluenced ?? 0),
    currency: String(c.currency ?? "SAR"),
    startDate: String(c.startDate ?? ""),
  }));
}

const SEED_SUMMARY: AnyRecord = { total: 3, active: 2, riskItems: 0 };

const campaignsApi = {
  list: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/campaigns")).then((rows) =>
      rows.map((r) => ({
        ...r,
        campaignName: r.campaignName ?? r.title ?? "",
        segment: r.segment ?? "",
        channel: r.channel ?? "",
        audienceSize: Number(r.audienceSize ?? r.amount ?? 0),
        openRate: r.openRate ?? "—",
        responseRate: r.responseRate ?? "—",
        leadsGenerated: Number(r.leadsGenerated ?? 0),
        revenueInfluenced: Number(r.revenueInfluenced ?? 0),
        currency: r.currency ?? "SAR",
        startDate: r.startDate ?? r.due_at ?? "",
      }))
    ),
    () => buildSeed()
  ),
  create: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/campaigns", body)),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function ChannelBadge({ channel }: { channel: string }) {
  const cls =
    channel === "WhatsApp" ? "bg-teal-50 border-teal-200 text-teal-700" :
    channel === "Email" ? "bg-blue-50 border-blue-200 text-blue-700" :
    channel === "SMS" ? "bg-amber-50 border-amber-200 text-amber-700" :
    channel === "LinkedIn" ? "bg-violet-50 border-violet-200 text-violet-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{channel}</span>;
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Active" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Scheduled" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "Completed" ? "bg-slate-100 border-slate-200 text-slate-600" :
    status === "Paused" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-100 border-slate-200 text-slate-500";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

// ── Create Campaign Modal ─────────────────────────────────────────────────────

function CreateCampaignModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ title: "", segment: "", channel: "Email", startDate: "" });
  const qc = useQueryClient();
  const mut = useMutation({
    mutationFn: () => campaignsApi.create({ ...form, status: "Scheduled" } as unknown as AnyRecord),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["campaigns"] }); onSaved(); },
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="panel w-full max-w-md p-6 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-base font-bold text-slate-900">New Campaign</h2>
        <div className="grid grid-cols-2 gap-3">
          <div className="col-span-2">
            <label className="block text-xs font-medium text-slate-600 mb-1">Campaign Name*</label>
            <input className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
              placeholder="Cold Chain Summer Readiness" value={form.title} onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))} />
          </div>
          <div className="col-span-2">
            <label className="block text-xs font-medium text-slate-600 mb-1">Target Segment</label>
            <input className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
              placeholder="Pharma + Fresh Food" value={form.segment} onChange={(e) => setForm((f) => ({ ...f, segment: e.target.value }))} />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Channel</label>
            <select title="Channel" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.channel} onChange={(e) => setForm((f) => ({ ...f, channel: e.target.value }))}>
              {["Email", "SMS", "WhatsApp", "LinkedIn", "Multi-channel"].map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Start Date</label>
            <input type="date" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.startDate} onChange={(e) => setForm((f) => ({ ...f, startDate: e.target.value }))} />
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" disabled={!form.title || mut.isPending} className="btn-primary text-sm" onClick={() => mut.mutate()}>
            {mut.isPending ? "Saving…" : "Create Campaign"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function CampaignsPage() {
  const [statusFilter, setStatusFilter] = useState<"All" | "Active" | "Scheduled" | "Completed">("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const listQ = useQuery({ queryKey: ["campaigns", "list"], queryFn: campaignsApi.list, refetchInterval: 60_000 });
  const campaigns = (listQ.data ?? []) as AnyRecord[];

  const totalLeads = campaigns.reduce((s, c) => s + Number(c.leadsGenerated ?? 0), 0);
  const totalRevenue = campaigns.reduce((s, c) => s + Number(c.revenueInfluenced ?? 0), 0);
  const active = campaigns.filter((c) => c.status === "Active").length;

  const filtered = campaigns.filter((c) => {
    if (statusFilter !== "All" && c.status !== statusFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(c.campaignName ?? c.title ?? "").toLowerCase().includes(q) ||
        String(c.segment ?? "").toLowerCase().includes(q) ||
        String(c.channel ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {showCreate && <CreateCampaignModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Campaigns</h1>
          <p className="text-sm text-slate-500 mt-0.5">Outbound marketing campaigns — audience targeting, channel engagement, leads generated and revenue influenced</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("campaigns", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Campaign</button>
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Campaigns",     val: campaigns.length },
          { label: "Active",              val: active, accent: "text-teal-600" },
          { label: "Leads Generated",     val: totalLeads, accent: "text-blue-600" },
          { label: "Revenue Influenced",  val: `SAR ${(totalRevenue / 1_000_000).toFixed(2)}M`, accent: "text-violet-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-36">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Active", "Scheduled", "Completed"] as const).map((f) => (
            <button key={f} type="button" onClick={() => setStatusFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                statusFilter === f ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}>{f}</button>
          ))}
        </div>
        <input type="search" placeholder="Search campaigns, segments…" value={search} onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56" />
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No campaigns match your filters" /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Campaign", "Segment", "Channel", "Status", "Audience", "Open Rate", "Response Rate", "Leads", "Rev. Influenced", "Start"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((c, i) => (
                  <tr key={String(c.id ?? i)} className={`hover:bg-slate-50 cursor-pointer ${selected?.id === c.id ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === c.id ? null : c)}>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900 max-w-44 truncate">{String(c.campaignName ?? c.title ?? "--")}</p>
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-600 max-w-32">{String(c.segment ?? "—")}</td>
                    <td className="px-4 py-3"><ChannelBadge channel={String(c.channel ?? "")} /></td>
                    <td className="px-4 py-3"><StatusBadge status={String(c.status ?? "Active")} /></td>
                    <td className="px-4 py-3 text-slate-700">{Number(c.audienceSize ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3 font-medium text-slate-700">{String(c.openRate ?? "—")}</td>
                    <td className="px-4 py-3 font-medium text-teal-700">{String(c.responseRate ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(c.leadsGenerated ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700 text-xs">
                      {Number(c.revenueInfluenced ?? 0) > 0
                        ? `${String(c.currency ?? "SAR")} ${Number(c.revenueInfluenced).toLocaleString()}`
                        : "—"}
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(c.startDate ?? "—")}</td>
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
              <span className="text-sm font-semibold text-white max-w-56 truncate">{String(selected.campaignName ?? selected.title)}</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6 flex gap-2">
              <StatusBadge status={String(selected.status ?? "Active")} />
              <ChannelBadge channel={String(selected.channel ?? "")} />
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Segment", String(selected.segment ?? "—")],
                ["Audience", Number(selected.audienceSize ?? 0).toLocaleString()],
                ["Open Rate", String(selected.openRate ?? "—")],
                ["Response Rate", String(selected.responseRate ?? "—")],
                ["Leads Generated", String(selected.leadsGenerated ?? "0")],
                ["Revenue Influenced", `${String(selected.currency ?? "SAR")} ${Number(selected.revenueInfluenced ?? 0).toLocaleString()}`],
                ["Start Date", String(selected.startDate ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Performance Insight</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {String(selected.status) === "Active"
                  ? `Campaign is live. Response rate of ${String(selected.responseRate)} indicates ${parseFloat(String(selected.responseRate ?? "0")) > 10 ? "strong" : "moderate"} engagement.`
                  : String(selected.status) === "Scheduled"
                  ? "Campaign is queued. Verify audience list and creative assets before launch."
                  : "Campaign completed. Analyze lead quality and revenue attribution to inform next campaign."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
